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

    [CreateAssetMenu(
        fileName = "Effect_Impulse",
        menuName = "Project Eri/Skill System V2/Effects/Impulse or Pushback")]
    public sealed class ImpulseEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private SpellImpulseDirection direction =
            SpellImpulseDirection.AwayFromCaster;

        [SerializeField, Min(0f)]
        private float magnitude = 5f;

        [SerializeField, Min(0f)]
        private float duration;

        [SerializeField]
        private SpellImpulseMode mode = SpellImpulseMode.Impulse;

        public override bool Apply(in SpellEffectContext context)
        {
            ISpellImpulseReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellImpulseReceiver>(
                    context.Target);
            if (receiver == null)
                return false;

            Vector2 resolvedDirection = ResolveDirection(context);
            if (resolvedDirection.sqrMagnitude <= 0.000001f)
                return false;

            var request = new SpellImpulseRequest(
                context,
                resolvedDirection,
                Mathf.Max(0f, magnitude) * context.PotencyScale,
                Mathf.Max(0f, duration),
                mode);
            return receiver.TryReceiveImpulse(request);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (magnitude <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Impulse effect '{DisplayName}' has zero magnitude."));
            }
        }

        private Vector2 ResolveDirection(in SpellEffectContext context)
        {
            Vector2 casterPosition = context.Cast.Caster != null
                ? (Vector2)context.Cast.Caster.transform.position
                : context.Cast.Origin;
            Vector2 targetPosition = context.Target != null
                ? (Vector2)context.Target.transform.position
                : context.HitPoint;
            Vector2 away = targetPosition - casterPosition;

            switch (direction)
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
