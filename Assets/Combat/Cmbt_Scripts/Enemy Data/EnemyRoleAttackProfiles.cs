using System;
using UnityEngine;

/// <summary>
/// Inspector-editable role-to-attack-pattern mapping for Project Eri combat.
///
/// Design intent:
/// - EnemySquadRole decides the tactical job: suppress, flank, anchor, retreat.
/// - Enemy personality still modifies attitude/cadence inside EnemyBrain.
/// - This asset owns the exact pretty danmaku pattern choice for each role/intensity.
///
/// Create an asset from:
/// Assets > Create > Combat > Enemy Role Attack Profiles
/// </summary>
[CreateAssetMenu(fileName = "RoleAttackProfiles_Default", menuName = "Combat/Enemy Role Attack Profiles")]
public class EnemyRoleAttackProfiles : ScriptableObject
{
    [Header("Global Density Tuning v4")]
    [Tooltip("Scales fan bullet minimums after slot values. Existing assets can lower density here without editing every slot.")]
    [Range(0.35f, 1.25f)] public float fanBulletDensityMultiplier = 0.65f;

    [Tooltip("Scales ring bullet minimums after slot values. Existing assets can lower density here without editing every slot.")]
    [Range(0.35f, 1.25f)] public float ringBulletDensityMultiplier = 0.65f;

    [Tooltip("Scales fan arc width after slot values. Smaller arcs create clearer gaps for basic enemies.")]
    [Range(0.55f, 1.1f)] public float fanArcMultiplier = 0.82f;

    [Tooltip("Caps fan-like pretty patterns after slot values and density multiplier.")]
    [Min(1)] public int maxFanBulletsForBasicEnemies = 4;

    [Tooltip("Caps ring-like pretty patterns after slot values and density multiplier.")]
    [Min(3)] public int maxRingBulletsForBasicEnemies = 8;

    [Serializable]
    public class PatternSlot
    {
        [Tooltip("Pattern fired for this role/intensity slot.")]
        public EnemyShooterDebug.PatternType pattern = EnemyShooterDebug.PatternType.PetalFan;

        [Header("Minimum Shape Values")]
        [Tooltip("Minimum shots per burst after personality values are applied. Use 0 to leave unchanged.")]
        [Min(0)] public int minShotsPerBurst = 1;

        [Tooltip("Minimum bursts per attack-window enable. Keep 1 for most non-boss enemies.")]
        [Min(0)] public int minBurstsPerEnable = 1;

        [Tooltip("Minimum fan bullet count for fan-like patterns. Use 0 to leave unchanged.")]
        [Min(0)] public int minFanBullets = 5;

        [Tooltip("Fan arc for fan-like patterns. Negative means leave the current value unchanged.")]
        public float fanArcDegrees = 40f;

        [Tooltip("Minimum ring bullet count for ring-like patterns. Use 0 to leave unchanged.")]
        [Min(0)] public int minRingBullets = 8;

        [Tooltip("Angular speed for spiral/sweep/rosette-style patterns. Negative means leave unchanged.")]
        public float angularSpeedDegPerTick = -1f;

        [Header("Cadence Multipliers")]
        [Tooltip("Multiplies the attack's intra-burst interval after personality values are applied.")]
        [Min(0.01f)] public float intraBurstIntervalMultiplier = 1f;

        [Tooltip("Multiplies the attack's burst cooldown after personality values are applied.")]
        [Min(0.01f)] public float burstCooldownMultiplier = 1f;

        [Tooltip("Multiplies continuous-pressure rearm delay. Lower means this slot keeps pressure faster.")]
        [Min(0.01f)] public float pressureRearmDelayMultiplier = 1f;

        [Header("Punish Window")]
        [Tooltip("If > 0, EnemyBrain uses at least this much delay before replanning/rearming. Higher = clearer punish beat.")]
        [Min(0f)] public float minimumPostBurstReplanDelay = 0f;
    }

    [Serializable]
    public class RoleProfile
    {
        public string displayName = "Role";

        [Header("Visual Identity")]
        public Color projectileTint = Color.white;

        [Header("Low / Medium / High / Close")]
        public PatternSlot low = new PatternSlot();
        public PatternSlot medium = new PatternSlot();
        public PatternSlot high = new PatternSlot();
        public PatternSlot close = new PatternSlot();

