using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class DamageEffectSettings : SpellEffectSettings
    {
        [Tooltip("Optional damage category used by resistances, reactions, and other game systems.")]
        [SerializeField]
        private DamageTypeDefinition damageType;

        [Tooltip("Base health removed when this effect succeeds.")]
        [SerializeField, Min(0f)]
        private float amount = 10f;

        [Tooltip("Deal damage even while the target reports that it is invulnerable.")]
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
        [Tooltip("Default Damage Type copied into a spell when this effect is equipped.")]
        [SerializeField]
        private DamageTypeDefinition damageType;

        [Tooltip("Default damage amount copied into a spell's inline settings.")]
        [SerializeField, Min(0f)]
        private float amount = 10f;

        [Tooltip("Default choice for whether this damage bypasses invulnerability.")]
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

            float dealtMultiplier = SpellStatModifierUtility.Evaluate(
                context.Cast.Caster,
                SpellActorStat.DamageDealt,
                1f);
            float receivedMultiplier = SpellStatModifierUtility.Evaluate(
                context.Target,
                SpellActorStat.DamageReceived,
                1f);
            var request = new SpellDamageRequest(
                context,
                resolved.DamageType,
                resolved.Amount * context.PotencyScale * dealtMultiplier *
                receivedMultiplier,
                resolved.IgnoreInvulnerability);
            return receiver.TryReceiveDamage(request, out _);
        }

        public override string DescribeApplicationFailure(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            DamageEffectSettings resolved =
                settings as DamageEffectSettings ??
                (DamageEffectSettings)CreateDefaultSettings();
            if (SpellEffectReceiverResolver.Find<ISpellDamageReceiver>(
                    context.Target) == null)
            {
                return "The target has no ISpellDamageReceiver. Add the " +
                       "appropriate player or enemy health adapter.";
            }

            if (resolved.Amount * context.PotencyScale <= 0f)
                return "The resolved damage amount is zero.";

            return "The damage receiver rejected the request. Check " +
                   "invulnerability, team rules, and health state.";
        }

        public override bool DescribesDamageType(
            SpellEffectSettings settings,
            DamageTypeDefinition queriedType)
        {
            DamageEffectSettings resolved =
                settings as DamageEffectSettings ??
                (DamageEffectSettings)CreateDefaultSettings();
            return resolved.DamageType == queriedType;
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
