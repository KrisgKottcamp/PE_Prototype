using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemySlowReceiverV2 : MonoBehaviour
    {
        private struct MovementSource
        {
            public float movementMultiplier;
            public float expiresAt;
            public string debugName;
        }

        [Header("Movement Speed Modifier Receiver")]
        [Tooltip("Smallest allowed final movement multiplier. Prevents accidental zero or negative values from breaking locomotion.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float minimumMovementMultiplier = 0.05f;

        [Tooltip("Largest allowed final movement multiplier. Prevents accidental extreme speed values.")]
        [Range(1f, 10f)]
        [SerializeField] private float maximumMovementMultiplier = 5f;

        [Tooltip("If true, new movement-speed changes are logged. Keep off unless diagnosing spell movement modifiers.")]
        [SerializeField] private bool logApplications = false;

        [Header("Runtime Debug")]
        [SerializeField] private bool debugIsSlowed;
        [SerializeField] private bool debugHasSpeedBoost;
        [SerializeField] private float debugMovementMultiplier = 1f;
        [SerializeField] private float debugSpeedBoostMultiplier = 1f;
        [SerializeField] private int debugActiveSources;
        [SerializeField] private string debugStrongestSource = "None";
        [SerializeField] private string debugStrongestBoostSource = "None";

        private readonly Dictionary<int, MovementSource> activeSources = new Dictionary<int, MovementSource>();
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
            ApplyInternal(
                source,
                Mathf.Clamp(movementMultiplier, minimumMovementMultiplier, 1f),
                lingerSeconds);
        }

        public void ApplyMovementSpeedChange(
            Component source,
            float movementMultiplier,
            float duration)
        {
            ApplyInternal(
                source,
                Mathf.Clamp(
                    movementMultiplier,
                    minimumMovementMultiplier,
                    maximumMovementMultiplier),
                duration);
        }

        private void ApplyInternal(
            Component source,
            float movementMultiplier,
            float duration)
        {
            if (source == null)
                return;

            int id = source.GetInstanceID();
            float clampedMultiplier = Mathf.Clamp(
                movementMultiplier,
                minimumMovementMultiplier,
                maximumMovementMultiplier);
            float expiry = Time.time + Mathf.Max(0.02f, duration);

            MovementSource entry = new MovementSource
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
                    $"[Enemy AI V2] {name}: movement speed changed by {source.name}, move x{clampedMultiplier:0.00}, duration {duration:0.00}s",
                    this
                );
            }
        }

        public void ClearSlow(Component source)
        {
            ClearMovementSpeedChange(source);
        }

        public void ClearMovementSpeedChange(Component source)
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

            foreach (KeyValuePair<int, MovementSource> pair in activeSources)
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
                debugHasSpeedBoost = false;
                debugMovementMultiplier = 1f;
                debugSpeedBoostMultiplier = 1f;
                debugStrongestSource = "None";
                debugStrongestBoostSource = "None";
                return;
            }

            float strongestSlow = 1f;
            float strongestBoost = 1f;
            string strongestSlowName = "None";
            string strongestBoostName = "None";

            foreach (KeyValuePair<int, MovementSource> pair in activeSources)
            {
                if (pair.Value.movementMultiplier < strongestSlow)
                {
                    strongestSlow = pair.Value.movementMultiplier;
                    strongestSlowName = pair.Value.debugName;
                }
                else if (pair.Value.movementMultiplier > strongestBoost)
                {
                    strongestBoost = pair.Value.movementMultiplier;
                    strongestBoostName = pair.Value.debugName;
                }
            }

            debugMovementMultiplier = Mathf.Clamp(
                strongestSlow * strongestBoost,
                minimumMovementMultiplier,
                maximumMovementMultiplier);
            debugSpeedBoostMultiplier = strongestBoost;
            debugIsSlowed = debugMovementMultiplier < 0.999f;
            debugHasSpeedBoost = strongestBoost > 1.001f;
            debugStrongestSource = debugIsSlowed
                ? strongestSlowName
                : debugHasSpeedBoost
                    ? strongestBoostName
                    : "None";
            debugStrongestBoostSource = debugHasSpeedBoost
                ? strongestBoostName
                : "None";
        }
    }
}
