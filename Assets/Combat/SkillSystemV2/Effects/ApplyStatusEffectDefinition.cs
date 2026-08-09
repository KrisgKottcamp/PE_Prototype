using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_ApplyStatus",
        menuName = "Project Eri/Skill System V2/Effects/Apply Status")]
    public sealed class ApplyStatusEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private StatusDefinition status;

        [Tooltip("Zero uses the Status Definition's default duration.")]
        [SerializeField, Min(0f)]
        private float durationOverride;

        [SerializeField, Min(1)]
        private int stacks = 1;

        public override bool Apply(in SpellEffectContext context)
        {
            if (status == null)
                return false;

            ISpellStatusReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellStatusReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellStatusApplyRequest(
                context,
                status,
                durationOverride,
                Mathf.Max(1, stacks));
            return receiver.TryApplyStatus(request, out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (status == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Apply Status effect '{DisplayName}' needs a status definition."));
            }
            else
            {
                status.CollectValidationIssues(issues);
            }
        }

        private void OnValidate()
        {
            durationOverride = Mathf.Max(0f, durationOverride);
            stacks = Mathf.Max(1, stacks);
        }
    }
}
