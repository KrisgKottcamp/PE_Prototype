using System;
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

        private int ApplyEffectsInternal(
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            bool includeAreaPresenceEffects)
        {
            if (Spell == null || target == null)
                return 0;

            if (!Spell.TargetFilter.IsValid(Cast, target))
                return 0;

            int appliedCount = 0;
            var effects = Spell.EffectSlots;

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
                    potencyScale);

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
    }
}
