using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public interface ISpellDeliveryExecution
    {
        bool IsComplete { get; }
        void Begin();
        void Tick(float deltaTime);
        void End();
        void Cancel();
    }

    [Serializable]
    public abstract class SpellDeliverySettings
    {
        [Tooltip("How a player aims and confirms this delivery. Enemy AI ignores this and supplies its own target information.")]
        [SerializeField]
        private PlayerTargetingDefinition playerTargeting;

        public PlayerTargetingDefinition PlayerTargeting => playerTargeting;

        protected SpellDeliverySettings()
        {
        }

        protected SpellDeliverySettings(
            PlayerTargetingDefinition targetingDefinition)
        {
            playerTargeting = targetingDefinition;
        }
    }

    public abstract class DeliveryDefinition : ScriptableObject
    {
        [Tooltip("The reusable delivery module's designer-facing name.")]
        [SerializeField]
        private string displayName;

        [Header("Player Targeting")]
        [Tooltip("Optional. Player controllers use this to aim and confirm the delivery. Enemy AI ignores it and supplies CastContext directly.")]
        [SerializeField]
        private PlayerTargetingDefinition playerTargeting;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        public PlayerTargetingDefinition PlayerTargeting => playerTargeting;

        public virtual Type SettingsType => null;

        public virtual SpellDeliverySettings CreateDefaultSettings()
        {
            return null;
        }

        public virtual PlayerTargetingDefinition ResolvePlayerTargeting(
            SpellDeliverySettings settings)
        {
            return settings?.PlayerTargeting ?? playerTargeting;
        }

        /// <summary>
        /// Lets a delivery replace generic targeting-asset preview geometry
        /// with the exact per-spell geometry used by its runtime query.
        /// </summary>
        public virtual PlayerTargetingPreview ResolveTargetingPreview(
            in PlayerTargetingPreview preview,
            SpellDeliverySettings settings)
        {
            return preview;
        }

        public abstract CastTargetingRequirement TargetingRequirement
        {
            get;
        }

        public virtual bool ValidateContext(
            in CastContext context,
            out string rejectionReason)
        {
            return ValidateContext(context, null, out rejectionReason);
        }

        public virtual bool ValidateContext(
            in CastContext context,
            SpellDeliverySettings settings,
            out string rejectionReason)
        {
            CastTargetingRequirement required = TargetingRequirement;

            if ((required & CastTargetingRequirement.Direction) != 0 &&
                !context.HasAimDirection)
            {
                rejectionReason = "Delivery requires an aim direction.";
                return false;
            }

            if ((required & CastTargetingRequirement.TargetPoint) != 0 &&
                !context.HasTargetPoint)
            {
                rejectionReason = "Delivery requires a target point.";
                return false;
            }

            if ((required & CastTargetingRequirement.SelectedTarget) != 0 &&
                !context.HasSelectedTarget)
            {
                rejectionReason = "Delivery requires a selected target.";
                return false;
            }

            if ((required &
                 CastTargetingRequirement.MultipleTargetPoints) != 0 &&
                (context.TargetingPayload == null ||
                 context.TargetingPayload.PointCount < 2))
            {
                rejectionReason =
                    "Delivery requires at least two confirmed target points.";
                return false;
            }

            rejectionReason = string.Empty;
            return true;
        }

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, null);
        }

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellDeliverySettings settings)
        {
            PlayerTargetingDefinition targeting =
                ResolvePlayerTargeting(settings);
            if (issues == null || targeting == null)
                return;

            if (!targeting.Supports(TargetingRequirement))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Player targeting '{targeting.DisplayName}' does not provide " +
                    $"the context required by delivery '{DisplayName}'."));
            }
        }

        public virtual ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context,
            SpellDeliverySettings settings)
        {
            return CreateExecution(context);
        }

        public abstract ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context);
    }
}
