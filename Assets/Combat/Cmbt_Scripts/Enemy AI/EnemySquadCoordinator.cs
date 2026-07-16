using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates enemy roles, shared perception, squad pressure, attack overlap, pincer positioning,
/// and Phase 8 player-attention/opportunity-flanking intelligence.
///
/// Phase 8 goals:
/// - Track which enemy the player is currently fighting.
/// - Let other enemies exploit that attention with committed side/back flanks.
/// - Keep normal pincer pressure, but make flankers feel opportunistic and intelligent.
/// </summary>
public class EnemySquadCoordinator : MonoBehaviour
{
    private sealed class RoleStamp
    {
        public EnemySquadRole role;
        public float assignedAt;
    }

    private sealed class AttackSlot
    {
        public IEnemySquadAgent owner;
        public float claimedAt;
        public float expiresAt;
        public string reason;
    }

    [Header("References")]
    [SerializeField] private Transform player;

    [Header("Tick")]
    [SerializeField, Min(0.05f)] private float coordinatorTick = 0.35f;
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Behavior Weights")]
    [SerializeField] private float coverCampingDistance = 8.0f;
    [SerializeField, Range(0f, 1f)] private float coverCampingLosThreshold = 0.5f;
    [SerializeField] private float playerAggroDistance = 2.5f;
    [SerializeField, Range(0f, 1f)] private float lowHpRetreatThreshold = 0.30f;

    [Header("Role Limits")]
    [SerializeField, Min(0)] private int maxSuppressors = 1;
    [SerializeField, Min(0)] private int maxFlankers = 2;
    [SerializeField] private bool forceAtLeastOneAnchor = true;

    [Header("Role Stability")]
    [SerializeField, Min(0f)] private float minimumRoleDuration = 2.0f;
    [SerializeField, Range(0f, 1f)] private float emergencyRoleOverridePressure = 0.85f;

    [Header("LOS")]
    [SerializeField] private LayerMask losBlockMask;

    [Header("Shared Perception")]
    [SerializeField] private bool useTrueLastSeenPosition = true;
    [SerializeField, Min(0f)] private float lastSeenMemorySeconds = 3.0f;

    [Header("Stalemate / Pressure")]
    [SerializeField] private float stalemateGracePeriod = 2.75f;
    [SerializeField] private float pressureRampDuration = 3.5f;
    [Tooltip("Kept for compatibility with old inspector values. In Phase 4, player movement no longer fully resets pressure.")]
    [SerializeField] private float playerMoveResetThreshold = 0.6f;
    [SerializeField] private float pressureDecayRate = 0.4f;

    [Header("Engagement Health")]
    [SerializeField, Min(0.25f)] private float idleAgentGrace = 1.8f;
    [SerializeField, Range(0f, 1f)] private float blockedLosPressureWeight = 0.65f;
    [SerializeField, Range(0f, 1f)] private float idleAgentPressureWeight = 0.45f;

    [Header("Attack Slots / Overwhelm Rhythm v2")]
    [SerializeField] private bool useAttackSlots = true;

    [Tooltip("If true, one- and two-enemy fights avoid Anchor roles so every living enemy keeps pressure.")]
    [SerializeField] private bool avoidAnchorsInSmallSquads = true;

    [Tooltip("When true, attack slots are a soft overwhelm cap instead of a one-at-a-time queue. This lets multiple enemies attack together.")]
    [SerializeField] private bool allowOverlappingEnemyAttacks = true;

    [Tooltip("Normal maximum number of enemies that may be attacking at the same time. Clamped by living enemy count when Use Alive Count As Attack Cap is enabled.")]
    [SerializeField, Min(1)] private int normalSimultaneousAttackers = 3;

    [Tooltip("High-pressure maximum number of simultaneous attackers. Use this to create deliberate overwhelm spikes.")]
    [SerializeField, Min(1)] private int highPressureSimultaneousAttackers = 5;

    [Tooltip("If true, the simultaneous attacker count can never exceed the number of living enemies.")]
    [SerializeField] private bool useAliveCountAsAttackCap = true;

    [Tooltip("At this pressure, the squad uses the high-pressure simultaneous attacker cap.")]
    [SerializeField, Range(0f, 1f)] private float highPressureTwoSlotThreshold = 0.72f;

    [Tooltip("Maximum seconds the squad is allowed to go without a new telegraph/lunge before it starts nudging attacks.")]
    [SerializeField, Min(0.1f)] private float maximumThreatGapSeconds = 0.85f;

    [Tooltip("Past this gap, the next eligible enemy treats its attack as urgent.")]
    [SerializeField, Min(0.1f)] private float urgentThreatGapSeconds = 1.20f;

    [Tooltip("How long after Maximum Threat Gap it takes to ramp pressure from 0 to 1.")]
    [SerializeField, Min(0.1f)] private float threatGapPressureRampSeconds = 2.0f;

    [Tooltip("Failsafe duration for an owned attack slot if an enemy forgets to release it.")]
    [SerializeField, Min(0.2f)] private float attackSlotLeaseSeconds = 3.0f;

    [Tooltip("Legacy hard-slot mode only. When overlapping attacks are disabled, 3+ enemy squads can use a second slot at high pressure.")]
    [SerializeField] private bool allowHighPressureSecondSlot = true;

    [Header("Pincer Positioning / Escape Denial v1")]
    [Tooltip("If true, the coordinator gives enemies preferred pressure-side targets and scoring bonuses so they attack from multiple angles instead of clumping.")]
    [SerializeField] private bool usePincerPositioning = true;

    [Tooltip("How quickly the sampled player velocity follows the real player movement. Higher = reacts faster to dodge direction.")]
    [SerializeField, Min(0.1f)] private float playerVelocitySharpness = 10f;

    [Tooltip("Below this speed, escape-cutoff logic falls back to enemy/squad direction instead of noisy player velocity.")]
    [SerializeField, Min(0f)] private float playerVelocityDeadZone = 0.25f;

    [Tooltip("Preferred radius around the player used for generated pincer targets.")]
    [SerializeField, Min(0.5f)] private float pincerDefaultRadius = 3.2f;

    [Tooltip("How far left/right flank targets sit from the player's current motion/front direction.")]
    [SerializeField, Min(0.1f)] private float pincerSideOffset = 2.8f;

