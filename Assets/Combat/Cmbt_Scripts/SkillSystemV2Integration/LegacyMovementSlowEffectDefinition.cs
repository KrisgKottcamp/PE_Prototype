using System;
using ProjectEri.EnemyAI.V2;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Migration effect that lets a V2 spell drive Project Eri's current movement
/// stacks. It can be replaced later by a fully generic stat-modifier effect
/// without changing the spell's targeting or delivery assets.
/// </summary>
[Serializable]
public sealed class MovementSlowEffectSettings : SpellEffectSettings
{
    [Tooltip("Movement speed multiplier while slowed. For example, 0.6 means 60% of normal speed.")]
    [SerializeField, Range(0.05f, 1f)]
    private float movementMultiplier = 0.6f;

    [Tooltip("Seconds the slow remains after application. Lingering Area presence removes it immediately when the target leaves.")]
    [SerializeField, Min(0.02f)]
    private float duration = 3f;

    public float MovementMultiplier =>
        Mathf.Clamp(movementMultiplier, 0.05f, 1f);
    public float Duration => Mathf.Max(0.02f, duration);

    public MovementSlowEffectSettings()
    {
    }

    public MovementSlowEffectSettings(float multiplier, float slowDuration)
    {
        movementMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
        duration = Mathf.Max(0.02f, slowDuration);
    }
}

[CreateAssetMenu(
    fileName = "Effect_LegacyMovementSlow",
    menuName = "Project Eri/Skill System V2/Integration/Movement Slow")]
