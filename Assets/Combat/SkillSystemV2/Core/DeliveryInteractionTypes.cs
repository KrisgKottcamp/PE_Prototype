using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Flags]
    public enum DeliveryContactPhase
    {
        None = 0,
        Impact = 1 << 0,
        Enter = 1 << 1,
        Stay = 1 << 2,
        Exit = 1 << 3,
        Expire = 1 << 4,
        Any = ~0
    }

    public enum InteractionFilterMatchMode
    {
        All,
        Any
    }

    public enum DeliverySourceRelationship
    {
        Any,
        Self,
        Allies,
        Enemies,
        Neutral
    }

    public enum InteractionTriggerPolicy
    {
        EveryContact,
        OncePerSourceDelivery,
        OnceTotal
    }

    public readonly struct DeliveryInteractionContext
    {
        public SpellExecutionContext Source { get; }
        public SpellExecutionContext Receiver { get; }
        public DeliveryContactPhase Phase { get; }
        public Vector2 ContactPoint { get; }
        public int SourceRuntimeId { get; }

        public SpellDefinition SourceSpell => Source.Spell;
        public DeliveryDefinition SourceDelivery => SourceSpell != null
            ? SourceSpell.Delivery
            : null;
        public GameObject SourceCaster => Source.Cast.Caster;
        public CombatTeam SourceTeam => Source.Cast.CasterTeam;
        public SpellDefinition ReceiverSpell => Receiver.Spell;
        public GameObject ReceiverCaster => Receiver.Cast.Caster;
        public CombatTeam ReceiverTeam => Receiver.Cast.CasterTeam;

        public DeliveryInteractionContext(
            in SpellExecutionContext source,
            in SpellExecutionContext receiver,
            DeliveryContactPhase phase,
            Vector2 contactPoint,
            int sourceRuntimeId)
        {
            Source = source;
            Receiver = receiver;
            Phase = phase;
            ContactPoint = contactPoint;
            SourceRuntimeId = sourceRuntimeId;
        }

        public bool SourceCarriesEffect(EffectDefinition requiredEffect)
        {
            if (SourceSpell == null || requiredEffect == null)
                return false;

            IReadOnlyList<SpellEffectSlot> effects = SourceSpell.EffectSlots;
            if (SlotsContainEffect(effects, requiredEffect))
                return true;

            IReadOnlyList<SpellEventEffectRoute> routes =
                SourceSpell.EventEffectRoutes;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i] != null &&
                    SlotsContainEffect(
                        routes[i].EffectSlots,
                        requiredEffect))
                {
                    return true;
                }
            }

            IReadOnlyList<SpellReactiveEffectGroup> groups =
                SourceSpell.ReactiveEffectGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null &&
                    SlotsContainEffect(
                        groups[i].EffectSlots,
                        requiredEffect))
                {
                    return true;
                }
            }

            return false;
        }

        public bool SourceCarriesDamageType(DamageTypeDefinition damageType)
        {
            if (SourceSpell == null)
                return false;

            IReadOnlyList<SpellEffectSlot> effects = SourceSpell.EffectSlots;
            if (SlotsContainDamageType(effects, damageType))
                return true;

            IReadOnlyList<SpellEventEffectRoute> routes =
                SourceSpell.EventEffectRoutes;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i] != null &&
                    SlotsContainDamageType(
                        routes[i].EffectSlots,
                        damageType))
                {
                    return true;
                }
            }

            IReadOnlyList<SpellReactiveEffectGroup> groups =
                SourceSpell.ReactiveEffectGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null &&
                    SlotsContainDamageType(
                        groups[i].EffectSlots,
                        damageType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SlotsContainEffect(
            IReadOnlyList<SpellEffectSlot> slots,
            EffectDefinition requiredEffect)
        {
            if (slots == null)
                return false;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i]?.Effect == requiredEffect)
                    return true;
            }
            return false;
        }

        private static bool SlotsContainDamageType(
            IReadOnlyList<SpellEffectSlot> slots,
            DamageTypeDefinition damageType)
        {
            if (slots == null)
                return false;

            for (int i = 0; i < slots.Count; i++)
            {
                SpellEffectSlot slot = slots[i];
                if (slot?.Effect != null &&
                    slot.Effect.DescribesDamageType(
                        slot.Settings,
                        damageType))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [Serializable]
    public abstract class DeliveryInteractionCondition
    {
        [Tooltip("Reverse this rule. For example, Enemies becomes Anything Except Enemies.")]
        [SerializeField]
        private bool inverted;

        public bool Inverted => inverted;
        public abstract string DisplayName { get; }

        public bool Evaluate(in DeliveryInteractionContext context)
        {
            bool result = Matches(context);
            return inverted ? !result : result;
        }

        protected abstract bool Matches(
            in DeliveryInteractionContext context);
    }

    [Serializable]
    public sealed class InteractionRelationshipCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Required relationship between the incoming delivery's caster and this delivery's caster.")]
        [SerializeField]
        private DeliverySourceRelationship relationship =
            DeliverySourceRelationship.Enemies;

        public override string DisplayName => "Source Relationship";

        public InteractionRelationshipCondition() { }
        public InteractionRelationshipCondition(
            DeliverySourceRelationship requiredRelationship)
        {
            relationship = requiredRelationship;
        }

        protected override bool Matches(
            in DeliveryInteractionContext context)
        {
            if (relationship == DeliverySourceRelationship.Any)
                return true;

            bool sameCaster = context.SourceCaster != null &&
                              context.SourceCaster == context.ReceiverCaster;
            if (relationship == DeliverySourceRelationship.Self)
                return sameCaster;

            if (context.SourceTeam == CombatTeam.Neutral ||
                context.ReceiverTeam == CombatTeam.Neutral)
            {
                return relationship == DeliverySourceRelationship.Neutral;
            }

            bool sameTeam = context.SourceTeam == context.ReceiverTeam;
            return relationship == DeliverySourceRelationship.Allies
                ? sameTeam && !sameCaster
                : relationship == DeliverySourceRelationship.Enemies &&
                  !sameTeam;
        }
    }

    [Serializable]
    public sealed class InteractionContactPhaseCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Which contact moments count, such as entering, staying inside, leaving, or striking the delivery.")]
        [SerializeField]
        private DeliveryContactPhase acceptedPhases =
            DeliveryContactPhase.Impact | DeliveryContactPhase.Enter;

        public override string DisplayName => "Contact Phase";

        public InteractionContactPhaseCondition() { }
        public InteractionContactPhaseCondition(
            DeliveryContactPhase phases)
        {
            acceptedPhases = phases;
        }

        protected override bool Matches(
            in DeliveryInteractionContext context)
        {
            return (acceptedPhases & context.Phase) != 0;
        }
    }

    [Serializable]
    public sealed class InteractionSpellCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Only deliveries produced by this exact Spell Definition may match.")]
        [SerializeField]
        private SpellDefinition spell;

        public override string DisplayName => "Specific Spell";
        public InteractionSpellCondition() { }
        public InteractionSpellCondition(SpellDefinition requiredSpell)
        {
            spell = requiredSpell;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return spell != null && context.SourceSpell == spell;
        }
    }

    [Serializable]
    public sealed class InteractionSpellCategoryCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Only spells whose Category text matches this value may trigger the reaction.")]
        [SerializeField]
        private string category;

        public override string DisplayName => "Spell Category";
        public InteractionSpellCategoryCondition() { }
        public InteractionSpellCategoryCondition(string requiredCategory)
        {
            category = requiredCategory;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return context.SourceSpell != null &&
                string.Equals(
                    context.SourceSpell.Category,
                    category?.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class InteractionDeliveryCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Only the selected delivery module, such as Projectile or Melee Arc, may trigger the reaction.")]
        [SerializeField]
        private DeliveryDefinition delivery;

        public override string DisplayName => "Delivery Module";
        public InteractionDeliveryCondition() { }
        public InteractionDeliveryCondition(DeliveryDefinition requiredDelivery)
        {
            delivery = requiredDelivery;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return delivery != null && context.SourceDelivery == delivery;
        }
    }

    [Serializable]
    public sealed class InteractionEffectCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("The incoming spell must carry this effect in Default Effects, Event Recipes, or Reactive Effect Groups.")]
        [SerializeField]
        private EffectDefinition effect;

        public override string DisplayName => "Effect Module";
        public InteractionEffectCondition() { }
        public InteractionEffectCondition(EffectDefinition requiredEffect)
        {
            effect = requiredEffect;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return context.SourceCarriesEffect(effect);
        }
    }

    [Serializable]
    public sealed class InteractionDamageTypeCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("The incoming spell must carry an effect configured with this Damage Type.")]
        [SerializeField]
        private DamageTypeDefinition damageType;

        public override string DisplayName => "Damage Type";
        public InteractionDamageTypeCondition() { }
        public InteractionDamageTypeCondition(DamageTypeDefinition requiredType)
        {
            damageType = requiredType;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return context.SourceCarriesDamageType(damageType);
        }
    }

    [Serializable]
    public sealed class InteractionCasterTeamCondition :
        DeliveryInteractionCondition
    {
        [Tooltip("Only deliveries cast by this absolute combat team may match.")]
        [SerializeField]
        private CombatTeam team = CombatTeam.Enemy;

        public override string DisplayName => "Caster Team";
        public InteractionCasterTeamCondition() { }
        public InteractionCasterTeamCondition(CombatTeam requiredTeam)
        {
            team = requiredTeam;
        }
        protected override bool Matches(in DeliveryInteractionContext context)
        {
            return context.SourceTeam == team;
        }
    }

    [Serializable]
    public sealed class DeliveryInteractionFilter
    {
        [Tooltip("Require every trigger rule to match, or allow any single rule to match.")]
        [SerializeField]
        private InteractionFilterMatchMode matchMode =
            InteractionFilterMatchMode.All;

        [Tooltip("Optional rules describing which incoming deliveries are allowed to trigger the reaction. An empty list accepts every V2 delivery.")]
        [SerializeReference]
        private List<DeliveryInteractionCondition> conditions =
            new List<DeliveryInteractionCondition>();

        public InteractionFilterMatchMode MatchMode => matchMode;
        public IReadOnlyList<DeliveryInteractionCondition> Conditions =>
            conditions;

        public bool Matches(in DeliveryInteractionContext context)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            if (matchMode == InteractionFilterMatchMode.All)
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    if (conditions[i] != null &&
                        !conditions[i].Evaluate(context))
                    {
                        return false;
                    }
                }

                return true;
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] != null &&
                    conditions[i].Evaluate(context))
                {
                    return true;
                }
            }

            return false;
        }

        public void ReplaceConditions(
            InteractionFilterMatchMode mode,
            params DeliveryInteractionCondition[] replacement)
        {
            matchMode = mode;
            conditions = replacement != null
                ? new List<DeliveryInteractionCondition>(replacement)
                : new List<DeliveryInteractionCondition>();
        }
    }

    public interface ISpellDeliveryReactionHost
    {
        bool InteractionActive { get; }
        void SetInteractionActive(bool active);
        void PulseEffectsOnOccupants();
        void SetReactiveEffectGroupActive(
            string groupId,
            bool active,
            bool applyToCurrentOccupantsImmediately);
        void TriggerEventEffectRoute(
            string routeId,
            in DeliveryInteractionContext interaction);
        void DestroyDelivery();
    }

    [Serializable]
    public abstract class DeliveryInteractionResponse
    {
        public abstract string DisplayName { get; }
        public abstract void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context);
    }

    [Serializable]
    public sealed class ActivateDeliveryResponse :
        DeliveryInteractionResponse
    {
        [Tooltip("Set whether this persistent delivery is active. Inactive deliveries remain present for reactions but do not apply their normal effects.")]
        [SerializeField]
        private bool active = true;

        [Tooltip("When activating, immediately apply the delivery's normal effects to objects already inside it.")]
        [SerializeField]
        private bool pulseEffectsImmediately = true;

        public override string DisplayName => "Activate Delivery";

        public ActivateDeliveryResponse() { }
        public ActivateDeliveryResponse(
            bool shouldBeActive,
            bool shouldPulseImmediately)
        {
            active = shouldBeActive;
            pulseEffectsImmediately = shouldPulseImmediately;
        }

        public override void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context)
        {
            if (host == null)
                return;

            host.SetInteractionActive(active);
            if (active && pulseEffectsImmediately)
                host.PulseEffectsOnOccupants();
        }
    }

    [Serializable]
    public sealed class PulseEffectsResponse : DeliveryInteractionResponse
    {
        public override string DisplayName => "Pulse Effects on Occupants";
        public override void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context)
        {
            host?.PulseEffectsOnOccupants();
        }
    }

    [Serializable]
    public sealed class DestroyDeliveryResponse : DeliveryInteractionResponse
    {
        public override string DisplayName => "Destroy Delivery";
        public override void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context)
        {
            host?.DestroyDelivery();
        }
    }

    [Serializable]
    public sealed class SetReactiveEffectGroupActiveResponse :
        DeliveryInteractionResponse
    {
        [Tooltip("The Reactive Effect Group changed by this action.")]
        [SerializeField]
        private string groupId;

        [Tooltip("Enable the selected group when checked; disable it when unchecked.")]
        [SerializeField]
        private bool active = true;

        [Tooltip("Immediately apply the newly enabled group to valid objects already inside the area.")]
        [SerializeField]
        private bool applyToCurrentOccupantsImmediately = true;

        public string GroupId => groupId;
        public bool Active => active;
        public bool ApplyToCurrentOccupantsImmediately =>
            applyToCurrentOccupantsImmediately;
        public override string DisplayName =>
            "Change Reactive Effect Group";

        public SetReactiveEffectGroupActiveResponse() { }

        public SetReactiveEffectGroupActiveResponse(
            string reactiveGroupId,
            bool shouldBeActive,
            bool applyImmediately)
        {
            groupId = reactiveGroupId;
            active = shouldBeActive;
            applyToCurrentOccupantsImmediately = applyImmediately;
        }

        public override void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context)
        {
            host?.SetReactiveEffectGroupActive(
                groupId,
                active,
                applyToCurrentOccupantsImmediately);
        }
    }

    [Serializable]
    public sealed class RunEventEffectRouteResponse :
        DeliveryInteractionResponse
    {
        [Tooltip("The Manual Reaction Event Effect Recipe run by this action.")]
        [SerializeField]
        private string routeId;

        public string RouteId => routeId;
        public override string DisplayName => "Run Event Effect Recipe";

        public RunEventEffectRouteResponse() { }

        public RunEventEffectRouteResponse(string eventRouteId)
        {
            routeId = eventRouteId;
        }

        public override void Execute(
            ISpellDeliveryReactionHost host,
            in DeliveryInteractionContext context)
        {
            host?.TriggerEventEffectRoute(routeId, context);
        }
    }

    [Serializable]
    public sealed class SpellReactionSlot
    {
        [Tooltip("Turn this reaction on or off without deleting its trigger rules and actions.")]
        [SerializeField]
        private bool enabled = true;

        [Tooltip("Rules describing which incoming deliveries may trigger this reaction.")]
        [SerializeField]
        private DeliveryInteractionFilter filter =
            new DeliveryInteractionFilter();

        [Tooltip("Ordered actions performed after the reaction matches. Actions run from top to bottom.")]
        [SerializeReference]
        private List<DeliveryInteractionResponse> responses =
            new List<DeliveryInteractionResponse>
            {
                new ActivateDeliveryResponse()
            };

        [Tooltip("Controls whether the reaction runs on every contact, once per source delivery, or only once for this delivery instance.")]
        [SerializeField]
        private InteractionTriggerPolicy triggerPolicy =
            InteractionTriggerPolicy.OncePerSourceDelivery;

        [Tooltip("Minimum unscaled seconds before this reaction may run again.")]
        [SerializeField, Min(0f)]
        private float cooldown;

        public bool Enabled => enabled;
        public DeliveryInteractionFilter Filter
        {
            get
            {
                filter ??= new DeliveryInteractionFilter();
                return filter;
            }
        }
        public IReadOnlyList<DeliveryInteractionResponse> Responses
        {
            get
            {
                responses ??= new List<DeliveryInteractionResponse>();
                return responses;
            }
        }
        public InteractionTriggerPolicy TriggerPolicy => triggerPolicy;
        public float Cooldown => Mathf.Max(0f, cooldown);

        public SpellReactionSlot() { }
        public SpellReactionSlot(
            DeliveryInteractionFilter interactionFilter,
            DeliveryInteractionResponse interactionResponse,
            InteractionTriggerPolicy policy =
                InteractionTriggerPolicy.OncePerSourceDelivery,
            float reactionCooldown = 0f)
        {
            filter = interactionFilter ?? new DeliveryInteractionFilter();
            responses = new List<DeliveryInteractionResponse>();
            if (interactionResponse != null)
                responses.Add(interactionResponse);
            triggerPolicy = policy;
            cooldown = Mathf.Max(0f, reactionCooldown);
        }

        public void ReplaceResponses(
            params DeliveryInteractionResponse[] replacement)
        {
            responses = replacement != null
                ? new List<DeliveryInteractionResponse>(replacement)
                : new List<DeliveryInteractionResponse>();
        }
    }
}
