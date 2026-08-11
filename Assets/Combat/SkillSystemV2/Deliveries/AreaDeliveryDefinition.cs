using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class AreaDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("How far the one-time area reaches from its chosen center.")]
        [SerializeField, Min(0.01f)] private float radius = 1.5f;
        [Tooltip("Unity layers searched for objects that may receive effects.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Maximum colliders checked during one cast. Increase only for unusually crowded encounters.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;

        public float Radius => Mathf.Max(0.01f, radius);
        public LayerMask HitMask => hitMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);

        public AreaDeliverySettings() { }
        public AreaDeliverySettings(PlayerTargetingDefinition targeting,
            float areaRadius, LayerMask mask, int capacity) : base(targeting)
        {
            radius = areaRadius;
            hitMask = mask;
            maximumColliders = capacity;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_Area",
        menuName = "Project Eri/Skill System V2/Delivery/Area at Point")]
    public sealed class AreaDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default radius copied into a spell when this delivery is equipped.")]
        [SerializeField, Min(0.01f)]
        private float radius = 1.5f;

        [Tooltip("Default Unity layers searched for possible targets.")]
        [SerializeField]
        private LayerMask hitMask = ~0;

        [Tooltip("Default maximum number of colliders checked per cast.")]
        [SerializeField, Min(1)]
        private int maximumColliders = 32;

        public float Radius => Mathf.Max(0.01f, radius);

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.TargetPoint;

        public override Type SettingsType => typeof(AreaDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new AreaDeliverySettings(
                PlayerTargeting, radius, hitMask, maximumColliders);
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
            AreaDeliverySettings resolved = settings as AreaDeliverySettings ??
                (AreaDeliverySettings)CreateDefaultSettings();
            return new Execution(context, resolved.Radius, resolved.HitMask,
                resolved.MaximumColliders);
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.01f, radius);
            maximumColliders = Mathf.Max(1, maximumColliders);
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly float radius;
            private readonly LayerMask hitMask;
            private readonly Collider2D[] hits;
            private readonly HashSet<int> appliedTargets = new HashSet<int>();

            public bool IsComplete { get; private set; }

            public Execution(
                in SpellExecutionContext context,
                float radius,
                LayerMask hitMask,
                int capacity)
            {
                this.context = context;
                this.radius = radius;
                this.hitMask = hitMask;
                hits = new Collider2D[capacity];
            }

            public void Begin()
            {
                Vector2 center = context.Cast.TargetPoint;
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.DeliveryStarted,
                    null,
                    context.Cast.Origin,
                    context.Cast.AimDirection));
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.AreaCreated,
                    null,
                    center,
                    Vector2.zero));
                SpellDeliveryInteractionService.EmitCircle(
                    context,
                    center,
                    radius);
                var filter = new ContactFilter2D();
                filter.SetLayerMask(hitMask);
                filter.useTriggers = true;
                int count = Physics2D.OverlapCircle(
                    center,
                    radius,
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

                    int targetId = SpellTargetResolver.GetTargetId(target);
                    if (targetId == 0 || !appliedTargets.Add(targetId))
                        continue;

                    Vector2 hitPoint = hit.ClosestPoint(center);
                    Vector2 normal = hitPoint - center;
                    context.ApplyEffects(
                        target,
                        hitPoint,
                        normal.sqrMagnitude > 0.000001f
                            ? normal.normalized
                            : Vector2.zero);
                    context.DispatchEvent(new SpellEventOccurrence(
                        SpellEventType.TargetHit,
                        target,
                        hitPoint,
                        normal));
                }

                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.AreaPulse,
                    null,
                    center,
                    Vector2.zero));

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
