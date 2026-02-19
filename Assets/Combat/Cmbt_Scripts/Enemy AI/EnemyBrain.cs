using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// EnemyBrain
/// - Works with your squad roles (Anchor/Suppressor/Flanker/Retreater) and tactical points.
/// - Adds "Personality" that is randomly assigned per enemy (unless overridden).
/// - Personality biases movement, range, flee rules, and shooter burst + pattern selection.
/// - Fixes "Anchor shoots into walls" by hard LOS gating with quick replan.
/// - Avoids the old "cooldown constantly" bug by only enabling shooter once per attack window.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBrain : MonoBehaviour, IEnemySquadAgent
{
    // ----------------------------
    // Personality (self-contained)
    // ----------------------------
    public enum Personality
    {
        Aggressive,
        ScaredCat,
        Backstabber,
        Avenger
    }

    public enum OpeningAction
    {
        None,
        ChargePlayer,
        RunToCover,
        SeekFlank
    }

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
        [Range(0f, 1f)] public float fleeAtHealth01 = 0.35f;   // 0 = never flee
        [Min(0f)] public float panicDistance = 1.3f;           // if player closer than this, do not flee, fight

        [Header("Point Bias (added to tactical point score)")]
        public float coverBias = 0f;
        public float flankBias = 0f;
        public float fireBias = 0f;

        [Header("Behavior Gates")]
        public bool mustBeInPreferredRangeToShoot = false;     // Aggressive style
        public bool preferFlanksAlways = false;                // Backstabber style

        [Header("Burst Style (pushed to shooter if methods exist)")]
        [Min(1)] public int shotsPerBurst = 2;
        [Min(0.01f)] public float intraBurstInterval = 0.12f;
        [Min(0.01f)] public float burstCooldown = 0.90f;
        [Min(1)] public int burstsPerEnable = 1;

        [Header("Pattern Names (string, so this script does not hard depend on your shooter enum)")]
        public string farPattern = "AimedSingle";
        public string midPattern = "AimedFan";
        public string closePattern = "BoI_8Way";

        [Header("Pattern Params (optional, only applied if shooter exposes matching fields/methods)")]
        [Min(1)] public int fanBullets = 5;
        [Min(0f)] public float fanArcDegrees = 35f;
        [Min(3)] public int ringBullets = 12;
        [Min(0f)] public float angularSpeedDegPerTick = 12f;

        public PersonalityProfile Clone()
        {
            return (PersonalityProfile)MemberwiseClone();
        }
    }

    [Header("Personality Assignment")]
    [SerializeField] private bool randomizePersonalityOnEnable = true;

    [Tooltip("If you have a separate EnemyPersonalityState component, EnemyBrain will read it automatically (via reflection) and override this.")]
    [SerializeField] private Personality personality = Personality.Aggressive;

    [Tooltip("Global odds for random personality assignment. Values are weights, not percentages.")]
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
    [Tooltip("Walls and cover that block line of sight and also count as obstacles for movement probe.")]
    [SerializeField] private LayerMask losBlockMask;

    [Header("Strict LOS gate (fix for anchors shooting into walls)")]
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

    [Header("Role Shooter Tuning (base)")]
    [SerializeField]
    private RoleShooterTuning suppressorShooter = new RoleShooterTuning
    {
        fireInterval = 0.20f,
        aimLag = 0.04f,
        settleWindow = new Vector2(0.04f, 0.10f),
        attackWindow = new Vector2(2.8f, 4.2f),
        firstShotDelay = 0.00f
    };

    [SerializeField]
    private RoleShooterTuning anchorShooter = new RoleShooterTuning
    {
        fireInterval = 0.28f,
        aimLag = 0.06f,
        settleWindow = new Vector2(0.05f, 0.12f),
        attackWindow = new Vector2(2.4f, 3.4f),
        firstShotDelay = 0.00f
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

    [Header("Scoring (role + general)")]
    [SerializeField] private float occupiedPenalty = 1.4f;
    [SerializeField] private float travelCostWeight = 0.25f;
    [SerializeField] private float rangeFitWeight = 1.2f;
    [SerializeField] private float flankWeight = 1.6f;
    [SerializeField] private float fireWeight = 1.9f;
    [SerializeField] private float coverWeight = 0.65f;

    [Header("Backstabber pressure response")]
    [SerializeField] private float underFireSeconds = 1.1f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;
    [SerializeField] private string debugState = "None";
    [SerializeField] private Personality effectivePersonality;
    [SerializeField] private EnemySquadRole currentRole = EnemySquadRole.None;
    [SerializeField] private bool usingFallbackChase = false;
    [SerializeField] private CombatTacticalPoint currentPoint;

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

    private enum BrainState
    {
        Replan,
        Move,
        Settle,
        AttackWindow
    }

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
        // Register with squad if present
        squad = FindObjectOfType<EnemySquadCoordinator>(true);
        if (squad != null) squad.Register(this);

        // Personality: read external state if present, otherwise randomize if requested
        personality = ReadExternalPersonalityOrFallback(personality);

        if (randomizePersonalityOnEnable)
        {
            // If an external state exists, it already decided personality.
            // If not, roll now.
            if (!HasExternalPersonalityState())
                personality = RollRandomPersonality();
        }

        effectivePersonality = personality;
        profile = GetProfileFor(effectivePersonality);

        openingUntil = Time.time + Mathf.Max(0f, profile.openingDuration);
        underFireUntil = 0f;

        lastStuckPos = transform.position;

        SetShooterGate(false);
        ClearMoveIntent();
        EnterReplan(true);
    }

    private void OnDisable()
    {
        if (squad != null) squad.Unregister(this);
        ReleaseCurrentPoint();
        SetShooterGate(false);
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

        // Avenger: if last alive, flip to Aggressive
        if (effectivePersonality == Personality.Avenger && Time.time >= nextAvengerCheckTime)
        {
            nextAvengerCheckTime = Time.time + 0.25f;
            if (IsLastEnemyAlive())
            {
                effectivePersonality = Personality.Aggressive;
                profile = GetProfileFor(effectivePersonality);
                openingUntil = Time.time + 0.6f; // small "rage" opening
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

        debugState = state.ToString();
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

        if (rb != null)
        {
            Vector2 next = rb.position + desiredVelocity * Time.fixedDeltaTime;
            rb.MovePosition(next);
        }
        else
        {
            transform.position += (Vector3)(desiredVelocity * Time.fixedDeltaTime);
        }
    }

    // ----------------------------
    // Public hooks
    // ----------------------------
    public void SetRole(EnemySquadRole role)
    {
        currentRole = role;
    }

    public void SetSharedPlayerPosition(Vector2 pos)
    {
        sharedPlayerPos = pos;
        hasSharedPlayerPos = true;
    }

    /// <summary>Call this when the enemy takes damage. Backstabber uses it to retreat to cover.</summary>
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

        // Personality flee rule
        bool panicFight;
        bool shouldFlee = ShouldFlee(out panicFight);

        // Opening action overrides normal selection for a short time
        if (IsInOpening())
        {
            switch (profile.openingAction)
            {
                case OpeningAction.ChargePlayer:
                    usingFallbackChase = true;
                    currentPoint = null;
                    state = BrainState.Move;
                    nextStuckCheckTime = Time.time + stuckCheckInterval;
                    lastStuckPos = transform.position;
                    return;

                case OpeningAction.RunToCover:
                    // Just bias, not hard override
                    break;

                case OpeningAction.SeekFlank:
                    // Just bias, not hard override
                    break;
            }
        }

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

        // No tactical point, chase player for aggression
        usingFallbackChase = true;
        currentPoint = null;
        state = BrainState.Move;
        nextStuckCheckTime = Time.time + stuckCheckInterval;
        lastStuckPos = transform.position;
    }

    private void TickMove()
    {
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

        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        float dist = Vector2.Distance(pos, target);

        if (dist <= arrivalRadius)
        {
            ClearMoveIntent();
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
        if (Time.time >= nextStateTime)
            EnterAttackWindow();
    }

    private void EnterAttackWindow()
    {
        state = BrainState.AttackWindow;
        shooterArmedThisWindow = false;
        noLosTimer = 0f;

        RoleShooterTuning t = GetRoleTuning();

        // Configure shooter core timing (if those methods exist)
        TryCall(shooter, "SetFireInterval", new object[] { t.fireInterval });
        TryCall(shooter, "SetAimLag", new object[] { t.aimLag });

        // Apply personality firing style (burst + pattern)
        ApplyFiringStyleForThisWindow();

        // Add slight random stagger so multiple enemies do not fire on same frame
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

        // Strict LOS gate for anchors/suppressors (fix "shoot into wall")
        if (RoleNeedsStrictLos())
        {
            bool hasLos = HasLineOfSightToPlayerNow();
            if (!hasLos)
            {
                noLosTimer += Time.deltaTime;
                SetShooterGate(false);

                if (noLosTimer >= maxNoLosBeforeReplan)
                {
                    EnterReplan(true);
                }
                return;
            }
            noLosTimer = 0f;
        }

        // Aggressive: refuse to shoot until in preferred range
        if (profile.mustBeInPreferredRangeToShoot)
        {
            float dist = Vector2.Distance(transform.position, GetPlayerPos());
            if (dist > Mathf.Max(profile.preferredMaxRange, 0.01f))
            {
                SetShooterGate(false);
                // Keep pressure by chasing
                usingFallbackChase = true;
                currentPoint = null;
                state = BrainState.Move;
                nextStuckCheckTime = Time.time + stuckCheckInterval;
                lastStuckPos = transform.position;
                return;
            }
        }

        if (Time.time < attackEnableTime) return;

        // Enable shooter once per window
        if (!shooterArmedThisWindow)
        {
            SyncShooterTarget();
            TryCall(shooter, "ForceReadyToFire", new object[] { 0f });
            SetShooterGate(true);
            shooterArmedThisWindow = true;
        }
    }

    // ----------------------------
    // Movement helpers
    // ----------------------------
    private void MoveTowardTarget(Vector2 target, float speed)
    {
        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        Vector2 toTarget = target - pos;

        if (toTarget.sqrMagnitude < 0.0001f)
        {
            ClearMoveIntent();
            return;
        }

        Vector2 dir = toTarget.normalized;

        // Simple obstacle probe: if blocked, bias sideways for a moment
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

            bool sideBlocked = Physics2D.Raycast(pos, dir, obstacleProbeDistance * 0.90f, losBlockMask);
            if (sideBlocked)
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
    // Tactical selection and scoring
    // ----------------------------
    private CombatTacticalPoint PickBestPointForRole(bool shouldFlee, bool panicFight)
    {
        var points = CombatTacticalPoint.AllPoints;
        if (points == null || points.Count == 0) return null;

        Vector2 myPos = transform.position;
        Vector2 playerPos = GetPlayerPos();

        CombatTacticalPoint best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            CombatTacticalPoint p = points[i];
            if (p == null) continue;
            if (!p.CanBeUsedBy(this, Time.time)) continue;

            float score = 0f;

            // Base role fit
            score += ScoreRoleTypeFit(p.pointType);

            // Personality bias (including opening and under-fire)
            score += ScorePersonalityBias(p);

            // If fleeing, strongly prefer cover or retreat points
            if (shouldFlee && !panicFight)
            {
                if (p.pointType == TacticalPointType.Cover || p.pointType == TacticalPointType.Retreat) score += 2.2f;
                if (p.pointType == TacticalPointType.Fire) score -= 0.6f;
                if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score -= 0.4f;
            }

            // Backstabber always prefers flank points
            if (profile.preferFlanksAlways)
            {
                if (p.pointType == TacticalPointType.FlankLeft || p.pointType == TacticalPointType.FlankRight) score += 1.4f;
                if (p.pointType == TacticalPointType.Fire) score -= 0.25f;
            }

            // LOS considerations: anchors and suppressors should not pick fire points with no LOS
            bool pointHasLos = !Physics2D.Linecast(p.Position, playerPos, losBlockMask);
            if (currentRole == EnemySquadRole.Anchor || currentRole == EnemySquadRole.Suppressor)
            {
                score += pointHasLos ? 1.2f : -2.4f;
            }

            // Range fit
            float distToPlayer = Vector2.Distance(p.Position, playerPos);
            score += ScoreRangeFit(distToPlayer) * rangeFitWeight;

            // Flank side scoring (if the point encodes left/right)
            if (currentRole == EnemySquadRole.FlankerLeft || currentRole == EnemySquadRole.FlankerRight)
            {
                score += ScoreFlankSide(p.Position, playerPos) * flankWeight;
            }

            // Travel cost and reservation penalty
            float travel = Vector2.Distance(myPos, p.Position);
            score -= travel * travelCostWeight * 0.2f;

            if (p.IsReserved && p != currentPoint) score -= occupiedPenalty;
            if (p == currentPoint) score += 0.45f;

            // Small noise to avoid identical behavior
            score += UnityEngine.Random.Range(-0.06f, 0.06f);

            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        return best;
    }

    private float ScoreRoleTypeFit(TacticalPointType type)
    {
        switch (currentRole)
        {
            case EnemySquadRole.Suppressor:
                if (type == TacticalPointType.Fire) return fireWeight + 1.0f;
                if (type == TacticalPointType.Cover) return coverWeight + 0.2f;
                return 0f;

            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                if (type == TacticalPointType.FlankLeft || type == TacticalPointType.FlankRight) return flankWeight + 0.8f;
                if (type == TacticalPointType.Cover) return coverWeight + 0.2f;
                return 0f;

            case EnemySquadRole.Anchor:
                if (type == TacticalPointType.Cover) return coverWeight + 0.8f;
                if (type == TacticalPointType.Fire) return fireWeight + 0.2f;
                return 0f;

            case EnemySquadRole.Retreater:
                if (type == TacticalPointType.Retreat) return coverWeight + 0.9f;
                if (type == TacticalPointType.Cover) return coverWeight + 0.6f;
                return -0.2f;

            default:
                return 0f;
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

        // Opening hard preference
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

        // Backstabber under fire: retreat to cover strongly
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
        // Use personality preferred ranges as the main ÅgfeelÅh
        float min = Mathf.Max(0f, profile.preferredMinRange);
        float max = Mathf.Max(min + 0.01f, profile.preferredMaxRange);

        if (isMelee && !isRanged)
        {
            float best = 1.4f;
            return Mathf.Clamp01(1f - Mathf.Abs(dist - best) / 3f);
        }

        if (dist < min) return -0.6f * (min - dist);
        if (dist > max) return -0.2f * (dist - max);
        return 1.0f;
    }

    private float ScoreFlankSide(Vector2 pointPos, Vector2 playerPos)
    {
        Vector2 refPos = transform.position;
        Vector2 forward = (playerPos - refPos).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector2.right;

        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 v = (pointPos - playerPos).normalized;
        float sideDot = Vector2.Dot(v, right);

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
    private void SetShooterGate(bool enabled)
    {
        if (shooter == null) return;
        if (shooterGateEnabled == enabled) return;

        shooterGateEnabled = enabled;
        TryCall(shooter, "SetShootingEnabled", new object[] { enabled && isRanged });
    }

    private void SyncShooterTarget()
    {
        if (shooter == null) return;
        if (playerTransform == null) return;

        if (lastSyncedShooterTarget != playerTransform)
        {
            TryCall(shooter, "SetTarget", new object[] { playerTransform });
            lastSyncedShooterTarget = playerTransform;
        }
    }

    private void ApplyFiringStyleForThisWindow()
    {
        if (shooter == null) return;

        // Burst config (if shooter supports it)
        TryCall(shooter, "SetBurstConfig", new object[] { profile.shotsPerBurst, profile.intraBurstInterval, profile.burstCooldown });
        TryCall(shooter, "SetBurstQuotaPerEnable", new object[] { true, Mathf.Max(1, profile.burstsPerEnable) });

        // Pattern selection based on distance
        float dist = Vector2.Distance(transform.position, GetPlayerPos());
        string chosen =
            dist < profile.preferredMinRange ? profile.closePattern :
            dist > profile.preferredMaxRange ? profile.farPattern :
            profile.midPattern;

        // Try to call SetPattern(PatternType) or SetPattern(string)
        if (!TrySetShooterPatternByName(chosen))
        {
            // If your shooter has no pattern system yet, this does nothing.
        }

        // Optional: apply common pattern params if shooter has matching fields
        TrySetFieldOrProperty(shooter, "fanBullets", profile.fanBullets);
        TrySetFieldOrProperty(shooter, "fanArcDegrees", profile.fanArcDegrees);
        TrySetFieldOrProperty(shooter, "ringBullets", profile.ringBullets);
        TrySetFieldOrProperty(shooter, "angularSpeedDegPerTick", profile.angularSpeedDegPerTick);
    }

    private bool TrySetShooterPatternByName(string patternName)
    {
        if (shooter == null || string.IsNullOrEmpty(patternName)) return false;

        // First: SetPattern(string)
        if (TryCall(shooter, "SetPattern", new object[] { patternName })) return true;

        // Second: SetPattern(PatternType) where PatternType is a nested enum in EnemyShooterDebug
        Type shooterType = shooter.GetType();
        Type enumType = shooterType.GetNestedType("PatternType", BindingFlags.Public | BindingFlags.NonPublic);
        if (enumType == null || !enumType.IsEnum) return false;

        try
        {
            object enumVal = Enum.Parse(enumType, patternName, true);
            return TryCall(shooter, "SetPattern", new object[] { enumVal });
        }
        catch
        {
            return false;
        }
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

        Vector2 origin = transform.position;
        Vector2 targetPos = playerTransform.position;

        RaycastHit2D[] hits = Physics2D.LinecastAll(origin, targetPos, losBlockMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null) continue;

            // ignore own colliders
            if (c.transform.root == transform.root) continue;

            // if the first relevant hit is the player, LOS is clear
            if (c.transform.root == playerTransform.root) return true;

            // otherwise blocked by cover/wall
            return false;
        }

        return true;
    }

    private bool ShouldFlee(out bool panicFight)
    {
        panicFight = false;

        if (profile.fleeAtHealth01 <= 0f) return false;

        float h = Health01;
        if (h > profile.fleeAtHealth01) return false;

        float dist = Vector2.Distance(transform.position, GetPlayerPos());
        if (dist <= profile.panicDistance)
        {
            panicFight = true;
            return false;
        }

        // Aggressive never flees
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
        if (!autoFindPlayerByTag) return;
        if (playerTransform != null) return;
        if (Time.time < nextPlayerSearchTime) return;

        nextPlayerSearchTime = Time.time + playerSearchInterval;

        GameObject playerObj = null;
        try { playerObj = GameObject.FindWithTag(playerTag); }
        catch { return; }

        if (playerObj != null) playerTransform = playerObj.transform;
    }

    private Vector2 GetPlayerPos()
    {
        if (playerTransform != null) return playerTransform.position;
        if (hasSharedPlayerPos) return sharedPlayerPos;
        return sharedPlayerPos;
    }

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
            case EnemySquadRole.Anchor:
            case EnemySquadRole.None:
            default: return anchorShooter;
        }
    }

    // ----------------------------
    // Personality selection
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
        float a = Mathf.Max(0f, wAggressive);
        float s = Mathf.Max(0f, wScaredCat);
        float b = Mathf.Max(0f, wBackstabber);
        float v = Mathf.Max(0f, wAvenger);

        float total = a + s + b + v;
        if (total <= 0.0001f) return Personality.Aggressive;

        float r = UnityEngine.Random.value * total;

        if (r < a) return Personality.Aggressive;
        r -= a;

        if (r < s) return Personality.ScaredCat;
        r -= s;

        if (r < b) return Personality.Backstabber;

        return Personality.Avenger;
    }

    // Reads an external "EnemyPersonalityState" if present, without hard dependency.
    // If found, it tries to read property/field named "Current" or "Personality" and parse it.
    private Personality ReadExternalPersonalityOrFallback(Personality fallback)
    {
        var state = GetComponent("EnemyPersonalityState");
        if (state == null) return fallback;

        object val = null;

        val = TryGetFieldOrProperty(state, "Current");
        if (val == null) val = TryGetFieldOrProperty(state, "Personality");
        if (val == null) val = TryGetFieldOrProperty(state, "basePersonality");

        if (val == null) return fallback;

        try
        {
            string name = val.ToString();
            if (Enum.TryParse(name, true, out Personality p))
                return p;
        }
        catch { }

        return fallback;
    }

    private bool HasExternalPersonalityState()
    {
        return GetComponent("EnemyPersonalityState") != null;
    }

    private bool IsLastEnemyAlive()
    {
        // Max enemies is small (5). This is fast and avoids extra coordinator requirements.
        EnemyBrain[] brains = FindObjectsOfType<EnemyBrain>(false);
        int alive = 0;

        for (int i = 0; i < brains.Length; i++)
        {
            if (brains[i] == null) continue;
            if (brains[i].IsAlive) alive++;
        }

        return alive <= 1;
    }

    // ----------------------------
    // Health (reflection-safe)
    // ----------------------------
    private int GetCurrentHP()
    {
        if (enemyHealth == null) return 1;

        object v = TryGetFieldOrProperty(enemyHealth, "CurrentHP");
        if (v is int i) return i;

        v = TryGetFieldOrProperty(enemyHealth, "currentHP");
        if (v is int i2) return i2;

        // If no field found, assume alive
        return 1;
    }

    private float GetHealth01()
    {
        if (enemyHealth == null) return 1f;

        object h01 = TryGetFieldOrProperty(enemyHealth, "Health01");
        if (h01 is float f01) return Mathf.Clamp01(f01);

        object cur = TryGetFieldOrProperty(enemyHealth, "CurrentHP");
        object max = TryGetFieldOrProperty(enemyHealth, "MaxHP");

        if (cur is int c && max is int m && m > 0)
            return Mathf.Clamp01((float)c / m);

        // Try lowercase names too
        cur = TryGetFieldOrProperty(enemyHealth, "currentHP");
        max = TryGetFieldOrProperty(enemyHealth, "maxHP");
        if (cur is int c2 && max is int m2 && m2 > 0)
            return Mathf.Clamp01((float)c2 / m2);

        return 1f;
    }

    // ----------------------------
    // Reflection utilities
    // ----------------------------
    private static bool TryCall(object obj, string methodName, object[] args)
    {
        if (obj == null) return false;

        Type t = obj.GetType();
        MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != methodName) continue;

            ParameterInfo[] ps = methods[i].GetParameters();
            if (ps.Length != args.Length) continue;

            try
            {
                methods[i].Invoke(obj, args);
                return true;
            }
            catch { }
        }

        return false;
    }

    private static object TryGetFieldOrProperty(object obj, string name)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return null;

        Type t = obj.GetType();

        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f.GetValue(obj);

        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanRead) return p.GetValue(obj);

        return null;
    }

    private static bool TrySetFieldOrProperty(object obj, string name, object value)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return false;

        Type t = obj.GetType();

        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            try { f.SetValue(obj, Convert.ChangeType(value, f.FieldType)); return true; }
            catch { }
        }

        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite)
        {
            try { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType)); return true; }
            catch { }
        }

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
    }
}
