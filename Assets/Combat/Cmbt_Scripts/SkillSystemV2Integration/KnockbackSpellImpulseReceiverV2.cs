using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Connects generic Skill System V2 impulse effects to Project Eri's existing
/// wall-aware knockback implementation. This supports Dynamic and Kinematic
/// enemy bodies and cooperates with both enemy locomotion backends.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(KnockbackReceiver2D))]
public sealed class KnockbackSpellImpulseReceiverV2 :
    MonoBehaviour,
    ISpellImpulseReceiver
{
    [SerializeField] private KnockbackReceiver2D knockbackReceiver;

    [SerializeField, Min(0.02f)]
    private float fallbackDuration = 0.3f;

    [Header("Runtime Debug")]
    [SerializeField] private int debugReceivedImpulseCount;
    [SerializeField] private Vector2 debugLastDirection;
    [SerializeField] private float debugLastMagnitude;
    [SerializeField] private float debugLastDuration;

    private void Awake()
    {
        ResolveReceiver();
    }

    public bool TryReceiveImpulse(in SpellImpulseRequest request)
    {
        ResolveReceiver();
        if (knockbackReceiver == null ||
            request.Direction.sqrMagnitude <= 0.000001f ||
            request.Magnitude <= 0f)
        {
            return false;
        }

        Rigidbody2D body = knockbackReceiver.GetComponent<Rigidbody2D>();
        if (body == null || body.bodyType == RigidbodyType2D.Static)
            return false;

        float duration = request.Duration > 0.001f
            ? request.Duration
            : Mathf.Max(0.02f, fallbackDuration);

        debugReceivedImpulseCount++;
        debugLastDirection = request.Direction;
        debugLastMagnitude = request.Magnitude;
        debugLastDuration = duration;

        knockbackReceiver.ApplyKnockback(
            request.Direction,
            request.Magnitude,
            duration);
        return true;
    }

    private void ResolveReceiver()
    {
        if (knockbackReceiver == null)
            knockbackReceiver = GetComponent<KnockbackReceiver2D>();
    }

    private void OnValidate()
    {
        fallbackDuration = Mathf.Max(0.02f, fallbackDuration);
    }
}
