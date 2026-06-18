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
    private bool shooterArmedThisWindow = false;
    private float noLosTimer = 0f;
    private float openingUntil = 0f;
    private float underFireUntil = 0f;
    private float nextAvengerCheckTime = 0f;

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

    // ----------------------------
    // Unity
    // ----------------------------
    private void Awake()
    {
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
        if (shooter == null) shooter = GetComponent<EnemyShooterDebug>();
        if (rb == null) rb = GetComponent<Rigidbody2D>();
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
        ResetAdvanceStuck();
        ResetRepositionState();
        SetShooterGate(false, force: true);
        ClearMoveIntent();
        EnterReplan(true);
    }

    private void OnDisable()
    {
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
            + (isRepositioning ? " [REPOSITION]" : "");
        debugInWindowTime = inWindowTime;
        debugRepositionsThisWindow = repositionsThisWindow;
        debugAdvanceStuck = advanceStuckCount > 0 ? $"stuck x{advanceStuckCount}" : "";
    }

    private void FixedUpdate()
    {
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

        var speedMod = GetComponent<SpeedModifier>();
        float speedMult = speedMod != null ? speedMod.Multiplier : 1f;

        if (rb != null)
            rb.MovePosition(rb.position + desiredVelocity * speedMult * Time.fixedDeltaTime);
        else
            transform.position += (Vector3)(desiredVelocity * speedMult * Time.fixedDeltaTime);
    }

    // ----------------------------
    // IEnemySquadAgent public hooks
    // ----------------------------
    public void SetRole(EnemySquadRole role) => currentRole = role;

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
        noLosTimer = 0f;
        isRepositioning = false;
        SetShooterGate(false);
        ClearMoveIntent();
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
                nextStuckCheckTime = Time.time + stuckCheckInterval;
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
                pointLockedUntil = Time.time + minPointLockTime;
            }

            usingFallbackChase = false;
            state = BrainState.Move;
            nextStuckCheckTime = Time.time + stuckCheckInterval;
            lastStuckPos = transform.position;
            return;
        }

        BeginFallbackChase();
    }

    private void BeginFallbackChase()
    {
        usingFallbackChase = true;
        currentPoint = null;
        shooterArmedThisWindow = false;
        state = BrainState.Move;
        nextStuckCheckTime = Time.time + stuckCheckInterval;
        lastStuckPos = transform.position;
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

            MoveTowardTarget(escapeWaypointTarget, moveSpeed);

            // Stuck while even navigating to the escape waypoint — give up and replan normally
            if (Time.time >= nextStuckCheckTime)
            {
                float traveled = Vector2.Distance((Vector2)transform.position, lastStuckPos);
                if (traveled < stuckMinTravel)
                {
                    usingEscapeWaypoint = false;
                    ResetAdvanceStuck();
                    pointLockedUntil = 0f;
                    EnterReplan(false);
                    return;
                }
                lastStuckPos = transform.position;
                nextStuckCheckTime = Time.time + stuckCheckInterval;
            }
            return;
        }

        // --- Normal move ---
        Vector2 playerPos = GetPlayerPos();
        Vector2 target;
        float arrivalRadius;

        if (usingFallbackChase || currentPoint == null)
        {
            target = playerPos;
            arrivalRadius = fallbackArrivalRadius;
        }
        else
        {
            target = currentPoint.Position;
            arrivalRadius = Mathf.Max(0.08f, currentPoint.arrivalRadius);
        }

        Vector2 myPos = rb != null ? rb.position : (Vector2)transform.position;

        if (Vector2.Distance(myPos, target) <= arrivalRadius)
        {
            ClearMoveIntent();
            // Arrived at player position during fallback chase — reset advance stuck since
            // we successfully navigated there
            if (usingFallbackChase) ResetAdvanceStuck();
            EnterSettle();
            return;
        }

        float speed = moveSpeed;
        if (currentRole == EnemySquadRole.Retreater) speed *= retreatSpeedMultiplier;
        MoveTowardTarget(target, speed);

        if (Time.time >= nextStuckCheckTime)
        {
            float traveled = Vector2.Distance((Vector2)transform.position, lastStuckPos);
            if (traveled < stuckMinTravel)
            {
                pointLockedUntil = 0f;

                // Track how many times we've been stuck during a pressure advance
                if (usingFallbackChase && squadPressure01 >= advancePressureThreshold)
                    advanceStuckCount++;

                EnterReplan(true);
                return;
            }
            lastStuckPos = transform.position;
            nextStuckCheckTime = Time.time + stuckCheckInterval;
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
        if (Time.time >= nextStateTime) EnterAttackWindow();

        // Pace within the zone while waiting to enter attack window.
        // Shooter is off during settle — this is purely movement.
        TickStrafe(idleWanderSpeedMultiplier);
    }

    private void EnterAttackWindow()
    {
        state = BrainState.AttackWindow;
        shooterArmedThisWindow = false;
        noLosTimer = 0f;
        inWindowTime = 0f;
        ResetRepositionState();

        // Pick first strafe target immediately on window entry
        nextStrafePickTime = Time.time;
        isStrafeMoving = false;

        RoleShooterTuning t = GetRoleTuning();
        TryCall(shooter, "SetFireInterval", new object[] { t.fireInterval });
        TryCall(shooter, "SetAimLag", new object[] { t.aimLag });
        ApplyFiringStyleForThisWindow();

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
                usingFallbackChase = true;
                currentPoint = null;
                state = BrainState.Move;
                nextStuckCheckTime = Time.time + stuckCheckInterval;
                lastStuckPos = transform.position;
                return;
            }
        }

        if (Time.time < attackEnableTime) return;

        if (!shooterArmedThisWindow)
        {
            SyncShooterTarget();
            TryCall(shooter, "ForceReadyToFire", new object[] { 0f });
            SetShooterGate(true);
            shooterArmedThisWindow = true;
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

            bool hasLos = !Physics2D.Linecast(candidate, playerPos, losBlockMask);
            if (!hasLos) continue;

            Vector2 newAimDir = (playerPos - candidate).normalized;
            float angleDelta = Vector2.Angle(currentAimDir, newAimDir);
            float angleScore = angleDelta / 90f;
            float rangeScore = ScoreRangeFit(Vector2.Distance(candidate, playerPos));
            float novelty = Vector2.Distance(candidate, myPos) > 0.4f ? 0.4f : 0f;
            float drift = Vector2.Distance(candidate, zoneCenter) / radius * 0.3f;

            float score = angleScore * 2.0f + rangeScore * 0.8f + novelty - drift;

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
            TryCall(shooter, "ForceReadyToFire", new object[] { 0f });
            SetShooterGate(true);
            shooterArmedThisWindow = true;
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

                // Must have LOS and be meaningfully different from current position
                bool hasLos = !Physics2D.Linecast(candidate, playerPos, losBlockMask);
                bool isNovel = Vector2.Distance(candidate, myPos) > 0.2f;
                if (hasLos && isNovel) { bestCandidate = candidate; found = true; break; }
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
    private void MoveTowardTarget(Vector2 target, float speed)
    {
        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 toTarget = target - pos;
        if (toTarget.sqrMagnitude < 0.0001f) { ClearMoveIntent(); return; }

        Vector2 dir = toTarget.normalized;

        bool blocked = Physics2D.Raycast(pos, dir, obstacleProbeDistance, losBlockMask);
        if (blocked)
        {
            if (Time.time >= avoidSideUntil || avoidSideSign == 0)
            {
                avoidSideSign = UnityEngine.Random.value < 0.5f ? -1 : 1;
                avoidSideUntil = Time.time + 0.30f;
            }
            Vector2 perp = avoidSideSign > 0
                ? new Vector2(-dir.y, dir.x)
                : new Vector2(dir.y, -dir.x);
            dir = (dir * 0.35f + perp * 0.65f).normalized;
            if (Physics2D.Raycast(pos, dir, obstacleProbeDistance * 0.90f, losBlockMask))
            {
                ClearMoveIntent();
                return;
            }
        }

        desiredVelocity = dir * speed;
        hasMoveIntent = true;
    }

    private void ClearMoveIntent()
    {
        desiredVelocity = Vector2.zero;
        hasMoveIntent = false;
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
            if (p == null || !p.CanBeUsedBy(this, Time.time)) continue;

            float score = 0f;
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

            bool pointHasLos = !Physics2D.Linecast(p.Position, playerPos, losBlockMask);
            if (currentRole == EnemySquadRole.Anchor || currentRole == EnemySquadRole.Suppressor)
                score += pointHasLos ? 1.2f : -2.4f;

            float distToPlayer = Vector2.Distance(p.Position, playerPos);
            score += ScoreRangeFit(distToPlayer) * rangeFitWeight;

            if (currentRole == EnemySquadRole.FlankerLeft || currentRole == EnemySquadRole.FlankerRight)
                score += ScoreFlankSide(p.Position, playerPos) * flankWeight;

            float travel = Vector2.Distance(myPos, p.Position);
            score -= travel * travelCostWeight * 0.2f;
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

    private void ReleaseCurrentPoint()
    {
        if (currentPoint == null) return;
        currentPoint.Release(this, Time.time);
        currentPoint = null;
    }

    // ----------------------------
    // Shooter integration
    // ----------------------------
    private void SetShooterGate(bool enabled, bool force = false)
    {
        if (shooter == null) return;

        bool finalEnabled = enabled && isRanged;

        if (!force && shooterGateEnabled == finalEnabled)
            return;

        shooterGateEnabled = finalEnabled;
        TryCall(shooter, "SetShootingEnabled", new object[] { finalEnabled });
    }

    private void SyncShooterTarget()
    {
        if (shooter == null || playerTransform == null) return;
        if (lastSyncedShooterTarget != playerTransform)
        {
            TryCall(shooter, "SetTarget", new object[] { playerTransform });
            lastSyncedShooterTarget = playerTransform;
        }
    }

    private void ApplyFiringStyleForThisWindow()
    {
        if (shooter == null) return;

        TryCall(shooter, "SetBurstConfig", new object[] { profile.shotsPerBurst, profile.intraBurstInterval, profile.burstCooldown });
        TryCall(shooter, "SetBurstQuotaPerEnable", new object[] { true, Mathf.Max(1, profile.burstsPerEnable) });

        float dist = Vector2.Distance(transform.position, GetPlayerPos());

        // Close-range pattern forcing
        string chosen;
        if (dist < closeRangeOverrideDistance)
            chosen = "BoI_8Way";
        else
            chosen = dist < profile.preferredMinRange ? profile.closePattern
                   : dist > profile.preferredMaxRange ? profile.farPattern
                   : profile.midPattern;

        TrySetShooterPatternByName(chosen);

        TrySetFieldOrProperty(shooter, "fanBullets", profile.fanBullets);
        TrySetFieldOrProperty(shooter, "fanArcDegrees", profile.fanArcDegrees);
        TrySetFieldOrProperty(shooter, "ringBullets", profile.ringBullets);
        TrySetFieldOrProperty(shooter, "angularSpeedDegPerTick", profile.angularSpeedDegPerTick);
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
        if (playerTransform == null) return false;
        RaycastHit2D[] hits = Physics2D.LinecastAll(transform.position, playerTransform.position, losBlockMask);
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform.root == transform.root) continue;
            return hit.collider.transform.root == playerTransform.root;
        }
        return true;
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