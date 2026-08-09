using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class TimedSpellObject : MonoBehaviour
    {
        private float remaining;
        private SpellTimeMode timeMode;
        private bool initialized;

        public void Initialize(float lifetime, SpellTimeMode mode)
        {
            remaining = Mathf.Max(0f, lifetime);
            timeMode = mode;
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
                return;

            remaining -= timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            if (remaining <= 0f)
                Destroy(gameObject);
        }
    }
}
