using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Optional immutable targeting data for deliveries that need more than
    /// one point. Player UI and enemy AI both construct the same payload.
    /// </summary>
    public sealed class SpellTargetingPayload
    {
        private readonly Vector2[] points;

        public int PointCount => points.Length;

        public SpellTargetingPayload(params Vector2[] targetPoints)
        {
            points = targetPoints != null && targetPoints.Length > 0
                ? (Vector2[])targetPoints.Clone()
                : Array.Empty<Vector2>();
        }

        public bool TryGetPoint(int index, out Vector2 point)
        {
            if (index >= 0 && index < points.Length)
            {
                point = points[index];
                return true;
            }

            point = default;
            return false;
        }

        public Vector2 GetPointOrDefault(int index, Vector2 fallback)
        {
            return index >= 0 && index < points.Length
                ? points[index]
                : fallback;
        }
    }
}
