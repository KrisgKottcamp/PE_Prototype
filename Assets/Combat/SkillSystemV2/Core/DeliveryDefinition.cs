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

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

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

        public abstract ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context);
    }
}
