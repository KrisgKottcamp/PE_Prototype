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
            if (Spell == null || target == null)
                return 0;

            if (!Spell.TargetFilter.IsValid(Cast, target))
                return 0;

            int appliedCount = 0;
            var effects = Spell.Effects;

            for (int i = 0; i < effects.Count; i++)
            {
                EffectDefinition effect = effects[i];
                if (effect == null)
                    continue;

                var effectContext = new SpellEffectContext(
                    Spell,
                    Cast,
                    target,
                    hitPoint,
                    hitNormal,
                    potencyScale);

                try
                {
                    if (effect.Apply(effectContext))
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
