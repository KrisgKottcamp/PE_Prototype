using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public struct SpellTiming
    {
        [SerializeField, Min(0f)]
        private float buildUpDuration;

        [SerializeField, Min(0f)]
        private float firingDuration;

        [SerializeField, Min(0f)]
        private float channelDuration;

        [SerializeField, Min(0f)]
        private float recoveryDuration;

        [SerializeField]
        private SpellTimeMode timeMode;

        public float BuildUpDuration => Mathf.Max(0f, buildUpDuration);
        public float FiringDuration => Mathf.Max(0f, firingDuration);
        public float ChannelDuration => Mathf.Max(0f, channelDuration);
        public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);
        public SpellTimeMode TimeMode => timeMode;

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
