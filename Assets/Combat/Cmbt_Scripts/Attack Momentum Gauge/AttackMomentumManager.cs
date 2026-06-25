using System;
using UnityEngine;

/// <summary>
/// Scene-local, party-wide Attack Momentum state.
///
/// Design goals:
/// - Momentum is slow to build and increasingly difficult near the top.
/// - After a tier-based grace period, the visible value drains quickly.
/// - A qualifying action before zero restores the visible value to the banked value,
///   applies the new gain, banks the result, and restarts grace.
/// - Reaching zero breaks the chain and clears the bank.
/// - Encounter time always uses real time.
/// </summary>
[DisallowMultipleComponent]
public class AttackMomentumManager : MonoBehaviour
{
    public static AttackMomentumManager Instance { get; private set; }

    [Header("Gauge")]
    [SerializeField, Min(1f)] private float maximumMomentum = 100f;

    [Tooltip(
        "Scales every gain reported by attacks and skills. " +
        "At 0.25, an attack that reports 8 Momentum actually adds 2 before tier scaling."
    )]
    [SerializeField, Min(0f)] private float globalGainScale = 0.25f;

    [Header("Difficulty Thresholds")]
    [SerializeField, Range(0.01f, 0.98f)] private float firstThreshold = 0.50f;
    [SerializeField, Range(0.02f, 0.99f)] private float secondThreshold = 0.75f;
    [SerializeField, Range(0.03f, 1f)] private float thirdThreshold = 0.90f;

    [Header("Gain Multipliers by Current Momentum")]
    [Tooltip("Applied from 0% up to the first threshold.")]
    [SerializeField, Min(0f)] private float lowMomentumGainMultiplier = 1.00f;

    [Tooltip("Applied from the first threshold up to the second threshold.")]
    [SerializeField, Min(0f)] private float mediumMomentumGainMultiplier = 0.70f;

    [Tooltip("Applied from the second threshold up to the third threshold.")]
    [SerializeField, Min(0f)] private float highMomentumGainMultiplier = 0.45f;

    [Tooltip("Applied from the third threshold to 100%.")]
    [SerializeField, Min(0f)] private float peakMomentumGainMultiplier = 0.20f;

    [Header("Grace Period by Momentum Tier")]
    [SerializeField, Min(0f)] private float lowMomentumGrace = 1.25f;
    [SerializeField, Min(0f)] private float mediumMomentumGrace = 1.00f;
    [SerializeField, Min(0f)] private float highMomentumGrace = 0.80f;
    [SerializeField, Min(0f)] private float peakMomentumGrace = 0.60f;

    [Header("Drain Time by Momentum Tier")]
    [Tooltip(
        "Approximate real-time seconds for the visible gauge to drain from its banked " +
        "value to zero after grace expires."
    )]
    [SerializeField, Min(0.05f)] private float lowMomentumDrainToZeroSeconds = 2.40f;
    [SerializeField, Min(0.05f)] private float mediumMomentumDrainToZeroSeconds = 2.00f;
    [SerializeField, Min(0.05f)] private float highMomentumDrainToZeroSeconds = 1.60f;
    [SerializeField, Min(0.05f)] private float peakMomentumDrainToZeroSeconds = 1.25f;


    [Header("Debug")]
    [SerializeField] private bool logQualifyingActions = false;
    [SerializeField] private bool logEncounterResult = true;

    public event Action<float, float, float> OnMomentumChanged;
    public event Action OnChainBroken;
    public event Action<AttackMomentumResult> OnEncounterCompleted;

    public float MaximumMomentum => Mathf.Max(1f, maximumMomentum);
    public float CurrentMomentum { get; private set; }
    public float BankedMomentum { get; private set; }

    public float CurrentNormalized =>
        Mathf.Clamp01(CurrentMomentum / MaximumMomentum);

    public float BankedNormalized =>
        Mathf.Clamp01(BankedMomentum / MaximumMomentum);

    public float PeakMomentumNormalized { get; private set; }
    public float EncounterSeconds { get; private set; }

    public float AverageMomentumNormalized =>
        sampledMomentumSeconds > 0f
            ? Mathf.Clamp01(momentumIntegral / sampledMomentumSeconds)
            : 0f;

