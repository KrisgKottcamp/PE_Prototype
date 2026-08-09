using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Delivery_InstantTarget",
        menuName = "Project Eri/Skill System V2/Delivery/Instant Target")]
    public sealed class InstantTargetDeliveryDefinition : DeliveryDefinition
    {
        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.SelectedTarget;

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return new Execution(context);
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;

            public bool IsComplete { get; private set; }

            public Execution(in SpellExecutionContext context)
            {
                this.context = context;
            }

            public void Begin()
            {
                GameObject target = context.Cast.SelectedTarget;
                if (target != null)
                {
                    Vector2 hitPoint = target.transform.position;
                    Vector2 normal = hitPoint - context.Cast.Origin;
                    context.ApplyEffects(
                        target,
                        hitPoint,
                        normal.sqrMagnitude > 0.000001f
                            ? normal.normalized
                            : Vector2.zero);
                }

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
