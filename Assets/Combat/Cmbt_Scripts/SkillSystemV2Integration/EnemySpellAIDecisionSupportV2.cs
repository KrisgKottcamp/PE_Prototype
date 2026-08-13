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
    public sealed class EnemySpellAIDecisionSupportV2 : MonoBehaviour
    {
        [SerializeField] private SpellLoadout loadout;
        [SerializeField] private SpellRunner spellRunner;
        [SerializeField] private string debugBestSpell = "None";
        [SerializeField] private float debugBestScore;
        [SerializeField] private string debugRejection = "Not evaluated";

        public SpellDefinition BasicAttack =>
            loadout != null ? loadout.BasicAttack : null;

        private void Awake()
        {
            if (loadout == null)
                loadout = GetComponent<SpellLoadout>();
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();
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
                bool built = TryBuildCandidateContext(
                        candidate,
                        preferredTarget,
                        preferredGroundPoint,
                        origin,
                        out CastContext requested);
                CastContext resolved = default;
                bool validated = built && candidate.TryResolveContext(
                        requested,
                        out resolved,
                        out rejection);
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
                if (candidateScore <= score)
                    continue;

                score = candidateScore;
                spell = candidate;
                cast = resolved;
            }

            debugBestSpell = spell != null ? spell.DisplayName : "None";
            debugBestScore = float.IsNegativeInfinity(score) ? 0f : score;
            if (spell != null)
                debugRejection = "Candidate validated";
            return spell != null;
        }

        private bool TryBuildCandidateContext(
            SpellDefinition spell,
            GameObject target,
            Vector2 groundPoint,
            Vector2 origin,
            out CastContext context)
        {
            CastTargetingRequirement requirements =
                spell.Delivery.TargetingRequirement;
            if ((requirements &
                 CastTargetingRequirement.MultipleTargetPoints) != 0)
            {
                context = default;
                return false;
            }

            bool needsTarget = (requirements &
                CastTargetingRequirement.SelectedTarget) != 0;
            if (needsTarget && target == null)
            {
                context = default;
                return false;
            }

            Vector2 targetPoint = target != null && needsTarget
                ? target.transform.position
                : groundPoint;
            Vector2 direction = targetPoint - origin;
            context = new CastContext(
                gameObject,
                CombatTeamMember.ResolveTeam(gameObject),
                origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                targetPoint,
                (requirements & CastTargetingRequirement.TargetPoint) != 0,
                needsTarget ? target : null);
            return true;
        }
    }
}