public sealed class LegacyMovementSlowEffectDefinition :
    EffectDefinition,
    IAreaPresenceEffectDefinition
{
    [Tooltip("Default movement multiplier copied into a spell when this effect is equipped.")]
    [SerializeField, Range(0.05f, 1f)]
    private float movementMultiplier = 0.6f;

    [Tooltip("Default slow duration copied into a spell's inline settings.")]
    [SerializeField, Min(0.02f)]
    private float duration = 3f;

    public override Type SettingsType =>
        typeof(MovementSlowEffectSettings);

    public override SpellEffectSettings CreateDefaultSettings()
    {
        return new MovementSlowEffectSettings(
            movementMultiplier,
            duration);
    }

    public override bool Apply(in SpellEffectContext context)
    {
        return Apply(context, CreateDefaultSettings());
    }

    public override bool Apply(
        in SpellEffectContext context,
        SpellEffectSettings settings)
    {
        if (context.Target == null)
            return false;

        MovementSlowEffectSettings resolved =
            settings as MovementSlowEffectSettings ??
            (MovementSlowEffectSettings)CreateDefaultSettings();
        float resolvedDuration = resolved.Duration;
        float resolvedMultiplier = resolved.MovementMultiplier;

        SpellProjectile2D v2Projectile =
            SpellEffectReceiverResolver.Find<SpellProjectile2D>(
                context.Target);
        if (v2Projectile != null)
        {
            SpellMotionRateModifier motion =
                v2Projectile.GetComponent<SpellMotionRateModifier>();
            if (motion == null)
                motion = v2Projectile.gameObject.AddComponent<SpellMotionRateModifier>();
            motion.ApplySlow(this, resolvedMultiplier, resolvedDuration);
            return true;
        }

        // Project Eri enemy prefabs can carry both AI backends at once while
        // SquadDirectorV2 switches between Active and ObserveOnly. Apply to
        // both modifier channels: each backend reads only its own channel, so
        // this remains safe and keeps working across a live backend switch.
        EnemyLocomotionV2 locomotion =
            SpellEffectReceiverResolver.Find<EnemyLocomotionV2>(
                context.Target);
        if (locomotion != null)
        {
            EnemySlowReceiverV2 enemy =
                locomotion.GetComponent<EnemySlowReceiverV2>();
            if (enemy == null)
                enemy = locomotion.gameObject.AddComponent<EnemySlowReceiverV2>();

            enemy.ApplySlow(enemy, resolvedMultiplier, resolvedDuration);
        }

        PlayerMoveSpeedModifierReceiverV2 player =
            SpellEffectReceiverResolver.Find<
                PlayerMoveSpeedModifierReceiverV2>(context.Target);
        if (player == null)
        {
            CombatPawnMover mover =
                SpellEffectReceiverResolver.Find<CombatPawnMover>(
                    context.Target);
            if (mover != null)
                player = mover.gameObject.AddComponent<PlayerMoveSpeedModifierReceiverV2>();
        }

        if (player != null)
        {
            player.ApplySlow(player, resolvedMultiplier, resolvedDuration);
            return true;
        }

        SpeedModifier legacy =
            SpellEffectReceiverResolver.Find<SpeedModifier>(context.Target);
        if (legacy == null)
        {
            EnemyHealth health =
                SpellEffectReceiverResolver.Find<EnemyHealth>(
                    context.Target);
            GameObject owner = health != null
                ? health.gameObject
                : context.Target;
            legacy = owner.AddComponent<SpeedModifier>();
        }

        LegacyTimedSlowHandle handle =
            legacy.gameObject.AddComponent<LegacyTimedSlowHandle>();
        handle.Begin(legacy, resolvedMultiplier, resolvedDuration);
        return true;
    }

    public bool ApplyPresence(
        in SpellEffectContext context,
        Component source,
        SpellEffectSettings settings)
    {
        if (context.Target == null || source == null)
            return false;

        MovementSlowEffectSettings resolved =
            settings as MovementSlowEffectSettings ??
            (MovementSlowEffectSettings)CreateDefaultSettings();
        float multiplier = resolved.MovementMultiplier;

        SpellProjectile2D v2Projectile =
            SpellEffectReceiverResolver.Find<SpellProjectile2D>(
                context.Target);
        if (v2Projectile != null)
        {
            SpellMotionRateModifier motion =
                v2Projectile.GetComponent<SpellMotionRateModifier>();
            if (motion == null)
                motion = v2Projectile.gameObject.AddComponent<SpellMotionRateModifier>();
            motion.SetSlow(source, multiplier);
            return true;
        }

        EnemyLocomotionV2 locomotion =
            SpellEffectReceiverResolver.Find<EnemyLocomotionV2>(
                context.Target);
        if (locomotion != null)
        {
            EnemySlowReceiverV2 enemy =
                locomotion.GetComponent<EnemySlowReceiverV2>();
            if (enemy == null)
                enemy = locomotion.gameObject.AddComponent<EnemySlowReceiverV2>();
            enemy.ApplySlow(source, multiplier, float.MaxValue);
        }

        PlayerMoveSpeedModifierReceiverV2 player =
            SpellEffectReceiverResolver.Find<
                PlayerMoveSpeedModifierReceiverV2>(context.Target);
        if (player == null)
        {
            CombatPawnMover mover =
                SpellEffectReceiverResolver.Find<CombatPawnMover>(
                    context.Target);
            if (mover != null)
                player = mover.gameObject.AddComponent<PlayerMoveSpeedModifierReceiverV2>();
        }

        if (player != null)
        {
            player.ApplySlow(source, multiplier, float.MaxValue);
            return true;
        }

        SpeedModifier legacy =
            SpellEffectReceiverResolver.Find<SpeedModifier>(context.Target);
        if (legacy == null)
        {
            EnemyHealth health =
                SpellEffectReceiverResolver.Find<EnemyHealth>(
                    context.Target);
            GameObject owner = health != null
                ? health.gameObject
                : context.Target;
            legacy = owner.AddComponent<SpeedModifier>();
        }
        legacy.ApplySlow(source.GetInstanceID(), multiplier);
        return true;
    }

    public void RemovePresence(
        GameObject target,
        Component source,
        SpellEffectSettings settings)
    {
        if (target == null || source == null)
            return;

        SpellProjectile2D v2Projectile =
            SpellEffectReceiverResolver.Find<SpellProjectile2D>(target);
        if (v2Projectile != null)
        {
            SpellMotionRateModifier motion =
                v2Projectile.GetComponent<SpellMotionRateModifier>();
            if (motion != null)
                motion.ClearSlow(source);
            return;
        }

        EnemyLocomotionV2 locomotion =
            SpellEffectReceiverResolver.Find<EnemyLocomotionV2>(target);
        if (locomotion != null)
        {
            EnemySlowReceiverV2 enemy =
                locomotion.GetComponent<EnemySlowReceiverV2>();
            if (enemy != null)
                enemy.ClearSlow(source);
        }

        PlayerMoveSpeedModifierReceiverV2 player =
            SpellEffectReceiverResolver.Find<
                PlayerMoveSpeedModifierReceiverV2>(target);
        if (player != null)
            player.ClearSource(source);

        SpeedModifier legacy =
            SpellEffectReceiverResolver.Find<SpeedModifier>(target);
        if (legacy != null)
            legacy.RemoveSlow(source.GetInstanceID());
    }

    private void OnValidate()
    {
        movementMultiplier = Mathf.Clamp(movementMultiplier, 0.05f, 1f);
        duration = Mathf.Max(0.02f, duration);
    }
}

public sealed class LegacyTimedSlowHandle : MonoBehaviour
{
    private SpeedModifier receiver;
    private int sourceId;
    private float removeAt;

    public void Begin(
        SpeedModifier target,
        float multiplier,
        float duration)
    {
        receiver = target;
        sourceId = GetInstanceID();
        removeAt = Time.time + Mathf.Max(0.02f, duration);
        receiver.ApplySlow(sourceId, multiplier);
    }

    private void Update()
    {
        if (Time.time < removeAt)
            return;

        if (receiver != null)
            receiver.RemoveSlow(sourceId);
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (receiver != null)
            receiver.RemoveSlow(sourceId);
    }
}
