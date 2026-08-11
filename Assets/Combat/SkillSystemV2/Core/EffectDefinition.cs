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

    /// <summary>
    /// Optional contract for effects that constrain or normalize the cast
    /// context before resources are spent. Movement effects use this to clamp
    /// destinations and reject obstructed paths during player targeting and
    /// enemy-AI cast validation.
    /// </summary>
    public interface ISpellCastContextModifierEffectDefinition
    {
        bool TryModifyCastContext(
            in CastContext requestedContext,
            SpellEffectSettings settings,
            out CastContext resolvedContext,
            out string rejectionReason);
    }

    public readonly struct SpellEffectContext
    {
        public SpellDefinition Spell { get; }
        public CastContext Cast { get; }
        public GameObject Target { get; }
        public Vector2 HitPoint { get; }
        public Vector2 HitNormal { get; }
        public float PotencyScale { get; }
        public SpellEventType EventType { get; }
        public GameObject EventSubject { get; }
        public Component DeliveryRuntime { get; }
        public bool HasDeliveryEvent => EventType != SpellEventType.None;

        public SpellEffectContext(
            SpellDefinition spell,
            in CastContext cast,
            GameObject target,
            Vector2 hitPoint,
            Vector2 hitNormal,
            float potencyScale,
            SpellEventType eventType = SpellEventType.None,
            GameObject eventSubject = null,
            Component deliveryRuntime = null)
        {
            Spell = spell;
            Cast = cast;
            Target = target;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            PotencyScale = Mathf.Max(0f, potencyScale);
            EventType = eventType;
            EventSubject = eventSubject;
            DeliveryRuntime = deliveryRuntime;
        }
    }

    public abstract class EffectDefinition : ScriptableObject
    {
        [Tooltip("The reusable effect module's designer-facing name.")]
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

        /// <summary>
        /// True when this effect can run with only an event world point and no
        /// GameObject recipient. Custom effects should override this when they
        /// intentionally support world-point recipes.
        /// </summary>
        public virtual bool CanApplyWithoutRecipient(
            SpellEffectSettings settings)
        {
            return false;
        }

        public virtual bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            return Apply(context);
        }

        public virtual bool DescribesDamageType(
            SpellEffectSettings settings,
            DamageTypeDefinition damageType)
        {
            return false;
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
