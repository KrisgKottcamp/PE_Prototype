using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Shared delivery moments that can drive optional effect recipes.
    /// Delivery implementations report these moments; they never decide what
    /// a spell does with them.
    /// </summary>
    public enum SpellEventType
    {
        None,
        CastStarted,
        DeliveryStarted,
        PointReached,
        TargetHit,
        BlockingHit,
        DeliveryStopped,
        AreaCreated,
        AreaPulse,
        TargetEnteredArea,
        TargetExitedArea,
        DeliveryExpired,
        ManualReaction,
        Armed,
        TargetCrossed,
        ProximityTriggered,
        TimerExpired,
        Bounced,
        Stuck,
        Deflected,
        Detonated
    }

    public enum SpellEventRecipient
    {
        EventSubject,
        Caster,
        SelectedTarget,
        WorldPoint
    }

    public enum SpellEventSubjectRuleMode
    {
        NoRestrictions,
        RequireEventSubject,
        UseSpellTargetRules,
        CustomRules
    }

    /// <summary>
    /// One occurrence reported by a delivery. Subject is the object involved
    /// in the moment, while Point and Normal describe where it happened.
    /// </summary>
    public readonly struct SpellEventOccurrence
    {
        public SpellEventType Type { get; }
        public GameObject Subject { get; }
        public Vector2 Point { get; }
        public Vector2 Normal { get; }
        public Component DeliveryRuntime { get; }

        public SpellEventOccurrence(
            SpellEventType type,
            GameObject subject,
            Vector2 point,
            Vector2 normal,
            Component deliveryRuntime = null)
        {
            Type = type;
            Subject = subject;
            Point = point;
            Normal = normal.sqrMagnitude > 0.000001f
                ? normal.normalized
                : Vector2.zero;
            DeliveryRuntime = deliveryRuntime;
        }
    }

    /// <summary>
    /// A designer-authored WHEN/APPLY TO/EFFECTS recipe. It is stored inside a
    /// spell so shared effect modules still receive independent inline values.
    /// </summary>
    [Serializable]
    public sealed class SpellEventEffectRoute
    {
        [Tooltip("Turn this recipe on or off without deleting its setup.")]
        [SerializeField]
        private bool enabled = true;

        [Tooltip("A short descriptive name such as Teleport on Impact or Refund AP on Miss.")]
        [SerializeField]
        private string displayName = "New Event Effect Recipe";

        [SerializeField, HideInInspector]
        private string stableId;

        [Tooltip("The moment from this spell's own delivery that starts the recipe.")]
        [SerializeField]
        private SpellEventType trigger = SpellEventType.TargetHit;

        [Tooltip("Controls whether the object involved in the event must pass any rules before the recipe runs.")]
        [SerializeField]
        private SpellEventSubjectRuleMode subjectRuleMode =
            SpellEventSubjectRuleMode.UseSpellTargetRules;

        [Tooltip("Rules used for the involved object when Involved Object is set to Use Custom Rules.")]
        [SerializeField]
        private TargetFilter customSubjectRules = new TargetFilter(
            TargetRelationship.Enemies,
            requireTarget: false);

        [Tooltip("Who or what receives the recipe's effects when it runs.")]
        [SerializeField]
        private SpellEventRecipient recipient =
            SpellEventRecipient.EventSubject;

        [Tooltip("Effects applied by this recipe. Each effect keeps its own per-spell settings here.")]
        [SerializeField]
        private List<SpellEffectSlot> effectSlots =
            new List<SpellEffectSlot>();

        public bool Enabled => enabled;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? "Event Effect Recipe"
            : displayName.Trim();
        public string StableId => stableId;
        public SpellEventType Trigger => trigger;
        public SpellEventSubjectRuleMode SubjectRuleMode => subjectRuleMode;
        public TargetFilter CustomSubjectRules => customSubjectRules;
        public SpellEventRecipient Recipient => recipient;
        public IReadOnlyList<SpellEffectSlot> EffectSlots
        {
            get
            {
                EnsureValid();
                return effectSlots;
            }
        }

        public SpellEventEffectRoute()
        {
            EnsureStableId();
        }

        public SpellEventEffectRoute(
            string routeName,
            SpellEventType eventTrigger,
            SpellEventRecipient effectRecipient,
            SpellEffectSlot[] effects,
            SpellEventSubjectRuleMode subjectRules =
                SpellEventSubjectRuleMode.UseSpellTargetRules,
            TargetFilter customRules = default)
        {
            displayName = routeName;
            trigger = eventTrigger;
            recipient = effectRecipient;
            subjectRuleMode = subjectRules;
            customSubjectRules = customRules;
            effectSlots = effects != null
                ? new List<SpellEffectSlot>(effects)
                : new List<SpellEffectSlot>();
            EnsureValid();
        }

        public bool Matches(
            SpellDefinition spell,
            in CastContext cast,
            in SpellEventOccurrence occurrence)
        {
            if (!enabled || trigger != occurrence.Type)
                return false;

            switch (subjectRuleMode)
            {
                case SpellEventSubjectRuleMode.NoRestrictions:
                    return true;

                case SpellEventSubjectRuleMode.RequireEventSubject:
                    return occurrence.Subject != null;

                case SpellEventSubjectRuleMode.UseSpellTargetRules:
                    return spell != null && occurrence.Subject != null &&
                           spell.TargetFilter.IsValid(
                               cast,
                               occurrence.Subject);

                case SpellEventSubjectRuleMode.CustomRules:
                    return occurrence.Subject != null &&
                           customSubjectRules.IsValid(
                               cast,
                               occurrence.Subject);

                default:
                    return false;
            }
        }

        public GameObject ResolveRecipient(
            in CastContext cast,
            in SpellEventOccurrence occurrence)
        {
            switch (recipient)
            {
                case SpellEventRecipient.Caster:
                    return cast.Caster;
                case SpellEventRecipient.SelectedTarget:
                    return cast.SelectedTarget;
                case SpellEventRecipient.WorldPoint:
                    return null;
                default:
                    return occurrence.Subject;
            }
        }

        public void ReplaceEffectSlots(params SpellEffectSlot[] replacement)
        {
            effectSlots = replacement != null
                ? new List<SpellEffectSlot>(replacement)
                : new List<SpellEffectSlot>();
            EnsureValid();
        }

        public bool EnsureValid()
        {
            bool changed = EnsureStableId();
            effectSlots ??= new List<SpellEffectSlot>();
            for (int i = 0; i < effectSlots.Count; i++)
            {
                if (effectSlots[i] == null)
                {
                    effectSlots[i] = new SpellEffectSlot();
                    changed = true;
                }

                changed |= effectSlots[i].EnsureCompatibleSettings();
            }

            return changed;
        }

        public bool EnsureStableId()
        {
            if (!string.IsNullOrWhiteSpace(stableId))
                return false;

            stableId = Guid.NewGuid().ToString("N");
            return true;
        }
    }

    public static class SpellEventSupport
    {
        public static bool DeliveryReports(
            DeliveryDefinition delivery,
            SpellEventType eventType)
        {
            if (delivery == null || eventType == SpellEventType.None)
                return false;

            if (eventType == SpellEventType.CastStarted ||
                eventType == SpellEventType.DeliveryStarted)
            {
                return true;
            }

            if (eventType == SpellEventType.ManualReaction)
                return delivery is LingeringAreaDeliveryDefinition;
            if (eventType == SpellEventType.Armed)
            {
                return delivery is TripWireDeliveryDefinition ||
                       delivery is ProximityMineDeliveryDefinition;
            }
            if (eventType == SpellEventType.TargetCrossed)
                return delivery is TripWireDeliveryDefinition;
            if (eventType == SpellEventType.ProximityTriggered)
                return delivery is ProximityMineDeliveryDefinition;
            if (eventType == SpellEventType.TimerExpired ||
                eventType == SpellEventType.Detonated ||
                eventType == SpellEventType.Stuck)
            {
                return delivery is GrenadeDeliveryDefinition;
            }
            if (eventType == SpellEventType.Bounced)
            {
                return delivery is GrenadeDeliveryDefinition ||
                       delivery is RicochetProjectileDeliveryDefinition;
            }
            if (eventType == SpellEventType.Deflected)
                return delivery is RicochetProjectileDeliveryDefinition;
            if (eventType == SpellEventType.BlockingHit ||
                eventType == SpellEventType.DeliveryStopped)
            {
                return delivery is ProjectileDeliveryDefinition ||
                       delivery is GrenadeDeliveryDefinition ||
                       delivery is RicochetProjectileDeliveryDefinition;
            }
            if (eventType == SpellEventType.TargetEnteredArea ||
                eventType == SpellEventType.TargetExitedArea ||
                eventType == SpellEventType.DeliveryExpired)
            {
                return delivery is LingeringAreaDeliveryDefinition;
            }
            if (eventType == SpellEventType.AreaCreated ||
                eventType == SpellEventType.AreaPulse)
            {
                return delivery is AreaDeliveryDefinition ||
                       delivery is LingeringAreaDeliveryDefinition;
            }
            if (eventType == SpellEventType.PointReached)
            {
                return delivery is PointClickDeliveryDefinition ||
                       delivery is SelfDeliveryDefinition;
            }
            if (eventType == SpellEventType.TargetHit)
                return !(delivery is PointClickDeliveryDefinition) &&
                       !(delivery is TripWireDeliveryDefinition);

            return false;
        }
    }
}
