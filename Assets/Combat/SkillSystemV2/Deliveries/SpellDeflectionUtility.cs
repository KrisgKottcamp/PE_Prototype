using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Shared bridge used by player and enemy basic attacks. The attack only
    /// reports its owner and direction; the delivery decides whether and how
    /// it can be deflected.
    /// </summary>
    public static class SpellDeflectionUtility
    {
        private static readonly Collider2D[] overlapBuffer =
            new Collider2D[64];

        public static bool TryDeflect(
            GameObject contactedObject,
            GameObject newCaster,
            Vector2 direction)
        {
            if (contactedObject == null || newCaster == null)
                return false;

            MonoBehaviour[] behaviours =
                contactedObject.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellDeflectableDelivery delivery &&
                    delivery.TryDeflect(newCaster, direction))
                {
                    return true;
                }
            }

            return false;
        }

        public static int DeflectInCircle(
            GameObject newCaster,
            Vector2 center,
            float radius,
            Vector2 direction,
            LayerMask mask)
        {
            int count = Physics2D.OverlapCircleNonAlloc(
                center,
                Mathf.Max(0.01f, radius),
                overlapBuffer,
                mask);
            int deflected = 0;
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapBuffer[i];
                if (hit != null && TryDeflect(
                        hit.gameObject,
                        newCaster,
                        direction))
                {
                    deflected++;
                }
            }
            return deflected;
        }

        public static int DeflectInBox(
            GameObject newCaster,
            Vector2 center,
            Vector2 size,
            float angle,
            Vector2 direction,
            LayerMask mask)
        {
            int count = Physics2D.OverlapBoxNonAlloc(
                center,
                size,
                angle,
                overlapBuffer,
                mask);
            int deflected = 0;
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapBuffer[i];
                if (hit != null && TryDeflect(
                        hit.gameObject,
                        newCaster,
                        direction))
                {
                    deflected++;
                }
            }
            return deflected;
        }
    }
}
