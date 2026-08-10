using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
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
        [SerializeField]
        private EffectDefinition effect;

        [SerializeReference]
        private SpellEffectSettings settings;

        public EffectDefinition Effect => effect;
        public SpellEffectSettings Settings => settings;

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
