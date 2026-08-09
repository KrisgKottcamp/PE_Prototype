using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum TriggeredSpellRunnerSource
    {
        OriginalCaster,
        EffectTarget
    }

    public enum TriggeredSpellTargetSource
    {
        EffectTarget,
        TriggeredCaster,
        None
    }

    [CreateAssetMenu(
        fileName = "Effect_TriggerSpell",
        menuName = "Project Eri/Skill System V2/Effects/Trigger Secondary Spell")]
    public sealed class TriggerSpellEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private SpellDefinition secondarySpell;

        [SerializeField]
        private TriggeredSpellRunnerSource runnerSource =
            TriggeredSpellRunnerSource.OriginalCaster;

        [SerializeField]
        private TriggeredSpellTargetSource targetSource =
            TriggeredSpellTargetSource.EffectTarget;

        [SerializeField]
        private bool useHitPointAsTargetPoint = true;

        public override bool Apply(in SpellEffectContext context)
        {
            if (secondarySpell == null)
                return false;

            GameObject runnerObject = runnerSource ==
                                      TriggeredSpellRunnerSource.EffectTarget
                ? context.Target
                : context.Cast.Caster;
            SpellRunner runner = runnerObject != null
                ? runnerObject.GetComponentInParent<SpellRunner>()
                : null;
            if (runner == null)
                return false;

            GameObject selectedTarget;
            switch (targetSource)
            {
                case TriggeredSpellTargetSource.TriggeredCaster:
                    selectedTarget = runner.gameObject;
                    break;
                case TriggeredSpellTargetSource.None:
                    selectedTarget = null;
                    break;
                default:
                    selectedTarget = context.Target;
                    break;
            }

            Vector2 origin = runner.transform.position;
            Vector2 targetPoint = useHitPointAsTargetPoint
                ? context.HitPoint
                : context.Cast.HasTargetPoint
                    ? context.Cast.TargetPoint
                    : context.HitPoint;
            Vector2 direction = selectedTarget != null
                ? (Vector2)selectedTarget.transform.position - origin
                : targetPoint - origin;

            if (direction.sqrMagnitude <= 0.000001f &&
                context.Cast.HasAimDirection)
            {
                direction = context.Cast.AimDirection;
            }

            var childContext = new CastContext(
                runner.gameObject,
                CombatTeamMember.ResolveTeam(
                    runner.gameObject,
                    context.Cast.CasterTeam),
                origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                targetPoint,
                useHitPointAsTargetPoint || context.Cast.HasTargetPoint,
                selectedTarget,
                context.Cast.ChainBudget,
                context.Cast.ChainDepth + 1);

            return runner.QueueTriggeredCast(
                secondarySpell,
                childContext,
                out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (secondarySpell == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Trigger Spell effect '{DisplayName}' needs a secondary spell."));
            }
        }
    }
}
