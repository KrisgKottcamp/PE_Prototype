using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class MeleeArcDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("How far the melee swing reaches from the caster.")]
        [SerializeField, Min(0.01f)] private float range = 1.75f;
        [Tooltip("Width of the swing in degrees. 360 creates a full circle.")]
        [SerializeField, Range(0.1f, 360f)] private float arcAngle = 90f;
        [Tooltip("Unity layers searched for objects inside the swing.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Maximum colliders checked by one swing.")]
        [SerializeField, Min(1)] private int maximumColliders = 24;

        public float Range => Mathf.Max(0.01f, range);
        public float ArcAngle => Mathf.Clamp(arcAngle, 0.1f, 360f);
        public LayerMask HitMask => hitMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);

        public MeleeArcDeliverySettings() { }
        public MeleeArcDeliverySettings(PlayerTargetingDefinition targeting,
            float deliveryRange, float angle, LayerMask mask, int capacity)
            : base(targeting)
        {
            range = deliveryRange;
            arcAngle = angle;
            hitMask = mask;
            maximumColliders = capacity;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_MeleeArc",
        menuName = "Project Eri/Skill System V2/Delivery/Melee Arc")]
    public sealed class MeleeArcDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default reach copied into a spell when this delivery is equipped.")]
        [SerializeField, Min(0.01f)]
        private float range = 1.75f;

        [Tooltip("Default swing width in degrees.")]
        [SerializeField, Range(0.1f, 360f)]
        private float arcAngle = 90f;

        [Tooltip("Default Unity layers searched by the swing.")]
        [SerializeField]
        private LayerMask hitMask = ~0;

        [Tooltip("Default maximum colliders checked by one swing.")]
        [SerializeField, Min(1)]
        private int maximumColliders = 24;

        public float Range => Mathf.Max(0.01f, range);
        public float ArcAngle => Mathf.Clamp(arcAngle, 0.1f, 360f);

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.Direction;

        public override Type SettingsType =>
            typeof(MeleeArcDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new MeleeArcDeliverySettings(
                PlayerTargeting, range, arcAngle, hitMask, maximumColliders);
        }

        public override PlayerTargetingPreview ResolveTargetingPreview(
            in PlayerTargetingPreview preview,
            SpellDeliverySettings settings)
        {
            MeleeArcDeliverySettings resolved =
                settings as MeleeArcDeliverySettings ??
                (MeleeArcDeliverySettings)CreateDefaultSettings();
            return new PlayerTargetingPreview(
                PlayerTargetingPreviewShape.Cone,
                preview.Origin,
                preview.AimPoint,
                preview.Direction,
                resolved.Range,
                preview.Radius,
                resolved.ArcAngle,
                preview.SelectedTarget,
                preview.IsValid,
                preview.ValidationMessage);
        }

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return CreateExecution(context, CreateDefaultSettings());
        }

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context,
            SpellDeliverySettings settings)
        {
            MeleeArcDeliverySettings resolved =
                settings as MeleeArcDeliverySettings ??
                (MeleeArcDeliverySettings)CreateDefaultSettings();
            return new Execution(context, resolved.Range, resolved.ArcAngle,
                resolved.HitMask, resolved.MaximumColliders);
        }

        private void OnValidate()
        {
            range = Mathf.Max(0.01f, range);
            arcAngle = Mathf.Clamp(arcAngle, 0.1f, 360f);
            maximumColliders = Mathf.Max(1, maximumColliders);
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly float range;
            private readonly float arcAngle;
            private readonly float minimumDot;
            private readonly LayerMask hitMask;
            private readonly Collider2D[] hits;
            private readonly HashSet<int> appliedTargets = new HashSet<int>();

            public bool IsComplete { get; private set; }

            public Execution(
                in SpellExecutionContext context,
                float range,
                float arcAngle,
                LayerMask hitMask,
                int capacity)
            {
                this.context = context;
                this.range = range;
                this.arcAngle = arcAngle;
                minimumDot = arcAngle >= 359.9f
                    ? -1f
                    : Mathf.Cos(arcAngle * 0.5f * Mathf.Deg2Rad);
                this.hitMask = hitMask;
                hits = new Collider2D[capacity];
            }

            public void Begin()
            {
                SpellDeliveryGeometry geometry = SpellDeliveryGeometry.Arc(
                    context.Cast.Origin,
                    context.Cast.AimDirection,
                    range,
                    arcAngle);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.DeliveryStarted,
                    null,
                    context.Cast.Origin,
                    context.Cast.AimDirection).WithGeometry(geometry));
                SpellDeliveryInteractionService.EmitArc(
                    context,
                    context.Cast.Origin,
                    context.Cast.AimDirection,
                    range,
                    arcAngle);
                ApplyCastWideEffects();

                Vector2 origin = context.Cast.Origin;
                Vector2 aim = context.Cast.AimDirection;
                var filter = new ContactFilter2D();
                filter.SetLayerMask(hitMask);
                // Enemy hurtboxes and projectiles commonly use triggers.
                // An authored melee delivery should not silently stop working
                // when the project's global query setting ignores triggers.
                filter.useTriggers = true;
                int count = Physics2D.OverlapCircle(
                    origin,
                    range,
                    filter,
                    hits);

                for (int i = 0; i < count; i++)
                {
                    Collider2D hit = hits[i];
                    if (hit == null)
                        continue;

                    if (!SpellTargetResolver.TryResolveValidTarget(
                            context,
                            hit.gameObject,
                            out GameObject target))
                    {
                        continue;
                    }

                    Vector2 hitPoint = hit.ClosestPoint(origin);
                    Vector2 toTarget = hitPoint - origin;

                    if (toTarget.sqrMagnitude > 0.000001f &&
                        Vector2.Dot(aim, toTarget.normalized) < minimumDot)
                    {
                        continue;
                    }

                    int targetId = SpellTargetResolver.GetTargetId(target);
                    if (targetId == 0 || !appliedTargets.Add(targetId))
                        continue;

                    context.ApplyEffects(
                        target,
                        hit.gameObject,
                        hitPoint,
                        toTarget.sqrMagnitude > 0.000001f
                            ? toTarget.normalized
                            : aim);
                    context.DispatchEvent(new SpellEventOccurrence(
                        SpellEventType.TargetHit,
                        target,
                        hitPoint,
                        toTarget).WithGeometry(geometry));
                }

                IsComplete = true;
            }

            private void ApplyCastWideEffects()
            {
                var effects = context.Spell.EffectSlots;
                for (int i = 0; i < effects.Count; i++)
                {
                    SpellEffectSlot slot = effects[i];
                    if (slot?.DeliveryBinding ==
                        SpellEffectDeliveryBinding.DeliveryAnchor)
                    {
                        continue;
                    }
                    EffectDefinition effect = slot?.Effect;
                    if (!(effect is IMeleeArcCastEffectDefinition arcEffect))
                        continue;

                    try
                    {
                        arcEffect.ApplyToMeleeArc(
                            context,
                            range,
                            arcAngle,
                            slot.Settings);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, effect);
                    }
                }
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
