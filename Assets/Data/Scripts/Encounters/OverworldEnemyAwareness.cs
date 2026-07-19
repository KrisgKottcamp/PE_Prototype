using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class OverworldEnemyAwareness : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private OverworldEnemyWander wander;
    [SerializeField] private OverworldEncounter encounter;

    [Tooltip(
        "Optional point used as the center of the cone. When empty, the " +
        "root object's position plus Vision Forward Offset is used."
    )]
    [SerializeField] private Transform visionOrigin;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";

    [SerializeField, Min(0.1f)]
    private float visionDistance = 3.5f;

    [Tooltip("Full width of the cone in degrees.")]
    [SerializeField, Range(1f, 179f)]
    private float visionAngle = 70f;

    [SerializeField, Min(0f)]
    private float visionForwardOffset = 0.25f;

    [Tooltip(
        "Assign only walls and solid scenery that should block sight. " +
        "Do not include the Player or overworld-enemy layer."
    )]
    [SerializeField] private LayerMask visionBlockingMask;

    [SerializeField]
    private bool requireClearLineOfSight = true;

    [Header("Rear Advantage")]
    [Tooltip(
        "Full width of the rear zone. The player must touch this zone " +
        "without having entered the vision cone to earn advantage."
    )]
    [SerializeField, Range(1f, 179f)]
    private float rearAdvantageAngle = 120f;

    [Header("Vision Cone Display")]
    [SerializeField] private bool showVisionConeInGame = true;
    [SerializeField] private bool hideConeAfterDetection = true;

    [SerializeField, Range(3, 64)]
    private int coneArcSegments = 20;

    [SerializeField, Min(0.001f)]
    private float coneLineWidth = 0.035f;

    [SerializeField]
    private Color coneColor = new(1f, 0.85f, 0.15f, 0.65f);

    [Tooltip(
        "Optional. When empty, the script creates a temporary material " +
        "from Sprites/Default or a URP unlit shader."
    )]
    [SerializeField] private Material coneMaterial;

    [SerializeField] private int coneSortingOrderOffset = -1;

    [Header("Optional Alert Animation")]
    [SerializeField] private Animator enemyAnimator;
    [SerializeField] private string detectedTriggerName = "Detected";

    [Header("Debug")]
    [SerializeField] private bool logDetection = true;

    private LineRenderer coneRenderer;
    private Material runtimeConeMaterial;
    private Transform playerRoot;

    public bool HasDetectedPlayer { get; private set; }
    public bool IsChasing => wander != null && wander.IsChasing;

    private void Awake()
    {
        if (wander == null)
            wander = GetComponent<OverworldEnemyWander>();

        if (encounter == null)
            encounter = GetComponent<OverworldEncounter>();

        if (enemyAnimator == null)
            enemyAnimator = GetComponentInChildren<Animator>(true);

        coneRenderer = GetComponent<LineRenderer>();
        ConfigureConeRenderer();
    }

    private void Start()
    {
        ResolvePlayerRoot();
        UpdateConeDisplay();
    }

    private void Update()
    {
        UpdateConeDisplay();

        if (encounter != null && encounter.IsLocallyTriggered)
            return;

        if (playerRoot == null ||
            !playerRoot.gameObject.activeInHierarchy)
        {
            ResolvePlayerRoot();
        }

        if (playerRoot == null || PlayerHasEncounterGrace(playerRoot))
            return;

        if (HasDetectedPlayer)
        {
            wander?.SetChaseTarget(playerRoot);
            return;
        }

        EvaluatePlayerNow(playerRoot);
    }

    private void OnDestroy()
    {
        if (runtimeConeMaterial != null)
            Destroy(runtimeConeMaterial);
    }

    public bool EvaluatePlayerNow(Transform candidatePlayer)
    {
        if (HasDetectedPlayer)
            return true;

        if (candidatePlayer == null ||
            PlayerHasEncounterGrace(candidatePlayer))
        {
            return false;
        }

        if (!IsInsideVisionCone(candidatePlayer))
            return false;

        MarkPlayerDetected(candidatePlayer);
        return true;
    }

    public bool CanGrantRearAdvantage(Transform candidatePlayer)
    {
        if (candidatePlayer == null || HasDetectedPlayer)
            return false;

        // This protects against contact and vision detection occurring in
        // different Unity callback orders during the same frame.
        if (EvaluatePlayerNow(candidatePlayer))
            return false;

        return IsInsideRearAdvantageArc(candidatePlayer);
    }

    public void ResetAwareness()
    {
        HasDetectedPlayer = false;
        playerRoot = null;
        wander?.ClearChase();
        ResolvePlayerRoot();
        UpdateConeDisplay();
    }

    private void MarkPlayerDetected(Transform detectedPlayer)
    {
        HasDetectedPlayer = true;
        playerRoot = detectedPlayer;
        wander?.SetChaseTarget(detectedPlayer);

        if (enemyAnimator != null &&
            !string.IsNullOrWhiteSpace(detectedTriggerName))
        {
            enemyAnimator.SetTrigger(detectedTriggerName);
        }

        if (logDetection)
        {
            Debug.Log(
                $"OverworldEnemyAwareness: '{gameObject.name}' detected " +
                "the player. Rear-contact advantage is disabled for this " +
                "encounter attempt.",
                this
            );
        }
    }

    private bool IsInsideVisionCone(Transform candidatePlayer)
    {
        Vector2 origin = GetVisionOrigin();
        Vector2 toPlayer =
            (Vector2)candidatePlayer.position - origin;

        float distance = toPlayer.magnitude;

        if (distance <= 0.0001f || distance > visionDistance)
            return false;

        Vector2 directionToPlayer = toPlayer / distance;
        Vector2 forward = GetFacingDirection();

        float minimumDot = Mathf.Cos(
            visionAngle * 0.5f * Mathf.Deg2Rad
        );

        if (Vector2.Dot(forward, directionToPlayer) < minimumDot)
            return false;

        if (!requireClearLineOfSight || visionBlockingMask.value == 0)
            return true;

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            directionToPlayer,
            distance,
            visionBlockingMask
        );

        return hit.collider == null;
    }

    private bool IsInsideRearAdvantageArc(Transform candidatePlayer)
    {
        Vector2 toPlayer =
            (Vector2)candidatePlayer.position -
            (Vector2)transform.position;

        if (toPlayer.sqrMagnitude < 0.0001f)
            return false;

        Vector2 directionToPlayer = toPlayer.normalized;
        Vector2 rearDirection = -GetFacingDirection();

        float minimumDot = Mathf.Cos(
            rearAdvantageAngle * 0.5f * Mathf.Deg2Rad
        );

        return Vector2.Dot(rearDirection, directionToPlayer) >= minimumDot;
    }

    private void ResolvePlayerRoot()
    {
        if (PlayerSingleton.Instance != null)
        {
            playerRoot = PlayerSingleton.Instance.transform;
            return;
        }

        if (string.IsNullOrWhiteSpace(playerTag))
            return;

        GameObject playerObject =
            GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
            playerRoot = playerObject.transform;
    }

    private Vector2 GetFacingDirection()
    {
        if (wander != null)
            return wander.FacingDirection;

        return Vector2.right;
    }

    private Vector2 GetVisionOrigin()
    {
        if (visionOrigin != null)
            return visionOrigin.position;

        return (Vector2)transform.position +
            GetFacingDirection() * visionForwardOffset;
    }

    private void ConfigureConeRenderer()
    {
        if (coneRenderer == null)
            return;

        coneRenderer.useWorldSpace = true;
        coneRenderer.loop = false;
        coneRenderer.widthMultiplier = coneLineWidth;
        coneRenderer.startColor = coneColor;
        coneRenderer.endColor = coneColor;
        coneRenderer.numCapVertices = 2;
        coneRenderer.numCornerVertices = 2;

        if (coneMaterial != null)
        {
            coneRenderer.material = coneMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
            {
                runtimeConeMaterial = new Material(shader)
                {
                    name = "Runtime Overworld Vision Cone Material",
                    hideFlags = HideFlags.DontSave
                };

                coneRenderer.material = runtimeConeMaterial;
            }
        }

        SpriteRenderer sprite =
            GetComponentInChildren<SpriteRenderer>(true);

        if (sprite != null)
        {
            coneRenderer.sortingLayerID = sprite.sortingLayerID;
            coneRenderer.sortingOrder =
                sprite.sortingOrder + coneSortingOrderOffset;
        }
    }

    private void UpdateConeDisplay()
    {
        if (coneRenderer == null)
            return;

        bool shouldShow = showVisionConeInGame &&
            !(hideConeAfterDetection && HasDetectedPlayer) &&
            !(encounter != null && encounter.IsLocallyTriggered);

        coneRenderer.enabled = shouldShow;

        if (!shouldShow)
            return;

        coneRenderer.widthMultiplier = coneLineWidth;
        coneRenderer.startColor = coneColor;
        coneRenderer.endColor = coneColor;

        int segments = Mathf.Max(3, coneArcSegments);
        int positionCount = segments + 3;
        coneRenderer.positionCount = positionCount;

        Vector2 origin = GetVisionOrigin();
        Vector2 forward = GetFacingDirection();
        float halfAngle = visionAngle * 0.5f;

        coneRenderer.SetPosition(0, origin);

        for (int i = 0; i <= segments; i++)
        {
            float interpolation = i / (float)segments;
            float angle = Mathf.Lerp(
                -halfAngle,
                halfAngle,
                interpolation
            );

            Vector2 rayDirection = Rotate(forward, angle);
            Vector2 point = origin + rayDirection * visionDistance;
            coneRenderer.SetPosition(i + 1, point);
        }

        coneRenderer.SetPosition(positionCount - 1, origin);
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

    private static bool PlayerHasEncounterGrace(Transform player)
    {
        OverworldEncounterGracePeriod gracePeriod =
            player.GetComponent<OverworldEncounterGracePeriod>();

        if (gracePeriod == null)
        {
            gracePeriod =
                player.GetComponentInChildren<
                    OverworldEncounterGracePeriod
                >(true);
        }

        return gracePeriod != null && gracePeriod.IsActive;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        visionDistance = Mathf.Max(0.1f, visionDistance);
        coneArcSegments = Mathf.Clamp(coneArcSegments, 3, 64);
        coneLineWidth = Mathf.Max(0.001f, coneLineWidth);
    }

    private void OnDrawGizmosSelected()
    {
        OverworldEnemyWander currentWander = wander != null
            ? wander
            : GetComponent<OverworldEnemyWander>();

        Vector2 forward = currentWander != null
            ? currentWander.FacingDirection
            : Vector2.right;

        Vector2 origin = visionOrigin != null
            ? (Vector2)visionOrigin.position
            : (Vector2)transform.position +
              forward * visionForwardOffset;

        float halfVisionAngle = visionAngle * 0.5f;
        Gizmos.DrawLine(
            origin,
            origin + Rotate(forward, -halfVisionAngle) * visionDistance
        );
        Gizmos.DrawLine(
            origin,
            origin + Rotate(forward, halfVisionAngle) * visionDistance
        );

        float halfRearAngle = rearAdvantageAngle * 0.5f;
        Vector2 rear = -forward;
        Gizmos.DrawLine(
            transform.position,
            (Vector2)transform.position +
            Rotate(rear, -halfRearAngle)
        );
        Gizmos.DrawLine(
            transform.position,
            (Vector2)transform.position +
            Rotate(rear, halfRearAngle)
        );
    }
#endif
}
