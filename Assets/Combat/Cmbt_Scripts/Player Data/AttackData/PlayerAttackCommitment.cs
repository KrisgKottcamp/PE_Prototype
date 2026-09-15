using UnityEngine;

/// <summary>
/// PlayerAttackCommitment
///
/// Small combat-feel helper used by basic attacks and CombatPawnMover.
/// It lets attacks briefly reduce player movement speed and lets damage briefly
/// lock out new attacks without fully freezing the player.
///
/// This is deliberately lightweight:
/// - It does not read input.
/// - It reads only the active character's basic-attack lock preference.
/// - It exposes movement commitment, a hard movement lock, and action lockout.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAttackCommitment : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private float debugMovementMultiplier = 1f;
    [SerializeField] private float debugMovementCommitmentRemaining;
    [SerializeField] private bool debugBasicAttackMovementLocked;
    [SerializeField] private float debugBasicAttackMovementLockRemaining;
    [SerializeField] private float debugActionLockoutRemaining;

    private float movementMultiplier = 1f;
    private float movementCommitmentUntil = -999f;
    private float basicAttackMovementLockUntil = -999f;
    private float actionLockoutUntil = -999f;

    public float MovementMultiplier
    {
        get
        {
            if (Time.time >= movementCommitmentUntil)
                return 1f;

            return Mathf.Clamp(movementMultiplier, 0f, 1f);
        }
    }

    public bool IsActionLocked => Time.time < actionLockoutUntil;
    public bool IsBasicAttackMovementLocked =>
        Time.time < basicAttackMovementLockUntil;

    public float MovementCommitmentRemaining =>
        Mathf.Max(0f, movementCommitmentUntil - Time.time);

    public float BasicAttackMovementLockRemaining =>
        Mathf.Max(0f, basicAttackMovementLockUntil - Time.time);

    public float ActionLockoutRemaining =>
        Mathf.Max(0f, actionLockoutUntil - Time.time);

    public bool CanStartAttack => !IsActionLocked;

    private void Update()
    {
        if (Time.time >= movementCommitmentUntil)
            movementMultiplier = 1f;

        debugMovementMultiplier = MovementMultiplier;
        debugMovementCommitmentRemaining = MovementCommitmentRemaining;
        debugBasicAttackMovementLocked = IsBasicAttackMovementLocked;
        debugBasicAttackMovementLockRemaining =
            BasicAttackMovementLockRemaining;
        debugActionLockoutRemaining = ActionLockoutRemaining;
    }

    /// <summary>
    /// Locks voluntary movement for the current character's authored attack
    /// window when that Character Definition enables the option.
    /// </summary>
    public void ApplyBasicAttackMovementLock(float duration)
    {
        PartyManager manager = PartyManager.Instance;
        bool hasActiveCharacter = manager != null &&
                                  manager.party != null &&
                                  manager.activeIndex >= 0 &&
                                  manager.activeIndex < manager.party.Count;
        PartyManager.CharacterState active = hasActiveCharacter
            ? manager.party[manager.activeIndex]
            : null;
        bool shouldLock = active != null && active.def != null &&
                          active.def.lockMovementDuringBasicAttack;
        float effectiveDuration = active != null && active.def != null
            ? Mathf.Max(duration,
                active.def.basicAttackMovementLockDuration)
            : duration;
        ApplyBasicAttackMovementLock(shouldLock, effectiveDuration);
    }

    /// <summary>Explicit overload used by tooling and deterministic tests.</summary>
    public void ApplyBasicAttackMovementLock(
        bool shouldLock,
        float duration)
    {
        if (!shouldLock || duration <= 0f)
            return;

        basicAttackMovementLockUntil = Mathf.Max(
            basicAttackMovementLockUntil,
            Time.time + duration);
    }

    /// <summary>
    /// Briefly slows movement. If multiple attacks overlap, the strongest slow wins
    /// and the longest remaining duration is preserved.
    /// </summary>
    public void ApplyMovementCommitment(
        float multiplier,
        float duration)
    {
        if (duration <= 0f)
            return;

        multiplier = Mathf.Clamp01(multiplier);

        if (multiplier >= 0.999f)
            return;

        float until = Time.time + duration;

        if (Time.time >= movementCommitmentUntil)
        {
            movementMultiplier = multiplier;
            movementCommitmentUntil = until;
            return;
        }

        movementMultiplier = Mathf.Min(
            movementMultiplier,
            multiplier
        );

        movementCommitmentUntil = Mathf.Max(
            movementCommitmentUntil,
            until
        );
    }

    /// <summary>
    /// Prevents starting new basic attacks for a short time. Movement is not stopped.
    /// </summary>
    public void ApplyActionLockout(float duration)
    {
        if (duration <= 0f)
            return;

        actionLockoutUntil = Mathf.Max(
            actionLockoutUntil,
            Time.time + duration
        );
    }

    public void ClearMovementCommitment()
    {
        movementMultiplier = 1f;
        movementCommitmentUntil = -999f;
    }

    public void ClearBasicAttackMovementLock()
    {
        basicAttackMovementLockUntil = -999f;
    }

    public void ClearActionLockout()
    {
        actionLockoutUntil = -999f;
    }

    public void ClearAll()
    {
        ClearMovementCommitment();
        ClearBasicAttackMovementLock();
        ClearActionLockout();
    }
}
