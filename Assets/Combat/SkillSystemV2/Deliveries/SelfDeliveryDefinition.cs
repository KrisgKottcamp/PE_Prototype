using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class SelfDeliverySettings : SpellDeliverySettings
    {
        public SelfDeliverySettings() { }
        public SelfDeliverySettings(PlayerTargetingDefinition targeting)
            : base(targeting) { }
    }

    [CreateAssetMenu(
        fileName = "Delivery_Self",
        menuName = "Project Eri/Skill System V2/Delivery/Self")]
    public sealed class SelfDeliveryDefinition : DeliveryDefinition
    {
        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.None;

        public override Type SettingsType => typeof(SelfDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new SelfDeliverySettings(PlayerTargeting);
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
                GameObject caster = context.Cast.Caster;
                if (caster != null)
                {
                    context.ApplyEffects(
                        caster,
                        caster.transform.position,
                        Vector2.zero);
                }

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
