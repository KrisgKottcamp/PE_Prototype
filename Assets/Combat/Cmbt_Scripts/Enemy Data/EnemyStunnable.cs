using System.Collections;
using UnityEngine;

/// <summary>
/// EnemyStunnable v2
///
/// Phase 1 combat-aggression change:
/// - Stun no longer has to disable EnemyBrain / EnemyShooterDebug.
/// - The default Stun(float) path is now a lightweight hit reaction flag.
/// - Hard stuns are still available through StunWithScriptDisable or by enabling
///   disableConfiguredScriptsDuringStun in the Inspector.
///
/// This prevents player basic attacks from repeatedly restarting enemy AI state.
/// </summary>
public class EnemyStunnable : MonoBehaviour
{
    [Header("Stun")]
    [SerializeField] private float defaultStunSeconds = 0.25f;

    [Tooltip(
        "If true, regular Stun(float) disables the configured scripts. " +
        "Leave OFF for the harder Phase 1 combat feel so basic attacks do not reset enemy AI."
    )]
    [SerializeField] private bool disableConfiguredScriptsDuringStun = false;

    [Tooltip(
        "If true, regular Stun(float) zeros the Rigidbody velocity at stun start. " +
        "Leave OFF if basic attacks should not interrupt enemy movement."
    )]
    [SerializeField] private bool freezeRigidbodyDuringStun = false;

    [Header("Disable during hard stun")]
    [SerializeField] private MonoBehaviour[] disableScripts;

    [Header("Optional")]
    [SerializeField] private Rigidbody2D rb2d;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private string debugLastStunType = "None";
    [SerializeField] private float debugStunRemaining;

    private Coroutine stunRoutine;
    private float stunEndTime;
    private bool activeDisableScripts;
    private bool activeFreezeRigidbody;

    public bool IsStunned { get; private set; }
    public float StunRemaining => Mathf.Max(0f, stunEndTime - Time.time);
    public bool IsHardStunned => IsStunned && activeDisableScripts;

    private void Awake()
    {
        if (rb2d == null)
            rb2d = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        debugStunRemaining = StunRemaining;
    }

    /// <summary>
    /// Lightweight stun path used by existing basic attacks.
    /// By default this no longer disables AI scripts or freezes movement.
    /// </summary>
    public void Stun(float seconds)
    {
        StartStun(
            seconds,
            disableConfiguredScriptsDuringStun,
            freezeRigidbodyDuringStun,
            disableConfiguredScriptsDuringStun ? "ConfiguredHardStun" : "LightHitReaction"
        );
    }

    /// <summary>
    /// Explicit hard stun path for future strong interrupts, poise breaks, and CC skills.
    /// </summary>
    public void StunWithScriptDisable(float seconds)
    {
        StartStun(seconds, true, true, "ExplicitHardStun");
    }

    /// <summary>
    /// Explicit movement-only reaction. Useful for future heavy hit reactions without AI reset.
    /// </summary>
    public void StunMovementOnly(float seconds)
    {
        StartStun(seconds, false, true, "MovementOnlyReaction");
    }

    private void StartStun(
        float seconds,
        bool shouldDisableScripts,
        bool shouldFreezeRigidbody,
        string stunType)
    {
        if (seconds <= 0f)
            seconds = defaultStunSeconds;

        float newEnd = Time.time + seconds;
        if (newEnd > stunEndTime)
            stunEndTime = newEnd;

        // Escalate an already-running reaction if a stronger one arrives.
        activeDisableScripts |= shouldDisableScripts;
        activeFreezeRigidbody |= shouldFreezeRigidbody;
        debugLastStunType = stunType;

        if (debugLogs)
        {
            Debug.Log(
                $"EnemyStunnable: {name} {stunType} for {seconds:0.00}s " +
                $"DisableScripts={activeDisableScripts} FreezeRB={activeFreezeRigidbody}",
                this
            );
        }

        if (stunRoutine == null)
            stunRoutine = StartCoroutine(StunRoutine());
        else if (activeDisableScripts)
            ApplyScriptDisableState(false);
    }

    private IEnumerator StunRoutine()
    {
        IsStunned = true;

        if (activeFreezeRigidbody)
            StopRigidbodyMotion();

        if (activeDisableScripts)
            ApplyScriptDisableState(false);

        while (Time.time < stunEndTime)
        {
            if (activeFreezeRigidbody)
                StopRigidbodyMotion();

            if (activeDisableScripts)
                ApplyScriptDisableState(false);

            yield return null;
        }

        if (activeDisableScripts)
            ApplyScriptDisableState(true);

        IsStunned = false;
        activeDisableScripts = false;
        activeFreezeRigidbody = false;
        stunRoutine = null;
        debugStunRemaining = 0f;
    }

    private void StopRigidbodyMotion()
    {
        if (rb2d == null)
            return;

#if UNITY_6000_0_OR_NEWER
        rb2d.linearVelocity = Vector2.zero;
#else
        rb2d.velocity = Vector2.zero;
#endif
        rb2d.angularVelocity = 0f;
    }

    private void ApplyScriptDisableState(bool enabled)
    {
        if (disableScripts == null)
            return;

        for (int i = 0; i < disableScripts.Length; i++)
        {
            MonoBehaviour script = disableScripts[i];

            if (script == null || script == this)
                continue;

            script.enabled = enabled;
        }
    }

    private void OnDisable()
    {
        if (activeDisableScripts)
            ApplyScriptDisableState(true);

        IsStunned = false;
        activeDisableScripts = false;
        activeFreezeRigidbody = false;
        stunRoutine = null;
        debugStunRemaining = 0f;
    }
}
