using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Lightweight rate modifier for V2 moving spell objects. It intentionally
    /// changes movement speed without changing lifetime or effect potency.
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
                float value = 1f;
                foreach (Entry entry in entries.Values)
                    value = Mathf.Min(value, entry.Multiplier);
                return Mathf.Clamp(value, 0.02f, 1f);
            }
        }

        public void ApplySlow(
            Object source,
            float movementMultiplier,
            float duration)
        {
            if (source == null)
                return;

            entries[source.GetInstanceID()] = new Entry
            {
                Multiplier = Mathf.Clamp(movementMultiplier, 0.02f, 1f),
                ExpiresAt = Time.time + Mathf.Max(0.02f, duration),
                Persistent = false
            };
        }

        public void SetSlow(
            Object source,
            float movementMultiplier)
        {
            if (source == null)
                return;

            entries[source.GetInstanceID()] = new Entry
            {
                Multiplier = Mathf.Clamp(movementMultiplier, 0.02f, 1f),
                ExpiresAt = 0f,
                Persistent = true
            };
        }

        public void ClearSlow(Object source)
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
