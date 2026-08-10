using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class AreaDeliverySettings : SpellDeliverySettings
    {
        [SerializeField, Min(0.01f)] private float radius = 1.5f;
        [SerializeField] private LayerMask hitMask = ~0;
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
        [SerializeField, Min(0.01f)]
        private float radius = 1.5f;

        [SerializeField]
        private LayerMask hitMask = ~0;

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
                var filter = new ContactFilter2D();
                filter.SetLayerMask(hitMask);
                filter.useTriggers = Physics2D.queriesHitTriggers;
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

                    GameObject target = SpellTargetResolver.Resolve(
                        hit.gameObject);
                    if (target == null ||
                        !appliedTargets.Add(target.GetInstanceID()))
                    {
                        continue;
                    }

                    Vector2 hitPoint = hit.ClosestPoint(center);
                    Vector2 normal = hitPoint - center;
                    context.ApplyEffects(
                        target,
                        hitPoint,
                        normal.sqrMagnitude > 0.000001f
                            ? normal.normalized
                            : Vector2.zero);
                }

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