        public PatternSlot GetSlot(int intensity, bool closeRange)
        {
            if (closeRange)
                return close != null ? close : high;

            if (intensity >= 2)
                return high != null ? high : medium;

            if (intensity >= 1)
                return medium != null ? medium : low;

            return low;
        }
    }

    public struct ResolvedAttack
    {
        public string patternName;
        public int minShotsPerBurst;
        public int minBurstsPerEnable;
        public int minFanBullets;
        public float fanArcDegrees;
        public int minRingBullets;
        public float angularSpeedDegPerTick;
        public float intraBurstIntervalMultiplier;
        public float burstCooldownMultiplier;
        public float pressureRearmDelayMultiplier;
        public float minimumPostBurstReplanDelay;
        public string sourceLabel;
    }

    [Header("Role Profiles")]
    public RoleProfile suppressor = new RoleProfile
    {
        displayName = "Suppressor",
        projectileTint = new Color(1.0f, 0.52f, 0.16f, 1f),
        low = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.PetalFan,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 36f,
            minRingBullets = 8,
            burstCooldownMultiplier = 0.95f,
            minimumPostBurstReplanDelay = 0.18f
        },
        medium = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.StaggeredRosette,
            minShotsPerBurst = 2,
            minFanBullets = 4,
            fanArcDegrees = 42f,
            minRingBullets = 8,
            intraBurstIntervalMultiplier = 0.95f,
            burstCooldownMultiplier = 0.90f,
            minimumPostBurstReplanDelay = 0.18f
        },
        high = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.ClosingBlossom,
            minShotsPerBurst = 1,
            minFanBullets = 5,
            fanArcDegrees = 50f,
            minRingBullets = 8,
            intraBurstIntervalMultiplier = 0.85f,
            burstCooldownMultiplier = 0.86f,
            pressureRearmDelayMultiplier = 0.85f,
            minimumPostBurstReplanDelay = 0.24f
        },
        close = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.CrescentSweep,
            minShotsPerBurst = 1,
            minFanBullets = 4,
            fanArcDegrees = 46f,
            minRingBullets = 8,
            minimumPostBurstReplanDelay = 0.24f
        }
    };

    public RoleProfile flanker = new RoleProfile
    {
        displayName = "Flanker",
        projectileTint = new Color(1.0f, 0.22f, 0.85f, 1f),
        low = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.AimedSingle,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 24f,
            burstCooldownMultiplier = 0.85f,
            minimumPostBurstReplanDelay = 0.10f
        },
        medium = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.ButterflySpread,
            minShotsPerBurst = 1,
            minFanBullets = 4,
            fanArcDegrees = 26f,
            intraBurstIntervalMultiplier = 0.86f,
            burstCooldownMultiplier = 0.78f,
            pressureRearmDelayMultiplier = 0.85f,
            minimumPostBurstReplanDelay = 0.10f
        },
        high = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.EscapeCutoff,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 28f,
            intraBurstIntervalMultiplier = 0.82f,
            burstCooldownMultiplier = 0.72f,
            pressureRearmDelayMultiplier = 0.75f,
            minimumPostBurstReplanDelay = 0.10f
        },
        close = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.CloseCross,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 32f,
            minRingBullets = 8,
            minimumPostBurstReplanDelay = 0.18f
        }
    };

    public RoleProfile anchor = new RoleProfile
    {
        displayName = "Anchor",
        projectileTint = new Color(0.25f, 0.86f, 1.0f, 1f),
        low = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.CloseCross,
            minShotsPerBurst = 1,
            minFanBullets = 4,
            fanArcDegrees = 40f,
            minRingBullets = 8,
            minimumPostBurstReplanDelay = 0.24f
        },
        medium = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.RotatingFlowerRing,
            minShotsPerBurst = 1,
            minFanBullets = 4,
            fanArcDegrees = 38f,
            minRingBullets = 8,
            burstCooldownMultiplier = 1.10f,
            minimumPostBurstReplanDelay = 0.34f
        },
        high = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.HaloSpear,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 36f,
            minRingBullets = 14,
            burstCooldownMultiplier = 1.12f,
            minimumPostBurstReplanDelay = 0.38f
        },
        close = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.RotatingFlowerRing,
            minShotsPerBurst = 1,
            minFanBullets = 4,
            fanArcDegrees = 38f,
            minRingBullets = 8,
            minimumPostBurstReplanDelay = 0.34f
        }
    };

    public RoleProfile retreater = new RoleProfile
    {
        displayName = "Retreater",
        projectileTint = new Color(0.75f, 0.78f, 1.0f, 1f),
        low = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.AimedSingle,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 24f,
            burstCooldownMultiplier = 1.05f,
            minimumPostBurstReplanDelay = 0.20f
        },
        medium = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.PetalFan,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 28f,
            burstCooldownMultiplier = 1.08f,
            minimumPostBurstReplanDelay = 0.22f
        },
        high = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.CrescentSweep,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 32f,
            burstCooldownMultiplier = 1.10f,
            minimumPostBurstReplanDelay = 0.24f
        },
        close = new PatternSlot
        {
            pattern = EnemyShooterDebug.PatternType.CloseCross,
            minShotsPerBurst = 1,
            minFanBullets = 3,
            fanArcDegrees = 32f,
            minRingBullets = 8,
            minimumPostBurstReplanDelay = 0.26f
        }
    };

    public bool TryResolve(
        EnemySquadRole role,
        int intensity,
        bool closeRange,
        out ResolvedAttack resolved)
    {
        resolved = default;

        RoleProfile profile = GetRoleProfile(role);
        if (profile == null)
            return false;

        PatternSlot slot = profile.GetSlot(intensity, closeRange);
        if (slot == null)
            return false;

        resolved.patternName = slot.pattern.ToString();
        resolved.minShotsPerBurst = slot.minShotsPerBurst;
        resolved.minBurstsPerEnable = slot.minBurstsPerEnable;
        resolved.minFanBullets = slot.minFanBullets;
        resolved.fanArcDegrees = slot.fanArcDegrees;
        resolved.minRingBullets = slot.minRingBullets;

        ApplyGlobalDensityTuning(ref resolved);
        resolved.angularSpeedDegPerTick = slot.angularSpeedDegPerTick;
        resolved.intraBurstIntervalMultiplier = slot.intraBurstIntervalMultiplier;
        resolved.burstCooldownMultiplier = slot.burstCooldownMultiplier;
        resolved.pressureRearmDelayMultiplier = slot.pressureRearmDelayMultiplier;
        resolved.minimumPostBurstReplanDelay = slot.minimumPostBurstReplanDelay;
        resolved.sourceLabel = string.IsNullOrWhiteSpace(profile.displayName)
            ? role.ToString()
            : profile.displayName;

        return true;
    }

    private void ApplyGlobalDensityTuning(
        ref ResolvedAttack resolved)
    {
        if (resolved.minFanBullets > 0)
        {
            resolved.minFanBullets = Mathf.Clamp(
                Mathf.RoundToInt(
                    resolved.minFanBullets *
                    Mathf.Clamp(fanBulletDensityMultiplier, 0.35f, 1.25f)
                ),
                1,
                Mathf.Max(1, maxFanBulletsForBasicEnemies)
            );
        }

        if (resolved.minRingBullets > 0)
        {
            resolved.minRingBullets = Mathf.Clamp(
                Mathf.RoundToInt(
                    resolved.minRingBullets *
                    Mathf.Clamp(ringBulletDensityMultiplier, 0.35f, 1.25f)
                ),
                3,
                Mathf.Max(3, maxRingBulletsForBasicEnemies)
            );
        }

        if (resolved.fanArcDegrees >= 0f)
        {
            resolved.fanArcDegrees = Mathf.Max(
                1f,
                resolved.fanArcDegrees *
                Mathf.Clamp(fanArcMultiplier, 0.55f, 1.1f)
            );
        }
    }

    public bool TryGetProjectileTint(
        EnemySquadRole role,
        out Color color)
    {
        RoleProfile profile = GetRoleProfile(role);

        if (profile == null)
        {
            color = Color.white;
            return false;
        }

        color = profile.projectileTint;
        return true;
    }

    private RoleProfile GetRoleProfile(EnemySquadRole role)
    {
        switch (role)
        {
            case EnemySquadRole.Suppressor:
                return suppressor;
            case EnemySquadRole.FlankerLeft:
            case EnemySquadRole.FlankerRight:
                return flanker;
            case EnemySquadRole.Anchor:
                return anchor;
            case EnemySquadRole.Retreater:
                return retreater;
            default:
                return null;
        }
    }
}