    [Tooltip("How far the suppressor/front target sits in the pressure direction.")]
    [SerializeField, Min(0.1f)] private float pincerFrontOffset = 3.0f;

    [Tooltip("How far a support/anchor target sits away from the main pressure direction.")]
    [SerializeField, Min(0.1f)] private float pincerSupportOffset = 3.8f;

    [Tooltip("How far ahead of the player's current movement the dog/flanker tries to cut off escape.")]
    [SerializeField, Min(0f)] private float escapeCutoffAhead = 2.2f;

    [Tooltip("Side offset used by dog/flanker escape cutoff targets.")]
    [SerializeField, Min(0f)] private float escapeCutoffSide = 1.5f;

    [Tooltip("Candidates closer than this to another living enemy are penalized.")]
    [SerializeField, Min(0.05f)] private float antiClumpRadius = 1.25f;

    [Tooltip("Candidates closer than this to another living enemy receive a heavy penalty.")]
    [SerializeField, Min(0.05f)] private float hardClumpRadius = 0.65f;

    [Tooltip("Weight of the anti-clumping score returned to enemy point selection.")]
    [SerializeField, Min(0f)] private float antiClumpScoreWeight = 1.4f;

    [Tooltip("Weight of crossfire / angle diversity scoring returned to ranged point selection.")]
    [SerializeField, Min(0f)] private float crossfireScoreWeight = 1.2f;

    [Tooltip("Ideal angular separation in degrees between ranged attackers around the player.")]
    [SerializeField, Range(15f, 180f)] private float idealCrossfireAngle = 85f;

    [Header("Player Attention / Opportunity Flanking v8")]
    [Tooltip("If true, the squad remembers which enemy the player recently attacked so other enemies can exploit the distraction.")]
    [SerializeField] private bool usePlayerAttentionTracking = true;

    [Tooltip("Seconds the player is considered focused on an enemy after damaging it.")]
    [SerializeField, Min(0.1f)] private float playerAttentionDuration = 1.35f;

    [Tooltip("If the player is farther than this from the focus enemy, the distraction is considered over.")]
    [SerializeField, Min(0.25f)] private float attentionMaxPlayerDistanceToFocus = 4.25f;

    [Tooltip("Minimum alive enemies required before attention flanks are allowed.")]
    [SerializeField, Min(2)] private int attentionMinimumAliveEnemies = 2;

    [Tooltip("Small lockout preventing the focus target from being refreshed every single damage tick.")]
    [SerializeField, Min(0f)] private float attentionRefreshCooldown = 0.06f;

    [Tooltip("If true, damaged enemies slightly raise pressure so allies are encouraged to capitalize.")]
    [SerializeField] private bool damageFocusRaisesPressure = true;

    [Tooltip("How much pressure is injected when the player focuses/damages one enemy.")]
    [SerializeField, Range(0f, 1f)] private float damageFocusPressureBoost = 0.18f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool debugAttackSlots = false;
    [SerializeField] private float debugCurrentPressure = 0f;
    [SerializeField] private float debugBlockedLosRatio = 0f;
    [SerializeField] private float debugIdleAgentRatio = 0f;
    [SerializeField] private bool debugAnyEnemySeesPlayer = false;
    [SerializeField] private int debugAliveAgents = 0;
    [SerializeField] private int debugAllowedAttackSlots = 1;
    [SerializeField] private int debugUsedAttackSlots = 0;
    [SerializeField] private float debugThreatGapSeconds = 0f;
    [SerializeField] private bool debugThreatGapUrgent = false;
    [SerializeField] private string debugAttackSlotOwners = "None";
    [SerializeField] private Vector2 debugPlayerVelocity;
    [SerializeField] private string debugPincerState = "None";
    [SerializeField] private string debugPlayerFocusTarget = "None";
    [SerializeField] private float debugPlayerFocusRemaining = 0f;
    [SerializeField] private string debugOpportunityFlankState = "None";

    private readonly List<IEnemySquadAgent> agents = new List<IEnemySquadAgent>();
    private readonly Dictionary<IEnemySquadAgent, RoleStamp> roleStamps = new Dictionary<IEnemySquadAgent, RoleStamp>();
    private readonly List<AttackSlot> attackSlots = new List<AttackSlot>();

    private float nextTickTime;
    private Vector2 sharedLastSeenPlayerPos;
    private float lastPlayerSeenTime = -999f;

    private float stalemateTimer = 0f;
    private float currentPressure01 = 0f;
    private Vector2 lastPlayerPosForStalemate;
    private bool stalematePlayerPosInitialized = false;

    private Vector2 lastPlayerPositionForVelocity;
    private bool playerVelocityInitialized = false;
    private Vector2 smoothedPlayerVelocity;

    private float lastThreatStartedTime = -999f;
    private float lastThreatEndedTime = -999f;

    private IEnemySquadAgent playerFocusAgent;
    private float playerFocusUntil = -999f;
    private float lastAttentionRefreshTime = -999f;
    private Vector2 playerFocusEnemyPosition;
    private Vector2 playerFocusPlayerPosition;

    public Vector2 SharedLastSeenPlayerPos => sharedLastSeenPlayerPos;
    public float CurrentPressure01 => currentPressure01;
    public int AliveAgentCount => BuildAliveListNoAllocCount();
    public bool ThreatGapUrgent => debugThreatGapUrgent;
    public float ThreatGapSeconds => debugThreatGapSeconds;
    public Vector2 SmoothedPlayerVelocity => smoothedPlayerVelocity;
    public bool UsePincerPositioning => usePincerPositioning;
    public bool HasActivePlayerFocus => IsPlayerFocusActive();

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void Awake()
    {
        TryFindPlayer();

        sharedLastSeenPlayerPos =
            player != null ? (Vector2)player.position : Vector2.zero;

        lastPlayerPositionForVelocity = sharedLastSeenPlayerPos;
        playerVelocityInitialized = player != null;

        lastThreatStartedTime = Now;
        lastThreatEndedTime = Now;
        nextTickTime = Now + coordinatorTick;
    }

