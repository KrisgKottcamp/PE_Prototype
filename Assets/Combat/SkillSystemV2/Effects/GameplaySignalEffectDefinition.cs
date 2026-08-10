using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class GameplaySignalEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        private GameplaySignalDefinition signal;

        [SerializeField]
        private string label;

        [SerializeField]
        private float value = 1f;

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
        [SerializeField]
        private GameplaySignalDefinition signal;

        [SerializeField]
        private string label;

        [SerializeField]
        private float value = 1f;

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
