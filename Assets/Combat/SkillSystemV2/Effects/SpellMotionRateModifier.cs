using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Lightweight movement-speed modifier for V2 moving spell objects. It
    /// changes travel speed without changing lifetime or effect potency.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpellMotionRateModifier : MonoBehaviour
    {
        private struct Entry
        {
            public float Multiplier;
            public float ExpiresAt;
            public bool Persistent;
        }

        private readonly Dictionary<int, Entry> entries =
            new Dictionary<int, Entry>();
        private readonly List<int> removeBuffer = new List<int>();

        public float Multiplier
        {
            get
            {
                Prune();
                float strongestSlow = 1f;
                float strongestBoost = 1f;
                foreach (Entry entry in entries.Values)
                {
                    if (entry.Multiplier < 1f)
                        strongestSlow = Mathf.Min(
                            strongestSlow,
                            entry.Multiplier);
                    else if (entry.Multiplier > 1f)
                        strongestBoost = Mathf.Max(
                            strongestBoost,
                            entry.Multiplier);
                }

                return Mathf.Clamp(
                    strongestSlow * strongestBoost,
                    0.02f,
                    5f);
            }
        }

        public void ApplySlow(
            Object source,
            float movementMultiplier,
            float duration)
        {
            ApplyInternal(
                source,
                Mathf.Clamp(movementMultiplier, 0.02f, 1f),
                duration,
                persistent: false);
        }

        public void ApplyMovementSpeedChange(
            Object source,
            float movementMultiplier,
            float duration)
        {
            ApplyInternal(
                source,
                movementMultiplier,
                duration,
                persistent: false);
        }

        private void ApplyInternal(
            Object source,
            float movementMultiplier,
            float duration,
            bool persistent)
        {
            if (source == null)
                return;

            entries[source.GetInstanceID()] = new Entry
            {
                Multiplier = Mathf.Clamp(movementMultiplier, 0.02f, 5f),
                ExpiresAt = Time.time + Mathf.Max(0.02f, duration),
                Persistent = persistent
            };
        }

        public void SetSlow(
            Object source,
            float movementMultiplier)
        {
            ApplyInternal(
                source,
                Mathf.Clamp(movementMultiplier, 0.02f, 1f),
                0.02f,
                persistent: true);
        }

        public void SetMovementSpeedChange(
            Object source,
            float movementMultiplier)
        {
            ApplyInternal(
                source,
                movementMultiplier,
                0.02f,
                persistent: true);
        }

        public void ClearSlow(Object source)
        {
            ClearMovementSpeedChange(source);
        }

        public void ClearMovementSpeedChange(Object source)
        {
            if (source != null)
                entries.Remove(source.GetInstanceID());
        }

        private void Update()
        {
            Prune();
        }

        private void Prune()
        {
            removeBuffer.Clear();
            foreach (KeyValuePair<int, Entry> pair in entries)
            {
                if (!pair.Value.Persistent &&
                    pair.Value.ExpiresAt <= Time.time)
                {
                    removeBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < removeBuffer.Count; i++)
                entries.Remove(removeBuffer[i]);
        }
    }
}
