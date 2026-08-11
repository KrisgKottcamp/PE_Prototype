using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class ApplyStatusEffectSettings : SpellEffectSettings
    {
        [Tooltip("The reusable Status Definition placed on the target.")]
        [SerializeField]
        private StatusDefinition status;

        [Tooltip("Zero uses the Status Definition's default duration.")]
        [SerializeField, Min(0f)]
        private float durationOverride;

        [Tooltip("How many stacks are applied at once.")]
        [SerializeField, Min(1)]
        private int stacks = 1;

        public StatusDefinition Status => status;
        public float DurationOverride => Mathf.Max(0f, durationOverride);
        public int Stacks => Mathf.Max(1, stacks);

        public ApplyStatusEffectSettings()
        {
        }

        public ApplyStatusEffectSettings(
            StatusDefinition statusDefinition,
            float duration = 0f,
            int stackCount = 1)
        {
            status = statusDefinition;
            durationOverride = Mathf.Max(0f, duration);
            stacks = Mathf.Max(1, stackCount);
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_ApplyStatus",
        menuName = "Project Eri/Skill System V2/Effects/Apply Status")]
    public sealed class ApplyStatusEffectDefinition : EffectDefinition
    {
        [Tooltip("Default status copied into a spell when this effect is equipped.")]
        [SerializeField]
        private StatusDefinition status;

        [Tooltip("Zero uses the Status Definition's default duration.")]
        [SerializeField, Min(0f)]
        private float durationOverride;

        [Tooltip("Default number of stacks applied at once.")]
        [SerializeField, Min(1)]
        private int stacks = 1;

        public override Type SettingsType =>
            typeof(ApplyStatusEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new ApplyStatusEffectSettings(
                status,
                durationOverride,
                stacks);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            ApplyStatusEffectSettings resolved =
                settings as ApplyStatusEffectSettings ??
                (ApplyStatusEffectSettings)CreateDefaultSettings();
            if (resolved.Status == null)
                return false;

            ISpellStatusReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellStatusReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellStatusApplyRequest(
                context,
                resolved.Status,
                resolved.DurationOverride,
                resolved.Stacks);
            return receiver.TryApplyStatus(request, out _);
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
            ApplyStatusEffectSettings resolved =
                settings as ApplyStatusEffectSettings ??
                (ApplyStatusEffectSettings)CreateDefaultSettings();
            if (resolved.Status == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Apply Status effect '{DisplayName}' needs a status definition."));
            }
            else
            {
                resolved.Status.CollectValidationIssues(issues);
            }
        }

        private void OnValidate()
        {
            durationOverride = Mathf.Max(0f, durationOverride);
            stacks = Mathf.Max(1, stacks);
        }
    }
}
