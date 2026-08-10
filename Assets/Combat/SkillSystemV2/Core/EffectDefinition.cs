using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Optional contract for effects that should exist only while a target is
    /// physically inside a lingering delivery. The delivery supplies itself
    /// as a unique source so overlapping zones can be removed independently.
    /// </summary>
    public interface IAreaPresenceEffectDefinition
    {
        bool ApplyPresence(
            in SpellEffectContext context,
            Component source,
            SpellEffectSettings settings);

        void RemovePresence(
            GameObject target,
            Component source,
            SpellEffectSettings settings);
    }

    /// <summary>
    /// Optional contract for effects that operate once across an entire melee
    /// arc rather than requiring a collider-resolved target. This is useful
    /// for transient objects such as projectiles whose collider setup varies
    /// between authored prefabs.
    /// </summary>
    public interface IMeleeArcCastEffectDefinition
    {
        int ApplyToMeleeArc(
            in SpellExecutionContext context,
            float range,
            float arcAngle,
            SpellEffectSettings settings);
    }

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

        /// <summary>
        /// The inline settings type this module expects. Null means the module
        /// currently uses only its shared asset configuration.
        /// </summary>
        public virtual Type SettingsType => null;

        public virtual SpellEffectSettings CreateDefaultSettings()
        {
            return null;
        }

        public virtual bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            return Apply(context);
        }

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
        }

        public virtual void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            CollectValidationIssues(issues);
        }

        public abstract bool Apply(in SpellEffectContext context);
    }
}
