using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyShooterDebug : MonoBehaviour
{
    [System.Serializable]
    private class PatternTelegraph
    {
        public PatternType pattern;
        public EnemyTelegraphProfile profile;
    }

    public enum PatternType
    {
        AimedSingle,
        AimedFan,
        Ring,
        Spiral,
        BoI_4Way,
        BoI_8Way,
        SweepFan,

        // Pretty danmaku / tactical patterns v2
        PetalFan,
        ButterflySpread,
        ClosingBlossom,
        RotatingFlowerRing,
        StaggeredRosette,
        CrescentSweep,
        BraidedStream,
        HaloSpear,
        CloseCross,
        EscapeCutoff
    }

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float projectileSpeed = 7f;
    [SerializeField] private bool applyVelocityToRigidbody = true;

    [Header("Burst Fire")]
    [SerializeField] private bool useBurstFire = true;
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float intraBurstInterval = 0.10f;
    [SerializeField] private float burstCooldown = 0.90f;
    [SerializeField] private float losRetryDelay = 0.06f;

    [Header("Burst Quota Per Enable")]
    [SerializeField] private bool limitBurstsPerEnable = true;
    [SerializeField] private int burstsPerEnable = 1;

    [Header("Aim")]
    [SerializeField] private float aimLagSeconds = 0.07f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float maxRange = 30f;
    [Tooltip("When firing a fan pattern, always include one extra bullet aimed directly at the " +
             "target's current real position (bypasses aim lag). Eliminates gaps in fan dead zones.")]
    [SerializeField] private bool guaranteeCenterBullet = true;

    [Header("Target")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";
    [SerializeField] private Transform target;

    [Header("Pattern")]
    [SerializeField] private PatternType pattern = PatternType.AimedFan;

    [Tooltip("Used by AimedFan and SweepFan")]
    [SerializeField] private int fanBullets = 5;

    [Tooltip("Degrees total cone width for AimedFan")]
    [SerializeField] private float fanArcDegrees = 40f;

    [Tooltip("Used by Ring")]
    [SerializeField] private int ringBullets = 12;

    [Tooltip("Degrees per tick for Spiral and SweepFan")]
    [SerializeField] private float angularSpeedDegPerTick = 12f;

    [Tooltip("For Spiral: bullets per tick (usually 1-3)")]
    [SerializeField] private int spiralBulletsPerTick = 2;

    [Tooltip("If true, Spiral is centered on aim direction. If false, it rotates globally.")]
    [SerializeField] private bool spiralCenteredOnAim = true;

    [Header("Pretty Danmaku Patterns v4")]
    [Tooltip("Extra rotation applied to flower/ring patterns every time this enemy emits a pattern. Higher = more pinwheel motion.")]
    [SerializeField] private float prettyPatternRotationStep = 11f;

    [Tooltip("Center gap in degrees for Butterfly Spread. Larger = safer middle lane, stronger side pincer.")]
    [SerializeField] private float butterflyCenterGapDegrees = 18f;

    [Tooltip("Used by Halo Spear. The spear projectile fires after the decorative halo by this many seconds.")]
    [SerializeField, Min(0f)] private float haloSpearDelay = 0.11f;

    [Tooltip("Used by Escape Cutoff. This aims ahead of the player's recent movement, making repeated dodge directions less safe.")]
    [SerializeField] private float escapeCutoffLeadDistance = 1.15f;

    [Tooltip("If true, role/identity code may tint projectile sprites. Leave on for Touhou-style readability.")]
    [SerializeField] private bool allowProjectileTint = true;

    [Tooltip("If false, pretty patterns do not add a bonus direct center bullet on top of their shape. Recommended false for regular enemies.")]
    [SerializeField] private bool guaranteeCenterBulletOnPrettyPatterns = false;

    [Tooltip("Maximum halo bullets used by HaloSpear before the delayed spear. Keeps basic enemy halo patterns readable.")]
    [SerializeField, Min(6)] private int haloSpearMaxHaloBullets = 10;

    [Tooltip("Maximum visible projectile directions used by rotating flower rings on regular enemies.")]
    [SerializeField, Min(4)] private int rotatingFlowerMaxProjectiles = 8;

    [Tooltip("Current tint applied to spawned projectile SpriteRenderers when tinting is enabled by EnemyBrain.")]
    [SerializeField] private Color projectileTint = Color.white;

    [Tooltip("Runtime/debug. EnemyBrain toggles this when role tinting is enabled.")]
    [SerializeField] private bool projectileTintEnabled = false;

    [Header("Attack Telegraph")]
    [SerializeField] private EnemyAttackTelegraph attackTelegraph;
    [SerializeField] private PatternTelegraph[] patternTelegraphs;
    [SerializeField] private EnemyTelegraphProfile fallbackTelegraphProfile;
    [SerializeField] private bool lockAimDuringTelegraph = true;

    [Header("Pretty Pattern Telegraph Reuse v4")]
    [Tooltip("If a new pretty pattern has no exact telegraph assigned, reuse an older pattern telegraph with a similar shape.")]
    [SerializeField] private bool reuseLegacyTelegraphsForPrettyPatterns = true;

    [Tooltip("Runtime/debug: shows which telegraph profile was selected for the current pattern.")]
    [SerializeField] private string debugTelegraphProfileSource = "None";

    [Header("Aggression / Burst Flow v1")]
    [Tooltip(
        "If true, burst attacks telegraph once and then fire all burst shots quickly. " +
        "This removes the old slow loop where every single burst shot had its own full telegraph."
    )]
    [SerializeField] private bool telegraphOnlyOncePerBurst = true;

    [Tooltip("Optional logs for burst-flow testing.")]
    [SerializeField] private bool debugBurstFlowLogs = false;


    [Header("Projectile Readability Startup v3")]
    [Tooltip("If true, enemy projectiles appear as a full pattern, wait as a readable threat, then accelerate to normal speed.")]
    [SerializeField] private bool useProjectileStartupMotion = true;

    [Tooltip("Seconds the fully revealed pattern stays stationary before the acceleration ramp begins.")]
    [SerializeField, Min(0f)] private float projectileStartupHoldTime = 0.08f;

    [Tooltip("Seconds spent accelerating from the initial multiplier to full projectile speed.")]
    [SerializeField, Min(0f)] private float projectileStartupRampDuration = 0.28f;

    [Tooltip("Speed multiplier at the instant the launch ramp begins. 0.05 gives a gentle first movement after the stationary warning.")]
    [SerializeField, Range(0f, 1f)] private float projectileStartupInitialSpeedMultiplier = 0.05f;

    [Tooltip("Higher values stay slower for longer, then snap closer to full speed near the end. 1 = linear.")]
    [SerializeField, Min(0.1f)] private float projectileStartupEasePower = 2.0f;

    [Tooltip("When a bullet is reflected by Push Back, remove this startup slow so the reflected bullet immediately travels normally.")]
    [SerializeField] private bool removeStartupRampWhenReflected = true;

    [Tooltip("Fades each spawned projectile from transparent to its normal tinted color without changing its collider size.")]
    [SerializeField] private bool useProjectileStartupFade = true;

    [Tooltip("Seconds for a projectile to become fully visible. When the movement wait below is enabled, this is also the first part of the stationary reaction window.")]
    [SerializeField, Min(0.01f)] private float projectileStartupFadeDuration = 0.18f;

    [Tooltip("Starting fraction of the projectile sprite's normal alpha. 0.10 keeps the bullet barely visible on its spawn frame.")]
    [SerializeField, Range(0f, 1f)] private float projectileStartupInitialAlpha = 0.10f;

    [Tooltip("Keeps the projectile completely stationary until its fade has finished. This guarantees it is fully visible before it is fired.")]
    [SerializeField] private bool waitForProjectileFadeBeforeMovement = true;

    [Header("Runtime")]
    [SerializeField] private bool shootingEnabled = false;
    [SerializeField] private string lastBlockReason = "None";
    [SerializeField] private string cooldownSetBy = "None";
    [SerializeField] private bool debugLosBlocked = false;
    [SerializeField] private string debugLosHitObject = "None";
    [SerializeField] private float lastSuccessfulShotTime = -999f;

    [Header("Basic Attack Interrupt Debug")]
    [SerializeField] private float debugTelegraphProgress;
    [SerializeField] private string debugLastInterruptResult = "None";

    private struct TargetSample { public float time; public Vector2 pos; }
    private readonly List<TargetSample> samples = new List<TargetSample>(64);

    private float nextFireTime = 0f;
    private float nextTargetSearchTime = 0f;
    private int burstShotsRemaining = 0;
    private int burstsFiredThisEnable = 0;
    private float spiralAngleDeg = 0f;
    private float sweepAngleDeg = 0f;
    private int sweepDir = 1;
    private int prettyPatternPulseIndex = 0;
    private Vector2 lastTargetPosition;
    private Vector2 estimatedTargetVelocity;
    private bool hasLastTargetPosition = false;
    private bool isTelegraphing = false;
    private bool telegraphCommitted = false;
    private float telegraphStartedTime;
    private float activeTelegraphDuration;
    private Coroutine activeAttackRoutine;

    private const float TargetSearchInterval = 0.20f;
    private const float MinInterval = 0.03f;

    public Transform CurrentTarget => target;
    public string LastBlockReason => lastBlockReason;
    public bool IsShootingEnabled => shootingEnabled;
    public bool IsTelegraphing => isTelegraphing;
    public bool DebugLosBlocked => debugLosBlocked;
    public string DebugLosHitObject => debugLosHitObject;
    public float LastSuccessfulShotTime => lastSuccessfulShotTime;
    public bool BurstQuotaReached =>
        limitBurstsPerEnable &&
        useBurstFire &&
        burstsPerEnable > 0 &&
        burstsFiredThisEnable >= burstsPerEnable;
    public int BurstsFiredThisEnable => burstsFiredThisEnable;
    public int BurstQuota => burstsPerEnable;

    private void OnEnable()
    {
        samples.Clear();
        ResetBurstStateForEnable();

        // Enemies should not fire just because the prefab spawned.
        // EnemyBrain opens this gate only after the enemy reaches / settles into
        // its tactical behavior and enters an attack window.
        shootingEnabled = false;
        lastBlockReason = "SpawnLocked";
        ScheduleNextFire(999f, "SpawnLocked");
    }

    private void Update()
    {
        debugTelegraphProgress = GetTelegraphProgress();

        ResolveTarget();
        RecordTargetSample();

        if (!shootingEnabled) { lastBlockReason = "ShootingDisabled"; return; }
        if (isTelegraphing) { lastBlockReason = "Telegraphing"; return; }
        if (projectilePrefab == null) { lastBlockReason = "NoProjectilePrefab"; return; }

        if (Time.time < nextFireTime)
        {
            lastBlockReason = $"Cooldown({cooldownSetBy})";
            return;
        }

        if (BurstQuotaReached)
        {
            lastBlockReason = "BurstQuotaReached";
            return;
        }

        if (!TryGetDelayedAimPoint(out Vector2 aimPoint))
        {
            lastBlockReason = "NoTargetOrAimPoint";
            ScheduleNextFire(0.04f, "RetryNoTarget");
            return;
        }

        Vector2 origin = muzzle != null ? (Vector2)muzzle.position : (Vector2)transform.position;
        Vector2 toAim = aimPoint - origin;
        float dist = toAim.magnitude;

        if (dist < 0.001f) { lastBlockReason = "AimTooClose"; ScheduleNextFire(0.04f, "RetryAimTooClose"); return; }
        if (dist > maxRange) { lastBlockReason = "OutOfRange"; ScheduleNextFire(0.06f, "RetryOutOfRange"); return; }

        if (requireLineOfSight && target != null)
        {
            if (IsLineBlocked(origin, target.position))
            {
                lastBlockReason = "LOSBlocked";
                ScheduleNextFire(losRetryDelay, "LOSRetry");
                return;
            }
        }

        Vector2 baseDir = toAim / dist;

        activeAttackRoutine = StartCoroutine(
            TelegraphAndFireRoutine(origin, baseDir, useBurstFire)
        );
    }

    /// <summary>
    /// Gives a basic attack one chance to defer this shooter's next attack.
    /// Early telegraphs are canceled; committed telegraphs and active bursts
    /// are protected so repeated basics cannot permanently suppress offense.
    /// </summary>
    public bool TryBasicAttackInterrupt(
        float delay,
        float commitPoint = 0.70f)
    {
        if (!isActiveAndEnabled || !shootingEnabled)
        {
            debugLastInterruptResult = "ShooterInactive";
            return false;
        }

        if (isTelegraphing)
        {
            float progress = GetTelegraphProgress();

            if (telegraphCommitted ||
                progress >= Mathf.Clamp01(commitPoint))
            {
                debugLastInterruptResult =
                    $"Committed({progress:0.00})";

                return false;
            }

            CancelActiveAttackTelegraph();
            burstShotsRemaining = 0;
            DelayNextFireAtLeast(delay, "BasicAttackInterrupt");
            lastBlockReason = "BasicAttackInterruptedTelegraph";
            debugLastInterruptResult = "TelegraphInterrupted";
            return true;
        }

        DelayNextFireAtLeast(delay, "BasicAttackOpening");
        lastBlockReason = "BasicAttackOpening";
        debugLastInterruptResult = "NextAttackDeferred";
        return true;
    }

    public void SetShootingEnabled(bool enabled)
    {
        if (enabled == shootingEnabled) return;

        shootingEnabled = enabled;

        if (shootingEnabled)
        {
            ResetBurstStateForEnable();
            ScheduleNextFire(0.03f, "EnableRisingEdge");
            lastBlockReason = "ShooterEnabled";
        }
        else
        {
            ResetBurstStateForEnable();
            CancelActiveAttackTelegraph();
            ScheduleNextFire(999f, "ShootingDisabled");
            lastBlockReason = "ShootingDisabled";
        }
    }

    public void SetFireInterval(float value)
    {
        intraBurstInterval = Mathf.Max(MinInterval, value);
    }

    public void SetAimLag(float value)
    {
        aimLagSeconds = Mathf.Max(0f, value);
    }

    public void ForceReadyToFire(float delay = 0f)
    {
        ScheduleNextFire(delay, "ForceReadyToFire");
    }

    public void SetTarget(Transform newTarget)
    {
        if (target == newTarget) return;
        target = newTarget;
        samples.Clear();
    }

    public void SetPattern(PatternType newPattern)
    {
        pattern = newPattern;
    }

    public bool SetPattern(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName))
            return false;

        if (!System.Enum.TryParse(
                patternName,
                true,
                out PatternType parsed))
        {
            return false;
        }

        SetPattern(parsed);
        return true;
    }

    public void SetBurstConfig(
        int newShotsPerBurst,
        float newIntraBurstInterval,
        float newBurstCooldown)
    {
        shotsPerBurst =
            Mathf.Max(1, newShotsPerBurst);

        intraBurstInterval =
            Mathf.Max(
                MinInterval,
                newIntraBurstInterval
            );

        burstCooldown =
            Mathf.Max(
                MinInterval,
                newBurstCooldown
            );
    }

    public void SetBurstQuotaPerEnable(
        bool enabled,
        int quota)
    {
        limitBurstsPerEnable = enabled;
        burstsPerEnable = Mathf.Max(1, quota);
    }

    public void SetFanConfig(
        int bullets,
        float arcDegrees)
    {
        fanBullets = Mathf.Max(1, bullets);
        fanArcDegrees = Mathf.Max(0f, arcDegrees);
    }

    public void SetRingBullets(int bullets)
    {
        ringBullets = Mathf.Max(3, bullets);
    }

    public void SetAngularSpeed(
        float degreesPerTick)
    {
        angularSpeedDegPerTick =
            degreesPerTick;
    }

    public void SetProjectileTint(Color tint, bool enabled)
    {
        projectileTint = tint;
        projectileTintEnabled = enabled;
    }

    public void ClearProjectileTint()
    {
        projectileTint = Color.white;
        projectileTintEnabled = false;
    }

    private IEnumerator TelegraphAndFireRoutine(
        Vector2 origin,
        Vector2 initialDirection,
        bool fireAsBurstStep)
    {
        if (isTelegraphing)
            yield break;

        isTelegraphing = true;
        telegraphCommitted = false;

        EnemyTelegraphProfile profile =
            GetTelegraphProfile(pattern);

        activeTelegraphDuration = profile != null
            ? Mathf.Max(0.01f, profile.duration)
            : 0f;

        telegraphStartedTime = Time.time;

        Vector2 lockedDirection =
            initialDirection.sqrMagnitude > 0.0001f
                ? initialDirection.normalized
                : Vector2.right;

        if (attackTelegraph != null &&
            profile != null)
        {
            yield return
                attackTelegraph.PlayTelegraphRoutine(
                    profile,
                    lockedDirection
                );
        }
        else if (profile != null)
        {
            yield return new WaitForSeconds(
                Mathf.Max(
                    0.01f,
                    profile.duration
                )
            );
        }

        telegraphCommitted = true;

        if (!shootingEnabled ||
            target == null)
        {
            FinishActiveAttack();
            yield break;
        }

        Vector2 currentOrigin =
            muzzle != null
                ? (Vector2)muzzle.position
                : (Vector2)transform.position;

        if (requireLineOfSight &&
            IsLineBlocked(
                currentOrigin,
                target.position))
        {
            lastBlockReason =
                "LOSBlockedAfterTelegraph";

            ScheduleNextFire(
                losRetryDelay,
                "LOSRetryAfterTelegraph"
            );

            FinishActiveAttack();
            yield break;
        }

        Vector2 fireDirection =
            lockedDirection;

        if (!lockAimDuringTelegraph &&
            TryGetDelayedAimPoint(
                out Vector2 updatedAimPoint))
        {
            Vector2 toUpdatedAim =
                updatedAimPoint -
                currentOrigin;

            if (toUpdatedAim.sqrMagnitude >
                0.0001f)
            {
                fireDirection =
                    toUpdatedAim.normalized;
            }
        }

        if (fireAsBurstStep && telegraphOnlyOncePerBurst)
        {
            yield return FireBurstSequenceAfterTelegraph(
                currentOrigin,
                fireDirection
            );
        }
        else if (fireAsBurstStep)
        {
            FireBurstStep(
                currentOrigin,
                fireDirection
            );
        }
        else
        {
            FireSingleTick(
                currentOrigin,
                fireDirection
            );
        }

        FinishActiveAttack();
    }

    private float GetTelegraphProgress()
    {
        if (!isTelegraphing)
            return 0f;

        if (telegraphCommitted || activeTelegraphDuration <= 0f)
            return 1f;

        return Mathf.Clamp01(
            (Time.time - telegraphStartedTime) /
            activeTelegraphDuration
        );
    }

    private void CancelActiveAttackTelegraph()
    {
        if (activeAttackRoutine != null)
        {
            StopCoroutine(activeAttackRoutine);
            activeAttackRoutine = null;
        }

        if (attackTelegraph != null)
            attackTelegraph.CancelTelegraph();

        isTelegraphing = false;
        telegraphCommitted = false;
        activeTelegraphDuration = 0f;
        debugTelegraphProgress = 0f;
    }

    private void FinishActiveAttack()
    {
        activeAttackRoutine = null;
        isTelegraphing = false;
        telegraphCommitted = false;
        activeTelegraphDuration = 0f;
        debugTelegraphProgress = 0f;
    }

    private void DelayNextFireAtLeast(float delay, string reason)
    {
        float requestedTime =
            Time.time + Mathf.Max(0f, delay);

        if (requestedTime > nextFireTime)
            nextFireTime = requestedTime;

        cooldownSetBy = reason;
    }

    private IEnumerator FireBurstSequenceAfterTelegraph(
        Vector2 firstOrigin,
        Vector2 initialDirection)
    {
        int shots =
            Mathf.Max(1, shotsPerBurst);

        bool firedAnyShot = false;

        for (int i = 0; i < shots; i++)
        {
            if (!shootingEnabled || target == null)
                break;

            Vector2 shotOrigin =
                muzzle != null
                    ? (Vector2)muzzle.position
                    : (Vector2)transform.position;

            Vector2 shotDirection =
                initialDirection.sqrMagnitude > 0.0001f
                    ? initialDirection.normalized
                    : Vector2.right;

            if (!lockAimDuringTelegraph &&
                TryGetDelayedAimPoint(out Vector2 aimPoint))
            {
                Vector2 toAim =
                    aimPoint - shotOrigin;

                if (toAim.sqrMagnitude > 0.0001f)
                    shotDirection = toAim.normalized;
            }

            if (requireLineOfSight &&
                IsLineBlocked(shotOrigin, target.position))
            {
                lastBlockReason =
                    firedAnyShot
                        ? "LOSBlockedDuringBurst"
                        : "LOSBlockedBeforeBurstShot";

                ScheduleNextFire(
                    losRetryDelay,
                    "LOSRetryDuringBurst"
                );

                break;
            }

            EmitPattern(shotOrigin, shotDirection);
            firedAnyShot = true;

            if (debugBurstFlowLogs)
            {
                Debug.Log(
                    $"EnemyShooterDebug: {name} burst shot {i + 1}/{shots}",
                    this
                );
            }

            if (i < shots - 1)
            {
                yield return new WaitForSeconds(
                    Mathf.Max(MinInterval, intraBurstInterval)
                );
            }
        }

        if (firedAnyShot)
        {
            burstsFiredThisEnable++;
            burstShotsRemaining = 0;
            ScheduleNextFire(
                Mathf.Max(MinInterval, burstCooldown),
                "BurstSequenceCooldown"
            );
        }
    }

    private EnemyTelegraphProfile GetTelegraphProfile(PatternType requestedPattern)
    {
        EnemyTelegraphProfile exact = FindTelegraphProfile(requestedPattern);
        if (exact != null)
        {
            debugTelegraphProfileSource = $"Exact:{requestedPattern}";
            return exact;
        }

        if (reuseLegacyTelegraphsForPrettyPatterns &&
            TryGetLegacyTelegraphAlias(
                requestedPattern,
                out PatternType aliasPattern))
        {
            EnemyTelegraphProfile alias =
                FindTelegraphProfile(aliasPattern);

            if (alias != null)
            {
                debugTelegraphProfileSource =
                    $"Alias:{requestedPattern}->{aliasPattern}";

                return alias;
            }
        }

        debugTelegraphProfileSource =
            fallbackTelegraphProfile != null
                ? $"Fallback:{requestedPattern}"
                : $"None:{requestedPattern}";

        return fallbackTelegraphProfile;
    }

    private EnemyTelegraphProfile FindTelegraphProfile(
        PatternType requestedPattern)
    {
        if (patternTelegraphs == null)
            return null;

        for (int i = 0; i < patternTelegraphs.Length; i++)
        {
            PatternTelegraph entry = patternTelegraphs[i];

            if (entry != null &&
                entry.pattern == requestedPattern &&
                entry.profile != null)
            {
                return entry.profile;
            }
        }

        return null;
    }

    private bool TryGetLegacyTelegraphAlias(
        PatternType requestedPattern,
        out PatternType aliasPattern)
    {
        switch (requestedPattern)
        {
            case PatternType.PetalFan:
                aliasPattern = PatternType.AimedFan;
                return true;

            case PatternType.ButterflySpread:
                aliasPattern = PatternType.AimedFan;
                return true;

            case PatternType.ClosingBlossom:
                aliasPattern = PatternType.SweepFan;
                return true;

            case PatternType.RotatingFlowerRing:
                aliasPattern = PatternType.Ring;
                return true;

            case PatternType.StaggeredRosette:
                aliasPattern = PatternType.Spiral;
                return true;

            case PatternType.CrescentSweep:
                aliasPattern = PatternType.SweepFan;
                return true;

            case PatternType.BraidedStream:
                aliasPattern = PatternType.Spiral;
                return true;

            case PatternType.HaloSpear:
                aliasPattern = PatternType.Ring;
                return true;

            case PatternType.CloseCross:
                aliasPattern = PatternType.BoI_4Way;
                return true;

            case PatternType.EscapeCutoff:
                aliasPattern = PatternType.AimedSingle;
                return true;
        }

        aliasPattern = requestedPattern;
        return false;
    }

    private void FireSingleTick(Vector2 origin, Vector2 baseDir)
    {
        EmitPattern(origin, baseDir);
        ScheduleNextFire(Mathf.Max(MinInterval, intraBurstInterval), "SingleTick");
    }

    private void FireBurstStep(Vector2 origin, Vector2 baseDir)
    {
        if (burstShotsRemaining <= 0)
            burstShotsRemaining = Mathf.Max(1, shotsPerBurst);

        EmitPattern(origin, baseDir);
        burstShotsRemaining--;

        if (burstShotsRemaining > 0)
            ScheduleNextFire(Mathf.Max(MinInterval, intraBurstInterval), "IntraBurst");
        else
        {
            burstsFiredThisEnable++;
            ScheduleNextFire(Mathf.Max(MinInterval, burstCooldown), "BurstCooldown");
        }
    }

    private void EmitPattern(Vector2 origin, Vector2 baseDir)
    {
        lastBlockReason = $"Pattern:{pattern}";
        prettyPatternPulseIndex++;

        switch (pattern)
        {
            case PatternType.AimedSingle:
                SpawnProjectile(origin, baseDir);
                break;
            case PatternType.AimedFan:
                EmitFan(origin, baseDir, Mathf.Max(1, fanBullets), fanArcDegrees);
                break;
            case PatternType.Ring:
                EmitRing(origin, Mathf.Max(3, ringBullets), baseDir);
                break;
            case PatternType.Spiral:
                EmitSpiral(origin, baseDir);
                break;
            case PatternType.BoI_4Way:
                EmitFixedWays(origin, 4);
                break;
            case PatternType.BoI_8Way:
                EmitFixedWays(origin, 8);
                break;
            case PatternType.SweepFan:
                EmitSweepFan(origin, baseDir);
                break;
            case PatternType.PetalFan:
                EmitPetalFan(origin, baseDir);
                break;
            case PatternType.ButterflySpread:
                EmitButterflySpread(origin, baseDir);
                break;
            case PatternType.ClosingBlossom:
                EmitClosingBlossom(origin, baseDir);
                break;
            case PatternType.RotatingFlowerRing:
                EmitRotatingFlowerRing(origin, baseDir);
                break;
            case PatternType.StaggeredRosette:
                EmitStaggeredRosette(origin, baseDir);
                break;
            case PatternType.CrescentSweep:
                EmitCrescentSweep(origin, baseDir);
                break;
            case PatternType.BraidedStream:
                EmitBraidedStream(origin, baseDir);
                break;
            case PatternType.HaloSpear:
                EmitHaloSpear(origin, baseDir);
                break;
            case PatternType.CloseCross:
                EmitCloseCross(origin, baseDir);
                break;
            case PatternType.EscapeCutoff:
                EmitEscapeCutoff(origin, baseDir);
                break;
        }
    }

    private void EmitFan(Vector2 origin, Vector2 baseDir, int count, float arcDeg)
    {
        if (count == 1)
        {
            SpawnProjectile(origin, baseDir);
            return;
        }

        float half = arcDeg * 0.5f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            float ang = Mathf.Lerp(-half, half, t);
            SpawnProjectile(origin, Rotate(baseDir, ang));
        }

        // Guarantee one bullet aimed at the target's CURRENT real position,
        // bypassing aim lag. This closes the dead zone that opens up when fan
        // gaps align with a stationary player, regardless of spread angle.
        if (guaranteeCenterBullet && target != null)
        {
            Vector2 toTarget = (Vector2)target.position - origin;
            if (toTarget.sqrMagnitude > 0.0001f)
                SpawnProjectile(origin, toTarget.normalized);
        }
    }

    private void EmitRing(Vector2 origin, int count, Vector2 baseDir)
    {
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < count; i++)
        {
            float ang = baseAngle + (360f * i / count);
            SpawnProjectile(origin, AngleToDir(ang));
        }
    }

    private void EmitSpiral(Vector2 origin, Vector2 baseDir)
    {
        float baseAngle = spiralCenteredOnAim ? Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg : 0f;
        spiralAngleDeg += angularSpeedDegPerTick;

        int n = Mathf.Max(1, spiralBulletsPerTick);
        for (int i = 0; i < n; i++)
        {
            float step = (n == 1) ? 0f : (360f / n) * i;
            float ang = baseAngle + spiralAngleDeg + step;
            SpawnProjectile(origin, AngleToDir(ang));
        }
    }

    private void EmitFixedWays(Vector2 origin, int ways)
    {
        int n = Mathf.Max(2, ways);
        for (int i = 0; i < n; i++)
        {
            float ang = 360f * i / n;
            SpawnProjectile(origin, AngleToDir(ang));
        }
    }

    private void EmitSweepFan(Vector2 origin, Vector2 baseDir)
    {
        sweepAngleDeg += angularSpeedDegPerTick * sweepDir;

        float maxSweep = Mathf.Max(5f, fanArcDegrees);
        if (sweepAngleDeg > maxSweep * 0.5f) { sweepAngleDeg = maxSweep * 0.5f; sweepDir = -1; }
        if (sweepAngleDeg < -maxSweep * 0.5f) { sweepAngleDeg = -maxSweep * 0.5f; sweepDir = 1; }

        Vector2 centerDir = Rotate(baseDir, sweepAngleDeg);
        EmitFan(origin, centerDir, Mathf.Max(1, fanBullets), fanArcDegrees);
    }

    private void EmitPetalFan(Vector2 origin, Vector2 baseDir)
    {
        int count = Mathf.Max(3, fanBullets);
        float half = Mathf.Max(8f, fanArcDegrees) * 0.5f;

        if (count == 1)
        {
            SpawnProjectile(origin, baseDir);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float t = count == 1
                ? 0f
                : (i / (float)(count - 1)) * 2f - 1f;

            // Curve the spacing so the outer bullets look like petals instead of a flat cone.
            float curved = Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 0.72f);
            float decorativeWave = Mathf.Sin((prettyPatternPulseIndex + i) * 0.85f) * 2.0f;
            float angle = curved * half + decorativeWave;

            SpawnProjectile(origin, Rotate(baseDir, angle));
        }

        if (guaranteeCenterBulletOnPrettyPatterns && guaranteeCenterBullet && target != null)
        {
            Vector2 toTarget = (Vector2)target.position - origin;
            if (toTarget.sqrMagnitude > 0.0001f)
                SpawnProjectile(origin, toTarget.normalized);
        }
    }

    private void EmitButterflySpread(Vector2 origin, Vector2 baseDir)
    {
        int total = Mathf.Max(4, fanBullets);
        int perWing = Mathf.Max(2, total / 2);
        float outer = Mathf.Max(20f, fanArcDegrees * 0.5f);
        float gap = Mathf.Clamp(butterflyCenterGapDegrees, 4f, outer - 2f);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < perWing; i++)
            {
                float t = perWing == 1 ? 0f : i / (float)(perWing - 1);
                float wingCurve = Mathf.Lerp(gap, outer, Mathf.Pow(t, 0.82f));
                float feather = Mathf.Sin((prettyPatternPulseIndex + i) * 0.65f) * 1.5f;
                SpawnProjectile(origin, Rotate(baseDir, side * (wingCurve + feather)));
            }
        }
    }

    private void EmitClosingBlossom(Vector2 origin, Vector2 baseDir)
    {
        int perSide = Mathf.Max(2, fanBullets / 2);
        float outer = Mathf.Max(24f, fanArcDegrees * 0.5f);
        float inner = Mathf.Clamp(outer * 0.25f, 7f, outer - 4f);

        // Two curved petal walls. They visually suggest a gate closing around the player.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < perSide; i++)
            {
                float t = perSide == 1 ? 0f : i / (float)(perSide - 1);
                float angle = Mathf.Lerp(outer, inner, t);
                float petalBend = Mathf.Sin(t * Mathf.PI) * 4.0f;
                SpawnProjectile(origin, Rotate(baseDir, side * (angle + petalBend)));
            }
        }
    }

    private void EmitRotatingFlowerRing(Vector2 origin, Vector2 baseDir)
    {
        int total = Mathf.Clamp(
            Mathf.Max(4, ringBullets),
            4,
            Mathf.Max(4, rotatingFlowerMaxProjectiles)
        );

        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        float rotation = prettyPatternPulseIndex * prettyPatternRotationStep;

        // v4: one clean flower ring instead of multiple bullets per petal.
        // This keeps the pattern pretty without overwhelming basic enemy fights.
        for (int i = 0; i < total; i++)
        {
            float petalWave = Mathf.Sin((i / (float)total) * Mathf.PI * 2f) * 3.0f;
            float angle = baseAngle + rotation + 360f * i / total + petalWave;
            SpawnProjectile(origin, AngleToDir(angle));
        }
    }

    private void EmitStaggeredRosette(Vector2 origin, Vector2 baseDir)
    {
        int count = Mathf.Max(4, fanBullets);
        float half = Mathf.Max(14f, fanArcDegrees) * 0.5f;
        float centerRotation = prettyPatternPulseIndex * prettyPatternRotationStep;
        Vector2 centerDir = Rotate(baseDir, centerRotation);

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (i / (float)(count - 1)) * 2f - 1f;
            float angle = t * half;
            SpawnProjectile(origin, Rotate(centerDir, angle));
        }
    }

    private void EmitCrescentSweep(Vector2 origin, Vector2 baseDir)
    {
        int count = Mathf.Max(4, fanBullets);
        float arc = Mathf.Max(34f, fanArcDegrees);
        float half = arc * 0.5f;
        float sweep = Mathf.Sin(prettyPatternPulseIndex * 0.55f) * Mathf.Min(18f, half * 0.35f);

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (i / (float)(count - 1)) * 2f - 1f;
            float crescent = t * half + Mathf.Sin((t + 1f) * Mathf.PI) * 5f;
            SpawnProjectile(origin, Rotate(baseDir, crescent + sweep));
        }
    }

    private void EmitBraidedStream(Vector2 origin, Vector2 baseDir)
    {
        float wave = Mathf.Sin(prettyPatternPulseIndex * 0.75f) * Mathf.Max(8f, fanArcDegrees * 0.25f);
        float mirror = Mathf.Cos(prettyPatternPulseIndex * 0.75f) * 5f;

        SpawnProjectile(origin, Rotate(baseDir, wave + mirror));
        SpawnProjectile(origin, Rotate(baseDir, -wave + mirror));

        if (fanBullets >= 5)
            SpawnProjectile(origin, baseDir);
    }

    private void EmitHaloSpear(Vector2 origin, Vector2 baseDir)
    {
        int haloCount = Mathf.Clamp(
            Mathf.Max(6, ringBullets),
            6,
            Mathf.Max(6, haloSpearMaxHaloBullets)
        );
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        float rotation = prettyPatternPulseIndex * prettyPatternRotationStep;

        for (int i = 0; i < haloCount; i++)
        {
            float angle = baseAngle + rotation + 360f * i / haloCount;
            SpawnProjectile(origin, AngleToDir(angle));
        }

        StartCoroutine(SpawnDelayedProjectile(origin, baseDir, haloSpearDelay));
    }

    private void EmitCloseCross(Vector2 origin, Vector2 baseDir)
    {
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        float offset = (prettyPatternPulseIndex % 2 == 0) ? 0f : 45f;
        int ways = Mathf.Max(4, ringBullets >= 10 ? 6 : 4);

        for (int i = 0; i < ways; i++)
        {
            float angle = baseAngle + offset + 360f * i / ways;
            SpawnProjectile(origin, AngleToDir(angle));
        }
    }

    private void EmitEscapeCutoff(Vector2 origin, Vector2 baseDir)
    {
        Vector2 cutoffDir = baseDir;

        if (target != null && estimatedTargetVelocity.sqrMagnitude > 0.0001f)
        {
            Vector2 predictedPoint =
                (Vector2)target.position +
                estimatedTargetVelocity.normalized * Mathf.Max(0.1f, escapeCutoffLeadDistance);

            Vector2 toPredicted = predictedPoint - origin;
            if (toPredicted.sqrMagnitude > 0.0001f)
                cutoffDir = toPredicted.normalized;
        }

        SpawnProjectile(origin, cutoffDir);

        if (fanBullets >= 3)
        {
            float narrow = Mathf.Clamp(fanArcDegrees * 0.18f, 6f, 14f);
            SpawnProjectile(origin, Rotate(cutoffDir, narrow));
            SpawnProjectile(origin, Rotate(cutoffDir, -narrow));
        }
    }

    private IEnumerator SpawnDelayedProjectile(Vector2 origin, Vector2 dir, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (!shootingEnabled)
            yield break;

        Vector2 spawnOrigin =
            muzzle != null
                ? (Vector2)muzzle.position
                : origin;

        SpawnProjectile(spawnOrigin, dir);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir)
    {
        lastSuccessfulShotTime = Time.time;

        dir =
            dir.sqrMagnitude > 0.0001f
                ? dir.normalized
                : Vector2.right;

        Vector2 spawnPos = origin + dir * 0.20f;
        Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir);

        GameObject spawned = Instantiate(projectilePrefab, spawnPos, rot);

        Collider2D[] ownerCols = GetComponentsInChildren<Collider2D>(true);
        Collider2D[] projCols = spawned.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerCols.Length; i++)
            for (int j = 0; j < projCols.Length; j++)
                if (ownerCols[i] != null && projCols[j] != null)
                    Physics2D.IgnoreCollision(ownerCols[i], projCols[j], true);

        ApplyProjectileTint(spawned);

        bool hasProjectile =
            spawned.TryGetComponent<Projectile>(
                out Projectile p
            );

        if (hasProjectile)
            p.Initialize(dir, projectileSpeed);

        if (useProjectileStartupMotion)
            AttachProjectileStartupRamp(spawned, dir);

        if (applyVelocityToRigidbody &&
            spawned.TryGetComponent<Rigidbody2D>(
                out Rigidbody2D prb))
        {
            float initialMultiplier =
                useProjectileStartupMotion &&
                useProjectileStartupFade &&
                waitForProjectileFadeBeforeMovement
                    ? 0f
                    : useProjectileStartupMotion
                        ? Mathf.Clamp01(
                            projectileStartupInitialSpeedMultiplier)
                        : 1f;

            Vector2 velocity =
                dir * projectileSpeed * initialMultiplier;

#if UNITY_6000_0_OR_NEWER
            prb.linearVelocity = velocity;
#else
            prb.velocity = velocity;
#endif
        }
    }

    private void ApplyProjectileTint(GameObject spawned)
    {
        if (!allowProjectileTint || !projectileTintEnabled || spawned == null)
            return;

        SpriteRenderer[] renderers =
            spawned.GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].color = projectileTint;
        }
    }

    private void AttachProjectileStartupRamp(
        GameObject spawned,
        Vector2 dir)
    {
        if (spawned == null)
            return;

        float hold =
            Mathf.Max(0f, projectileStartupHoldTime);

        float ramp =
            Mathf.Max(0f, projectileStartupRampDuration);

        if (hold <= 0f && ramp <= 0f && !useProjectileStartupFade)
            return;

        EnemyProjectileStartupRamp startup =
            spawned.GetComponent<EnemyProjectileStartupRamp>();

        if (startup == null)
            startup =
                spawned.AddComponent<EnemyProjectileStartupRamp>();

        startup.Configure(
            dir,
            projectileSpeed,
            hold,
            ramp,
            Mathf.Clamp01(
                projectileStartupInitialSpeedMultiplier),
            Mathf.Max(0.1f, projectileStartupEasePower),
            removeStartupRampWhenReflected,
            useProjectileStartupFade,
            Mathf.Max(0.01f, projectileStartupFadeDuration),
            Mathf.Clamp01(projectileStartupInitialAlpha),
            waitForProjectileFadeBeforeMovement
        );
    }

    private void ResetBurstStateForEnable()
    {
        burstShotsRemaining = 0;
        burstsFiredThisEnable = 0;
    }

    private void ScheduleNextFire(float delay, string reason)
    {
        nextFireTime = Time.time + Mathf.Max(0f, delay);
        cooldownSetBy = reason;
    }

    private void OnDisable()
    {
        CancelActiveAttackTelegraph();
    }

    private void ResolveTarget()
    {
        if (target != null) return;
        if (!autoFindPlayer) return;
        if (Time.time < nextTargetSearchTime) return;

        nextTargetSearchTime = Time.time + TargetSearchInterval;

        GameObject playerObj = null;
        try { playerObj = GameObject.FindWithTag(playerTag); }
        catch (UnityException) { lastBlockReason = $"MissingTag:{playerTag}"; return; }

        if (playerObj != null) { target = playerObj.transform; lastBlockReason = "TargetAcquired"; }
    }

    private void RecordTargetSample()
    {
        if (target == null) return;

        Vector2 currentTargetPos = target.position;

        if (hasLastTargetPosition)
        {
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector2 rawVelocity = (currentTargetPos - lastTargetPosition) / dt;
            estimatedTargetVelocity = Vector2.Lerp(estimatedTargetVelocity, rawVelocity, 0.25f);
        }

        lastTargetPosition = currentTargetPos;
        hasLastTargetPosition = true;

        samples.Add(new TargetSample { time = Time.time, pos = currentTargetPos });

        float keepFrom = Time.time - Mathf.Max(aimLagSeconds + 0.75f, 1.0f);
        int removeCount = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].time < keepFrom) removeCount++;
            else break;
        }
        if (removeCount > 0) samples.RemoveRange(0, removeCount);
    }

    private bool TryGetDelayedAimPoint(out Vector2 aimPoint)
    {
        aimPoint = Vector2.zero;
        if (target == null) return false;

        if (samples.Count == 0) { aimPoint = target.position; return true; }

        float t = Time.time - aimLagSeconds;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            if (samples[i].time <= t) { aimPoint = samples[i].pos; return true; }
        }

        aimPoint = samples[0].pos;
        return true;
    }

    private bool IsLineBlocked(
        Vector2 origin,
        Vector2 point)
    {
        bool hasLos =
            CombatLineOfSight2D.HasLineOfSight(
                this,
                origin,
                point,
                obstacleMask,
                out Collider2D blocker
            );

        debugLosBlocked = !hasLos;

        debugLosHitObject =
            blocker != null
                ? blocker.gameObject.name
                : "None";

        return !hasLos;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad);
        float sn = Mathf.Sin(rad);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs).normalized;
    }

    private static Vector2 AngleToDir(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
    }
}