    private void Update()
    {
        if (player == null)
            TryFindPlayer();

        UpdatePlayerVelocity();

        CleanupDeadRefs();
        CleanupAttackSlots();
        UpdateThreatGapDebug(BuildAliveListNoAllocCount());
        CleanupPlayerFocus();

        if (player == null)
            return;

        if (Now < nextTickTime)
            return;

        nextTickTime = Now + Mathf.Max(0.05f, coordinatorTick);
        TickCoordinator();
    }

    public void Register(IEnemySquadAgent agent)
    {
        if (agent == null)
            return;

        if (!agents.Contains(agent))
            agents.Add(agent);
    }

    public void Unregister(IEnemySquadAgent agent)
    {
        if (agent == null)
            return;

        agents.Remove(agent);
        roleStamps.Remove(agent);
        ReleaseAttackSlot(agent, "Unregistered");
    }

    public bool TryRequestAttackSlot(IEnemySquadAgent agent, string reason = "Attack")
    {
        if (agent == null || !agent.IsAlive)
            return false;

        if (!useAttackSlots)
        {
            RegisterThreatStarted(agent, reason);
            return true;
        }

        CleanupAttackSlots();

        if (IsAttackSlotOwner(agent))
            return true;

        int aliveCount = BuildAliveListNoAllocCount();
        int allowed = GetAllowedAttackSlots(aliveCount);

        if (attackSlots.Count >= allowed)
            return false;

        AttackSlot slot = new AttackSlot
        {
            owner = agent,
            claimedAt = Now,
            expiresAt = Now + Mathf.Max(0.2f, attackSlotLeaseSeconds),
            reason = string.IsNullOrEmpty(reason) ? "Attack" : reason
        };

        attackSlots.Add(slot);
        RegisterThreatStarted(agent, slot.reason);
        UpdateAttackSlotDebug(aliveCount);
        return true;
    }

