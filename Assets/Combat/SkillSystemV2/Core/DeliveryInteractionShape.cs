using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum DeliveryInteractionShapeKind
    {
        Point,
        Circle,
        Segment
    }

    /// <summary>
    /// Lightweight world-space shape used only for delivery-to-delivery
    /// contacts. Character collision remains owned by Unity Physics2D.
    /// </summary>
    public readonly struct DeliveryInteractionShape
    {
        public DeliveryInteractionShapeKind Kind { get; }
        public Vector2 Start { get; }
        public Vector2 End { get; }
        public float Radius { get; }

        public Vector2 Center => Kind == DeliveryInteractionShapeKind.Segment
            ? (Start + End) * 0.5f
            : Start;

        public float BoundingRadius =>
            Kind == DeliveryInteractionShapeKind.Segment
                ? Vector2.Distance(Start, End) * 0.5f + Radius
                : Radius;

        private DeliveryInteractionShape(
            DeliveryInteractionShapeKind kind,
            Vector2 start,
            Vector2 end,
            float radius)
        {
            Kind = kind;
            Start = start;
            End = end;
            Radius = Mathf.Max(0f, radius);
        }

        public static DeliveryInteractionShape Point(Vector2 point)
        {
            return new DeliveryInteractionShape(
                DeliveryInteractionShapeKind.Point,
                point,
                point,
                0f);
        }

        public static DeliveryInteractionShape Circle(
            Vector2 center,
            float radius)
        {
            return new DeliveryInteractionShape(
                DeliveryInteractionShapeKind.Circle,
                center,
                center,
                radius);
        }

        public static DeliveryInteractionShape Segment(
            Vector2 start,
            Vector2 end,
            float radius)
        {
            return new DeliveryInteractionShape(
                DeliveryInteractionShapeKind.Segment,
                start,
                end,
                radius);
        }
    }

    public interface ISpellDeliveryInteractionShapeProvider
    {
        DeliveryInteractionShape InteractionShape { get; }
    }

    internal static class DeliveryInteractionGeometry
    {
        public static bool TryIntersect(
            in DeliveryInteractionShape first,
            in DeliveryInteractionShape second,
            out Vector2 contactPoint)
        {
            if (first.Kind == DeliveryInteractionShapeKind.Segment &&
                second.Kind == DeliveryInteractionShapeKind.Segment)
            {
                ClosestPointsBetweenSegments(
                    first.Start,
                    first.End,
                    second.Start,
                    second.End,
                    out Vector2 firstPoint,
                    out Vector2 secondPoint);
                float radius = first.Radius + second.Radius;
                contactPoint = (firstPoint + secondPoint) * 0.5f;
                return (firstPoint - secondPoint).sqrMagnitude <=
                       radius * radius;
            }

            if (first.Kind == DeliveryInteractionShapeKind.Segment)
            {
                Vector2 closest = ClosestPointOnSegment(
                    first.Start,
                    first.End,
                    second.Center);
                float radius = first.Radius + second.Radius;
                contactPoint = closest;
                return (closest - second.Center).sqrMagnitude <=
                       radius * radius;
            }

            if (second.Kind == DeliveryInteractionShapeKind.Segment)
                return TryIntersect(second, first, out contactPoint);

            float combined = first.Radius + second.Radius;
            Vector2 offset = second.Center - first.Center;
            bool overlaps = offset.sqrMagnitude <= combined * combined;
            contactPoint = offset.sqrMagnitude > 0.000001f
                ? first.Center + offset.normalized * first.Radius
                : first.Center;
            return overlaps;
        }

        public static Vector2 ClosestPointOnSegment(
            Vector2 start,
            Vector2 end,
            Vector2 point)
        {
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.000001f)
                return start;

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) /
                                    denominator);
            return start + segment * t;
        }

        private static void ClosestPointsBetweenSegments(
            Vector2 firstStart,
            Vector2 firstEnd,
            Vector2 secondStart,
            Vector2 secondEnd,
            out Vector2 firstPoint,
            out Vector2 secondPoint)
        {
            Vector2 firstDirection = firstEnd - firstStart;
            Vector2 secondDirection = secondEnd - secondStart;
            Vector2 offset = firstStart - secondStart;
            float firstLength = firstDirection.sqrMagnitude;
            float secondLength = secondDirection.sqrMagnitude;
            float product = Vector2.Dot(firstDirection, secondDirection);
            float firstOffset = Vector2.Dot(firstDirection, offset);
            float secondOffset = Vector2.Dot(secondDirection, offset);
            float denominator = firstLength * secondLength -
                                product * product;

            float firstT = denominator > 0.000001f
                ? Mathf.Clamp01(
                    (product * secondOffset -
                     firstOffset * secondLength) / denominator)
                : 0f;
            float secondT = secondLength > 0.000001f
                ? Mathf.Clamp01(
                    (product * firstT + secondOffset) / secondLength)
                : 0f;
            if (firstLength > 0.000001f)
            {
                firstT = Mathf.Clamp01(
                    (product * secondT - firstOffset) / firstLength);
            }

            firstPoint = firstStart + firstDirection * firstT;
            secondPoint = secondStart + secondDirection * secondT;
        }
    }
}
