using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class PointClickDeliverySettings : SpellDeliverySettings
    {
        public PointClickDeliverySettings() { }

        public PointClickDeliverySettings(
            PlayerTargetingDefinition targeting)
            : base(targeting)
        {
        }
    }

    /// <summary>
    /// Resolves a clicked world point but applies the spell's effects to the
    /// caster. Effects can read Cast.TargetPoint to decide what to do with the
    /// selected destination.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Delivery_PointClick",
        menuName = "Project Eri/Skill System V2/Delivery/Point Click for Caster")]
    public sealed class PointClickDeliveryDefinition : DeliveryDefinition
    {
        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.TargetPoint;

        public override Type SettingsType =>
            typeof(PointClickDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new PointClickDeliverySettings(PlayerTargeting);
        }

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return new Execution(context);
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;

            public bool IsComplete { get; private set; }

            public Execution(in SpellExecutionContext executionContext)
            {
                context = executionContext;
            }

            public void Begin()
            {
                GameObject caster = context.Cast.Caster;
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.DeliveryStarted,
                    null,
                    context.Cast.Origin,
                    context.Cast.AimDirection));
                if (caster != null && context.Cast.HasTargetPoint)
                {
                    Vector2 destination = context.Cast.TargetPoint;
                    Vector2 direction = destination -
                                        (Vector2)caster.transform.position;
                    SpellDeliveryInteractionService.EmitPoint(
                        context,
                        destination);
                    context.DispatchEvent(new SpellEventOccurrence(
                        SpellEventType.PointReached,
                        null,
                        destination,
                        direction));
                    context.ApplyEffects(
                        caster,
                        destination,
                        direction.sqrMagnitude > 0.000001f
                            ? direction.normalized
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