    public void ReleaseAttackSlot(IEnemySquadAgent agent, string reason = "Released")
    {
        if (agent == null)
            return;

        bool removed = false;

        for (int i = attackSlots.Count - 1; i >= 0; i--)
        {
            if (attackSlots[i].owner == agent)
            {
                attackSlots.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            lastThreatEndedTime = Now;

            if (debugAttackSlots)
            {
                Debug.Log(
                    $"[SquadSlots] {GetAgentName(agent)} released slot: {reason}",
                    this
                );
            }
        }

        UpdateAttackSlotDebug(BuildAliveListNoAllocCount());
    }

    public bool IsAttackSlotOwner(IEnemySquadAgent agent)
    {
        if (agent == null)
            return false;

        CleanupAttackSlots();

        for (int i = 0; i < attackSlots.Count; i++)
        {
            if (attackSlots[i].owner == agent)
                return true;
        }

        return false;
    }

    public bool ShouldForceAttack(IEnemySquadAgent agent)
    {
        if (agent == null || !agent.IsAlive)
            return false;

        if (agent.CurrentRole == EnemySquadRole.Retreater)
            return false;

        int aliveCount = BuildAliveListNoAllocCount();
        UpdateThreatGapDebug(aliveCount);

        if (!debugThreatGapUrgent)
            return false;

        if (!useAttackSlots)
            return true;

        if (IsAttackSlotOwner(agent))
            return true;

        return attackSlots.Count < GetAllowedAttackSlots(aliveCount);
    }

    private void RegisterThreatStarted(IEnemySquadAgent agent, string reason)
    {
        lastThreatStartedTime = Now;
        debugThreatGapSeconds = 0f;
        debugThreatGapUrgent = false;

        // Starting a readable threat means the squad is doing its job; lower pressure a little
        // without completely erasing positional pressure.
        currentPressure01 = Mathf.MoveTowards(currentPressure01, 0f, 0.18f);
        debugCurrentPressure = currentPressure01;

        if (debugAttackSlots)
        {
            Debug.Log(
                $"[SquadSlots] {GetAgentName(agent)} claimed slot: {reason}",
                this
            );
        }
    }

    public void NotifyAgentDamagedByPlayer(IEnemySquadAgent agent)
    {
        if (!usePlayerAttentionTracking || agent == null || agent.Transform == null || !agent.IsAlive)
            return;

        if (BuildAliveListNoAllocCount() < attentionMinimumAliveEnemies)
            return;

        if (Now - lastAttentionRefreshTime < attentionRefreshCooldown && playerFocusAgent == agent)
            return;

        playerFocusAgent = agent;
        playerFocusUntil = Now + Mathf.Max(0.1f, playerAttentionDuration);
        lastAttentionRefreshTime = Now;
        playerFocusEnemyPosition = agent.Transform.position;
        playerFocusPlayerPosition = player != null ? (Vector2)player.position : sharedLastSeenPlayerPos;

        debugPlayerFocusTarget = GetAgentName(agent);
        debugPlayerFocusRemaining = Mathf.Max(0f, playerFocusUntil - Now);
        debugOpportunityFlankState = $"Player focused {debugPlayerFocusTarget}";

        if (damageFocusRaisesPressure)
        {
            currentPressure01 = Mathf.Max(
                currentPressure01,
                Mathf.Clamp01(damageFocusPressureBoost)
            );
            debugCurrentPressure = currentPressure01;
        }
    }

    public bool TryGetPlayerAttentionFocus(
        IEnemySquadAgent requester,
        out IEnemySquadAgent focus,
        out Vector2 focusPosition,
        out Vector2 playerPosition)
    {
        focus = null;
        focusPosition = Vector2.zero;
        playerPosition = player != null ? (Vector2)player.position : sharedLastSeenPlayerPos;

        if (!usePlayerAttentionTracking)
            return false;

        CleanupPlayerFocus();

        if (!IsPlayerFocusActive())
            return false;

        if (requester == null || requester == playerFocusAgent)
            return false;

        if (BuildAliveListNoAllocCount() < attentionMinimumAliveEnemies)
            return false;

        float focusDistance = Vector2.Distance(playerPosition, playerFocusAgent.Transform.position);
        if (focusDistance > attentionMaxPlayerDistanceToFocus)
        {
            ClearPlayerFocus("TooFar");
            return false;
        }

        focus = playerFocusAgent;
        focusPosition = playerFocusAgent.Transform.position;
        playerFocusEnemyPosition = focusPosition;
        playerFocusPlayerPosition = playerPosition;
        debugPlayerFocusRemaining = Mathf.Max(0f, playerFocusUntil - Now);
        return true;
    }

    public bool TryGetOpportunityFlankTarget(
        IEnemySquadAgent requester,
        float behindDistance,
        float sideDistance,
        float fallbackRadius,
        out Vector2 target,
        out string reason)
    {
        target = Vector2.zero;
        reason = "NoFocus";

        if (!TryGetPlayerAttentionFocus(
                requester,
                out IEnemySquadAgent focus,
                out Vector2 focusPosition,
                out Vector2 playerPosition))
        {
            return false;
        }

        Vector2 focusToPlayer = playerPosition - focusPosition;
        if (focusToPlayer.sqrMagnitude <= 0.0001f)
        {
            focusToPlayer = requester != null && requester.Transform != null
                ? playerPosition - (Vector2)requester.Transform.position
                : Vector2.up;
        }

        if (focusToPlayer.sqrMagnitude <= 0.0001f)
            focusToPlayer = Vector2.up;

        Vector2 forward = focusToPlayer.normalized;
        Vector2 right = new Vector2(forward.y, -forward.x);

        float roleSide = requester != null ? GetRoleSideSign(requester.CurrentRole) : 0f;
        float sideSign = Mathf.Abs(roleSide) > 0.01f ? roleSide : 0f;

        if (Mathf.Abs(sideSign) < 0.01f && requester != null && requester.Transform != null)
        {
            Vector2 toRequester = (Vector2)requester.Transform.position - playerPosition;
            sideSign = Vector2.Dot(toRequester, right) < 0f ? -1f : 1f;
        }

        if (Mathf.Abs(sideSign) < 0.01f)
            sideSign = 1f;

        float behind = Mathf.Max(0.5f, behindDistance > 0f ? behindDistance : pincerDefaultRadius);
        float side = Mathf.Max(0f, sideDistance);

        target = playerPosition + forward * behind + right * sideSign * side;

        if (Vector2.Distance(target, playerPosition) < 0.5f)
            target = playerPosition + forward * Mathf.Max(0.75f, fallbackRadius);

        reason = $"Focus:{GetAgentName(focus)} side:{sideSign:0}";
        debugOpportunityFlankState = $"{GetAgentName(requester)} flank vs {GetAgentName(focus)}";
        return true;
    }

    private bool IsPlayerFocusActive()
    {
        return usePlayerAttentionTracking &&
               playerFocusAgent != null &&
               playerFocusAgent.Transform != null &&
               playerFocusAgent.IsAlive &&
               Now < playerFocusUntil;
    }

    private void CleanupPlayerFocus()
    {
        if (!usePlayerAttentionTracking)
        {
            ClearPlayerFocus("Disabled");
            return;
        }

        if (playerFocusAgent == null || playerFocusAgent.Transform == null || !playerFocusAgent.IsAlive || Now >= playerFocusUntil)
        {
            ClearPlayerFocus("Expired");
            return;
        }

        if (player != null)
        {
            float focusDistance = Vector2.Distance(player.position, playerFocusAgent.Transform.position);
            if (focusDistance > attentionMaxPlayerDistanceToFocus)
            {
                ClearPlayerFocus("TooFar");
                return;
            }
        }

        debugPlayerFocusTarget = GetAgentName(playerFocusAgent);
        debugPlayerFocusRemaining = Mathf.Max(0f, playerFocusUntil - Now);
    }

    private void ClearPlayerFocus(string reason)
    {
        playerFocusAgent = null;
        playerFocusUntil = -999f;
        debugPlayerFocusRemaining = 0f;
        debugPlayerFocusTarget = "None";

        if (!string.IsNullOrEmpty(reason) && reason != "Expired")
            debugOpportunityFlankState = $"Focus cleared: {reason}";
    }

    private void TickCoordinator()
    {
        CleanupDeadRefs();
        CleanupAttackSlots();

        List<IEnemySquadAgent> alive = BuildAliveList();
        debugAliveAgents = alive.Count;

        if (alive.Count == 0)
            return;

        UpdateSharedPerception(alive);
        UpdatePressure(alive);
        UpdateAttackSlotDebug(alive.Count);

        for (int i = 0; i < alive.Count; i++)
        {
            alive[i].SetSharedPlayerPosition(sharedLastSeenPlayerPos);
            alive[i].NotifySquadPressure(currentPressure01);
        }

        AssignStableRoles(alive);

        if (debugLogs)
        {
            Debug.Log(
                $"[Squad] Alive={alive.Count} Pressure={currentPressure01:F2} " +
                $"Slots={attackSlots.Count}/{debugAllowedAttackSlots} " +
                $"ThreatGap={debugThreatGapSeconds:F2} Urgent={debugThreatGapUrgent} " +
                $"BlockedLOS={debugBlockedLosRatio:F2} Idle={debugIdleAgentRatio:F2} " +
                $"SeesPlayer={debugAnyEnemySeesPlayer}",
                this
            );
        }
    }

    private void UpdateSharedPerception(List<IEnemySquadAgent> alive)
    {
        bool anySeesPlayer = false;

        for (int i = 0; i < alive.Count; i++)
        {
            bool sees = HasAgentLineOfSight(alive[i]);
            if (sees)
            {
                anySeesPlayer = true;
                break;
            }
        }

        debugAnyEnemySeesPlayer = anySeesPlayer;

        if (!useTrueLastSeenPosition || anySeesPlayer)
        {
            sharedLastSeenPlayerPos = player.position;
            lastPlayerSeenTime = Now;
            return;
        }

        if (Now - lastPlayerSeenTime > lastSeenMemorySeconds)
        {
            // Intentionally keep the old last-seen position.
        }
    }

    private void UpdatePressure(List<IEnemySquadAgent> alive)
    {
        Vector2 playerPosition = player.position;

        if (!stalematePlayerPosInitialized)
        {
            lastPlayerPosForStalemate = playerPosition;
            stalematePlayerPosInitialized = true;
        }

        float playerMoveDelta = Vector2.Distance(playerPosition, lastPlayerPosForStalemate);
        bool playerIsMoving = playerMoveDelta >= playerMoveResetThreshold;

        if (playerIsMoving)
            lastPlayerPosForStalemate = playerPosition;

        int blockedCount = 0;
        int idleCount = 0;

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];

            if (!HasAgentLineOfSight(agent))
                blockedCount++;

            float lastMeaningful = GetLastMeaningfulActionTime(agent);
            if (Now - lastMeaningful >= idleAgentGrace)
                idleCount++;
        }

