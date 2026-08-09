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

        private float capturedTimeScale = 1f;
        private float capturedFixedDeltaTime = 0.02f;
        private bool ownsTimeScale;

        public bool HasRequests => requests.Count > 0;

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

            Time.timeScale = Mathf.Min(capturedTimeScale, lowest);

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

            Time.timeScale = capturedTimeScale;
            Time.fixedDeltaTime = capturedFixedDeltaTime;
            requests.Clear();
            ownsTimeScale = false;
        }

        private void OnDisable()
        {
            RestoreCapturedTime();
        }
    }
}
