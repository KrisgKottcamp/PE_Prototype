using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Bridges a generic V2 effect into Project Eri's current enemy projectile
/// reflection path. Projectile owns the authoritative team, collision,
/// visuals, lifetime, and reflected-damage behavior.
/// </summary>
[Serializable]
public sealed class ReflectProjectileEffectSettings : SpellEffectSettings
{
    [Tooltip("Base damage assigned to an enemy projectile after it becomes player-owned.")]
    [SerializeField, Min(1)]
    private int reflectedDamageBase = 10;

    [Tooltip("Unity layer assigned to the reflected projectile so it collides like a player projectile.")]
    [SerializeField]
    private string reflectedLayerName = "PlayerProjectile";

    [Tooltip("Seconds reflection tracking data is kept for one root cast.")]
    [SerializeField, Min(0.1f)]
    private float trackerRetentionSeconds = 1f;

    public int ReflectedDamageBase => Mathf.Max(1, reflectedDamageBase);
    public string ReflectedLayerName => reflectedLayerName;
    public float TrackerRetentionSeconds =>
        Mathf.Max(0.1f, trackerRetentionSeconds);

    public ReflectProjectileEffectSettings()
    {
    }

    public ReflectProjectileEffectSettings(
        int damageBase,
        string layerName,
        float retentionSeconds)
    {
        reflectedDamageBase = Mathf.Max(1, damageBase);
        reflectedLayerName = layerName;
        trackerRetentionSeconds = Mathf.Max(0.1f, retentionSeconds);
    }
}

[CreateAssetMenu(
    fileName = "Effect_ReflectProjectile",
    menuName = "Project Eri/Skill System V2/Integration/Reflect Projectile")]
