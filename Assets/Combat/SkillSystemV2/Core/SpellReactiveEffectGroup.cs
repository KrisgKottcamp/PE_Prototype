using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// A named effect set whose active state can be changed by delivery
    /// reactions. Settings remain inline and local to the owning spell.
    /// </summary>
    [Serializable]
    public sealed class SpellReactiveEffectGroup
    {
        [Tooltip("A short name describing the alternate behavior, such as Ignited Damage or Frozen Slow.")]
        [SerializeField]
        private string displayName = "New Reactive Effect Group";

        [SerializeField, HideInInspector]
        private string stableId;

        [Tooltip("Enable these effects as soon as the delivery is created. Disable this when a Reaction should unlock them later.")]
        [SerializeField]
        private bool startsActive;

        [Tooltip("Use the Spell Definition's main Target Rules for this group. Disable it to give this group its own rules.")]
        [SerializeField]
        private bool useSpellTargetRules = true;

        [Tooltip("Independent target rules used only by this group when Use Spell Target Rules is disabled.")]
        [SerializeField]
        private TargetFilter targetFilter = new TargetFilter(
            TargetRelationship.Enemies,
            requireTarget: false);

        [Tooltip("Effects applied while this group is active.")]
        [SerializeField]
        private List<SpellEffectSlot> effectSlots =
            new List<SpellEffectSlot>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? "Reactive Effect Group"
            : displayName.Trim();
        public string StableId => stableId;
        public bool StartsActive => startsActive;
        public bool UsesSpellTargetRules => useSpellTargetRules;
        public TargetFilter TargetFilter => targetFilter;
        public IReadOnlyList<SpellEffectSlot> EffectSlots
        {
            get
            {
                EnsureValid();
                return effectSlots;
            }
        }

        public SpellReactiveEffectGroup()
        {
            EnsureStableId();
        }

        public SpellReactiveEffectGroup(
            string groupName,
            bool activeAtStart,
            SpellEffectSlot[] effects,
            bool inheritSpellTargetRules = true,
            TargetFilter groupTargetFilter = default)
        {
            displayName = groupName;
            startsActive = activeAtStart;
            useSpellTargetRules = inheritSpellTargetRules;
            targetFilter = groupTargetFilter;
            effectSlots = effects != null
                ? new List<SpellEffectSlot>(effects)
                : new List<SpellEffectSlot>();
            EnsureValid();
        }

        public bool IsValidTarget(
            SpellDefinition spell,
            in CastContext cast,
            GameObject candidate)
        {
            return IsValidTarget(spell, cast, candidate, candidate);
        }

        public bool IsValidTarget(
            SpellDefinition spell,
            in CastContext cast,
            GameObject candidate,
            GameObject detectedObject)
        {
            TargetFilter resolved = useSpellTargetRules && spell != null
                ? spell.TargetFilter
                : targetFilter;
            return resolved.IsValid(cast, candidate, detectedObject);
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
}
