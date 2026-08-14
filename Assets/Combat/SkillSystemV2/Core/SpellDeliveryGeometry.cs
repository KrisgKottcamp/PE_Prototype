using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellDeliveryGeometryShape
    {
        Point,
        Circle,
        Arc,
        Segment
    }

    public interface ISpellDeliveryGeometryProvider
    {
        bool TryGetDeliveryGeometry(out SpellDeliveryGeometry geometry);
    }

    /// <summary>
    /// Runtime geometry shared by deliveries, effect anchors, previews, and
    /// compatibility diagnostics. Geometry may follow a live delivery; an
    /// independent anchor retains the last valid snapshot when that delivery
    /// is destroyed.
    /// </summary>
    public readonly struct SpellDeliveryGeometry
    {
        public SpellDeliveryGeometryShape Shape { get; }
        public Vector2 Center { get; }
        public float Radius { get; }
        public Vector2 Direction { get; }
        public float ArcAngle { get; }
        public Vector2 Start { get; }
        public Vector2 End { get; }
        public float HalfWidth { get; }
        public Transform FollowTransform { get; }
        private Vector2 FollowOrigin { get; }

        public Vector2 BoundingCenter
        {
            get
            {
                switch (Shape)
                {
                    case SpellDeliveryGeometryShape.Segment:
                        return (Start + End) * 0.5f;
                    default:
                        return Center;
                }
            }
        }

        public float BoundingRadius
        {
            get
            {
                switch (Shape)
                {
                    case SpellDeliveryGeometryShape.Segment:
                        return Vector2.Distance(Start, End) * 0.5f +
                               HalfWidth;
                    default:
                        return Radius;
                }
            }
        }

        public float CharacteristicSize => Shape ==
            SpellDeliveryGeometryShape.Segment
                ? Mathf.Max(HalfWidth, BoundingRadius)
                : Radius;

        private SpellDeliveryGeometry(
            SpellDeliveryGeometryShape shape,
            Vector2 center,
            float radius,
            Vector2 direction,
            float arcAngle,
            Vector2 start,
            Vector2 end,
            float halfWidth,
            Transform followTransform,
            Vector2 followOrigin)
        {
            Shape = shape;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            Direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector2.up;
            ArcAngle = Mathf.Clamp(arcAngle, 0.1f, 360f);
            Start = start;
            End = end;
            HalfWidth = Mathf.Max(0f, halfWidth);
            FollowTransform = followTransform;
            FollowOrigin = followOrigin;
        }

        public static SpellDeliveryGeometry Point(Vector2 point)
        {
            return Circle(point, 0f, SpellDeliveryGeometryShape.Point);
        }

        public static SpellDeliveryGeometry Circle(
            Vector2 center,
            float radius)
        {
            return Circle(
                center,
                radius,
                SpellDeliveryGeometryShape.Circle);
        }

        public static SpellDeliveryGeometry FollowCircle(
            Transform follow,
            float radius)
        {
            Vector2 center = follow != null
                ? (Vector2)follow.position
                : Vector2.zero;
            return new SpellDeliveryGeometry(
                SpellDeliveryGeometryShape.Circle,
                center,
                radius,
                Vector2.up,
                360f,
                center,
                center,
                0f,
                follow,
                center);
        }

        public static SpellDeliveryGeometry Arc(
            Vector2 origin,
            Vector2 direction,
            float range,
            float angle)
        {
            return new SpellDeliveryGeometry(
                SpellDeliveryGeometryShape.Arc,
                origin,
                range,
                direction,
                angle,
                origin,
                origin,
                0f,
                null,
                origin);
        }

        public static SpellDeliveryGeometry FollowArc(
            Transform follow,
            Vector2 direction,
            float range,
            float angle)
        {
            Vector2 origin = follow != null
                ? (Vector2)follow.position
                : Vector2.zero;
            return new SpellDeliveryGeometry(
                SpellDeliveryGeometryShape.Arc,
                origin,
                range,
                direction,
                angle,
                origin,
                origin,
                0f,
                follow,
                origin);
        }

        public static SpellDeliveryGeometry Segment(
            Vector2 start,
            Vector2 end,
            float halfWidth)
        {
            return new SpellDeliveryGeometry(
                SpellDeliveryGeometryShape.Segment,
                (start + end) * 0.5f,
                0f,
                end - start,
                0.1f,
                start,
                end,
                halfWidth,
                null,
                (start + end) * 0.5f);
        }

        public SpellDeliveryGeometry WithSizeOverride(float sizeOverride)
        {
            float size = Mathf.Max(0f, sizeOverride);
            switch (Shape)
            {
                case SpellDeliveryGeometryShape.Point:
                    return Circle(
                        Center,
                        size > 0f ? size : 1f,
                        SpellDeliveryGeometryShape.Point,
                        FollowTransform,
                        FollowOrigin);
                case SpellDeliveryGeometryShape.Circle:
                    return Circle(
                        Center,
                        size > 0f ? size : Mathf.Max(0.01f, Radius),
                        SpellDeliveryGeometryShape.Circle,
                        FollowTransform,
                        FollowOrigin);
                case SpellDeliveryGeometryShape.Arc:
                    return new SpellDeliveryGeometry(
                        Shape,
                        Center,
                        size > 0f ? size : Mathf.Max(0.01f, Radius),
                        Direction,
                        ArcAngle,
                        Start,
                        End,
                        HalfWidth,
                        FollowTransform,
                        FollowOrigin);
                default:
                    return new SpellDeliveryGeometry(
                        Shape,
                        Center,
                        Radius,
                        Direction,
                        ArcAngle,
                        Start,
                        End,
                        size > 0f
                            ? size
                            : Mathf.Max(0.01f, HalfWidth),
                        FollowTransform,
                        FollowOrigin);
            }
        }

        public SpellDeliveryGeometry Snapshot()
        {
            if (FollowTransform == null)
                return this;

            Vector2 delta =
                (Vector2)FollowTransform.position - FollowOrigin;
            return new SpellDeliveryGeometry(
                Shape,
                Center + delta,
                Radius,
                Direction,
                ArcAngle,
                Start + delta,
                End + delta,
                HalfWidth,
                null,
                Vector2.zero);
        }

        public bool Contains(Collider2D collider)
        {
            if (collider == null)
                return false;

            switch (Shape)
            {
                case SpellDeliveryGeometryShape.Segment:
                {
                    Vector2 nearest = ClosestPointOnSegment(
                        Start,
                        End,
                        collider.bounds.center);
                    Vector2 colliderPoint = collider.ClosestPoint(nearest);
                    return (colliderPoint - nearest).sqrMagnitude <=
                           HalfWidth * HalfWidth + 0.0001f;
                }
                case SpellDeliveryGeometryShape.Arc:
                {
                    Vector2 closest = collider.ClosestPoint(Center);
                    Vector2 offset = closest - Center;
                    if (offset.sqrMagnitude > Radius * Radius + 0.0001f)
                        return false;
                    if (ArcAngle >= 359.9f ||
                        offset.sqrMagnitude <= 0.000001f)
                    {
                        return true;
                    }

                    Vector2 centerOffset =
                        (Vector2)collider.bounds.center - Center;
                    float extent = collider.bounds.extents.magnitude;
                    float padding = centerOffset.sqrMagnitude > 0.000001f
                        ? Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp01(
                            extent / centerOffset.magnitude))
                        : 180f;
                    return Vector2.Angle(Direction, centerOffset) <=
                           ArcAngle * 0.5f + padding;
                }
                default:
                {
                    Vector2 closest = collider.ClosestPoint(Center);
                    return (closest - Center).sqrMagnitude <=
                           Radius * Radius + 0.0001f;
                }
            }
        }

        public Vector2 ResolveHitPoint(Collider2D collider)
        {
            if (collider == null)
                return BoundingCenter;
            if (Shape == SpellDeliveryGeometryShape.Segment)
            {
                Vector2 nearest = ClosestPointOnSegment(
                    Start,
                    End,
                    collider.bounds.center);
                return collider.ClosestPoint(nearest);
            }
            return collider.ClosestPoint(Center);
        }

        public Vector2 ResolveHitNormal(GameObject target)
        {
            if (target == null)
                return Vector2.zero;
            Vector2 reference = Shape == SpellDeliveryGeometryShape.Segment
                ? ClosestPointOnSegment(
                    Start,
                    End,
                    target.transform.position)
                : Center;
            Vector2 offset =
                (Vector2)target.transform.position - reference;
            return offset.sqrMagnitude > 0.000001f
                ? offset.normalized
                : Direction;
        }

        private static SpellDeliveryGeometry Circle(
            Vector2 center,
            float radius,
            SpellDeliveryGeometryShape shape,
            Transform follow = null,
            Vector2 followOrigin = default)
        {
            return new SpellDeliveryGeometry(
                shape,
                center,
                radius,
                Vector2.up,
                360f,
                center,
                center,
                0f,
                follow,
                followOrigin);
        }

        internal static Vector2 ClosestPointOnSegment(
            Vector2 start,
            Vector2 end,
            Vector2 point)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
                return start;
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / lengthSquared);
            return start + segment * t;
        }
    }
}
