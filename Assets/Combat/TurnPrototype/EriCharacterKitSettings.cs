using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Project Eri/Prototype/Character Kit Settings")]
public sealed class EriCharacterKitSettings : ScriptableObject
{
    [Serializable]
    public sealed class SkillTuning
    {
        public EriCommandKind Kind;
        public int Damage, Segments, MPCost;
        [Min(1)] public int CooldownTurns = 1;
        [Min(0f), Tooltip("Seconds the caster is rooted and vulnerable before this skill resolves.")]
        public float WindUpSeconds;
        public float Range, Radius, Duration;
        public SkillTuning(EriCommandKind kind, int damage, int segments, int mp, float range, float radius,
            float duration = 0, float windUpSeconds = 0)
        { Kind = kind; Damage = damage; Segments = segments; MPCost = mp; Range = range; Radius = radius; Duration = duration; WindUpSeconds = windUpSeconds; }
    }

    [Tooltip("Editable prototype values. Damage is before buffs, elemental weakness and defense routing.")]
    public SkillTuning[] Skills = Defaults();
    [Min(0.1f)] public float ProjectileSpeed = 18f;
    [Min(1)] public int FanProjectileCount = 5;
    [Range(5, 120)] public float FanAngle = 55f;
    [Min(0.1f)] public float GrenadeFlightSeconds = 0.55f;
    [Min(0.1f)] public float DashSeconds = 0.18f;
    [Min(0.1f)] public float MarkSeconds = 28f;
    [Min(0.1f)] public float OffBalanceSeconds = 8f;
    [Min(0.1f)] public float OilBurnSeconds = 3f;

    public SkillTuning Get(EriCommandKind kind)
    {
        if (Skills != null) foreach (var value in Skills) if (value != null && value.Kind == kind) return value;
        foreach (var value in Defaults()) if (value.Kind == kind) return value;
        return new SkillTuning(kind, 30, 1, 5, 10, 2);
    }
    public static EriCharacterKitSettings Load()
    {
        var settings = Resources.Load<EriCharacterKitSettings>("EriCharacterKitSettings");
        return settings != null ? settings : CreateInstance<EriCharacterKitSettings>();
    }
    private static SkillTuning[] Defaults() => new[]
    {
        new SkillTuning(EriCommandKind.Slash, 32, 1, 2, 2.5f, 1.1f),
        new SkillTuning(EriCommandKind.DashSlash, 36, 2, 6, 4f, 0.8f),
        new SkillTuning(EriCommandKind.Shot, 30, 1, 5, 12f, 0.24f),
        new SkillTuning(EriCommandKind.Motivate, 0, 1, 6, 0, 0, windUpSeconds: 0.7f),
        new SkillTuning(EriCommandKind.Snipe, 38, 3, 10, 16f, 0.24f),
        new SkillTuning(EriCommandKind.Fan, 30, 1, 6, 11f, 0.22f),
        new SkillTuning(EriCommandKind.Grenade, 38, 2, 7, 9f, 2.2f),
        new SkillTuning(EriCommandKind.HealSelf, 35, 2, 8, 0, 0),
        new SkillTuning(EriCommandKind.Reflect, 35, 2, 7, 0, 4.5f, 3.5f),
        new SkillTuning(EriCommandKind.Cover, 0, 2, 6, 7f, 1.1f, 6f),
        new SkillTuning(EriCommandKind.Inspire, 0, 2, 10, 0, 0, windUpSeconds: 1f),
        new SkillTuning(EriCommandKind.Silence, 0, 1, 5, 0, 5f),
        new SkillTuning(EriCommandKind.WhipSlash, 30, 1, 3, 4.5f, 0.6f),
        new SkillTuning(EriCommandKind.MarkShot, 0, 1, 4, 13f, 0.3f, windUpSeconds: 0.65f),
        new SkillTuning(EriCommandKind.Dispel, 0, 1, 4, 9f, 2f, windUpSeconds: 0.75f),
        new SkillTuning(EriCommandKind.OilSpill, 24, 1, 4, 8f, 2f, 12f)
    };
}
