using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class HealingEffectSettings : SpellEffectSettings
    {
        [Tooltip("Base health restored when this effect succeeds.")]
        [SerializeField, Min(0f)]
        private float amount = 10f;

        [Tooltip("Allow this healing effect to return a defeated character to positive health.")]
        [SerializeField]
        private bool allowRevive;

        public float Amount => Mathf.Max(0f, amount);
        public bool AllowRevive => allowRevive;

        public HealingEffectSettings()
        {
        }

        public HealingEffectSettings(float healingAmount, bool canRevive = false)
        {
            amount = Mathf.Max(0f, healingAmount);
            allowRevive = canRevive;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_Healing",
        menuName = "Project Eri/Skill System V2/Effects/Healing")]
    public sealed class HealingEffectDefinition : EffectDefinition
    {
        [Tooltip("Default healing amount copied into a spell when this effect is equipped.")]
        [SerializeField, Min(0f)]
        private float amount = 10f;

        [Tooltip("Default choice for whether this healing can revive defeated targets.")]
        [SerializeField]
        private bool allowRevive;

        public float Amount => Mathf.Max(0f, amount);

        public override Type SettingsType => typeof(HealingEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new HealingEffectSettings(amount, allowRevive);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            HealingEffectSettings resolved =
                settings as HealingEffectSettings ??
                (HealingEffectSettings)CreateDefaultSettings();
            ISpellHealingReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellHealingReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellHealingRequest(
                context,
                resolved.Amount * context.PotencyScale,
                resolved.AllowRevive);
            return receiver.TryReceiveHealing(request, out _);
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
            HealingEffectSettings resolved =
                settings as HealingEffectSettings ??
                (HealingEffectSettings)CreateDefaultSettings();
            if (resolved.Amount <= 0f)
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
