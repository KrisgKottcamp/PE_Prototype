using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class KnockbackReceiver2D : MonoBehaviour
{
    [Header("Kinematic Knockback")]
    [Tooltip("For Kinematic bodies, treat force as units/second push speed.")]
    [SerializeField] private float kinematicSpeedMultiplier = 1f;

    [Header("Dynamic Knockback")]
    [SerializeField] private float dragDuringKnockback = 8f;

    private Rigidbody2D rb;
    private Coroutine routine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
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

        // Optional: cancel any existing kinematic velocity feel by not relying on rb.velocity
        while (t < duration)
        {
            t += Time.fixedDeltaTime;

            Vector2 next = rb.position + dir * speed * Time.fixedDeltaTime;
            rb.MovePosition(next);

            yield return new WaitForFixedUpdate();
        }

        routine = null;
    }
}
