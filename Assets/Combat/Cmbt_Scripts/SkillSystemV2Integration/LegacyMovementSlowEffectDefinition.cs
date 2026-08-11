using System;
using ProjectEri.EnemyAI.V2;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Per-spell movement-speed change settings. The legacy class name is retained
/// so existing SerializeReference data continues to load without migration.
/// </summary>
[Serializable]
public sealed class MovementSlowEffectSettings : SpellEffectSettings
{
    [Tooltip("Multiply the target's movement speed by this value. Use less than 1 to slow, 1 for no change, or greater than 1 to speed up. Examples: 0.5 is half speed; 1.5 is 50% faster.")]
    [InspectorName("Movement Speed Multiplier")]
    [SerializeField, Range(0.05f, 5f)]
    private float movementMultiplier = 0.6f;

    [Tooltip("How many seconds the speed change lasts after a normal hit. A Lingering Area removes the change immediately when the target leaves.")]
    [SerializeField, Min(0.02f)]
    private float duration = 3f;

    public float MovementMultiplier =>
        Mathf.Clamp(movementMultiplier, 0.05f, 5f);
    public float Duration => Mathf.Max(0.02f, duration);

    public MovementSlowEffectSettings()
    {
    }

    public MovementSlowEffectSettings(float multiplier, float effectDuration)
    {
        movementMultiplier = Mathf.Clamp(multiplier, 0.05f, 5f);
        duration = Mathf.Max(0.02f, effectDuration);
    }
}

[CreateAssetMenu(
    fileName = "Effect_MovementSpeedChange",
    menuName = "Project Eri/Skill System V2/Effects/Movement Speed Change")]
public sealed class LegacyMovementSlowEffectDefinition :
    EffectDefinition,
    IAreaPresenceEffectDefinition
{
    [Tooltip("Default speed multiplier copied into a spell. Less than 1 slows; greater than 1 speeds up.")]
    [InspectorName("Movement Speed Multiplier")]
    [SerializeField, Range(0.05f, 5f)]
    private float movementMultiplier = 0.6f;

    [Tooltip("Default number of seconds the movement-speed change lasts after a normal hit.")]
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
            motion.ApplyMovementSpeedChange(
                this,
                resolvedMultiplier,
                resolvedDuration);
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

            enemy.ApplyMovementSpeedChange(
                enemy,
                resolvedMultiplier,
                resolvedDuration);
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
            ApplyPlayerMovementChange(
                player,
                player,
                resolvedMultiplier,
                resolvedDuration);
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
            motion.SetMovementSpeedChange(source, multiplier);
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
            enemy.ApplyMovementSpeedChange(
                source,
                multiplier,
                float.MaxValue);
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
            ApplyPlayerMovementChange(
                player,
                source,
                multiplier,
                float.MaxValue);
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
        ApplyLegacyMovementChange(
            legacy,
            source.GetInstanceID(),
            multiplier);
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
                motion.ClearMovementSpeedChange(source);
            return;
        }

        EnemyLocomotionV2 locomotion =
            SpellEffectReceiverResolver.Find<EnemyLocomotionV2>(target);
        if (locomotion != null)
        {
            EnemySlowReceiverV2 enemy =
                locomotion.GetComponent<EnemySlowReceiverV2>();
            if (enemy != null)
                enemy.ClearMovementSpeedChange(source);
        }

        PlayerMoveSpeedModifierReceiverV2 player =
            SpellEffectReceiverResolver.Find<
                PlayerMoveSpeedModifierReceiverV2>(target);
        if (player != null)
            player.ClearSource(source);

        SpeedModifier legacy =
            SpellEffectReceiverResolver.Find<SpeedModifier>(target);
        if (legacy != null)
            legacy.RemoveSource(source.GetInstanceID());
    }

    private void OnValidate()
    {
        movementMultiplier = Mathf.Clamp(movementMultiplier, 0.05f, 5f);
        duration = Mathf.Max(0.02f, duration);
    }

    private static void ApplyPlayerMovementChange(
        PlayerMoveSpeedModifierReceiverV2 receiver,
        Component source,
        float multiplier,
        float duration)
    {
        if (multiplier < 1f)
            receiver.ApplySlow(source, multiplier, duration);
        else if (multiplier > 1f)
            receiver.ApplyBoost(source, multiplier, duration);
        else
            receiver.ApplyGenericMultiplier(source, 1f, duration);
    }

    internal static void ApplyLegacyMovementChange(
        SpeedModifier receiver,
        int sourceId,
        float multiplier)
    {
        receiver.ApplyMovementSpeedChange(sourceId, multiplier);
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
        LegacyMovementSlowEffectDefinition.ApplyLegacyMovementChange(
            receiver,
            sourceId,
            multiplier);
    }

    private void Update()
    {
        if (Time.time < removeAt)
            return;

        if (receiver != null)
            receiver.RemoveSource(sourceId);
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (receiver != null)
            receiver.RemoveSource(sourceId);
    }
}
