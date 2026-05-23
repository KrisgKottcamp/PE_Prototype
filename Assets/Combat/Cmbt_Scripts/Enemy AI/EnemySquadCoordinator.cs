using System.Collections.Generic;
using UnityEngine;

public class EnemySquadCoordinator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player; // auto-find CombatPawn if null

    [Header("Tick")]
    [SerializeField] private float coordinatorTick = 0.35f;
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Behavior Weights")]
    [SerializeField] private float coverCampingDistance = 6.0f;
    [SerializeField] private float coverCampingLosThreshold = 0.5f; // if many enemies lose LOS to player
    [SerializeField] private float playerAggroDistance = 2.5f;
    [SerializeField] private float lowHpRetreatThreshold = 0.30f;

    [Header("Role Limits")]
    [SerializeField] private int maxSuppressors = 1;
    [SerializeField] private int maxFlankers = 2; // left+right total
    [SerializeField] private bool forceAtLeastOneAnchor = true;

    [Header("LOS")]
    [SerializeField] private LayerMask losBlockMask; // reuse Obstacles layer

    [Header("Stalemate / Pressure Escalation")]
    [Tooltip("Seconds the player must remain stationary and hidden before pressure starts building.")]
    [SerializeField] private float stalemateGracePeriod = 3.5f;
    [Tooltip("Seconds it takes to ramp from zero pressure to full pressure (1.0) after the grace period ends.")]
    [SerializeField] private float pressureRampDuration = 4.0f;
    [Tooltip("How far the player must move (world units) to be considered 'active' and reset the stalemate timer.")]
    [SerializeField] private float playerMoveResetThreshold = 0.6f;
    [Tooltip("How quickly pressure bleeds off when the player becomes active again. 1 = instant reset, 0.1 = slow bleed.")]
    [SerializeField] private float pressureDecayRate = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private float debugCurrentPressure = 0f;

    private readonly List<IEnemySquadAgent> agents = new();
    private float nextTickTime;
    private Vector2 sharedLastSeenPlayerPos;

    // Stalemate tracking
    private float stalemateTimer = 0f;
    private float currentPressure01 = 0f;
    private Vector2 lastPlayerPosForStalemate;
    private bool stalematePlayerPosInitialized = false;

    public Vector2 SharedLastSeenPlayerPos => sharedLastSeenPlayerPos;
    public float CurrentPressure01 => currentPressure01;

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void Awake()
    {
        TryFindPlayer();
        sharedLastSeenPlayerPos = player != null ? (Vector2)player.position : Vector2.zero;
        nextTickTime = Now + coordinatorTick;
    }

    private void Update()
    {
        if (player == null) TryFindPlayer();
        if (player == null) return;

        if (Now < nextTickTime) return;
        nextTickTime = Now + Mathf.Max(0.05f, coordinatorTick);

        TickCoordinator();
    }

    public void Register(IEnemySquadAgent agent)
    {
        if (agent == null) return;
        if (!agents.Contains(agent)) agents.Add(agent);
    }

    public void Unregister(IEnemySquadAgent agent)
    {
        if (agent == null) return;
        agents.Remove(agent);
    }

    private void TickCoordinator()
    {
        CleanupDeadRefs();

        if (agents.Count == 0) return;

        sharedLastSeenPlayerPos = player.position;

        // Push shared player position to all agents
        for (int i = 0; i < agents.Count; i++)
            agents[i].SetSharedPlayerPosition(sharedLastSeenPlayerPos);

        // Build alive list
        List<IEnemySquadAgent> alive = new();
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] != null && agents[i].IsAlive) alive.Add(agents[i]);
        }

        if (alive.Count == 0) return;

        // --- Stalemate / Pressure Escalation ---
        UpdatePressure(alive);

        // Push pressure to all alive agents
        for (int i = 0; i < alive.Count; i++)
            alive[i].NotifySquadPressure(currentPressure01);

        // Compute squad context
        Vector2 squadCenter = ComputeSquadCenter(alive);
        bool playerIsAggressive = IsPlayerAggressive(alive);
        bool playerIsCoverCamping = IsPlayerCoverCamping(alive);

        // Sort by urgency: low HP first for retreat consideration
        alive.Sort((a, b) => a.Health01.CompareTo(b.Health01));

        // Role pools
        int suppressors = 0;
        int flankers = 0;
        int anchors = 0;

        // 1) Force retreaters first (low HP)
        for (int i = 0; i < alive.Count; i++)
        {
            var ag = alive[i];
            if (ag.Health01 <= lowHpRetreatThreshold)
            {
                ag.SetRole(EnemySquadRole.Retreater);
            }
            else
            {
                ag.SetRole(EnemySquadRole.None);
            }
        }

        // 2) Assign suppressor
        if (maxSuppressors > 0)
        {
            IEnemySquadAgent bestSuppressor = PickBestSuppressor(alive);
            if (bestSuppressor != null && bestSuppressor.CurrentRole == EnemySquadRole.None)
            {
                bestSuppressor.SetRole(EnemySquadRole.Suppressor);
                suppressors++;
            }
        }

        // 3) Assign flankers when player cover-camps, or occasionally in balanced mode,
        //    or when pressure is high (force flanking to break stalemate)
        bool shouldFlank = playerIsCoverCamping
            || (!playerIsAggressive && alive.Count >= 3)
            || currentPressure01 >= 0.5f;

        if (shouldFlank && maxFlankers > 0)
        {
            // Pick up to 2 flankers, distribute left/right
            IEnemySquadAgent flankA = PickBestFlanker(alive, exclude: null);
            if (flankA != null && flankA.CurrentRole == EnemySquadRole.None)
            {
                EnemySquadRole side = ChooseFlankSide(flankA.Transform.position, squadCenter, sharedLastSeenPlayerPos);
                flankA.SetRole(side);
                flankers++;
            }

            if (flankers < maxFlankers)
            {
                IEnemySquadAgent flankB = PickBestFlanker(alive, exclude: flankA);
                if (flankB != null && flankB.CurrentRole == EnemySquadRole.None)
                {
                    // Prefer opposite side for spread
                    EnemySquadRole sideB = EnemySquadRole.FlankerLeft;
                    if (flankA != null && flankA.CurrentRole == EnemySquadRole.FlankerLeft) sideB = EnemySquadRole.FlankerRight;
                    else if (flankA != null && flankA.CurrentRole == EnemySquadRole.FlankerRight) sideB = EnemySquadRole.FlankerLeft;
                    else sideB = ChooseFlankSide(flankB.Transform.position, squadCenter, sharedLastSeenPlayerPos);

                    flankB.SetRole(sideB);
                    flankers++;
                }
            }
        }

        // 4) Remaining become anchors
        for (int i = 0; i < alive.Count; i++)
        {
            var ag = alive[i];
            if (ag.CurrentRole == EnemySquadRole.None)
            {
                ag.SetRole(EnemySquadRole.Anchor);
                anchors++;
            }
        }

        // 5) Ensure at least one anchor if requested
        if (forceAtLeastOneAnchor && anchors == 0)
        {
            IEnemySquadAgent fallback = PickBestAnchorCandidate(alive);
            if (fallback != null)
            {
                fallback.SetRole(EnemySquadRole.Anchor);
                anchors = 1;
            }
        }

        if (debugLogs)
        {
            Debug.Log($"[Squad] Alive={alive.Count} Aggro={playerIsAggressive} CoverCamp={playerIsCoverCamping} " +
                      $"Pressure={currentPressure01:F2} Stalemate={stalemateTimer:F1}s Roles S:{suppressors} F:{flankers} A:{anchors}");
        }
    }

    // ------------------------------------
    // Pressure / Stalemate
    // ------------------------------------

    private void UpdatePressure(List<IEnemySquadAgent> alive)
    {
        Vector2 playerPos = player.position;

        if (!stalematePlayerPosInitialized)
        {
            lastPlayerPosForStalemate = playerPos;
            stalematePlayerPosInitialized = true;
        }

        float playerMoveDelta = Vector2.Distance(playerPos, lastPlayerPosForStalemate);
        bool playerIsMoving = playerMoveDelta >= playerMoveResetThreshold;

        // The squad is in stalemate if the player is hiding (cover-camping) and not moving.
        // We also only build pressure when there are living enemies that are actually passive.
        bool squadIsPassive = IsPlayerCoverCamping(alive);
        bool stalemateCondition = !playerIsMoving && squadIsPassive;

        if (playerIsMoving)
        {
            // Player moved — decay pressure, reset stalemate timer
            lastPlayerPosForStalemate = playerPos;
            stalemateTimer = 0f;
            currentPressure01 = Mathf.MoveTowards(currentPressure01, 0f, pressureDecayRate * coordinatorTick);
        }
        else if (stalemateCondition)
        {
            stalemateTimer += coordinatorTick;

            float excess = stalemateTimer - stalemateGracePeriod;
            if (excess > 0f)
            {
                float targetPressure = Mathf.Clamp01(excess / Mathf.Max(0.01f, pressureRampDuration));
                // Pressure only ever increases during a stalemate
                currentPressure01 = Mathf.Max(currentPressure01, targetPressure);
            }
        }
        else
        {
            // Player is stationary but enemies have LOS — slow decay
            stalemateTimer = Mathf.Max(0f, stalemateTimer - coordinatorTick * 0.5f);
            currentPressure01 = Mathf.MoveTowards(currentPressure01, 0f, pressureDecayRate * 0.5f * coordinatorTick);
        }

        debugCurrentPressure = currentPressure01;
    }

    // ------------------------------------
    // Player / squad helpers
    // ------------------------------------

    private void TryFindPlayer()
    {
        CombatPawn pawn = FindObjectOfType<CombatPawn>(true);
        if (pawn != null) player = pawn.transform;
    }

    private void CleanupDeadRefs()
    {
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            if (agents[i] == null || agents[i].Transform == null)
                agents.RemoveAt(i);
        }
    }

    private Vector2 ComputeSquadCenter(List<IEnemySquadAgent> alive)
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        for (int i = 0; i < alive.Count; i++)
        {
            sum += (Vector2)alive[i].Transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector2.zero;
    }

    private bool IsPlayerAggressive(List<IEnemySquadAgent> alive)
    {
        int closeCount = 0;
        Vector2 p = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            float d = Vector2.Distance(alive[i].Transform.position, p);
            if (d <= playerAggroDistance) closeCount++;
        }

        return closeCount >= Mathf.CeilToInt(alive.Count * 0.5f);
    }

    private bool IsPlayerCoverCamping(List<IEnemySquadAgent> alive)
    {
        int blockedLos = 0;
        int inRange = 0;
        Vector2 p = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            float d = Vector2.Distance(a.Transform.position, p);
            if (d <= coverCampingDistance)
            {
                inRange++;
                bool blocked = Physics2D.Linecast(a.Transform.position, p, losBlockMask);
                if (blocked) blockedLos++;
            }
        }

        if (inRange == 0) return false;
        float ratio = (float)blockedLos / inRange;
        return ratio >= coverCampingLosThreshold;
    }

    private IEnemySquadAgent PickBestSuppressor(List<IEnemySquadAgent> alive)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 p = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            if (!a.IsAlive || a.CurrentRole != EnemySquadRole.None) continue;
            if (!a.IsRanged) continue;

            float dist = Vector2.Distance(a.Transform.position, p);
            bool hasLos = !Physics2D.Linecast(a.Transform.position, p, losBlockMask);

            float score = 0f;
            score += hasLos ? 2.0f : -1.0f;
            score += Mathf.Clamp01(1f - Mathf.Abs(dist - 4.0f) / 4.0f);
            score += a.Health01;

            if (score > bestScore) { bestScore = score; best = a; }
        }
        return best;
    }

    private IEnemySquadAgent PickBestFlanker(List<IEnemySquadAgent> alive, IEnemySquadAgent exclude)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 p = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            if (!a.IsAlive || a.CurrentRole != EnemySquadRole.None) continue;
            if (a == exclude) continue;

            float dist = Vector2.Distance(a.Transform.position, p);
            float score = 0f;
            score += 1.0f - Mathf.Clamp01(dist / 10f);
            score += a.IsMelee ? 0.6f : 0.2f;
            score += a.Health01;

            if (score > bestScore) { bestScore = score; best = a; }
        }
        return best;
    }

    private IEnemySquadAgent PickBestAnchorCandidate(List<IEnemySquadAgent> alive)
    {
        IEnemySquadAgent best = null;
        float bestScore = float.NegativeInfinity;
        Vector2 p = player.position;

        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            if (!a.IsAlive) continue;
            float dist = Vector2.Distance(a.Transform.position, p);
            float score = a.Health01 + Mathf.Clamp01(dist / 8f);
            if (score > bestScore) { bestScore = score; best = a; }
        }
        return best;
    }

    private EnemySquadRole ChooseFlankSide(Vector2 agentPos, Vector2 squadCenter, Vector2 playerPos)
    {
        Vector2 forward = (playerPos - squadCenter).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector2.right;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 v = agentPos - playerPos;
        float sideDot = Vector2.Dot(v, right);
        return sideDot >= 0f ? EnemySquadRole.FlankerRight : EnemySquadRole.FlankerLeft;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos || player == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.position, playerAggroDistance);

        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(player.position, coverCampingDistance);

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(sharedLastSeenPlayerPos, 0.08f);

        // Pressure indicator: draw a pulsing red sphere around player when pressure is high
        if (currentPressure01 > 0.05f)
        {
            Gizmos.color = new Color(1f, 0f, 0f, currentPressure01 * 0.5f);
            Gizmos.DrawWireSphere(player.position, 0.3f + currentPressure01 * 0.4f);
        }
    }
#endif
}