    public float ActiveAverageMomentumNormalized =>
        activeSampledMomentumSeconds > 0f
            ? Mathf.Clamp01(
                activeMomentumIntegral /
                activeSampledMomentumSeconds
            )
            : 0f;

    public float ActiveMomentumScoringSeconds =>
        activeSampledMomentumSeconds;

    public bool HasStartedActiveMomentumScoring =>
        activeScoringStarted;

    public int ChainBreakCount { get; private set; }
    public int QualifyingActionCount { get; private set; }

    public bool IsRunning { get; private set; }
    public bool IsMomentumPaused { get; private set; }

    public float FocusDrainMultiplier =>
        Mathf.Clamp01(focusDrainMultiplier);

    public AttackMomentumResult LastResult { get; private set; }

    public float CurrentTierGainMultiplier =>
        GetTierGainMultiplier(BankedNormalized);

    public float CurrentTierGraceDuration =>
        GetTierGraceDuration(BankedNormalized);

    public float CurrentTierDrainToZeroSeconds =>
        GetTierDrainToZeroSeconds(BankedNormalized);

    private float graceRemaining;
    private float momentumIntegral;
    private float sampledMomentumSeconds;

    private bool activeScoringStarted;
    private float activeMomentumIntegral;
    private float activeSampledMomentumSeconds;

    // Runtime value supplied by PlayerFocusMode.
    // 1 = normal drain, 0.5 = half-speed drain, 0 = no active drain.
    private float focusDrainMultiplier = 1f;

    private float lastBroadcastCurrent = -1f;
    private float lastBroadcastBanked = -1f;

