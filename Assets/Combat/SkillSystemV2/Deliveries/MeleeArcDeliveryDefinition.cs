using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Delivery_MeleeArc",
        menuName = "Project Eri/Skill System V2/Delivery/Melee Arc")]
    public sealed class MeleeArcDeliveryDefinition : DeliveryDefinition
    {
        [SerializeField, Min(0.01f)]
        private float range = 1.75f;

        [SerializeField, Range(0.1f, 360f)]
        private float arcAngle = 90f;

        [SerializeField]
        private LayerMask hitMask = ~0;

        [SerializeField, Min(1)]
        private int maximumColliders = 24;

        public float Range => Mathf.Max(0.01f, range);
        public float ArcAngle => Mathf.Clamp(arcAngle, 0.1f, 360f);

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.Direction;

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return new Execution(
                context,
                Range,
                ArcAngle,
                hitMask,
                Mathf.Max(1, maximumColliders));
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
                minimumDot = arcAngle >= 359.9f
                    ? -1f
                    : Mathf.Cos(arcAngle * 0.5f * Mathf.Deg2Rad);
                this.hitMask = hitMask;
                hits = new Collider2D[capacity];
            }

            public void Begin()
            {
                Vector2 origin = context.Cast.Origin;
                Vector2 aim = context.Cast.AimDirection;
                var filter = new ContactFilter2D();
                filter.SetLayerMask(hitMask);
                filter.useTriggers = Physics2D.queriesHitTriggers;
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

                    GameObject target = SpellTargetResolver.Resolve(
                        hit.gameObject);
                    if (target == null ||
                        SpellTargetResolver.IsSameHierarchy(
                            context.Cast.Caster,
                            target))
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

                    if (!appliedTargets.Add(target.GetInstanceID()))
                        continue;

                    context.ApplyEffects(
                        target,
                        hitPoint,
                        toTarget.sqrMagnitude > 0.000001f
                            ? toTarget.normalized
                            : aim);
                }

                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel() { IsComplete = true; }
        }
    }
}
