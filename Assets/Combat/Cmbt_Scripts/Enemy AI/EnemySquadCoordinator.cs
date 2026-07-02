using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates stable squad roles, shared perception, and anti-stalemate pressure.
///
/// Roles are no longer cleared and rebuilt every tick. An enemy keeps a role
/// for a minimum duration unless it must retreat or the squad reaches emergency
/// pressure.
/// </summary>
public class EnemySquadCoordinator : MonoBehaviour
{
    private sealed class RoleStamp
    {
        public EnemySquadRole role;
        public float assignedAt;
    }

    [Header("References")]
    [SerializeField]
    private Transform player;

    [Header("Tick")]
    [SerializeField, Min(0.05f)]
    private float coordinatorTick = 0.35f;

    [SerializeField]
    private bool useUnscaledTime = false;

    [Header("Behavior Weights")]
    [SerializeField]
    private float coverCampingDistance = 8.0f;

    [SerializeField, Range(0f, 1f)]
    private float coverCampingLosThreshold = 0.5f;

    [SerializeField]
    private float playerAggroDistance = 2.5f;

    [SerializeField, Range(0f, 1f)]
    private float lowHpRetreatThreshold = 0.30f;

    [Header("Role Limits")]
    [SerializeField, Min(0)]
    private int maxSuppressors = 1;

    [SerializeField, Min(0)]
    private int maxFlankers = 2;

    [SerializeField]
    private bool forceAtLeastOneAnchor = true;

    [Header("Role Stability")]
    [Tooltip(
        "Minimum time a normal role is kept before the coordinator may replace it."
    )]
    [SerializeField, Min(0f)]
    private float minimumRoleDuration = 2.0f;

    [Tooltip(
        "Above this pressure, anchors may be reassigned early to break a stall."
    )]
    [SerializeField, Range(0f, 1f)]
    private float emergencyRoleOverridePressure = 0.85f;

    [Header("LOS")]
    [SerializeField]
    private LayerMask losBlockMask;

    [Header("Shared Perception")]
    [Tooltip(
        "The squad updates the shared player position only while at least one enemy sees the player."
    )]
    [SerializeField]
    private bool useTrueLastSeenPosition = true;

    [SerializeField, Min(0f)]
    private float lastSeenMemorySeconds = 3.0f;

    [Header("Stalemate / Pressure")]
    [SerializeField]
    private float stalemateGracePeriod = 2.75f;

    [SerializeField]
    private float pressureRampDuration = 3.5f;

    [SerializeField]
    private float playerMoveResetThreshold = 0.6f;

    [SerializeField]
    private float pressureDecayRate = 0.4f;

    [Header("Engagement Health")]
    [Tooltip(
        "An enemy that has done nothing meaningful for this long contributes to pressure."
    )]
    [SerializeField, Min(0.25f)]
    private float idleAgentGrace = 1.8f;

    [SerializeField, Range(0f, 1f)]
    private float blockedLosPressureWeight = 0.65f;

    [SerializeField, Range(0f, 1f)]
    private float idleAgentPressureWeight = 0.45f;

    [Header("Debug")]
    [SerializeField]
    private bool debugLogs = false;

    [SerializeField]
    private bool drawGizmos = true;

    [SerializeField]
    private float debugCurrentPressure = 0f;

    [SerializeField]
    private float debugBlockedLosRatio = 0f;

    [SerializeField]
    private float debugIdleAgentRatio = 0f;

    [SerializeField]
    private bool debugAnyEnemySeesPlayer = false;

    private readonly List<IEnemySquadAgent> agents =
        new List<IEnemySquadAgent>();

    private readonly Dictionary<
        IEnemySquadAgent,
        RoleStamp
    > roleStamps =
        new Dictionary<
            IEnemySquadAgent,
            RoleStamp
        >();

    private float nextTickTime;
    private Vector2 sharedLastSeenPlayerPos;
    private float lastPlayerSeenTime = -999f;

    private float stalemateTimer = 0f;
    private float currentPressure01 = 0f;
    private Vector2 lastPlayerPosForStalemate;
    private bool stalematePlayerPosInitialized = false;

    public Vector2 SharedLastSeenPlayerPos =>
        sharedLastSeenPlayerPos;

