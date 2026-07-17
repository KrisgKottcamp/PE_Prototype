using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemySlowReceiverV2 : MonoBehaviour
    {
        private struct SlowSource
        {
            public float movementMultiplier;
            public float expiresAt;
            public string debugName;
        }

        [Header("Slow Receiver")]
        [Tooltip("Smallest allowed movement multiplier from any slow zone. Prevents accidental zero/negative values from freezing or breaking locomotion.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float minimumMovementMultiplier = 0.05f;

        [Tooltip("If true, new slow applications are logged. Keep off unless diagnosing Slow Orb behavior.")]
        [SerializeField] private bool logApplications = false;

        [Header("Runtime Debug")]
        [SerializeField] private bool debugIsSlowed;
        [SerializeField] private float debugMovementMultiplier = 1f;
        [SerializeField] private int debugActiveSources;
        [SerializeField] private string debugStrongestSource = "None";

        private readonly Dictionary<int, SlowSource> activeSources = new Dictionary<int, SlowSource>();
        private readonly List<int> removeBuffer = new List<int>();

        public bool IsSlowed
        {
            get
            {
                RefreshDebugAndPrune();
                return debugIsSlowed;
            }
        }

        public float MovementSpeedMultiplier
        {
            get
            {
                RefreshDebugAndPrune();
                return debugMovementMultiplier;
            }
        }

        public string DebugStrongestSource
        {
            get
            {
                RefreshDebugAndPrune();
                return debugStrongestSource;
            }
        }

        public void ApplySlow(Component source, float movementMultiplier, float lingerSeconds)
        {
            if (source == null)
                return;

            int id = source.GetInstanceID();
            float clampedMultiplier = Mathf.Clamp(movementMultiplier, minimumMovementMultiplier, 1f);
            float expiry = Time.time + Mathf.Max(0.02f, lingerSeconds);

            SlowSource entry = new SlowSource
            {
                movementMultiplier = clampedMultiplier,
                expiresAt = expiry,
                debugName = source.name
            };

            activeSources[id] = entry;
            RefreshDebugAndPrune();

            if (logApplications)
            {
                Debug.Log(
                    $"[Enemy AI V2] {name}: slow applied by {source.name}, move x{clampedMultiplier:0.00}, linger {lingerSeconds:0.00}s",
                    this
                );
            }
        }

        public void ClearSlow(Component source)
        {
            if (source == null)
                return;

            activeSources.Remove(source.GetInstanceID());
            RefreshDebugAndPrune();
        }

        public void ClearAllSlows()
        {
            activeSources.Clear();
            RefreshDebugAndPrune();
        }

        private void Update()
        {
            RefreshDebugAndPrune();
        }

        private void RefreshDebugAndPrune()
        {
            removeBuffer.Clear();

            foreach (KeyValuePair<int, SlowSource> pair in activeSources)
            {
                if (Time.time > pair.Value.expiresAt)
                    removeBuffer.Add(pair.Key);
            }

            for (int i = 0; i < removeBuffer.Count; i++)
                activeSources.Remove(removeBuffer[i]);

            debugActiveSources = activeSources.Count;

            if (activeSources.Count == 0)
            {
                debugIsSlowed = false;
                debugMovementMultiplier = 1f;
                debugStrongestSource = "None";
                return;
            }

            float strongest = 1f;
            string strongestName = "Unknown";

            foreach (KeyValuePair<int, SlowSource> pair in activeSources)
            {
                if (pair.Value.movementMultiplier < strongest)
                {
                    strongest = pair.Value.movementMultiplier;
                    strongestName = pair.Value.debugName;
                }
            }

            debugIsSlowed = strongest < 0.999f;
            debugMovementMultiplier = Mathf.Clamp(strongest, minimumMovementMultiplier, 1f);
            debugStrongestSource = debugIsSlowed ? strongestName : "None";
        }
    }
}
