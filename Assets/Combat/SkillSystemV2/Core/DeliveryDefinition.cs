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

    public abstract class DeliveryDefinition : ScriptableObject
    {
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

        public abstract CastTargetingRequirement TargetingRequirement
        {
            get;
        }

        public virtual bool ValidateContext(
            in CastContext context,
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

            rejectionReason = string.Empty;
            return true;
        }

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (issues == null || playerTargeting == null)
                return;

            if (!playerTargeting.Supports(TargetingRequirement))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Player targeting '{playerTargeting.DisplayName}' does not provide " +
                    $"the context required by delivery '{DisplayName}'."));
            }
        }

        public abstract ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context);
    }
}