    public float CurrentPressure01 =>
        currentPressure01;

    private float Now =>
        useUnscaledTime
            ? Time.unscaledTime
            : Time.time;

    private void Awake()
    {
        TryFindPlayer();

        sharedLastSeenPlayerPos =
            player != null
                ? (Vector2)player.position
                : Vector2.zero;

        nextTickTime =
            Now +
            coordinatorTick;
    }

    private void Update()
    {
        if (player == null)
            TryFindPlayer();

        if (player == null)
            return;

        if (Now < nextTickTime)
            return;

        nextTickTime =
            Now +
            Mathf.Max(
                0.05f,
                coordinatorTick
            );

        TickCoordinator();
    }

    public void Register(
        IEnemySquadAgent agent)
    {
        if (agent == null)
            return;

        if (!agents.Contains(agent))
            agents.Add(agent);
    }

    public void Unregister(
        IEnemySquadAgent agent)
    {
        if (agent == null)
            return;

        agents.Remove(agent);
        roleStamps.Remove(agent);
    }

    private void TickCoordinator()
    {
        CleanupDeadRefs();

        List<IEnemySquadAgent> alive =
            BuildAliveList();

        if (alive.Count == 0)
            return;

        UpdateSharedPerception(alive);
        UpdatePressure(alive);

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            alive[i].SetSharedPlayerPosition(
                sharedLastSeenPlayerPos
            );

            alive[i].NotifySquadPressure(
                currentPressure01
            );
        }

        AssignStableRoles(alive);

