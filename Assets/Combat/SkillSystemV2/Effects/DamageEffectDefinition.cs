using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class DamageEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        private DamageTypeDefinition damageType;

        [SerializeField, Min(0f)]
        private float amount = 10f;

        [SerializeField]
        private bool ignoreInvulnerability;

        public DamageTypeDefinition DamageType => damageType;
        public float Amount => Mathf.Max(0f, amount);
        public bool IgnoreInvulnerability => ignoreInvulnerability;

        public DamageEffectSettings()
        {
        }

        public DamageEffectSettings(
            float damageAmount,
            DamageTypeDefinition type = null,
            bool shouldIgnoreInvulnerability = false)
        {
            amount = Mathf.Max(0f, damageAmount);
            damageType = type;
            ignoreInvulnerability = shouldIgnoreInvulnerability;
        }
    }

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

        public override Type SettingsType => typeof(DamageEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new DamageEffectSettings(
                amount,
                damageType,
                ignoreInvulnerability);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            DamageEffectSettings resolved =
                settings as DamageEffectSettings ??
                (DamageEffectSettings)CreateDefaultSettings();
            ISpellDamageReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellDamageReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellDamageRequest(
                context,
                resolved.DamageType,
                resolved.Amount * context.PotencyScale,
                resolved.IgnoreInvulnerability);
            return receiver.TryReceiveDamage(request, out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, CreateDefaultSettings());
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            DamageEffectSettings resolved =
                settings as DamageEffectSettings ??
                (DamageEffectSettings)CreateDefaultSettings();
            if (resolved.Amount <= 0f)
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
