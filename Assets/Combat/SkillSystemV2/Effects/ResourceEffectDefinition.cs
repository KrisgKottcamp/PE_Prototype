using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class ResourceEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        private GameplayResourceDefinition resource;

        [SerializeField]
        private SpellResourceOperation operation = SpellResourceOperation.Add;

        [SerializeField, Min(0f)]
        private float amount = 10f;

        [SerializeField]
        private bool allowOverflow;

        public GameplayResourceDefinition Resource => resource;
        public SpellResourceOperation Operation => operation;
        public float Amount => Mathf.Max(0f, amount);
        public bool AllowOverflow => allowOverflow;

        public ResourceEffectSettings()
        {
        }

        public ResourceEffectSettings(
            GameplayResourceDefinition resourceDefinition,
            SpellResourceOperation resourceOperation,
            float resourceAmount,
            bool canOverflow = false)
        {
            resource = resourceDefinition;
            operation = resourceOperation;
            amount = Mathf.Max(0f, resourceAmount);
            allowOverflow = canOverflow;
        }
    }

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

        public override Type SettingsType => typeof(ResourceEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new ResourceEffectSettings(
                resource,
                operation,
                amount,
                allowOverflow);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            ResourceEffectSettings resolved =
                settings as ResourceEffectSettings ??
                (ResourceEffectSettings)CreateDefaultSettings();
            if (resolved.Resource == null)
                return false;

            ISpellResourceReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellResourceReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            var request = new SpellResourceChangeRequest(
                context,
                resolved.Resource,
                resolved.Operation,
                resolved.Amount * context.PotencyScale,
                resolved.AllowOverflow);
            return receiver.TryChangeResource(request, out _);
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
            ResourceEffectSettings resolved =
                settings as ResourceEffectSettings ??
                (ResourceEffectSettings)CreateDefaultSettings();
            if (resolved.Resource == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Resource effect '{DisplayName}' needs a resource definition."));
            }

            if (resolved.Amount <= 0f)
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
