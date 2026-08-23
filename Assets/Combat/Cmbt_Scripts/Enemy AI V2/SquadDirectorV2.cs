using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class SquadDirectorV2 : MonoBehaviour
    {
        private enum PlanPhase
        {
            None,
            Forming,
            ControllerAttack,
            FlankerAttack,
            Recovering
        }

        private struct AttackSelection
        {
            public string patternName;
            public string debugReason;
            public bool overrideBurst;
            public int shotsPerBurst;
            public float intraBurstInterval;
            public float burstCooldown;
            public bool multiplyCurrentCadence;
            public float intraBurstIntervalMultiplier;
            public float burstCooldownMultiplier;
            public bool overridePatternShape;
            public int fanBullets;
            public float fanArcDegrees;
            public int ringBullets;
            public float angularSpeedDegPerTick;
        }

        [Header("Mode")]
        [SerializeField] private EnemyAIV2BackendMode backendMode =
            EnemyAIV2BackendMode.ObserveOnly;

        [Header("Mode Transition Debug v2")]
        [SerializeField] private EnemyAIV2BackendMode debugAppliedBackendMode =
            EnemyAIV2BackendMode.ObserveOnly;
        [SerializeField] private string debugBackendModeTransition = "Not applied yet";

        [Header("References")]
        [SerializeField] private EnemyAIV2Profile profile;
        [SerializeField] private ArenaNavigationGrid navigationGrid;
        [SerializeField] private Transform player;
        [SerializeField] private string playerTag = "PlayerCombatPawn";

        [Header("Runtime Plan")]
        [SerializeField] private SquadTacticV2 currentTactic = SquadTacticV2.None;
        [SerializeField] private string debugPlan = "Not started";
        [SerializeField] private string debugPhase = "None";
        [SerializeField] private int debugAliveAgents;
        [SerializeField] private float debugPlanAge;
        [SerializeField] private EnemyAgentV2 debugController;
        [SerializeField] private EnemyAgentV2 debugFlanker;
        [SerializeField] private EnemyAgentV2 debugSentinel;
        [SerializeField] private Vector2 debugControllerSlot;
        [SerializeField] private Vector2 debugFlankerSlot;
        [SerializeField] private Vector2 debugSentinelSlot;

        [Header("Stage 2 Debug")]
        [SerializeField] private string debugStage2Watchdog = "Idle";
        [SerializeField] private int debugClosePairs;
        [SerializeField] private float debugClosestEnemyDistance;
        [SerializeField] private float debugClumpAge;
        [SerializeField] private string debugLastFailureResponse = "None";
        [SerializeField] private string debugDestinationLeaseState = "None";

        [Header("Stage 3 Attack Identity Debug")]
        [SerializeField] private string debugLastAttackSelection = "None";
        [SerializeField] private string debugControllerAttackSelection = "None";
        [SerializeField] private string debugFlankerAttackSelection = "None";
        [SerializeField] private string debugSentinelAttackSelection = "None";
        [SerializeField] private string debugSoloAttackSelection = "None";
        [SerializeField] private string debugRoleAttackProfileSelection = "None";

        [Header("Stage 4 Sentinel Debug")]
        [SerializeField] private string debugSentinelPlan = "None";
        [SerializeField] private float debugSentinelLaneScore;
        [SerializeField] private int debugSentinelLaneCoverage;

        [Header("Stage 4.1 Attack Concurrency Debug")]
        [SerializeField] private string debugAttackConcurrencyGate = "Off";
        [SerializeField] private EnemyAgentV2 debugActiveAttacker;
        [SerializeField] private float debugAttackGateGapRemaining;

        [Header("Stage 4.2 Pressure Pacing Debug")]
        [SerializeField] private string debugPressurePacing = "Off";
        [SerializeField] private string debugCombatTempo = "Default";
        [SerializeField] private float debugAttackStartGapRemaining;
        [SerializeField] private float debugPhraseBreatherRemaining;

        [Header("Skill Cadence Debug")]
        [SerializeField] private string debugSkillCadence = "Not evaluated";
        [SerializeField] private float debugSkillStartGapRemaining;
        [SerializeField] private int debugLegacyAttacksSinceSkill;
        [SerializeField] private int debugConsecutiveSkillActions;

        [Header("Stage 3.5 Fluid Movement Debug")]
        [SerializeField] private string debugFluidDecision = "None";
        [SerializeField] private bool debugControllerUsedFluidPressure;
        [SerializeField] private bool debugFlankerUsedFluidPressure;
        [SerializeField] private bool debugSentinelUsedFluidPressure;
        [SerializeField] private float debugCurrentFlankAngle;

        [Header("Runtime Readiness")]
        [SerializeField] private bool debugRuntimeReady;
        [SerializeField] private string debugReadinessReason = "Not initialized";
        [SerializeField] private bool debugHasProfile;
        [SerializeField] private bool debugHasPlayer;
        [SerializeField] private bool debugHasNavigationGrid;

        private readonly List<EnemyAgentV2> agents = new List<EnemyAgentV2>();
        private readonly Dictionary<EnemyAgentV2, float> lastFailureResponseTime = new Dictionary<EnemyAgentV2, float>();
        private readonly Dictionary<EnemyAgentV2, string> lastAttackPatternByAgent = new Dictionary<EnemyAgentV2, string>();
        private readonly Dictionary<EnemyRoleV2, string> lastAttackPatternByRole = new Dictionary<EnemyRoleV2, string>();

        private float nextTickTime;
        private float planStartedAt;
        private float phaseStartedAt;
        private float clumpStartedAt = -1f;
        private float lastClumpReformAt = -999f;
        private PlanPhase phase = PlanPhase.None;
        private EnemyAgentV2 controller;
        private EnemyAgentV2 flanker;
        private EnemyAgentV2 sentinel;
        private int nextOrderId = 1;
        private bool controllerAttackIssued;
        private bool flankerAttackIssued;
        private bool sentinelAttackIssued;
        private bool attackGateWasOccupied;
        private float lastAttackGateReleasedAt = -999f;
        private float lastAnyAttackStartedAt = -999f;
        private float lastSkillStartedAt = -999f;
        private int legacyAttacksSinceLastSkill = int.MaxValue;
        private int consecutiveSkillActions;
        private float phraseBreatherUntil = -1f;

        private EnemyAIV2Profile lastAppliedProfile;
        private ArenaNavigationGrid lastAppliedGrid;
        private Transform lastAppliedPlayer;
        private bool wasRuntimeReady;
        private EnemyAIV2BackendMode lastAppliedBackendMode;
        private bool backendModeHasBeenApplied;

        public EnemyAIV2BackendMode BackendMode => backendMode;
        public IReadOnlyList<EnemyAgentV2> Agents => agents;
        public Transform Player => player;
        public SquadTacticV2 CurrentTactic => currentTactic;
        public bool RuntimeReady => profile != null && player != null;

        private void Awake()
        {
            ResolveReferences();
            RefreshReadinessDebug();
        }

        private void OnEnable()
        {
            ResolveReferences();
            DiscoverAgents();
            ApplyBackendModeChange("Director enabled", force: true);
            RefreshReadinessDebug();
        }

        private void Update()
        {
            bool playerChanged = ResolvePlayer();
            CleanupAgents();

            bool backendModeChanged =
                !backendModeHasBeenApplied ||
                lastAppliedBackendMode != backendMode;

            bool contextChanged =
                playerChanged ||
                lastAppliedProfile != profile ||
                lastAppliedGrid != navigationGrid ||
                lastAppliedPlayer != player;

            if (backendModeChanged)
            {
                ApplyBackendModeChange($"Backend mode changed to {backendMode}", force: true);
            }
            else if (contextChanged)
            {
                RefreshAgentRuntimeContexts(force: true);
                BeginNewPlan("Runtime context changed");
            }

            RefreshReadinessDebug();

            if (wasRuntimeReady != RuntimeReady)
            {
                wasRuntimeReady = RuntimeReady;
                ApplyBackendModeChange(RuntimeReady
                    ? "V2 runtime became ready"
                    : "V2 runtime lost required references", force: true);
            }

            if (Time.time < nextTickTime)
                return;

            nextTickTime =
                Time.time +
                Mathf.Max(0.02f,
                    profile != null ? profile.directorTickSeconds : 0.1f);

            TickDirector();
        }

        public void Register(EnemyAgentV2 agent)
        {
            if (agent == null || agents.Contains(agent))
                return;

            agents.Add(agent);
            agent.Initialize(this, player, profile, navigationGrid);
            BeginNewPlan($"Registered {agent.name}");
        }

        public void Unregister(EnemyAgentV2 agent)
        {
            if (agent == null)
                return;

            agents.Remove(agent);
            lastFailureResponseTime.Remove(agent);

            if (controller == agent)
                controller = null;

            if (flanker == agent)
                flanker = null;

            if (sentinel == agent)
                sentinel = null;

            BeginNewPlan($"Unregistered {agent.name}");
        }

        [ContextMenu("Discover AI V2 Agents")]
        public void DiscoverAgents()
        {
            EnemyAgentV2[] found = FindObjectsOfType<EnemyAgentV2>(true);

            for (int i = 0; i < found.Length; i++)
                Register(found[i]);
        }

        [ContextMenu("Apply Backend Mode")]
        public void ApplyBackendMode()
        {
            ApplyBackendModeChange($"Manual apply: {backendMode}", force: true);
        }

        [ContextMenu("Force New Plan")]
        public void ForceNewPlan()
        {
            BeginNewPlan("Manual force");
        }

        public Vector2 GetDestinationSeparationDirection(EnemyAgentV2 requester, Vector2 currentPosition)
        {
            if (profile == null || requester == null || !profile.useDestinationLeases)
                return Vector2.zero;

            Vector2 result = Vector2.zero;
            float radius = Mathf.Max(0.05f, profile.assignedSlotRepulsionRadius);

            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 other = agents[i];

                if (other == null || other == requester || !other.IsAlive)
                    continue;

                if (other.CurrentRole == EnemyRoleV2.Unassigned)
                    continue;

                Vector2 away = currentPosition - other.AssignedSlot;
                float distance = away.magnitude;

                if (distance <= 0.001f || distance >= radius)
                    continue;

                result += away.normalized * (1f - distance / radius);
            }

            return result;
        }

        private void TickDirector()
        {
            if (!RuntimeReady)
            {
                debugPlan = profile == null
                    ? "Waiting for EnemyAIV2Profile; legacy AI remains active"
                    : "Waiting for player; legacy AI remains active";
                debugPhase = "Compatibility fallback";
                return;
            }

            List<EnemyAgentV2> alive = BuildAliveList();
            debugAliveAgents = alive.Count;
            debugPlanAge = Time.time - planStartedAt;
            TickAttackConcurrencyDebug(alive);

            if (alive.Count == 0)
            {
                currentTactic = SquadTacticV2.None;
                debugPlan = "No living AI V2 agents";
                return;
            }

            if (backendMode == EnemyAIV2BackendMode.ObserveOnly)
            {
                BuildObservedAssignments(alive);
                TickStage2DebugOnly(alive);
                debugPhase = "ObserveOnly";
                return;
            }

            if (TickStage2Watchdogs(alive))
                return;

            if (Time.time - planStartedAt >=
                Mathf.Max(0.5f, profile.planMaximumSeconds))
            {
                BeginNewPlan("Plan hard timeout");
                return;
            }

            if (alive.Count == 1)
                TickSoloDuel(alive[0]);
            else
                TickPinAndPincer(alive);
        }

        private void TickSoloDuel(EnemyAgentV2 solo)
        {
            if (currentTactic != SquadTacticV2.SoloDuel || controller != solo)
                BeginSoloPlan(solo);

            debugPhase = phase.ToString();

            if (solo.ActionRunner == null)
                return;

            switch (phase)
            {
                case PlanPhase.Forming:
                    if (IsActionFailed(solo) ||
                        IsActionFinished(solo) ||
                        Time.time - phaseStartedAt >= profile.formationMaximumSeconds)
                    {
                        bool issued = IssuePressureOrAttack(
                            solo,
                            EnemyRoleV2.SoloDuelist,
                            solo.AssignedSlot,
                            "Solo fail-open / opening shot",
                            preferFluid: profile.useFluidCombatMovement && profile.soloUsesSkirmishPressure
                        );

                        if (issued)
                            SetPhase(PlanPhase.ControllerAttack);
                    }
                    break;

                case PlanPhase.ControllerAttack:
                    if (IsActionFinished(solo))
                    {
                        IssueRecover(solo, GetRecoverySeconds(EnemyRoleV2.SoloDuelist));
                        SetPhase(PlanPhase.Recovering);
                    }
                    break;

                case PlanPhase.Recovering:
                    if (IsActionFinished(solo) && CanCompletePhraseAfterBreather(soloPhrase: true, hasSentinel: false))
                        BeginNewPlan("Solo phrase complete");
                    break;
            }
        }

        private void TickPinAndPincer(List<EnemyAgentV2> alive)
        {
            if ((currentTactic != SquadTacticV2.PinAndPincer &&
                 currentTactic != SquadTacticV2.PinPincerSentinel) ||
                controller == null || flanker == null ||
                !controller.IsAlive || !flanker.IsAlive ||
                (currentTactic == SquadTacticV2.PinPincerSentinel &&
                 (sentinel == null || !sentinel.IsAlive)))
            {
                BeginPinAndPincerPlan(alive);
            }

            if (controller == null || flanker == null)
                return;

            debugPhase = phase.ToString();

            switch (phase)
            {
                case PlanPhase.Forming:
                {
                    if (HandleMovementFailure(controller, EnemyRoleV2.Controller, alive))
                        return;

                    if (HandleMovementFailure(flanker, EnemyRoleV2.Flanker, alive))
                        return;

                    if (sentinel != null && HandleMovementFailure(sentinel, EnemyRoleV2.Sentinel, alive))
                        return;

                    bool controllerReady = IsMoveSuccessfulOrClose(controller);
                    bool formationTimedOut =
                        Time.time - phaseStartedAt >=
                        Mathf.Max(0.2f, profile.formationMaximumSeconds);

                    bool controllerFluidReady =
                        profile.useFluidCombatMovement &&
                        profile.controllerPressuresWhileForming &&
                        Time.time - phaseStartedAt >= Mathf.Max(0f, profile.controllerStartPressureAfterSeconds);

                    if (!controllerAttackIssued &&
                        (controllerReady || formationTimedOut || controllerFluidReady))
                    {
                        bool usedFluid = IssuePressureOrAttack(
                            controller,
                            EnemyRoleV2.Controller,
                            controller.AssignedSlot,
                            controllerFluidReady
                                ? "Controller moving pressure: good-enough lane"
                                : "Controller begins pin pressure",
                            preferFluid: profile.useFluidCombatMovement && profile.controllerPressuresWhileForming
                        );

                        if (!usedFluid)
                            break;

                        debugControllerUsedFluidPressure = usedFluid;
                        controllerAttackIssued = true;
                        SetPhase(PlanPhase.ControllerAttack);
                    }
                    break;
                }

                case PlanPhase.ControllerAttack:
                {
                    if (HandleMovementFailure(flanker, EnemyRoleV2.Flanker, alive))
                        return;

                    if (sentinel != null && HandleMovementFailure(sentinel, EnemyRoleV2.Sentinel, alive))
                        return;

                    if (TickSentinelPressureDuringControllerAttack())
                        return;

                    bool delayElapsed =
                        Time.time - phaseStartedAt >=
                        Mathf.Max(0f, profile.flankerAttackDelayAfterController);

                    bool flankerReady = IsMoveSuccessfulOrClose(flanker);
                    bool flankerGoodEnough =
                        profile.useFluidCombatMovement &&
                        profile.flankerCanFireBeforePerfectSlot &&
                        HasGoodEnoughFlankAngle(flanker);
                    bool flankerOpportunisticTime =
                        profile.useFluidCombatMovement &&
                        Time.time - phaseStartedAt >=
                        Mathf.Max(0.1f, profile.flankerOpportunisticShotSeconds);
                    bool flankerTimedOut =
                        Time.time - phaseStartedAt >=
                        Mathf.Max(0.2f, profile.flankerFailOpenAttackSeconds);

                    if (!flankerAttackIssued && delayElapsed &&
                        (flankerReady || flankerGoodEnough || flankerOpportunisticTime))
                    {
                        string reason = flankerReady
                            ? "Flanker reached pincer angle"
                            : flankerGoodEnough
                                ? "Flanker moving pressure: good-enough side angle"
                                : "Flanker opportunistic moving shot";

                        bool usedFluid = IssuePressureOrAttack(
                            flanker,
                            EnemyRoleV2.Flanker,
                            flanker.AssignedSlot,
                            reason,
                            preferFluid: profile.useFluidCombatMovement
                        );

                        if (!usedFluid)
                            break;

                        debugFlankerUsedFluidPressure = usedFluid;
                        flankerAttackIssued = true;
                        SetPhase(PlanPhase.FlankerAttack);
                    }
                    else if (!flankerAttackIssued && flankerTimedOut)
                    {
                        bool usedFluid = IssuePressureOrAttack(
                            flanker,
                            EnemyRoleV2.Flanker,
                            flanker.AssignedSlot,
                            "Flanker fail-open shot after long route",
                            preferFluid: profile.useFluidCombatMovement
                        );

                        if (!usedFluid)
                            break;

                        debugFlankerUsedFluidPressure = usedFluid;
                        flankerAttackIssued = true;
                        SetPhase(PlanPhase.FlankerAttack);
                    }
                    break;
                }

                case PlanPhase.FlankerAttack:
                {
                    bool controllerFinished = IsActionFinished(controller);
                    bool flankerFinished = IsActionFinished(flanker);
                    bool sentinelFinished = sentinel == null || IsActionFinished(sentinel);

                    if (profile != null && profile.oneEnemyAttacksAtATime &&
                        profile.oneAtATimeSentinelAttacksAfterFlanker &&
                        sentinel != null && !sentinelAttackIssued &&
                        controllerFinished && flankerFinished)
                    {
                        bool issuedSentinel = IssuePressureOrAttack(
                            sentinel,
                            EnemyRoleV2.Sentinel,
                            sentinel.AssignedSlot,
                            "Sequential Sentinel area denial after Flanker",
                            preferFluid: profile.useFluidCombatMovement && profile.sentinelUsesFluidPressure
                        );

                        if (issuedSentinel)
                        {
                            debugSentinelUsedFluidPressure = true;
                            sentinelAttackIssued = true;
                            debugSentinelPlan = "One-at-a-time mode: Sentinel attacks after Flanker";
                        }
                        else
                        {
                            debugSentinelPlan = "One-at-a-time mode: Sentinel waiting for attack gate";
                        }

                        break;
                    }

                    if (controllerFinished && flankerFinished && sentinelFinished)
                    {
                        IssueRecover(controller, GetRecoverySeconds(EnemyRoleV2.Controller));
                        IssueRecover(flanker, GetRecoverySeconds(EnemyRoleV2.Flanker));

                        if (sentinel != null)
                            IssueRecover(sentinel, GetRecoverySeconds(EnemyRoleV2.Sentinel));

                        SetPhase(PlanPhase.Recovering);
                    }
                    break;
                }

                case PlanPhase.Recovering:
                    if (IsActionFinished(controller) && IsActionFinished(flanker) &&
                        (sentinel == null || IsActionFinished(sentinel)) &&
                        CanCompletePhraseAfterBreather(soloPhrase: false, hasSentinel: sentinel != null))
                    {
                        BeginNewPlan(sentinel != null
                            ? "Pin / pincer / sentinel phrase complete"
                            : "Pin and pincer phrase complete");
                    }
                    break;
            }
        }

        private bool TickSentinelPressureDuringControllerAttack()
        {
            if (sentinel == null || sentinelAttackIssued || profile == null)
                return false;

            if (profile.oneEnemyAttacksAtATime && profile.oneAtATimeSentinelAttacksAfterFlanker)
            {
                debugSentinelPlan = "One-at-a-time mode: Sentinel waits until after Flanker";
                return false;
            }

            bool delayElapsed = Time.time - phaseStartedAt >=
                                Mathf.Max(0f, profile.sentinelAttackDelayAfterController);

            if (!delayElapsed)
                return false;

            if (profile.sentinelReformsWhenLaneInvalid && player != null &&
                !HasLineOfSight(sentinel.transform.position, player.position) &&
                !IsMoveSuccessfulOrClose(sentinel))
            {
                debugSentinelPlan = "Sentinel lane invalid before attack; re-forming";
                BeginNewPlan("Sentinel lane invalid");
                return true;
            }

            bool sentinelReady = IsMoveSuccessfulOrClose(sentinel);
            bool timedPressure = profile.useFluidCombatMovement && profile.sentinelUsesFluidPressure;

            if (!sentinelReady && !timedPressure)
                return false;

            bool usedFluid = IssuePressureOrAttack(
                sentinel,
                EnemyRoleV2.Sentinel,
                sentinel.AssignedSlot,
                sentinelReady
                    ? "Sentinel area denial from claimed lane"
                    : "Sentinel moving area denial",
                preferFluid: profile.useFluidCombatMovement && profile.sentinelUsesFluidPressure
            );

            if (!usedFluid)
            {
                debugSentinelPlan = "Sentinel area denial failed to issue; waiting for fail-open/replan";
                return false;
            }

            debugSentinelUsedFluidPressure = true;
            sentinelAttackIssued = true;
            debugSentinelPlan = "Sentinel fired area denial while holding/moving to lane";

            return false;
        }


        private float TempoAttackCadenceMultiplier()
        {
            if (profile == null || !profile.useCombatTempoScaling)
                return 1f;

            return Mathf.Clamp(profile.attackCadenceTempoMultiplier, 0.75f, 2.25f);
        }

        private float TempoRecoveryMultiplier()
        {
            if (profile == null || !profile.useCombatTempoScaling)
                return 1f;

            return Mathf.Clamp(profile.recoveryTempoMultiplier, 0.75f, 2.25f);
        }

        private float TempoAttackStartGapMultiplier()
        {
            if (profile == null || !profile.useCombatTempoScaling)
                return 1f;

            return Mathf.Clamp(profile.attackStartGapTempoMultiplier, 0.5f, 2.5f);
        }

        private void ApplyTempoToAttackTimings(ref float interval, ref float cooldown, ref float timeout)
        {
            float cadence = TempoAttackCadenceMultiplier();
            interval = Mathf.Max(0.03f, interval * cadence);
            cooldown = Mathf.Max(0.05f, cooldown * cadence);
            timeout = Mathf.Max(0.1f, timeout * Mathf.Max(1f, cadence * 0.85f));
        }

        private void TickAttackConcurrencyDebug(List<EnemyAgentV2> alive)
        {
            if (profile == null)
            {
                debugAttackConcurrencyGate = "Off";
                debugPressurePacing = "Off";
                debugCombatTempo = "Default";
                debugActiveAttacker = null;
                debugAttackGateGapRemaining = 0f;
                debugAttackStartGapRemaining = 0f;
                debugPhraseBreatherRemaining = 0f;
                attackGateWasOccupied = false;
                return;
            }

            float attackStartGap = profile.usePressurePacing
                ? Mathf.Max(0f, profile.minimumSecondsBetweenAttackStarts) * TempoAttackStartGapMultiplier()
                : 0f;
            debugAttackStartGapRemaining = Mathf.Max(0f, attackStartGap - (Time.time - lastAnyAttackStartedAt));
            debugPhraseBreatherRemaining = phraseBreatherUntil > 0f
                ? Mathf.Max(0f, phraseBreatherUntil - Time.time)
                : 0f;

            if (profile.usePressurePacing || profile.usePhraseBreathingRoom)
            {
                if (debugPhraseBreatherRemaining > 0.001f)
                    debugPressurePacing = $"Breather: {debugPhraseBreatherRemaining:0.00}s";
                else if (debugAttackStartGapRemaining > 0.001f)
                    debugPressurePacing = $"Attack-start gap: {debugAttackStartGapRemaining:0.00}s";
                else
                    debugPressurePacing = "Pacing ready";
            }
            else
            {
                debugPressurePacing = "Off";
            }

            debugCombatTempo = profile.useCombatTempoScaling
                ? $"Tempo scale: move x{profile.enemyMovementTempoMultiplier:0.00}, cadence x{profile.attackCadenceTempoMultiplier:0.00}, recovery x{profile.recoveryTempoMultiplier:0.00}"
                : "Default";

            if (!profile.oneEnemyAttacksAtATime)
            {
                debugAttackConcurrencyGate = "Off";
                debugActiveAttacker = null;
                debugAttackGateGapRemaining = 0f;
                attackGateWasOccupied = false;
                return;
            }

            EnemyAgentV2 active;
            bool occupied = TryFindActiveAttackAgent(null, out active);

            if (attackGateWasOccupied && !occupied)
                lastAttackGateReleasedAt = Time.time;

            attackGateWasOccupied = occupied;
            debugActiveAttacker = active;

            float gap = Mathf.Max(0f, profile.oneAtATimeGapAfterAttack);
            debugAttackGateGapRemaining = Mathf.Max(0f, gap - (Time.time - lastAttackGateReleasedAt));

            if (occupied && active != null)
                debugAttackConcurrencyGate = $"One-at-a-time: {active.name} is attacking";
            else if (debugAttackGateGapRemaining > 0.001f)
                debugAttackConcurrencyGate = $"One-at-a-time: handoff gap {debugAttackGateGapRemaining:0.00}s";
            else
                debugAttackConcurrencyGate = "One-at-a-time: gate open";
        }

        private bool IsAttackStartBlockedByConcurrency(EnemyAgentV2 requester, out string reason)
        {
            reason = null;

            if (profile == null)
                return false;

            if (profile.usePressurePacing)
            {
                float attackStartGap = Mathf.Max(0f, profile.minimumSecondsBetweenAttackStarts) * TempoAttackStartGapMultiplier();
                float attackStartRemaining = attackStartGap - (Time.time - lastAnyAttackStartedAt);
                if (attackStartRemaining > 0f)
                {
                    reason = $"Pressure pacing: attack-start gap {attackStartRemaining:0.00}s";
                    debugPressurePacing = reason;
                    debugAttackStartGapRemaining = attackStartRemaining;
                    return true;
                }
            }

            if (!profile.oneEnemyAttacksAtATime)
                return false;

            EnemyAgentV2 active;
            if (TryFindActiveAttackAgent(requester, out active))
            {
                reason = active != null
                    ? $"One-at-a-time gate blocked by {active.name}"
                    : "One-at-a-time gate blocked by active attacker";
                debugAttackConcurrencyGate = reason;
                debugActiveAttacker = active;
                return true;
            }

            float handoffGap = Mathf.Max(0f, profile.oneAtATimeGapAfterAttack);
            float handoffRemaining = handoffGap - (Time.time - lastAttackGateReleasedAt);
            if (handoffRemaining > 0f)
            {
                reason = $"One-at-a-time handoff gap {handoffRemaining:0.00}s";
                debugAttackConcurrencyGate = reason;
                debugAttackGateGapRemaining = handoffRemaining;
                return true;
            }

            debugAttackConcurrencyGate = requester != null
                ? $"One-at-a-time gate open for {requester.name}"
                : "One-at-a-time gate open";
            debugActiveAttacker = null;
            debugAttackGateGapRemaining = 0f;
            return false;
        }

        private bool TryFindActiveAttackAgent(EnemyAgentV2 requester, out EnemyAgentV2 active)
        {
            active = null;

            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 candidate = agents[i];

                if (candidate == null || candidate == requester || !candidate.IsAlive || candidate.ActionRunner == null)
                    continue;

                if (!candidate.ActionRunner.IsBusy)
                    continue;

                EnemyActionKindV2 kind = candidate.ActionRunner.CurrentKind;
                bool countsAsAttack = kind == EnemyActionKindV2.AttackPattern ||
                                      kind == EnemyActionKindV2.CastSkill ||
                                      (profile != null && profile.oneAtATimeCountsFluidPressureAsAttack &&
                                       kind == EnemyActionKindV2.FluidPressure);

                if (!countsAsAttack)
                    continue;

                active = candidate;
                return true;
            }

            return false;
        }

        private bool TickStage2Watchdogs(List<EnemyAgentV2> alive)
        {
            TickStage2DebugOnly(alive);

            if (profile == null || alive.Count < 2)
                return false;

            if (debugClosePairs > 0)
            {
                if (clumpStartedAt < 0f)
                    clumpStartedAt = Time.time;

                debugClumpAge = Time.time - clumpStartedAt;
                debugStage2Watchdog = $"Clump watch: {debugClosePairs} close pair(s), {debugClumpAge:0.00}s";

                bool canReform = Time.time - lastClumpReformAt >=
                                 Mathf.Max(0.1f, profile.clumpReformCooldown);

                if (canReform && debugClumpAge >= Mathf.Max(0.1f, profile.clumpWatchSeconds))
                {
                    lastClumpReformAt = Time.time;
                    BeginNewPlan("Stage 2 clump watchdog re-form");

                    if (profile.logStage2Watchdogs)
                        Debug.Log($"[Enemy AI V2] Clump watchdog forced re-form ({debugClosePairs} pairs).", this);

                    return true;
                }
            }
            else
            {
                clumpStartedAt = -1f;
                debugClumpAge = 0f;
                debugStage2Watchdog = "No clump";
            }

            return false;
        }

        private void TickStage2DebugOnly(List<EnemyAgentV2> alive)
        {
            debugClosePairs = 0;
            debugClosestEnemyDistance = float.PositiveInfinity;

            if (profile == null || alive == null || alive.Count < 2)
            {
                debugClosestEnemyDistance = 0f;
                return;
            }

            float clumpRadius = Mathf.Max(0.05f, profile.clumpWatchRadius);

            for (int i = 0; i < alive.Count; i++)
            {
                for (int j = i + 1; j < alive.Count; j++)
                {
                    float distance = Vector2.Distance(
                        alive[i].transform.position,
                        alive[j].transform.position
                    );

                    if (distance < debugClosestEnemyDistance)
                        debugClosestEnemyDistance = distance;

                    if (distance <= clumpRadius)
                        debugClosePairs++;
                }
            }

            if (float.IsInfinity(debugClosestEnemyDistance))
                debugClosestEnemyDistance = 0f;
        }

        private bool HandleMovementFailure(EnemyAgentV2 agent, EnemyRoleV2 role, List<EnemyAgentV2> alive)
        {
            if (agent == null || agent.ActionRunner == null || !IsActionFailed(agent))
                return false;

            if (RecentlyRespondedToFailure(agent))
                return false;

            RememberFailureResponse(agent);

            if (profile.retryAlternateSlotBeforeFailOpenAttack &&
                TryAssignAlternateSlot(agent, role, alive))
            {
                debugLastFailureResponse = $"{agent.name}: alternate {role} slot";
                return true;
            }

            if (profile.failOpenWhenMovementFails && TryFailOpenAttack(agent, role))
            {
                debugLastFailureResponse = $"{agent.name}: fail-open {role} attack";
                return true;
            }

            BeginNewPlan($"Movement failure could not recover: {agent.name}");
            debugLastFailureResponse = $"{agent.name}: forced full replan";
            return true;
        }

        private bool RecentlyRespondedToFailure(EnemyAgentV2 agent)
        {
            if (agent == null || !lastFailureResponseTime.TryGetValue(agent, out float lastTime))
                return false;

            return Time.time - lastTime < Mathf.Max(0.05f, profile.failedActionResponseCooldown);
        }

        private void RememberFailureResponse(EnemyAgentV2 agent)
        {
            if (agent == null)
                return;

            lastFailureResponseTime[agent] = Time.time;
        }

        private bool TryAssignAlternateSlot(EnemyAgentV2 agent, EnemyRoleV2 role, List<EnemyAgentV2> alive)
        {
            if (agent == null || player == null)
                return false;

            Vector2 slot;

            if (role == EnemyRoleV2.Flanker)
            {
                Vector2 controllerSlot = controller != null
                    ? controller.AssignedSlot
                    : FindBestControllerSlot(agent, alive);

                slot = FindBestFlankerSlot(agent, controllerSlot, alive, avoidRecentFailure: true);
            }
            else if (role == EnemyRoleV2.SoloDuelist)
            {
                slot = FindBestSoloSlot(agent);
            }
            else if (role == EnemyRoleV2.Sentinel)
            {
                Vector2 controllerSlot = controller != null
                    ? controller.AssignedSlot
                    : FindBestControllerSlot(agent, alive);

                Vector2 flankerSlot = flanker != null
                    ? flanker.AssignedSlot
                    : player.position;

                slot = FindBestSentinelSlot(agent, controllerSlot, flankerSlot, alive);
            }
            else
            {
                slot = FindBestControllerSlot(agent, alive, avoidRecentFailure: true);
            }

            if (agent.Locomotion != null && agent.Locomotion.IsDestinationRecentlyFailed(slot, 0.45f))
                return false;

            agent.AssignRoleAndSlot(role, GetSectorForPosition(slot), slot, $"Stage 2 alternate {role} slot");

            bool issued = IssueMove(agent, slot, $"Stage 2 alternate {role} slot");

            if (issued)
            {
                if (role == EnemyRoleV2.Controller)
                    debugControllerSlot = slot;
                else if (role == EnemyRoleV2.Flanker)
                    debugFlankerSlot = slot;
                else if (role == EnemyRoleV2.Sentinel)
                    debugSentinelSlot = slot;
            }

            return issued;
        }

        private bool TryFailOpenAttack(EnemyAgentV2 agent, EnemyRoleV2 role)
        {
            if (agent == null || player == null)
                return false;

            float distance = Vector2.Distance(agent.transform.position, player.position);
            bool allowed = distance <= Mathf.Max(0.5f, profile.failOpenAttackDistance) ||
                           HasLineOfSight(agent.transform.position, player.position);

            if (!allowed)
                return false;

            return IssuePressureOrAttack(
                agent,
                role,
                agent.AssignedSlot,
                $"Stage 2 fail-open {role} attack",
                preferFluid: profile != null && profile.useFluidCombatMovement
            );
        }

        private void BeginSoloPlan(EnemyAgentV2 solo)
        {
            CancelAllOrders("Begin solo plan");
            controller = solo;
            flanker = null;
            sentinel = null;
            currentTactic = SquadTacticV2.SoloDuel;
            planStartedAt = Time.time;
            controllerAttackIssued = false;
            flankerAttackIssued = false;
            sentinelAttackIssued = false;

            Vector2 slot = FindBestSoloSlot(solo);
            solo.AssignRoleAndSlot(
                EnemyRoleV2.SoloDuelist,
                GetSectorForPosition(slot),
                slot,
                "Solo Duel"
            );

            if (profile.useFluidCombatMovement && profile.soloUsesSkirmishPressure)
            {
                debugFluidDecision = "Solo skirmish: moving pressure immediately";
                IssuePressureOrAttack(
                    solo,
                    EnemyRoleV2.SoloDuelist,
                    slot,
                    "Solo skirmish pressure",
                    preferFluid: true
                );
                SetPhase(PlanPhase.ControllerAttack);
            }
            else
            {
                IssueMove(solo, slot, "Solo preferred range");
                SetPhase(PlanPhase.Forming);
            }

            SyncDebugRefs();
            LogPlan("Solo Duel");
        }

        private void BeginPinAndPincerPlan(List<EnemyAgentV2> alive)
        {
            CancelAllOrders("Begin pin and pincer");

            SelectControllerAndFlanker(alive, out controller, out flanker);
            sentinel = ShouldUseSentinel(alive)
                ? SelectSentinel(alive, controller, flanker)
                : null;

            if (controller == null || flanker == null)
            {
                debugPlan = "Could not find Controller + Flanker candidates";
                currentTactic = SquadTacticV2.ReForm;
                return;
            }

            currentTactic = sentinel != null
                ? SquadTacticV2.PinPincerSentinel
                : SquadTacticV2.PinAndPincer;
            planStartedAt = Time.time;
            controllerAttackIssued = false;
            flankerAttackIssued = false;
            sentinelAttackIssued = false;

            FindPinAndPincerSlots(
                controller,
                flanker,
                alive,
                out Vector2 controllerSlot,
                out Vector2 flankerSlot
            );

            controller.AssignRoleAndSlot(
                EnemyRoleV2.Controller,
                GetSectorForPosition(controllerSlot),
                controllerSlot,
                "Pin lane"
            );

            flanker.AssignRoleAndSlot(
                EnemyRoleV2.Flanker,
                GetSectorForPosition(flankerSlot),
                flankerSlot,
                "Pincer route"
            );

            IssueMove(controller, controllerSlot, "Controller claims firing lane");
            IssueMove(flanker, flankerSlot, "Flanker rotates to unique angle");

            debugControllerSlot = controllerSlot;
            debugFlankerSlot = flankerSlot;

            if (sentinel != null)
            {
                Vector2 sentinelSlot = FindBestSentinelSlot(sentinel, controllerSlot, flankerSlot, alive);

                sentinel.AssignRoleAndSlot(
                    EnemyRoleV2.Sentinel,
                    GetSectorForPosition(sentinelSlot),
                    sentinelSlot,
                    "Sentinel controls lane"
                );

                IssueMove(sentinel, sentinelSlot, "Sentinel claims area-denial lane");
                debugSentinelSlot = sentinelSlot;
                debugSentinelPlan = $"Sentinel assigned: {sentinel.name} controls lane from {sentinelSlot}";
            }
            else
            {
                debugSentinelSlot = Vector2.zero;
                debugSentinelPlan = "No Sentinel: fewer than required enemies or no candidate";
            }

            SetPhase(PlanPhase.Forming);
            SyncDebugRefs();
            LogPlan(sentinel != null ? "Pin / Pincer / Sentinel" : "Pin and Pincer");
        }

        private void BuildObservedAssignments(List<EnemyAgentV2> alive)
        {
            if (alive.Count == 1)
            {
                Vector2 slot = FindBestSoloSlot(alive[0]);
                alive[0].AssignRoleAndSlot(
                    EnemyRoleV2.SoloDuelist,
                    GetSectorForPosition(slot),
                    slot,
                    "Observe: Solo Duel"
                );
                debugPlan = "Observe: Solo Duel";
                return;
            }

            SelectControllerAndFlanker(alive, out EnemyAgentV2 observedController, out EnemyAgentV2 observedFlanker);

            if (observedController == null || observedFlanker == null)
                return;

            FindPinAndPincerSlots(
                observedController,
                observedFlanker,
                alive,
                out Vector2 controllerSlot,
                out Vector2 flankerSlot
            );

            observedController.AssignRoleAndSlot(
                EnemyRoleV2.Controller,
                GetSectorForPosition(controllerSlot),
                controllerSlot,
                "Observe: Controller"
            );

            observedFlanker.AssignRoleAndSlot(
                EnemyRoleV2.Flanker,
                GetSectorForPosition(flankerSlot),
                flankerSlot,
                "Observe: Flanker"
            );

            EnemyAgentV2 observedSentinel = ShouldUseSentinel(alive)
                ? SelectSentinel(alive, observedController, observedFlanker)
                : null;

            if (observedSentinel != null)
            {
                Vector2 sentinelSlot = FindBestSentinelSlot(
                    observedSentinel,
                    controllerSlot,
                    flankerSlot,
                    alive
                );

                observedSentinel.AssignRoleAndSlot(
                    EnemyRoleV2.Sentinel,
                    GetSectorForPosition(sentinelSlot),
                    sentinelSlot,
                    "Observe: Sentinel"
                );

                debugSentinel = observedSentinel;
                debugSentinelSlot = sentinelSlot;
                debugPlan = "Observe: Pin / Pincer / Sentinel";
            }
            else
            {
                debugSentinel = null;
                debugSentinelSlot = Vector2.zero;
                debugPlan = "Observe: Pin and Pincer";
            }

            debugController = observedController;
            debugFlanker = observedFlanker;
            debugControllerSlot = controllerSlot;
            debugFlankerSlot = flankerSlot;
        }

        private void SelectControllerAndFlanker(
            List<EnemyAgentV2> alive,
            out EnemyAgentV2 selectedController,
            out EnemyAgentV2 selectedFlanker)
        {
            selectedController = null;
            selectedFlanker = null;
            float bestControllerScore = float.NegativeInfinity;

            for (int i = 0; i < alive.Count; i++)
            {
                EnemyAgentV2 candidate = alive[i];

                if (!candidate.IsRanged || !candidate.EligibleForController)
                    continue;

                float distance = Vector2.Distance(candidate.transform.position, player.position);
                float score = -distance * 0.15f;

                if (HasLineOfSight(candidate.transform.position, player.position))
                    score += 2f;

                if (candidate.ActionRunner != null &&
                    (candidate.ActionRunner.CurrentKind ==
                         EnemyActionKindV2.AttackPattern ||
                     candidate.ActionRunner.CurrentKind ==
                         EnemyActionKindV2.CastSkill))
                    score += 0.35f;

                if (score > bestControllerScore)
                {
                    bestControllerScore = score;
                    selectedController = candidate;
                }
            }

            float bestFlankerScore = float.NegativeInfinity;

            for (int i = 0; i < alive.Count; i++)
            {
                EnemyAgentV2 candidate = alive[i];

                if (candidate == selectedController ||
                    !candidate.IsRanged ||
                    !candidate.EligibleForFlanker)
                {
                    continue;
                }

                float pathCost = EstimatePathCost(candidate.transform.position, player.position);
                float score = float.IsInfinity(pathCost) ? -1000f : -pathCost * 0.05f;

                if (!HasLineOfSight(candidate.transform.position, player.position))
                    score += 0.5f;

                if (score > bestFlankerScore)
                {
                    bestFlankerScore = score;
                    selectedFlanker = candidate;
                }
            }

            if (selectedFlanker == null)
            {
                for (int i = 0; i < alive.Count; i++)
                {
                    if (alive[i] != selectedController)
                    {
                        selectedFlanker = alive[i];
                        break;
                    }
                }
            }
        }

        private bool ShouldUseSentinel(List<EnemyAgentV2> alive)
        {
            if (profile == null || !profile.useSentinelRole || alive == null)
                return false;

            return alive.Count >= Mathf.Max(3, profile.minimumAgentsForSentinel);
        }

        private EnemyAgentV2 SelectSentinel(
            List<EnemyAgentV2> alive,
            EnemyAgentV2 selectedController,
            EnemyAgentV2 selectedFlanker)
        {
            if (!ShouldUseSentinel(alive))
                return null;

            EnemyAgentV2 selected = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < alive.Count; i++)
            {
                EnemyAgentV2 candidate = alive[i];

                if (candidate == null ||
                    candidate == selectedController ||
                    candidate == selectedFlanker ||
                    !candidate.IsRanged ||
                    !candidate.EligibleForSentinel)
                {
                    continue;
                }

                float distance = player != null
                    ? Vector2.Distance(candidate.transform.position, player.position)
                    : 0f;

                float pathCost = player != null
                    ? EstimatePathCost(candidate.transform.position, player.position)
                    : distance;

                float score = float.IsInfinity(pathCost) ? -1000f : -pathCost * 0.035f;

                // A Sentinel is best when it is not already the closest threat.
                // It should feel like it is claiming an angle/region, not joining
                // the same front line as the Controller.
                score += Mathf.Clamp(distance - 2.0f, 0f, 3.0f) * 0.18f;

                if (HasLineOfSight(candidate.transform.position, player.position))
                    score += 0.35f;

                if (score > bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                }
            }

            return selected;
        }

        private void FindPinAndPincerSlots(
            EnemyAgentV2 controllerAgent,
            EnemyAgentV2 flankerAgent,
            List<EnemyAgentV2> alive,
            out Vector2 controllerSlot,
            out Vector2 flankerSlot)
        {
            controllerSlot = FindBestControllerSlot(controllerAgent, alive);
            flankerSlot = FindBestFlankerSlot(
                flankerAgent,
                controllerSlot,
                alive,
                avoidRecentFailure: false
            );
        }

        private Vector2 FindBestControllerSlot(EnemyAgentV2 agent, List<EnemyAgentV2> alive, bool avoidRecentFailure = false)
        {
            Vector2 playerPosition = player.position;
            Vector2 best = SnapToNavigation(agent.transform.position);
            float bestScore = float.NegativeInfinity;
            int count = Mathf.Max(8, profile.formationCandidateCount);

            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;
                Vector2 direction = DirectionFromAngle(angle);
                Vector2 candidate = playerPosition + direction * profile.controllerRadius;
                candidate = SnapToNavigation(candidate);

                if (avoidRecentFailure && agent.Locomotion != null && agent.Locomotion.IsDestinationRecentlyFailed(candidate, 0.5f))
                    continue;

                float score = ScoreCandidate(agent, candidate, null, alive);

                if (HasLineOfSight(candidate, playerPosition))
                    score += profile.candidateLosBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private Vector2 FindBestFlankerSlot(
            EnemyAgentV2 agent,
            Vector2 controllerSlot,
            List<EnemyAgentV2> alive,
            bool avoidRecentFailure)
        {
            Vector2 playerPosition = player.position;
            Vector2 controllerDirection =
                (controllerSlot - playerPosition).sqrMagnitude > 0.0001f
                    ? (controllerSlot - playerPosition).normalized
                    : Vector2.up;

            Vector2 best = SnapToNavigation(agent.transform.position);
            float bestScore = float.NegativeInfinity;
            int count = Mathf.Max(8, profile.formationCandidateCount);

            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;
                Vector2 direction = DirectionFromAngle(angle);
                Vector2 candidate = playerPosition + direction * profile.flankerRadius;
                candidate = SnapToNavigation(candidate);

                if (avoidRecentFailure && agent.Locomotion != null && agent.Locomotion.IsDestinationRecentlyFailed(candidate, 0.5f))
                    continue;

                float angleSeparation = Vector2.Angle(controllerDirection, direction);

                if (angleSeparation < profile.minimumFlankAngleDegrees)
                    continue;

                float angleFit = 1f - Mathf.Abs(
                    angleSeparation - profile.idealFlankAngleDegrees
                ) / 180f;

                float score = ScoreCandidate(agent, candidate, controllerSlot, alive);
                score += angleFit * profile.candidateAngleWeight;

                if (HasLineOfSight(candidate, playerPosition))
                    score += profile.candidateLosBonus * 0.75f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (float.IsNegativeInfinity(bestScore))
            {
                Vector2 opposite = playerPosition - controllerDirection * profile.flankerRadius;
                best = SnapToNavigation(opposite);
            }

            return best;
        }

        private Vector2 FindBestSentinelSlot(
            EnemyAgentV2 agent,
            Vector2 controllerSlot,
            Vector2 flankerSlot,
            List<EnemyAgentV2> alive)
        {
            Vector2 playerPosition = player.position;
            Vector2 best = SnapToNavigation(agent.transform.position);
            float bestScore = float.NegativeInfinity;
            int bestCoverage = 0;
            int count = Mathf.Max(8, profile.formationCandidateCount);

            Vector2 controllerDirection = (controllerSlot - playerPosition).sqrMagnitude > 0.0001f
                ? (controllerSlot - playerPosition).normalized
                : Vector2.up;

            Vector2 flankerDirection = (flankerSlot - playerPosition).sqrMagnitude > 0.0001f
                ? (flankerSlot - playerPosition).normalized
                : -controllerDirection;

            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;
                Vector2 direction = DirectionFromAngle(angle);
                Vector2 candidate = playerPosition + direction * profile.sentinelRadius;
                candidate = SnapToNavigation(candidate);

                if (agent.Locomotion != null && agent.Locomotion.IsDestinationRecentlyFailed(candidate, 0.5f))
                    continue;

                float controllerSeparation = Vector2.Distance(candidate, controllerSlot);
                float flankerSeparation = Vector2.Distance(candidate, flankerSlot);

                if (controllerSeparation < profile.hardDestinationSeparation ||
                    flankerSeparation < profile.hardDestinationSeparation)
                {
                    continue;
                }

                float score = ScoreCandidate(agent, candidate, controllerSlot, alive);

                if (score <= -9999f)
                    continue;

                float angleFromController = Vector2.Angle(controllerDirection, direction);
                float angleFromFlanker = Vector2.Angle(flankerDirection, direction);
                float angleScore = Mathf.Min(angleFromController, angleFromFlanker) / 180f;
                score += angleScore * Mathf.Max(0f, profile.sentinelAngleSeparationWeight);

                int coverage = profile.sentinelScoresLaneCoverage
                    ? CountSentinelLaneCoverage(candidate, playerPosition)
                    : 0;

                score += coverage * Mathf.Max(0f, profile.sentinelLaneCoverageBonus);

                if (HasLineOfSight(candidate, playerPosition))
                    score += profile.candidateLosBonus * 0.55f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestCoverage = coverage;
                }
            }

            if (float.IsNegativeInfinity(bestScore))
            {
                Vector2 blended = -(controllerDirection + flankerDirection);
                if (blended.sqrMagnitude <= 0.0001f)
                    blended = Vector2.Perpendicular(controllerDirection);

                best = SnapToNavigation(playerPosition + blended.normalized * profile.sentinelRadius);
                bestScore = 0f;
                bestCoverage = CountSentinelLaneCoverage(best, playerPosition);
            }

            debugSentinelLaneScore = bestScore;
            debugSentinelLaneCoverage = bestCoverage;
            debugSentinelPlan = $"Sentinel lane score {bestScore:0.00}, coverage {bestCoverage}";
            return best;
        }

        private int CountSentinelLaneCoverage(Vector2 sentinelCandidate, Vector2 playerPosition)
        {
            if (profile == null || !profile.sentinelScoresLaneCoverage)
                return 0;

            int count = Mathf.Clamp(profile.sentinelLaneProbeCount, 4, 16);
            float radius = Mathf.Max(0.3f, profile.sentinelLaneProbeRadius);
            int visible = 0;

            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;
                Vector2 probe = playerPosition + DirectionFromAngle(angle) * radius;
                probe = SnapToNavigation(probe);

                if (HasLineOfSight(sentinelCandidate, probe))
                    visible++;
            }

            return visible;
        }

        private Vector2 FindBestSoloSlot(EnemyAgentV2 agent)
        {
            Vector2 playerPosition = player.position;
            Vector2 fromPlayer =
                (Vector2)agent.transform.position - playerPosition;

            if (fromPlayer.sqrMagnitude <= 0.0001f)
                fromPlayer = Vector2.up;

            Vector2 candidate =
                playerPosition +
                fromPlayer.normalized *
                profile.soloPreferredRange;

            return SnapToNavigation(candidate);
        }

        private float ScoreCandidate(
            EnemyAgentV2 agent,
            Vector2 candidate,
            Vector2? otherSlot,
            List<EnemyAgentV2> alive)
        {
            float pathCost = EstimatePathCost(agent.transform.position, candidate);

            if (float.IsInfinity(pathCost))
                return -10000f;

            float score = -pathCost * profile.candidatePathCostWeight;

            if (otherSlot.HasValue)
            {
                float separation = Vector2.Distance(candidate, otherSlot.Value);

                if (separation < profile.hardDestinationSeparation)
                {
                    debugDestinationLeaseState = "Rejected: too close to paired slot";
                    return -10000f;
                }

                score += Mathf.Min(2f, separation) * profile.candidateSeparationWeight;
            }

            if (profile.useDestinationLeases && alive != null)
            {
                float leaseRadius = Mathf.Max(profile.hardDestinationSeparation, profile.destinationLeaseRadius);

                for (int i = 0; i < alive.Count; i++)
                {
                    EnemyAgentV2 other = alive[i];

                    if (other == null || other == agent || !other.IsAlive)
                        continue;

                    Vector2 leasePosition = other.CurrentRole == EnemyRoleV2.Unassigned
                        ? (Vector2)other.transform.position
                        : other.AssignedSlot;

                    float separation = Vector2.Distance(candidate, leasePosition);

                    if (separation < leaseRadius)
                    {
                        debugDestinationLeaseState = $"Rejected: {agent.name} candidate near {other.name}'s lease";
                        return -10000f;
                    }

                    score += Mathf.Min(2f, separation) * 0.12f;
                }
            }

            debugDestinationLeaseState = "Candidate leases valid";
            return score;
        }

        private bool IssueMove(EnemyAgentV2 agent, Vector2 target, string reason)
        {
            if (agent == null || agent.ActionRunner == null)
                return false;

            return agent.ActionRunner.AssignOrder(new EnemyActionOrderV2
            {
                orderId = nextOrderId++,
                kind = EnemyActionKindV2.MoveToSlot,
                targetPosition = target,
                arrivalRadius = profile.tacticalSlotArrivalRadius,
                timeoutSeconds = profile.formationMaximumSeconds + 0.8f,
                reason = reason
            });
        }


        private bool IssuePressureOrAttack(
            EnemyAgentV2 agent,
            EnemyRoleV2 role,
            Vector2 movementTarget,
            string reason,
            bool preferFluid)
        {
            string gateReason;
            if (IsAttackStartBlockedByConcurrency(agent, out gateReason))
            {
                debugLastAttackSelection = gateReason;
                return false;
            }

            if (preferFluid && profile != null && profile.useFluidCombatMovement)
                return IssueFluidPressure(agent, role, movementTarget, reason);

            return IssueAttack(agent, role, reason);
        }

        private bool IssueFluidPressure(
            EnemyAgentV2 agent,
            EnemyRoleV2 role,
            Vector2 movementTarget,
            string reason)
        {
            if (agent == null || agent.ActionRunner == null)
                return false;

            if (profile != null && profile.skillsMayReplaceFluidPressure &&
                TryIssueSkill(agent, role, reason + " (skill candidate)"))
            {
                return true;
            }

            string legacyPattern;
            int shots;
            float interval;
            float cooldown;
            float timeout;

            if (role == EnemyRoleV2.Flanker)
            {
                legacyPattern = profile.flankerPattern;
                shots = profile.flankerShotsPerBurst;
                interval = profile.flankerIntraBurstInterval;
                cooldown = profile.flankerBurstCooldown;
                timeout = profile.flankerAttackTimeout;
            }
            else if (role == EnemyRoleV2.SoloDuelist)
            {
                legacyPattern = profile.forceSoloAimedPattern
                    ? profile.soloAimedPattern
                    : profile.soloPattern;
                shots = profile.soloShotsPerBurst;
                interval = profile.soloIntraBurstInterval;
                cooldown = profile.soloBurstCooldown;
                timeout = profile.controllerAttackTimeout;
            }
            else
            {
                legacyPattern = profile.controllerPattern;
                shots = profile.controllerShotsPerBurst;
                interval = profile.controllerIntraBurstInterval;
                cooldown = profile.controllerBurstCooldown;
                timeout = profile.controllerAttackTimeout;
            }

            AttackSelection selection = ChooseAttackPattern(agent, role, legacyPattern, reason);
            string pattern = string.IsNullOrWhiteSpace(selection.patternName)
                ? ResolveSupportedPatternAlias(legacyPattern)
                : selection.patternName;

            ApplyAttackSelectionOverrides(
                selection,
                ref shots,
                ref interval,
                ref cooldown
            );
            ApplyTempoToAttackTimings(ref interval, ref cooldown, ref timeout);

            bool issued = agent.ActionRunner.AssignOrder(new EnemyActionOrderV2
            {
                orderId = nextOrderId++,
                kind = EnemyActionKindV2.FluidPressure,
                targetPosition = movementTarget,
                arrivalRadius = profile.tacticalSlotArrivalRadius,
                timeoutSeconds = timeout,
                patternName = pattern,
                shotsPerBurst = shots,
                intraBurstInterval = interval,
                burstCooldown = cooldown,
                resetShooterAimSamples = profile.resetShooterAimSamplesOnV2Attack,
                aimLagSeconds = profile.v2AttackAimLagSeconds,
                overridePatternShape = selection.overridePatternShape,
                fanBullets = selection.fanBullets,
                fanArcDegrees = selection.fanArcDegrees,
                ringBullets = selection.ringBullets,
                angularSpeedDegPerTick = selection.angularSpeedDegPerTick,
                reason = selection.debugReason + " + moving pressure"
            });

            if (issued)
            {
                RecordLegacyAttackForSkillCadence();
                lastAttackPatternByAgent[agent] = pattern;
                lastAttackPatternByRole[role] = pattern;
                lastAnyAttackStartedAt = Time.time;
                debugLastAttackSelection = selection.debugReason + " + moving pressure";
                debugFluidDecision = $"{agent.name}: {role} fluid pressure toward {movementTarget}";

                if (role == EnemyRoleV2.Controller)
                    debugControllerAttackSelection = debugLastAttackSelection;
                else if (role == EnemyRoleV2.Flanker)
                    debugFlankerAttackSelection = debugLastAttackSelection;
                else if (role == EnemyRoleV2.Sentinel)
                    debugSentinelAttackSelection = debugLastAttackSelection;
                else if (role == EnemyRoleV2.SoloDuelist)
                    debugSoloAttackSelection = debugLastAttackSelection;

                if (profile.logFluidCombatMovement)
                    Debug.Log($"[Enemy AI V2] {debugFluidDecision}", this);
            }

            return issued;
        }

        private bool IssueAttack(EnemyAgentV2 agent, EnemyRoleV2 role, string reason)
        {
            if (agent == null || agent.ActionRunner == null)
                return false;

            if (TryIssueSkill(agent, role, reason + " (skill candidate)"))
                return true;

            string legacyPattern;
            int shots;
            float interval;
            float cooldown;
            float timeout;

            if (role == EnemyRoleV2.Flanker)
            {
                legacyPattern = profile.flankerPattern;
                shots = profile.flankerShotsPerBurst;
                interval = profile.flankerIntraBurstInterval;
                cooldown = profile.flankerBurstCooldown;
                timeout = profile.flankerAttackTimeout;
            }
            else if (role == EnemyRoleV2.SoloDuelist)
            {
                legacyPattern = profile.forceSoloAimedPattern
                    ? profile.soloAimedPattern
                    : profile.soloPattern;
                shots = profile.soloShotsPerBurst;
                interval = profile.soloIntraBurstInterval;
                cooldown = profile.soloBurstCooldown;
                timeout = profile.controllerAttackTimeout;
            }
            else
            {
                legacyPattern = profile.controllerPattern;
                shots = profile.controllerShotsPerBurst;
                interval = profile.controllerIntraBurstInterval;
                cooldown = profile.controllerBurstCooldown;
                timeout = profile.controllerAttackTimeout;
            }

            AttackSelection selection = ChooseAttackPattern(agent, role, legacyPattern, reason);
            string pattern = string.IsNullOrWhiteSpace(selection.patternName)
                ? ResolveSupportedPatternAlias(legacyPattern)
                : selection.patternName;

            ApplyAttackSelectionOverrides(
                selection,
                ref shots,
                ref interval,
                ref cooldown
            );
            ApplyTempoToAttackTimings(ref interval, ref cooldown, ref timeout);

            bool issued = agent.ActionRunner.AssignOrder(new EnemyActionOrderV2
            {
                orderId = nextOrderId++,
                kind = EnemyActionKindV2.AttackPattern,
                timeoutSeconds = timeout,
                patternName = pattern,
                shotsPerBurst = shots,
                intraBurstInterval = interval,
                burstCooldown = cooldown,
                resetShooterAimSamples = profile.resetShooterAimSamplesOnV2Attack,
                aimLagSeconds = profile.v2AttackAimLagSeconds,
                overridePatternShape = selection.overridePatternShape,
                fanBullets = selection.fanBullets,
                fanArcDegrees = selection.fanArcDegrees,
                ringBullets = selection.ringBullets,
                angularSpeedDegPerTick = selection.angularSpeedDegPerTick,
                reason = selection.debugReason
            });

            if (issued)
            {
                RecordLegacyAttackForSkillCadence();
                lastAttackPatternByAgent[agent] = pattern;
                lastAttackPatternByRole[role] = pattern;
                lastAnyAttackStartedAt = Time.time;
                debugLastAttackSelection = selection.debugReason;

                if (role == EnemyRoleV2.Controller)
                    debugControllerAttackSelection = selection.debugReason;
                else if (role == EnemyRoleV2.Flanker)
                    debugFlankerAttackSelection = selection.debugReason;
                else if (role == EnemyRoleV2.Sentinel)
                    debugSentinelAttackSelection = selection.debugReason;
                else if (role == EnemyRoleV2.SoloDuelist)
                    debugSoloAttackSelection = selection.debugReason;
            }

            return issued;
        }

        private bool TryIssueSkill(
            EnemyAgentV2 agent,
            EnemyRoleV2 role,
            string reason)
        {
            if (profile == null || !profile.enableSkillActions ||
                agent == null || agent.ActionRunner == null ||
                agent.PlayerTarget == null)
            {
                return false;
            }

            float skillGap = Mathf.Max(
                0f,
                profile.minimumSecondsBetweenSkillStarts);
            float skillGapRemaining =
                skillGap - (Time.time - lastSkillStartedAt);
            debugSkillStartGapRemaining = Mathf.Max(0f, skillGapRemaining);
            debugLegacyAttacksSinceSkill = legacyAttacksSinceLastSkill;
            debugConsecutiveSkillActions = consecutiveSkillActions;
            if (skillGapRemaining > 0f)
            {
                debugSkillCadence =
                    $"Skill-start gap: {skillGapRemaining:0.00}s";
                return false;
            }

            bool hasUsedSkill = lastSkillStartedAt > -900f;
            int requiredLegacyAttacks = Mathf.Max(
                0,
                profile.minimumLegacyAttacksBetweenSkills);
            if (hasUsedSkill &&
                legacyAttacksSinceLastSkill < requiredLegacyAttacks)
            {
                debugSkillCadence =
                    $"Waiting for legacy attack " +
                    $"{legacyAttacksSinceLastSkill}/{requiredLegacyAttacks}";
                return false;
            }

            int maximumConsecutive = Mathf.Max(
                1,
                profile.maximumConsecutiveSkillActions);
            if (consecutiveSkillActions >= maximumConsecutive)
            {
                debugSkillCadence =
                    $"Consecutive skill cap {maximumConsecutive}; " +
                    "legacy attack required";
                return false;
            }

            EnemySpellAIDecisionSupportV2 decisionSupport =
                agent.GetComponent<EnemySpellAIDecisionSupportV2>();
            EnemySkillExecutorV2 executor =
                agent.GetComponent<EnemySkillExecutorV2>();
            if (decisionSupport == null || executor == null)
            {
                debugLastAttackSelection =
                    $"{agent.name}: skill actions enabled but Skill AI components are missing";
                return false;
            }

            EnemyHealth health = agent.GetComponent<EnemyHealth>();
            float casterHealth = health != null
                ? health.CurrentHP / (float)Mathf.Max(1, health.MaxHP)
                : 1f;
            GameObject target = agent.PlayerTarget.gameObject;
            Vector2 targetPoint = agent.PlayerTarget.position;

            bool chose = decisionSupport.TryChooseSkill(
                target,
                targetPoint,
                usefulTargetCount: 1,
                casterHealthFraction: casterHealth,
                targetHealthFraction: 1f,
                incomingDanger: 0f,
                commitmentCost: 0f,
                activeComboTags: null,
                out ProjectEri.SkillSystemV2.SpellDefinition spell,
                out ProjectEri.SkillSystemV2.CastContext cast,
                out float score);
            if (!chose || spell == null ||
                score < Mathf.Max(0f, profile.minimumSkillUtility))
            {
                debugSkillCadence = !chose || spell == null
                    ? "No skill candidate passed targeting/cadence rules"
                    : $"Skill utility {score:0.00} below " +
                      $"{profile.minimumSkillUtility:0.00}";
                return false;
            }

            float timeout = Mathf.Max(
                0.25f,
                spell.Timing.TotalDuration + profile.skillCastTimeoutPadding);
            bool issued = agent.ActionRunner.AssignOrder(new EnemyActionOrderV2
            {
                orderId = nextOrderId++,
                kind = EnemyActionKindV2.CastSkill,
                timeoutSeconds = timeout,
                skillSpell = spell,
                skillCast = cast,
                reason =
                    $"{reason}: {role} chose {spell.DisplayName} (utility {score:0.00})"
            });

            if (!issued)
                return false;

            SpellAITacticalMemory.RecordCast(
                spell,
                agent.gameObject,
                cast);
            lastSkillStartedAt = Time.time;
            legacyAttacksSinceLastSkill = 0;
            consecutiveSkillActions++;
            debugSkillCadence =
                $"Issued {spell.DisplayName}; next skill requires " +
                $"{profile.minimumLegacyAttacksBetweenSkills} legacy attack(s) " +
                $"and {profile.minimumSecondsBetweenSkillStarts:0.00}s";
            debugSkillStartGapRemaining = Mathf.Max(
                0f,
                profile.minimumSecondsBetweenSkillStarts);
            debugLegacyAttacksSinceSkill = 0;
            debugConsecutiveSkillActions = consecutiveSkillActions;
            lastAnyAttackStartedAt = Time.time;
            debugLastAttackSelection =
                $"{agent.name}: SkillSystemV2 {spell.DisplayName}, utility {score:0.00}";
            if (role == EnemyRoleV2.Controller)
                debugControllerAttackSelection = debugLastAttackSelection;
            else if (role == EnemyRoleV2.Flanker)
                debugFlankerAttackSelection = debugLastAttackSelection;
            else if (role == EnemyRoleV2.Sentinel)
                debugSentinelAttackSelection = debugLastAttackSelection;
            else if (role == EnemyRoleV2.SoloDuelist)
                debugSoloAttackSelection = debugLastAttackSelection;
            return true;
        }

        private void RecordLegacyAttackForSkillCadence()
        {
            if (legacyAttacksSinceLastSkill < int.MaxValue)
                legacyAttacksSinceLastSkill++;
            consecutiveSkillActions = 0;
            debugLegacyAttacksSinceSkill = legacyAttacksSinceLastSkill;
            debugConsecutiveSkillActions = 0;
            debugSkillCadence = lastSkillStartedAt > -900f
                ? $"Legacy attack recorded " +
                  $"({legacyAttacksSinceLastSkill}/" +
                  $"{Mathf.Max(0, profile.minimumLegacyAttacksBetweenSkills)})"
                : "No skill has been cast yet";
        }

        private void ApplyAttackSelectionOverrides(
            AttackSelection selection,
            ref int shots,
            ref float interval,
            ref float cooldown)
        {
            if (profile == null)
                return;

            if (profile.useAttackOptionBurstOverrides && selection.overrideBurst)
            {
                if (selection.multiplyCurrentCadence)
                {
                    shots = Mathf.Max(shots, Mathf.Max(1, selection.shotsPerBurst));
                    interval = Mathf.Max(0.03f, interval * Mathf.Max(0.01f, selection.intraBurstIntervalMultiplier));
                    cooldown = Mathf.Max(0.05f, cooldown * Mathf.Max(0.01f, selection.burstCooldownMultiplier));
                }
                else
                {
                    shots = Mathf.Max(1, selection.shotsPerBurst);
                    interval = Mathf.Max(0.03f, selection.intraBurstInterval);
                    cooldown = Mathf.Max(0.05f, selection.burstCooldown);
                }
            }
        }

        private string AttackPatternKey(string patternName)
        {
            if (string.IsNullOrWhiteSpace(patternName))
                return string.Empty;

            return patternName.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        }

        private bool TryResolveRoleProfileSlot(
            EnemySquadRole legacyRole,
            int intensity,
            bool closeRange,
            out EnemyRoleAttackProfiles.ResolvedAttack resolved)
        {
            resolved = default;

            if (profile == null || profile.roleAttackProfiles == null)
                return false;

            return profile.roleAttackProfiles.TryResolve(
                legacyRole,
                Mathf.Clamp(intensity, 0, 2),
                closeRange,
                out resolved);
        }

        private bool TryChooseRoleAttackProfile(
            EnemyAgentV2 agent,
            EnemyRoleV2 role,
            string orderReason,
            float distance,
            float flankAngle,
            string fallback,
            out AttackSelection selection)
        {
            selection = default;

            if (profile == null ||
                !profile.useRoleAttackProfilesAsset ||
                profile.roleAttackProfiles == null ||
                !profile.roleAttackProfilesTakePriority)
            {
                return false;
            }

            EnemySquadRole legacyRole = MapV2RoleToLegacyRoleProfile(role);
            int intensity = DetermineRoleAttackProfileIntensity(role, distance, flankAngle);
            bool closeRange = distance <= Mathf.Max(0.2f, profile.closeRangeAttackDistance);

            EnemyRoleAttackProfiles.ResolvedAttack resolved;
            if (!TryResolveRoleProfileSlot(legacyRole, intensity, closeRange, out resolved))
                return false;

            string resolvedPattern = ResolveSupportedPatternAlias(resolved.patternName);
            if (string.IsNullOrWhiteSpace(resolvedPattern))
                resolvedPattern = fallback;

            if (profile.rotateRoleProfileIntensityToAvoidRepeats &&
                profile.avoidImmediatePatternRepeats &&
                !closeRange)
            {
                string lastPattern;
                lastAttackPatternByAgent.TryGetValue(agent, out lastPattern);

                string chosenKey = AttackPatternKey(resolvedPattern);
                string lastKey = AttackPatternKey(lastPattern);

                if (!string.IsNullOrEmpty(lastKey) && chosenKey == lastKey)
                {
                    int[] alternates = new int[]
                    {
                        Mathf.Clamp(intensity + 1, 0, 2),
                        Mathf.Clamp(intensity - 1, 0, 2),
                        2,
                        1,
                        0
                    };

                    for (int i = 0; i < alternates.Length; i++)
                    {
                        int alternateIntensity = alternates[i];
                        if (alternateIntensity == intensity)
                            continue;

                        EnemyRoleAttackProfiles.ResolvedAttack alternateResolved;
                        if (!TryResolveRoleProfileSlot(legacyRole, alternateIntensity, closeRange, out alternateResolved))
                            continue;

                        string alternatePattern = ResolveSupportedPatternAlias(alternateResolved.patternName);
                        if (string.IsNullOrWhiteSpace(alternatePattern))
                            alternatePattern = fallback;

                        if (AttackPatternKey(alternatePattern) == lastKey)
                            continue;

                        resolved = alternateResolved;
                        resolvedPattern = alternatePattern;
                        intensity = alternateIntensity;
                        break;
                    }
                }
            }

            bool shapeOverride = profile.useRoleAttackProfileShape;
            bool cadenceOverride = profile.useRoleAttackProfileCadence;

            int fanBullets = Mathf.Max(1, resolved.minFanBullets > 0 ? resolved.minFanBullets : 5);
            float fanArc = resolved.fanArcDegrees >= 0f ? resolved.fanArcDegrees : 40f;
            int ringBullets = Mathf.Max(3, resolved.minRingBullets > 0 ? resolved.minRingBullets : 8);
            float angularSpeed = resolved.angularSpeedDegPerTick >= 0f ? resolved.angularSpeedDegPerTick : 12f;
            int minShots = Mathf.Max(1, resolved.minShotsPerBurst > 0 ? resolved.minShotsPerBurst : 1);

            string source = string.IsNullOrWhiteSpace(resolved.sourceLabel)
                ? legacyRole.ToString()
                : resolved.sourceLabel;

            string prettyName = string.IsNullOrWhiteSpace(resolved.patternName)
                ? resolvedPattern
                : resolved.patternName;

            string closeLabel = closeRange ? " close" : "";
            string profileDebug =
                $"{orderReason}: RoleAttackProfiles {source}/{legacyRole} {prettyName}->{resolvedPattern} intensity {intensity}{closeLabel} at {distance:0.0}u";

            if (shapeOverride)
                profileDebug += $" shape fan {fanBullets}/{fanArc:0}° ring {ringBullets}";

            if (cadenceOverride)
                profileDebug += $" cadence x{Mathf.Max(0.01f, resolved.intraBurstIntervalMultiplier):0.00}/x{Mathf.Max(0.01f, resolved.burstCooldownMultiplier):0.00}";

            if (role == EnemyRoleV2.Flanker && flankAngle > 0f)
                profileDebug += $" angle {flankAngle:0}°";

            selection = new AttackSelection
            {
                patternName = resolvedPattern,
                debugReason = profileDebug,
                overrideBurst = cadenceOverride,
                shotsPerBurst = minShots,
                multiplyCurrentCadence = cadenceOverride,
                intraBurstIntervalMultiplier = Mathf.Max(0.01f, resolved.intraBurstIntervalMultiplier),
                burstCooldownMultiplier = Mathf.Max(0.01f, resolved.burstCooldownMultiplier),
                overridePatternShape = shapeOverride,
                fanBullets = fanBullets,
                fanArcDegrees = fanArc,
                ringBullets = ringBullets,
                angularSpeedDegPerTick = angularSpeed
            };

            debugRoleAttackProfileSelection = profileDebug;
            return true;
        }

        private EnemySquadRole MapV2RoleToLegacyRoleProfile(EnemyRoleV2 role)
        {
            switch (role)
            {
                case EnemyRoleV2.Flanker:
                    if (profile != null && player != null && flanker != null)
                    {
                        Vector2 playerPosition = player.position;
                        Vector2 direction = (Vector2)flanker.transform.position - playerPosition;
                        if (direction.sqrMagnitude <= 0.0001f)
                            direction = flanker.AssignedSlot - playerPosition;

                        if (direction.sqrMagnitude > 0.0001f)
                        {
                            float cross = Vector3.Cross(Vector2.up, direction.normalized).z;
                            return cross >= 0f
                                ? EnemySquadRole.FlankerRight
                                : EnemySquadRole.FlankerLeft;
                        }
                    }

                    return EnemySquadRole.FlankerRight;

                case EnemyRoleV2.Sentinel:
                    return EnemySquadRole.Anchor;

                case EnemyRoleV2.SoloDuelist:
                    return profile != null && profile.soloUsesSuppressorRoleProfile
                        ? EnemySquadRole.Suppressor
                        : EnemySquadRole.FlankerRight;

                case EnemyRoleV2.Harrier:
                case EnemyRoleV2.Opportunist:
                    return EnemySquadRole.FlankerRight;

                case EnemyRoleV2.Controller:
                default:
                    return EnemySquadRole.Suppressor;
            }
        }

        private int DetermineRoleAttackProfileIntensity(
            EnemyRoleV2 role,
            float distance,
            float flankAngle)
        {
            if (profile == null)
                return 0;

            bool closeRange = distance <= Mathf.Max(0.2f, profile.closeRangeAttackDistance);
            if (closeRange && profile.keepCloseRangeSafetyPatterns)
                return 0;

            int intensity;

            if (role == EnemyRoleV2.Flanker)
            {
                if (flankAngle >= Mathf.Max(25f, profile.flankerHighProfileAngleDegrees))
                    intensity = 2;
                else
                    intensity = Mathf.Clamp(profile.flankerProfileIntensity, 0, 2);

                if (profile.biasRoleAttackProfilesTowardTouhouSlots && !closeRange)
                    intensity = Mathf.Max(intensity, Mathf.Clamp(profile.minimumFlankerTouhouIntensity, 0, 2));

                return Mathf.Clamp(intensity, 0, 2);
            }

            if (role == EnemyRoleV2.SoloDuelist)
            {
                intensity = Mathf.Clamp(profile.soloProfileIntensity, 0, 2);

                if (profile.biasRoleAttackProfilesTowardTouhouSlots && !closeRange)
                    intensity = Mathf.Max(intensity, Mathf.Clamp(profile.minimumSoloTouhouIntensity, 0, 2));

                return Mathf.Clamp(intensity, 0, 2);
            }

            if (role == EnemyRoleV2.Sentinel)
            {
                intensity = Mathf.Clamp(profile.sentinelProfileIntensity, 0, 2);

                if (profile.biasRoleAttackProfilesTowardTouhouSlots && !closeRange)
                    intensity = Mathf.Max(intensity, Mathf.Clamp(profile.minimumSentinelTouhouIntensity, 0, 2));

                return Mathf.Clamp(intensity, 0, 2);
            }

            if (role == EnemyRoleV2.Controller)
            {
                intensity = Mathf.Clamp(profile.controllerProfileIntensity, 0, 2);

                if (profile.biasRoleAttackProfilesTowardTouhouSlots && !closeRange)
                    intensity = Mathf.Max(intensity, Mathf.Clamp(profile.minimumControllerTouhouIntensity, 0, 2));

                return Mathf.Clamp(intensity, 0, 2);
            }

            return 1;
        }

        private AttackSelection ChooseAttackPattern(
            EnemyAgentV2 agent,
            EnemyRoleV2 role,
            string legacyPattern,
            string orderReason)
        {
            string fallback = ResolveSupportedPatternAlias(legacyPattern);

            if (profile == null || agent == null)
            {
                return new AttackSelection
                {
                    patternName = fallback,
                    debugReason = $"{orderReason}: legacy fixed pattern {fallback}"
                };
            }

            if (role == EnemyRoleV2.SoloDuelist &&
                profile.forceSoloAimedPattern &&
                !profile.rolePoolsOverrideSoloAimedSafety)
            {
                string soloFallback = ResolveSupportedPatternAlias(profile.soloAimedPattern);
                return new AttackSelection
                {
                    patternName = soloFallback,
                    debugReason = $"{orderReason}: solo aimed safety {soloFallback}"
                };
            }

            float distance = player != null
                ? Vector2.Distance(agent.transform.position, player.position)
                : 99f;

            float flankAngle = role == EnemyRoleV2.Flanker
                ? CalculateAngleFromController(agent)
                : 0f;

            AttackSelection profileSelection;
            if (TryChooseRoleAttackProfile(
                    agent,
                    role,
                    orderReason,
                    distance,
                    flankAngle,
                    fallback,
                    out profileSelection))
            {
                return profileSelection;
            }

            if (!profile.useRoleAttackPools)
            {
                return new AttackSelection
                {
                    patternName = fallback,
                    debugReason = $"{orderReason}: role pools disabled, legacy fixed pattern {fallback}"
                };
            }

            EnemyAttackPatternOptionV2[] pool = GetAttackPool(role);

            string lastAgentPattern = null;
            lastAttackPatternByAgent.TryGetValue(agent, out lastAgentPattern);

            string lastRolePattern = null;
            lastAttackPatternByRole.TryGetValue(role, out lastRolePattern);

            EnemyAttackPatternOptionV2 bestOption = null;
            string bestPattern = fallback;
            float bestScore = float.NegativeInfinity;
            int validOptions = 0;

            if (pool != null)
            {
                for (int i = 0; i < pool.Length; i++)
                {
                    EnemyAttackPatternOptionV2 option = pool[i];
                    if (option == null)
                        continue;

                    string candidatePattern = ResolveSupportedPatternAlias(option.patternName);
                    if (string.IsNullOrWhiteSpace(candidatePattern))
                        continue;

                    if (distance < option.minDistance || distance > option.maxDistance)
                        continue;

                    validOptions++;
                    float score = Mathf.Max(0.01f, option.weight);
                    score += GetRolePatternBonus(role, candidatePattern, distance, flankAngle);

                    if (profile.avoidImmediatePatternRepeats)
                    {
                        float repeatMultiplier = Mathf.Clamp01(profile.repeatedPatternWeightMultiplier);

                        if (!string.IsNullOrWhiteSpace(lastAgentPattern) &&
                            candidatePattern == lastAgentPattern &&
                            HasDifferentValidPattern(pool, candidatePattern, distance))
                        {
                            score *= repeatMultiplier;
                        }

                        if (!string.IsNullOrWhiteSpace(lastRolePattern) &&
                            candidatePattern == lastRolePattern &&
                            HasDifferentValidPattern(pool, candidatePattern, distance))
                        {
                            score *= Mathf.Lerp(0.45f, 1f, repeatMultiplier);
                        }
                    }

                    if (profile.attackChoiceRandomness > 0f)
                        score += Random.Range(0f, profile.attackChoiceRandomness);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOption = option;
                        bestPattern = candidatePattern;
                    }
                }
            }

            if (bestOption == null || validOptions == 0)
            {
                return new AttackSelection
                {
                    patternName = fallback,
                    debugReason = $"{orderReason}: no valid pool option, fallback {fallback} at {distance:0.0}u"
                };
            }

            bool useShapeOverride = profile.useAttackOptionShapeOverrides && bestOption.overridePatternShape;
            bool useBurstOverride = profile.useAttackOptionBurstOverrides && bestOption.overrideBurst;

            string phraseDebug = "";
            if (useShapeOverride)
                phraseDebug += $" shape fan {Mathf.Max(1, bestOption.fanBullets)}/{Mathf.Max(0f, bestOption.fanArcDegrees):0}° ring {Mathf.Max(3, bestOption.ringBullets)}";
            if (useBurstOverride)
                phraseDebug += $" burst {Mathf.Max(1, bestOption.shotsPerBurst)}";
            if (role == EnemyRoleV2.Flanker && flankAngle > 0f)
                phraseDebug += $" angle {flankAngle:0}°";

            return new AttackSelection
            {
                patternName = bestPattern,
                debugReason = $"{orderReason}: {role} chose {bestPattern} ({bestOption.label}; {bestOption.intent}) at {distance:0.0}u{phraseDebug}",
                overrideBurst = useBurstOverride,
                shotsPerBurst = bestOption.shotsPerBurst,
                intraBurstInterval = bestOption.intraBurstInterval,
                burstCooldown = bestOption.burstCooldown,
                multiplyCurrentCadence = false,
                intraBurstIntervalMultiplier = 1f,
                burstCooldownMultiplier = 1f,
                overridePatternShape = useShapeOverride,
                fanBullets = bestOption.fanBullets,
                fanArcDegrees = bestOption.fanArcDegrees,
                ringBullets = bestOption.ringBullets,
                angularSpeedDegPerTick = bestOption.angularSpeedDegPerTick
            };
        }

        private EnemyAttackPatternOptionV2[] GetAttackPool(EnemyRoleV2 role)
        {
            if (profile == null)
                return null;

            if (role == EnemyRoleV2.Flanker)
            {
                if (profile.useStage3V2DefaultRoleAttackPools)
                    return BuildDefaultFlankerPool();

                return HasOptions(profile.flankerAttackPool)
                    ? profile.flankerAttackPool
                    : BuildDefaultFlankerPool();
            }

            if (role == EnemyRoleV2.SoloDuelist)
            {
                if (profile.useStage3V2DefaultRoleAttackPools)
                    return BuildDefaultSoloPool();

                return HasOptions(profile.soloAttackPool)
                    ? profile.soloAttackPool
                    : BuildDefaultSoloPool();
            }

            if (profile.useStage3V2DefaultRoleAttackPools)
                return BuildDefaultControllerPool();

            return HasOptions(profile.controllerAttackPool)
                ? profile.controllerAttackPool
                : BuildDefaultControllerPool();
        }

        private static bool HasOptions(EnemyAttackPatternOptionV2[] options)
        {
            return options != null && options.Length > 0;
        }

        private static EnemyAttackPatternOptionV2[] BuildDefaultControllerPool()
        {
            return new EnemyAttackPatternOptionV2[]
            {
                new EnemyAttackPatternOptionV2("Wide herding fan", "AimedFan", 3.2f, 0.7f, 8.5f, "wide pressure that shapes player movement")
                {
                    overridePatternShape = true,
                    fanBullets = 5,
                    fanArcDegrees = 62f,
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.13f,
                    burstCooldown = 0.66f
                },
                new EnemyAttackPatternOptionV2("Sweeping lane", "SweepFan", 2.7f, 1.7f, 8.5f, "moving lane pressure while allies rotate")
                {
                    overridePatternShape = true,
                    fanBullets = 4,
                    fanArcDegrees = 46f,
                    angularSpeedDegPerTick = 16f,
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.12f,
                    burstCooldown = 0.78f
                },
                new EnemyAttackPatternOptionV2("Close ring check", "Ring", 1.55f, 0.0f, 3.0f, "deny close space around the Controller")
                {
                    overridePatternShape = true,
                    ringBullets = 8,
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.13f,
                    burstCooldown = 0.86f
                }
            };
        }

        private static EnemyAttackPatternOptionV2[] BuildDefaultFlankerPool()
        {
            return new EnemyAttackPatternOptionV2[]
            {
                new EnemyAttackPatternOptionV2("Precise ambush shot", "AimedSingle", 3.4f, 0.0f, 9.0f, "precise side/rear punish")
                {
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.10f,
                    burstCooldown = 0.58f
                },
                new EnemyAttackPatternOptionV2("Narrow side fan", "AimedFan", 2.0f, 1.2f, 7.5f, "compact side-angle spread")
                {
                    overridePatternShape = true,
                    fanBullets = 3,
                    fanArcDegrees = 28f,
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.10f,
                    burstCooldown = 0.64f
                },
                new EnemyAttackPatternOptionV2("Point-blank cross", "BoI_4Way", 1.6f, 0.0f, 2.7f, "close crossfire escape check")
                {
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.10f,
                    burstCooldown = 0.78f
                }
            };
        }

        private static EnemyAttackPatternOptionV2[] BuildDefaultSoloPool()
        {
            return new EnemyAttackPatternOptionV2[]
            {
                new EnemyAttackPatternOptionV2("Clean aimed shot", "AimedSingle", 3.1f, 0.0f, 9.0f, "reliable aimed duel shot")
                {
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.12f,
                    burstCooldown = 0.60f
                },
                new EnemyAttackPatternOptionV2("Short duel fan", "AimedFan", 1.9f, 1.0f, 6.5f, "small pressure spread")
                {
                    overridePatternShape = true,
                    fanBullets = 3,
                    fanArcDegrees = 34f,
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.12f,
                    burstCooldown = 0.68f
                },
                new EnemyAttackPatternOptionV2("Close escape check", "BoI_4Way", 1.25f, 0.0f, 2.5f, "close-range safety check")
                {
                    overrideBurst = true,
                    shotsPerBurst = 1,
                    intraBurstInterval = 0.10f,
                    burstCooldown = 0.76f
                }
            };
        }

        private bool HasDifferentValidPattern(
            EnemyAttackPatternOptionV2[] pool,
            string currentPattern,
            float distance)
        {
            if (pool == null)
                return false;

            for (int i = 0; i < pool.Length; i++)
            {
                EnemyAttackPatternOptionV2 option = pool[i];
                if (option == null)
                    continue;

                string pattern = ResolveSupportedPatternAlias(option.patternName);
                if (string.IsNullOrWhiteSpace(pattern) || pattern == currentPattern)
                    continue;

                if (distance >= option.minDistance && distance <= option.maxDistance)
                    return true;
            }

            return false;
        }

        private float CalculateAngleFromController(EnemyAgentV2 agent)
        {
            if (agent == null || player == null || controller == null || controller == agent)
                return 0f;

            Vector2 playerPosition = player.position;
            Vector2 controllerDirection = (Vector2)controller.transform.position - playerPosition;
            Vector2 agentDirection = (Vector2)agent.transform.position - playerPosition;

            if (controllerDirection.sqrMagnitude <= 0.0001f)
                controllerDirection = controller.AssignedSlot - playerPosition;

            if (agentDirection.sqrMagnitude <= 0.0001f)
                agentDirection = agent.AssignedSlot - playerPosition;

            if (controllerDirection.sqrMagnitude <= 0.0001f ||
                agentDirection.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }

            return Vector2.Angle(controllerDirection.normalized, agentDirection.normalized);
        }

        private float GetRolePatternBonus(
            EnemyRoleV2 role,
            string pattern,
            float distance,
            float flankAngle)
        {
            float closeDistance = profile != null
                ? Mathf.Max(0.2f, profile.closeRangeAttackDistance)
                : 2.15f;

            bool close = distance <= closeDistance;
            float bonus = 0f;

            if (role == EnemyRoleV2.Controller)
            {
                if (pattern == "AimedFan" || pattern == "SweepFan")
                    bonus += 0.45f;

                if (close && pattern == "Ring")
                    bonus += 1.25f;
            }
            else if (role == EnemyRoleV2.Flanker)
            {
                bool hasGoodAngle = profile != null &&
                                    profile.preferPrecisionFlankerAtGoodAngle &&
                                    flankAngle >= Mathf.Max(25f, profile.precisionFlankerAngleDegrees);

                if (pattern == "AimedSingle")
                    bonus += hasGoodAngle ? 1.6f : 0.55f;

                if (pattern == "AimedFan")
                    bonus += hasGoodAngle ? -0.2f : 0.35f;

                if (close && (pattern == "BoI_4Way" || pattern == "BoI_8Way" || pattern == "Ring"))
                    bonus += 1.3f;
            }
            else if (role == EnemyRoleV2.SoloDuelist)
            {
                if (!close && pattern == "AimedSingle")
                    bonus += 0.5f;

                if (close && (pattern == "BoI_4Way" || pattern == "BoI_8Way" || pattern == "Ring"))
                    bonus += 1.15f;
            }
            else if (role == EnemyRoleV2.Sentinel)
            {
                if (!close && (pattern == "Ring" || pattern == "RotatingFlowerRing" || pattern == "HaloSpear"))
                    bonus += 1.0f;

                if (!close && (pattern == "SweepFan" || pattern == "CrescentSweep"))
                    bonus += 0.45f;

                if (close && (pattern == "Ring" || pattern == "CloseCross" || pattern == "BoI_4Way"))
                    bonus += 1.2f;
            }

            return bonus;
        }

        private string ResolveSupportedPatternAlias(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return "AimedSingle";

            string trimmed = requested.Trim();
            string key = AttackPatternKey(trimmed);
            bool preferTouhou = profile == null || profile.preferTouhouPatternNamesWhenAvailable;

            switch (key)
            {
                case "aimedsingle": return "AimedSingle";
                case "aimedfan": return "AimedFan";
                case "ring": return "Ring";
                case "spiral": return "Spiral";
                case "boi_4way": return "BoI_4Way";
                case "boi4way": return "BoI_4Way";
                case "boi_8way": return "BoI_8Way";
                case "boi8way": return "BoI_8Way";
                case "sweepfan": return "SweepFan";

                // Pretty/Touhou pattern names. Stage 3 v4 now passes these
                // through to EnemyShooterDebug first when enabled. The executor
                // still has a per-pattern compatibility fallback, so older
                // shooter versions remain safe.
                case "petalfan": return preferTouhou ? "PetalFan" : "AimedFan";
                case "butterflyspread": return preferTouhou ? "ButterflySpread" : "AimedFan";
                case "closingblossom": return preferTouhou ? "ClosingBlossom" : "SweepFan";
                case "staggeredrosette": return preferTouhou ? "StaggeredRosette" : "Spiral";
                case "crescentsweep": return preferTouhou ? "CrescentSweep" : "SweepFan";
                case "rotatingflowerring": return preferTouhou ? "RotatingFlowerRing" : "Ring";
                case "halospear": return preferTouhou ? "HaloSpear" : "Ring";
                case "closecross": return preferTouhou ? "CloseCross" : "BoI_4Way";
                case "escapecutoff": return preferTouhou ? "EscapeCutoff" : "AimedSingle";
                case "braidedstream": return preferTouhou ? "BraidedStream" : "Spiral";
                default: return trimmed;
            }
        }

        private void IssueRecover(EnemyAgentV2 agent, float seconds)
        {
            if (agent == null || agent.ActionRunner == null)
                return;

            agent.ActionRunner.AssignOrder(new EnemyActionOrderV2
            {
                orderId = nextOrderId++,
                kind = EnemyActionKindV2.Recover,
                durationSeconds = seconds,
                reason = "Visible punish/recovery beat"
            });
        }


        private bool HasGoodEnoughFlankAngle(EnemyAgentV2 flankerAgent)
        {
            debugCurrentFlankAngle = 0f;

            if (profile == null || player == null || flankerAgent == null)
                return false;

            Vector2 playerPosition = player.position;

            Vector2 controllerDirection = controller != null
                ? (Vector2)controller.transform.position - playerPosition
                : Vector2.zero;

            if (controllerDirection.sqrMagnitude <= 0.0001f && controller != null)
                controllerDirection = controller.AssignedSlot - playerPosition;

            Vector2 flankerDirection =
                (Vector2)flankerAgent.transform.position - playerPosition;

            if (flankerDirection.sqrMagnitude <= 0.0001f)
                flankerDirection = flankerAgent.AssignedSlot - playerPosition;

            if (controllerDirection.sqrMagnitude <= 0.0001f ||
                flankerDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            debugCurrentFlankAngle = Vector2.Angle(
                controllerDirection.normalized,
                flankerDirection.normalized
            );

            bool angleGoodEnough = debugCurrentFlankAngle >=
                                   Mathf.Max(25f, profile.goodEnoughFlankAngleDegrees);

            bool closeToSlot = Vector2.Distance(
                flankerAgent.transform.position,
                flankerAgent.AssignedSlot
            ) <= Mathf.Max(0.1f, profile.goodEnoughSlotDistance);

            bool hasPressureLine = HasLineOfSight(
                flankerAgent.transform.position,
                player.position
            );

            return hasPressureLine && (angleGoodEnough || closeToSlot);
        }

        private bool CanCompletePhraseAfterBreather(bool soloPhrase, bool hasSentinel)
        {
            if (profile == null || !profile.usePhraseBreathingRoom)
                return true;

            if (phraseBreatherUntil < 0f)
            {
                float duration = soloPhrase
                    ? Mathf.Max(0f, profile.soloPhraseBreatherSeconds)
                    : Mathf.Max(0f, profile.phraseBreatherSeconds) +
                      (hasSentinel ? Mathf.Max(0f, profile.extraBreatherAfterSentinelPhrase) : 0f);

                phraseBreatherUntil = Time.time + duration;
                debugPhraseBreatherRemaining = duration;
                debugPressurePacing = duration > 0.001f
                    ? $"Phrase breather: {duration:0.00}s"
                    : "Phrase breather skipped";

                if (profile.logPressurePacing && duration > 0.001f)
                    Debug.Log($"[Enemy AI V2] {debugPressurePacing}", this);

                return duration <= 0.001f;
            }

            float remaining = phraseBreatherUntil - Time.time;
            if (remaining > 0f)
            {
                debugPhraseBreatherRemaining = remaining;
                debugPressurePacing = $"Phrase breather: {remaining:0.00}s";
                return false;
            }

            phraseBreatherUntil = -1f;
            debugPhraseBreatherRemaining = 0f;
            debugPressurePacing = "Breather complete";
            return true;
        }

        private float GetRecoverySeconds(EnemyRoleV2 role)
        {
            float baseSeconds = role == EnemyRoleV2.SoloDuelist
                ? profile.soloRecoverySeconds
                : profile.standardRecoverySeconds;

            if (profile != null && profile.useFluidCombatMovement)
                baseSeconds *= Mathf.Clamp(profile.fluidRecoveryMultiplier, 0.25f, 1f);

            if (profile != null && profile.usePressurePacing)
                baseSeconds *= Mathf.Clamp(profile.pressurePacingRecoveryMultiplier, 0.5f, 2.5f);

            baseSeconds *= TempoRecoveryMultiplier();

            return Mathf.Max(0.05f, baseSeconds);
        }

        private bool IsMoveSuccessfulOrClose(EnemyAgentV2 agent)
        {
            if (agent == null || agent.ActionRunner == null)
                return false;

            if (agent.ActionRunner.Status == EnemyActionStatusV2.Succeeded)
                return true;

            float distanceToSlot = Vector2.Distance(agent.transform.position, agent.AssignedSlot);

            if (profile != null && profile.useFluidCombatMovement &&
                distanceToSlot <= Mathf.Max(profile.tacticalSlotArrivalRadius * 1.35f, profile.goodEnoughSlotDistance))
            {
                return true;
            }

            return distanceToSlot <= profile.tacticalSlotArrivalRadius * 1.35f;
        }

        private bool IsActionFailed(EnemyAgentV2 agent)
        {
            return agent != null &&
                   agent.ActionRunner != null &&
                   agent.ActionRunner.Status == EnemyActionStatusV2.Failed;
        }

        private bool IsActionFinished(EnemyAgentV2 agent)
        {
            if (agent == null || agent.ActionRunner == null)
                return true;

            EnemyActionStatusV2 status = agent.ActionRunner.Status;
            return status == EnemyActionStatusV2.Succeeded ||
                   status == EnemyActionStatusV2.Failed ||
                   status == EnemyActionStatusV2.Cancelled ||
                   status == EnemyActionStatusV2.Idle;
        }

        private void BeginNewPlan(string reason)
        {
            CancelAllOrders(reason);
            currentTactic = SquadTacticV2.None;
            phase = PlanPhase.None;
            controller = null;
            flanker = null;
            sentinel = null;
            planStartedAt = Time.time;
            phaseStartedAt = Time.time;
            controllerAttackIssued = false;
            flankerAttackIssued = false;
            sentinelAttackIssued = false;
            phraseBreatherUntil = -1f;
            debugPlan = $"Planning: {reason}";
            debugControllerUsedFluidPressure = false;
            debugFlankerUsedFluidPressure = false;
            debugSentinelUsedFluidPressure = false;
            debugCurrentFlankAngle = 0f;
            debugSentinelPlan = $"New plan: {reason}";
            debugSentinelLaneScore = 0f;
            debugSentinelLaneCoverage = 0;
            debugFluidDecision = $"New plan: {reason}";
            SyncDebugRefs();
        }

        private void SetPhase(PlanPhase newPhase)
        {
            phase = newPhase;
            phaseStartedAt = Time.time;
            debugPhase = newPhase.ToString();
        }

        private void CancelAllOrders(string reason)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null)
                    agents[i].ActionRunner?.CancelCurrent(reason);
            }
        }

        private List<EnemyAgentV2> BuildAliveList()
        {
            List<EnemyAgentV2> alive = new List<EnemyAgentV2>();

            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 agent = agents[i];

                if (agent != null && agent.isActiveAndEnabled && agent.IsAlive)
                    alive.Add(agent);
            }

            return alive;
        }

        private void CleanupAgents()
        {
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                if (agents[i] == null)
                    agents.RemoveAt(i);
            }
        }

        private void ResolveReferences()
        {
            if (navigationGrid == null)
                navigationGrid = FindObjectOfType<ArenaNavigationGrid>(true);

            ResolvePlayer();
        }

        private bool ResolvePlayer()
        {
            Transform previous = player;

            if (player == null && !string.IsNullOrWhiteSpace(playerTag))
            {
                GameObject found = GameObject.FindGameObjectWithTag(playerTag);

                if (found != null)
                    player = found.transform;
            }

            return previous != player;
        }

        private void ApplyBackendModeChange(string reason, bool force)
        {
            lastAppliedBackendMode = backendMode;
            backendModeHasBeenApplied = true;
            debugAppliedBackendMode = backendMode;
            debugBackendModeTransition = reason;

            RefreshAgentRuntimeContexts(force: true);
            BeginNewPlan(reason);

            if (profile != null && profile.logPlanChanges)
                Debug.Log($"[Enemy AI V2] {reason}", this);
        }

        private void RefreshAgentRuntimeContexts(bool force)
        {
            if (!force &&
                lastAppliedProfile == profile &&
                lastAppliedGrid == navigationGrid &&
                lastAppliedPlayer == player)
            {
                return;
            }

            lastAppliedProfile = profile;
            lastAppliedGrid = navigationGrid;
            lastAppliedPlayer = player;

            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 agent = agents[i];
                if (agent == null)
                    continue;

                agent.Initialize(this, player, profile, navigationGrid);
            }
        }

        private void RefreshReadinessDebug()
        {
            debugHasProfile = profile != null;
            debugHasPlayer = player != null;
            debugHasNavigationGrid = navigationGrid != null;
            debugRuntimeReady = RuntimeReady;

            if (RuntimeReady)
                debugReadinessReason = "Ready";
            else if (profile == null)
                debugReadinessReason = "Assign EnemyAIV2Profile";
            else if (player == null)
                debugReadinessReason = $"Waiting for GameObject tagged '{playerTag}'";
            else
                debugReadinessReason = "Not ready";
        }

        private Vector2 SnapToNavigation(Vector2 position)
        {
            if (navigationGrid != null && navigationGrid.IsBuilt)
                return navigationGrid.FindNearestWalkablePosition(position);

            return position;
        }

        private float EstimatePathCost(Vector2 start, Vector2 end)
        {
            if (navigationGrid != null && navigationGrid.IsBuilt)
                return navigationGrid.EstimatePathCost(start, end);

            return Vector2.Distance(start, end);
        }

        private bool HasLineOfSight(Vector2 origin, Vector2 target)
        {
            if (navigationGrid != null && navigationGrid.IsBuilt)
                return navigationGrid.HasClearPath(origin, target);

            return true;
        }

        private EnemySectorV2 GetSectorForPosition(Vector2 position)
        {
            if (player == null)
                return EnemySectorV2.None;

            Vector2 direction = position - (Vector2)player.position;

            if (direction.sqrMagnitude <= 0.0001f)
                return EnemySectorV2.None;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            angle = (angle + 360f) % 360f;

            if (angle < 22.5f || angle >= 337.5f) return EnemySectorV2.Right;
            if (angle < 67.5f) return EnemySectorV2.FrontRight;
            if (angle < 112.5f) return EnemySectorV2.Front;
            if (angle < 157.5f) return EnemySectorV2.FrontLeft;
            if (angle < 202.5f) return EnemySectorV2.Left;
            if (angle < 247.5f) return EnemySectorV2.RearLeft;
            if (angle < 292.5f) return EnemySectorV2.Rear;
            return EnemySectorV2.RearRight;
        }

        private static Vector2 DirectionFromAngle(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private void SyncDebugRefs()
        {
            debugController = controller;
            debugFlanker = flanker;
            debugSentinel = sentinel;
        }

        private void LogPlan(string planName)
        {
            debugPlan = planName;

            if (profile != null && profile.logPlanChanges)
                Debug.Log($"[Enemy AI V2] {planName}", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (profile == null || !profile.drawFormationGizmos)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(debugControllerSlot, 0.32f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(debugFlankerSlot, 0.32f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(debugSentinelSlot, 0.32f);

            if (profile.drawLeasesAndClumps)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.18f);
                Gizmos.DrawWireSphere(debugControllerSlot, Mathf.Max(0.05f, profile.destinationLeaseRadius));

                Gizmos.color = new Color(1f, 0f, 1f, 0.18f);
                Gizmos.DrawWireSphere(debugFlankerSlot, Mathf.Max(0.05f, profile.destinationLeaseRadius));

                Gizmos.color = new Color(0f, 1f, 1f, 0.18f);
                Gizmos.DrawWireSphere(debugSentinelSlot, Mathf.Max(0.05f, profile.destinationLeaseRadius));
            }

            if (player != null)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.75f);
                Gizmos.DrawLine(player.position, debugControllerSlot);

                Gizmos.color = new Color(1f, 0f, 1f, 0.75f);
                Gizmos.DrawLine(player.position, debugFlankerSlot);

                Gizmos.color = new Color(0f, 1f, 1f, 0.75f);
                Gizmos.DrawLine(player.position, debugSentinelSlot);
            }
        }
#endif
    }
}
