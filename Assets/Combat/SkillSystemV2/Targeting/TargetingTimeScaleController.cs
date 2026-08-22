using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public interface ITargetingTimeController
    {
        void Acquire(object owner, float requestedTimeScale);
        void Release(object owner);
    }

    [DisallowMultipleComponent]
    public sealed class TargetingTimeScaleController : MonoBehaviour,
        ITargetingTimeController
    {
        private readonly Dictionary<object, float> requests =
            new Dictionary<object, float>();
        private readonly List<object> staleOwners = new List<object>();

        private float capturedTimeScale = 1f;
        private float capturedFixedDeltaTime = 0.02f;
        private float appliedTimeScale = 1f;
        private bool ownsTimeScale;

        public bool HasRequests => requests.Count > 0;

        private void Update()
        {
            PruneInactiveOwnersNow();
        }

        /// <summary>
        /// Immediately removes destroyed or inactive targeting owners.
        /// Exposed so diagnostics and deterministic EditMode tests do not
        /// need to invoke Unity's private Update message indirectly.
        /// </summary>
        public void PruneInactiveOwnersNow()
        {
            PruneStaleOwners();
        }

        public void Acquire(object owner, float requestedTimeScale)
        {
            if (owner == null)
                return;

            if (!ownsTimeScale)
            {
                capturedTimeScale = Time.timeScale;
                capturedFixedDeltaTime = Time.fixedDeltaTime;
                ownsTimeScale = true;
            }

            requests[owner] = Mathf.Clamp(requestedTimeScale, 0.01f, 1f);
            ApplyLowestRequest();
        }

        public void Release(object owner)
        {
            if (owner == null || !requests.Remove(owner))
                return;

            if (requests.Count > 0)
            {
                ApplyLowestRequest();
                return;
            }

            RestoreCapturedTime();
        }

        private void ApplyLowestRequest()
        {
            float lowest = 1f;

            foreach (float request in requests.Values)
                lowest = Mathf.Min(lowest, request);

            appliedTimeScale = Mathf.Min(capturedTimeScale, lowest);
            Time.timeScale = appliedTimeScale;

            if (capturedTimeScale > 0.0001f)
            {
                Time.fixedDeltaTime = capturedFixedDeltaTime *
                                      (Time.timeScale / capturedTimeScale);
            }
        }

        private void RestoreCapturedTime()
        {
            if (!ownsTimeScale)
                return;

            // Normal release and a stronger transient slowdown (hitstop) may
            // restore the captured baseline. If time is already faster than
            // the scale we applied, an outer owner such as the skill menu has
            // restored first; never write our older slow baseline over it.
            if (Time.timeScale <= appliedTimeScale + 0.0001f)
            {
                Time.timeScale = capturedTimeScale;
                Time.fixedDeltaTime = capturedFixedDeltaTime;
            }

            requests.Clear();
            ownsTimeScale = false;
            appliedTimeScale = Time.timeScale;
        }

        private void PruneStaleOwners()
        {
            if (requests.Count == 0)
                return;

            staleOwners.Clear();
            foreach (KeyValuePair<object, float> request in requests)
            {
                object owner = request.Key;
                if (owner is Object unityOwner && unityOwner == null)
                {
                    staleOwners.Add(owner);
                    continue;
                }

                if (owner is PlayerSpellTargetingController targeting &&
                    !targeting.IsTargeting)
                {
                    staleOwners.Add(owner);
                }
            }

            if (staleOwners.Count == 0)
                return;

            for (int i = 0; i < staleOwners.Count; i++)
                requests.Remove(staleOwners[i]);

            staleOwners.Clear();
            if (requests.Count > 0)
                ApplyLowestRequest();
            else
                RestoreCapturedTime();
        }

        private void OnDisable()
        {
            RestoreCapturedTime();
        }
    }
}
