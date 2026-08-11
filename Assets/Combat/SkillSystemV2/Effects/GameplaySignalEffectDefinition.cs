using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class GameplaySignalEffectSettings : SpellEffectSettings
    {
        [Tooltip("The signal channel raised for other game systems to hear.")]
        [SerializeField]
        private GameplaySignalDefinition signal;

        [Tooltip("Optional plain-text detail passed to listeners.")]
        [SerializeField]
        private string label;

        [Tooltip("Number passed to listeners, such as strength, score, or count.")]
        [SerializeField]
        private float value = 1f;

        [Tooltip("Multiply Value by the effect's potency scale before raising the signal.")]
        [SerializeField]
        private bool scaleValueWithPotency = true;

        public GameplaySignalDefinition Signal => signal;
        public string Label => label;
        public float Value => value;
        public bool ScaleValueWithPotency => scaleValueWithPotency;

        public GameplaySignalEffectSettings()
        {
        }

        public GameplaySignalEffectSettings(
            GameplaySignalDefinition signalDefinition,
            string eventLabel,
            float eventValue,
            bool shouldScaleWithPotency = true)
        {
            signal = signalDefinition;
            label = eventLabel;
            value = eventValue;
            scaleValueWithPotency = shouldScaleWithPotency;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_GameplaySignal",
        menuName = "Project Eri/Skill System V2/Effects/Raise Gameplay Signal")]
    public sealed class GameplaySignalEffectDefinition : EffectDefinition
    {
        [Tooltip("Default signal channel copied into a spell when this effect is equipped.")]
        [SerializeField]
        private GameplaySignalDefinition signal;

        [Tooltip("Default optional text passed to signal listeners.")]
        [SerializeField]
        private string label;

        [Tooltip("Default number passed to signal listeners.")]
        [SerializeField]
        private float value = 1f;

        [Tooltip("Default choice for whether potency changes the signal value.")]
        [SerializeField]
        private bool scaleValueWithPotency = true;

        public override Type SettingsType =>
            typeof(GameplaySignalEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new GameplaySignalEffectSettings(
                signal,
                label,
                value,
                scaleValueWithPotency);
        }

        public override bool CanApplyWithoutRecipient(
            SpellEffectSettings settings)
        {
            return true;
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            GameplaySignalEffectSettings resolved =
                settings as GameplaySignalEffectSettings ??
                (GameplaySignalEffectSettings)CreateDefaultSettings();
            if (resolved.Signal == null)
                return false;

            float resolvedValue = resolved.ScaleValueWithPotency
                ? resolved.Value * context.PotencyScale
                : resolved.Value;
            resolved.Signal.Raise(new GameplaySignalEvent(
                resolved.Signal,
                context,
                resolved.Label,
                resolvedValue));
            return true;
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
            GameplaySignalEffectSettings resolved =
                settings as GameplaySignalEffectSettings ??
                (GameplaySignalEffectSettings)CreateDefaultSettings();
            if (resolved.Signal == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Gameplay Signal effect '{DisplayName}' needs a signal asset."));
            }
        }
    }
}
