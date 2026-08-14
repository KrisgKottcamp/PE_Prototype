using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellEffectDeliveryBinding
    {
        DeliveredTargets,
        DeliveryAnchor
    }

    public enum SpellEffectAnchorApplication
    {
        OnEnter,
        Periodic,
        WhilePresent,
        OnceAtAnchor
    }

    public enum SpellEffectAnchorMultiplicity
    {
        OncePerRootCast,
        PerDeliveryRuntime,
        PerEventOccurrence
    }

    /// <summary>
    /// Base class for typed, per-spell effect configuration. Concrete effect
    /// modules provide the matching serializable settings type.
    /// </summary>
    [Serializable]
    public abstract class SpellEffectSettings
    {
    }

    /// <summary>
    /// One effect equipped by a spell. The shared EffectDefinition owns the
    /// behavior and defaults; Settings is an inline copy owned by this spell.
    /// </summary>
    [Serializable]
    public sealed class SpellEffectSlot
    {
        [Tooltip("The reusable effect this slot applies. Its values below are stored only for this spell.")]
        [SerializeField]
        private EffectDefinition effect;

        [Tooltip("Per-spell values for this effect slot. Editing these does not change the reusable effect asset.")]
        [SerializeReference]
        private SpellEffectSettings settings;

        [Tooltip("Delivered Targets preserves the delivery's normal hit behavior. Delivery Anchor creates an independent point, circle, arc, segment, or moving field when the selected delivery event occurs.")]
        [SerializeField]
        private SpellEffectDeliveryBinding deliveryBinding =
            SpellEffectDeliveryBinding.DeliveredTargets;

        [Tooltip("The delivery event that creates this anchor when the slot is in Default Effects. Event Effect Recipes use their recipe event instead.")]
        [SerializeField]
        private SpellEventType anchorTrigger = SpellEventType.DeliveryStarted;

        [Tooltip("On Enter applies once when a target enters. Periodic reapplies at an interval. While Present uses exact apply/remove ownership when the effect supports it and otherwise safely falls back to On Enter. Once At Anchor runs one world-point-capable effect when the anchor is created.")]
        [SerializeField]
        private SpellEffectAnchorApplication anchorApplication =
            SpellEffectAnchorApplication.WhilePresent;

        [Tooltip("How many anchors this slot may create: one for the root cast, one for each delivery runtime, or one for every matching event occurrence.")]
        [SerializeField]
        private SpellEffectAnchorMultiplicity anchorMultiplicity =
            SpellEffectAnchorMultiplicity.PerDeliveryRuntime;

        [Tooltip("Seconds the independent anchor remains active after it is created.")]
        [SerializeField, Min(0.02f)]
        private float anchorDuration = 1f;

        [Tooltip("Seconds between applications when Anchor Application is Periodic.")]
        [SerializeField, Min(0.02f)]
        private float anchorInterval = 0.25f;

        [Tooltip("Override for point/circle radius, arc range, or segment half-width. Zero uses the delivery's own geometry; point events without authored size use one world unit.")]
        [SerializeField, Min(0f)]
        private float anchorSizeOverride;

        [Tooltip("Allow On Enter effects to apply again after a target fully leaves and later re-enters this anchor.")]
        [SerializeField]
        private bool reapplyAfterExit = true;

        public EffectDefinition Effect => effect;
        public SpellEffectSettings Settings => settings;
        public SpellEffectDeliveryBinding DeliveryBinding => deliveryBinding;
        public SpellEventType AnchorTrigger => anchorTrigger;
        public SpellEffectAnchorApplication AnchorApplication =>
            anchorApplication;
        public SpellEffectAnchorMultiplicity AnchorMultiplicity =>
            anchorMultiplicity;
        public float AnchorDuration => Mathf.Max(0.02f, anchorDuration);
        public float AnchorInterval => Mathf.Max(0.02f, anchorInterval);
        public float AnchorSizeOverride => Mathf.Max(0f, anchorSizeOverride);
        public bool ReapplyAfterExit => reapplyAfterExit;

        public SpellEffectSlot()
        {
        }

        public SpellEffectSlot(EffectDefinition effectDefinition)
        {
            effect = effectDefinition;
            ResetSettingsToDefaults();
        }

        public SpellEffectSlot(
            EffectDefinition effectDefinition,
            SpellEffectSettings effectSettings)
        {
            effect = effectDefinition;
            settings = effectSettings;
            EnsureCompatibleSettings();
        }

        public void SetEffect(EffectDefinition effectDefinition)
        {
            if (effect == effectDefinition)
            {
                EnsureCompatibleSettings();
                return;
            }

            effect = effectDefinition;
            ResetSettingsToDefaults();
        }

        public void SetSettings(SpellEffectSettings effectSettings)
        {
            settings = effectSettings;
            EnsureCompatibleSettings();
        }

        public void ConfigureDeliveryAnchor(
            SpellEventType trigger,
            SpellEffectAnchorApplication application,
            float duration,
            float interval = 0.25f,
            float sizeOverride = 0f,
            SpellEffectAnchorMultiplicity multiplicity =
                SpellEffectAnchorMultiplicity.PerDeliveryRuntime,
            bool allowReapplyAfterExit = true)
        {
            deliveryBinding = SpellEffectDeliveryBinding.DeliveryAnchor;
            anchorTrigger = trigger;
            anchorApplication = application;
            anchorDuration = Mathf.Max(0.02f, duration);
            anchorInterval = Mathf.Max(0.02f, interval);
            anchorSizeOverride = Mathf.Max(0f, sizeOverride);
            anchorMultiplicity = multiplicity;
            reapplyAfterExit = allowReapplyAfterExit;
        }

        public void UseDeliveredTargets()
        {
            deliveryBinding = SpellEffectDeliveryBinding.DeliveredTargets;
        }

        public void ResetSettingsToDefaults()
        {
            settings = effect != null
                ? effect.CreateDefaultSettings()
                : null;
        }

        public bool EnsureCompatibleSettings()
        {
            if (effect == null)
            {
                bool changed = settings != null;
                settings = null;
                return changed;
            }

            Type expected = effect.SettingsType;
            if (expected == null)
            {
                bool changed = settings != null;
                settings = null;
                return changed;
            }

            if (settings != null && settings.GetType() == expected)
                return false;

            settings = effect.CreateDefaultSettings();
            return true;
        }
    }
}