        if (debugLogs)
        {
            Debug.Log(
                $"[Squad] Alive={alive.Count} " +
                $"Pressure={currentPressure01:F2} " +
                $"BlockedLOS={debugBlockedLosRatio:F2} " +
                $"Idle={debugIdleAgentRatio:F2} " +
                $"SeesPlayer={debugAnyEnemySeesPlayer}",
                this
            );
        }
    }

    private void UpdateSharedPerception(
        List<IEnemySquadAgent> alive)
    {
        bool anySeesPlayer = false;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            EnemyBrain brain =
                alive[i] as EnemyBrain;

            bool sees =
                brain != null
                    ? brain.HasLineOfSightToPlayer
                    : CombatLineOfSight2D
                        .HasLineOfSight(
                            alive[i].Transform,
                            alive[i].Transform.position,
                            player,
                            losBlockMask,
                            out _
                        );

            if (sees)
            {
                anySeesPlayer = true;
                break;
            }
        }

        debugAnyEnemySeesPlayer =
            anySeesPlayer;

        if (!useTrueLastSeenPosition ||
            anySeesPlayer)
        {
            sharedLastSeenPlayerPos =
                player.position;

            lastPlayerSeenTime = Now;
            return;
        }

        if (Now - lastPlayerSeenTime >
            lastSeenMemorySeconds)
        {
            // Keep the last known position rather than granting exact
            // knowledge through walls. The brain can investigate it.
        }
    }

    private void UpdatePressure(
        List<IEnemySquadAgent> alive)
    {
        Vector2 playerPosition =
            player.position;

        if (!stalematePlayerPosInitialized)
        {
            lastPlayerPosForStalemate =
                playerPosition;

            stalematePlayerPosInitialized =
                true;
        }

        float playerMoveDelta =
            Vector2.Distance(
                playerPosition,
                lastPlayerPosForStalemate
            );

        bool playerIsMoving =
            playerMoveDelta >=
            playerMoveResetThreshold;

        int blockedCount = 0;
        int idleCount = 0;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            EnemyBrain brain =
                alive[i] as EnemyBrain;

            bool hasLos =
                brain != null
                    ? brain.HasLineOfSightToPlayer
                    : CombatLineOfSight2D
                        .HasLineOfSight(
                            alive[i].Transform,
                            alive[i].Transform.position,
                            player,
                            losBlockMask,
                            out _
                        );

            if (!hasLos)
                blockedCount++;

            if (brain != null &&
                Now -
                brain.LastMeaningfulActionTime >=
                idleAgentGrace)
            {
                idleCount++;
            }
        }

        debugBlockedLosRatio =
            alive.Count > 0
                ? blockedCount /
                  (float)alive.Count
                : 0f;

        debugIdleAgentRatio =
            alive.Count > 0
                ? idleCount /
                  (float)alive.Count
                : 0f;

        bool hidden =
            debugBlockedLosRatio >=
            coverCampingLosThreshold;

        bool squadInactive =
            debugIdleAgentRatio >=
            0.5f;

        bool stalemateCondition =
            !playerIsMoving &&
            (hidden || squadInactive);

        if (playerIsMoving)
        {
            lastPlayerPosForStalemate =
                playerPosition;

            stalemateTimer = 0f;

            currentPressure01 =
                Mathf.MoveTowards(
                    currentPressure01,
                    0f,
                    pressureDecayRate *
                    coordinatorTick
                );

            debugCurrentPressure =
                currentPressure01;

            return;
        }

        if (stalemateCondition)
        {
            stalemateTimer +=
                coordinatorTick;

            float elapsedAfterGrace =
                stalemateTimer -
                stalemateGracePeriod;

            float timePressure =
                elapsedAfterGrace > 0f
                    ? Mathf.Clamp01(
                        elapsedAfterGrace /
                        Mathf.Max(
                            0.01f,
                            pressureRampDuration
                        )
                    )
                    : 0f;

            float engagementPressure =
                Mathf.Clamp01(
                    debugBlockedLosRatio *
                    blockedLosPressureWeight +
                    debugIdleAgentRatio *
                    idleAgentPressureWeight
                );

            currentPressure01 =
                Mathf.Max(
                    currentPressure01,
                    timePressure,
                    engagementPressure
                );
        }
        else
        {
            stalemateTimer =
                Mathf.Max(
                    0f,
                    stalemateTimer -
                    coordinatorTick *
                    0.5f
                );

            currentPressure01 =
                Mathf.MoveTowards(
                    currentPressure01,
                    0f,
                    pressureDecayRate *
                    0.5f *
                    coordinatorTick
                );
        }

        debugCurrentPressure =
            currentPressure01;
    }

    private void AssignStableRoles(
        List<IEnemySquadAgent> alive)
    {
        Dictionary<
            IEnemySquadAgent,
            EnemySquadRole
        > desired =
            new Dictionary<
                IEnemySquadAgent,
                EnemySquadRole
            >();

        int suppressors = 0;
        int flankers = 0;
        int anchors = 0;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            IEnemySquadAgent agent =
                alive[i];

            if (agent.Health01 <=
                lowHpRetreatThreshold)
            {
                desired[agent] =
                    EnemySquadRole.Retreater;

                continue;
            }

            if (ShouldPreserveCurrentRole(
                    agent))
            {
                desired[agent] =
                    agent.CurrentRole;

                CountRole(
                    agent.CurrentRole,
                    ref suppressors,
                    ref flankers,
                    ref anchors
                );
            }
        }

        while (suppressors <
               maxSuppressors)
        {
            IEnemySquadAgent candidate =
                PickBestSuppressor(
                    alive,
                    desired
                );

            if (candidate == null)
                break;

            desired[candidate] =
                EnemySquadRole.Suppressor;

            suppressors++;
        }

        bool playerAggressive =
            IsPlayerAggressive(alive);

        bool shouldFlank =
            debugBlockedLosRatio >=
                coverCampingLosThreshold ||
            (!playerAggressive &&
             alive.Count >= 3) ||
            currentPressure01 >= 0.5f;

        IEnemySquadAgent firstFlanker =
            null;

        while (shouldFlank &&
               flankers <
               maxFlankers)
        {
            IEnemySquadAgent candidate =
                PickBestFlanker(
                    alive,
                    desired
                );

            if (candidate == null)
                break;

            EnemySquadRole side;

            if (firstFlanker == null)
            {
                side =
                    ChooseFlankSide(
                        candidate.Transform.position,
                        ComputeSquadCenter(alive),
                        sharedLastSeenPlayerPos
                    );

                firstFlanker = candidate;
            }
            else
            {
                EnemySquadRole firstRole =
                    desired[firstFlanker];

                side =
                    firstRole ==
                    EnemySquadRole.FlankerLeft
                        ? EnemySquadRole.FlankerRight
                        : EnemySquadRole.FlankerLeft;
            }

            desired[candidate] = side;
            flankers++;
        }

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            IEnemySquadAgent agent =
                alive[i];

            if (desired.ContainsKey(agent))
                continue;

            desired[agent] =
                EnemySquadRole.Anchor;

            anchors++;
        }

        if (forceAtLeastOneAnchor &&
            anchors == 0)
        {
            IEnemySquadAgent fallback =
                PickBestAnchorCandidate(
                    alive
                );

            if (fallback != null)
            {
                desired[fallback] =
                    EnemySquadRole.Anchor;
            }
        }

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            ApplyRole(
                alive[i],
                desired[alive[i]]
            );
        }
    }

    private bool ShouldPreserveCurrentRole(
        IEnemySquadAgent agent)
    {
        EnemySquadRole current =
            agent.CurrentRole;

        if (current ==
            EnemySquadRole.None)
        {
            return false;
        }

        if (!roleStamps.TryGetValue(
                agent,
                out RoleStamp stamp))
        {
            roleStamps[agent] =
                new RoleStamp
                {
                    role = current,
                    assignedAt = Now
                };

            return true;
        }

        if (stamp.role != current)
        {
            stamp.role = current;
            stamp.assignedAt = Now;
        }

        if (currentPressure01 >=
                emergencyRoleOverridePressure &&
            current ==
                EnemySquadRole.Anchor)
        {
            return false;
        }

        return
            Now -
            stamp.assignedAt <
            minimumRoleDuration;
    }

    private void ApplyRole(
        IEnemySquadAgent agent,
        EnemySquadRole role)
    {
        if (agent.CurrentRole == role)
        {
            if (!roleStamps.ContainsKey(
                    agent))
            {
                roleStamps[agent] =
                    new RoleStamp
                    {
                        role = role,
                        assignedAt = Now
                    };
            }

            return;
        }

        agent.SetRole(role);

        roleStamps[agent] =
            new RoleStamp
            {
                role = role,
                assignedAt = Now
            };
    }

    private void CountRole(
        EnemySquadRole role,
        ref int suppressors,
        ref int flankers,
        ref int anchors)
    {
        if (role ==
            EnemySquadRole.Suppressor)
        {
            suppressors++;
        }
        else if (
            role ==
                EnemySquadRole.FlankerLeft ||
            role ==
                EnemySquadRole.FlankerRight)
        {
            flankers++;
        }
        else if (role ==
                 EnemySquadRole.Anchor)
        {
            anchors++;
        }
    }

    private void TryFindPlayer()
    {
        CombatPawn pawn =
            FindObjectOfType<
                CombatPawn
            >(true);

        if (pawn != null)
            player = pawn.transform;
    }

    private List<IEnemySquadAgent>
        BuildAliveList()
    {
        List<IEnemySquadAgent> alive =
            new List<IEnemySquadAgent>();

        for (int i =
                 agents.Count - 1;
             i >= 0;
             i--)
        {
            IEnemySquadAgent agent =
                agents[i];

            if (agent == null ||
                agent.Transform == null)
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

    private void CleanupDeadRefs()
    {
        for (int i =
                 agents.Count - 1;
             i >= 0;
             i--)
        {
            IEnemySquadAgent agent =
                agents[i];

            if (agent == null ||
                agent.Transform == null)
            {
                roleStamps.Remove(agent);
                agents.RemoveAt(i);
            }
        }
    }

    private Vector2 ComputeSquadCenter(
        List<IEnemySquadAgent> alive)
    {
        Vector2 sum = Vector2.zero;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            sum +=
                (Vector2)alive[i]
                    .Transform.position;
        }

        return
            alive.Count > 0
                ? sum / alive.Count
                : Vector2.zero;
    }

    private bool IsPlayerAggressive(
        List<IEnemySquadAgent> alive)
    {
        int closeCount = 0;
        Vector2 playerPosition =
            player.position;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            float distance =
                Vector2.Distance(
                    alive[i].Transform.position,
                    playerPosition
                );

            if (distance <=
                playerAggroDistance)
            {
                closeCount++;
            }
        }

        return closeCount >=
            Mathf.CeilToInt(
                alive.Count *
                0.5f
            );
    }

    private IEnemySquadAgent
        PickBestSuppressor(
            List<IEnemySquadAgent> alive,
            Dictionary<
                IEnemySquadAgent,
                EnemySquadRole
            > assigned)
    {
        IEnemySquadAgent best = null;
        float bestScore =
            float.NegativeInfinity;

        Vector2 playerPosition =
            player.position;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            IEnemySquadAgent agent =
                alive[i];

            if (assigned.ContainsKey(agent) ||
                !agent.IsRanged)
            {
                continue;
            }

            EnemyBrain brain =
                agent as EnemyBrain;

            bool hasLos =
                brain != null
                    ? brain.HasLineOfSightToPlayer
                    : CombatLineOfSight2D
                        .HasLineOfSight(
                            agent.Transform,
                            agent.Transform.position,
                            player,
                            losBlockMask,
                            out _
                        );

            float distance =
                Vector2.Distance(
                    agent.Transform.position,
                    playerPosition
                );

            float score = 0f;
            score += hasLos ? 2f : -1f;

            score +=
                Mathf.Clamp01(
                    1f -
                    Mathf.Abs(
                        distance - 4f
                    ) /
                    4f
                );

            score += agent.Health01;

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private IEnemySquadAgent
        PickBestFlanker(
            List<IEnemySquadAgent> alive,
            Dictionary<
                IEnemySquadAgent,
                EnemySquadRole
            > assigned)
    {
        IEnemySquadAgent best = null;
        float bestScore =
            float.NegativeInfinity;

        Vector2 playerPosition =
            player.position;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            IEnemySquadAgent agent =
                alive[i];

            if (assigned.ContainsKey(agent))
                continue;

            float distance =
                Vector2.Distance(
                    agent.Transform.position,
                    playerPosition
                );

            float score = 0f;

            score +=
                1f -
                Mathf.Clamp01(
                    distance /
                    10f
                );

            score +=
                agent.IsMelee
                    ? 0.6f
                    : 0.2f;

            score += agent.Health01;

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private IEnemySquadAgent
        PickBestAnchorCandidate(
            List<IEnemySquadAgent> alive)
    {
        IEnemySquadAgent best = null;
        float bestScore =
            float.NegativeInfinity;

        Vector2 playerPosition =
            player.position;

        for (int i = 0;
             i < alive.Count;
             i++)
        {
            IEnemySquadAgent agent =
                alive[i];

            float distance =
                Vector2.Distance(
                    agent.Transform.position,
                    playerPosition
                );

            float score =
                agent.Health01 +
                Mathf.Clamp01(
                    distance /
                    8f
                );

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private EnemySquadRole ChooseFlankSide(
        Vector2 agentPosition,
        Vector2 squadCenter,
        Vector2 playerPosition)
    {
        Vector2 forward =
            (
                playerPosition -
                squadCenter
            ).normalized;

        if (forward.sqrMagnitude <
            0.0001f)
        {
            forward = Vector2.right;
        }

        Vector2 right =
            new Vector2(
                forward.y,
                -forward.x
            );

        Vector2 fromPlayer =
            agentPosition -
            playerPosition;

        float side =
            Vector2.Dot(
                fromPlayer,
                right
            );

        return
            side >= 0f
                ? EnemySquadRole.FlankerRight
                : EnemySquadRole.FlankerLeft;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos ||
            player == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            player.position,
            playerAggroDistance
        );

        Gizmos.color =
            new Color(
                0f,
                1f,
                1f,
                0.25f
            );

        Gizmos.DrawWireSphere(
            player.position,
            coverCampingDistance
        );

        Gizmos.color = Color.white;

        Gizmos.DrawSphere(
            sharedLastSeenPlayerPos,
            0.08f
        );

        if (currentPressure01 > 0.05f)
        {
            Gizmos.color =
                new Color(
                    1f,
                    0f,
                    0f,
                    currentPressure01 *
                    0.5f
                );

            Gizmos.DrawWireSphere(
                player.position,
                0.3f +
                currentPressure01 *
                0.4f
            );
        }
    }
#endif
}
