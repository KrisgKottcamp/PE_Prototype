using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// EnemyBrain
/// - Squad roles, tactical points, personality, LOS gating, squad pressure escalation.
/// - Zone wandering / miss pressure: micro-repositions within tactical zone to break firing dead zones.
/// - Close-range pattern forcing: BoI_8Way override within closeRangePatternThreshold.
///
/// FIX: Stuck-during-advance escape routing.
/// When the enemy is stuck while executing a pressure-forced fallback chase (geometry trap),
/// instead of looping back into the same blocked advance path, it picks the nearest
/// reachable tactical point as an escape waypoint and navigates there first, then resumes
/// the advance from a better position. The advance intent is preserved throughout.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBrain : MonoBehaviour, IEnemySquadAgent
{
    // ----------------------------
    // Personality (self-contained)
    // ----------------------------
    public enum Personality { Aggressive, ScaredCat, Backstabber, Avenger }
    public enum OpeningAction { None, ChargePlayer, RunToCover, SeekFlank }

    [Serializable]
    public class PersonalityProfile
    {
        public Personality personality = Personality.Aggressive;
        [Header("Opening")]
        public OpeningAction openingAction = OpeningAction.None;
        [Min(0f)] public float openingDuration = 0.9f;
        [Header("Preferred Range")]
        [Min(0f)] public float preferredMinRange = 1.2f;
        [Min(0f)] public float preferredMaxRange = 3.2f;
        [Header("Flee")]
        [Range(0f, 1f)] public float fleeAtHealth01 = 0.35f;
        [Min(0f)] public float panicDistance = 1.3f;
        [Header("Point Bias")]
        public float coverBias = 0f;
        public float flankBias = 0f;
        public float fireBias = 0f;
        [Header("Behavior Gates")]
        public bool mustBeInPreferredRangeToShoot = false;
        public bool preferFlanksAlways = false;
        [Header("Burst Style")]
        [Min(1)] public int shotsPerBurst = 2;
        [Min(0.01f)] public float intraBurstInterval = 0.12f;
        [Min(0.01f)] public float burstCooldown = 0.90f;
        [Min(1)] public int burstsPerEnable = 1;
        [Header("Pattern Names")]
        public string farPattern = "AimedSingle";
        public string midPattern = "AimedFan";
        public string closePattern = "BoI_8Way";
        [Header("Pattern Params")]
        [Min(1)] public int fanBullets = 5;
        [Min(0f)] public float fanArcDegrees = 35f;
        [Min(3)] public int ringBullets = 12;
        [Min(0f)] public float angularSpeedDegPerTick = 12f;
        public PersonalityProfile Clone() => (PersonalityProfile)MemberwiseClone();
    }

    [Header("Personality Assignment")]
    [SerializeField] private bool randomizePersonalityOnEnable = true;
    [SerializeField] private Personality personality = Personality.Aggressive;
    [SerializeField] private float wAggressive = 1.2f;
    [SerializeField] private float wScaredCat = 1.0f;
    [SerializeField] private float wBackstabber = 0.8f;
    [SerializeField] private float wAvenger = 0.6f;

    [Header("Default Personality Profiles")]
    [SerializeField]
    private PersonalityProfile aggressiveProfile = new PersonalityProfile
    {
        personality = Personality.Aggressive,
        openingAction = OpeningAction.ChargePlayer,
        openingDuration = 0.9f,
        preferredMinRange = 0.9f,
        preferredMaxRange = 2.2f,
        fleeAtHealth01 = 0f,
        panicDistance = 0f,
        coverBias = -0.2f,
        flankBias = 0.2f,
        fireBias = 0.3f,
        mustBeInPreferredRangeToShoot = true,
        preferFlanksAlways = false,
        shotsPerBurst = 2,
        intraBurstInterval = 0.11f,
        burstCooldown = 0.75f,
        burstsPerEnable = 1,
        farPattern = "AimedFan",
        midPattern = "AimedFan",
        closePattern = "BoI_8Way",
        fanBullets = 5,
        fanArcDegrees = 28f
    };
    [SerializeField]
    private PersonalityProfile scaredCatProfile = new PersonalityProfile
    {
        personality = Personality.ScaredCat,
        openingAction = OpeningAction.None,
        openingDuration = 1.0f,
        preferredMinRange = 7.0f,
        preferredMaxRange = 12.0f,
        fleeAtHealth01 = 0.20f,
        panicDistance = 3.0f,
        coverBias = -0.1f,
        flankBias = 0.1f,
        fireBias = 1.2f,
        mustBeInPreferredRangeToShoot = true,
        preferFlanksAlways = false,
        shotsPerBurst = 1,
        intraBurstInterval = 0.20f,
        burstCooldown = 1.4f,
        burstsPerEnable = 1,
        farPattern = "AimedSingle",
        midPattern = "AimedSingle",
        closePattern = "AimedFan",
        fanBullets = 4,
        fanArcDegrees = 50f
    };
    [SerializeField]
    private PersonalityProfile backstabberProfile = new PersonalityProfile
    {
        personality = Personality.Backstabber,
        openingAction = OpeningAction.SeekFlank,
        openingDuration = 1.1f,
        preferredMinRange = 2.5f,
        preferredMaxRange = 6.0f,
        fleeAtHealth01 = 0.35f,
        panicDistance = 1.3f,
        coverBias = 0.7f,
        flankBias = 1.2f,
        fireBias = 0.2f,
        mustBeInPreferredRangeToShoot = false,
        preferFlanksAlways = true,
        shotsPerBurst = 2,
        intraBurstInterval = 0.10f,
        burstCooldown = 0.95f,
        burstsPerEnable = 1,
        farPattern = "AimedFan",
        midPattern = "AimedFan",
        closePattern = "AimedFan",
        fanBullets = 3,
        fanArcDegrees = 20f
    };
    [SerializeField]
    private PersonalityProfile avengerProfile = new PersonalityProfile
    {
        personality = Personality.Avenger,
        openingAction = OpeningAction.RunToCover,
        openingDuration = 1.0f,
        preferredMinRange = 4.5f,
        preferredMaxRange = 8.0f,
        fleeAtHealth01 = 0.45f,
        panicDistance = 1.4f,
        coverBias = 0.9f,
        flankBias = -0.2f,
        fireBias = 0.1f,
        mustBeInPreferredRangeToShoot = false,
        preferFlanksAlways = false,
        shotsPerBurst = 2,
        intraBurstInterval = 0.16f,
        burstCooldown = 1.10f,
        burstsPerEnable = 1,
        farPattern = "AimedSingle",
        midPattern = "AimedFan",
        closePattern = "AimedFan",
        fanBullets = 4,
        fanArcDegrees = 50f
    };

    // ----------------------------
    // Core setup
    // ----------------------------
    [Header("Archetype")]
    [SerializeField] private bool isRanged = true;
    [SerializeField] private bool isMelee = false;

    [Header("Core Refs")]
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private EnemyShooterDebug shooter;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private EnemyStunnable stunnable;

    [Header("Player Tracking")]
    [SerializeField] private bool autoFindPlayerByTag = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";
    [SerializeField] private float playerSearchInterval = 0.20f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.8f;
    [SerializeField] private float retreatSpeedMultiplier = 1.05f;
    [SerializeField] private float obstacleProbeDistance = 0.35f;
    [SerializeField] private float stuckCheckInterval = 0.35f;
    [SerializeField] private float stuckMinTravel = 0.05f;

    [Header("Navigation")]
    [SerializeField] private ArenaNavigationGrid navigationGrid;

    [SerializeField, Min(0.05f)]
    private float pathRefreshInterval = 0.35f;

    [SerializeField, Min(0.05f)]
    private float waypointArrivalRadius = 0.18f;

    [Tooltip(
        "How long a tactical point is avoided after this enemy fails to reach it."
    )]
    [SerializeField, Min(0.1f)]
    private float failedPointCooldown = 2.0f;

    [Tooltip(
        "Maximum time movement may make no meaningful progress before replanning."
    )]
    [SerializeField, Min(0.2f)]
    private float progressTimeout = 1.1f;

    [SerializeField, Min(0.01f)]
    private float progressRequired = 0.12f;

    [Tooltip(
        "Failsafe for an enemy stuck in Replan or Move without doing anything useful."
    )]
    [SerializeField, Min(0.5f)]
    private float activityWatchdogSeconds = 2.5f;

    [Header("Planning")]
    [SerializeField] private Vector2 replanIntervalRange = new Vector2(0.18f, 0.34f);
    [SerializeField] private float minPointLockTime = 0.45f;
    [SerializeField] private float replanHardTimeout = 0.40f;
    [SerializeField] private float fallbackArrivalRadius = 0.80f;

    [Header("LOS and Obstacles")]
    [SerializeField] private LayerMask losBlockMask;

    [Header("Strict LOS gate")]
    [SerializeField] private bool strictLosForAnchor = true;
    [SerializeField] private bool strictLosForSuppressor = true;
    [SerializeField] private bool strictLosForAllRanged = false;
    [SerializeField] private float maxNoLosBeforeReplan = 0.35f;

    [Serializable]
    public class RoleShooterTuning
    {
        public float fireInterval = 0.30f;
        public float aimLag = 0.08f;
        public Vector2 settleWindow = new Vector2(0.06f, 0.12f);
        public Vector2 attackWindow = new Vector2(2.0f, 3.0f);
        public float firstShotDelay = 0.00f;
    }

    [Header("Role Shooter Tuning")]
    [SerializeField]
    private RoleShooterTuning suppressorShooter = new RoleShooterTuning
    {
        fireInterval = 0.20f,
        aimLag = 0.04f,
        settleWindow = new Vector2(0.04f, 0.10f),
        attackWindow = new Vector2(2.8f, 4.2f)
    };
    [SerializeField]
    private RoleShooterTuning anchorShooter = new RoleShooterTuning
    {
        fireInterval = 0.28f,
        aimLag = 0.06f,
        settleWindow = new Vector2(0.05f, 0.12f),
        attackWindow = new Vector2(2.4f, 3.4f)
    };
    [SerializeField]
    private RoleShooterTuning flankerShooter = new RoleShooterTuning
    {
        fireInterval = 0.24f,
        aimLag = 0.05f,
        settleWindow = new Vector2(0.05f, 0.12f),
        attackWindow = new Vector2(2.0f, 3.0f),
        firstShotDelay = 0.08f
    };
    [SerializeField]
    private RoleShooterTuning retreaterShooter = new RoleShooterTuning
    {
        fireInterval = 0.42f,
        aimLag = 0.10f,
        settleWindow = new Vector2(0.05f, 0.10f),
        attackWindow = new Vector2(1.3f, 2.0f),
        firstShotDelay = 0.06f
    };

    [Header("Scoring")]
    [SerializeField] private float occupiedPenalty = 1.4f;
    [SerializeField] private float travelCostWeight = 0.25f;
    [SerializeField] private float rangeFitWeight = 1.3f;
    [SerializeField] private float flankWeight = 1.6f;
    [SerializeField] private float fireWeight = 1.9f;
    [SerializeField] private float coverWeight = 0.65f;

    [Header("Backstabber")]
    [SerializeField] private float underFireSeconds = 1.1f;

    // ----------------------------
    // Squad Pressure Response
    // ----------------------------
    [Header("Pressure Response (stalemate escalation)")]
    [SerializeField] private float advancePressureThreshold = 0.65f;
    [SerializeField] private float pressureCoverPenalty = 3.0f;
    [SerializeField] private float pressureFireBonus = 2.5f;

    // ----------------------------
    // Stuck-During-Advance Escape
    // ----------------------------
    [Header("Stuck-During-Advance Escape")]
    [Tooltip("How many consecutive stuck events during a pressure-forced advance before the " +
             "enemy stops trying to path directly and instead routes through a waypoint.")]
    [SerializeField] private int advanceStuckEscapeThreshold = 2;

    [Tooltip("How far from the enemy to search for an escape waypoint when stuck. " +
             "Should be larger than obstacleProbeDistance.")]
    [SerializeField] private float escapeWaypointSearchRadius = 3.5f;

    [Tooltip("Seconds before the escape waypoint attempt is abandoned and the enemy replans normally.")]
    [SerializeField] private float escapeWaypointTimeout = 2.0f;

    // ----------------------------
    // Zone Wandering / Miss Pressure
    // ----------------------------
    [Header("Zone Wander (strafing while shooting)")]
    [SerializeField] private Vector2 wanderIntervalRange = new Vector2(0.9f, 1.8f);
    [SerializeField] private float wanderSpeedMultiplier = 0.45f;
    [Tooltip("Speed multiplier for idle pacing while not shooting (Settle state). " +
             "Should be noticeably slower than wanderSpeedMultiplier to feel relaxed rather than combat-ready.")]
    [SerializeField] private float idleWanderSpeedMultiplier = 0.22f;
    [SerializeField] private float missTimeThreshold = 2.2f;
    [SerializeField] private float playerStationaryThreshold = 0.5f;
    [SerializeField] private int maxRepositionsPerWindow = 2;

    // ----------------------------
    // Close-Range Pattern Override
    // ----------------------------
    [Header("Close-Range Pattern Override")]
    [SerializeField] private float closeRangeOverrideDistance = 2.5f;

    // ----------------------------
    // Aggression / Flow v1
    // ----------------------------
    [Header("Aggression / Flow v1")]
    [Tooltip(
        "If true, EnemyBrain movement pauses while EnemyStunnable.IsStunned. " +
        "Default OFF for Phase 1 so basic attacks do not freeze enemies."
    )]
    [SerializeField] private bool pauseMovementWhileStunned = false;

    [Tooltip(
        "If true, the enemy leaves AttackWindow shortly after its configured burst quota is spent. " +
        "This removes dead air where the shooter is enabled but stuck on BurstQuotaReached."
    )]
    [SerializeField] private bool endAttackWindowWhenBurstQuotaReached = true;

    [SerializeField, Min(0f)] private float postBurstReplanDelay = 0.15f;

    [Header("Continuous Pressure Anti-Idle v2")]
    [Tooltip(
        "If true, a ranged enemy that has just spent its burst quota may quickly re-arm another burst " +
        "instead of leaving AttackWindow and looping through Replan. This prevents aggressive enemies " +
        "from standing near the player after one burst when the squad is trying to overwhelm the player."
    )]
    [SerializeField] private bool continuePressureAfterBurstQuota = true;

    [Tooltip(
        "Minimum squad pressure needed before a burst-spent enemy repeats pressure from the same attack window. " +
        "Close range and urgent threat-gap can also trigger the repeat."
    )]
    [SerializeField, Range(0f, 1f)] private float continuousPressureThreshold = 0.55f;

    [Tooltip("Delay before a pressure-repeat burst becomes eligible. Keep short so enemies do not look idle.")]
    [SerializeField, Min(0f)] private float continuousPressureRearmDelay = 0.22f;

    [Tooltip(
        "If the enemy is this close to the player, it may continue pressure even below the pressure threshold. " +
        "This is the anti-hug safety valve."
    )]
    [SerializeField, Min(0.1f)] private float closeRangePressureRepeatDistance = 3.25f;

    [Tooltip(
        "How many repeated pressure bursts are allowed inside one AttackWindow before the enemy is forced to replan " +
        "for a new pincer/crossfire position. 0 means unlimited repeats until the normal attack window timer expires."
    )]
    [SerializeField, Min(0)] private int maxContinuousPressureBurstsBeforeReplan = 2;

    [Tooltip(
        "Minimum extra time added to AttackWindow when a pressure-repeat burst is queued. " +
        "Prevents the state timer from expiring before the next burst can start."
    )]
    [SerializeField, Min(0.05f)] private float continuousPressureWindowExtension = 0.75f;

    [SerializeField] private bool debugAggressionFlow = false;

    [Header("Burst Reposition Anti-Idle v3")]
    [Tooltip(
        "If true, pressure-repeat shooters do not re-arm while standing still inside AttackWindow. " +
        "After spending a burst, they take a short pincer step first, then fire again. " +
        "This prevents the Replan -> AttackWindow [CHASE] idle loop seen in testing."
    )]
    [SerializeField] private bool moveBetweenPressureBursts = true;

    [Tooltip("Maximum time spent stepping to a fresh pressure angle before firing again anyway.")]
    [SerializeField, Min(0.05f)] private float pressureBurstStepDuration = 0.55f;

    [Tooltip("Minimum movement that counts as a useful pressure step before the enemy can fire again.")]
    [SerializeField, Min(0f)] private float pressureBurstMinStepDistance = 0.55f;

    [Tooltip("If the enemy gets this close to the pressure target, it may fire again even if the timer has not expired.")]
    [SerializeField, Min(0.05f)] private float pressureBurstReadyDistance = 0.85f;

    [Tooltip("If true, the enemy prefers to regain line of sight before ending the pressure step. Timeout still lets it fire/replan.")]
    [SerializeField] private bool pressureStepPrefersLineOfSight = true;

    [SerializeField] private string debugPressureBurstStep = "None";


    [Header("Squad Attack Slots / Rhythm v1")]
    [Tooltip("If true, this ranged enemy must claim a squad attack slot before opening its shooter gate.")]
    [SerializeField] private bool useSquadAttackSlots = true;

    [Tooltip("How often this enemy retries an attack-slot request while waiting in AttackWindow.")]
    [SerializeField, Min(0.03f)] private float attackSlotRetryDelay = 0.12f;

    [Tooltip("If true, squad threat-gap urgency can skip the last bit of Settle and first-shot delay.")]
    [SerializeField] private bool threatGapCanHurryAttack = true;

    [Tooltip("If true, a ranged enemy may stop moving and take an opportunity shot when the squad threat gap is urgent.")]
    [SerializeField] private bool allowOpportunityAttackWhileMoving = true;

    [SerializeField, Min(0.1f)] private float opportunityAttackCooldown = 1.1f;

    [Header("Close-Range Anti-Hug")]
    [Tooltip(
        "If true, a ranged fallback-chasing enemy that reaches close range with LOS stops chasing " +
        "the player's center and immediately opens an attack window instead. This prevents enemies " +
        "from hugging the player without firing."
    )]
    [SerializeField] private bool attackInsteadOfHuggingPlayer = true;

    [Tooltip(
        "Distance at which a fallback-chasing ranged enemy should stop approaching and start its close-range attack. " +
        "Set close to closeRangeOverrideDistance so the enemy uses its close pattern instead of standing on the player."
    )]
    [SerializeField, Min(0.1f)] private float closeAttackStartDistance = 2.35f;

    [Tooltip(
        "Small grace period so the enemy can still start the close attack if LOS was valid a moment ago. " +
        "Helps prevent jitter at obstacle edges."
    )]
    [SerializeField, Min(0f)] private float closeAttackLineOfSightGrace = 0.10f;

    [Tooltip(
        "Delay before the shooter is force-ready after a close-range chase converts into an attack. " +
        "Use 0 for aggressive enemies."
    )]
    [SerializeField, Min(0f)] private float closeAttackForceReadyDelay = 0f;

    [Tooltip(
        "If true, fallback chase skips the normal Settle wait when it reaches close attack range."
    )]
    [SerializeField] private bool skipSettleWhenCloseChasing = true;

    [SerializeField] private string debugAttackSlot = "None";
    [SerializeField] private string debugCloseRangeAntiHug = "None";

    [Header("Pincer Positioning / Escape Denial v1")]
    [Tooltip("If true, this enemy asks the squad coordinator for pincer/crossfire positions instead of blindly chasing the player's center.")]
    [SerializeField] private bool usePincerPositioning = true;

    [Tooltip("If true, pressure/fallback chase moves to a side/crossfire target near the player instead of the exact player position.")]
    [SerializeField] private bool usePincerFallbackChase = true;

    [Tooltip("Preferred distance from the player for generated fallback pincer targets.")]
    [SerializeField, Min(0.5f)] private float pincerFallbackRadius = 2.9f;

    [Tooltip("How often the fallback pincer target refreshes while the player moves.")]
    [SerializeField, Min(0.05f)] private float pincerTargetRefreshInterval = 0.22f;

    [Tooltip("Scoring weight applied to tactical points that create better crossfire / side pressure.")]
    [SerializeField, Min(0f)] private float pincerTacticalPointWeight = 1.15f;

    [Tooltip("Scoring weight applied to micro-reposition candidates that create better crossfire / side pressure.")]
    [SerializeField, Min(0f)] private float pincerMicroRepositionWeight = 1.0f;

    [SerializeField] private string debugPincerPositioning = "None";

    [Header("Enemy Attack Identity / Role Pressure v1")]
    [Tooltip("If true, ranged enemies pick attack patterns and burst settings from their role/personality instead of only distance.")]
    [SerializeField] private bool useRoleSpecificAttackIdentity = true;

    [Header("Role Attack Profiles v4")]
    [Tooltip("Optional inspector-editable role-to-pattern mapping. If assigned, this replaces the hardcoded Suppressor/Flanker/Anchor/Retreater pattern choices while keeping personality and pressure modifiers.")]
    [SerializeField] private EnemyRoleAttackProfiles roleAttackProfiles;

    [Tooltip("If true and Role Attack Profiles is assigned, role pattern choices come from the ScriptableObject instead of the hardcoded Apply___Identity methods.")]
    [SerializeField] private bool useRoleAttackProfiles = true;

    [Tooltip("If true, squad pressure upgrades pattern size, burst count, and cadence as the player lets the squad overwhelm them.")]
    [SerializeField] private bool usePressureAttackIntensity = true;

    [Tooltip("Pressure needed for medium intensity attacks.")]
    [SerializeField, Range(0f, 1f)] private float mediumAttackIntensityPressure = 0.35f;

    [Tooltip("Pressure needed for high intensity attacks.")]
    [SerializeField, Range(0f, 1f)] private float highAttackIntensityPressure = 0.72f;

    [Tooltip("Enemies inside this distance prefer their close-range identity attack immediately.")]
    [SerializeField, Min(0.1f)] private float identityCloseRangeDistance = 2.55f;

    [Tooltip("Extra fan bullets added at high pressure. Kept small so patterns stay readable.")]
    [SerializeField, Min(0)] private int highPressureExtraFanBullets = 1;

    [Tooltip("Extra ring bullets added at high pressure.")]
    [SerializeField, Min(0)] private int highPressureExtraRingBullets = 4;

    [Tooltip("Extra burst shots added at high pressure for roles that are meant to overwhelm.")]
    [SerializeField, Min(0)] private int highPressureExtraBurstShots = 1;

    [Header("Pretty Pattern Density Safety v4")]
    [Tooltip("If true, Touhou-style shape patterns fire as one readable pattern for normal enemies instead of repeating the whole pattern multiple times per burst.")]
    [SerializeField] private bool usePrettyPatternDensitySafety = true;

    [Tooltip("Maximum shots-per-burst for pretty shape patterns when density safety is on. 1 means one full pattern per attack beat.")]
    [SerializeField, Min(1)] private int maxPrettyPatternShotsPerBurst = 1;

    [Tooltip("If false, high pressure can still widen/intensify a pretty pattern, but it will not add extra whole pattern repeats.")]
    [SerializeField] private bool allowHighPressureExtraPrettyPatternBursts = false;

    [Tooltip("If true, high pressure may add a small number of bullets to pretty fan/ring shapes. If false, role profile counts stay exact.")]
    [SerializeField] private bool allowHighPressureExtraPrettyPatternBullets = false;

    [Tooltip("Hard cap for fan-like pretty patterns after all profile/personality/pressure modifiers.")]
    [SerializeField, Min(1)] private int maxPrettyFanBullets = 4;

    [Tooltip("Hard cap for ring-like pretty patterns after all profile/personality/pressure modifiers.")]
    [SerializeField, Min(3)] private int maxPrettyRingBullets = 8;

    [Tooltip("Hard cap for pretty pattern fan arc. Keeps basic enemy patterns from covering too much of the arena.")]
    [SerializeField, Min(1f)] private float maxPrettyFanArcDegrees = 50f;

    [Tooltip("Extra recovery after dense pretty patterns. This makes them feel like fair, readable attack beats instead of bullet spam.")]
    [SerializeField, Min(0f)] private float prettyPatternExtraPunishDelay = 0.08f;

    [Tooltip("Minimum delay before a pretty pattern can be pressure-rearmed. Prevents basic enemies from chaining decorative patterns too rapidly.")]
    [SerializeField, Min(0f)] private float minPrettyPatternPressureRearmDelay = 0.24f;

    [Tooltip("Minimum delay before enemies replan/rearm after a pretty pattern.")]
    [SerializeField, Min(0f)] private float minPrettyPatternPostBurstDelay = 0.26f;

    [Header("Pattern Volley Pacing v5")]
    [Tooltip(
        "If true, decorative patterns fire in short volleys instead of being pressure-rearmed forever. " +
        "A basic enemy can throw out 1-3 pattern beats, rarely 4, then takes a small recovery beat."
    )]
    [SerializeField] private bool usePatternVolleyPacing = true;

    [Tooltip("If true, only pretty/danmaku shape patterns use this volley pacing. Simple aimed shots stay snappy.")]
    [SerializeField] private bool patternVolleyOnlyForPrettyPatterns = true;

    [Tooltip("Minimum number of full pattern beats before the enemy cools down.")]
    [SerializeField, Min(1)] private int minPatternVolleyBeats = 1;

    [Tooltip("Maximum normal number of full pattern beats before cooldown. Recommended 3 for basic enemies.")]
    [SerializeField, Min(1)] private int maxPatternVolleyBeats = 3;

    [Tooltip("Chance that the enemy gets one extra surprise pattern beat beyond the normal maximum.")]
    [SerializeField, Range(0f, 1f)] private float rareExtraPatternBeatChance = 0.14f;

    [Tooltip("Minimum delay between pattern beats inside one volley. Higher = less spammy.")]
    [SerializeField, Min(0f)] private float patternVolleyRearmDelay = 0.36f;

    [Tooltip("Cooldown after a normal pretty-pattern volley finishes.")]
    [SerializeField, Min(0f)] private float patternVolleyCooldownAfter = 0.62f;

    [Tooltip("Cooldown after heavier ring/halo/blossom style volleys finish.")]
    [SerializeField, Min(0f)] private float heavyPatternVolleyCooldownAfter = 0.78f;

    [Tooltip("If false, single aimed shots are never limited by decorative-pattern volley pacing.")]
    [SerializeField] private bool countSingleShotsAsPatternVolley = false;

    [SerializeField] private string debugPatternVolleyPacing = "None";

    [Header("Solo Enemy Anti-Idle v6")]
    [Tooltip("If true, a lone ranged enemy uses a simpler duelist loop instead of squad pincer pressure-step logic. This prevents solo enemies from getting stuck in Replan/AttackWindow/Move loops with no visible action.")]
    [SerializeField] private bool useSoloEnemyAntiIdle = true;

    [Tooltip("If true, solo enemies do not take the pressure-burst reposition step between repeated volleys. They cool down briefly, then attack again if they still have line of sight.")]
    [SerializeField] private bool soloBypassPressureBurstStep = true;

    [Tooltip("If a solo enemy is in a pressure-step move but cannot find a path, it immediately attacks if it has line of sight instead of replanning forever.")]
    [SerializeField] private bool soloAttackWhenPressureStepFails = true;

    [Tooltip("Minimum rearm delay between solo pressure beats. This is still combined with pattern volley pacing, so pretty patterns get their normal cooldown rhythm.")]
    [SerializeField, Min(0f)] private float soloPressureRearmDelay = 0.42f;

    [Tooltip("After a solo enemy finishes a full pattern volley, it waits at least this long before opening a new attack window if it still has line of sight.")]
    [SerializeField, Min(0f)] private float soloVolleyCooldownAfter = 0.70f;

    [Tooltip("If the solo enemy is farther than this, it is allowed to replan/chase instead of standing still to shoot.")]
    [SerializeField, Min(0.5f)] private float soloMaxImmediateAttackDistance = 8.0f;

    [SerializeField] private string debugSoloAntiIdle = "None";

    [Header("Point-Blank Skirmish Anti-Stuck v7")]
    [Tooltip("If true, a ranged enemy that is too close to the player moves sideways/backward during volley cooldown instead of standing on top of the player.")]
    [SerializeField] private bool usePointBlankSkirmishAntiStuck = true;

    [Tooltip("If the enemy is closer than this while cooling down after a pattern, it will try to sidestep/backstep while waiting.")]
    [SerializeField, Min(0.1f)] private float pointBlankSkirmishDistance = 2.15f;

    [Tooltip("Desired distance from the player for the small cooldown skirmish step.")]
    [SerializeField, Min(0.2f)] private float pointBlankSkirmishTargetDistance = 2.85f;

    [Tooltip("How much sideways motion is blended into the backstep. 0 = pure backstep, 1 = mostly sidestep.")]
    [SerializeField, Range(0f, 1f)] private float pointBlankSkirmishSideWeight = 0.45f;

    [Tooltip("Movement speed multiplier used during the small skirmish step between pattern volleys.")]
    [SerializeField, Min(0.05f)] private float pointBlankSkirmishSpeedMultiplier = 0.82f;

    [Tooltip("If true, skirmish movement is allowed during the pattern-volley cooldown wait.")]
    [SerializeField] private bool pointBlankSkirmishDuringVolleyCooldown = true;

    [Tooltip("If true, a close-range enemy with LOS attacks instead of replanning when a pressure/pincer move returns No Path.")]
    [SerializeField] private bool pointBlankFireImmediatelyIfNoPath = true;

    [Tooltip("Small delay before the forced no-path close attack arms the shooter.")]
    [SerializeField, Min(0f)] private float noPathImmediateAttackDelay = 0.08f;

    [SerializeField] private string debugPointBlankSkirmish = "None";

    [Tooltip("Minimum delay after dangerous attacks before the enemy replans/rearms. This creates a readable punish window.")]
    [SerializeField, Min(0.02f)] private float dangerousAttackPunishDelay = 0.34f;

    [Tooltip("Quick flanker/single-shot recovery delay. Makes side pressure feel snappy without every attack feeling heavy.")]
    [SerializeField, Min(0.02f)] private float quickAttackPunishDelay = 0.10f;

    [SerializeField] private string debugAttackIdentity = "None";

    [Header("Opportunity Flanking / Player Attention v8")]
    [Tooltip("If true, ranged enemies can exploit the player focusing another enemy by taking a committed flank route before attacking.")]
    [SerializeField] private bool useOpportunityFlanking = true;

    [Tooltip("If true, this enemy only starts opportunity flanks when the squad reports that the player is distracted by another enemy.")]
    [SerializeField] private bool opportunityFlankRequiresPlayerFocus = true;

    [Tooltip("If true, only FlankerLeft/FlankerRight roles start opportunity flanks. Leave off if suppressors/backstabbers may also exploit openings.")]
    [SerializeField] private bool opportunityFlankOnlyForFlankerRoles = false;

    [Tooltip("How long before this enemy may start another committed opportunity flank.")]
    [SerializeField, Min(0.1f)] private float opportunityFlankCooldown = 2.4f;

    [Tooltip("How far beyond the player, opposite the currently-focused enemy, the flanker tries to move.")]
    [SerializeField, Min(0.5f)] private float opportunityFlankBehindDistance = 3.3f;

    [Tooltip("Additional side offset applied to the flank target so it attacks from a real angle instead of a straight line.")]
    [SerializeField, Min(0f)] private float opportunityFlankSideDistance = 1.35f;

    [Tooltip("Fallback radius used when the focus/player geometry is too tight.")]
    [SerializeField, Min(0.5f)] private float opportunityFlankFallbackRadius = 3.0f;

    [Tooltip("Do not choose flank targets closer than this to the player.")]
    [SerializeField, Min(0.25f)] private float opportunityFlankMinPlayerDistance = 2.2f;

    [Tooltip("Do not choose flank targets farther than this from the player.")]
    [SerializeField, Min(1f)] private float opportunityFlankMaxPlayerDistance = 6.6f;

    [Tooltip("How close the enemy must get to the flank target before it ambushes.")]
    [SerializeField, Min(0.05f)] private float opportunityFlankArrivalRadius = 0.45f;

    [Tooltip("Maximum seconds the enemy may spend on the committed flank before giving up or attacking if it has line of sight.")]
    [SerializeField, Min(0.25f)] private float opportunityFlankMaxCommitSeconds = 2.35f;

    [Tooltip("Movement speed multiplier while executing a committed flank route.")]
    [SerializeField, Min(0.1f)] private float opportunityFlankSpeedMultiplier = 1.05f;

    [Tooltip("If true, final flank targets must have line of sight to the player before they are accepted.")]
    [SerializeField] private bool opportunityFlankRequiresDestinationLos = true;

    [Tooltip("If true, the enemy attacks immediately after reaching a flank instead of going through normal settle timing.")]
    [SerializeField] private bool opportunityFlankAttackImmediatelyOnArrival = true;

    [Tooltip("Small delay before the ambush shot after the flank arrives. Keep low so the flank feels intentional.")]
    [SerializeField, Min(0f)] private float opportunityFlankAmbushDelay = 0.06f;

    [Tooltip("How much squad pincer scoring affects opportunity flank target choice.")]
    [SerializeField, Min(0f)] private float opportunityFlankPincerScoreWeight = 1.35f;

    [SerializeField] private string debugOpportunityFlank = "None";

    [Header("Pretty Danmaku Pattern Selection v2")]
    [Tooltip("If true, role identity prefers prettier danmaku-style patterns such as PetalFan, ButterflySpread, ClosingBlossom, RotatingFlowerRing, and HaloSpear.")]
    [SerializeField] private bool usePrettyDanmakuPatterns = true;

    [Tooltip("If true, projectile sprites are tinted by role/personality for better Touhou-style readability.")]
    [SerializeField] private bool useRoleProjectileTint = true;

    [SerializeField] private Color suppressorTint = new Color(1.0f, 0.52f, 0.16f, 1f);
    [SerializeField] private Color flankerTint = new Color(1.0f, 0.22f, 0.85f, 1f);
    [SerializeField] private Color anchorTint = new Color(0.25f, 0.86f, 1.0f, 1f);
    [SerializeField] private Color aggressiveTint = new Color(1.0f, 0.12f, 0.20f, 1f);
    [SerializeField] private Color retreaterTint = new Color(0.75f, 0.78f, 1.0f, 1f);


    // ----------------------------
    // Debug
    // ----------------------------
    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;
    [SerializeField] private string debugState = "None";
    [SerializeField] private Personality effectivePersonality;
    [SerializeField] private EnemySquadRole currentRole = EnemySquadRole.None;
    [SerializeField] private bool usingFallbackChase = false;
    [SerializeField] private CombatTacticalPoint currentPoint;
    [SerializeField] private float squadPressure01 = 0f;
    [SerializeField] private float debugInWindowTime = 0f;
    [SerializeField] private int debugRepositionsThisWindow = 0;
    [SerializeField] private string debugAdvanceStuck = "";
    [SerializeField] private string debugPathStatus = "No Path";
    [SerializeField] private int debugPathWaypoints = 0;
    [SerializeField] private string debugLastMeaningfulAction = "None";
    [SerializeField] private int debugFailedPointCount = 0;
    [SerializeField] private string debugAggressionState = "None";

    // ----------------------------
    // Private runtime state
    // ----------------------------
    private PersonalityProfile profile;
    private EnemySquadCoordinator squad;
    private Transform playerTransform;
    private Transform lastSyncedShooterTarget;
    private bool hasSharedPlayerPos = false;
    private Vector2 sharedPlayerPos;
    private float nextPlayerSearchTime = 0f;
    private float nextReplanTime = 0f;
    private float replanEnteredTime = 0f;
    private float pointLockedUntil = 0f;
    private float nextStateTime = 0f;
    private float attackEnableTime = 0f;
    private float nextStuckCheckTime = 0f;
    private Vector2 lastStuckPos;
    private Vector2 desiredVelocity;
    private bool hasMoveIntent = false;
    private int avoidSideSign = 0;
    private float avoidSideUntil = 0f;
    private bool shooterGateEnabled = false;
    private bool ownsSquadAttackSlot = false;
    private float nextSquadAttackSlotRequestTime = 0f;
    private float nextOpportunityAttackTime = 0f;
    private float lastCloseAttackLosTime = -999f;
    private Vector2 cachedPincerChaseTarget;
    private float nextPincerTargetRefreshTime = 0f;
    private bool hasCachedPincerTarget = false;
    private bool shooterArmedThisWindow = false;
    private bool burstQuotaFinishQueued = false;
    private float burstQuotaFinishTime = 0f;
    private int continuousPressureBurstsThisWindow = 0;
    private float currentPostBurstReplanDelay = 0.15f;
    private float currentContinuousPressureRearmDelay = 0.22f;
    private bool currentPatternUsesVolleyPacing = false;
    private int currentPatternVolleyLimit = 1;
    private int currentPatternVolleyBeatsFired = 0;
    private float currentPatternVolleyCooldownDelay = 0.62f;
    private string currentPatternVolleyPatternName = "None";
    private bool pressureBurstStepQueued = false;
    private Vector2 pressureBurstStepStart;
    private float pressureBurstStepEarliestFireTime = 0f;
    private float pressureBurstStepDeadline = 0f;
    private float noLosTimer = 0f;
    private float openingUntil = 0f;
    private float underFireUntil = 0f;
    private float nextAvengerCheckTime = 0f;

    // Phase 8 opportunity flanking
    private bool opportunityFlankActive = false;
    private Vector2 opportunityFlankTarget;
    private float opportunityFlankStartedTime = -999f;
    private float nextOpportunityFlankAllowedTime = 0f;
    private string opportunityFlankReason = "None";

    // Stuck-during-advance tracking
    private int advanceStuckCount = 0;
    private bool usingEscapeWaypoint = false;
    private Vector2 escapeWaypointTarget;
    private float escapeWaypointStartTime = 0f;

    // Zone wander / miss pressure
    private float inWindowTime = 0f;
    private int repositionsThisWindow = 0;
    private bool isRepositioning = false;
    private Vector2 repositionTarget;
    private float repositionStartTime = 0f;
    private Vector2 lastPlayerPosForMiss;
    private bool playerPosForMissInitialized = false;

    // In-window strafing
    private bool isStrafeMoving = false;
    private Vector2 strafeTarget;
    private float nextStrafePickTime = 0f;

    // Navigation and progress
    private readonly List<Vector2> navigationPath =
        new List<Vector2>();

    private int navigationPathIndex = 0;
    private Vector2 navigationDestination;
    private bool hasNavigationDestination = false;
    private float nextPathRefreshTime = 0f;

    private float lastProgressTime = 0f;
    private float bestRemainingDistance =
        float.PositiveInfinity;

    private float lastMeaningfulActionTime = 0f;
    private string lastMeaningfulActionReason = "Spawn";

    private readonly Dictionary<
        CombatTacticalPoint,
        float
    > failedPointUntil =
        new Dictionary<
            CombatTacticalPoint,
            float
        >();

    private enum BrainState { Replan, Move, Settle, AttackWindow }
    private BrainState state = BrainState.Replan;

    // ----------------------------
    // IEnemySquadAgent
    // ----------------------------
    public Transform Transform => transform;
    public bool IsAlive => GetCurrentHP() > 0;
    public float Health01 => GetHealth01();
    public bool IsRanged => isRanged;
    public bool IsMelee => isMelee;
    public EnemySquadRole CurrentRole => currentRole;
    public bool HasLineOfSightToPlayer =>
        HasLineOfSightToPlayerNow();
    public float LastMeaningfulActionTime =>
        lastMeaningfulActionTime;
    public bool IsMovingWithPurpose =>
        state == BrainState.Move &&
        hasMoveIntent;

    // ----------------------------
    // Unity
    // ----------------------------
    private void Awake()
    {
        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (shooter == null)
            shooter = GetComponent<EnemyShooterDebug>();

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (stunnable == null)
            stunnable = GetComponent<EnemyStunnable>();

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<
                    ArenaNavigationGrid
                >(true);
        }
    }

    private void OnEnable()
    {
        squad = FindObjectOfType<EnemySquadCoordinator>(true);
        if (squad != null) squad.Register(this);

        personality = ReadExternalPersonalityOrFallback(personality);
        if (randomizePersonalityOnEnable && !HasExternalPersonalityState())
            personality = RollRandomPersonality();

        effectivePersonality = personality;
        profile = GetProfileFor(effectivePersonality);
        openingUntil = Time.time + Mathf.Max(0f, profile.openingDuration);
        underFireUntil = 0f;
        squadPressure01 = 0f;
        lastStuckPos = transform.position;

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<
                    ArenaNavigationGrid
                >(true);
        }

        failedPointUntil.Clear();
        ClearNavigationPath();

        lastMeaningfulActionTime =
            Time.time;

        lastMeaningfulActionReason =
            "Enabled";

        ResetAdvanceStuck();
        ResetRepositionState();
        ResetBurstQuotaFinish();
        SetShooterGate(false, force: true);
        ClearMoveIntent();
        EnterReplan(true);
    }

    private void OnDisable()
    {
        ReleaseSquadAttackSlot("Disabled");
        if (squad != null) squad.Unregister(this);
        ReleaseCurrentPoint();
        SetShooterGate(false, force: true);
        ClearMoveIntent();
    }

    private void Update()
    {
        if (!IsAlive)
        {
            SetShooterGate(false);
            ClearMoveIntent();
            return;
        }

        ResolvePlayerTransform();
        SyncShooterTarget();

        if (effectivePersonality == Personality.Avenger && Time.time >= nextAvengerCheckTime)
        {
            nextAvengerCheckTime = Time.time + 0.25f;
            if (IsLastEnemyAlive())
            {
                effectivePersonality = Personality.Aggressive;
                profile = GetProfileFor(effectivePersonality);
                openingUntil = Time.time + 0.6f;
                EnterReplan(true);
            }
        }

        switch (state)
        {
            case BrainState.Replan: TickReplan(); break;
            case BrainState.Move: TickMove(); break;
            case BrainState.Settle: TickSettle(); break;
            case BrainState.AttackWindow: TickAttackWindow(); break;
        }

        debugState = state.ToString()
            + (usingFallbackChase ? " [CHASE]" : "")
            + (usingEscapeWaypoint ? " [ESCAPE]" : "")
            + (opportunityFlankActive ? " [OPPORTUNITY FLANK]" : "")
            + (isRepositioning ? " [REPOSITION]" : "");
        CleanupFailedPointMemory();

        if (playerTransform != null &&
            (state == BrainState.Move ||
             state == BrainState.Replan) &&
            Time.time -
            lastMeaningfulActionTime >=
            activityWatchdogSeconds)
        {
            MarkCurrentPointFailed();
            ReleaseCurrentPoint();
            ClearNavigationPath();
            MarkMeaningfulAction(
                "ActivityWatchdog"
            );
            EnterReplan(true);
        }

        debugInWindowTime = inWindowTime;
        debugRepositionsThisWindow = repositionsThisWindow;
        debugAdvanceStuck =
            advanceStuckCount > 0
                ? $"stuck x{advanceStuckCount}"
                : "";

        debugPathWaypoints =
            Mathf.Max(
                0,
                navigationPath.Count -
                navigationPathIndex
            );

        debugLastMeaningfulAction =
            $"{lastMeaningfulActionReason} " +
            $"{Time.time - lastMeaningfulActionTime:0.0}s ago";

        debugFailedPointCount =
            failedPointUntil.Count;

        if (!useSoloEnemyAntiIdle)
            debugSoloAntiIdle = "Off";
        else if (!IsSoloEnemyForAntiIdle())
            debugSoloAntiIdle = squad != null ? $"Squad size {squad.AliveAgentCount}" : "No coordinator";
    }

    private void FixedUpdate()
    {
        if (pauseMovementWhileStunned &&
            stunnable != null &&
            stunnable.IsStunned)
        {
            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector2.zero;
#else
                rb.velocity = Vector2.zero;
#endif
            }
            return;
        }

        if (!hasMoveIntent)
        {
            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector2.zero;
#else
                rb.velocity = Vector2.zero;
#endif
            }
            return;
        }

        SpeedModifier speedModifier = null;

        if (enemyHealth != null)
        {
            speedModifier =
                enemyHealth.GetComponent<
                    SpeedModifier
                >();
        }

        if (speedModifier == null)
        {
            speedModifier =
                GetComponentInParent<
                    SpeedModifier
                >();
        }

        float speedMultiplier =
            speedModifier != null
                ? speedModifier.Multiplier
                : 1f;

        if (rb != null)
        {
            rb.MovePosition(
                rb.position +
                desiredVelocity *
                speedMultiplier *
                Time.fixedDeltaTime
            );
        }
        else
        {
            transform.position +=
                (Vector3)(
                    desiredVelocity *
                    speedMultiplier *
                    Time.fixedDeltaTime
                );
        }
    }

    // ----------------------------
    // IEnemySquadAgent public hooks
    // ----------------------------
    public void SetRole(
        EnemySquadRole role)
    {
        if (currentRole == role)
            return;

        currentRole = role;
        hasCachedPincerTarget = false;

        if (!isActiveAndEnabled)
            return;

        if (currentPoint != null &&
            !IsPointCompatibleWithRole(
                currentPoint,
                role))
        {
            ReleaseCurrentPoint();
        }

        ClearNavigationPath();
        MarkMeaningfulAction(
            $"Role:{role}"
        );

        EnterReplan(true);
    }

    public void SetSharedPlayerPosition(Vector2 pos)
    {
        sharedPlayerPos = pos;
        hasSharedPlayerPos = true;
    }

    public void NotifySquadPressure(float pressure01)
    {
        squadPressure01 = Mathf.Clamp01(pressure01);

        if (squadPressure01 >= advancePressureThreshold)
        {
            if ((state == BrainState.AttackWindow || state == BrainState.Settle) && !shooterArmedThisWindow)
                EnterReplan(true);
        }
    }

    public void NotifyDamaged()
    {
        underFireUntil = Time.time + Mathf.Max(0.05f, underFireSeconds);

        // Phase 8: damage usually means the player is focusing this enemy.
        // Allies can exploit that attention with committed opportunity flanks.
        if (squad != null)
            squad.NotifyAgentDamagedByPlayer(this);
    }

    // ----------------------------
    // State machine
    // ----------------------------
    private void EnterReplan(bool immediate)
    {
        state = BrainState.Replan;
        replanEnteredTime = Time.time;
        usingFallbackChase = false;
        shooterArmedThisWindow = false;
        ResetBurstQuotaFinish();
        pressureBurstStepQueued = false;
        debugPressureBurstStep = "None";
        noLosTimer = 0f;
        isRepositioning = false;
        CancelOpportunityFlank("Replan", applyCooldown: false);
        SetShooterGate(false);
        ClearMoveIntent();
        ClearNavigationPath();
        hasCachedPincerTarget = false;

        nextReplanTime = immediate
            ? Time.time
            : Time.time + UnityEngine.Random.Range(replanIntervalRange.x, replanIntervalRange.y);
    }

    private void TickReplan()
    {
        bool hardTimedOut = (Time.time - replanEnteredTime) >= replanHardTimeout;
        if (!hardTimedOut && Time.time < nextReplanTime) return;

        bool panicFight;
        bool shouldFlee = ShouldFlee(out panicFight);

        if (IsInOpening() && profile.openingAction == OpeningAction.ChargePlayer)
        {
            BeginFallbackChase();
            return;
        }

        if (TryStartOpportunityFlank())
            return;

        // Full-pressure advance — but only if we haven't been stuck too many times already.
        // If stuck repeatedly, drop to waypoint escape mode instead of looping.
        if (squadPressure01 >= advancePressureThreshold && currentRole != EnemySquadRole.Retreater)
        {
            if (advanceStuckCount < advanceStuckEscapeThreshold)
            {
                // Normal pressure advance — go straight at the player
                ReleaseCurrentPoint();
                BeginFallbackChase();
                return;
            }
            else
            {
                // Stuck too many times chasing directly — route through an escape waypoint.
                // The enemy navigates to a clear nearby tactical point first,
                // then the stuckCount resets and the advance resumes from a better position.
                if (!usingEscapeWaypoint)
                {
                    Vector2 waypointPos;
                    if (TryFindEscapeWaypoint(out waypointPos))
                    {
                        usingEscapeWaypoint = true;
                        escapeWaypointTarget = waypointPos;
                        escapeWaypointStartTime = Time.time;
                        ReleaseCurrentPoint();
                        usingFallbackChase = false;
                        state = BrainState.Move;
                        nextStuckCheckTime = Time.time + stuckCheckInterval;
                        lastStuckPos = transform.position;
                        return;
                    }
                    else
                    {
                        // No escape waypoint found — reset stuck count and try the advance
                        // again anyway (better than doing nothing)
                        ResetAdvanceStuck();
                        ReleaseCurrentPoint();
                        BeginFallbackChase();
                        return;
                    }
                }
            }
        }

        // Normal tactical point selection
        CombatTacticalPoint best = PickBestPointForRole(shouldFlee, panicFight);

        if (best != null)
        {
            if (currentPoint != null && Time.time < pointLockedUntil && currentPoint.CanBeUsedBy(this, Time.time))
            {
                state = BrainState.Move;
                nextStuckCheckTime =
                    Time.time +
                    stuckCheckInterval;

                PrepareMovementProgress(
                    currentPoint.Position
                );

                return;
            }

            if (currentPoint != best)
            {
                ReleaseCurrentPoint();
                if (!best.TryReserve(this, Time.time))
                {
                    nextReplanTime = Time.time + 0.08f;
                    return;
                }
                currentPoint = best;

                pointLockedUntil =
                    Time.time +
                    minPointLockTime;

                MarkMeaningfulAction(
                    $"Reserved:{best.name}"
                );
            }

            usingFallbackChase = false;
            state = BrainState.Move;

            nextStuckCheckTime =
                Time.time +
                stuckCheckInterval;

            lastStuckPos =
                transform.position;

            PrepareMovementProgress(
                currentPoint.Position
            );

            return;
        }

        BeginFallbackChase();
    }

    private void BeginFallbackChase()
    {
        CancelOpportunityFlank("FallbackChase", applyCooldown: false);
        ReleaseCurrentPoint();
        ClearNavigationPath();

        usingFallbackChase = true;
        shooterArmedThisWindow = false;
        state = BrainState.Move;

        nextStuckCheckTime =
            Time.time +
            stuckCheckInterval;

        lastStuckPos =
            transform.position;

        PrepareMovementProgress(
            GetFallbackChaseTarget()
        );

        MarkMeaningfulAction(
            "FallbackChase"
        );
    }

    private void TickMove()
    {
        // --- Escape waypoint handling ---
        if (usingEscapeWaypoint)
        {
            // Timeout guard
            if (Time.time - escapeWaypointStartTime > escapeWaypointTimeout)
            {
                usingEscapeWaypoint = false;
                ResetAdvanceStuck();
                EnterReplan(true);
                return;
            }

            Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
            float dist = Vector2.Distance(pos, escapeWaypointTarget);

            if (dist <= fallbackArrivalRadius)
            {
                // Reached the escape waypoint — reset stuck count, resume advance
                usingEscapeWaypoint = false;
                ResetAdvanceStuck();
                EnterReplan(true); // will now pick fallback chase from a better position
                return;
            }

            if (!MoveTowardTarget(
                    escapeWaypointTarget,
                    moveSpeed))
            {
                usingEscapeWaypoint = false;
                ResetAdvanceStuck();
                pointLockedUntil = 0f;
                EnterReplan(false);
                return;
            }

            if (HasMovementProgressTimedOut(
                    escapeWaypointTarget))
            {
                usingEscapeWaypoint = false;
                ResetAdvanceStuck();
                pointLockedUntil = 0f;
                ClearNavigationPath();
                EnterReplan(false);
                return;
            }
            return;
        }

        // --- Normal move ---
        Vector2 playerPos = GetPlayerPos();
        Vector2 target;
        float arrivalRadius;

        if (opportunityFlankActive)
        {
            target = opportunityFlankTarget;
            arrivalRadius = Mathf.Max(0.05f, opportunityFlankArrivalRadius);
        }
        else if (usingFallbackChase || currentPoint == null)
        {
            target = GetFallbackChaseTarget();
            arrivalRadius = fallbackArrivalRadius;
        }
        else
        {
            target = currentPoint.Position;
            arrivalRadius = Mathf.Max(0.08f, currentPoint.arrivalRadius);
        }

        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;

        UpdateCloseAttackLosMemory();

        if (opportunityFlankActive &&
            Time.time - opportunityFlankStartedTime >= opportunityFlankMaxCommitSeconds)
        {
            if (HasLineOfSightToPlayerNow())
            {
                CompleteOpportunityFlank("TimeoutAttack");
                return;
            }

            FailOpportunityFlank("TimeoutNoLOS");
            return;
        }

        if (TryFinishPressureBurstStep(myPos, target))
            return;

        if (!opportunityFlankActive &&
            !pressureBurstStepQueued &&
            TryStartCloseRangeAttackInsteadOfHugging(myPos, playerPos))
            return;

        if (!opportunityFlankActive &&
            !pressureBurstStepQueued &&
            TryStartOpportunityAttackWhileMoving())
            return;

        if (Vector2.Distance(myPos, target) <= arrivalRadius)
        {
            if (pressureBurstStepQueued)
            {
                ClearMoveIntent();

                if (Time.time >= pressureBurstStepEarliestFireTime)
                {
                    pressureBurstStepDeadline = Time.time;
                    if (TryFinishPressureBurstStep(myPos, target))
                        return;
                }

                debugPressureBurstStep =
                    "Reached pressure target; waiting rearm.";

                return;
            }

            if (opportunityFlankActive)
            {
                CompleteOpportunityFlank("Arrived");
                return;
            }

            ClearMoveIntent();
            // Arrived at player position during fallback chase — reset advance stuck since
            // we successfully navigated there
            if (usingFallbackChase)
                ResetAdvanceStuck();

            MarkMeaningfulAction(
                "ReachedDestination"
            );

            ClearNavigationPath();
            EnterSettle();
            return;
        }

        float speed = moveSpeed;
        if (currentRole == EnemySquadRole.Retreater) speed *= retreatSpeedMultiplier;
        if (opportunityFlankActive) speed *= opportunityFlankSpeedMultiplier;
        if (!MoveTowardTarget(
                target,
                speed))
        {
            if (opportunityFlankActive)
            {
                FailOpportunityFlank("NoPath");
                return;
            }

            if (TrySoloFailOpenPressureStepToAttack("Pressure step no path") ||
                TryPointBlankFailOpenToAttack("No path close attack"))
                return;

            pointLockedUntil = 0f;

            if (currentPoint != null)
            {
                MarkCurrentPointFailed();
                ReleaseCurrentPoint();
            }

            if (usingFallbackChase &&
                squadPressure01 >=
                advancePressureThreshold)
            {
                advanceStuckCount++;
            }

            EnterReplan(true);
            return;
        }

        if (HasMovementProgressTimedOut(
                target))
        {
            if (opportunityFlankActive)
            {
                FailOpportunityFlank("ProgressTimeout");
                return;
            }

            if (TrySoloFailOpenPressureStepToAttack("Pressure step progress timeout") ||
                TryPointBlankFailOpenToAttack("Progress timeout close attack"))
                return;

            pointLockedUntil = 0f;

            if (currentPoint != null)
            {
                MarkCurrentPointFailed();
                ReleaseCurrentPoint();
            }

            if (usingFallbackChase &&
                squadPressure01 >=
                advancePressureThreshold)
            {
                advanceStuckCount++;
            }

            ClearNavigationPath();
            MarkMeaningfulAction(
                "ProgressTimeout"
            );

            EnterReplan(true);
            return;
        }
    }

    private void EnterSettle()
    {
        state = BrainState.Settle;
        shooterArmedThisWindow = false;
        noLosTimer = 0f;
        ClearMoveIntent();
        SetShooterGate(false);
        RoleShooterTuning t = GetRoleTuning();
        nextStateTime = Time.time + UnityEngine.Random.Range(t.settleWindow.x, t.settleWindow.y);
    }

    private void TickSettle()
    {
        if (threatGapCanHurryAttack && squad != null && squad.ShouldForceAttack(this) && HasLineOfSightToPlayerNow())
        {
            EnterAttackWindow();
            attackEnableTime = Time.time;
            return;
        }

        if (Time.time >= nextStateTime) EnterAttackWindow();

        // Pace within the zone while waiting to enter attack window.
        // Shooter is off during settle — this is purely movement.
        TickStrafe(idleWanderSpeedMultiplier);
    }

    private void EnterAttackWindow()
    {
        state = BrainState.AttackWindow;
        pressureBurstStepQueued = false;
        debugPressureBurstStep = "None";
        shooterArmedThisWindow = false;
        ResetBurstQuotaFinish();
        continuousPressureBurstsThisWindow = 0;
        currentPatternVolleyBeatsFired = 0;
        noLosTimer = 0f;
        inWindowTime = 0f;
        ResetRepositionState();

        // Pick first strafe target immediately on window entry
        nextStrafePickTime = Time.time;
        isStrafeMoving = false;

        RoleShooterTuning t =
            GetRoleTuning();

        if (shooter != null)
        {
            shooter.SetFireInterval(
                t.fireInterval
            );

            shooter.SetAimLag(
                t.aimLag
            );
        }

        ApplyFiringStyleForThisWindow();
        MarkMeaningfulAction(
            "AttackWindow"
        );

        attackEnableTime = Time.time + Mathf.Max(0f, t.firstShotDelay + UnityEngine.Random.Range(0.04f, 0.16f));
        nextStateTime = Time.time + UnityEngine.Random.Range(t.attackWindow.x, t.attackWindow.y);
        SetShooterGate(false);
    }

    private void TickAttackWindow()
    {
        if (Time.time >= nextStateTime)
        {
            SetShooterGate(false);
            EnterReplan(false);
            return;
        }

        if (!isRanged || shooter == null) return;

        if (TryFinishAttackWindowAfterBurstQuota())
            return;

        // Handle active micro-reposition
        if (isRepositioning)
        {
            TickReposition();
            return;
        }

        // Strict LOS gate
        if (RoleNeedsStrictLos())
        {
            bool hasLos = HasLineOfSightToPlayerNow();
            if (!hasLos)
            {
                noLosTimer += Time.deltaTime;
                SetShooterGate(false);
                if (noLosTimer >= maxNoLosBeforeReplan) EnterReplan(true);
                return;
            }
            noLosTimer = 0f;
        }

        // Range gate (bypassed under full pressure)
        if (profile.mustBeInPreferredRangeToShoot && squadPressure01 < advancePressureThreshold)
        {
            float dist = Vector2.Distance(transform.position, GetPlayerPos());
            if (dist > Mathf.Max(profile.preferredMaxRange, 0.01f))
            {
                SetShooterGate(false);
                BeginFallbackChase();
                return;
            }
        }

        if (threatGapCanHurryAttack && squad != null && squad.ShouldForceAttack(this))
            attackEnableTime = Mathf.Min(attackEnableTime, Time.time);

        if (Time.time < attackEnableTime) return;

        if (!shooterArmedThisWindow)
        {
            if (!TryClaimSquadAttackSlotForShooter())
                return;

            SyncShooterTarget();

            if (shooter != null)
            {
                shooter.ForceReadyToFire(0f);
            }

            SetShooterGate(true);
            shooterArmedThisWindow = true;

            MarkMeaningfulAction(
                "ShooterArmed"
            );
        }

        // Miss pressure: track time in window, trigger reposition when player is stationary
        if (shooterArmedThisWindow && repositionsThisWindow < maxRepositionsPerWindow)
        {
            // Only count time when the player is relatively stationary (dead zone condition)
            Vector2 playerPos = GetPlayerPos();
            if (!playerPosForMissInitialized)
            {
                lastPlayerPosForMiss = playerPos;
                playerPosForMissInitialized = true;
            }

            float playerDelta = Vector2.Distance(playerPos, lastPlayerPosForMiss);
            if (playerDelta < playerStationaryThreshold)
            {
                inWindowTime += Time.deltaTime;
                if (inWindowTime >= missTimeThreshold)
                    TryMicroReposition();
            }
            else
            {
                // Player moved — reset the miss timer
                inWindowTime = 0f;
                lastPlayerPosForMiss = playerPos;
            }
        }

        // In-window strafing: gently move within the zone while shooting.
        // Only active once the shooter is armed. Does not disable the shooter.
        // Skipped if a full reposition is already in progress.
        if (shooterArmedThisWindow && !isRepositioning)
            TickStrafe(wanderSpeedMultiplier);
    }

    // ----------------------------
    // Phase 8 Opportunity Flanking / Player Attention
    // ----------------------------
    private bool TryStartOpportunityFlank()
    {
        if (!useOpportunityFlanking || !isRanged || shooter == null)
            return false;

        if (squad == null)
            return false;

        if (Time.time < nextOpportunityFlankAllowedTime)
            return false;

        if (currentRole == EnemySquadRole.Retreater)
            return false;

        if (opportunityFlankOnlyForFlankerRoles &&
            currentRole != EnemySquadRole.FlankerLeft &&
            currentRole != EnemySquadRole.FlankerRight)
        {
            debugOpportunityFlank = "Skipped: not flanker role";
            return false;
        }

        if (squad.AliveAgentCount <= 1)
        {
            debugOpportunityFlank = "Skipped: solo enemy";
            return false;
        }

        if (opportunityFlankRequiresPlayerFocus &&
            !squad.TryGetPlayerAttentionFocus(
                this,
                out _,
                out _,
                out _))
        {
            debugOpportunityFlank = "Waiting for player focus";
            return false;
        }

        if (!TryPickOpportunityFlankTarget(out Vector2 target, out string reason))
        {
            debugOpportunityFlank = $"No flank target: {reason}";
            return false;
        }

        opportunityFlankActive = true;
        opportunityFlankTarget = target;
        opportunityFlankStartedTime = Time.time;
        opportunityFlankReason = reason;

        ReleaseCurrentPoint();
        usingFallbackChase = false;
        state = BrainState.Move;
        shooterArmedThisWindow = false;
        SetShooterGate(false);
        ClearMoveIntent();
        ClearNavigationPath();
        PrepareMovementProgress(target);

        nextStuckCheckTime = Time.time + stuckCheckInterval;
        lastStuckPos = transform.position;

        debugOpportunityFlank = $"Started: {reason} -> {Vector2.Distance(target, GetPlayerPos()):0.0}u";
        MarkMeaningfulAction("OpportunityFlankStart");
        return true;
    }

    private bool TryPickOpportunityFlankTarget(out Vector2 bestTarget, out string reason)
    {
        bestTarget = Vector2.zero;
        reason = "NoFocus";

        if (squad == null)
            return false;

        if (!squad.TryGetPlayerAttentionFocus(
                this,
                out IEnemySquadAgent focus,
                out Vector2 focusPos,
                out Vector2 playerPos))
        {
            return false;
        }

        if (!squad.TryGetOpportunityFlankTarget(
                this,
                opportunityFlankBehindDistance,
                opportunityFlankSideDistance,
                opportunityFlankFallbackRadius,
                out Vector2 primary,
                out string squadReason))
        {
            reason = squadReason;
            return false;
        }

        Vector2 focusToPlayer = playerPos - focusPos;
        if (focusToPlayer.sqrMagnitude <= 0.0001f)
            focusToPlayer = playerPos - (Vector2)transform.position;

        if (focusToPlayer.sqrMagnitude <= 0.0001f)
            focusToPlayer = Vector2.up;

        Vector2 forward = focusToPlayer.normalized;
        Vector2 right = new Vector2(forward.y, -forward.x);
        float roleSide = currentRole == EnemySquadRole.FlankerLeft ? -1f :
                         currentRole == EnemySquadRole.FlankerRight ? 1f : 0f;

        if (Mathf.Abs(roleSide) < 0.01f)
        {
            Vector2 toMe = (Vector2)transform.position - playerPos;
            roleSide = Vector2.Dot(toMe, right) < 0f ? -1f : 1f;
        }

        Vector2[] candidates = new Vector2[]
        {
            primary,
            playerPos + forward * opportunityFlankBehindDistance,
            playerPos + forward * opportunityFlankBehindDistance + right * roleSide * opportunityFlankSideDistance,
            playerPos + forward * opportunityFlankBehindDistance - right * roleSide * opportunityFlankSideDistance,
            playerPos + right * roleSide * Mathf.Max(opportunityFlankSideDistance, opportunityFlankFallbackRadius),
            playerPos - right * roleSide * Mathf.Max(opportunityFlankSideDistance, opportunityFlankFallbackRadius),
            playerPos + (forward + right * roleSide * 0.65f).normalized * opportunityFlankFallbackRadius,
            playerPos + (forward - right * roleSide * 0.65f).normalized * opportunityFlankFallbackRadius
        };

        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;
        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2 candidate = candidates[i];
            if (!IsOpportunityFlankCandidateValid(myPos, playerPos, candidate))
                continue;

            Vector2 fromPlayer = candidate - playerPos;
            float distanceToPlayer = fromPlayer.magnitude;
            Vector2 candidateDir = distanceToPlayer > 0.001f ? fromPlayer / distanceToPlayer : forward;
            Vector2 focusDir = (focusPos - playerPos);
            if (focusDir.sqrMagnitude <= 0.0001f)
                focusDir = -forward;
            focusDir.Normalize();

            float oppositeFocusScore = Mathf.Clamp01(Vector2.Angle(candidateDir, focusDir) / 180f);
            float rangeMid = (opportunityFlankMinPlayerDistance + opportunityFlankMaxPlayerDistance) * 0.5f;
            float rangeSpan = Mathf.Max(0.1f, (opportunityFlankMaxPlayerDistance - opportunityFlankMinPlayerDistance) * 0.5f);
            float rangeScore = 1f - Mathf.Clamp01(Mathf.Abs(distanceToPlayer - rangeMid) / rangeSpan);
            float pathCost = Vector2.Distance(myPos, candidate);

            if (navigationGrid != null && navigationGrid.IsBuilt)
            {
                float estimated = navigationGrid.EstimatePathCost(myPos, candidate);
                if (!float.IsInfinity(estimated))
                    pathCost = estimated;
            }

            float score = 0f;
            score += oppositeFocusScore * 3.0f;
            score += rangeScore * 1.1f;
            score -= Mathf.Clamp01(pathCost / 8f) * 0.75f;

            if (squad != null && opportunityFlankPincerScoreWeight > 0f)
            {
                score += squad.ScorePincerCandidate(
                    this,
                    candidate,
                    currentRole
                ) * opportunityFlankPincerScoreWeight;
            }

            if (i == 0)
                score += 0.35f;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
                found = true;
            }
        }

        reason = found
            ? $"{squadReason} score:{bestScore:0.0}"
            : "NoValidCandidate";
        return found;
    }

    private bool IsOpportunityFlankCandidateValid(Vector2 myPos, Vector2 playerPos, Vector2 candidate)
    {
        float playerDistance = Vector2.Distance(candidate, playerPos);

        if (playerDistance < opportunityFlankMinPlayerDistance ||
            playerDistance > opportunityFlankMaxPlayerDistance)
        {
            return false;
        }

        if (navigationGrid != null && navigationGrid.IsBuilt)
        {
            if (!navigationGrid.IsPositionWalkable(candidate))
                return false;

            if (!navigationGrid.AreConnected(myPos, candidate))
                return false;
        }
        else if (losBlockMask.value != 0)
        {
            Vector2 toCandidate = candidate - myPos;
            float distance = toCandidate.magnitude;
            if (distance > 0.001f &&
                Physics2D.Raycast(myPos, toCandidate / distance, Mathf.Min(distance, obstacleProbeDistance * 2.5f), losBlockMask))
            {
                return false;
            }
        }

        if (opportunityFlankRequiresDestinationLos)
        {
            bool hasLos = CombatLineOfSight2D.HasLineOfSight(
                this,
                candidate,
                playerPos,
                losBlockMask,
                out _
            );

            if (!hasLos)
                return false;
        }

        return true;
    }

    private void CompleteOpportunityFlank(string reason)
    {
        opportunityFlankActive = false;
        nextOpportunityFlankAllowedTime = Time.time + Mathf.Max(0.1f, opportunityFlankCooldown * 0.55f);
        debugOpportunityFlank = $"Arrived/ambush: {reason}";

        ClearMoveIntent();
        ClearNavigationPath();
        MarkMeaningfulAction("OpportunityFlankArrived");

        if (opportunityFlankAttackImmediatelyOnArrival &&
            isRanged &&
            shooter != null &&
            HasLineOfSightToPlayerNow())
        {
            EnterAttackWindow();
            attackEnableTime = Time.time + Mathf.Max(0f, opportunityFlankAmbushDelay);
            debugOpportunityFlank = $"Ambush attack: {reason}";
            return;
        }

        EnterReplan(false);
    }

    private void FailOpportunityFlank(string reason)
    {
        opportunityFlankActive = false;
        nextOpportunityFlankAllowedTime = Time.time + Mathf.Max(0.1f, opportunityFlankCooldown);
        debugOpportunityFlank = $"Failed: {reason}";
        ClearMoveIntent();
        ClearNavigationPath();
        MarkMeaningfulAction("OpportunityFlankFailed");
        EnterReplan(false);
    }

    private void CancelOpportunityFlank(string reason, bool applyCooldown)
    {
        if (!opportunityFlankActive)
            return;

        opportunityFlankActive = false;
        debugOpportunityFlank = $"Canceled: {reason}";

        if (applyCooldown)
            nextOpportunityFlankAllowedTime = Time.time + Mathf.Max(0.1f, opportunityFlankCooldown);
    }

    // ----------------------------
    // Escape waypoint selection
    // ----------------------------

    /// <summary>
    /// When stuck during a pressure advance, finds the nearest tactical point or clear
    /// position that the enemy can walk to without being immediately blocked.
    /// This gives the enemy an escape route around the obstacle trapping it.
    /// </summary>
    private bool TryFindEscapeWaypoint(out Vector2 waypoint)
    {
        waypoint = Vector2.zero;
        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 playerPos = GetPlayerPos();

        // First: try nearby tactical points.
        // Prefer ones that are closer to the player than we currently are (forward progress).
        var points = CombatTacticalPoint.AllPoints;
        float myDistToPlayer = Vector2.Distance(myPos, playerPos);

        CombatTacticalPoint bestPoint = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p == null) continue;

            float distToPoint = Vector2.Distance(myPos, p.Position);
            if (distToPoint > escapeWaypointSearchRadius) continue;
            if (distToPoint < 0.3f) continue; // already here

            // Check we're not immediately blocked going to this point
            bool pathClear = !Physics2D.Raycast(myPos, (p.Position - myPos).normalized,
                                                 obstacleProbeDistance * 2f, losBlockMask);
            if (!pathClear) continue;

            float distToPlayer = Vector2.Distance(p.Position, playerPos);

            // Score: prefer points that are closer to the player (progress),
            // closer to us (fast to reach), and not behind us
            float progress = myDistToPlayer - distToPlayer; // positive = moves us closer to player
            float proximity = 1f - Mathf.Clamp01(distToPoint / escapeWaypointSearchRadius);
            float score = progress * 1.5f + proximity;

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = p;
            }
        }

        if (bestPoint != null)
        {
            waypoint = bestPoint.Position;
            return true;
        }

        // Fallback: sample positions in a circle around the enemy, pick one with
        // a clear first step and that makes progress toward the player.
        for (int i = 0; i < 12; i++)
        {
            float angleDeg = (360f / 12f) * i;
            float r = escapeWaypointSearchRadius * UnityEngine.Random.Range(0.5f, 1.0f);
            Vector2 candidate = myPos + new Vector2(
                Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * r;

            bool pathClear = !Physics2D.Raycast(myPos, (candidate - myPos).normalized,
                                                 obstacleProbeDistance * 2f, losBlockMask);
            if (!pathClear) continue;

            float distToPlayer = Vector2.Distance(candidate, playerPos);
            if (distToPlayer < myDistToPlayer)
            {
                waypoint = candidate;
                return true;
            }
        }

        return false;
    }

    // ----------------------------
    // Zone wandering / micro-reposition
    // ----------------------------
    private void TryMicroReposition()
    {
        Vector2 playerPos = GetPlayerPos();
        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 currentAimDir = (playerPos - myPos).normalized;

        Vector2 zoneCenter = currentPoint != null ? currentPoint.Position : myPos;
        float radius = currentPoint != null
            ? Mathf.Max(currentPoint.wanderRadius, 0.05f)
            : 1.0f;

        if (radius < 0.1f) { EnterReplan(true); return; }

        Vector2 bestPos = Vector2.zero;
        float bestScore = float.NegativeInfinity;
        bool found = false;
        int candidates = 8;

        for (int i = 0; i < candidates; i++)
        {
            float angleDeg = (360f / candidates) * i + UnityEngine.Random.Range(-5f, 5f);
            float r = radius * UnityEngine.Random.Range(0.4f, 1.0f);
            Vector2 candidate = zoneCenter + new Vector2(
                Mathf.Cos(angleDeg * Mathf.Deg2Rad),
                Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * r;

            bool hasLos =
                CombatLineOfSight2D.HasLineOfSight(
                    this,
                    candidate,
                    playerPos,
                    losBlockMask,
                    out _
                );

            if (!hasLos)
                continue;

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                if (!navigationGrid.IsPositionWalkable(
                        candidate))
                {
                    continue;
                }

                if (!navigationGrid.AreConnected(
                        myPos,
                        candidate))
                {
                    continue;
                }
            }

            Vector2 newAimDir = (playerPos - candidate).normalized;
            float angleDelta = Vector2.Angle(currentAimDir, newAimDir);
            float angleScore = angleDelta / 90f;
            float rangeScore = ScoreRangeFit(Vector2.Distance(candidate, playerPos));
            float novelty = Vector2.Distance(candidate, myPos) > 0.4f ? 0.4f : 0f;
            float drift = Vector2.Distance(candidate, zoneCenter) / radius * 0.3f;

            float score = angleScore * 2.0f + rangeScore * 0.8f + novelty - drift;

            if (usePincerPositioning && squad != null && pincerMicroRepositionWeight > 0f)
            {
                score += squad.ScorePincerCandidate(
                    this,
                    candidate,
                    currentRole
                ) * pincerMicroRepositionWeight;
            }

            if (score > bestScore) { bestScore = score; bestPos = candidate; found = true; }
        }

        if (!found) { EnterReplan(true); return; }

        repositionTarget = bestPos;
        isRepositioning = true;
        repositionStartTime = Time.time;
        repositionsThisWindow++;
        inWindowTime = 0f;
        playerPosForMissInitialized = false;

        SetShooterGate(false);
        shooterArmedThisWindow = false;
        ClearMoveIntent();
    }

    private void TickReposition()
    {
        if (Time.time - repositionStartTime > 1.5f)
        {
            isRepositioning = false;
            ClearMoveIntent();
            EnterReplan(true);
            return;
        }

        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        if (Vector2.Distance(pos, repositionTarget) <= 0.25f)
        {
            isRepositioning = false;
            ClearMoveIntent();
            ApplyFiringStyleForThisWindow();
            SyncShooterTarget();

            if (shooter != null)
            {
                shooter.ForceReadyToFire(0f);
            }

            SetShooterGate(true);
            shooterArmedThisWindow = true;

            MarkMeaningfulAction(
                "RepositionComplete"
            );
        }
        else
        {
            MoveTowardTarget(repositionTarget, moveSpeed * wanderSpeedMultiplier * 2.5f);
        }
    }

    /// <summary>
    /// Gentle lateral movement within the tactical zone while shooting.
    /// Picks a new spot inside the wander zone on a timer, moves there slowly,
    /// then picks another. Shooter stays enabled throughout.
    /// </summary>
    private void TickStrafe(float speedMultiplier)
    {
        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;

        // Time to pick a new strafe destination
        if (Time.time >= nextStrafePickTime)
        {
            Vector2 zoneCenter = currentPoint != null ? currentPoint.Position : myPos;
            float radius = currentPoint != null
                ? Mathf.Max(currentPoint.wanderRadius * 0.7f, 0.1f)
                : 0.7f;

            // Sample a few candidates and pick one with LOS to the player
            Vector2 playerPos = GetPlayerPos();
            Vector2 bestCandidate = myPos;
            bool found = false;

            for (int i = 0; i < 6; i++)
            {
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float r = radius * UnityEngine.Random.Range(0.3f, 1.0f);
                Vector2 candidate = zoneCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

                bool hasLos =
                    CombatLineOfSight2D.HasLineOfSight(
                        this,
                        candidate,
                        playerPos,
                        losBlockMask,
                        out _
                    );

                bool isNovel =
                    Vector2.Distance(
                        candidate,
                        myPos
                    ) > 0.2f;

                bool navigable = true;

                if (navigationGrid != null &&
                    navigationGrid.IsBuilt)
                {
                    navigable =
                        navigationGrid.IsPositionWalkable(
                            candidate
                        ) &&
                        navigationGrid.AreConnected(
                            myPos,
                            candidate
                        );
                }

                if (hasLos &&
                    isNovel &&
                    navigable)
                {
                    bestCandidate = candidate;
                    found = true;
                    break;
                }
            }

            if (found)
            {
                strafeTarget = bestCandidate;
                isStrafeMoving = true;
            }
            else
            {
                isStrafeMoving = false;
            }

            // Next pick: random interval so movement feels organic, not mechanical
            nextStrafePickTime = Time.time + UnityEngine.Random.Range(
                wanderIntervalRange.x, wanderIntervalRange.y);
        }

        // Move toward the current strafe target at the given speed
        if (isStrafeMoving)
        {
            Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
            if (Vector2.Distance(pos, strafeTarget) <= 0.15f)
            {
                isStrafeMoving = false;
                ClearMoveIntent();
            }
            else
            {
                MoveTowardTarget(strafeTarget, moveSpeed * speedMultiplier);
            }
        }
    }

    private void ResetRepositionState()
    {
        inWindowTime = 0f;
        repositionsThisWindow = 0;
        isRepositioning = false;
        repositionStartTime = 0f;
        playerPosForMissInitialized = false;
    }

    private void ResetAdvanceStuck()
    {
        advanceStuckCount = 0;
        usingEscapeWaypoint = false;
    }

    // ----------------------------
    // Movement helpers
    // ----------------------------
    private bool MoveTowardTarget(
        Vector2 target,
        float speed)
    {
        Vector2 position =
            rb != null
                ? rb.position
                : (Vector2)transform.position;

        Vector2 steeringTarget =
            target;

        if (navigationGrid != null &&
            navigationGrid.IsBuilt)
        {
            bool destinationChanged =
                !hasNavigationDestination ||
                Vector2.Distance(
                    navigationDestination,
                    target
                ) >
                Mathf.Max(
                    0.25f,
                    navigationGrid.CellSize
                );

            bool needsRefresh =
                destinationChanged ||
                navigationPath.Count == 0 ||
                navigationPathIndex >=
                navigationPath.Count ||
                Time.time >=
                nextPathRefreshTime;

            if (needsRefresh)
            {
                navigationPath.Clear();
                navigationPathIndex = 0;

                bool found =
                    navigationGrid.TryFindPath(
                        position,
                        target,
                        navigationPath
                    );

                navigationDestination =
                    target;

                hasNavigationDestination =
                    true;

                nextPathRefreshTime =
                    Time.time +
                    Mathf.Max(
                        0.05f,
                        pathRefreshInterval
                    );

                if (!found ||
                    navigationPath.Count == 0)
                {
                    debugPathStatus =
                        "Path Failed";

                    ClearMoveIntent();
                    return false;
                }

                debugPathStatus =
                    $"Path {navigationPath.Count}";
            }

            while (navigationPathIndex <
                   navigationPath.Count)
            {
                Vector2 waypoint =
                    navigationPath[
                        navigationPathIndex
                    ];

                if (Vector2.Distance(
                        position,
                        waypoint) >
                    waypointArrivalRadius)
                {
                    break;
                }

                navigationPathIndex++;

                MarkMeaningfulAction(
                    "ReachedWaypoint"
                );
            }

            if (navigationPathIndex <
                navigationPath.Count)
            {
                steeringTarget =
                    navigationPath[
                        navigationPathIndex
                    ];
            }
            else
            {
                steeringTarget = target;
            }
        }
        else
        {
            debugPathStatus =
                "Direct Steering";
        }

        Vector2 toTarget =
            steeringTarget -
            position;

        if (toTarget.sqrMagnitude <
            0.0001f)
        {
            ClearMoveIntent();
            return true;
        }

        Vector2 direction =
            toTarget.normalized;

        bool blocked =
            Physics2D.Raycast(
                position,
                direction,
                obstacleProbeDistance,
                losBlockMask
            );

        if (blocked &&
            (navigationGrid == null ||
             !navigationGrid.IsBuilt))
        {
            if (Time.time >=
                    avoidSideUntil ||
                avoidSideSign == 0)
            {
                avoidSideSign =
                    UnityEngine.Random.value <
                    0.5f
                        ? -1
                        : 1;

                avoidSideUntil =
                    Time.time +
                    0.30f;
            }

            Vector2 perpendicular =
                avoidSideSign > 0
                    ? new Vector2(
                        -direction.y,
                        direction.x
                    )
                    : new Vector2(
                        direction.y,
                        -direction.x
                    );

            direction =
                (
                    direction *
                    0.35f +
                    perpendicular *
                    0.65f
                ).normalized;

            if (Physics2D.Raycast(
                    position,
                    direction,
                    obstacleProbeDistance *
                    0.90f,
                    losBlockMask))
            {
                ClearMoveIntent();
                return false;
            }
        }

        desiredVelocity =
            direction *
            speed;

        hasMoveIntent = true;

        UpdateMovementProgress(
            target
        );

        return true;
    }

    private void ClearMoveIntent()
    {
        desiredVelocity = Vector2.zero;
        hasMoveIntent = false;
    }

    private void ClearNavigationPath()
    {
        navigationPath.Clear();
        navigationPathIndex = 0;
        hasNavigationDestination = false;
        nextPathRefreshTime = 0f;
        debugPathStatus = "No Path";
    }

    private void PrepareMovementProgress(
        Vector2 destination)
    {
        lastProgressTime = Time.time;

        bestRemainingDistance =
            CalculateRemainingDistance(
                destination
            );
    }

    private void UpdateMovementProgress(
        Vector2 destination)
    {
        float remaining =
            CalculateRemainingDistance(
                destination
            );

        if (float.IsInfinity(
                bestRemainingDistance) ||
            remaining <=
            bestRemainingDistance -
            progressRequired)
        {
            bestRemainingDistance =
                remaining;

            lastProgressTime =
                Time.time;

            MarkMeaningfulAction(
                "MovementProgress"
            );
        }
    }

    private bool HasMovementProgressTimedOut(
        Vector2 destination)
    {
        UpdateMovementProgress(
            destination
        );

        return
            Time.time -
            lastProgressTime >=
            progressTimeout;
    }

    private float CalculateRemainingDistance(
        Vector2 destination)
    {
        Vector2 position =
            rb != null
                ? rb.position
                : (Vector2)transform.position;

        if (navigationPath.Count == 0 ||
            navigationPathIndex >=
            navigationPath.Count)
        {
            return Vector2.Distance(
                position,
                destination
            );
        }

        float total =
            Vector2.Distance(
                position,
                navigationPath[
                    navigationPathIndex
                ]
            );

        for (int i =
                 navigationPathIndex + 1;
             i < navigationPath.Count;
             i++)
        {
            total += Vector2.Distance(
                navigationPath[i - 1],
                navigationPath[i]
            );
        }

        return total;
    }

    private void MarkMeaningfulAction(
        string reason)
    {
        lastMeaningfulActionTime =
            Time.time;

        lastMeaningfulActionReason =
            reason;
    }

    private void MarkCurrentPointFailed()
    {
        if (currentPoint == null)
            return;

        failedPointUntil[currentPoint] =
            Time.time +
            Mathf.Max(
                0.1f,
                failedPointCooldown
            );
    }

    private bool IsPointTemporarilyFailed(
        CombatTacticalPoint point)
    {
        if (point == null)
            return false;

        if (!failedPointUntil.TryGetValue(
                point,
                out float until))
        {
            return false;
        }

        if (Time.time >= until)
        {
            failedPointUntil.Remove(point);
            return false;
        }

        return true;
    }

    private void CleanupFailedPointMemory()
    {
        if (failedPointUntil.Count == 0)
            return;

        List<CombatTacticalPoint> remove =
            null;

        foreach (var pair in failedPointUntil)
        {
            if (pair.Key == null ||
                Time.time >= pair.Value)
            {
                remove ??=
                    new List<
                        CombatTacticalPoint
                    >();

                remove.Add(pair.Key);
            }
        }

        if (remove == null)
            return;

        for (int i = 0;
             i < remove.Count;
             i++)
        {
            failedPointUntil.Remove(
                remove[i]
            );
        }
    }

    // ----------------------------
    // Tactical point selection
    // ----------------------------
    private CombatTacticalPoint PickBestPointForRole(bool shouldFlee, bool panicFight)
    {
        var points = CombatTacticalPoint.AllPoints;
        if (points == null || points.Count == 0) return null;

        Vector2 myPos = transform.position;
        Vector2 playerPos = GetPlayerPos();

        float pressureInfluence = Mathf.Clamp01(
            (squadPressure01 - advancePressureThreshold * 0.5f) /
            Mathf.Max(0.01f, advancePressureThreshold));

        CombatTacticalPoint best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];

            if (p == null ||
                !p.CanBeUsedBy(
                    this,
                    Time.time) ||
                IsPointTemporarilyFailed(p))
            {
                continue;
            }

            float travel =
                navigationGrid != null &&
                navigationGrid.IsBuilt
                    ? navigationGrid.EstimatePathCost(
                        myPos,
                        p.Position
                    )
                    : Vector2.Distance(
                        myPos,
                        p.Position
                    );

            if (float.IsInfinity(travel))
                continue;

            float score = 0f;
            score += p.baseScoreBias;
            score += ScoreRoleTypeFit(p.pointType);
            score += ScorePersonalityBias(p);

            if (shouldFlee && !panicFight)
            {
                if (p.pointType == TacticalPointType.Cover || p.pointType == TacticalPointType.Retreat) score += 2.2f;
                if (p.pointType == TacticalPointType.Fire) score -= 0.6f;
                if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score -= 0.4f;
            }

            if (profile.preferFlanksAlways)
            {
                if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score += 1.4f;
                if (p.pointType == TacticalPointType.Fire) score -= 0.25f;
            }

            bool pointHasLos =
                CombatLineOfSight2D.HasLineOfSight(
                    this,
                    p.Position,
                    playerPos,
                    losBlockMask,
                    out _
                );
            if (currentRole == EnemySquadRole.Anchor || currentRole == EnemySquadRole.Suppressor)
                score += pointHasLos ? 1.2f : -2.4f;

            float distToPlayer = Vector2.Distance(p.Position, playerPos);
            score += ScoreRangeFit(distToPlayer) * rangeFitWeight;

            if (usePincerPositioning && squad != null && pincerTacticalPointWeight > 0f)
            {
                score += squad.ScorePincerCandidate(
                    this,
                    p.Position,
                    currentRole
                ) * pincerTacticalPointWeight;
            }

            if (currentRole ==
                    EnemySquadRole.FlankerLeft ||
                currentRole ==
                    EnemySquadRole.FlankerRight)
            {
                score +=
                    ScoreFlankSide(
                        p.Position,
                        playerPos
                    ) *
                    flankWeight;

                int desiredLane =
                    currentRole ==
                    EnemySquadRole.FlankerLeft
                        ? -1
                        : 1;

                if (p.laneTag == desiredLane)
                    score += 0.75f;
                else if (p.laneTag == -desiredLane)
                    score -= 0.75f;
            }

            score -=
                travel *
                travelCostWeight *
                0.2f;
            if (p.IsReserved && p != currentPoint) score -= occupiedPenalty;
            if (p == currentPoint) score += 0.45f;

            if (pressureInfluence > 0f)
            {
                bool isPassive = p.pointType == TacticalPointType.Cover || p.pointType == TacticalPointType.Retreat;
                bool isAggressive = p.pointType == TacticalPointType.Fire
                                 || p.pointType == TacticalPointType.FlankLeft
                                 || p.pointType == TacticalPointType.FlankRight;
                if (isPassive) score -= pressureCoverPenalty * pressureInfluence;
                if (isAggressive) score += pressureFireBonus * pressureInfluence;
                score += Mathf.Clamp01(1f - distToPlayer / 8f) * pressureInfluence * 1.5f;
            }

            score += UnityEngine.Random.Range(-0.06f, 0.06f);
            if (score > bestScore) { bestScore = score; best = p; }
        }

        return best;
    }

    private float ScoreRoleTypeFit(TacticalPointType type)
    {
        switch (currentRole)
        {
            case EnemySquadRole.Suppressor:
                return type == TacticalPointType.Fire ? fireWeight + 1.0f
                     : type == TacticalPointType.Cover ? coverWeight + 0.2f : 0f;
            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                return (type == TacticalPointType.FlankLeft || type == TacticalPointType.FlankRight) ? flankWeight + 0.8f
                     : type == TacticalPointType.Cover ? coverWeight + 0.2f : 0f;
            case EnemySquadRole.Anchor:
                return type == TacticalPointType.Cover ? coverWeight + 0.8f
                     : type == TacticalPointType.Fire ? fireWeight + 0.2f : 0f;
            case EnemySquadRole.Retreater:
                return type == TacticalPointType.Retreat ? coverWeight + 0.9f
                     : type == TacticalPointType.Cover ? coverWeight + 0.6f : -0.2f;
            default: return 0f;
        }
    }

    private float ScorePersonalityBias(CombatTacticalPoint p)
    {
        float score = 0f;
        float openingMult = IsInOpening() ? 1.6f : 1.0f;

        if (p.pointType == TacticalPointType.Cover) score += profile.coverBias * openingMult;
        if (p.pointType == TacticalPointType.Fire) score += profile.fireBias * openingMult;
        if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight)
            score += profile.flankBias * openingMult;

        if (IsInOpening())
        {
            if (profile.openingAction == OpeningAction.RunToCover)
            {
                if (p.pointType == TacticalPointType.Cover) score += 1.4f;
                if (p.pointType == TacticalPointType.Fire) score -= 0.3f;
            }
            else if (profile.openingAction == OpeningAction.SeekFlank)
            {
                if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score += 1.2f;
                if (p.pointType == TacticalPointType.Cover) score += 0.2f;
            }
        }

        if (IsUnderFire() && effectivePersonality == Personality.Backstabber)
        {
            if (p.pointType == TacticalPointType.Cover) score += 2.0f;
            if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score -= 1.0f;
            if (p.pointType == TacticalPointType.Fire) score -= 0.6f;
        }

        return score;
    }

    private float ScoreRangeFit(float dist)
    {
        float min = Mathf.Max(0f, profile.preferredMinRange);
        float max = Mathf.Max(min + 0.01f, profile.preferredMaxRange);
        if (isMelee && !isRanged) return Mathf.Clamp01(1f - Mathf.Abs(dist - 1.4f) / 3f);
        if (dist < min) return -0.6f * (min - dist);
        if (dist > max) return -0.2f * (dist - max);
        return 1.0f;
    }

    private float ScoreFlankSide(Vector2 pointPos, Vector2 playerPos)
    {
        Vector2 forward = (playerPos - (Vector2)transform.position).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector2.right;
        Vector2 right = new Vector2(forward.y, -forward.x);
        float sideDot = Vector2.Dot((pointPos - playerPos).normalized, right);
        if (currentRole == EnemySquadRole.FlankerLeft) return -sideDot;
        if (currentRole == EnemySquadRole.FlankerRight) return sideDot;
        return 0f;
    }

    private bool IsPointCompatibleWithRole(
        CombatTacticalPoint point,
        EnemySquadRole role)
    {
        if (point == null)
            return false;

        switch (role)
        {
            case EnemySquadRole.Suppressor:
                return
                    point.pointType ==
                        TacticalPointType.Fire ||
                    point.pointType ==
                        TacticalPointType.Cover;

            case EnemySquadRole.FlankerLeft:
                return
                    point.pointType ==
                        TacticalPointType.FlankLeft ||
                    point.pointType ==
                        TacticalPointType.Cover;

            case EnemySquadRole.FlankerRight:
                return
                    point.pointType ==
                        TacticalPointType.FlankRight ||
                    point.pointType ==
                        TacticalPointType.Cover;

            case EnemySquadRole.Retreater:
                return
                    point.pointType ==
                        TacticalPointType.Retreat ||
                    point.pointType ==
                        TacticalPointType.Cover;

            case EnemySquadRole.Anchor:
                return
                    point.pointType ==
                        TacticalPointType.Cover ||
                    point.pointType ==
                        TacticalPointType.Fire;

            default:
                return true;
        }
    }

    private void ReleaseCurrentPoint()
    {
        if (currentPoint == null) return;
        currentPoint.Release(this, Time.time);
        currentPoint = null;
    }

    private Vector2 GetFallbackChaseTarget()
    {
        Vector2 playerPos = GetPlayerPos();

        if (!usePincerPositioning ||
            !usePincerFallbackChase ||
            squad == null ||
            currentRole == EnemySquadRole.Retreater)
        {
            debugPincerPositioning = "Direct Player";
            return playerPos;
        }

        if (!hasCachedPincerTarget || Time.time >= nextPincerTargetRefreshTime)
        {
            cachedPincerChaseTarget = squad.GetPressurePositionForAgent(
                this,
                playerPos,
                Mathf.Max(0.5f, pincerFallbackRadius)
            );

            hasCachedPincerTarget = true;
            nextPincerTargetRefreshTime = Time.time + Mathf.Max(0.05f, pincerTargetRefreshInterval);
            debugPincerPositioning = $"Fallback {currentRole} {Vector2.Distance(cachedPincerChaseTarget, playerPos):0.0}u";
        }

        if (navigationGrid != null && navigationGrid.IsBuilt)
        {
            Vector2 nearest = navigationGrid.FindNearestWalkablePosition(cachedPincerChaseTarget);
            if (Vector2.Distance(nearest, cachedPincerChaseTarget) <= Mathf.Max(0.75f, navigationGrid.CellSize * 3f))
                return nearest;
        }

        return cachedPincerChaseTarget;
    }

    private bool IsSoloEnemyForAntiIdle()
    {
        if (!useSoloEnemyAntiIdle)
            return false;

        if (!isRanged || shooter == null)
            return false;

        if (currentRole == EnemySquadRole.Retreater)
            return false;

        if (squad == null)
            return true;

        return squad.AliveAgentCount <= 1;
    }

    private bool SoloEnemyHasImmediateShot(Vector2? knownPlayerPos = null)
    {
        if (!IsSoloEnemyForAntiIdle())
            return false;

        Vector2 playerPos = knownPlayerPos ?? GetPlayerPos();
        float distance = Vector2.Distance(
            transform.position,
            playerPos
        );

        if (distance > Mathf.Max(0.5f, soloMaxImmediateAttackDistance))
        {
            debugSoloAntiIdle = $"Too far {distance:0.0}/{soloMaxImmediateAttackDistance:0.0}";
            return false;
        }

        if (!HasLineOfSightToPlayerNow())
        {
            debugSoloAntiIdle = "No LOS";
            return false;
        }

        return true;
    }

    private void TickPointBlankSkirmishDuringVolleyCooldown()
    {
        if (!usePointBlankSkirmishAntiStuck ||
            !pointBlankSkirmishDuringVolleyCooldown ||
            !isRanged ||
            shooter == null ||
            playerTransform == null ||
            shooter.IsTelegraphing)
        {
            return;
        }

        Vector2 myPos =
            rb != null
                ? rb.position
                : (Vector2)transform.position;

        Vector2 playerPos = GetPlayerPos();

        float distance =
            Vector2.Distance(myPos, playerPos);

        if (distance >
            Mathf.Max(0.1f, pointBlankSkirmishDistance))
        {
            return;
        }

        if (!HasLineOfSightToPlayerNow())
            return;

        if (TryMovePointBlankSkirmish(myPos, playerPos))
        {
            debugPointBlankSkirmish =
                $"Cooldown skirmish {distance:0.0}u.";
        }
        else
        {
            debugPointBlankSkirmish =
                $"Cooldown skirmish failed {distance:0.0}u.";
        }
    }

    private bool TryMovePointBlankSkirmish(
        Vector2 myPos,
        Vector2 playerPos)
    {
        Vector2 away = myPos - playerPos;

        if (away.sqrMagnitude < 0.0001f)
            away = Vector2.right;
        else
            away.Normalize();

        Vector2 left =
            new Vector2(
                -away.y,
                away.x
            );

        float sideSign = 1f;

        switch (currentRole)
        {
            case EnemySquadRole.FlankerLeft:
                sideSign = 1f;
                break;

            case EnemySquadRole.FlankerRight:
                sideSign = -1f;
                break;

            default:
                sideSign =
                    Mathf.Sin(Time.time * 3.17f + GetInstanceID()) >= 0f
                        ? 1f
                        : -1f;
                break;
        }

        Vector2 side = left * sideSign;

        float sideWeight =
            Mathf.Clamp01(pointBlankSkirmishSideWeight);

        Vector2[] dirs =
        {
            (away * (1f - sideWeight) + side * sideWeight).normalized,
            (away * (1f - sideWeight) - side * sideWeight).normalized,
            away,
            side,
            -side
        };

        float targetDistance =
            Mathf.Max(
                pointBlankSkirmishDistance + 0.15f,
                pointBlankSkirmishTargetDistance
            );

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector2 dir = dirs[i];

            if (dir.sqrMagnitude < 0.0001f)
                continue;

            Vector2 candidate =
                playerPos +
                dir.normalized *
                targetDistance;

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                candidate =
                    navigationGrid.FindNearestWalkablePosition(
                        candidate
                    );
            }

            if (Vector2.Distance(candidate, myPos) < 0.10f)
                continue;

            if (MoveTowardTarget(
                    candidate,
                    moveSpeed *
                    Mathf.Max(0.05f, pointBlankSkirmishSpeedMultiplier)))
            {
                return true;
            }

            ClearNavigationPath();
        }

        ClearMoveIntent();
        return false;
    }

    private bool TryPointBlankFailOpenToAttack(string reason)
    {
        if (!usePointBlankSkirmishAntiStuck ||
            !pointBlankFireImmediatelyIfNoPath ||
            !isRanged ||
            shooter == null ||
            playerTransform == null)
        {
            return false;
        }

        Vector2 myPos =
            rb != null
                ? rb.position
                : (Vector2)transform.position;

        float distance =
            Vector2.Distance(myPos, GetPlayerPos());

        if (distance >
            Mathf.Max(
                pointBlankSkirmishDistance,
                closeAttackStartDistance) + 0.45f)
        {
            return false;
        }

        if (!HasLineOfSightToPlayerNow())
            return false;

        pressureBurstStepQueued = false;
        ClearMoveIntent();
        ClearNavigationPath();

        debugPointBlankSkirmish =
            $"{reason}: forced close attack {distance:0.0}u.";

        debugPressureBurstStep =
            debugPointBlankSkirmish;

        MarkMeaningfulAction("PointBlankFailOpen");
        EnterAttackWindow();
        attackEnableTime =
            Time.time +
            Mathf.Max(0f, noPathImmediateAttackDelay);

        return true;
    }


    private bool TrySoloContinueAfterVolleyCooldown()
    {
        if (!IsPatternVolleyLimitReached())
            return false;

        if (!SoloEnemyHasImmediateShot())
            return false;

        SetShooterGate(false);
        shooterArmedThisWindow = false;
        burstQuotaFinishQueued = false;
        burstQuotaFinishTime = 0f;
        pressureBurstStepQueued = false;
        ClearMoveIntent();
        ClearNavigationPath();

        debugSoloAntiIdle =
            "Solo volley cooldown finished. New attack window.";

        MarkMeaningfulAction("SoloVolleyRestart");
        EnterAttackWindow();
        attackEnableTime = Time.time;
        return true;
    }

    private bool TrySoloFailOpenPressureStepToAttack(string reason)
    {
        if (!soloAttackWhenPressureStepFails ||
            !pressureBurstStepQueued ||
            !SoloEnemyHasImmediateShot())
        {
            return false;
        }

        if (Time.time < pressureBurstStepEarliestFireTime)
            return false;

        pressureBurstStepQueued = false;
        ClearMoveIntent();
        ClearNavigationPath();

        debugSoloAntiIdle = $"{reason}: attack instead of replan.";
        debugPressureBurstStep = debugSoloAntiIdle;

        MarkMeaningfulAction("SoloPressureStepFailOpen");
        EnterAttackWindow();
        attackEnableTime = Time.time;
        return true;
    }

    private bool TryScheduleSoloPressureRearm(float requestedDelay)
    {
        if (!IsSoloEnemyForAntiIdle() || !soloBypassPressureBurstStep)
            return false;

        if (!SoloEnemyHasImmediateShot())
            return false;

        float delay = Mathf.Max(
            requestedDelay,
            soloPressureRearmDelay
        );

        attackEnableTime = Time.time + delay;

        nextStateTime = Mathf.Max(
            nextStateTime,
            Time.time + delay + Mathf.Max(0.05f, continuousPressureWindowExtension)
        );

        inWindowTime = 0f;
        playerPosForMissInitialized = false;

        debugSoloAntiIdle =
            $"Solo rearm x{continuousPressureBurstsThisWindow} in {delay:0.00}s.";

        debugAggressionState = debugSoloAntiIdle;
        MarkMeaningfulAction("SoloPressureRearm");
        return true;
    }

    private bool TryFinishAttackWindowAfterBurstQuota()
    {
        if (!endAttackWindowWhenBurstQuotaReached ||
            shooter == null ||
            !shooter.BurstQuotaReached ||
            shooter.IsTelegraphing)
        {
            return false;
        }

        if (!burstQuotaFinishQueued)
        {
            burstQuotaFinishQueued = true;
            RegisterPatternVolleyBeat();

            if (IsPatternVolleyLimitReached())
            {
                float cooldown = currentPatternVolleyCooldownDelay;

                if (IsSoloEnemyForAntiIdle())
                    cooldown = Mathf.Max(cooldown, soloVolleyCooldownAfter);

                burstQuotaFinishTime = Time.time + Mathf.Max(0f, cooldown);
                debugAggressionState =
                    $"Pattern volley finished {currentPatternVolleyBeatsFired}/{currentPatternVolleyLimit}. " +
                    $"Cooldown {cooldown:0.00}s.";

                if (IsSoloEnemyForAntiIdle())
                    debugSoloAntiIdle = debugAggressionState;
            }
            else if (ShouldContinuePressureAfterBurstQuota())
            {
                burstQuotaFinishTime = Time.time + Mathf.Max(0f, currentContinuousPressureRearmDelay);
                debugAggressionState =
                    $"Burst quota reached. Pressure rearm in {currentContinuousPressureRearmDelay:0.00}s.";
            }
            else
            {
                burstQuotaFinishTime = Time.time + Mathf.Max(0f, currentPostBurstReplanDelay);
                debugAggressionState =
                    $"Burst quota reached. Replan in {currentPostBurstReplanDelay:0.00}s.";
            }

            if (debugAggressionFlow)
            {
                Debug.Log(
                    $"EnemyBrain: {name} burst quota spent. {debugAggressionState}",
                    this
                );
            }
        }

        if (Time.time < burstQuotaFinishTime)
        {
            TickPointBlankSkirmishDuringVolleyCooldown();
            return true;
        }

        if (TrySoloContinueAfterVolleyCooldown())
            return true;

        if (ShouldContinuePressureAfterBurstQuota())
        {
            ContinuePressureAfterBurstQuota();
            return true;
        }

        SetShooterGate(false);
        MarkMeaningfulAction("BurstQuotaFinished");
        EnterReplan(false);
        return true;
    }

    private bool ShouldContinuePressureAfterBurstQuota()
    {
        if (!continuePressureAfterBurstQuota)
            return false;

        if (!isRanged || shooter == null)
            return false;

        if (IsPatternVolleyLimitReached())
            return false;

        if (currentRole == EnemySquadRole.Retreater)
            return false;

        if (!HasLineOfSightToPlayerNow())
            return false;

        float distanceToPlayer = Vector2.Distance(
            transform.position,
            GetPlayerPos()
        );

        bool closeEnough =
            distanceToPlayer <=
            Mathf.Max(0.1f, closeRangePressureRepeatDistance);

        bool pressureHighEnough =
            squadPressure01 >=
            Mathf.Clamp01(continuousPressureThreshold);

        bool urgentThreatGap =
            threatGapCanHurryAttack &&
            squad != null &&
            squad.ShouldForceAttack(this);

        return closeEnough || pressureHighEnough || urgentThreatGap;
    }

    private void ContinuePressureAfterBurstQuota()
    {
        continuousPressureBurstsThisWindow++;

        SetShooterGate(false);
        shooterArmedThisWindow = false;
        burstQuotaFinishQueued = false;
        burstQuotaFinishTime = 0f;

        int repeatLimit = GetContinuousPressureRepeatLimitForCurrentPattern();

        if (repeatLimit > 0 &&
            continuousPressureBurstsThisWindow >= repeatLimit)
        {
            debugAggressionState =
                $"Pressure burst x{continuousPressureBurstsThisWindow}; replan for new angle.";

            MarkMeaningfulAction("PressureBurstReplan");
            EnterReplan(true);
            return;
        }

        float delay = Mathf.Max(0f, currentContinuousPressureRearmDelay);

        if (TryScheduleSoloPressureRearm(delay))
            return;

        if (moveBetweenPressureBursts)
        {
            BeginPressureBurstStep(delay);
            return;
        }

        attackEnableTime = Time.time + delay;
        nextStateTime = Mathf.Max(
            nextStateTime,
            Time.time + delay + Mathf.Max(0.05f, continuousPressureWindowExtension)
        );

        // Reset miss-pressure state so the enemy can start a clean new burst instead of
        // immediately deciding it has missed for too long.
        inWindowTime = 0f;
        playerPosForMissInitialized = false;

        debugAggressionState =
            $"Pressure rearm x{continuousPressureBurstsThisWindow} in {delay:0.00}s.";

        MarkMeaningfulAction("PressureRearm");
    }

    private void BeginPressureBurstStep(float rearmDelay)
    {
        Vector2 myPos = rb != null
            ? rb.position
            : (Vector2)transform.position;

        SetShooterGate(false);
        shooterArmedThisWindow = false;
        burstQuotaFinishQueued = false;
        burstQuotaFinishTime = 0f;
        inWindowTime = 0f;
        playerPosForMissInitialized = false;

        ReleaseCurrentPoint();
        ClearNavigationPath();
        ClearMoveIntent();

        hasCachedPincerTarget = false;
        usingFallbackChase = true;
        pressureBurstStepQueued = true;
        pressureBurstStepStart = myPos;
        pressureBurstStepEarliestFireTime =
            Time.time + Mathf.Max(0f, rearmDelay);
        pressureBurstStepDeadline =
            Time.time + Mathf.Max(
                pressureBurstStepDuration,
                rearmDelay + 0.05f
            );

        state = BrainState.Move;
        nextStuckCheckTime = Time.time + stuckCheckInterval;
        lastStuckPos = transform.position;

        Vector2 target = GetFallbackChaseTarget();
        PrepareMovementProgress(target);

        debugAggressionState =
            $"Pressure step x{continuousPressureBurstsThisWindow} -> new angle.";

        debugPressureBurstStep =
            $"Stepping to {Vector2.Distance(target, GetPlayerPos()):0.0}u pressure target.";

        MarkMeaningfulAction("PressureStep");
    }

    private bool TryFinishPressureBurstStep(
        Vector2 myPos,
        Vector2 currentMoveTarget)
    {
        if (!pressureBurstStepQueued)
            return false;

        float moved = Vector2.Distance(
            myPos,
            pressureBurstStepStart
        );

        float targetDistance = Vector2.Distance(
            myPos,
            currentMoveTarget
        );

        bool timeReady =
            Time.time >= pressureBurstStepEarliestFireTime;

        bool deadlineReached =
            Time.time >= pressureBurstStepDeadline;

        bool movedEnough =
            moved >= Mathf.Max(0f, pressureBurstMinStepDistance);

        bool reachedPressureTarget =
            targetDistance <= Mathf.Max(
                fallbackArrivalRadius,
                pressureBurstReadyDistance
            );

        bool hasLos =
            HasLineOfSightToPlayerNow();

        bool losSatisfied =
            !pressureStepPrefersLineOfSight ||
            hasLos ||
            deadlineReached;

        if (timeReady &&
            losSatisfied &&
            (deadlineReached || movedEnough || reachedPressureTarget))
        {
            pressureBurstStepQueued = false;
            ClearMoveIntent();
            ClearNavigationPath();

            MarkMeaningfulAction("PressureStepAttack");
            EnterAttackWindow();
            attackEnableTime = Time.time;

            debugPressureBurstStep =
                $"Attack after step {moved:0.0}u" +
                (hasLos ? " LOS" : " noLOS timeout");

            return true;
        }

        debugPressureBurstStep =
            $"Step move {moved:0.0}u target {targetDistance:0.0}u" +
            (hasLos ? " LOS" : " noLOS");

        return false;
    }

    private void ResetBurstQuotaFinish()
    {
        burstQuotaFinishQueued = false;
        burstQuotaFinishTime = 0f;
        debugAggressionState = "None";
    }


    // ----------------------------
    // Squad attack-slot integration
    // ----------------------------
    private bool TryClaimSquadAttackSlotForShooter()
    {
        if (!useSquadAttackSlots || squad == null)
        {
            debugAttackSlot = "Slots disabled";
            return true;
        }

        if (ownsSquadAttackSlot || squad.IsAttackSlotOwner(this))
        {
            ownsSquadAttackSlot = true;
            debugAttackSlot = "Owned";
            return true;
        }

        if (Time.time < nextSquadAttackSlotRequestTime)
        {
            debugAttackSlot = "Waiting retry";
            return false;
        }

        bool urgent = squad.ShouldForceAttack(this);
        string reason = urgent ? "ThreatGap Shooter" : "Shooter AttackWindow";

        if (squad.TryRequestAttackSlot(this, reason))
        {
            ownsSquadAttackSlot = true;
            debugAttackSlot = reason;
            return true;
        }

        nextSquadAttackSlotRequestTime = Time.time + Mathf.Max(0.03f, attackSlotRetryDelay);
        debugAttackSlot = "Waiting for slot";
        return false;
    }

    private void ReleaseSquadAttackSlot(string reason)
    {
        if (squad != null && (ownsSquadAttackSlot || squad.IsAttackSlotOwner(this)))
            squad.ReleaseAttackSlot(this, reason);

        ownsSquadAttackSlot = false;
    }

    private void UpdateCloseAttackLosMemory()
    {
        if (!attackInsteadOfHuggingPlayer)
            return;

        if (!isRanged || shooter == null || playerTransform == null)
            return;

        if (HasLineOfSightToPlayerNow())
            lastCloseAttackLosTime = Time.time;
    }

    private bool TryStartCloseRangeAttackInsteadOfHugging(
        Vector2 myPos,
        Vector2 playerPos)
    {
        if (!attackInsteadOfHuggingPlayer)
            return false;

        if (!usingFallbackChase)
            return false;

        if (!isRanged || shooter == null)
        {
            debugCloseRangeAntiHug =
                !isRanged
                    ? "Skipped: Not Ranged"
                    : "Skipped: No Shooter";

            return false;
        }

        if (currentRole == EnemySquadRole.Retreater)
        {
            debugCloseRangeAntiHug = "Skipped: Retreater";
            return false;
        }

        float triggerDistance = Mathf.Max(
            0.1f,
            closeAttackStartDistance
        );

        float distance = Vector2.Distance(
            myPos,
            playerPos
        );

        if (distance > triggerDistance)
        {
            debugCloseRangeAntiHug =
                $"Too Far {distance:0.00}/{triggerDistance:0.00}";

            return false;
        }

        bool recentlyHadLos =
            Time.time - lastCloseAttackLosTime <=
            closeAttackLineOfSightGrace;

        if (!recentlyHadLos && !HasLineOfSightToPlayerNow())
        {
            debugCloseRangeAntiHug = "Close But No LOS";
            return false;
        }

        ClearMoveIntent();
        ClearNavigationPath();

        MarkMeaningfulAction(
            "CloseRangeAttack"
        );

        EnterAttackWindow();

        if (skipSettleWhenCloseChasing)
        {
            attackEnableTime =
                Time.time +
                Mathf.Max(
                    0f,
                    closeAttackForceReadyDelay
                );
        }

        debugCloseRangeAntiHug =
            $"Close Attack {distance:0.00}";

        return true;
    }

    private bool TryStartOpportunityAttackWhileMoving()
    {
        if (!allowOpportunityAttackWhileMoving || !isRanged || shooter == null)
            return false;

        if (squad == null || !squad.ShouldForceAttack(this))
            return false;

        if (currentRole == EnemySquadRole.Retreater)
            return false;

        if (Time.time < nextOpportunityAttackTime)
            return false;

        if (!HasLineOfSightToPlayerNow())
            return false;

        nextOpportunityAttackTime = Time.time + Mathf.Max(0.1f, opportunityAttackCooldown);
        ClearMoveIntent();
        ClearNavigationPath();
        MarkMeaningfulAction("OpportunityAttack");
        EnterAttackWindow();
        attackEnableTime = Time.time;
        return true;
    }

    // ----------------------------
    // Shooter integration
    // ----------------------------
    private void SetShooterGate(bool enabled, bool force = false)
    {
        if (shooter == null)
        {
            if (!enabled)
                ReleaseSquadAttackSlot("NoShooter");
            return;
        }

        bool finalEnabled = enabled && isRanged;

        if (!force && shooterGateEnabled == finalEnabled)
        {
            if (!finalEnabled)
                ReleaseSquadAttackSlot("ShooterGateAlreadyClosed");
            return;
        }

        shooterGateEnabled = finalEnabled;
        shooter.SetShootingEnabled(finalEnabled);

        if (!finalEnabled)
            ReleaseSquadAttackSlot("ShooterGateClosed");
    }

    private void SyncShooterTarget()
    {
        if (shooter == null || playerTransform == null) return;
        if (lastSyncedShooterTarget != playerTransform)
        {
            shooter.SetTarget(
                playerTransform
            );

            lastSyncedShooterTarget =
                playerTransform;
        }
    }


    private void ConfigurePatternVolleyPacing(AttackStyle style)
    {
        currentPatternVolleyPatternName = string.IsNullOrWhiteSpace(style.patternName)
            ? "None"
            : style.patternName;

        currentPatternVolleyBeatsFired = 0;
        currentPatternUsesVolleyPacing =
            usePatternVolleyPacing &&
            ShouldUsePatternVolleyPacing(currentPatternVolleyPatternName);

        if (!currentPatternUsesVolleyPacing)
        {
            currentPatternVolleyLimit = 1;
            currentPatternVolleyCooldownDelay = Mathf.Max(0f, currentPostBurstReplanDelay);
            debugPatternVolleyPacing = "Off";
            return;
        }

        int min = Mathf.Max(1, minPatternVolleyBeats);
        int max = Mathf.Max(min, maxPatternVolleyBeats);

        currentPatternVolleyLimit = UnityEngine.Random.Range(min, max + 1);

        if (rareExtraPatternBeatChance > 0f &&
            UnityEngine.Random.value < Mathf.Clamp01(rareExtraPatternBeatChance))
        {
            currentPatternVolleyLimit += 1;
        }

        bool heavy = IsHeavyPatternVolley(currentPatternVolleyPatternName);

        currentPatternVolleyCooldownDelay = Mathf.Max(
            currentPostBurstReplanDelay,
            heavy
                ? heavyPatternVolleyCooldownAfter
                : patternVolleyCooldownAfter
        );

        currentContinuousPressureRearmDelay = Mathf.Max(
            currentContinuousPressureRearmDelay,
            patternVolleyRearmDelay
        );

        currentPostBurstReplanDelay = Mathf.Max(
            currentPostBurstReplanDelay,
            currentPatternVolleyCooldownDelay
        );

        debugPatternVolleyPacing =
            $"{currentPatternVolleyPatternName} volley 0/{currentPatternVolleyLimit}, " +
            $"rearm {currentContinuousPressureRearmDelay:0.00}, cooldown {currentPatternVolleyCooldownDelay:0.00}";
    }

    private bool ShouldUsePatternVolleyPacing(string patternName)
    {
        if (string.IsNullOrWhiteSpace(patternName))
            return false;

        if (!countSingleShotsAsPatternVolley &&
            (patternName == "AimedSingle" || patternName == "EscapeCutoff"))
        {
            return false;
        }

        if (patternVolleyOnlyForPrettyPatterns)
        {
            return IsPrettyDanmakuPattern(patternName) ||
                   patternName == "Ring" ||
                   patternName == "BoI_4Way" ||
                   patternName == "BoI_8Way" ||
                   patternName == "Spiral" ||
                   patternName == "SweepFan";
        }

        return true;
    }

    private bool IsHeavyPatternVolley(string patternName)
    {
        return patternName == "Ring" ||
               patternName == "BoI_4Way" ||
               patternName == "BoI_8Way" ||
               patternName == "RotatingFlowerRing" ||
               patternName == "HaloSpear" ||
               patternName == "ClosingBlossom" ||
               patternName == "CloseCross";
    }

    private void RegisterPatternVolleyBeat()
    {
        if (!currentPatternUsesVolleyPacing)
            return;

        currentPatternVolleyBeatsFired = Mathf.Max(
            0,
            currentPatternVolleyBeatsFired
        ) + 1;

        debugPatternVolleyPacing =
            $"{currentPatternVolleyPatternName} volley " +
            $"{currentPatternVolleyBeatsFired}/{currentPatternVolleyLimit}";
    }

    private bool IsPatternVolleyLimitReached()
    {
        return currentPatternUsesVolleyPacing &&
               currentPatternVolleyLimit > 0 &&
               currentPatternVolleyBeatsFired >= currentPatternVolleyLimit;
    }

    private int GetContinuousPressureRepeatLimitForCurrentPattern()
    {
        // Pattern Volley Pacing owns the repeat limit for decorative attacks.
        // The old continuous-pressure cap is left active only for simple non-paced attacks.
        if (currentPatternUsesVolleyPacing)
            return 0;

        return maxContinuousPressureBurstsBeforeReplan;
    }

    private void ApplyFiringStyleForThisWindow()
    {
        if (shooter == null)
            return;

        AttackStyle style = BuildAttackStyleForThisWindow();

        shooter.SetBurstConfig(
            style.shotsPerBurst,
            style.intraBurstInterval,
            style.burstCooldown
        );

        shooter.SetBurstQuotaPerEnable(
            true,
            Mathf.Max(1, style.burstsPerEnable)
        );

        shooter.SetPattern(style.patternName);

        shooter.SetFanConfig(
            style.fanBullets,
            style.fanArcDegrees
        );

        shooter.SetRingBullets(
            style.ringBullets
        );

        shooter.SetAngularSpeed(
            style.angularSpeedDegPerTick
        );

        if (useRoleProjectileTint)
            shooter.SetProjectileTint(GetProjectileTintForCurrentStyle(), true);
        else
            shooter.ClearProjectileTint();

        currentPostBurstReplanDelay =
            Mathf.Max(0.02f, style.postBurstReplanDelay);

        currentContinuousPressureRearmDelay =
            Mathf.Max(0f, style.pressureRearmDelay);

        ConfigurePatternVolleyPacing(style);

        debugAttackIdentity = style.debugLabel;
    }

    private struct AttackStyle
    {
        public string patternName;
        public int shotsPerBurst;
        public float intraBurstInterval;
        public float burstCooldown;
        public int burstsPerEnable;
        public int fanBullets;
        public float fanArcDegrees;
        public int ringBullets;
        public float angularSpeedDegPerTick;
        public float postBurstReplanDelay;
        public float pressureRearmDelay;
        public string debugLabel;
    }

    private AttackStyle BuildAttackStyleForThisWindow()
    {
        AttackStyle style = new AttackStyle
        {
            patternName = GetDistanceBasedPattern(),
            shotsPerBurst = Mathf.Max(1, profile.shotsPerBurst),
            intraBurstInterval = Mathf.Max(0.03f, profile.intraBurstInterval),
            burstCooldown = Mathf.Max(0.03f, profile.burstCooldown),
            burstsPerEnable = Mathf.Max(1, profile.burstsPerEnable),
            fanBullets = Mathf.Max(1, profile.fanBullets),
            fanArcDegrees = Mathf.Max(0f, profile.fanArcDegrees),
            ringBullets = Mathf.Max(3, profile.ringBullets),
            angularSpeedDegPerTick = profile.angularSpeedDegPerTick,
            postBurstReplanDelay = Mathf.Max(0.02f, postBurstReplanDelay),
            pressureRearmDelay = Mathf.Max(0f, continuousPressureRearmDelay),
            debugLabel = "Profile"
        };

        if (!useRoleSpecificAttackIdentity)
        {
            style.debugLabel = $"Profile {style.patternName}";
            return style;
        }

        float distance = Vector2.Distance(
            transform.position,
            GetPlayerPos()
        );

        int intensity = GetAttackIntensity(distance);
        bool close = distance <= Mathf.Max(
            closeRangeOverrideDistance,
            identityCloseRangeDistance
        );

        string intensityName = intensity == 2
            ? "High"
            : intensity == 1
                ? "Med"
                : "Low";

        // Role identity first, then personality flavor. This keeps squad composition readable.
        // v4: If a Role Attack Profiles asset is assigned, use the inspector-editable mapping, then apply density safety for pretty patterns.
        if (!TryApplyRoleAttackProfileIdentity(ref style, intensity, close))
        {
            switch (currentRole)
            {
                case EnemySquadRole.Suppressor:
                    ApplySuppressorIdentity(ref style, intensity, close);
                    break;
                case EnemySquadRole.FlankerLeft:
                case EnemySquadRole.FlankerRight:
                    ApplyFlankerIdentity(ref style, intensity, close);
                    break;
                case EnemySquadRole.Anchor:
                    ApplyAnchorIdentity(ref style, intensity, close);
                    break;
                case EnemySquadRole.Retreater:
                    ApplyRetreaterIdentity(ref style, intensity, close);
                    break;
                default:
                    ApplyPersonalityFallbackIdentity(ref style, intensity, close);
                    break;
            }
        }

        if (effectivePersonality == Personality.Aggressive &&
            currentRole != EnemySquadRole.Retreater)
        {
            ApplyAggressiveFlavor(ref style, intensity, close);
        }
        else if (effectivePersonality == Personality.Backstabber &&
                 currentRole != EnemySquadRole.Retreater)
        {
            ApplyBackstabberFlavor(ref style, intensity, close);
        }
        else if (effectivePersonality == Personality.ScaredCat ||
                 effectivePersonality == Personality.Avenger)
        {
            ApplyCautiousFlavor(ref style, intensity, close);
        }

        if (usePressureAttackIntensity)
            ApplyGlobalPressureIntensity(ref style, intensity);

        NormalizeAttackStyle(ref style);

        string sourcePrefix =
            style.debugLabel != null &&
            style.debugLabel.StartsWith("RoleProfile")
                ? style.debugLabel + " "
                : string.Empty;

        style.debugLabel =
            $"{sourcePrefix}{currentRole}/{effectivePersonality} {intensityName} {style.patternName} " +
            $"shots:{style.shotsPerBurst} fan:{style.fanBullets}/{style.fanArcDegrees:0}";

        return style;
    }

    private string GetDistanceBasedPattern()
    {
        float distance = Vector2.Distance(
            transform.position,
            GetPlayerPos()
        );

        if (distance < closeRangeOverrideDistance)
            return "BoI_8Way";

        return distance < profile.preferredMinRange
            ? profile.closePattern
            : distance > profile.preferredMaxRange
                ? profile.farPattern
                : profile.midPattern;
    }

    private int GetAttackIntensity(float distanceToPlayer)
    {
        if (!usePressureAttackIntensity)
            return 0;

        float medium = Mathf.Clamp01(mediumAttackIntensityPressure);
        float high = Mathf.Clamp01(highAttackIntensityPressure);

        if (squadPressure01 >= high)
            return 2;

        if (squadPressure01 >= medium)
            return 1;

        // Being close is dangerous even before the squad pressure meter rises.
        if (distanceToPlayer <= Mathf.Max(closeRangeOverrideDistance, identityCloseRangeDistance))
            return 1;

        return 0;
    }

    private bool TryApplyRoleAttackProfileIdentity(
        ref AttackStyle style,
        int intensity,
        bool close)
    {
        if (!useRoleAttackProfiles || roleAttackProfiles == null)
            return false;

        EnemyRoleAttackProfiles.ResolvedAttack resolved;

        if (!roleAttackProfiles.TryResolve(
                currentRole,
                intensity,
                close,
                out resolved))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(resolved.patternName))
            style.patternName = resolved.patternName;

        if (resolved.minShotsPerBurst > 0)
        {
            style.shotsPerBurst = Mathf.Max(
                style.shotsPerBurst,
                resolved.minShotsPerBurst
            );
        }

        if (resolved.minBurstsPerEnable > 0)
        {
            style.burstsPerEnable = Mathf.Max(
                style.burstsPerEnable,
                resolved.minBurstsPerEnable
            );
        }

        if (resolved.minFanBullets > 0)
        {
            style.fanBullets = Mathf.Max(
                style.fanBullets,
                resolved.minFanBullets
            );
        }

        if (resolved.fanArcDegrees >= 0f)
            style.fanArcDegrees = resolved.fanArcDegrees;

        if (resolved.minRingBullets > 0)
        {
            style.ringBullets = Mathf.Max(
                style.ringBullets,
                resolved.minRingBullets
            );
        }

        if (resolved.angularSpeedDegPerTick >= 0f)
            style.angularSpeedDegPerTick = resolved.angularSpeedDegPerTick;

        style.intraBurstInterval *= Mathf.Max(
            0.01f,
            resolved.intraBurstIntervalMultiplier
        );

        style.burstCooldown *= Mathf.Max(
            0.01f,
            resolved.burstCooldownMultiplier
        );

        style.pressureRearmDelay *= Mathf.Max(
            0.01f,
            resolved.pressureRearmDelayMultiplier
        );

        if (resolved.minimumPostBurstReplanDelay > 0f)
        {
            style.postBurstReplanDelay = Mathf.Max(
                style.postBurstReplanDelay,
                resolved.minimumPostBurstReplanDelay
            );
        }

        style.debugLabel = $"RoleProfile:{resolved.sourceLabel}";
        return true;
    }

    private void ApplySuppressorIdentity(ref AttackStyle style, int intensity, bool close)
    {
        if (usePrettyDanmakuPatterns)
        {
            style.patternName = close
                ? "CloseCross"
                : intensity >= 2
                    ? "ClosingBlossom"
                    : intensity >= 1
                        ? "StaggeredRosette"
                        : "PetalFan";
        }
        else
        {
            style.patternName = close
                ? "BoI_8Way"
                : intensity >= 2
                    ? "SweepFan"
                    : "AimedFan";
        }

        style.fanBullets = Mathf.Max(style.fanBullets, intensity >= 2 ? 7 : 5);
        style.fanArcDegrees = Mathf.Max(style.fanArcDegrees, intensity >= 2 ? 62f : 46f);
        style.shotsPerBurst = Mathf.Max(style.shotsPerBurst, intensity >= 1 ? 2 : 1);
        style.intraBurstInterval *= intensity >= 2 ? 0.85f : 0.95f;
        style.burstCooldown *= 0.90f;
        style.postBurstReplanDelay = Mathf.Max(style.postBurstReplanDelay, 0.18f);
    }

    private void ApplyFlankerIdentity(ref AttackStyle style, int intensity, bool close)
    {
        if (usePrettyDanmakuPatterns)
        {
            style.patternName = close
                ? "ButterflySpread"
                : intensity >= 2
                    ? "EscapeCutoff"
                    : intensity >= 1
                        ? "ButterflySpread"
                        : "AimedSingle";
        }
        else
        {
            style.patternName = close
                ? "AimedFan"
                : intensity >= 2
                    ? "AimedFan"
                    : "AimedSingle";
        }

        style.fanBullets = Mathf.Max(3, Mathf.Min(style.fanBullets, intensity >= 2 ? 5 : 4));
        style.fanArcDegrees = intensity >= 2 ? 34f : 24f;
        style.shotsPerBurst = Mathf.Max(1, style.shotsPerBurst);
        style.intraBurstInterval *= 0.82f;
        style.burstCooldown *= 0.78f;
        style.postBurstReplanDelay = quickAttackPunishDelay;
        style.pressureRearmDelay *= 0.85f;
    }

    private void ApplyAnchorIdentity(ref AttackStyle style, int intensity, bool close)
    {
        if (usePrettyDanmakuPatterns)
        {
            style.patternName = close
                ? "RotatingFlowerRing"
                : intensity >= 2
                    ? "HaloSpear"
                    : intensity >= 1
                        ? "RotatingFlowerRing"
                        : "CloseCross";
        }
        else
        {
            style.patternName = close
                ? "BoI_8Way"
                : intensity >= 1
                    ? "Ring"
                    : "BoI_4Way";
        }

        style.ringBullets = Mathf.Max(style.ringBullets, intensity >= 2 ? 14 : 10);
        style.shotsPerBurst = 1;
        style.burstCooldown *= 1.15f;
        style.postBurstReplanDelay = Mathf.Max(style.postBurstReplanDelay, dangerousAttackPunishDelay);
    }

    private void ApplyRetreaterIdentity(ref AttackStyle style, int intensity, bool close)
    {
        style.patternName = close && intensity >= 1
            ? (usePrettyDanmakuPatterns ? "PetalFan" : "AimedFan")
            : "AimedSingle";

        style.fanBullets = 3;
        style.fanArcDegrees = 28f;
        style.shotsPerBurst = 1;
        style.burstCooldown *= 1.10f;
        style.postBurstReplanDelay = Mathf.Max(style.postBurstReplanDelay, 0.20f);
    }

    private void ApplyPersonalityFallbackIdentity(ref AttackStyle style, int intensity, bool close)
    {
        if (close)
            style.patternName = usePrettyDanmakuPatterns ? "CloseCross" : "BoI_8Way";
        else if (intensity >= 2)
            style.patternName = usePrettyDanmakuPatterns ? "PetalFan" : "AimedFan";
    }

    private void ApplyAggressiveFlavor(ref AttackStyle style, int intensity, bool close)
    {
        if (close)
        {
            style.patternName = usePrettyDanmakuPatterns ? "CloseCross" : "BoI_8Way";
            style.postBurstReplanDelay = Mathf.Max(style.postBurstReplanDelay, dangerousAttackPunishDelay);
        }
        else if (intensity >= 1 && currentRole != EnemySquadRole.Anchor)
        {
            style.patternName = usePrettyDanmakuPatterns ? "CrescentSweep" : "AimedFan";
            style.fanArcDegrees = Mathf.Max(style.fanArcDegrees, 38f);
        }

        style.shotsPerBurst += intensity >= 2 ? 1 : 0;
        style.intraBurstInterval *= 0.90f;
        style.burstCooldown *= 0.85f;
    }

    private void ApplyBackstabberFlavor(ref AttackStyle style, int intensity, bool close)
    {
        if (!close && currentRole != EnemySquadRole.Suppressor)
        {
            style.patternName = usePrettyDanmakuPatterns
                ? (intensity >= 1 ? "EscapeCutoff" : "ButterflySpread")
                : (intensity >= 1 ? "AimedFan" : "AimedSingle");
        }

        style.fanArcDegrees = Mathf.Min(Mathf.Max(style.fanArcDegrees, 18f), 36f);
        style.intraBurstInterval *= 0.90f;
        style.burstCooldown *= 0.82f;
    }

    private void ApplyCautiousFlavor(ref AttackStyle style, int intensity, bool close)
    {
        if (!close && currentRole != EnemySquadRole.Anchor)
            style.patternName = intensity >= 2 ? "AimedFan" : "AimedSingle";

        style.burstCooldown *= 1.05f;
    }

    private void ApplyGlobalPressureIntensity(ref AttackStyle style, int intensity)
    {
        if (intensity <= 0)
            return;

        bool pretty = IsPrettyDanmakuPattern(style.patternName);
        bool canAddPrettyBullets = !pretty || !usePrettyPatternDensitySafety || allowHighPressureExtraPrettyPatternBullets;
        bool canAddPrettyBursts = !pretty || !usePrettyPatternDensitySafety || allowHighPressureExtraPrettyPatternBursts;

        if (IsFanLikePattern(style.patternName))
        {
            if (canAddPrettyBullets)
                style.fanBullets += intensity >= 2 ? highPressureExtraFanBullets : 0;

            style.fanArcDegrees += intensity >= 2 ? (pretty ? 4f : 8f) : (pretty ? 2f : 4f);
        }

        if (IsRingLikePattern(style.patternName))
        {
            if (canAddPrettyBullets)
                style.ringBullets += intensity >= 2 ? highPressureExtraRingBullets : 2;
        }

        if (intensity >= 2 &&
            canAddPrettyBursts &&
            currentRole != EnemySquadRole.Anchor &&
            currentRole != EnemySquadRole.Retreater)
        {
            style.shotsPerBurst += highPressureExtraBurstShots;
        }

        style.intraBurstInterval *= intensity >= 2 ? 0.86f : 0.94f;
        style.burstCooldown *= intensity >= 2 ? 0.88f : 0.94f;
        style.pressureRearmDelay *= intensity >= 2 ? 0.82f : 0.92f;
    }

    private bool IsPrettyDanmakuPattern(string patternName)
    {
        return patternName == "PetalFan" ||
               patternName == "ButterflySpread" ||
               patternName == "ClosingBlossom" ||
               patternName == "RotatingFlowerRing" ||
               patternName == "StaggeredRosette" ||
               patternName == "CrescentSweep" ||
               patternName == "BraidedStream" ||
               patternName == "HaloSpear" ||
               patternName == "CloseCross" ||
               patternName == "EscapeCutoff";
    }

    private bool IsFanLikePattern(string patternName)
    {
        return patternName == "AimedFan" ||
               patternName == "SweepFan" ||
               patternName == "PetalFan" ||
               patternName == "ButterflySpread" ||
               patternName == "ClosingBlossom" ||
               patternName == "StaggeredRosette" ||
               patternName == "CrescentSweep" ||
               patternName == "BraidedStream" ||
               patternName == "EscapeCutoff";
    }

    private bool IsRingLikePattern(string patternName)
    {
        return patternName == "Ring" ||
               patternName == "BoI_4Way" ||
               patternName == "BoI_8Way" ||
               patternName == "RotatingFlowerRing" ||
               patternName == "HaloSpear" ||
               patternName == "CloseCross";
    }

    private Color GetProjectileTintForCurrentStyle()
    {
        if (useRoleAttackProfiles && roleAttackProfiles != null)
        {
            Color profileTint;

            if (roleAttackProfiles.TryGetProjectileTint(
                    currentRole,
                    out profileTint))
            {
                return profileTint;
            }
        }

        if (effectivePersonality == Personality.Aggressive &&
            currentRole != EnemySquadRole.Anchor &&
            currentRole != EnemySquadRole.Retreater)
        {
            return aggressiveTint;
        }

        switch (currentRole)
        {
            case EnemySquadRole.Suppressor:
                return suppressorTint;
            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                return flankerTint;
            case EnemySquadRole.Anchor:
                return anchorTint;
            case EnemySquadRole.Retreater:
                return retreaterTint;
            default:
                return effectivePersonality == Personality.Backstabber
                    ? flankerTint
                    : suppressorTint;
        }
    }

    private void NormalizeAttackStyle(ref AttackStyle style)
    {
        if (string.IsNullOrWhiteSpace(style.patternName))
            style.patternName = "AimedFan";

        if (usePrettyPatternDensitySafety && IsPrettyDanmakuPattern(style.patternName))
        {
            style.shotsPerBurst = Mathf.Min(
                style.shotsPerBurst,
                Mathf.Max(1, maxPrettyPatternShotsPerBurst)
            );

            if (IsFanLikePattern(style.patternName))
            {
                style.fanBullets = Mathf.Min(
                    style.fanBullets,
                    Mathf.Max(1, maxPrettyFanBullets)
                );

                style.fanArcDegrees = Mathf.Min(
                    style.fanArcDegrees,
                    Mathf.Max(1f, maxPrettyFanArcDegrees)
                );
            }

            if (IsRingLikePattern(style.patternName))
            {
                style.ringBullets = Mathf.Min(
                    style.ringBullets,
                    Mathf.Max(3, maxPrettyRingBullets)
                );
            }

            style.postBurstReplanDelay = Mathf.Max(
                style.postBurstReplanDelay + prettyPatternExtraPunishDelay,
                minPrettyPatternPostBurstDelay
            );

            style.pressureRearmDelay = Mathf.Max(
                style.pressureRearmDelay,
                minPrettyPatternPressureRearmDelay
            );
        }

        style.shotsPerBurst = Mathf.Clamp(style.shotsPerBurst, 1, 6);
        style.intraBurstInterval = Mathf.Clamp(style.intraBurstInterval, 0.045f, 0.35f);
        style.burstCooldown = Mathf.Clamp(style.burstCooldown, 0.22f, 2.0f);
        style.burstsPerEnable = Mathf.Clamp(style.burstsPerEnable, 1, 3);
        style.fanBullets = Mathf.Clamp(style.fanBullets, 1, 9);
        style.fanArcDegrees = Mathf.Clamp(style.fanArcDegrees, 0f, 90f);
        style.ringBullets = Mathf.Clamp(style.ringBullets, 3, 20);
        style.angularSpeedDegPerTick = Mathf.Clamp(style.angularSpeedDegPerTick, 2f, 40f);
        style.postBurstReplanDelay = Mathf.Clamp(style.postBurstReplanDelay, 0.02f, 1.25f);
        style.pressureRearmDelay = Mathf.Clamp(style.pressureRearmDelay, 0f, 1.25f);
    }

    private bool TrySetShooterPatternByName(string patternName)
    {
        if (shooter == null || string.IsNullOrEmpty(patternName)) return false;
        if (TryCall(shooter, "SetPattern", new object[] { patternName })) return true;

        Type shooterType = shooter.GetType();
        Type enumType = shooterType.GetNestedType("PatternType", BindingFlags.Public | BindingFlags.NonPublic);
        if (enumType == null || !enumType.IsEnum) return false;
        try
        {
            object enumVal = Enum.Parse(enumType, patternName, true);
            return TryCall(shooter, "SetPattern", new object[] { enumVal });
        }
        catch { return false; }
    }

    // ----------------------------
    // LOS and flee
    // ----------------------------
    private bool RoleNeedsStrictLos()
    {
        if (!isRanged) return false;
        if (strictLosForAllRanged) return true;
        if (currentRole == EnemySquadRole.Anchor && strictLosForAnchor) return true;
        if (currentRole == EnemySquadRole.Suppressor && strictLosForSuppressor) return true;
        return false;
    }

    private bool HasLineOfSightToPlayerNow()
    {
        if (playerTransform == null)
            return false;

        return CombatLineOfSight2D.HasLineOfSight(
            this,
            transform.position,
            playerTransform,
            losBlockMask,
            out _
        );
    }

    private bool ShouldFlee(out bool panicFight)
    {
        panicFight = false;
        if (profile.fleeAtHealth01 <= 0f || Health01 > profile.fleeAtHealth01) return false;
        float dist = Vector2.Distance(transform.position, GetPlayerPos());
        if (dist <= profile.panicDistance) { panicFight = true; return false; }
        if (effectivePersonality == Personality.Aggressive) return false;
        return true;
    }

    private bool IsInOpening() => Time.time < openingUntil;
    private bool IsUnderFire() => Time.time < underFireUntil;

    // ----------------------------
    // Player acquisition
    // ----------------------------
    private void ResolvePlayerTransform()
    {
        if (!autoFindPlayerByTag || playerTransform != null) return;
        if (Time.time < nextPlayerSearchTime) return;
        nextPlayerSearchTime = Time.time + playerSearchInterval;
        GameObject playerObj = null;
        try { playerObj = GameObject.FindWithTag(playerTag); } catch { return; }
        if (playerObj != null) playerTransform = playerObj.transform;
    }

    private Vector2 GetPlayerPos() =>
        playerTransform != null ? (Vector2)playerTransform.position : sharedPlayerPos;

    // ----------------------------
    // Role tuning
    // ----------------------------
    private RoleShooterTuning GetRoleTuning()
    {
        switch (currentRole)
        {
            case EnemySquadRole.Suppressor: return suppressorShooter;
            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight: return flankerShooter;
            case EnemySquadRole.Retreater: return retreaterShooter;
            default: return anchorShooter;
        }
    }

    // ----------------------------
    // Personality
    // ----------------------------
    private PersonalityProfile GetProfileFor(Personality p)
    {
        switch (p)
        {
            case Personality.Aggressive: return aggressiveProfile.Clone();
            case Personality.ScaredCat: return scaredCatProfile.Clone();
            case Personality.Backstabber: return backstabberProfile.Clone();
            case Personality.Avenger: return avengerProfile.Clone();
            default: return aggressiveProfile.Clone();
        }
    }

    private Personality RollRandomPersonality()
    {
        float a = Mathf.Max(0f, wAggressive), s = Mathf.Max(0f, wScaredCat);
        float b = Mathf.Max(0f, wBackstabber), v = Mathf.Max(0f, wAvenger);
        float total = a + s + b + v;
        if (total <= 0.0001f) return Personality.Aggressive;
        float r = UnityEngine.Random.value * total;
        if (r < a) return Personality.Aggressive; r -= a;
        if (r < s) return Personality.ScaredCat; r -= s;
        if (r < b) return Personality.Backstabber;
        return Personality.Avenger;
    }

    private Personality ReadExternalPersonalityOrFallback(Personality fallback)
    {
        var exState = GetComponent("EnemyPersonalityState");
        if (exState == null) return fallback;
        object val = TryGetFieldOrProperty(exState, "Current")
                  ?? TryGetFieldOrProperty(exState, "Personality")
                  ?? TryGetFieldOrProperty(exState, "basePersonality");
        if (val == null) return fallback;
        try { if (Enum.TryParse(val.ToString(), true, out Personality p)) return p; } catch { }
        return fallback;
    }

    private bool HasExternalPersonalityState() => GetComponent("EnemyPersonalityState") != null;

    private bool IsLastEnemyAlive()
    {
        int alive = 0;
        foreach (var b in FindObjectsOfType<EnemyBrain>(false))
            if (b != null && b.IsAlive) alive++;
        return alive <= 1;
    }

    // ----------------------------
    // Health (reflection-safe)
    // ----------------------------
    private int GetCurrentHP()
    {
        if (enemyHealth == null) return 1;
        object v = TryGetFieldOrProperty(enemyHealth, "CurrentHP")
                ?? TryGetFieldOrProperty(enemyHealth, "currentHP");
        return v is int i ? i : 1;
    }

    private float GetHealth01()
    {
        if (enemyHealth == null) return 1f;
        object h = TryGetFieldOrProperty(enemyHealth, "Health01");
        if (h is float f) return Mathf.Clamp01(f);
        object cur = TryGetFieldOrProperty(enemyHealth, "CurrentHP") ?? TryGetFieldOrProperty(enemyHealth, "currentHP");
        object max = TryGetFieldOrProperty(enemyHealth, "MaxHP") ?? TryGetFieldOrProperty(enemyHealth, "maxHP");
        if (cur is int c && max is int m && m > 0) return Mathf.Clamp01((float)c / m);
        return 1f;
    }

    // ----------------------------
    // Reflection utilities
    // ----------------------------
    private static bool TryCall(object obj, string methodName, object[] args)
    {
        if (obj == null) return false;
        foreach (var method in obj.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != methodName || method.GetParameters().Length != args.Length) continue;
            try { method.Invoke(obj, args); return true; } catch { }
        }
        return false;
    }

    private static object TryGetFieldOrProperty(object obj, string name)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return null;
        Type t = obj.GetType();
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f.GetValue(obj);
        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (p != null && p.CanRead) ? p.GetValue(obj) : null;
    }

    private static bool TrySetFieldOrProperty(object obj, string name, object value)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return false;
        Type t = obj.GetType();
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) { try { f.SetValue(obj, Convert.ChangeType(value, f.FieldType)); return true; } catch { } }
        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite) { try { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType)); return true; } catch { } }
        return false;
    }

    // ----------------------------
    // Gizmos
    // ----------------------------
    private void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;

        if (hasMoveIntent)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + desiredVelocity * 0.25f);
        }

        if (currentPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, currentPoint.transform.position);
            Gizmos.DrawWireSphere(currentPoint.transform.position, 0.12f);
        }

        if (isRepositioning)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(repositionTarget, 0.18f);
            Gizmos.DrawLine(transform.position, repositionTarget);
        }

        if (usingEscapeWaypoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(escapeWaypointTarget, 0.22f);
            Gizmos.DrawLine(transform.position, escapeWaypointTarget);
        }

        if (advanceStuckCount > 0)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.18f + advanceStuckCount * 0.06f);
        }

        if (squadPressure01 > 0.05f)
        {
            Gizmos.color = new Color(1f, 0f, 0f, squadPressure01 * 0.5f);
            Gizmos.DrawWireSphere(transform.position, 0.2f + squadPressure01 * 0.15f);
        }
    }
}