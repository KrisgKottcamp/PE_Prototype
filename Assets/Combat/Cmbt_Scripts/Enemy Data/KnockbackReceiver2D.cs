using System.Collections;
using ProjectEri.SkillSystemV2;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class KnockbackReceiver2D : MonoBehaviour, ISpellMotionBlocker
{
    [Header("Kinematic Knockback")]
    [Tooltip("For Kinematic bodies, treat force as units/second push speed.")]
    [SerializeField] private float kinematicSpeedMultiplier = 1f;

    [Tooltip("Layer mask for obstacles the kinematic knockback should stop at. Set this to walls/cover/obstacles only.")]
    [SerializeField] private LayerMask obstaclesMask;

    [Tooltip("Skin width used when casting against obstacles. Keeps the enemy from sinking into walls before stopping.")]
    [SerializeField] private float skinWidth = 0.03f;

    [Tooltip("Tiny movement allowance so wall-adjacent enemies do not get falsely locked by side contacts.")]
    [SerializeField] private float minimumMoveStep = 0.002f;

    [Tooltip("How directly an obstacle normal must oppose knockback to count as blocking. Higher ignores more side contacts.")]
    [Range(0f, 1f)]
    [SerializeField] private float blockingNormalDotThreshold = 0.35f;

    [Header("Dynamic Knockback")]
    [SerializeField] private float dragDuringKnockback = 8f;

    [Header("Debug")]
    [SerializeField] private bool logBlockedHits = false;

    private Rigidbody2D rb;
    private Coroutine routine;

    /// <summary>
    /// Movement controllers use this to yield while this component owns the
    /// enemy's displacement. Without that handshake, their next MovePosition
    /// or velocity write can erase the knockback immediately.
    /// </summary>
    public bool IsKnockbackActive => routine != null;
    public bool BlocksSpellMotion => IsKnockbackActive;

    private readonly RaycastHit2D[] castResults = new RaycastHit2D[8];
    private ContactFilter2D castFilter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        RebuildCastFilter();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            RebuildCastFilter();
    }

    private void RebuildCastFilter()
    {
        castFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstaclesMask,
            useTriggers = false
        };
    }

    public void ApplyKnockback(Vector2 direction, float force, float duration)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        if (force <= 0f || duration <= 0f) return;

        if (routine != null)
            StopCoroutine(routine);

        Vector2 dir = direction.normalized;

        if (rb.bodyType == RigidbodyType2D.Dynamic)
        {
            routine = StartCoroutine(DynamicKnock(dir, force, duration));
        }
        else if (rb.bodyType == RigidbodyType2D.Kinematic)
        {
            routine = StartCoroutine(KinematicKnock(dir, force, duration));
        }
        else
        {
            Debug.LogWarning($"KnockbackReceiver2D: '{name}' Rigidbody2D is Static, cannot knock back.");
        }
    }

    private IEnumerator DynamicKnock(Vector2 dir, float force, float duration)
    {
        float oldDrag = rb.linearDamping;
        rb.linearDamping = dragDuringKnockback;

        rb.linearVelocity = Vector2.zero;
        rb.AddForce(dir * force, ForceMode2D.Impulse);

        yield return new WaitForSeconds(duration);

        rb.linearDamping = oldDrag;
        routine = null;
    }

    private IEnumerator KinematicKnock(Vector2 dir, float forceAsSpeed, float duration)
    {
        float elapsed = 0f;
        float speed = forceAsSpeed * kinematicSpeedMultiplier;

        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;

            float wantedDistance = speed * Time.fixedDeltaTime;
            float allowedDistance = GetAllowedDistance(dir, wantedDistance);

            // If directly blocked by a wall, stop. But do not let tiny side contacts
            // completely kill wall-adjacent knockback.
            if (allowedDistance <= minimumMoveStep)
            {
                routine = null;
                yield break;
            }

            rb.MovePosition(rb.position + dir * allowedDistance);

            yield return new WaitForFixedUpdate();
        }

        routine = null;
    }

    private float GetAllowedDistance(Vector2 dir, float wantedDistance)
    {
        if (wantedDistance <= 0f)
            return 0f;

        if (obstaclesMask.value == 0)
            return wantedDistance;

        float castDistance = wantedDistance + skinWidth;
        float allowedDistance = wantedDistance;

        int hitCount = rb.Cast(dir, castFilter, castResults, castDistance);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = castResults[i];

            if (hit.collider == null)
                continue;

            // Important fix:
            // Only count a hit as blocking if the wall normal actually opposes movement.
            // Side contacts near walls can show up at distance 0 and should not cancel knockback.
            float opposingDot = Vector2.Dot(hit.normal, -dir);

            if (opposingDot < blockingNormalDotThreshold)
            {
                if (logBlockedHits)
                {
                    Debug.Log(
                        $"KnockbackReceiver2D: Ignoring side contact on {hit.collider.name}. " +
                        $"dot={opposingDot}, normal={hit.normal}, dir={dir}"
                    );
                }

                continue;
            }

            float distanceBeforeWall = hit.distance - skinWidth;

            if (distanceBeforeWall < allowedDistance)
                allowedDistance = distanceBeforeWall;

            if (logBlockedHits)
            {
                Debug.Log(
                    $"KnockbackReceiver2D: Blocking hit on {hit.collider.name}. " +
                    $"allowed={allowedDistance}, hitDistance={hit.distance}, dot={opposingDot}"
                );
            }
        }

        if (allowedDistance < 0f)
            allowedDistance = 0f;

        return allowedDistance;
    }
}
