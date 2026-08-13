using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum ProjectileEmissionPattern
    {
        Forward,
        Fan,
        Ring,
        RandomCone
    }

    public enum ProjectileMotionPattern
    {
        Straight,
        Homing,
        Boomerang
    }

    public enum ProjectileHitShape
    {
        Projectile,
        InstantBeam,
        Cone
    }

    public enum ProjectileDamageFalloff
    {
        None,
        DistanceTraveled
    }

    [Serializable]
    public sealed class ProjectileEmissionSettings
    {
        [Tooltip("Forward shoots along aim, Fan distributes shots across an arc, Ring distributes them around the caster, and Random Cone scatters within an arc.")]
        [SerializeField] private ProjectileEmissionPattern pattern =
            ProjectileEmissionPattern.Forward;
        [Tooltip("Total number of shots produced by one cast.")]
        [SerializeField, Min(1)] private int projectileCount = 1;
        [Tooltip("Total angle occupied by a fan or random cone. Ring always uses 360 degrees.")]
        [SerializeField, Range(0f, 360f)] private float spreadAngle = 30f;
        [Tooltip("Seconds between shots. Zero fires every shot simultaneously; a positive value creates rapid fire.")]
        [SerializeField, Min(0f)] private float shotInterval;
        [Tooltip("When enabled, sequential shots update their forward direction toward the caster's current selected target.")]
        [SerializeField] private bool reAimSequentialShots;

        public ProjectileEmissionPattern Pattern => pattern;
        public int ProjectileCount => Mathf.Max(1, projectileCount);
        public float SpreadAngle => Mathf.Clamp(spreadAngle, 0f, 360f);
        public float ShotInterval => Mathf.Max(0f, shotInterval);
        public bool ReAimSequentialShots => reAimSequentialShots;

        public ProjectileEmissionSettings() { }

        public ProjectileEmissionSettings(
            ProjectileEmissionPattern emissionPattern,
            int count,
            float arc,
            float interval = 0f,
            bool reAim = false)
        {
            pattern = emissionPattern;
            projectileCount = Mathf.Max(1, count);
            spreadAngle = Mathf.Clamp(arc, 0f, 360f);
            shotInterval = Mathf.Max(0f, interval);
            reAimSequentialShots = reAim;
        }

        public ProjectileEmissionSettings Clone()
        {
            return new ProjectileEmissionSettings(
                pattern,
                projectileCount,
                spreadAngle,
                shotInterval,
                reAimSequentialShots);
        }
    }

    [Serializable]
    public sealed class ProjectileMotionSettings
    {
        [Tooltip("Straight travels along its launch direction, Homing turns toward valid targets, and Boomerang returns to its caster.")]
        [SerializeField] private ProjectileMotionPattern pattern =
            ProjectileMotionPattern.Straight;
        [Tooltip("How far a homing shot searches for valid targets.")]
        [SerializeField, Min(0.1f)] private float homingAcquireRadius = 6f;
        [Tooltip("Maximum homing rotation in degrees per second.")]
        [SerializeField, Min(0f)] private float homingTurnRate = 180f;
        [Tooltip("Boomerang begins returning after traveling this fraction of its maximum range.")]
        [SerializeField, Range(0.05f, 0.95f)] private float returnAtRangeFraction = 0.5f;
        [Tooltip("Distance from the caster at which a returning boomerang completes.")]
        [SerializeField, Min(0.01f)] private float returnCatchRadius = 0.25f;

        public ProjectileMotionPattern Pattern => pattern;
        public float HomingAcquireRadius => Mathf.Max(0.1f, homingAcquireRadius);
        public float HomingTurnRate => Mathf.Max(0f, homingTurnRate);
        public float ReturnAtRangeFraction =>
            Mathf.Clamp(returnAtRangeFraction, 0.05f, 0.95f);
        public float ReturnCatchRadius => Mathf.Max(0.01f, returnCatchRadius);

        public ProjectileMotionSettings() { }

        public ProjectileMotionSettings(
            ProjectileMotionPattern motionPattern,
            float acquireRadius = 6f,
            float turnRate = 180f,
            float returnFraction = 0.5f,
            float catchRadius = 0.25f)
        {
            pattern = motionPattern;
            homingAcquireRadius = Mathf.Max(0.1f, acquireRadius);
            homingTurnRate = Mathf.Max(0f, turnRate);
            returnAtRangeFraction = Mathf.Clamp(returnFraction, 0.05f, 0.95f);
            returnCatchRadius = Mathf.Max(0.01f, catchRadius);
        }

        public ProjectileMotionSettings Clone()
        {
            return new ProjectileMotionSettings(
                pattern,
                homingAcquireRadius,
                homingTurnRate,
                returnAtRangeFraction,
                returnCatchRadius);
        }
    }

    [Serializable]
    public sealed class ProjectileShapeSettings
    {
        [Tooltip("Projectile creates a moving object. Instant Beam hits along a line immediately. Cone hits valid targets inside an aimed arc immediately.")]
        [SerializeField] private ProjectileHitShape hitShape =
            ProjectileHitShape.Projectile;
        [Tooltip("Collision width of an Instant Beam.")]
        [SerializeField, Min(0f)] private float beamWidth = 0.12f;
        [Tooltip("Total angle of a Cone hit shape.")]
        [SerializeField, Range(0.1f, 360f)] private float coneAngle = 45f;

        public ProjectileHitShape HitShape => hitShape;
        public float BeamWidth => Mathf.Max(0f, beamWidth);
        public float ConeAngle => Mathf.Clamp(coneAngle, 0.1f, 360f);

        public ProjectileShapeSettings() { }

        public ProjectileShapeSettings(
            ProjectileHitShape shape,
            float width = 0.12f,
            float angle = 45f)
        {
            hitShape = shape;
            beamWidth = Mathf.Max(0f, width);
            coneAngle = Mathf.Clamp(angle, 0.1f, 360f);
        }

        public ProjectileShapeSettings Clone()
        {
            return new ProjectileShapeSettings(
                hitShape,
                beamWidth,
                coneAngle);
        }
    }

    [Serializable]
    public sealed class ProjectileFalloffSettings
    {
        [Tooltip("Optionally reduce effect potency as a shot travels farther from its origin.")]
        [SerializeField] private ProjectileDamageFalloff mode =
            ProjectileDamageFalloff.None;
        [Tooltip("Lowest effect multiplier at maximum range. 0.5 means half damage or healing at the end of the shot.")]
        [SerializeField, Range(0f, 1f)] private float minimumPotency = 0.5f;
        [Tooltip("Shapes the falloff curve. 1 is linear, values above 1 retain strength longer, and values below 1 fall off earlier.")]
        [SerializeField, Min(0.05f)] private float curveExponent = 1f;

        public ProjectileDamageFalloff Mode => mode;
        public float MinimumPotency => Mathf.Clamp01(minimumPotency);
        public float CurveExponent => Mathf.Max(0.05f, curveExponent);

        public ProjectileFalloffSettings() { }

        public ProjectileFalloffSettings(
            ProjectileDamageFalloff falloffMode,
            float minimum,
            float exponent = 1f)
        {
            mode = falloffMode;
            minimumPotency = Mathf.Clamp01(minimum);
            curveExponent = Mathf.Max(0.05f, exponent);
        }

        public float Evaluate(float normalizedDistance)
        {
            if (mode == ProjectileDamageFalloff.None)
                return 1f;

            float t = Mathf.Pow(
                Mathf.Clamp01(normalizedDistance),
                curveExponent);
            return Mathf.Lerp(1f, minimumPotency, t);
        }

        public ProjectileFalloffSettings Clone()
        {
            return new ProjectileFalloffSettings(
                mode,
                minimumPotency,
                curveExponent);
        }
    }
}