        debugBlockedLosRatio = alive.Count > 0 ? blockedCount / (float)alive.Count : 0f;
        debugIdleAgentRatio = alive.Count > 0 ? idleCount / (float)alive.Count : 0f;

        bool hidden = debugBlockedLosRatio >= coverCampingLosThreshold;
        bool squadInactive = debugIdleAgentRatio >= 0.5f;

        if (hidden || squadInactive)
            stalemateTimer += coordinatorTick;
        else
            stalemateTimer = Mathf.Max(0f, stalemateTimer - coordinatorTick * 0.5f);

        float elapsedAfterGrace = stalemateTimer - stalemateGracePeriod;
        float stalematePressure = elapsedAfterGrace > 0f
            ? Mathf.Clamp01(elapsedAfterGrace / Mathf.Max(0.01f, pressureRampDuration))
            : 0f;

        float engagementPressure = Mathf.Clamp01(
            debugBlockedLosRatio * blockedLosPressureWeight +
            debugIdleAgentRatio * idleAgentPressureWeight
        );

        UpdateThreatGapDebug(alive.Count);

        float threatGapPressure = 0f;
        if (debugThreatGapSeconds > maximumThreatGapSeconds)
        {
            threatGapPressure = Mathf.Clamp01(
                (debugThreatGapSeconds - maximumThreatGapSeconds) /
                Mathf.Max(0.01f, threatGapPressureRampSeconds)
            );
        }

        float targetPressure = Mathf.Max(stalematePressure, engagementPressure, threatGapPressure);

        // Movement is no longer a full pressure reset. If the player is moving but not being
        // threatened, threat-gap pressure still climbs. Movement only helps pressure decay when
        // the squad is already producing threats.
        float decayScale = attackSlots.Count > 0 ? 1.0f : 0.25f;
        if (targetPressure > currentPressure01)
            currentPressure01 = targetPressure;
        else
            currentPressure01 = Mathf.MoveTowards(
                currentPressure01,
                targetPressure,
                pressureDecayRate * decayScale * coordinatorTick
            );

