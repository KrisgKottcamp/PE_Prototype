using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Optional player-facing restrictions and feedback active only during a
    /// spell's Build Up phase. Kept on SpellTiming so every delivery can use
    /// the same clear contract without duplicating settings per delivery.
    /// </summary>
    [Serializable]
    public struct SpellBuildUpControl
    {
        [Tooltip("Prevent the player caster from moving while this spell is building up.")]
        [SerializeField] private bool blockPlayerMovement;

        [Tooltip("Prevent the player caster from starting basic attacks while this spell is building up.")]
        [SerializeField] private bool blockPlayerBasicAttacks;

        [Tooltip("Prevent the player from opening or starting another skill while this spell is building up.")]
        [SerializeField] private bool blockPlayerSkillUsage;

        [Tooltip("Show small energy particles converging into the caster during the build up.")]
        [SerializeField] private bool showPowerUpParticles;

        [Tooltip("Particle color used for the build-up power effect.")]
        [SerializeField] private Color particleColor;

        [Tooltip("How many power-up particles appear each second.")]
        [SerializeField, Min(1f)] private float particlesPerSecond;

        [Tooltip("Distance from the caster where power-up particles begin.")]
        [SerializeField, Min(0.05f)] private float particleSpawnRadius;

        [Tooltip("How quickly particles travel inward toward the caster.")]
        [SerializeField, Min(0.01f)] private float particleInwardSpeed;

        [Tooltip("World-space size of each power-up particle.")]
        [SerializeField, Min(0.01f)] private float particleSize;

        public bool BlocksPlayerMovement => blockPlayerMovement;
        public bool BlocksPlayerBasicAttacks => blockPlayerBasicAttacks;
        public bool BlocksPlayerSkillUsage => blockPlayerSkillUsage;
        public bool ShowPowerUpParticles => showPowerUpParticles;
        public Color ParticleColor => particleColor.a > 0f
            ? particleColor
            : new Color(0.35f, 0.9f, 1f, 0.9f);
        public float ParticlesPerSecond => particlesPerSecond > 0f
            ? particlesPerSecond
            : 16f;
        public float ParticleSpawnRadius => particleSpawnRadius > 0f
            ? particleSpawnRadius
            : 0.85f;
        public float ParticleInwardSpeed => particleInwardSpeed > 0f
            ? particleInwardSpeed
            : 4f;
        public float ParticleSize => particleSize > 0f
            ? particleSize
            : 0.055f;
    }
}