public sealed class LegacyProjectileReflectEffectDefinition :
    EffectDefinition,
    IMeleeArcCastEffectDefinition
{
    private sealed class TrackerEntry
    {
        public ReflectDamageTracker Tracker;
        public float LastUsedAt;
    }

    [Tooltip("Default reflected projectile damage copied into a spell.")]
    [SerializeField, Min(1)] private int reflectedDamageBase = 10;
    [Tooltip("Default Unity layer name assigned after reflection.")]
    [SerializeField] private string reflectedLayerName = "PlayerProjectile";
    [Tooltip("Default lifetime of per-cast reflection tracking data.")]
    [SerializeField, Min(0.1f)] private float trackerRetentionSeconds = 1f;

    [Header("Runtime Debug")]
    [SerializeField] private int debugLastArcProjectileCount;
    [SerializeField] private int debugTotalReflectedCount;
    [SerializeField] private long debugLastRootCastId;

    private readonly Dictionary<long, TrackerEntry> trackers =
        new Dictionary<long, TrackerEntry>();
    private readonly List<long> removeBuffer = new List<long>();

    public override Type SettingsType =>
        typeof(ReflectProjectileEffectSettings);

    public override SpellEffectSettings CreateDefaultSettings()
    {
        return new ReflectProjectileEffectSettings(
            reflectedDamageBase,
            reflectedLayerName,
            trackerRetentionSeconds);
    }

    public override bool Apply(in SpellEffectContext context)
    {
        return Apply(context, CreateDefaultSettings());
    }

    public override bool Apply(
        in SpellEffectContext context,
        SpellEffectSettings settings)
    {
        if (context.Target == null || context.Cast.Caster == null)
            return false;

        Projectile projectile =
            SpellEffectReceiverResolver.Find<Projectile>(context.Target);
        if (projectile == null ||
            projectile.Team != Projectile.ProjectileTeam.Enemy)
        {
            return false;
        }

        ReflectProjectileEffectSettings resolved =
            settings as ReflectProjectileEffectSettings ??
            (ReflectProjectileEffectSettings)CreateDefaultSettings();
        ReflectProjectile(
            projectile,
            context.Cast.Caster.transform,
            GetTracker(context.Cast.RootCastId, resolved),
            resolved);
        debugTotalReflectedCount++;
        return true;
    }

    public int ApplyToMeleeArc(
        in SpellExecutionContext context,
        float range,
        float arcAngle,
        SpellEffectSettings settings)
    {
        if (context.Cast.Caster == null)
            return 0;

        ReflectProjectileEffectSettings resolved =
            settings as ReflectProjectileEffectSettings ??
            (ReflectProjectileEffectSettings)CreateDefaultSettings();
        Vector2 origin = context.Cast.Origin;
        Vector2 aim = context.Cast.AimDirection.sqrMagnitude > 0.000001f
            ? context.Cast.AimDirection.normalized
            : Vector2.up;
        float safeRange = Mathf.Max(0.01f, range);
        float minimumDot = arcAngle >= 359.9f
            ? -1f
            : Mathf.Cos(
                Mathf.Clamp(arcAngle, 0.1f, 360f) *
                0.5f *
                Mathf.Deg2Rad);

        Projectile[] projectiles =
            UnityEngine.Object.FindObjectsOfType<Projectile>(false);
        ReflectDamageTracker tracker = GetTracker(
            context.Cast.RootCastId,
            resolved);
        int reflectedCount = 0;

        debugLastRootCastId = context.Cast.RootCastId;

        for (int i = 0; i < projectiles.Length; i++)
        {
            Projectile projectile = projectiles[i];
            if (projectile == null ||
                projectile.Team != Projectile.ProjectileTeam.Enemy)
            {
                continue;
            }

            Vector2 offset =
                (Vector2)projectile.transform.position - origin;
            if (offset.sqrMagnitude > safeRange * safeRange)
                continue;

            if (offset.sqrMagnitude > 0.000001f &&
                Vector2.Dot(aim, offset.normalized) < minimumDot)
            {
                continue;
            }

            ReflectProjectile(
                projectile,
                context.Cast.Caster.transform,
                tracker,
                resolved);
            reflectedCount++;
        }

        debugLastArcProjectileCount = reflectedCount;
        debugTotalReflectedCount += reflectedCount;
        return reflectedCount;
    }

    private void ReflectProjectile(
        Projectile projectile,
        Transform newOwner,
        ReflectDamageTracker tracker,
        ReflectProjectileEffectSettings settings)
    {
        int reflectedLayer = string.IsNullOrWhiteSpace(
            settings.ReflectedLayerName)
            ? -1
            : LayerMask.NameToLayer(settings.ReflectedLayerName);

        projectile.Reflect(newOwner, reflectedLayer, tracker);
    }

    private ReflectDamageTracker GetTracker(
        long rootCastId,
        ReflectProjectileEffectSettings settings)
    {
        PruneTrackers(settings.TrackerRetentionSeconds);

        // Normal SpellRunner casts always have a positive root ID. Preserve a
        // useful fallback for direct effect invocations in tools or tests.
        if (rootCastId <= 0L)
            return new ReflectDamageTracker(settings.ReflectedDamageBase);

        if (!trackers.TryGetValue(rootCastId, out TrackerEntry entry))
        {
            entry = new TrackerEntry
            {
                Tracker = new ReflectDamageTracker(
                    settings.ReflectedDamageBase)
            };
            trackers.Add(rootCastId, entry);
        }

        entry.LastUsedAt = Time.unscaledTime;
        return entry.Tracker;
    }

    private void PruneTrackers(float retentionSeconds)
    {
        float cutoff = Time.unscaledTime -
                       Mathf.Max(0.1f, retentionSeconds);
        removeBuffer.Clear();

        foreach (KeyValuePair<long, TrackerEntry> pair in trackers)
        {
            if (pair.Value.LastUsedAt < cutoff)
                removeBuffer.Add(pair.Key);
        }

        for (int i = 0; i < removeBuffer.Count; i++)
            trackers.Remove(removeBuffer[i]);
    }

    private void OnValidate()
    {
        reflectedDamageBase = Mathf.Max(1, reflectedDamageBase);
        trackerRetentionSeconds = Mathf.Max(0.1f, trackerRetentionSeconds);
    }
}
