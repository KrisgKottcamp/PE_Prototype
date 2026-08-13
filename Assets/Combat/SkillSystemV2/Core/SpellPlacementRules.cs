using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class SpellPlacementRules
    {
        [Tooltip("Farthest distance from the caster that this spell may place a target point. Zero uses the targeting mode's own range without adding another limit.")]
        [SerializeField, Min(0f)] private float maximumDistance;

        [Tooltip("Require a clear path from the caster to every placement point. Use this to stop mines, areas, and teleports from being placed through walls.")]
        [SerializeField] private bool requireLineOfSight;

        [Tooltip("World layers that block placement when Require Line of Sight is enabled. Usually this should contain Obstacles.")]
        [SerializeField] private LayerMask lineOfSightMask;

        [Tooltip("Thickness of the line-of-sight check. Zero checks a thin line; a small radius is safer around wall corners.")]
        [SerializeField, Min(0f)] private float lineOfSightRadius = 0.03f;

        public float MaximumDistance => Mathf.Max(0f, maximumDistance);
        public bool RequireLineOfSight => requireLineOfSight;
        public LayerMask LineOfSightMask => lineOfSightMask;
        public float LineOfSightRadius => Mathf.Max(0f, lineOfSightRadius);

        public bool Validate(
            in CastContext context,
            out string rejectionReason)
        {
            if (!context.HasTargetPoint)
            {
                rejectionReason = string.Empty;
                return true;
            }

            if (!ValidatePoint(context, context.TargetPoint, out rejectionReason))
                return false;

            SpellTargetingPayload payload = context.TargetingPayload;
            if (payload == null)
                return true;
            for (int i = 0; i < payload.PointCount; i++)
            {
                if (payload.TryGetPoint(i, out Vector2 point) &&
                    !ValidatePoint(context, point, out rejectionReason))
                {
                    return false;
                }
            }

            rejectionReason = string.Empty;
            return true;
        }

        private bool ValidatePoint(
            in CastContext context,
            Vector2 point,
            out string rejectionReason)
        {
            float allowedDistance = MaximumDistance;
            if (allowedDistance > 0f)
            {
                allowedDistance *= SpellStatModifierUtility.Evaluate(
                    context.Caster,
                    SpellActorStat.SpellPlacementRange);
                if ((point - context.Origin).sqrMagnitude >
                    allowedDistance * allowedDistance + 0.0001f)
                {
                    rejectionReason =
                        $"Placement is farther than {allowedDistance:0.##} world units.";
                    return false;
                }
            }

            if (!requireLineOfSight || lineOfSightMask.value == 0)
            {
                rejectionReason = string.Empty;
                return true;
            }

            Vector2 offset = point - context.Origin;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
            {
                rejectionReason = string.Empty;
                return true;
            }

            RaycastHit2D[] hits = lineOfSightRadius > 0.0001f
                ? Physics2D.CircleCastAll(
                    context.Origin,
                    lineOfSightRadius,
                    offset / distance,
                    distance,
                    lineOfSightMask)
                : Physics2D.RaycastAll(
                    context.Origin,
                    offset / distance,
                    distance,
                    lineOfSightMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i].collider;
                if (collider == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Caster,
                        collider.gameObject))
                {
                    continue;
                }

                rejectionReason =
                    $"Line of sight is blocked by {collider.gameObject.name}.";
                return false;
            }

            rejectionReason = string.Empty;
            return true;
        }
    }
}
