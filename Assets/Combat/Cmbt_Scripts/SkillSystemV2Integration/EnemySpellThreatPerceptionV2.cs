using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    /// <summary>
    /// Passive perception/reaction adapter for Enemy AI V2. It queries generic
    /// delivery geometry, preserves the normal squad planner, and only issues
    /// a bounded EvadeThreat order when personality and commitment rules allow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyAgentV2))]
    [RequireComponent(typeof(EnemyLocomotionV2))]
    [RequireComponent(typeof(EnemyActionRunnerV2))]
    public sealed class EnemySpellThreatPerceptionV2 : MonoBehaviour
    {
        [Header("Behavior")]
        [Tooltip("Optional reusable personality profile. When empty, the built-in preset below is used.")]
        [SerializeField] private EnemyThreatResponseProfileV2 responseProfile;

        [Tooltip("Immediate fallback personality for enemies that do not need a custom response-profile asset.")]
        [SerializeField] private EnemyThreatReactionPresetV2 fallbackPreset =
            EnemyThreatReactionPresetV2.Humanoid;

        [Tooltip("Movement reactions this enemy body is physically capable of attempting.")]
        [SerializeField] private SpellAIReaction availableReactions =
            SpellAIReaction.DodgeSideways |
            SpellAIReaction.LeaveArea |
            SpellAIReaction.SeekCover |
            SpellAIReaction.SpreadOut |
            SpellAIReaction.CloseDistance |
            SpellAIReaction.InterruptCaster;

        [Header("Perception Geometry")]
        [Tooltip("Maximum base distance at which delivery geometry can be noticed. Hazard Awareness scales this value.")]
        [SerializeField, Min(0.1f)] private float basePerceptionRadius = 6f;

        [Tooltip("Approximate radius of this enemy body when predicting whether a path enters danger.")]
        [SerializeField, Min(0f)] private float bodyRadius = 0.35f;

        [Tooltip("Base seconds of current movement projected through nearby delivery geometry.")]
        [SerializeField, Min(0.05f)] private float baseAnticipationSeconds = 1.1f;

        [Tooltip("Seconds between threat evaluations. This bounds CPU work and prevents frame-perfect reactions.")]
        [SerializeField, Min(0.02f)] private float perceptionInterval = 0.08f;

        [Header("Avoidance Movement")]
        [Tooltip("Base distance used for sidesteps and escape destinations before Avoidance Strength is applied.")]
        [SerializeField, Min(0.1f)] private float reactionMoveDistance = 2f;

        [Tooltip("Extra space kept beyond a delivery's danger boundary to prevent edge oscillation.")]
        [SerializeField, Min(0f)] private float hazardClearanceMargin = 0.45f;

        [Tooltip("How close the locomotion action must get to the selected safe destination.")]
        [SerializeField, Min(0.05f)] private float reactionArrivalRadius = 0.18f;

        [Tooltip("Watchdog duration for one avoidance order.")]
        [SerializeField, Min(0.1f)] private float reactionTimeout = 1.6f;

        [Header("Runtime Diagnostics")]
        [SerializeField] private string debugPerception = "Not evaluated";
        [SerializeField] private string debugDecision = "None";
        [SerializeField] private string debugThreat = "None";
        [SerializeField] private SpellAIReaction debugReaction;
        [SerializeField] private float debugThreatScore;
        [SerializeField] private float debugTimeToImpact;
        [SerializeField] private int debugRelevantThreats;
        [SerializeField] private Vector2 debugSafestDestination;
        [SerializeField] private float debugNextReactionIn;

        [Header("References")]
        [SerializeField] private EnemyAgentV2 agent;
        [SerializeField] private EnemyLocomotionV2 locomotion;
        [SerializeField] private EnemyActionRunnerV2 actionRunner;

        private readonly List<SpellAIThreatEvaluation> relevantThreats =
            new List<SpellAIThreatEvaluation>(24);
        private readonly List<Vector2> candidateDestinations =
            new List<Vector2>(24);
        private readonly Dictionary<int, float> noticedAt =
            new Dictionary<int, float>();
        private readonly Dictionary<int, int> reactionCounts =
            new Dictionary<int, int>();
        private readonly List<int> staleThreatIds = new List<int>(16);

        private float nextPerceptionAt;
        private float nextReactionAt;
        private int reactionSequence;
        private int activeReactionThreatId;

        public EnemyThreatResponseProfileV2 ResponseProfile => responseProfile;
        public EnemyThreatReactionPresetV2 FallbackPreset => fallbackPreset;
        public string DebugDecision => debugDecision;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            nextPerceptionAt = 0f;
            nextReactionAt = 0f;
            activeReactionThreatId = 0;
        }

        private void OnDisable()
        {
            if (actionRunner != null && actionRunner.IsBusy &&
                actionRunner.CurrentKind == EnemyActionKindV2.EvadeThreat)
            {
                actionRunner.CancelCurrent("Threat perception disabled");
            }
            activeReactionThreatId = 0;
            noticedAt.Clear();
            reactionCounts.Clear();
        }

        private void Update()
        {
            ResolveReferences();
            EnemyAIV2Profile combatProfile = agent != null
                ? agent.Profile
                : null;
            if (combatProfile == null ||
                !combatProfile.enableSpellThreatReactions ||
                agent == null || !agent.IsAlive ||
                locomotion == null || actionRunner == null)
            {
                if (actionRunner != null && actionRunner.IsBusy &&
                    actionRunner.CurrentKind ==
                        EnemyActionKindV2.EvadeThreat)
                {
                    actionRunner.CancelCurrent(
                        "Threat reactions unavailable");
                }
                activeReactionThreatId = 0;
                noticedAt.Clear();
                reactionCounts.Clear();
                debugPerception = combatProfile == null
                    ? "Waiting for EnemyAIV2Profile"
                    : "Threat reactions disabled or runtime incomplete";
                return;
            }

            if (Time.time < nextPerceptionAt)
            {
                debugNextReactionIn = Mathf.Max(
                    0f,
                    nextReactionAt - Time.time);
                return;
            }
            nextPerceptionAt = Time.time + Mathf.Max(
                0.02f,
                perceptionInterval);

            EnemyThreatResponseBehaviorV2 behavior = ResolveBehavior();
            Vector2 position = transform.position;
            Vector2 plannedVelocity = ResolvePlannedVelocity(combatProfile);
            float awarenessRadius = basePerceptionRadius * Mathf.Lerp(
                0.35f,
                1.25f,
                behavior.HazardAwareness);
            float anticipation = baseAnticipationSeconds * Mathf.Lerp(
                0.45f,
                1.45f,
                behavior.HazardAwareness);
            SpellAIThreatService.CollectRelevantThreats(
                gameObject,
                position,
                plannedVelocity,
                awarenessRadius,
                bodyRadius,
                anticipation,
                relevantThreats);
            debugRelevantThreats = relevantThreats.Count;
            debugPerception = relevantThreats.Count > 0
                ? $"{relevantThreats.Count} relevant delivery threat(s)"
                : "No current or predicted path exposure";
            PruneThreatMemory();

            if (!actionRunner.IsBusy ||
                actionRunner.CurrentKind != EnemyActionKindV2.EvadeThreat)
                activeReactionThreatId = 0;
            else if (activeReactionThreatId != 0 &&
                     SpellAIThreatService.IsThreatActive(
                         activeReactionThreatId))
            {
                debugDecision =
                    "Continuing committed threat-avoidance route";
                debugNextReactionIn = Mathf.Max(
                    0f,
                    nextReactionAt - Time.time);
                return;
            }

            if (!TryChooseReactionOpportunity(
                    behavior,
                    combatProfile,
                    out SpellAIThreatEvaluation threat,
                    out SpellAIReaction reaction,
                    out string rejection))
            {
                debugDecision = rejection;
                debugNextReactionIn = Mathf.Max(
                    0f,
                    nextReactionAt - Time.time);
                return;
            }

            if (activeReactionThreatId == threat.ThreatId &&
                actionRunner.CurrentKind == EnemyActionKindV2.EvadeThreat)
            {
                debugDecision = "Continuing current threat-avoidance route";
                return;
            }

            Vector2 destination = BuildSafestUsefulDestination(
                threat,
                reaction,
                behavior,
                position,
                plannedVelocity);
            if ((destination - position).sqrMagnitude <= 0.04f)
            {
                debugDecision = "No safer reachable destination improved the plan";
                return;
            }

            var order = new EnemyActionOrderV2
            {
                orderId = BuildReactionOrderId(),
                kind = EnemyActionKindV2.EvadeThreat,
                targetPosition = destination,
                arrivalRadius = reactionArrivalRadius,
                timeoutSeconds = reactionTimeout,
                threatId = threat.ThreatId,
                threatScore = threat.Score,
                threatTimeToImpact = threat.TimeToImpact,
                threatIsInside = threat.IsInside,
                reason = $"{reaction} from {threat.Spell.DisplayName} " +
                         $"(score {threat.Score:0.00})"
            };
            if (!actionRunner.AssignOrder(order))
            {
                debugDecision = "Avoidance destination was rejected";
                return;
            }

            activeReactionThreatId = threat.ThreatId;
            reactionCounts.TryGetValue(threat.ThreatId, out int count);
            reactionCounts[threat.ThreatId] = count + 1;
            nextReactionAt = Time.time + behavior.AvoidanceCooldown;
            debugThreat = threat.Spell.DisplayName;
            debugReaction = reaction;
            debugThreatScore = threat.Score;
            debugTimeToImpact = threat.TimeToImpact;
            debugSafestDestination = destination;
            debugDecision = $"ISSUED {reaction}: {destination}";
            debugNextReactionIn = behavior.AvoidanceCooldown;
        }

        private bool TryChooseReactionOpportunity(
            in EnemyThreatResponseBehaviorV2 behavior,
            EnemyAIV2Profile combatProfile,
            out SpellAIThreatEvaluation best,
            out SpellAIReaction reaction,
            out string rejection)
        {
            best = default;
            reaction = SpellAIReaction.None;
            rejection = relevantThreats.Count == 0
                ? "No dangerous delivery geometry intersects the plan"
                : "Personality accepted current hazard risk";
            float bestPriority = float.NegativeInfinity;

            for (int i = 0; i < relevantThreats.Count; i++)
            {
                SpellAIThreatEvaluation candidate = relevantThreats[i];
                if (!noticedAt.ContainsKey(candidate.ThreatId))
                    noticedAt[candidate.ThreatId] = Time.time;

                bool emergency = candidate.IsInside ||
                    candidate.Score >= combatProfile.emergencyThreatScore;
                if (candidate.IsTrap &&
                    !behavior.RecognizesHiddenTraps &&
                    !candidate.IsInside)
                {
                    rejection = "This personality does not recognize traps early";
                    continue;
                }
                if (!candidate.IsInside && candidate.IsTelegraph &&
                    candidate.TimeToImpact < behavior.MinimumReadableWarning)
                {
                    rejection = "Telegraph was too brief to read fairly";
                    continue;
                }
                if (!candidate.IsInside &&
                    Time.time - noticedAt[candidate.ThreatId] <
                    behavior.ReactionDelay)
                {
                    rejection = "Readable reaction delay is still running";
                    continue;
                }
                if (!emergency && candidate.Score <= behavior.RiskTolerance)
                {
                    rejection = "Threat is below this personality's risk tolerance";
                    continue;
                }
                if (!emergency && behavior.CanChargeThroughDanger &&
                    candidate.Score < Mathf.Lerp(
                        0.72f,
                        0.94f,
                        behavior.RiskTolerance))
                {
                    rejection = "Personality intentionally charged through danger";
                    continue;
                }
                if (!emergency && DeterministicRoll(candidate.ThreatId) <
                    behavior.MistakeChance)
                {
                    rejection = "Personality missed this reaction opportunity";
                    continue;
                }
                reactionCounts.TryGetValue(candidate.ThreatId, out int used);
                if (!emergency && behavior.MaximumReactionsPerThreat > 0 &&
                    used >= behavior.MaximumReactionsPerThreat)
                {
                    rejection = "Reaction opportunities for this threat are exhausted";
                    continue;
                }
                if (!emergency && Time.time < nextReactionAt)
                {
                    rejection = "Avoidance cooldown preserves the current plan";
                    continue;
                }
                if (IsCommittedAttack(actionRunner.CurrentKind) &&
                    !ShouldInterruptAttack(candidate, behavior, emergency))
                {
                    rejection = "Current attack commitment is more valuable";
                    continue;
                }
                if (!emergency && ReactionCapacityReached(combatProfile))
                {
                    rejection = "Squad reaction capacity is currently occupied";
                    continue;
                }

                SpellAIReaction available =
                    candidate.SuggestedReactions & availableReactions;
                SpellAIReaction selected = ChooseReaction(
                    candidate,
                    available,
                    behavior);
                if (selected == SpellAIReaction.None)
                {
                    rejection = available == SpellAIReaction.None
                        ? "Authored reactions are unavailable to this enemy"
                        : "No supported movement response fits this threat";
                    continue;
                }

                float priority = candidate.Score +
                                 (candidate.IsInside ? 0.3f : 0f) -
                                 candidate.TimeToImpact * 0.05f;
                if (priority <= bestPriority)
                    continue;
                bestPriority = priority;
                best = candidate;
                reaction = selected;
            }

            return !float.IsNegativeInfinity(bestPriority);
        }

        private Vector2 BuildSafestUsefulDestination(
            in SpellAIThreatEvaluation primary,
            SpellAIReaction reaction,
            in EnemyThreatResponseBehaviorV2 behavior,
            Vector2 position,
            Vector2 plannedVelocity)
        {
            candidateDestinations.Clear();
            float moveDistance = reactionMoveDistance * Mathf.Lerp(
                0.65f,
                1.4f,
                behavior.AvoidanceStrength);
            moveDistance += Mathf.Max(0f, -primary.Clearance);
            Vector2 away = SafeDirection(
                primary.AvoidanceDirection,
                position - (Vector2)primary.Geometry.BoundingCenter);
            Vector2 pathDirection = SafeDirection(
                plannedVelocity,
                locomotion.HasDestination
                    ? locomotion.Destination - position
                    : away);
            Vector2 side = Perpendicular(
                primary.TravelDirection.sqrMagnitude > 0.0001f
                    ? primary.TravelDirection
                    : pathDirection);

            AddCandidate(position + away * moveDistance);
            AddCandidate(position + side * moveDistance);
            AddCandidate(position - side * moveDistance);
            AddCandidate(position +
                         (away + side * 0.75f).normalized * moveDistance);
            AddCandidate(position +
                         (away - side * 0.75f).normalized * moveDistance);

            SpellDeliveryGeometry geometry = primary.Geometry;
            float margin = bodyRadius + hazardClearanceMargin;
            if (geometry.Shape == SpellDeliveryGeometryShape.Circle ||
                geometry.Shape == SpellDeliveryGeometryShape.Arc ||
                geometry.Shape == SpellDeliveryGeometryShape.Point)
            {
                Vector2 radial = SafeDirection(
                    position - geometry.Center,
                    away);
                Vector2 tangent = Perpendicular(radial);
                float radius = geometry.Radius + margin;
                AddCandidate(geometry.Center +
                             (radial + tangent).normalized * radius);
                AddCandidate(geometry.Center +
                             (radial - tangent).normalized * radius);
            }
            else if (geometry.Shape == SpellDeliveryGeometryShape.Segment)
            {
                Vector2 segment = SafeDirection(
                    geometry.End - geometry.Start,
                    Vector2.right);
                AddCandidate(geometry.Start - segment * margin + away * margin);
                AddCandidate(geometry.End + segment * margin + away * margin);
            }

            if ((reaction == SpellAIReaction.CloseDistance ||
                 reaction == SpellAIReaction.InterruptCaster) &&
                primary.Caster != null)
            {
                AddCandidate(position + SafeDirection(
                    (Vector2)primary.Caster.transform.position - position,
                    away) * moveDistance);
            }

            ArenaNavigationGrid grid = agent.NavigationGrid;
            Vector2 originalDestination = locomotion.HasDestination
                ? locomotion.Destination
                : position;
            Vector2 best = position;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidateDestinations.Count; i++)
            {
                Vector2 candidate = candidateDestinations[i];
                if (grid != null && grid.IsBuilt)
                {
                    candidate = grid.FindNearestWalkablePosition(candidate);
                }
                if ((candidate - position).sqrMagnitude <= 0.04f)
                    continue;

                float score = ScoreCandidateSafety(
                    candidate,
                    originalDestination,
                    behavior.AvoidanceStrength,
                    reaction,
                    primary);
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = candidate;
            }
            return best;
        }

        private float ScoreCandidateSafety(
            Vector2 candidate,
            Vector2 originalDestination,
            float avoidanceStrength,
            SpellAIReaction reaction,
            in SpellAIThreatEvaluation primary)
        {
            float safety = 0f;
            for (int i = 0; i < relevantThreats.Count; i++)
            {
                SpellAIThreatEvaluation threat = relevantThreats[i];
                float clearance = SpellAIThreatService.SignedClearance(
                    threat.Geometry,
                    candidate,
                    bodyRadius,
                    out _);
                float desired = hazardClearanceMargin + 0.25f;
                float normalized = Mathf.Clamp(
                    clearance / Mathf.Max(0.1f, desired),
                    -2f,
                    2f);
                safety += normalized * Mathf.Max(0.1f, threat.Score);
            }

            float progress = originalDestination != (Vector2)transform.position
                ? -Vector2.Distance(candidate, originalDestination) * 0.08f
                : -Vector2.Distance(candidate, transform.position) * 0.03f;
            float directional = Vector2.Dot(
                SafeDirection(candidate - (Vector2)transform.position,
                    primary.AvoidanceDirection),
                primary.AvoidanceDirection);
            if (reaction == SpellAIReaction.DodgeSideways)
            {
                directional = 1f - Mathf.Abs(Vector2.Dot(
                    SafeDirection(candidate - (Vector2)transform.position,
                        Vector2.right),
                    primary.TravelDirection));
            }
            return safety * Mathf.Lerp(0.6f, 1.6f, avoidanceStrength) +
                   progress * (1f - avoidanceStrength) +
                   directional * 0.2f;
        }

        private SpellAIReaction ChooseReaction(
            in SpellAIThreatEvaluation threat,
            SpellAIReaction available,
            in EnemyThreatResponseBehaviorV2 behavior)
        {
            if (threat.IsInside &&
                Has(available, SpellAIReaction.LeaveArea))
                return SpellAIReaction.LeaveArea;
            if (threat.IsTrap && behavior.CanChargeThroughDanger)
                return SpellAIReaction.None;
            if (Has(available, SpellAIReaction.LeaveArea) &&
                (threat.Geometry.Shape == SpellDeliveryGeometryShape.Circle ||
                 threat.Geometry.Shape == SpellDeliveryGeometryShape.Segment))
                return SpellAIReaction.LeaveArea;
            if (Has(available, SpellAIReaction.DodgeSideways))
                return SpellAIReaction.DodgeSideways;
            if (Has(available, SpellAIReaction.SpreadOut))
                return SpellAIReaction.SpreadOut;
            if (Has(available, SpellAIReaction.InterruptCaster))
                return SpellAIReaction.InterruptCaster;
            if (Has(available, SpellAIReaction.CloseDistance))
                return SpellAIReaction.CloseDistance;
            if (Has(available, SpellAIReaction.SeekCover))
                return SpellAIReaction.SeekCover;
            return SpellAIReaction.None;
        }

        private bool ReactionCapacityReached(EnemyAIV2Profile combatProfile)
        {
            int limit = Mathf.Max(
                0,
                combatProfile.maximumConcurrentThreatReactions);
            if (limit == 0 || agent.Director == null)
                return false;

            int reacting = 0;
            IReadOnlyList<EnemyAgentV2> agents = agent.Director.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 other = agents[i];
                if (other != null && other.ActionRunner != null &&
                    other.ActionRunner.IsBusy &&
                    other.ActionRunner.CurrentKind ==
                        EnemyActionKindV2.EvadeThreat)
                {
                    reacting++;
                }
            }
            return reacting >= limit;
        }

        private bool ShouldInterruptAttack(
            in SpellAIThreatEvaluation threat,
            in EnemyThreatResponseBehaviorV2 behavior,
            bool emergency)
        {
            if (emergency)
                return true;
            float threshold = Mathf.Lerp(
                0.96f,
                0.46f,
                behavior.WillingnessToInterruptAttack);
            return threat.Score >= threshold &&
                   DeterministicRoll(threat.ThreatId + 7919) <=
                   behavior.WillingnessToInterruptAttack;
        }

        private Vector2 ResolvePlannedVelocity(EnemyAIV2Profile combatProfile)
        {
            Vector2 velocity = locomotion.CurrentVelocity;
            if (velocity.sqrMagnitude > 0.01f || !locomotion.HasDestination)
                return velocity;
            Vector2 direction = locomotion.Destination -
                                (Vector2)transform.position;
            return SafeDirection(direction, Vector2.zero) *
                   Mathf.Max(0.1f, combatProfile.moveSpeed);
        }

        private EnemyThreatResponseBehaviorV2 ResolveBehavior()
        {
            return responseProfile != null
                ? responseProfile.Behavior
                : EnemyThreatResponseProfileV2.BuiltIn(fallbackPreset);
        }

        private void ResolveReferences()
        {
            if (agent == null)
                agent = GetComponent<EnemyAgentV2>();
            if (locomotion == null)
                locomotion = GetComponent<EnemyLocomotionV2>();
            if (actionRunner == null)
                actionRunner = GetComponent<EnemyActionRunnerV2>();
        }

        private void PruneThreatMemory()
        {
            staleThreatIds.Clear();
            foreach (KeyValuePair<int, float> pair in noticedAt)
            {
                if (!SpellAIThreatService.IsThreatActive(pair.Key))
                    staleThreatIds.Add(pair.Key);
            }
            for (int i = 0; i < staleThreatIds.Count; i++)
            {
                int id = staleThreatIds[i];
                noticedAt.Remove(id);
                reactionCounts.Remove(id);
                if (activeReactionThreatId == id)
                {
                    activeReactionThreatId = 0;
                    if (actionRunner != null &&
                        actionRunner.IsBusy &&
                        actionRunner.CurrentKind ==
                            EnemyActionKindV2.EvadeThreat)
                    {
                        actionRunner.CancelCurrent(
                            "Delivery threat expired");
                    }
                }
            }
            staleThreatIds.Clear();
        }

        private void AddCandidate(Vector2 candidate)
        {
            for (int i = 0; i < candidateDestinations.Count; i++)
            {
                if ((candidateDestinations[i] - candidate).sqrMagnitude <
                    0.01f)
                {
                    return;
                }
            }
            candidateDestinations.Add(candidate);
        }

        private float DeterministicRoll(int threatId)
        {
            unchecked
            {
                uint value = (uint)(gameObject.GetInstanceID() * 73856093) ^
                             (uint)(threatId * 19349663) ^
                             (uint)(reactionSequence * 83492791);
                value ^= value >> 13;
                value *= 1274126177u;
                return (value & 0x00ffffff) / 16777215f;
            }
        }

        private int BuildReactionOrderId()
        {
            reactionSequence++;
            int value = gameObject.GetInstanceID() ^
                        (reactionSequence * 397);
            return value == int.MinValue
                ? -1
                : -Mathf.Max(1, Mathf.Abs(value));
        }

        private static bool IsCommittedAttack(EnemyActionKindV2 kind)
        {
            return kind == EnemyActionKindV2.AttackPattern ||
                   kind == EnemyActionKindV2.FluidPressure ||
                   kind == EnemyActionKindV2.CastSkill ||
                   kind == EnemyActionKindV2.ApproachAndCastSkill;
        }

        private static bool Has(
            SpellAIReaction mask,
            SpellAIReaction value)
        {
            return (mask & value) != 0;
        }

        private static Vector2 SafeDirection(
            Vector2 value,
            Vector2 fallback)
        {
            if (value.sqrMagnitude > 0.000001f)
                return value.normalized;
            return fallback.sqrMagnitude > 0.000001f
                ? fallback.normalized
                : Vector2.right;
        }

        private static Vector2 Perpendicular(Vector2 direction)
        {
            Vector2 safe = SafeDirection(direction, Vector2.up);
            return new Vector2(-safe.y, safe.x);
        }
    }
}
