using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellExecutionContext
    {
        public SpellDefinition Spell { get; }
        public CastContext Cast { get; }

        public SpellExecutionContext(
            SpellDefinition spell,
            in CastContext cast)
        {
            Spell = spell;
            Cast = cast;
        }

        public int DispatchEvent(in SpellEventOccurrence occurrence)
        {
            if (Spell == null)
                return 0;

            IReadOnlyList<SpellEventEffectRoute> routes =
                Spell.EventEffectRoutes;
            int applied = 0;
            for (int i = 0; i < routes.Count; i++)
            {
                SpellEventEffectRoute route = routes[i];
                if (route != null && route.Matches(
                        Spell,
                        Cast,
                        occurrence))
                {
                    applied += ApplyEventEffectRoute(route, occurrence);
                }
            }

            return applied;
        }

        internal int DispatchEventRoute(
            string routeId,
            in SpellEventOccurrence occurrence)
        {
            if (Spell == null || string.IsNullOrWhiteSpace(routeId))
                return 0;

            IReadOnlyList<SpellEventEffectRoute> routes =
                Spell.EventEffectRoutes;
            for (int i = 0; i < routes.Count; i++)
            {
                SpellEventEffectRoute route = routes[i];
                if (route == null || !route.Enabled ||
                    !string.Equals(
                        route.StableId,
                        routeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return route.Matches(Spell, Cast, occurrence)
                    ? ApplyEventEffectRoute(route, occurrence)
                    : 0;
            }

            return 0;
        }

        public int ApplyEffects(
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsInternal(
                target,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects: true);
        }

        internal int ApplyNonPresenceEffects(
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsInternal(
                target,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects: false);
        }

        internal int ApplyNonPresenceEffectSlotsUnchecked(
            IReadOnlyList<SpellEffectSlot> slots,
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            if (Spell == null || target == null)
                return 0;

            return ApplyEffectSlotsInternal(
                slots,
                target,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects: false);
        }

        private int ApplyEffectsInternal(
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            bool includeAreaPresenceEffects)
        {
            if (Spell == null || target == null)
                return 0;

            GameObject detectedObject = target;
            GameObject resolvedTarget = SpellTargetResolver.Resolve(target);
            if (resolvedTarget == null ||
                !Spell.TargetFilter.IsValid(
                    Cast,
                    resolvedTarget,
                    detectedObject))
            {
                return 0;
            }

            return ApplyEffectSlotsInternal(
                Spell.EffectSlots,
                resolvedTarget,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects);
        }

        private int ApplyEffectSlotsInternal(
            IReadOnlyList<SpellEffectSlot> effects,
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            bool includeAreaPresenceEffects,
            SpellEventType eventType = SpellEventType.None,
            GameObject eventSubject = null,
            Component deliveryRuntime = null)
        {
            if (effects == null)
                return 0;

            int appliedCount = 0;

            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                EffectDefinition effect = slot?.Effect;
                if (effect == null ||
                    (!includeAreaPresenceEffects &&
                     effect is IAreaPresenceEffectDefinition))
                {
                    continue;
                }

                var effectContext = new SpellEffectContext(
                    Spell,
                    Cast,
                    target,
                    hitPoint,
                    hitNormal,
                    potencyScale,
                    eventType,
                    eventSubject,
                    deliveryRuntime);

                try
                {
                    if (effect.Apply(effectContext, slot.Settings))
                        appliedCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, effect);
                }
            }

            return appliedCount;
        }

        private int ApplyEventEffectRoute(
            SpellEventEffectRoute route,
            in SpellEventOccurrence occurrence)
        {
            GameObject recipient = route.ResolveRecipient(Cast, occurrence);
            if (recipient == null &&
                route.Recipient != SpellEventRecipient.WorldPoint)
            {
                return 0;
            }

            return ApplyEffectSlotsInternal(
                route.EffectSlots,
                recipient,
                occurrence.Point,
                occurrence.Normal,
                1f,
                includeAreaPresenceEffects: false,
                eventType: occurrence.Type,
                eventSubject: occurrence.Subject,
                deliveryRuntime: occurrence.DeliveryRuntime);
        }
    }
}
