using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class EnemyBrain : MonoBehaviour, IEnemySquadAgent
{
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
    [SerializeField] private float playerSearchInterval = 0.2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    [SerializeField] private float retreatSpeedMultiplier = 1.0f;
    [SerializeField] private float obstacleProbeDistance = 0.35f;
    [SerializeField] private float stuckCheckInterval = 0.35f;
    [SerializeField] private float stuckMinTravel = 0.05f;

    [Header("Planning")]
    [SerializeField] private Vector2 replanIntervalRange = new Vector2(0.20f, 0.40f);
    [SerializeField] private float minPointLockTime = 0.45f;
    [SerializeField] private float replanHardTimeout = 0.40f;
    [SerializeField] private float roleChangeReplanDelay = 0.08f;

    [Header("Scoring")]
    [SerializeField] private float coverWeight = 0.65f;
    [SerializeField] private float fireWeight = 1.9f;
    [SerializeField] private float flankWeight = 1.6f;
    [SerializeField] private float retreatWeight = 0.35f;
    [SerializeField] private float travelCostWeight = 0.25f;
    [SerializeField] private float occupiedPenalty = 1.4f;
    [SerializeField] private float rangeFitWeight = 1.3f;

    [Header("Range Preferences")]
    [SerializeField] private float preferredMinRange = 1.2f;
    [SerializeField] private float preferredMaxRange = 3.2f;

    [Header("LOS / Obstacles")]
    [SerializeField] private LayerMask losBlockMask;

    [System.Serializable]
    public class RoleShooterTuning
    {
        public float fireInterval = 0.3f;
        public float aimLag = 0.08f;
        public Vector2 settleWindow = new Vector2(0.06f, 0.12f);
        public Vector2 attackWindow = new Vector2(2.0f, 3.0f);
        public float firstShotDelay = 0.0f;
    }

    [Header("Role Shooter Tuning")]
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

    [Header("Fallback Chase")]
    [SerializeField] private float fallbackArrivalRadius = 0.80f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;
    [SerializeField] private string debugState = "None";
    [SerializeField] private EnemySquadRole currentRole = EnemySquadRole.None;
    [SerializeField] private bool usingFallbackChase = false;
    [SerializeField] private Vector2 sharedPlayerPos;
    [SerializeField] private CombatTacticalPoint currentPoint;

    private EnemySquadCoordinator squad;
    private Transform playerTransform;
    private Transform lastSyncedShooterTarget;

    private bool hasSharedPlayerPos = false;
    private bool roleDirty = false;

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

    private enum BrainState
    {
        Replan,
        Move,
        Settle,
        AttackWindow
    }

    private BrainState state = BrainState.Replan;

    // IEnemySquadAgent
    public Transform Transform => transform;
    public bool IsAlive => enemyHealth == null || enemyHealth.CurrentHP > 0;
    public float Health01
    {
        get
        {
            if (enemyHealth == null || enemyHealth.MaxHP <= 0) return 1f;
            return Mathf.Clamp01((float)enemyHealth.CurrentHP / enemyHealth.MaxHP);
        }
    }
    public bool IsRanged => isRanged;
    public bool IsMelee => isMelee;
    public EnemySquadRole CurrentRole => currentRole;

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

        lastStuckPos = transform.position;
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

        // Keep shooter target synced, but only when changed.
        if (shooter != null && playerTransform != null && lastSyncedShooterTarget != playerTransform)
        {
            shooter.SetTarget(playerTransform);
            lastSyncedShooterTarget = playerTransform;
        }

        if (roleDirty)
        {
            roleDirty = false;
            nextReplanTime = Mathf.Min(nextReplanTime, Time.time + roleChangeReplanDelay);
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

    public void SetRole(EnemySquadRole role)
    {
        if (currentRole == role) return;
        currentRole = role;
        roleDirty = true;
    }

    public void SetSharedPlayerPosition(Vector2 pos)
    {
        sharedPlayerPos = pos;
        hasSharedPlayerPos = true;
    }

    private void ResolvePlayerTransform()
    {
        if (!autoFindPlayerByTag) return;
        if (playerTransform != null) return;
        if (Time.time < nextPlayerSearchTime) return;

        nextPlayerSearchTime = Time.time + playerSearchInterval;

        GameObject playerObj = null;
        try
        {
            playerObj = GameObject.FindWithTag(playerTag);
        }
        catch (UnityException)
        {
            return;
        }

        if (playerObj != null)
            playerTransform = playerObj.transform;
    }

    private Vector2 GetPlayerPos()
    {
        if (playerTransform != null) return playerTransform.position;
        if (hasSharedPlayerPos) return sharedPlayerPos;
        return sharedPlayerPos;
    }

    private void EnterReplan(bool immediate)
    {
        state = BrainState.Replan;
        replanEnteredTime = Time.time;
        usingFallbackChase = false;
        shooterArmedThisWindow = false;

        SetShooterGate(false);
        ClearMoveIntent();

        if (immediate)
            nextReplanTime = Time.time;
        else
            nextReplanTime = Time.time + UnityEngine.Random.Range(replanIntervalRange.x, replanIntervalRange.y);
    }

    private void TickReplan()
    {
        bool hardTimedOut = (Time.time - replanEnteredTime) >= replanHardTimeout;
        if (!hardTimedOut && Time.time < nextReplanTime) return;

        CombatTacticalPoint best = PickBestPointForRole();

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

        // No tactical point available, chase player so enemies stay aggressive.
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

        RoleShooterTuning t = GetRoleTuning();

        if (shooter != null)
        {
            shooter.SetFireInterval(t.fireInterval);
            shooter.SetAimLag(t.aimLag);
        }

        attackEnableTime = Time.time + Mathf.Max(0f, t.firstShotDelay);
        nextStateTime = Time.time + UnityEngine.Random.Range(t.attackWindow.x, t.attackWindow.y);

        // Start gated OFF, then arm ON once.
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
        if (Time.time < attackEnableTime) return;

        // Important: enable once per window, not every frame.
        if (!shooterArmedThisWindow)
        {
            if (playerTransform != null && lastSyncedShooterTarget != playerTransform)
            {
                shooter.SetTarget(playerTransform);
                lastSyncedShooterTarget = playerTransform;
            }

            SetShooterGate(true);
            shooterArmedThisWindow = true;
        }
    }

    private void SetShooterGate(bool enabled)
    {
        if (shooter == null) return;
        if (shooterGateEnabled == enabled) return;

        shooterGateEnabled = enabled;
        shooter.SetShootingEnabled(enabled && isRanged);
    }

    private RoleShooterTuning GetRoleTuning()
    {
        switch (currentRole)
        {
            case EnemySquadRole.Suppressor:
                return suppressorShooter;

            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                return flankerShooter;

            case EnemySquadRole.Retreater:
                return retreaterShooter;

            case EnemySquadRole.Anchor:
            case EnemySquadRole.None:
            default:
                return anchorShooter;
        }
    }

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

    private void ReleaseCurrentPoint()
    {
        if (currentPoint == null) return;
        currentPoint.Release(this, Time.time);
        currentPoint = null;
    }

    private CombatTacticalPoint PickBestPointForRole()
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

            float score = ScoreTypeFit(p.pointType);

            bool pointHasLOS = !Physics2D.Linecast(p.Position, playerPos, losBlockMask);
            bool pointCovered = Physics2D.Linecast(playerPos, p.Position, losBlockMask);

            if (currentRole == EnemySquadRole.Suppressor || currentRole == EnemySquadRole.Anchor)
                score += pointHasLOS ? 1.0f : -0.6f;

            if (currentRole == EnemySquadRole.Retreater)
                score += pointCovered ? 1.2f : -0.4f;

            if (currentRole == EnemySquadRole.FlankerLeft || currentRole == EnemySquadRole.FlankerRight)
                score += ScoreFlankSide(p.Position, playerPos) * flankWeight;

            float distToPlayer = Vector2.Distance(p.Position, playerPos);
            score += ScoreRangeFit(distToPlayer) * rangeFitWeight;

            float travel = Vector2.Distance(myPos, p.Position);
            score -= travel * travelCostWeight * 0.2f;

            if (p.IsReserved && p != currentPoint) score -= occupiedPenalty;
            if (p == currentPoint) score += 0.45f;

            score += UnityEngine.Random.Range(-0.06f, 0.06f);

            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        return best;
    }

    private float ScoreTypeFit(TacticalPointType type)
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
                if (type == TacticalPointType.Retreat) return retreatWeight + 1.0f;
                if (type == TacticalPointType.Cover) return coverWeight + 0.6f;
                return -0.2f;

            default:
                return 0f;
        }
    }

    private float ScoreRangeFit(float dist)
    {
        if (isMelee && !isRanged)
        {
            float best = 1.5f;
            return Mathf.Clamp01(1f - Mathf.Abs(dist - best) / 3f);
        }

        if (dist < preferredMinRange) return -0.6f * (preferredMinRange - dist);
        if (dist > preferredMaxRange) return -0.2f * (dist - preferredMaxRange);
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
