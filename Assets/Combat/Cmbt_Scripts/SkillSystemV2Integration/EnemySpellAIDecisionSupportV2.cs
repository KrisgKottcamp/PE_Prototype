using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    /// <summary>
    /// Compatibility seam between the current squad/action AI and Skill V2.
    /// It does not replace basic attacks or issue orders by itself. The future
    /// EnemySkillAI action asks this component for a validated spell candidate,
    /// then gives that cast to SpellRunner through its own action order.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpellLoadout))]
    [RequireComponent(typeof(SpellRunner))]
    [RequireComponent(typeof(EnemySpellTargetingSolverV2))]
    public sealed class EnemySpellAIDecisionSupportV2 : MonoBehaviour
    {
        [SerializeField] private SpellLoadout loadout;
        [SerializeField] private SpellRunner spellRunner;
        [SerializeField] private EnemySpellTargetingSolverV2 targetingSolver;
        [SerializeField] private EnemyAgentV2 agent;
        [SerializeField] private string debugBestSpell = "None";
        [SerializeField] private float debugBestScore;
        [SerializeField] private string debugTargetSolution = "Not evaluated";
        [SerializeField] private string debugRejection = "Not evaluated";
        [SerializeField] private float debugTacticalMultiplier = 1f;
        [SerializeField] private int debugRememberedActiveInstances;
        [SerializeField] private string debugComboPlan = "Not evaluated";
        [SerializeField] private int debugComboOpportunities;
        [SerializeField] private string debugSupportTarget = "None";
        [SerializeField] private float debugSupportTargetHealth = 1f;
        [SerializeField] private string debugApproachPlan = "None";

        public SpellDefinition BasicAttack =>
            loadout != null ? loadout.BasicAttack : null;

        private void Awake()
        {
            if (loadout == null)
                loadout = GetComponent<SpellLoadout>();
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();
            if (targetingSolver == null)
                targetingSolver = GetComponent<EnemySpellTargetingSolverV2>();
            if (agent == null)
                agent = GetComponent<EnemyAgentV2>();
        }

        public bool TryChooseSkill(
            GameObject preferredTarget,
            Vector2 preferredGroundPoint,
            int usefulTargetCount,
            float casterHealthFraction,
            float targetHealthFraction,
            float incomingDanger,
            float commitmentCost,
            ISet<string> activeComboTags,
            ISet<string> squadConsumerTags,
            bool enableComboPlanning,
            out SpellDefinition spell,
            out CastContext cast,
            out float score,
            out SpellAIComboPlan comboPlan,
            out GameObject chosenTarget,
            out bool approachBeforeCast)
        {
            spell = null;
            cast = default;
            score = float.NegativeInfinity;
            comboPlan = default;
            chosenTarget = null;
            approachBeforeCast = false;
            debugTacticalMultiplier = 1f;
            debugRememberedActiveInstances =
                SpellAITacticalMemory.ActiveInstanceCount;
            debugComboOpportunities =
                SpellAIComboCoordinator.ActiveOpportunityCount;
            debugComboPlan = enableComboPlanning
                ? "No matching plan"
                : "Disabled by EnemyAIV2Profile";
            debugSupportTarget = "None";
            debugSupportTargetHealth = 1f;
            debugApproachPlan = "None";
            if (loadout == null)
            {
                debugRejection = "Missing SpellLoadout";
                return false;
            }

            IReadOnlyList<SpellDefinition> skills = loadout.EquippedSkills;
            Vector2 origin = transform.position;
            for (int i = 0; i < skills.Count; i++)
            {
                SpellDefinition candidate = skills[i];
                if (candidate == null || candidate.Delivery == null)
                    continue;
                string rejection = string.Empty;
                GameObject preferenceTarget = preferredTarget;
                float candidateTargetHealth = targetHealthFraction;
                bool usesAllyPreference = IsAllyPreference(candidate);
                if (usesAllyPreference && !TryFindSupportTarget(
                        candidate,
                        out preferenceTarget,
                        out candidateTargetHealth,
                        out rejection))
                {
                    debugRejection =
                        $"{candidate.DisplayName}: {rejection}";
                    continue;
                }

                SpellAIComboPlan candidateComboPlan = default;
                if (enableComboPlanning &&
                    !SpellAIComboCoordinator.TryEvaluateSpell(
                        candidate,
                        gameObject,
                        preferredGroundPoint,
                        squadConsumerTags,
                        out candidateComboPlan,
                        out string comboRejection))
                {
                    debugRejection =
                        $"{candidate.DisplayName}: {comboRejection}";
                    continue;
                }

                Vector2 candidateGroundPoint =
                    candidateComboPlan.HasOpportunity
                        ? candidateComboPlan.TargetPoint
                        : preferredGroundPoint;
                GameObject candidatePreferredTarget =
                    candidateComboPlan.TargetActor != null
                        ? candidateComboPlan.TargetActor
                        : preferenceTarget;
                float actorDistance = candidatePreferredTarget != null
                    ? Vector2.Distance(
                        origin,
                        candidatePreferredTarget.transform.position)
                    : 0f;
                bool candidateApproach =
                    candidate.AIAffordance.MoveIntoRangeBeforeCasting &&
                    candidatePreferredTarget != null &&
                    actorDistance >
                        candidate.AIAffordance.RequiredAICastRange;
                float maximumApproach =
                    candidate.AIAffordance.MaximumAIApproachDistance;
                if (candidateApproach && maximumApproach > 0f &&
                    actorDistance > maximumApproach)
                {
                    debugRejection =
                        $"{candidate.DisplayName}: ally is " +
                        $"{actorDistance:0.00} units away, beyond the " +
                        $"{maximumApproach:0.00}-unit approach limit";
                    continue;
                }
                CastContext resolved = default;
                float targetingScore = 0f;
                bool validated = false;
                if (targetingSolver != null)
                {
                    validated = targetingSolver.TryResolveBestContext(
                        candidate,
                        candidatePreferredTarget,
                        candidateGroundPoint,
                        out resolved,
                        out targetingScore,
                        out rejection,
                        preferFallbackGroundPoint:
                            candidateComboPlan.HasOpportunity);
                }
                else
                {
                    rejection = "Missing EnemySpellTargetingSolverV2";
                }
                if (validated && spellRunner != null)
                {
                    CastContext contextToCheck = resolved;
                    validated = spellRunner.CanCast(
                        candidate,
                        contextToCheck,
                        out CastContext castableContext,
                        out SpellCastFailure castFailure);
                    if (validated)
                        resolved = castableContext;
                    if (!validated)
                        rejection = castFailure.ToString();
                }
                if (!validated)
                {
                    debugRejection = string.IsNullOrWhiteSpace(rejection)
                        ? $"{candidate.DisplayName}: no compatible target solution"
                        : $"{candidate.DisplayName}: {rejection}";
                    continue;
                }

                if (!SpellAITacticalMemory.TryEvaluate(
                        candidate,
                        gameObject,
                        resolved,
                        out float tacticalMultiplier,
                        out string tacticalRejection))
                {
                    debugRejection =
                        $"{candidate.DisplayName}: {tacticalRejection}";
                    continue;
                }

                float distance = Vector2.Distance(
                    origin,
                    resolved.HasTargetPoint
                        ? resolved.TargetPoint
                        : candidatePreferredTarget != null
                            ? (Vector2)candidatePreferredTarget.transform.position
                            : origin);
                ISet<string> candidateActiveTags = BuildActiveComboTags(
                    activeComboTags,
                    candidateComboPlan.MatchedTags);
                var decision = new SpellAIDecisionContext(
                    distance,
                    usefulTargetCount,
                    casterHealthFraction,
                    candidateTargetHealth,
                    incomingDanger,
                    commitmentCost,
                    candidateActiveTags,
                    candidateApproach);
                float candidateScore =
                    SpellAIDecisionUtility.Score(candidate, decision);
                if (!float.IsNegativeInfinity(candidateScore))
                {
                    candidateScore *= Mathf.Max(0.1f, targetingScore) *
                                      tacticalMultiplier;
                    if (candidateComboPlan.NeedsReservation)
                    {
                        candidateScore *= Mathf.Max(
                            0f,
                            candidateComboPlan.UtilityMultiplier);
                    }
                }
                if (candidateScore <= score)
                    continue;

                score = candidateScore;
                spell = candidate;
                cast = resolved;
                comboPlan = candidateComboPlan;
                chosenTarget = candidatePreferredTarget;
                approachBeforeCast = candidateApproach;
                debugTacticalMultiplier = tacticalMultiplier;
                debugTargetSolution = targetingSolver.DebugSolution;
                if (usesAllyPreference)
                {
                    debugSupportTarget = candidatePreferredTarget != null
                        ? candidatePreferredTarget.name
                        : "None";
                    debugSupportTargetHealth = candidateTargetHealth;
                    debugApproachPlan = candidateApproach
                        ? $"Approach to " +
                          $"{candidate.AIAffordance.RequiredAICastRange:0.00}"
                        : "Already in cast range";
                }
            }

            debugBestSpell = spell != null ? spell.DisplayName : "None";
            debugBestScore = float.IsNegativeInfinity(score) ? 0f : score;
            if (spell != null)
            {
                debugRejection = "Candidate validated";
                debugComboPlan = comboPlan.NeedsReservation
                    ? comboPlan.Description
                    : "Spell has no active combo role";
            }
            return spell != null;
        }

        private bool TryFindSupportTarget(
            SpellDefinition spell,
            out GameObject target,
            out float healthFraction,
            out string rejection)
        {
            target = null;
            healthFraction = 1f;
            rejection = "No living wounded ally is available";
            if (agent == null)
                agent = GetComponent<EnemyAgentV2>();
            if (agent == null || agent.Director == null)
            {
                rejection = "Enemy has no squad director for ally targeting";
                return false;
            }

            IReadOnlyList<EnemyAgentV2> allies = agent.Director.Agents;
            float bestHealth = float.PositiveInfinity;
            float bestDistance = float.PositiveInfinity;
            Vector2 origin = transform.position;
            for (int i = 0; i < allies.Count; i++)
            {
                EnemyAgentV2 ally = allies[i];
                if (ally == null || ally == agent || !ally.IsAlive ||
                    !ally.gameObject.activeInHierarchy)
                {
                    continue;
                }

                EnemyHealth health = ally.GetComponent<EnemyHealth>();
                if (health == null || health.MaxHP <= 0 ||
                    health.CurrentHP <= 0 ||
                    health.CurrentHP >= health.MaxHP)
                {
                    continue;
                }

                CastContext probe = CastContext.ForTarget(
                    gameObject,
                    origin,
                    ally.gameObject);
                if (!spell.TargetFilter.IsValid(
                        probe,
                        ally.gameObject,
                        out _))
                {
                    continue;
                }

                float fraction = health.CurrentHP /
                    (float)Mathf.Max(1, health.MaxHP);
                float distance = Vector2.Distance(
                    origin,
                    ally.transform.position);
                if (fraction > bestHealth + 0.0001f ||
                    (Mathf.Abs(fraction - bestHealth) <= 0.0001f &&
                     distance >= bestDistance))
                {
                    continue;
                }

                target = ally.gameObject;
                healthFraction = fraction;
                bestHealth = fraction;
                bestDistance = distance;
            }

            return target != null;
        }

        private static bool IsAllyPreference(SpellDefinition spell)
        {
            if (spell == null || spell.AIAffordance == null)
                return false;
            SpellAITargetPreference preference =
                spell.AIAffordance.TargetPreference;
            return preference == SpellAITargetPreference.LowestHealthAlly ||
                   preference == SpellAITargetPreference.AllyCluster;
        }

        private static ISet<string> BuildActiveComboTags(
            ISet<string> inherited,
            IReadOnlyList<string> matched)
        {
            if ((inherited == null || inherited.Count == 0) &&
                (matched == null || matched.Count == 0))
            {
                return null;
            }

            var result = new HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);
            if (inherited != null)
            {
                foreach (string tag in inherited)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                        result.Add(tag);
                }
            }
            if (matched != null)
            {
                for (int i = 0; i < matched.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(matched[i]))
                        result.Add(matched[i]);
                }
            }
            return result;
        }
    }
}