        debugCurrentPressure = currentPressure01;
    }

    private void AssignStableRoles(List<IEnemySquadAgent> alive)
    {
        Dictionary<IEnemySquadAgent, EnemySquadRole> desired =
            new Dictionary<IEnemySquadAgent, EnemySquadRole>();

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            if (agent.Health01 <= lowHpRetreatThreshold)
                desired[agent] = EnemySquadRole.Retreater;
        }

        int activeCount = alive.Count - desired.Count;

        if (avoidAnchorsInSmallSquads && activeCount <= 2)
        {
            AssignSmallSquadRoles(alive, desired, activeCount);
        }
        else
        {
            AssignStandardRoles(alive, desired);
        }

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            EnemySquadRole role = desired.ContainsKey(agent)
                ? desired[agent]
                : EnemySquadRole.Anchor;

            ApplyRole(agent, role);
        }
    }

    private void AssignSmallSquadRoles(
        List<IEnemySquadAgent> alive,
        Dictionary<IEnemySquadAgent, EnemySquadRole> desired,
        int activeCount)
    {
        if (activeCount <= 0)
            return;

        IEnemySquadAgent suppressor = PickBestSuppressor(alive, desired);

        if (suppressor != null)
            desired[suppressor] = EnemySquadRole.Suppressor;

        IEnemySquadAgent flanker = PickBestFlanker(alive, desired);
        if (flanker != null)
        {
            desired[flanker] = ChooseFlankSide(
                flanker.Transform.position,
                ComputeSquadCenter(alive),
                sharedLastSeenPlayerPos
            );
        }

        // If there is only one melee enemy, it becomes a flanker-style hunter.
        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            if (desired.ContainsKey(agent))
                continue;

            if (agent.IsRanged && suppressor == null)
            {
                desired[agent] = EnemySquadRole.Suppressor;
                suppressor = agent;
            }
            else
            {
                desired[agent] = ChooseFlankSide(
                    agent.Transform.position,
                    ComputeSquadCenter(alive),
                    sharedLastSeenPlayerPos
                );
            }
        }
    }

    private void AssignStandardRoles(
        List<IEnemySquadAgent> alive,
        Dictionary<IEnemySquadAgent, EnemySquadRole> desired)
    {
        int suppressors = 0;
        int flankers = 0;
        int anchors = 0;

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];

            if (desired.ContainsKey(agent))
                continue;

            if (ShouldPreserveCurrentRole(agent))
            {
                desired[agent] = agent.CurrentRole;
                CountRole(agent.CurrentRole, ref suppressors, ref flankers, ref anchors);
            }
        }

        while (suppressors < maxSuppressors)
        {
            IEnemySquadAgent candidate = PickBestSuppressor(alive, desired);
            if (candidate == null)
                break;

            desired[candidate] = EnemySquadRole.Suppressor;
            suppressors++;
        }

        bool playerAggressive = IsPlayerAggressive(alive);
        bool shouldFlank =
            debugBlockedLosRatio >= coverCampingLosThreshold ||
            (!playerAggressive && alive.Count >= 3) ||
            currentPressure01 >= 0.45f ||
            debugThreatGapUrgent;

        IEnemySquadAgent firstFlanker = null;

        while (shouldFlank && flankers < maxFlankers)
        {
            IEnemySquadAgent candidate = PickBestFlanker(alive, desired);
            if (candidate == null)
                break;

            EnemySquadRole side;
            if (firstFlanker == null)
            {
                side = ChooseFlankSide(candidate.Transform.position, ComputeSquadCenter(alive), sharedLastSeenPlayerPos);
                firstFlanker = candidate;
            }
            else
            {
                EnemySquadRole firstRole = desired[firstFlanker];
                side = firstRole == EnemySquadRole.FlankerLeft
                    ? EnemySquadRole.FlankerRight
                    : EnemySquadRole.FlankerLeft;
            }

            desired[candidate] = side;
            flankers++;
        }

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            if (desired.ContainsKey(agent))
                continue;

            desired[agent] = EnemySquadRole.Anchor;
            anchors++;
        }

        if (forceAtLeastOneAnchor && alive.Count >= 3 && anchors == 0)
        {
            IEnemySquadAgent fallback = PickBestAnchorCandidate(alive);
            if (fallback != null && !desired.ContainsKey(fallback))
                desired[fallback] = EnemySquadRole.Anchor;
        }
    }

    private bool ShouldPreserveCurrentRole(IEnemySquadAgent agent)
    {
        EnemySquadRole current = agent.CurrentRole;

        if (current == EnemySquadRole.None)
            return false;

        if (avoidAnchorsInSmallSquads && BuildAliveListNoAllocCount() <= 2 && current == EnemySquadRole.Anchor)
            return false;

        if (!roleStamps.TryGetValue(agent, out RoleStamp stamp))
        {
            roleStamps[agent] = new RoleStamp { role = current, assignedAt = Now };
            return true;
        }

        if (stamp.role != current)
        {
            stamp.role = current;
            stamp.assignedAt = Now;
        }

        if (currentPressure01 >= emergencyRoleOverridePressure && current == EnemySquadRole.Anchor)
            return false;

        if (debugThreatGapUrgent && current == EnemySquadRole.Anchor)
            return false;

        return Now - stamp.assignedAt < minimumRoleDuration;
    }

    private void ApplyRole(IEnemySquadAgent agent, EnemySquadRole role)
    {
        if (agent == null)
            return;

        if (agent.CurrentRole != role)
            agent.SetRole(role);

        roleStamps[agent] = new RoleStamp { role = role, assignedAt = Now };
    }

    private void CountRole(EnemySquadRole role, ref int suppressors, ref int flankers, ref int anchors)
    {
        if (role == EnemySquadRole.Suppressor)
            suppressors++;
        else if (role == EnemySquadRole.FlankerLeft || role == EnemySquadRole.FlankerRight)
            flankers++;
        else if (role == EnemySquadRole.Anchor)
            anchors++;
    }

    private int GetAllowedAttackSlots(int aliveCount)
    {
        if (aliveCount <= 0)
            return 0;

        // If slot gating is disabled, every living enemy may attack.
        if (!useAttackSlots)
            return Mathf.Max(1, aliveCount);

        // v2 default: slots are not a one-at-a-time queue. They are an
        // overwhelm cap. This allows several enemies to attack together while
        // still preventing accidental infinite ownership if a script fails to release.
        if (allowOverlappingEnemyAttacks)
        {
            int desired =
                currentPressure01 >= highPressureTwoSlotThreshold
                    ? Mathf.Max(1, highPressureSimultaneousAttackers)
                    : Mathf.Max(1, normalSimultaneousAttackers);

            if (useAliveCountAsAttackCap)
                desired = Mathf.Min(desired, aliveCount);

            return Mathf.Clamp(
                desired,
                1,
                Mathf.Max(1, useAliveCountAsAttackCap ? aliveCount : desired)
            );
        }

        // Legacy hard-slot mode from Phase 4 v1. Keep this available if a
        // specific encounter ever needs clean one-at-a-time attacks.
        if (aliveCount <= 2)
            return 1;

        if (allowHighPressureSecondSlot &&
            aliveCount >= 3 &&
            currentPressure01 >= highPressureTwoSlotThreshold)
        {
            return 2;
        }

        return 1;
    }

    private void CleanupAttackSlots()
    {
        for (int i = attackSlots.Count - 1; i >= 0; i--)
        {
            AttackSlot slot = attackSlots[i];

            if (slot == null || slot.owner == null || slot.owner.Transform == null || !slot.owner.IsAlive || Now >= slot.expiresAt)
            {
                attackSlots.RemoveAt(i);
                continue;
            }
        }
    }

    private void UpdateThreatGapDebug(int aliveCount)
    {
        if (aliveCount <= 0)
        {
            debugThreatGapSeconds = 0f;
            debugThreatGapUrgent = false;
            return;
        }

        float lastThreatTime = Mathf.Max(lastThreatStartedTime, lastThreatEndedTime);
        debugThreatGapSeconds = Mathf.Max(0f, Now - lastThreatTime);
        debugThreatGapUrgent = debugThreatGapSeconds >= urgentThreatGapSeconds;
    }

    private void UpdateAttackSlotDebug(int aliveCount)
    {
        debugAllowedAttackSlots = GetAllowedAttackSlots(aliveCount);
        debugUsedAttackSlots = attackSlots.Count;

        if (attackSlots.Count == 0)
        {
            debugAttackSlotOwners = "None";
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < attackSlots.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            sb.Append(GetAgentName(attackSlots[i].owner));
        }

        debugAttackSlotOwners = sb.ToString();
    }

    public Vector2 GetPressurePositionForAgent(
        IEnemySquadAgent agent,
        Vector2 fallbackPosition,
        float desiredRadius,
        float sideBias = 0f)
    {
        if (!usePincerPositioning || player == null || agent == null || agent.Transform == null)
            return fallbackPosition;

        Vector2 playerPosition = player.position;
        Vector2 forward = GetPincerForward(agent);
        Vector2 right = new Vector2(forward.y, -forward.x);

        float radius = Mathf.Max(0.5f, desiredRadius > 0f ? desiredRadius : pincerDefaultRadius);
        float sideSign = Mathf.Abs(sideBias) > 0.01f ? Mathf.Sign(sideBias) : GetRoleSideSign(agent.CurrentRole);

        Vector2 target;

        switch (agent.CurrentRole)
        {
            case EnemySquadRole.Suppressor:
                target = playerPosition - forward * Mathf.Max(radius, pincerFrontOffset);
                break;

            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                if (Mathf.Abs(sideSign) < 0.01f)
                    sideSign = agent.CurrentRole == EnemySquadRole.FlankerLeft ? -1f : 1f;

                target = playerPosition + right * sideSign * Mathf.Max(radius, pincerSideOffset);
                break;

            case EnemySquadRole.Anchor:
                target = playerPosition + forward * Mathf.Max(radius, pincerSupportOffset);
                break;

            case EnemySquadRole.Retreater:
                Vector2 away = (Vector2)agent.Transform.position - playerPosition;
                if (away.sqrMagnitude <= 0.0001f)
                    away = forward;
                target = playerPosition + away.normalized * Mathf.Max(radius, pincerSupportOffset);
                break;

            default:
                Vector2 toAgent = (Vector2)agent.Transform.position - playerPosition;
                if (toAgent.sqrMagnitude <= 0.0001f)
                    toAgent = -forward;
                target = playerPosition + toAgent.normalized * radius;
                break;
        }

        debugPincerState = $"{GetAgentName(agent)} target {agent.CurrentRole}";
        return target;
    }

    public Vector2 GetEscapeCutoffPosition(
        IEnemySquadAgent agent,
        float aheadDistance,
        float sideDistance,
        float fallbackRadius)
    {
        if (!usePincerPositioning || player == null || agent == null || agent.Transform == null)
        {
            return agent != null && agent.Transform != null
                ? (Vector2)agent.Transform.position
                : Vector2.zero;
        }

        Vector2 playerPosition = player.position;
        Vector2 forward = smoothedPlayerVelocity.magnitude >= playerVelocityDeadZone
            ? smoothedPlayerVelocity.normalized
            : GetPincerForward(agent);

        Vector2 right = new Vector2(forward.y, -forward.x);
        float sideSign = GetRoleSideSign(agent.CurrentRole);

        if (Mathf.Abs(sideSign) < 0.01f)
        {
            Vector2 toAgent = (Vector2)agent.Transform.position - playerPosition;
            sideSign = Vector2.Dot(toAgent, right) < 0f ? -1f : 1f;
        }

        Vector2 target =
            playerPosition +
            forward * Mathf.Max(0f, aheadDistance > 0f ? aheadDistance : escapeCutoffAhead) +
            right * sideSign * Mathf.Max(0f, sideDistance > 0f ? sideDistance : escapeCutoffSide);

        if (Vector2.Distance(target, playerPosition) < 0.5f)
            target = playerPosition + right * sideSign * Mathf.Max(0.5f, fallbackRadius);

        debugPincerState = $"{GetAgentName(agent)} cutoff";
        return target;
    }

    public float ScorePincerCandidate(
        IEnemySquadAgent requester,
        Vector2 candidate,
        EnemySquadRole role)
    {
        if (!usePincerPositioning || player == null || requester == null)
            return 0f;

        Vector2 playerPosition = player.position;
        Vector2 fromPlayer = candidate - playerPosition;
        if (fromPlayer.sqrMagnitude <= 0.0001f)
            return -2f;

        Vector2 candidateDir = fromPlayer.normalized;
        Vector2 forward = GetPincerForward(requester);
        Vector2 right = new Vector2(forward.y, -forward.x);

        float score = 0f;

        float sideSign = GetRoleSideSign(role);
        if (Mathf.Abs(sideSign) > 0.01f)
        {
            float sideDot = Vector2.Dot(candidateDir, right) * sideSign;
            score += sideDot * 1.4f;
        }
        else if (role == EnemySquadRole.Suppressor)
        {
            score += Vector2.Dot(candidateDir, -forward) * 0.9f;
        }
        else if (role == EnemySquadRole.Anchor)
        {
            score += Vector2.Dot(candidateDir, forward) * 0.65f;
        }

        float minAngle = 180f;
        int comparedAngles = 0;

        for (int i = 0; i < agents.Count; i++)
        {
            IEnemySquadAgent other = agents[i];
            if (other == null || other == requester || other.Transform == null || !other.IsAlive)
                continue;

            Vector2 otherPos = other.Transform.position;
            float distance = Vector2.Distance(candidate, otherPos);

            if (distance < hardClumpRadius)
                score -= 4.0f * antiClumpScoreWeight;
            else if (distance < antiClumpRadius)
                score -= (1f - distance / Mathf.Max(0.01f, antiClumpRadius)) * antiClumpScoreWeight;

            Vector2 otherDir = otherPos - playerPosition;
            if (otherDir.sqrMagnitude > 0.0001f)
            {
                float angle = Vector2.Angle(candidateDir, otherDir.normalized);
                minAngle = Mathf.Min(minAngle, angle);
                comparedAngles++;
            }
        }

        if (comparedAngles > 0)
        {
            float angleScore = Mathf.Clamp01(minAngle / Mathf.Max(1f, idealCrossfireAngle));
            score += angleScore * crossfireScoreWeight;
        }

        return score;
    }

    public int GetSuggestedSideSignForAgent(IEnemySquadAgent agent)
    {
        if (agent == null || agent.Transform == null || player == null)
            return 1;

        float roleSide = GetRoleSideSign(agent.CurrentRole);
        if (Mathf.Abs(roleSide) > 0.01f)
            return roleSide < 0f ? -1 : 1;

        Vector2 forward = GetPincerForward(agent);
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 toAgent = (Vector2)agent.Transform.position - (Vector2)player.position;
        return Vector2.Dot(toAgent, right) < 0f ? -1 : 1;
    }

    private void UpdatePlayerVelocity()
    {
        if (player == null)
            return;

        Vector2 current = player.position;

        if (!playerVelocityInitialized)
        {
            lastPlayerPositionForVelocity = current;
            playerVelocityInitialized = true;
            smoothedPlayerVelocity = Vector2.zero;
            debugPlayerVelocity = smoothedPlayerVelocity;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 rawVelocity = (current - lastPlayerPositionForVelocity) / dt;
        lastPlayerPositionForVelocity = current;

        float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, playerVelocitySharpness) * dt);
        smoothedPlayerVelocity = Vector2.Lerp(smoothedPlayerVelocity, rawVelocity, k);
        debugPlayerVelocity = smoothedPlayerVelocity;
    }

    private Vector2 GetPincerForward(IEnemySquadAgent requester)
    {
        if (player != null && smoothedPlayerVelocity.magnitude >= playerVelocityDeadZone)
            return smoothedPlayerVelocity.normalized;

        Vector2 playerPosition = player != null ? (Vector2)player.position : sharedLastSeenPlayerPos;
        Vector2 squadCenter = ComputeSquadCenter(BuildAliveList());
        Vector2 forward = playerPosition - squadCenter;

        if (forward.sqrMagnitude <= 0.0001f && requester != null && requester.Transform != null)
            forward = playerPosition - (Vector2)requester.Transform.position;

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector2.up;

        return forward.normalized;
    }

    private float GetRoleSideSign(EnemySquadRole role)
    {
        if (role == EnemySquadRole.FlankerLeft)
            return -1f;

        if (role == EnemySquadRole.FlankerRight)
            return 1f;

        return 0f;
    }

    private void TryFindPlayer()
    {
        CombatPawn pawn = FindObjectOfType<CombatPawn>(true);
        if (pawn != null)
            player = pawn.transform;
    }

    private List<IEnemySquadAgent> BuildAliveList()
    {
        List<IEnemySquadAgent> alive = new List<IEnemySquadAgent>();

        for (int i = agents.Count - 1; i >= 0; i--)
        {
            IEnemySquadAgent agent = agents[i];

            if (agent == null || agent.Transform == null)
            {
                roleStamps.Remove(agent);
                agents.RemoveAt(i);
                continue;
            }

            if (agent.IsAlive)
                alive.Add(agent);
        }

        return alive;
    }

    private int BuildAliveListNoAllocCount()
    {
        int count = 0;
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            IEnemySquadAgent agent = agents[i];
            if (agent == null || agent.Transform == null)
                continue;

            if (agent.IsAlive)
                count++;
        }
        return count;
    }

    private void CleanupDeadRefs()
    {
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            IEnemySquadAgent agent = agents[i];

            if (agent == null || agent.Transform == null)
            {
                roleStamps.Remove(agent);
                agents.RemoveAt(i);
                continue;
            }

            if (!agent.IsAlive)
                ReleaseAttackSlot(agent, "Dead");
        }
    }

    private Vector2 ComputeSquadCenter(List<IEnemySquadAgent> alive)
    {
        Vector2 sum = Vector2.zero;

        for (int i = 0; i < alive.Count; i++)
            sum += (Vector2)alive[i].Transform.position;

        return alive.Count > 0 ? sum / alive.Count : Vector2.zero;
    }

    private bool IsPlayerAggressive(List<IEnemySquadAgent> alive)
    {
        int closeCount = 0;
        Vector2 playerPosition = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            float distance = Vector2.Distance(alive[i].Transform.position, playerPosition);
            if (distance <= playerAggroDistance)
                closeCount++;
        }

        return closeCount >= Mathf.CeilToInt(alive.Count * 0.5f);
    }

    private IEnemySquadAgent PickBestSuppressor(
        List<IEnemySquadAgent> alive,
        Dictionary<IEnemySquadAgent, EnemySquadRole> assigned)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 playerPosition = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            if (assigned.ContainsKey(agent) || !agent.IsRanged)
                continue;

            bool hasLos = HasAgentLineOfSight(agent);
            float distance = Vector2.Distance(agent.Transform.position, playerPosition);
            float distanceFit = Mathf.Clamp01(1f - Mathf.Abs(distance - 4f) / 4f);

            float score = 0f;
            score += hasLos ? 2.2f : -0.5f;
            score += distanceFit;
            score += agent.Health01;

            if (agent.CurrentRole == EnemySquadRole.Suppressor)
                score += 0.35f;

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private IEnemySquadAgent PickBestFlanker(
        List<IEnemySquadAgent> alive,
        Dictionary<IEnemySquadAgent, EnemySquadRole> assigned)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 playerPosition = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            if (assigned.ContainsKey(agent))
                continue;

            float distance = Vector2.Distance(agent.Transform.position, playerPosition);

            float score = 0f;
            score += 1f - Mathf.Clamp01(distance / 10f);
            score += agent.IsMelee ? 1.0f : 0.2f;
            score += agent.Health01;

            if (agent.CurrentRole == EnemySquadRole.FlankerLeft || agent.CurrentRole == EnemySquadRole.FlankerRight)
                score += 0.25f;

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private IEnemySquadAgent PickBestAnchorCandidate(List<IEnemySquadAgent> alive)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 playerPosition = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            IEnemySquadAgent agent = alive[i];
            float distance = Vector2.Distance(agent.Transform.position, playerPosition);
            float score = agent.Health01 + Mathf.Clamp01(distance / 8f);

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private EnemySquadRole ChooseFlankSide(Vector2 agentPosition, Vector2 squadCenter, Vector2 playerPosition)
    {
        Vector2 forward = (playerPosition - squadCenter).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector2.right;

        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 toAgent = agentPosition - squadCenter;

        float side = Vector2.Dot(toAgent, right);
        return side < 0f ? EnemySquadRole.FlankerLeft : EnemySquadRole.FlankerRight;
    }

    private bool HasAgentLineOfSight(IEnemySquadAgent agent)
    {
        if (agent == null || agent.Transform == null || player == null)
            return false;

        EnemyBrain brain = agent as EnemyBrain;
        if (brain != null)
            return brain.HasLineOfSightToPlayer;

        return CombatLineOfSight2D.HasLineOfSight(
            agent.Transform,
            agent.Transform.position,
            player,
            losBlockMask,
            out _
        );
    }

    private float GetLastMeaningfulActionTime(IEnemySquadAgent agent)
    {
        EnemyBrain brain = agent as EnemyBrain;
        if (brain != null)
            return brain.LastMeaningfulActionTime;

        AttackDogBrain dog = agent as AttackDogBrain;
        if (dog != null)
            return dog.LastMeaningfulActionTime;

        return Now;
    }

    private string GetAgentName(IEnemySquadAgent agent)
    {
        if (agent == null || agent.Transform == null)
            return "None";

        return agent.Transform.name;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || player == null)
            return;

        Gizmos.color = debugThreatGapUrgent ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(player.position, coverCampingDistance);

        if (IsPlayerFocusActive() && playerFocusAgent != null && playerFocusAgent.Transform != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(player.position, playerFocusAgent.Transform.position);
            Gizmos.DrawWireSphere(playerFocusAgent.Transform.position, 0.45f);
        }
    }
}
