using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellSpawnPosition
    {
        HitPoint,
        Target,
        CastOrigin,
        TargetPoint
    }

    public enum SpellSpawnRotation
    {
        Identity,
        AimDirection,
        HitNormal
    }

    [Serializable]
    public sealed class SpawnEffectSettings : SpellEffectSettings
    {
        [Tooltip("The prefab created when this effect runs.")]
        [SerializeField]
        private GameObject prefab;

        [Tooltip("Which known spell location is used as the spawn position.")]
        [SerializeField]
        private SpellSpawnPosition spawnPosition = SpellSpawnPosition.HitPoint;

        [Tooltip("World-space offset added to the chosen spawn position.")]
        [SerializeField]
        private Vector2 worldOffset;

        [Tooltip("How the spawned object's rotation is chosen.")]
        [SerializeField]
        private SpellSpawnRotation rotation = SpellSpawnRotation.Identity;

        [Tooltip("Extra rotation in degrees added after the base rotation is chosen.")]
        [SerializeField]
        private float rotationOffsetDegrees;

        [Tooltip("Make the spawned object follow the effect target. Requires an object recipient.")]
        [SerializeField]
        private bool parentToTarget;

        [Tooltip("Seconds before the spawned object is automatically destroyed. Zero means it is not automatically destroyed.")]
        [SerializeField, Min(0f)]
        private float lifetime;

        [Tooltip("Scaled lifetime follows slow motion and pauses. Unscaled lifetime uses real-world time.")]
        [SerializeField]
        private SpellTimeMode lifetimeTimeMode = SpellTimeMode.Scaled;

        public GameObject Prefab => prefab;
        public SpellSpawnPosition SpawnPosition => spawnPosition;
        public Vector2 WorldOffset => worldOffset;
        public SpellSpawnRotation Rotation => rotation;
        public float RotationOffsetDegrees => rotationOffsetDegrees;
        public bool ParentToTarget => parentToTarget;
        public float Lifetime => Mathf.Max(0f, lifetime);
        public SpellTimeMode LifetimeTimeMode => lifetimeTimeMode;

        public SpawnEffectSettings()
        {
        }

        public SpawnEffectSettings(
            GameObject objectPrefab,
            SpellSpawnPosition position,
            Vector2 offset,
            SpellSpawnRotation spawnRotation,
            float rotationOffset,
            bool shouldParentToTarget,
            float objectLifetime,
            SpellTimeMode timeMode)
        {
            prefab = objectPrefab;
            spawnPosition = position;
            worldOffset = offset;
            rotation = spawnRotation;
            rotationOffsetDegrees = rotationOffset;
            parentToTarget = shouldParentToTarget;
            lifetime = Mathf.Max(0f, objectLifetime);
            lifetimeTimeMode = timeMode;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_Spawn",
        menuName = "Project Eri/Skill System V2/Effects/Spawn Object")]
    public sealed class SpawnEffectDefinition : EffectDefinition
    {
        [Tooltip("Default prefab copied into a spell when this effect is equipped.")]
        [SerializeField]
        private GameObject prefab;

        [Tooltip("Default spell location used for spawning.")]
        [SerializeField]
        private SpellSpawnPosition spawnPosition = SpellSpawnPosition.HitPoint;

        [Tooltip("Default world-space spawn offset.")]
        [SerializeField]
        private Vector2 worldOffset;

        [Tooltip("Default method used to rotate the spawned object.")]
        [SerializeField]
        private SpellSpawnRotation rotation = SpellSpawnRotation.Identity;

        [Tooltip("Default additional rotation in degrees.")]
        [SerializeField]
        private float rotationOffsetDegrees;

        [Tooltip("Default choice for whether the spawned object follows the target.")]
        [SerializeField]
        private bool parentToTarget;

        [Tooltip("Default seconds before automatic destruction. Zero disables automatic destruction.")]
        [SerializeField, Min(0f)]
        private float lifetime;

        [Tooltip("Default time source used for the spawned object's lifetime.")]
        [SerializeField]
        private SpellTimeMode lifetimeTimeMode = SpellTimeMode.Scaled;

        public override Type SettingsType => typeof(SpawnEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new SpawnEffectSettings(
                prefab,
                spawnPosition,
                worldOffset,
                rotation,
                rotationOffsetDegrees,
                parentToTarget,
                lifetime,
                lifetimeTimeMode);
        }

        public override bool CanApplyWithoutRecipient(
            SpellEffectSettings settings)
        {
            return true;
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            SpawnEffectSettings resolved =
                settings as SpawnEffectSettings ??
                (SpawnEffectSettings)CreateDefaultSettings();
            if (resolved.Prefab == null)
                return false;

            Vector2 position = ResolvePosition(
                context,
                resolved.SpawnPosition) + resolved.WorldOffset;
            Quaternion resolvedRotation = ResolveRotation(
                context,
                resolved.Rotation,
                resolved.RotationOffsetDegrees);
            GameObject instance = UnityEngine.Object.Instantiate(
                resolved.Prefab,
                new Vector3(
                    position.x,
                    position.y,
                    resolved.Prefab.transform.position.z),
                resolvedRotation);

            if (resolved.ParentToTarget && context.Target != null)
                instance.transform.SetParent(context.Target.transform, true);

            var spawnContext = new SpellSpawnContext(context, instance);
            MonoBehaviour[] receivers =
                instance.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] is ISpellSpawnReceiver receiver)
                    receiver.InitializeSpawn(spawnContext);
            }

            if (resolved.Lifetime > 0f)
            {
                TimedSpellObject timer = instance.GetComponent<TimedSpellObject>();
                if (timer == null)
                    timer = instance.AddComponent<TimedSpellObject>();

                timer.Initialize(
                    resolved.Lifetime,
                    resolved.LifetimeTimeMode);
            }

            return true;
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, CreateDefaultSettings());
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            SpawnEffectSettings resolved =
                settings as SpawnEffectSettings ??
                (SpawnEffectSettings)CreateDefaultSettings();
            if (resolved.Prefab == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Spawn effect '{DisplayName}' needs a prefab."));
            }
        }

        private Vector2 ResolvePosition(
            in SpellEffectContext context,
            SpellSpawnPosition resolvedSpawnPosition)
        {
            switch (resolvedSpawnPosition)
            {
                case SpellSpawnPosition.Target:
                    return context.Target != null
                        ? (Vector2)context.Target.transform.position
                        : context.HitPoint;
                case SpellSpawnPosition.CastOrigin:
                    return context.Cast.Origin;
                case SpellSpawnPosition.TargetPoint:
                    return context.Cast.HasTargetPoint
                        ? context.Cast.TargetPoint
                        : context.HitPoint;
                default:
                    return context.HitPoint;
            }
        }

        private Quaternion ResolveRotation(
            in SpellEffectContext context,
            SpellSpawnRotation resolvedRotation,
            float resolvedRotationOffset)
        {
            Vector2 direction;
            switch (resolvedRotation)
            {
                case SpellSpawnRotation.AimDirection:
                    direction = context.Cast.AimDirection;
                    break;
                case SpellSpawnRotation.HitNormal:
                    direction = context.HitNormal;
                    break;
                default:
                    return Quaternion.Euler(0f, 0f, resolvedRotationOffset);
            }

            float angle = direction.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                : 0f;
            return Quaternion.Euler(
                0f,
                0f,
                angle + resolvedRotationOffset);
        }

        private void OnValidate()
        {
            lifetime = Mathf.Max(0f, lifetime);
        }
    }
}
