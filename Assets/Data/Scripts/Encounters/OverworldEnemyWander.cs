using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class OverworldEnemyWander : MonoBehaviour
{
    [Header("Wander")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.25f;
    [SerializeField, Min(0f)] private float wanderRadius = 1.75f;
    [SerializeField, Min(0f)] private float minimumPause = 0.35f;
    [SerializeField, Min(0f)] private float maximumPause = 1.1f;
    [SerializeField, Min(0.001f)]
    private float destinationTolerance = 0.08f;

    [Header("Chase")]
    [Tooltip(
        "Movement speed after OverworldEnemyAwareness detects the player."
    )]
    [SerializeField, Min(0f)] private float chaseSpeed = 3.25f;

    [SerializeField, Min(0f)]
    private float chaseStopDistance = 0.03f;

    [Header("Collision")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField, Min(0f)] private float skinWidth = 0.03f;

    [Header("Visual Facing")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip(
        "Enable this because the current dog source sprite is drawn " +
        "facing right. The script flips SpriteRenderer.flipX only when " +
        "the enemy changes horizontal direction."
    )]
    [SerializeField] private bool spriteFacesRightByDefault = true;

    [Tooltip(
        "Horizontal movement smaller than this keeps the previous left/right " +
        "facing. This prevents jitter when the enemy moves almost vertically."
    )]
    [SerializeField, Min(0f)]
    private float horizontalFacingDeadZone = 0.01f;

    private static readonly float[] AvoidanceAngles =
    {
        0f,
        30f,
        -30f,
        60f,
        -60f,
        90f,
        -90f
    };

    private readonly RaycastHit2D[] castResults =
        new RaycastHit2D[8];

    private Rigidbody2D rb;
    private ContactFilter2D obstacleFilter;

    private Vector2 homePosition;
    private Vector2 destination;
    private float pauseTimer;
    private bool hasDestination;
    private bool movementLocked;

    private Transform chaseTarget;
    private Vector2 facingDirection = Vector2.right;

    public Vector2 HomePosition => homePosition;
    public bool MovementLocked => movementLocked;
    public bool IsChasing => chaseTarget != null;
    public Transform ChaseTarget => chaseTarget;
    public Vector2 FacingDirection => facingDirection;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        obstacleFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstacleMask,
            useTriggers = false
        };

        if (spriteRenderer == null)
        {
            spriteRenderer =
                GetComponentInChildren<SpriteRenderer>(true);
        }

        facingDirection = spriteFacesRightByDefault
            ? Vector2.right
            : Vector2.left;

        ApplySpriteFlip();
    }

    private void OnEnable()
    {
        homePosition = rb != null
            ? rb.position
            : (Vector2)transform.position;

        chaseTarget = null;
        hasDestination = false;
        pauseTimer = RandomPauseDuration();
    }

    private void FixedUpdate()
    {
        if (rb == null || movementLocked)
        {
            StopPhysicsMotion();
            return;
        }

        if (chaseTarget != null)
        {
            UpdateChaseMovement();
            return;
        }

        UpdateWanderMovement();
    }

    public void SetMovementLocked(bool locked)
    {
        movementLocked = locked;

        if (locked)
            StopPhysicsMotion();
    }

    public void SetChaseTarget(Transform target)
    {
        if (target == null)
            return;

        chaseTarget = target;
        hasDestination = false;
        pauseTimer = 0f;
    }

    public void ClearChase()
    {
        chaseTarget = null;
        hasDestination = false;
        pauseTimer = RandomPauseDuration();
        StopPhysicsMotion();
    }

    public void ResetHomePosition()
    {
        homePosition = rb != null
            ? rb.position
            : (Vector2)transform.position;

        ClearChase();
    }

    private void UpdateWanderMovement()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            StopPhysicsMotion();
            return;
        }

        if (!hasDestination)
            ChooseDestination();

        Vector2 currentPosition = rb.position;
        Vector2 toDestination = destination - currentPosition;

        if (toDestination.sqrMagnitude <=
            destinationTolerance * destinationTolerance)
        {
            BeginPause();
            return;
        }

        float desiredDistance = Mathf.Min(
            moveSpeed * Time.fixedDeltaTime,
            toDestination.magnitude
        );

        bool moved = TryMove(
            toDestination.normalized,
            desiredDistance,
            true
        );

        if (!moved)
            BeginPause(0.1f, 0.3f);
    }

    private void UpdateChaseMovement()
    {
        if (chaseTarget == null ||
            !chaseTarget.gameObject.activeInHierarchy)
        {
            ClearChase();
            return;
        }

        Vector2 currentPosition = rb.position;
        Vector2 toTarget =
            (Vector2)chaseTarget.position - currentPosition;

        if (toTarget.sqrMagnitude <=
            chaseStopDistance * chaseStopDistance)
        {
            StopPhysicsMotion();
            return;
        }

        float desiredDistance = Mathf.Min(
            chaseSpeed * Time.fixedDeltaTime,
            toTarget.magnitude
        );

        TryMove(
            toTarget.normalized,
            desiredDistance,
            false
        );
    }

    private bool TryMove(
        Vector2 desiredDirection,
        float desiredDistance,
        bool clampToHomeRadius)
    {
        if (desiredDirection.sqrMagnitude < 0.0001f ||
            desiredDistance <= 0f)
        {
            StopPhysicsMotion();
            return false;
        }

        desiredDirection.Normalize();

        Vector2 selectedDirection = Vector2.zero;
        float selectedDistance = 0f;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < AvoidanceAngles.Length; i++)
        {
            Vector2 candidateDirection = Rotate(
                desiredDirection,
                AvoidanceAngles[i]
            );

            float allowedDistance = GetAllowedDistance(
                candidateDirection,
                desiredDistance
            );

            if (allowedDistance <= 0.001f)
                continue;

            float alignment = Vector2.Dot(
                desiredDirection,
                candidateDirection
            );

            float score = alignment +
                allowedDistance / Mathf.Max(0.001f, desiredDistance);

            if (score <= bestScore)
                continue;

            bestScore = score;
            selectedDirection = candidateDirection;
            selectedDistance = allowedDistance;
        }

        if (selectedDistance <= 0.001f)
        {
            StopPhysicsMotion();
            return false;
        }

        Vector2 nextPosition =
            rb.position + selectedDirection * selectedDistance;

        if (clampToHomeRadius && wanderRadius > 0f)
        {
            Vector2 fromHome = nextPosition - homePosition;

            if (fromHome.sqrMagnitude >
                wanderRadius * wanderRadius)
            {
                nextPosition = homePosition +
                    fromHome.normalized * wanderRadius;
            }
        }

        rb.MovePosition(nextPosition);
        UpdateVisualFacing(selectedDirection);
        return true;
    }

    private float GetAllowedDistance(
        Vector2 direction,
        float desiredDistance)
    {
        int hitCount = rb.Cast(
            direction,
            obstacleFilter,
            castResults,
            desiredDistance + skinWidth
        );

        float allowedDistance = desiredDistance;

        for (int i = 0; i < hitCount; i++)
        {
            float candidateDistance =
                castResults[i].distance - skinWidth;

            if (candidateDistance < allowedDistance)
                allowedDistance = candidateDistance;
        }

        return Mathf.Max(0f, allowedDistance);
    }

    private void ChooseDestination()
    {
        destination = homePosition +
            Random.insideUnitCircle * wanderRadius;

        hasDestination = true;
    }

    private void BeginPause()
    {
        BeginPause(minimumPause, maximumPause);
    }

    private void BeginPause(float minimum, float maximum)
    {
        hasDestination = false;
        pauseTimer = Random.Range(
            Mathf.Min(minimum, maximum),
            Mathf.Max(minimum, maximum)
        );

        StopPhysicsMotion();
    }

    private float RandomPauseDuration()
    {
        return Random.Range(
            Mathf.Min(minimumPause, maximumPause),
            Mathf.Max(minimumPause, maximumPause)
        );
    }

    private void StopPhysicsMotion()
    {
        if (rb == null)
            return;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
    }

    private void UpdateVisualFacing(Vector2 direction)
    {
        if (spriteRenderer == null ||
            Mathf.Abs(direction.x) <= horizontalFacingDeadZone)
        {
            return;
        }

        facingDirection = direction.x > 0f
            ? Vector2.right
            : Vector2.left;

        ApplySpriteFlip();
    }

    private void ApplySpriteFlip()
    {
        if (spriteRenderer == null)
            return;

        bool facingLeft = facingDirection.x < 0f;

        spriteRenderer.flipX = spriteFacesRightByDefault
            ? facingLeft
            : !facingLeft;
    }

    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(radians);
        float sine = Mathf.Sin(radians);

        return new Vector2(
            direction.x * cosine - direction.y * sine,
            direction.x * sine + direction.y * cosine
        ).normalized;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maximumPause < minimumPause)
            maximumPause = minimumPause;

        horizontalFacingDeadZone = Mathf.Max(
            0f,
            horizontalFacingDeadZone
        );
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying
            ? (Vector3)homePosition
            : transform.position;

        Gizmos.DrawWireSphere(center, wanderRadius);
    }
#endif
}
