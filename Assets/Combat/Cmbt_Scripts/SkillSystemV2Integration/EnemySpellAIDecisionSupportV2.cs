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
        [SerializeField] private string debugBestSpell = "None";
        [SerializeField] private float debugBestScore;
        [SerializeField] private string debugTargetSolution = "Not evaluated";
        [SerializeField] private string debugRejection = "Not evaluated";
        [SerializeField] private float debugTacticalMultiplier = 1f;
        [SerializeField] private int debugRememberedActiveInstances;

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
            out SpellDefinition spell,
            out CastContext cast,
            out float score)
        {
            spell = null;
            cast = default;
            score = float.NegativeInfinity;
            debugTacticalMultiplier = 1f;
            debugRememberedActiveInstances =
                SpellAITacticalMemory.ActiveInstanceCount;
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
                CastContext resolved = default;
                float targetingScore = 0f;
                bool validated = false;
                if (targetingSolver != null)
                {
                    validated = targetingSolver.TryResolveBestContext(
                        candidate,
                        preferredTarget,
                        preferredGroundPoint,
                        out resolved,
                        out targetingScore,
                        out rejection);
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
                        : preferredTarget != null
                            ? (Vector2)preferredTarget.transform.position
                            : origin);
                var decision = new SpellAIDecisionContext(
                    distance,
                    usefulTargetCount,
                    casterHealthFraction,
                    targetHealthFraction,
                    incomingDanger,
                    commitmentCost,
                    activeComboTags);
                float candidateScore =
                    SpellAIDecisionUtility.Score(candidate, decision);
                if (!float.IsNegativeInfinity(candidateScore))
                {
                    candidateScore *= Mathf.Max(0.1f, targetingScore) *
                                      tacticalMultiplier;
                }
                if (candidateScore <= score)
                    continue;

                score = candidateScore;
                spell = candidate;
                cast = resolved;
                debugTacticalMultiplier = tacticalMultiplier;
                debugTargetSolution = targetingSolver.DebugSolution;
            }

            debugBestSpell = spell != null ? spell.DisplayName : "None";
            debugBestScore = float.IsNegativeInfinity(score) ? 0f : score;
            if (spell != null)
                debugRejection = "Candidate validated";
            return spell != null;
        }
    }
}
