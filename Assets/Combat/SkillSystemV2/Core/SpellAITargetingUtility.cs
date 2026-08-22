using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Shared, deterministic targeting math for AI adapters. This contains no
    /// enemy-brain policy: it only estimates delivery timing and creates a
    /// bounded set of point candidates that any planner may score.
    /// </summary>
    public static class SpellAITargetingUtility
    {
        public static float EstimateArrivalDelay(
            SpellDefinition spell,
            Vector2 origin,
            Vector2 targetPoint,
            float maximumPredictionSeconds = 1.5f)
        {
            if (spell == null)
                return 0f;

            float delay = spell.Timing.BuildUpDuration;
            float distance = Vector2.Distance(origin, targetPoint);
            SpellDeliverySettings settings = spell.DeliverySettings;

            if (settings is ProjectileDeliverySettings projectile)
                delay += distance / projectile.Speed;
            else if (settings is RicochetProjectileDeliverySettings ricochet)
                delay += distance / ricochet.Speed;
            else if (settings is GrenadeDeliverySettings grenade)
            {
                float travel = distance / grenade.Speed;
                delay += Mathf.Min(travel, grenade.FuseDuration);
            }

            return Mathf.Clamp(
                delay,
                0f,
                Mathf.Max(0f, maximumPredictionSeconds));
        }

        public static Vector2 PredictTargetPoint(
            Vector2 currentPosition,
            Vector2 velocity,
            float predictionSeconds,
            float maximumLeadDistance)
        {
            Vector2 lead = velocity * Mathf.Max(0f, predictionSeconds);
            float limit = Mathf.Max(0f, maximumLeadDistance);
            if (limit > 0f && lead.sqrMagnitude > limit * limit)
                lead = lead.normalized * limit;
            return currentPosition + lead;
        }

        public static void BuildPointCandidates(
            List<Vector2> output,
            Vector2 currentPosition,
            Vector2 predictedPosition,
            Vector2 movementDirection,
            float sampleRadius,
            int radialSampleCount,
            SpellAIPlacementIntent placementIntent)
        {
            if (output == null)
                return;

            output.Clear();
            AddUnique(output, predictedPosition);
            AddUnique(output, currentPosition);

            float radius = Mathf.Max(0f, sampleRadius);
            Vector2 forward = movementDirection.sqrMagnitude > 0.000001f
                ? movementDirection.normalized
                : Vector2.zero;

            if (radius > 0f && forward != Vector2.zero)
            {
                if (placementIntent == SpellAIPlacementIntent.ControlEscapeRoute)
                    AddUnique(output, predictedPosition + forward * radius);
                else if (placementIntent == SpellAIPlacementIntent.ProtectSelf)
                    AddUnique(output, currentPosition - forward * radius);
            }

            int count = Mathf.Clamp(radialSampleCount, 0, 16);
            for (int i = 0; i < count && radius > 0f; i++)
            {
                float radians = Mathf.PI * 2f * i / Mathf.Max(1, count);
                AddUnique(output, predictedPosition +
                    new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
            }
        }

        private static void AddUnique(List<Vector2> output, Vector2 point)
        {
            for (int i = 0; i < output.Count; i++)
            {
                if ((output[i] - point).sqrMagnitude <= 0.000001f)
                    return;
            }
            output.Add(point);
        }
    }
}
