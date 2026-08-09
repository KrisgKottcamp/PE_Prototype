using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellEffectContext
    {
        public SpellDefinition Spell { get; }
        public CastContext Cast { get; }
        public GameObject Target { get; }
        public Vector2 HitPoint { get; }
        public Vector2 HitNormal { get; }
        public float PotencyScale { get; }

        public SpellEffectContext(
            SpellDefinition spell,
            in CastContext cast,
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale)
        {
            Spell = spell;
            Cast = cast;
            Target = target;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            PotencyScale = Mathf.Max(0f, potencyScale);
        }
    }

    public abstract class EffectDefinition : ScriptableObject
    {
        [SerializeField]
        private string displayName;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
        }

        public abstract bool Apply(in SpellEffectContext context);
    }
}