    private const float ZeroEpsilon = 0.0001f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                "AttackMomentumManager: Multiple scene instances found. " +
                "The newest instance will become active.",
                this
            );
        }

        Instance = this;
    }

    private void OnValidate()
    {
        maximumMomentum = Mathf.Max(1f, maximumMomentum);
        globalGainScale = Mathf.Max(0f, globalGainScale);

        firstThreshold = Mathf.Clamp(firstThreshold, 0.01f, 0.98f);
        secondThreshold = Mathf.Clamp(
            secondThreshold,
            firstThreshold + 0.01f,
            0.99f
        );
        thirdThreshold = Mathf.Clamp(
            thirdThreshold,
            secondThreshold + 0.01f,
            1f
        );

        lowMomentumDrainToZeroSeconds =
            Mathf.Max(0.05f, lowMomentumDrainToZeroSeconds);

        mediumMomentumDrainToZeroSeconds =
            Mathf.Max(0.05f, mediumMomentumDrainToZeroSeconds);

        highMomentumDrainToZeroSeconds =
            Mathf.Max(0.05f, highMomentumDrainToZeroSeconds);

        peakMomentumDrainToZeroSeconds =
            Mathf.Max(0.05f, peakMomentumDrainToZeroSeconds);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        float dt = Time.unscaledDeltaTime;
        EncounterSeconds += dt;

        // The encounter clock continues while the skill menu is open,
        // but Momentum decay and average sampling pause.
        if (IsMomentumPaused)
            return;

        TickMomentum(dt);

        momentumIntegral += CurrentNormalized * dt;
        sampledMomentumSeconds += dt;

        if (activeScoringStarted)
        {
            activeMomentumIntegral += CurrentNormalized * dt;
            activeSampledMomentumSeconds += dt;
        }

        BroadcastIfChanged();
    }

    public void BeginEncounter()
    {
        CurrentMomentum = 0f;
        BankedMomentum = 0f;
        PeakMomentumNormalized = 0f;
        EncounterSeconds = 0f;

        ChainBreakCount = 0;
        QualifyingActionCount = 0;

        graceRemaining = 0f;
        momentumIntegral = 0f;
        sampledMomentumSeconds = 0f;

        activeScoringStarted = false;
        activeMomentumIntegral = 0f;
        activeSampledMomentumSeconds = 0f;

        IsMomentumPaused = false;
        focusDrainMultiplier = 1f;
        IsRunning = true;
        LastResult = default;

        ForceBroadcast();
    }

    /// <summary>
    /// Starts Active Average sampling for the rank system.
    ///
    /// Basic attacks, burn ticks, projectile destruction, and enemy-defeat
    /// bonuses should not call this. Successful skills should.
    /// </summary>
    public void StartActiveMomentumScoring()
    {
        if (!IsRunning || activeScoringStarted)
            return;

        activeScoringStarted = true;

        if (logQualifyingActions)
        {
            Debug.Log(
                "Active Momentum rank sampling started.",
                this
            );
        }
    }

    /// <summary>
    /// Registers Momentum from a successful skill and starts Active Average
    /// sampling before the next frame is measured.
    /// </summary>
    public void RegisterSuccessfulSkill(float rawAmount)
    {
        if (!IsRunning)
            return;

        rawAmount = Mathf.Max(0f, rawAmount);

        if (rawAmount <= 0f)
            return;

        StartActiveMomentumScoring();
        RegisterMomentum(rawAmount);
    }

    /// <summary>
    /// Registers one ordinary qualifying action.
    ///
    /// Use this for basic attacks, burn ticks, and other small non-skill gains.
    /// The manager applies globalGainScale and the current tier multiplier.
    /// </summary>
    public void RegisterMomentum(float rawAmount)
    {
        if (!IsRunning)
            return;

        rawAmount = Mathf.Max(0f, rawAmount);
        if (rawAmount <= 0f)
            return;

        // Save the chain first. This prevents intentionally waiting for the
        // visible value to fall into an easier gain tier.
        if (CurrentMomentum > ZeroEpsilon &&
            BankedMomentum > CurrentMomentum)
        {
            CurrentMomentum = BankedMomentum;
        }

        float normalizedBeforeGain = BankedNormalized;
        float tierMultiplier =
            GetTierGainMultiplier(normalizedBeforeGain);

        float appliedAmount =
            rawAmount *
            Mathf.Max(0f, globalGainScale) *
            tierMultiplier;

        if (appliedAmount <= 0f)
            return;

        CurrentMomentum = Mathf.Clamp(
            CurrentMomentum + appliedAmount,
            0f,
            MaximumMomentum
        );

        BankedMomentum = CurrentMomentum;

        // Use the tier reached after the gain so higher levels immediately
        // become harder to maintain.
        graceRemaining =
            GetTierGraceDuration(BankedNormalized);

        QualifyingActionCount++;

        PeakMomentumNormalized = Mathf.Max(
            PeakMomentumNormalized,
            CurrentNormalized
        );

        if (logQualifyingActions)
        {
            Debug.Log(
                $"Momentum raw +{rawAmount:0.##}, " +
                $"applied +{appliedAmount:0.##}. " +
                $"Tier multiplier={tierMultiplier:0.##}. " +
                $"Current={CurrentMomentum:0.##}, " +
                $"Banked={BankedMomentum:0.##}",
                this
            );
        }

        ForceBroadcast();
    }

    /// <summary>
    /// Adds an exact Momentum bonus without applying Global Gain Scale or
    /// tier-based gain penalties.
    ///
    /// Use this for major combat events such as defeating an enemy.
    /// The bonus still restores a draining chain, updates the bank, restarts
    /// the current tier's grace period, and contributes to peak Momentum.
    /// </summary>
    public void RegisterGuaranteedBonus(float amount)
    {
        if (!IsRunning)
            return;

        amount = Mathf.Max(0f, amount);

        if (amount <= 0f)
            return;

        // Save the chain before applying the reward.
        if (CurrentMomentum > ZeroEpsilon &&
            BankedMomentum > CurrentMomentum)
        {
            CurrentMomentum = BankedMomentum;
        }

        CurrentMomentum = Mathf.Clamp(
            CurrentMomentum + amount,
            0f,
            MaximumMomentum
        );

        BankedMomentum = CurrentMomentum;
        graceRemaining = GetTierGraceDuration(BankedNormalized);

        QualifyingActionCount++;

        PeakMomentumNormalized = Mathf.Max(
            PeakMomentumNormalized,
            CurrentNormalized
        );

        if (logQualifyingActions)
        {
            Debug.Log(
                $"Guaranteed Momentum bonus +{amount:0.##}. " +
                $"Current={CurrentMomentum:0.##}, " +
                $"Banked={BankedMomentum:0.##}",
                this
            );
        }

        ForceBroadcast();
    }

    /// <summary>
    /// Permanently removes Momentum from both the visible value and the bank.
    /// This is intended for mistakes such as the active character taking damage.
    ///
    /// The penalty does not restart grace and is not affected by gain scaling.
    /// If it reduces Current Momentum to zero, the chain breaks normally.
    /// </summary>
    public void ApplyMomentumPenalty(float amount)
    {
        if (!IsRunning)
            return;

        amount = Mathf.Max(0f, amount);

        if (amount <= 0f ||
            (CurrentMomentum <= ZeroEpsilon &&
             BankedMomentum <= ZeroEpsilon))
        {
            return;
        }

        float previousCurrent = CurrentMomentum;
        float previousBanked = BankedMomentum;

        CurrentMomentum = Mathf.Max(
            0f,
            CurrentMomentum - amount
        );

        BankedMomentum = Mathf.Max(
            0f,
            BankedMomentum - amount
        );

        // Current can never sit above the reduced bank.
        CurrentMomentum = Mathf.Min(
            CurrentMomentum,
            BankedMomentum
        );

        if (logQualifyingActions)
        {
            Debug.Log(
                $"Momentum penalty -{amount:0.##}. " +
                $"Current {previousCurrent:0.##} -> " +
                $"{CurrentMomentum:0.##}, " +
                $"Banked {previousBanked:0.##} -> " +
                $"{BankedMomentum:0.##}",
                this
            );
        }

        if (CurrentMomentum <= ZeroEpsilon)
        {
            BreakChain();
            return;
        }

        ForceBroadcast();
    }

    /// <summary>
    /// Instantly clears all current and banked Momentum when the active
    /// character is defeated.
    ///
    /// This counts as one chain break only when Momentum actually existed.
    /// It does not stop the encounter clock or end the encounter.
    /// </summary>
    public void ResetForCharacterDefeat()
    {
        if (!IsRunning)
            return;

        bool hadMomentum =
            CurrentMomentum > ZeroEpsilon ||
            BankedMomentum > ZeroEpsilon;

        CurrentMomentum = 0f;
        BankedMomentum = 0f;
        graceRemaining = 0f;

        if (hadMomentum)
        {
            ChainBreakCount++;
            OnChainBroken?.Invoke();
        }

        ForceBroadcast();

        if (logQualifyingActions)
        {
            Debug.Log(
                hadMomentum
                    ? "Momentum reset because the active character was defeated."
                    : "Character defeated while Momentum was already empty.",
                this
            );
        }
    }

    /// <summary>
    /// Sets the multiplier applied to active Momentum drainage.
    ///
    /// 1 = normal drain speed.
    /// 0.5 = half-speed drain.
    /// 0 = active drainage stops.
    ///
    /// This does not pause the encounter clock, average-Momentum sampling,
    /// or the grace-period countdown. The skill menu's full pause still
    /// takes priority through SetMomentumPaused.
    /// </summary>
    public void SetFocusDrainMultiplier(float multiplier)
    {
        focusDrainMultiplier = Mathf.Clamp01(multiplier);
    }

    public void SetMomentumPaused(bool paused)
    {
        IsMomentumPaused = paused;
    }

    public AttackMomentumResult CompleteEncounter(bool succeeded)
    {
        if (!IsRunning)
            return LastResult;

        IsRunning = false;
        IsMomentumPaused = false;
        focusDrainMultiplier = 1f;

        LastResult = new AttackMomentumResult(
            succeeded,
            EncounterSeconds,
            AverageMomentumNormalized,
            ActiveAverageMomentumNormalized,
            PeakMomentumNormalized,
            ActiveMomentumScoringSeconds,
            ChainBreakCount,
            QualifyingActionCount
        );

        if (logEncounterResult)
        {
            Debug.Log(
                $"Attack Momentum result: " +
                $"Success={LastResult.succeeded}, " +
                $"Time={LastResult.encounterSeconds:0.00}s, " +
                $"FullAverage={LastResult.averageMomentumNormalized:P1}, " +
                $"ActiveAverage={LastResult.activeAverageMomentumNormalized:P1}, " +
                $"Peak={LastResult.peakMomentumNormalized:P1}, " +
                $"Breaks={LastResult.chainBreakCount}, " +
                $"Actions={LastResult.qualifyingActionCount}",
                this
            );
        }

        OnEncounterCompleted?.Invoke(LastResult);
        ForceBroadcast();

        return LastResult;
    }

    private void TickMomentum(float dt)
    {
        if (CurrentMomentum <= ZeroEpsilon)
        {
            CurrentMomentum = 0f;
            return;
        }

        if (graceRemaining > 0f)
        {
            graceRemaining =
                Mathf.Max(0f, graceRemaining - dt);

            return;
        }

        float previous = CurrentMomentum;

        float drainDuration =
            GetTierDrainToZeroSeconds(BankedNormalized);

        // Drain based on the banked value, not the current partially drained
        // value. This creates a consistent, readable countdown to chain break.
        float drainRate =
            Mathf.Max(BankedMomentum, CurrentMomentum) /
            Mathf.Max(0.05f, drainDuration);

        float effectiveDrainRate =
            drainRate * Mathf.Clamp01(focusDrainMultiplier);

        CurrentMomentum = Mathf.MoveTowards(
            CurrentMomentum,
            0f,
            effectiveDrainRate * dt
        );

        if (previous > ZeroEpsilon &&
            CurrentMomentum <= ZeroEpsilon)
        {
            BreakChain();
        }
    }

    private float GetTierGainMultiplier(float normalized)
    {
        int tier = GetTier(normalized);

        return tier switch
        {
            0 => Mathf.Max(0f, lowMomentumGainMultiplier),
            1 => Mathf.Max(0f, mediumMomentumGainMultiplier),
            2 => Mathf.Max(0f, highMomentumGainMultiplier),
            _ => Mathf.Max(0f, peakMomentumGainMultiplier)
        };
    }

    private float GetTierGraceDuration(float normalized)
    {
        int tier = GetTier(normalized);

        return tier switch
        {
            0 => Mathf.Max(0f, lowMomentumGrace),
            1 => Mathf.Max(0f, mediumMomentumGrace),
            2 => Mathf.Max(0f, highMomentumGrace),
            _ => Mathf.Max(0f, peakMomentumGrace)
        };
    }

    private float GetTierDrainToZeroSeconds(float normalized)
    {
        int tier = GetTier(normalized);

        return tier switch
        {
            0 => Mathf.Max(
                0.05f,
                lowMomentumDrainToZeroSeconds
            ),
            1 => Mathf.Max(
                0.05f,
                mediumMomentumDrainToZeroSeconds
            ),
            2 => Mathf.Max(
                0.05f,
                highMomentumDrainToZeroSeconds
            ),
            _ => Mathf.Max(
                0.05f,
                peakMomentumDrainToZeroSeconds
            )
        };
    }

    private int GetTier(float normalized)
    {
        if (normalized >= thirdThreshold)
            return 3;

        if (normalized >= secondThreshold)
            return 2;

        if (normalized >= firstThreshold)
            return 1;

        return 0;
    }

    private void BreakChain()
    {
        CurrentMomentum = 0f;
        BankedMomentum = 0f;
        graceRemaining = 0f;

        ChainBreakCount++;

        OnChainBroken?.Invoke();
        ForceBroadcast();
    }

    private void BroadcastIfChanged()
    {
        if (Mathf.Approximately(
                lastBroadcastCurrent,
                CurrentMomentum
            ) &&
            Mathf.Approximately(
                lastBroadcastBanked,
                BankedMomentum
            ))
        {
            return;
        }

        ForceBroadcast();
    }

    private void ForceBroadcast()
    {
        lastBroadcastCurrent = CurrentMomentum;
        lastBroadcastBanked = BankedMomentum;

        OnMomentumChanged?.Invoke(
            CurrentMomentum,
            CurrentNormalized,
            BankedNormalized
        );
    }
}
