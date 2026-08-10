using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// A reusable delivery behavior equipped by one spell, paired with an
    /// inline settings copy owned exclusively by that spell.
    /// </summary>
    [Serializable]
    public sealed class SpellDeliverySlot
    {
        [SerializeField]
        private DeliveryDefinition delivery;

        [SerializeReference]
        private SpellDeliverySettings settings;

        public DeliveryDefinition Delivery => delivery;
        public SpellDeliverySettings Settings => settings;
        public PlayerTargetingDefinition PlayerTargeting =>
            delivery != null
                ? delivery.ResolvePlayerTargeting(settings)
                : null;

        public SpellDeliverySlot()
        {
        }

        public SpellDeliverySlot(DeliveryDefinition deliveryDefinition)
        {
            delivery = deliveryDefinition;
            ResetSettingsToDefaults();
        }

        public SpellDeliverySlot(
            DeliveryDefinition deliveryDefinition,
            SpellDeliverySettings deliverySettings)
        {
            delivery = deliveryDefinition;
            settings = deliverySettings;
            EnsureCompatibleSettings();
        }

        public void SetDelivery(DeliveryDefinition deliveryDefinition)
        {
            if (delivery == deliveryDefinition)
            {
                EnsureCompatibleSettings();
                return;
            }

            delivery = deliveryDefinition;
            ResetSettingsToDefaults();
        }

        public void SetSettings(SpellDeliverySettings deliverySettings)
        {
            settings = deliverySettings;
            EnsureCompatibleSettings();
        }

        public void ResetSettingsToDefaults()
        {
            settings = delivery != null
                ? delivery.CreateDefaultSettings()
                : null;
        }

        public bool EnsureCompatibleSettings()
        {
            if (delivery == null)
            {
                bool changed = settings != null;
                settings = null;
                return changed;
            }

            Type expected = delivery.SettingsType;
            if (expected == null)
            {
                bool changed = settings != null;
                settings = null;
                return changed;
            }

            if (settings != null && settings.GetType() == expected)
                return false;

            settings = delivery.CreateDefaultSettings();
            return true;
        }
    }
}
