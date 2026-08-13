using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class InstantTargetDeliverySettings : SpellDeliverySettings
    {
        // Direct Target works with any selected-target targeting definition.
        // Menu Select is the normal player-facing choice for party buffs,
        // enemy debuffs, heals, and other immediate single-target effects.
        public InstantTargetDeliverySettings() { }
        public InstantTargetDeliverySettings(PlayerTargetingDefinition targeting)
            : base(targeting) { }
    }

    [CreateAssetMenu(
        fileName = "Delivery_DirectTarget",
        menuName = "Project Eri/Skill System V2/Delivery/Direct Target (Immediate)")]
    public sealed class InstantTargetDeliveryDefinition : DeliveryDefinition
    {
        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.SelectedTarget;

        public override Type SettingsType =>
            typeof(InstantTargetDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new InstantTargetDeliverySettings(PlayerTargeting);
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

            public Execution(in SpellExecutionContext context)
            {
                this.context = context;
            }

            public void Begin()
            {
                GameObject target = SpellTargetResolver.Resolve(
                    context.Cast.SelectedTarget);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.DeliveryStarted,
                    null,
                    context.Cast.Origin,
                    context.Cast.AimDirection));
                if (target != null && context.Spell != null &&
                    context.Spell.TargetFilter.IsValid(
                        context.Cast,
                        target,
                        context.Cast.SelectedTarget))
                {
                    Vector2 hitPoint = target.transform.position;
                    SpellDeliveryInteractionService.EmitPoint(
                        context,
                        hitPoint);
                    Vector2 normal = hitPoint - context.Cast.Origin;
                    context.ApplyEffects(
                        target,
                        context.Cast.SelectedTarget != null
                            ? context.Cast.SelectedTarget
                            : target,
                        hitPoint,
                        normal.sqrMagnitude > 0.000001f
                            ? normal.normalized
                            : Vector2.zero);
                    context.DispatchEvent(new SpellEventOccurrence(
                        SpellEventType.TargetHit,
                        target,
                        hitPoint,
                        normal));
                }

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
