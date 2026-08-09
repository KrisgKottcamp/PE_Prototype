using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_Damage",
        menuName = "Project Eri/Skill System V2/Effects/Damage")]
    public sealed class DamageEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private DamageTypeDefinition damageType;

        [SerializeField, Min(0f)]
        private float amount = 10f;

        [SerializeField]
        private bool ignoreInvulnerability;

        public float Amount => Mathf.Max(0f, amount);
        public DamageTypeDefinition DamageType => damageType;

        public override bool Apply(in SpellEffectContext context)
        {
            ISpellDamageReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellDamageReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellDamageRequest(
                context,
                damageType,
                Amount * context.PotencyScale,
                ignoreInvulnerability);
            return receiver.TryReceiveDamage(request, out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (Amount <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Damage effect '{DisplayName}' has zero damage."));
            }
        }

        private void OnValidate()
        {
            amount = Mathf.Max(0f, amount);
        }
    }
}
