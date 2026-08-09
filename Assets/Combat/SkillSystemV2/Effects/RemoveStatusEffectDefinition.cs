using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_RemoveStatus",
        menuName = "Project Eri/Skill System V2/Effects/Remove Status")]
    public sealed class RemoveStatusEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private StatusDefinition status;

        [Tooltip("Zero removes the entire status. A positive value removes that many stacks.")]
        [SerializeField, Min(0)]
        private int stacksToRemove;

        public override bool Apply(in SpellEffectContext context)
        {
            if (status == null)
                return false;

            ISpellStatusReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellStatusReceiver>(
                    context.Target);
            return receiver != null &&
                   receiver.TryRemoveStatus(
                       status,
                       Mathf.Max(0, stacksToRemove),
                       out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (status == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Remove Status effect '{DisplayName}' needs a status definition."));
            }
            else if (string.IsNullOrWhiteSpace(status.StableId))
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Status '{status.DisplayName}' needs a stable ID."));
            }
        }

        private void OnValidate()
        {
            stacksToRemove = Mathf.Max(0, stacksToRemove);
        }
    }
}
