using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public interface ISpellDeliveryInteractionVolume
    {
        int InteractionRuntimeId { get; }
        SpellExecutionContext InteractionExecutionContext { get; }
        Vector2 InteractionCenter { get; }
        float InteractionRadius { get; }
        void ReceiveInteraction(in DeliveryInteractionContext context);
    }

    /// <summary>
    /// Normalizes contacts between every delivery shape and registered
    /// persistent delivery volume. It deliberately contains geometry only;
    /// reactions and outcomes remain authored on the receiving spell.
    /// </summary>
    public static class SpellDeliveryInteractionService
    {
        private static readonly List<ISpellDeliveryInteractionVolume> volumes =
            new List<ISpellDeliveryInteractionVolume>();

        public static int RegisteredVolumeCount
        {
            get
            {
                PruneDestroyed();
                return volumes.Count;
            }
        }

        public static void Register(ISpellDeliveryInteractionVolume volume)
        {
            if (volume == null ||
                volume.InteractionExecutionContext.SuppressGameplayEffects)
                return;

            PruneDestroyed();
            if (volumes.Contains(volume))
                return;

            for (int i = 0; i < volumes.Count; i++)
            {
                ISpellDeliveryInteractionVolume other = volumes[i];
                if (!CirclesOverlap(
                        volume.InteractionCenter,
                        volume.InteractionRadius,
                        other.InteractionCenter,
                        other.InteractionRadius))
                {
                    continue;
                }

                Vector2 point = Midpoint(
                    volume.InteractionCenter,
                    other.InteractionCenter);
                Notify(
                    other,
                    volume.InteractionExecutionContext,
                    DeliveryContactPhase.Enter,
                    point,
                    volume.InteractionRuntimeId);
                Notify(
                    volume,
                    other.InteractionExecutionContext,
                    DeliveryContactPhase.Enter,
                    point,
                    other.InteractionRuntimeId);
            }

            volumes.Add(volume);
        }

        public static void Unregister(ISpellDeliveryInteractionVolume volume)
        {
            if (volume != null)
                volumes.Remove(volume);
        }

        public static int EmitPoint(
            in SpellExecutionContext source,
            Vector2 point,
            DeliveryContactPhase phase = DeliveryContactPhase.Impact,
            int sourceRuntimeId = 0,
            ISet<int> contactedVolumes = null)
        {
            return EmitCircle(
                source,
                point,
                0f,
                phase,
                sourceRuntimeId,
                contactedVolumes);
        }

        public static int EmitCircle(
            in SpellExecutionContext source,
            Vector2 center,
            float radius,
            DeliveryContactPhase phase = DeliveryContactPhase.Impact,
            int sourceRuntimeId = 0,
            ISet<int> contactedVolumes = null)
        {
            if (source.SuppressGameplayEffects)
                return 0;

            PruneDestroyed();
            int count = 0;
            float safeRadius = Mathf.Max(0f, radius);
            for (int i = 0; i < volumes.Count; i++)
            {
                ISpellDeliveryInteractionVolume volume = volumes[i];
                if (volume.InteractionRuntimeId == sourceRuntimeId ||
                    !CirclesOverlap(
                        center,
                        safeRadius,
                        volume.InteractionCenter,
                        volume.InteractionRadius) ||
                    (contactedVolumes != null &&
                     !contactedVolumes.Add(volume.InteractionRuntimeId)))
                {
                    continue;
                }

                Notify(
                    volume,
                    source,
                    phase,
                    ClosestPointOnCircle(
                        center,
                        volume.InteractionCenter,
                        volume.InteractionRadius),
                    sourceRuntimeId);
                count++;
            }

            return count;
        }

        public static int EmitSegment(
            in SpellExecutionContext source,
            Vector2 start,
            Vector2 end,
            float radius,
            DeliveryContactPhase phase = DeliveryContactPhase.Impact,
            int sourceRuntimeId = 0,
            ISet<int> contactedVolumes = null)
        {
            if (source.SuppressGameplayEffects)
                return 0;

            PruneDestroyed();
            int count = 0;
            for (int i = 0; i < volumes.Count; i++)
            {
                ISpellDeliveryInteractionVolume volume = volumes[i];
                if (volume.InteractionRuntimeId == sourceRuntimeId)
                    continue;

                Vector2 closest = ClosestPointOnSegment(
                    start,
                    end,
                    volume.InteractionCenter);
                float combined = Mathf.Max(0f, radius) +
                                 Mathf.Max(0f, volume.InteractionRadius);
                if ((closest - volume.InteractionCenter).sqrMagnitude >
                    combined * combined ||
                    (contactedVolumes != null &&
                     !contactedVolumes.Add(volume.InteractionRuntimeId)))
                {
                    continue;
                }

                Notify(
                    volume,
                    source,
                    phase,
                    closest,
                    sourceRuntimeId);
                count++;
            }

            return count;
        }

        public static int EmitArc(
            in SpellExecutionContext source,
            Vector2 origin,
            Vector2 direction,
            float range,
            float arcAngle,
            DeliveryContactPhase phase = DeliveryContactPhase.Impact,
            int sourceRuntimeId = 0)
        {
            if (source.SuppressGameplayEffects)
                return 0;

            PruneDestroyed();
            Vector2 aim = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector2.up;
            float safeRange = Mathf.Max(0f, range);
            float halfAngle = Mathf.Clamp(arcAngle, 0.1f, 360f) * 0.5f;
            int count = 0;

            for (int i = 0; i < volumes.Count; i++)
            {
                ISpellDeliveryInteractionVolume volume = volumes[i];
                if (volume.InteractionRuntimeId == sourceRuntimeId)
                    continue;

                Vector2 offset = volume.InteractionCenter - origin;
                float allowedRange = safeRange + volume.InteractionRadius;
                if (offset.sqrMagnitude > allowedRange * allowedRange)
                    continue;

                if (offset.sqrMagnitude > 0.000001f && halfAngle < 179.95f)
                {
                    float angle = Vector2.Angle(aim, offset.normalized);
                    float angularPadding = Mathf.Rad2Deg * Mathf.Asin(
                        Mathf.Clamp01(
                            volume.InteractionRadius /
                            Mathf.Max(offset.magnitude, 0.0001f)));
                    if (angle > halfAngle + angularPadding)
                        continue;
                }

                Notify(
                    volume,
                    source,
                    phase,
                    volume.InteractionCenter,
                    sourceRuntimeId);
                count++;
            }

            return count;
        }

        public static void ClearAllRegistrations()
        {
            volumes.Clear();
        }

        private static void Notify(
            ISpellDeliveryInteractionVolume receiver,
            in SpellExecutionContext source,
            DeliveryContactPhase phase,
            Vector2 contactPoint,
            int sourceRuntimeId)
        {
            var context = new DeliveryInteractionContext(
                source,
                receiver.InteractionExecutionContext,
                phase,
                contactPoint,
                sourceRuntimeId);
            receiver.ReceiveInteraction(context);
        }

        private static void PruneDestroyed()
        {
            for (int i = volumes.Count - 1; i >= 0; i--)
            {
                ISpellDeliveryInteractionVolume volume = volumes[i];
                if (volume == null ||
                    (volume is UnityEngine.Object unityObject &&
                     unityObject == null))
                {
                    volumes.RemoveAt(i);
                }
            }
        }

        private static bool CirclesOverlap(
            Vector2 first,
            float firstRadius,
            Vector2 second,
            float secondRadius)
        {
            float combined = Mathf.Max(0f, firstRadius) +
                             Mathf.Max(0f, secondRadius);
            return (first - second).sqrMagnitude <= combined * combined;
        }

        private static Vector2 ClosestPointOnSegment(
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

        private static Vector2 ClosestPointOnCircle(
            Vector2 source,
            Vector2 center,
            float radius)
        {
            Vector2 offset = source - center;
            return offset.sqrMagnitude > 0.000001f
                ? center + offset.normalized * Mathf.Max(0f, radius)
                : center;
        }

        private static Vector2 Midpoint(Vector2 first, Vector2 second)
        {
            return (first + second) * 0.5f;
        }
    }
}
