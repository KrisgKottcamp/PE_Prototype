using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class KnockbackReceiver2D : MonoBehaviour
{
    [Header("Kinematic Knockback")]
    [Tooltip("For Kinematic bodies, treat force as units/second push speed.")]
    [SerializeField] private float kinematicSpeedMultiplier = 1f;

    [Tooltip("Layer mask for obstacles the kinematic knockback should stop at. " +
             "Set this to the same Obstacles layer your other movers use.")]
    [SerializeField] private LayerMask obstaclesMask;

    [Tooltip("Skin width used when casting against obstacles. Keeps the enemy from " +
             "sinking into walls before stopping.")]
    [SerializeField] private float skinWidth = 0.05f;

    [Header("Dynamic Knockback")]
    [SerializeField] private float dragDuringKnockback = 8f;

    private Rigidbody2D rb;
    private Coroutine routine;

    private readonly RaycastHit2D[] castResults = new RaycastHit2D[4];
    private ContactFilter2D castFilter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

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

        if (routine != null) StopCoroutine(routine);

        Vector2 dir = direction.normalized;

        if (rb.bodyType == RigidbodyType2D.Dynamic)
            routine = StartCoroutine(DynamicKnock(dir, force, duration));
        else if (rb.bodyType == RigidbodyType2D.Kinematic)
            routine = StartCoroutine(KinematicKnock(dir, force, duration));
        else
            Debug.LogWarning($"KnockbackReceiver2D: '{name}' Rigidbody2D is Static, cannot knock back.");
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
        float t = 0f;
        float speed = forceAsSpeed * kinematicSpeedMultiplier;

        while (t < duration)
        {
            t += Time.fixedDeltaTime;

            float wantedDistance = speed * Time.fixedDeltaTime;

            // Cast ahead along the knockback direction.
            // If an obstacle is closer than wantedDistance + skinWidth, stop there instead.
            float allowedDistance = wantedDistance;

            if (obstaclesMask.value != 0)
            {
                int hitCount = rb.Cast(dir, castFilter, castResults, wantedDistance + skinWidth);
                for (int i = 0; i < hitCount; i++)
                {
                    float d = castResults[i].distance - skinWidth;
                    if (d < allowedDistance) allowedDistance = d;
                }
                if (allowedDistance < 0f) allowedDistance = 0f;
            }

            // If completely blocked, stop the knockback early
            if (allowedDistance <= 0f)
            {
                routine = null;
                yield break;
            }

            rb.MovePosition(rb.position + dir * allowedDistance);

            yield return new WaitForFixedUpdate();
        }

        routine = null;
    }
}