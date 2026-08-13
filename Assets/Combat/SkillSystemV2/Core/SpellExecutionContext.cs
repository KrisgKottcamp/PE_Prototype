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
            return ApplyEffectsDetailed(
                target,
                target,
                hitPoint,
                hitNormal,
                potencyScale).AppliedCount;
        }

        public int ApplyEffects(
            GameObject target,
            GameObject detectedObject,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsDetailed(
                target,
                detectedObject,
                hitPoint,
                hitNormal,
                potencyScale).AppliedCount;
        }

        public SpellEffectApplicationResult ApplyEffectsDetailed(
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsDetailed(
                target,
                target,
                hitPoint,
                hitNormal,
                potencyScale);
        }

        public SpellEffectApplicationResult ApplyEffectsDetailed(
            GameObject target,
            GameObject detectedObject,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsInternal(
                target,
                detectedObject,
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
            return ApplyNonPresenceEffects(
                target,
                target,
                hitPoint,
                hitNormal,
                potencyScale);
        }

        internal int ApplyNonPresenceEffects(
            GameObject target,
            GameObject detectedObject,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale = 1f)
        {
            return ApplyEffectsInternal(
                target,
                detectedObject,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects: false).AppliedCount;
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
                target,
                target,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects: false).AppliedCount;
        }

        private SpellEffectApplicationResult ApplyEffectsInternal(
            GameObject target,
            GameObject detectedObject,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            bool includeAreaPresenceEffects)
        {
            if (Spell == null)
            {
                return ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.MissingSpell,
                    null,
                    target,
                    null,
                    detectedObject,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "The execution context has no Spell Definition."));
            }

            if (target == null)
            {
                return ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.MissingTarget,
                    Spell,
                    null,
                    null,
                    detectedObject,
                    Spell.EffectSlots.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "The delivery requested effects without a target."));
            }

            GameObject resolvedTarget = SpellTargetResolver.Resolve(target);
            if (resolvedTarget == null)
            {
                return ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.TargetResolutionFailed,
                    Spell,
                    target,
                    null,
                    detectedObject,
                    Spell.EffectSlots.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "The detected object could not be resolved to a stable target."));
            }

            GameObject layerObject = detectedObject != null
                ? detectedObject
                : target;
            if (!Spell.TargetFilter.IsValid(
                    Cast,
                    resolvedTarget,
                    layerObject,
                    out string rejectionReason))
            {
                return ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.TargetRejected,
                    Spell,
                    target,
                    resolvedTarget,
                    layerObject,
                    Spell.EffectSlots.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    string.IsNullOrWhiteSpace(rejectionReason)
                        ? "The target did not pass this spell's Target Rules."
                        : rejectionReason));
            }

            return ApplyEffectSlotsInternal(
                Spell.EffectSlots,
                target,
                resolvedTarget,
                layerObject,
                hitPoint,
                hitNormal,
                potencyScale,
                includeAreaPresenceEffects);
        }

        private SpellEffectApplicationResult ApplyEffectSlotsInternal(
            IReadOnlyList<SpellEffectSlot> effects,
            GameObject requestedTarget,
            GameObject target,
            GameObject detectedObject,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            bool includeAreaPresenceEffects,
            SpellEventType eventType = SpellEventType.None,
            GameObject eventSubject = null,
            Component deliveryRuntime = null)
        {
            int configuredCount = effects?.Count ?? 0;
            if (configuredCount == 0)
            {
                return ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.NoEffectsConfigured,
                    Spell,
                    requestedTarget,
                    target,
                    detectedObject,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "No effects are configured for this application moment."));
            }

            int attemptedCount = 0;
            int appliedCount = 0;
            int rejectedCount = 0;
            int skippedCount = 0;
            int exceptionCount = 0;

            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                EffectDefinition effect = slot?.Effect;
                if (effect == null)
                {
                    skippedCount++;
                    SpellRuntimeDiagnostics.ReportEffectSlot(
                        new SpellEffectSlotDiagnostic(
                            Spell,
                            null,
                            target,
                            i,
                            SpellEffectSlotStatus.EmptySlot,
                            "The effect slot is empty."));
                    continue;
                }

                if (!includeAreaPresenceEffects &&
                    effect is IAreaPresenceEffectDefinition)
                {
                    skippedCount++;
                    SpellRuntimeDiagnostics.ReportEffectSlot(
                        new SpellEffectSlotDiagnostic(
                            Spell,
                            effect,
                            target,
                            i,
                            SpellEffectSlotStatus.PresenceEffectSkipped,
                            "Presence effects are not applied by this event or pulse."));
                    continue;
                }

                attemptedCount++;
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
                    {
                        appliedCount++;
                        SpellRuntimeDiagnostics.ReportEffectSlot(
                            new SpellEffectSlotDiagnostic(
                                Spell,
                                effect,
                                target,
                                i,
                                SpellEffectSlotStatus.Applied,
                                "The effect applied successfully."));
                    }
                    else
                    {
                        rejectedCount++;
                        SpellRuntimeDiagnostics.ReportEffectSlot(
                            new SpellEffectSlotDiagnostic(
                                Spell,
                                effect,
                                target,
                                i,
                                SpellEffectSlotStatus.Rejected,
                                effect.DescribeApplicationFailure(
                                    effectContext,
                                    slot.Settings)));
                    }
                }
                catch (Exception exception)
                {
                    exceptionCount++;
                    SpellRuntimeDiagnostics.ReportEffectSlot(
                        new SpellEffectSlotDiagnostic(
                            Spell,
                            effect,
                            target,
                            i,
                            SpellEffectSlotStatus.Exception,
                            "The effect threw an exception.",
                            exception));
                    Debug.LogException(exception, effect);
                }
            }

            SpellEffectApplicationStatus status;
            string message;
            if (attemptedCount == 0)
            {
                status = SpellEffectApplicationStatus.NoApplicableEffects;
                message = "Every configured slot was empty or intentionally skipped.";
            }
            else if (appliedCount > 0 &&
                     rejectedCount == 0 &&
                     exceptionCount == 0)
            {
                status = SpellEffectApplicationStatus.Applied;
                message = "All attempted effects applied successfully.";
            }
            else if (appliedCount > 0)
            {
                status = SpellEffectApplicationStatus.PartialSuccess;
                message = "Some effects applied while others were rejected or failed.";
            }
            else if (exceptionCount > 0)
            {
                status = SpellEffectApplicationStatus.EffectException;
                message = "No effect applied because one or more effects threw an exception.";
            }
            else
            {
                status = SpellEffectApplicationStatus.AllEffectsRejected;
                message = "Every attempted effect returned false. Check the target's receiver components.";
            }

            return ReportApplication(new SpellEffectApplicationResult(
                status,
                Spell,
                requestedTarget,
                target,
                detectedObject,
                configuredCount,
                attemptedCount,
                appliedCount,
                rejectedCount,
                skippedCount,
                exceptionCount,
                message));
        }

        private int ApplyEventEffectRoute(
            SpellEventEffectRoute route,
            in SpellEventOccurrence occurrence)
        {
            GameObject recipient = route.ResolveRecipient(Cast, occurrence);
            if (recipient == null &&
                route.Recipient != SpellEventRecipient.WorldPoint)
            {
                ReportApplication(new SpellEffectApplicationResult(
                    SpellEffectApplicationStatus.MissingTarget,
                    Spell,
                    null,
                    null,
                    null,
                    route.EffectSlots.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    $"Event Effect Recipe '{route.DisplayName}' could not resolve its recipient."));
                return 0;
            }

            return ApplyEffectSlotsInternal(
                route.EffectSlots,
                recipient,
                recipient,
                recipient,
                occurrence.Point,
                occurrence.Normal,
                1f,
                includeAreaPresenceEffects: false,
                eventType: occurrence.Type,
                eventSubject: occurrence.Subject,
                deliveryRuntime: occurrence.DeliveryRuntime).AppliedCount;
        }

        private static SpellEffectApplicationResult ReportApplication(
            in SpellEffectApplicationResult result)
        {
            SpellRuntimeDiagnostics.ReportApplication(result);
            return result;
        }
    }
}
