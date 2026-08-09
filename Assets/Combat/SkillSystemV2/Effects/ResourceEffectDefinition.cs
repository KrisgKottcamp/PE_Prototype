using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_Resource",
        menuName = "Project Eri/Skill System V2/Effects/Resource or AP")]
    public sealed class ResourceEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private GameplayResourceDefinition resource;

        [SerializeField]
        private SpellResourceOperation operation = SpellResourceOperation.Add;

        [SerializeField, Min(0f)]
        private float amount = 10f;

        [SerializeField]
        private bool allowOverflow;

        public override bool Apply(in SpellEffectContext context)
        {
            if (resource == null)
                return false;

            ISpellResourceReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellResourceReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellResourceChangeRequest(
                context,
                resource,
                operation,
                Mathf.Max(0f, amount) * context.PotencyScale,
                allowOverflow);
            return receiver.TryChangeResource(request, out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (resource == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Resource effect '{DisplayName}' needs a resource definition."));
            }

            if (amount <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Resource effect '{DisplayName}' has a zero amount."));
            }
        }

        private void OnValidate()
        {
            amount = Mathf.Max(0f, amount);
        }
    }
}
