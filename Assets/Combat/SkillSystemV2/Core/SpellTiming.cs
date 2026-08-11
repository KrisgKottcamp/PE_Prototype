using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public struct SpellTiming
    {
        [Tooltip("Seconds spent preparing before the delivery begins. Use zero for an immediate cast.")]
        [SerializeField, Min(0f)]
        private float buildUpDuration;

        [Tooltip("Seconds the spell remains in its firing phase after the delivery begins.")]
        [SerializeField, Min(0f)]
        private float firingDuration;

        [Tooltip("Seconds the caster maintains the spell before recovery begins.")]
        [SerializeField, Min(0f)]
        private float channelDuration;

        [Tooltip("Seconds after the spell finishes before the cast is fully complete.")]
        [SerializeField, Min(0f)]
        private float recoveryDuration;

        [Tooltip("Scaled time follows slow motion and pauses. Unscaled time continues at real-world speed.")]
        [SerializeField]
        private SpellTimeMode timeMode;

        [Tooltip("Optional player action restrictions and power-up feedback used only during Build Up.")]
        [SerializeField]
        private SpellBuildUpControl buildUpControl;

        public float BuildUpDuration => Mathf.Max(0f, buildUpDuration);
        public float FiringDuration => Mathf.Max(0f, firingDuration);
        public float ChannelDuration => Mathf.Max(0f, channelDuration);
        public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);
        public SpellTimeMode TimeMode => timeMode;
        public SpellBuildUpControl BuildUpControl => buildUpControl;

        public float TotalDuration =>
            BuildUpDuration +
            FiringDuration +
            ChannelDuration +
            RecoveryDuration;

        public float GetDuration(SpellCastPhase phase)
        {
            switch (phase)
            {
                case SpellCastPhase.BuildUp:
                    return BuildUpDuration;
                case SpellCastPhase.Firing:
                    return FiringDuration;
                case SpellCastPhase.Channeling:
                    return ChannelDuration;
                case SpellCastPhase.Recovery:
                    return RecoveryDuration;
                default:
                    return 0f;
            }
        }
    }
}
