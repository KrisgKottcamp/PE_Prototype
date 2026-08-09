using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_Healing",
        menuName = "Project Eri/Skill System V2/Effects/Healing")]
    public sealed class HealingEffectDefinition : EffectDefinition
    {
        [SerializeField, Min(0f)]
        private float amount = 10f;

        [SerializeField]
        private bool allowRevive;

        public float Amount => Mathf.Max(0f, amount);

        public override bool Apply(in SpellEffectContext context)
        {
            ISpellHealingReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellHealingReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellHealingRequest(
                context,
                Amount * context.PotencyScale,
                allowRevive);
            return receiver.TryReceiveHealing(request, out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (Amount <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Healing effect '{DisplayName}' has zero healing."));
            }
        }

        private void OnValidate()
        {
            amount = Mathf.Max(0f, amount);
        }
    }
}
