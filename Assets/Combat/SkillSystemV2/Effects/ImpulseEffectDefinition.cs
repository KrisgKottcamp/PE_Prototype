using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellImpulseDirection
    {
        AwayFromCaster,
        TowardCaster,
        AimDirection,
        HitNormal
    }

    [Serializable]
    public sealed class ImpulseEffectSettings : SpellEffectSettings
    {
        [Tooltip("Direction the target is moved, measured from the caster, aim, or hit surface.")]
        [SerializeField]
        private SpellImpulseDirection direction =
            SpellImpulseDirection.AwayFromCaster;

        [Tooltip("Strength or distance of the push or pull, interpreted by the target's impulse receiver.")]
        [SerializeField, Min(0f)]
        private float magnitude = 5f;

        [Tooltip("How long the forced movement lasts when the receiver supports timed movement. Zero requests an immediate impulse.")]
        [SerializeField, Min(0f)]
        private float duration;

        [Tooltip("Impulse adds a burst of motion. Velocity Change directly changes movement speed.")]
        [SerializeField]
        private SpellImpulseMode mode = SpellImpulseMode.Impulse;

        public SpellImpulseDirection Direction => direction;
        public float Magnitude => Mathf.Max(0f, magnitude);
        public float Duration => Mathf.Max(0f, duration);
        public SpellImpulseMode Mode => mode;

        public ImpulseEffectSettings()
        {
        }

        public ImpulseEffectSettings(
            SpellImpulseDirection impulseDirection,
            float impulseMagnitude,
            float impulseDuration,
            SpellImpulseMode impulseMode = SpellImpulseMode.Impulse)
        {
            direction = impulseDirection;
            magnitude = Mathf.Max(0f, impulseMagnitude);
            duration = Mathf.Max(0f, impulseDuration);
            mode = impulseMode;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_Impulse",
        menuName = "Project Eri/Skill System V2/Effects/Impulse or Pushback")]
    public sealed class ImpulseEffectDefinition : EffectDefinition
    {
        [Tooltip("Default movement direction copied into a spell's inline settings.")]
        [SerializeField]
        private SpellImpulseDirection direction =
            SpellImpulseDirection.AwayFromCaster;

        [Tooltip("Default push or pull strength.")]
        [SerializeField, Min(0f)]
        private float magnitude = 5f;

        [Tooltip("Default duration of the forced movement.")]
        [SerializeField, Min(0f)]
        private float duration;

        [Tooltip("Default way the target's movement is changed.")]
        [SerializeField]
        private SpellImpulseMode mode = SpellImpulseMode.Impulse;

        public override Type SettingsType => typeof(ImpulseEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new ImpulseEffectSettings(
                direction,
                magnitude,
                duration,
                mode);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            ImpulseEffectSettings resolved =
                settings as ImpulseEffectSettings ??
                (ImpulseEffectSettings)CreateDefaultSettings();
            ISpellImpulseReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellImpulseReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            Vector2 resolvedDirection = ResolveDirection(
                context,
                resolved.Direction);
            if (resolvedDirection.sqrMagnitude <= 0.000001f)
                return false;

            var request = new SpellImpulseRequest(
                context,
                resolvedDirection,
                resolved.Magnitude * context.PotencyScale,
                resolved.Duration,
                resolved.Mode);
            return receiver.TryReceiveImpulse(request);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, CreateDefaultSettings());
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            ImpulseEffectSettings resolved =
                settings as ImpulseEffectSettings ??
                (ImpulseEffectSettings)CreateDefaultSettings();
            if (resolved.Magnitude <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Impulse effect '{DisplayName}' has zero magnitude."));
            }
        }

        private Vector2 ResolveDirection(
            in SpellEffectContext context,
            SpellImpulseDirection impulseDirection)
        {
            Vector2 casterPosition = context.Cast.Caster != null
                ? (Vector2)context.Cast.Caster.transform.position
                : context.Cast.Origin;
            Vector2 targetPosition = context.Target != null
                ? (Vector2)context.Target.transform.position
                : context.HitPoint;
            Vector2 away = targetPosition - casterPosition;

            switch (impulseDirection)
            {
                case SpellImpulseDirection.TowardCaster:
                    return -away;
                case SpellImpulseDirection.AimDirection:
                    return context.Cast.AimDirection;
                case SpellImpulseDirection.HitNormal:
                    return context.HitNormal;
                default:
                    return away;
            }
        }

        private void OnValidate()
        {
            magnitude = Mathf.Max(0f, magnitude);
            duration = Mathf.Max(0f, duration);
        }
    }
}
