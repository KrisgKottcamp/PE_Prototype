using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class RemoveStatusEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        private StatusDefinition status;

        [Tooltip("Zero removes the entire status. A positive value removes that many stacks.")]
        [SerializeField, Min(0)]
        private int stacksToRemove;

        public StatusDefinition Status => status;
        public int StacksToRemove => Mathf.Max(0, stacksToRemove);

        public RemoveStatusEffectSettings()
        {
        }

        public RemoveStatusEffectSettings(
            StatusDefinition statusDefinition,
            int stackCount = 0)
        {
            status = statusDefinition;
            stacksToRemove = Mathf.Max(0, stackCount);
        }
    }

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

        public override Type SettingsType =>
            typeof(RemoveStatusEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new RemoveStatusEffectSettings(status, stacksToRemove);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            RemoveStatusEffectSettings resolved =
                settings as RemoveStatusEffectSettings ??
                (RemoveStatusEffectSettings)CreateDefaultSettings();
            if (resolved.Status == null)
                return false;

            ISpellStatusReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellStatusReceiver>(
                    context.Target);
            return receiver != null &&
                   receiver.TryRemoveStatus(
                       resolved.Status,
                       resolved.StacksToRemove,
                       out _);
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
            RemoveStatusEffectSettings resolved =
                settings as RemoveStatusEffectSettings ??
                (RemoveStatusEffectSettings)CreateDefaultSettings();
            if (resolved.Status == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Remove Status effect '{DisplayName}' needs a status definition."));
            }
            else if (string.IsNullOrWhiteSpace(resolved.Status.StableId))
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Status '{resolved.Status.DisplayName}' needs a stable ID."));
            }
        }

        private void OnValidate()
        {
            stacksToRemove = Mathf.Max(0, stacksToRemove);
        }
    }
}
