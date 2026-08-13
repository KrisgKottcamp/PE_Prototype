using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class ProjectileDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("Optional prefab used for the projectile's appearance. Leave empty to use the simple prototype visual.")]
        [SerializeField] private GameObject projectilePrefab;
        [Tooltip("Advanced compatibility option. Leave disabled so legacy movement, collision, and damage scripts are removed from the visual prefab copy.")]
        [SerializeField] private bool allowPrefabGameplayComponents;
        [Tooltip("How many world units the projectile travels each second.")]
        [SerializeField, Min(0.01f)] private float speed = 8f;
        [Tooltip("Maximum world distance traveled before the projectile expires.")]
        [SerializeField, Min(0.01f)] private float range = 10f;
        [Tooltip("Radius used when checking the projectile's path for collisions. Zero behaves like a thin ray.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.08f;
        [Tooltip("Unity layers the projectile checks for targets, walls, and other blocking objects.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Allow the projectile to continue after hitting a valid target.")]
        [SerializeField] private bool pierceTargets;
        [Tooltip("How many valid targets a piercing projectile may hit before stopping.")]
        [SerializeField, Min(1)] private int maximumTargetHits = 1;
        [Tooltip("Stop on colliders that are in the Collision Mask but fail the spell's Target Rules. Usually enabled so walls block shots.")]
        [SerializeField] private bool stopOnBlockedCollider = true;
        [Tooltip("Maximum collision results checked during one movement step. Increase only if fast projectiles cross many colliders at once.")]
        [SerializeField, Min(1)] private int castBufferSize = 16;
        [Tooltip("How many shots are produced, their spread, and whether they fire simultaneously or as a rapid sequence.")]
        [SerializeField] private ProjectileEmissionSettings emission =
            new ProjectileEmissionSettings();
        [Tooltip("How moving projectile shots travel after being emitted.")]
        [SerializeField] private ProjectileMotionSettings motion =
            new ProjectileMotionSettings();
        [Tooltip("Whether this delivery produces moving projectiles, instant beams, or cone-shaped hits.")]
        [SerializeField] private ProjectileShapeSettings shotShape =
            new ProjectileShapeSettings();
        [Tooltip("Optional distance-based potency reduction shared by damage, healing, and other scalable effects.")]
        [SerializeField] private ProjectileFalloffSettings falloff =
            new ProjectileFalloffSettings();

        public GameObject ProjectilePrefab => projectilePrefab;
        public bool AllowPrefabGameplayComponents => allowPrefabGameplayComponents;
        public float Speed => Mathf.Max(0.01f, speed);
        public float Range => Mathf.Max(0.01f, range);
        public float CollisionRadius => Mathf.Max(0f, collisionRadius);
        public LayerMask CollisionMask => collisionMask;
        public bool PierceTargets => pierceTargets;
        public int MaximumTargetHits => Mathf.Max(1, maximumTargetHits);
        public bool StopOnBlockedCollider => stopOnBlockedCollider;
        public int CastBufferSize => Mathf.Max(1, castBufferSize);
        public ProjectileEmissionSettings Emission =>
            emission ??= new ProjectileEmissionSettings();
        public ProjectileMotionSettings Motion =>
            motion ??= new ProjectileMotionSettings();
        public ProjectileShapeSettings ShotShape =>
            shotShape ??= new ProjectileShapeSettings();
        public ProjectileFalloffSettings Falloff =>
            falloff ??= new ProjectileFalloffSettings();

        public ProjectileDeliverySettings() { }
        public ProjectileDeliverySettings(PlayerTargetingDefinition targeting,
            GameObject prefab, bool allowGameplayComponents,
            float projectileSpeed, float projectileRange, float radius,
            LayerMask mask, bool pierce, int maximumHits,
            bool stopOnBlocked, int bufferSize) : base(targeting)
        {
            projectilePrefab = prefab;
            allowPrefabGameplayComponents = allowGameplayComponents;
            speed = projectileSpeed;
            range = projectileRange;
            collisionRadius = radius;
            collisionMask = mask;
            pierceTargets = pierce;
            maximumTargetHits = maximumHits;
            stopOnBlockedCollider = stopOnBlocked;
            castBufferSize = bufferSize;
        }

        public ProjectileDeliverySettings(
            PlayerTargetingDefinition targeting,
            GameObject prefab,
            bool allowGameplayComponents,
            float projectileSpeed,
            float projectileRange,
            float radius,
            LayerMask mask,
            bool pierce,
            int maximumHits,
            bool stopOnBlocked,
            int bufferSize,
            ProjectileEmissionSettings emissionSettings,
            ProjectileMotionSettings motionSettings,
            ProjectileShapeSettings shapeSettings,
            ProjectileFalloffSettings falloffSettings)
            : this(
                targeting,
                prefab,
                allowGameplayComponents,
                projectileSpeed,
                projectileRange,
                radius,
                mask,
                pierce,
                maximumHits,
                stopOnBlocked,
                bufferSize)
        {
            emission = emissionSettings?.Clone() ??
                       new ProjectileEmissionSettings();
            motion = motionSettings?.Clone() ??
                     new ProjectileMotionSettings();
            shotShape = shapeSettings?.Clone() ??
                        new ProjectileShapeSettings();
            falloff = falloffSettings?.Clone() ??
                      new ProjectileFalloffSettings();
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_Projectile",
        menuName = "Project Eri/Skill System V2/Delivery/Projectile")]
    public sealed class ProjectileDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default projectile visual prefab copied into a spell's inline settings.")]
        [SerializeField]
        private GameObject projectilePrefab;

        [Tooltip("Advanced compatibility escape hatch. Leave disabled so V2 strips legacy gameplay scripts, Rigidbody simulation, and colliders from an assigned projectile prefab while preserving its renderers and animation.")]
        [SerializeField]
        private bool allowPrefabGameplayComponents;

        [Tooltip("Default projectile travel speed in world units per second.")]
        [SerializeField, Min(0.01f)]
        private float speed = 8f;

        [Tooltip("Default maximum projectile travel distance.")]
        [SerializeField, Min(0.01f)]
        private float range = 10f;

        [Tooltip("Default collision-check radius. Zero creates a thin ray-like projectile path.")]
        [SerializeField, Min(0f)]
        private float collisionRadius = 0.08f;

        [Tooltip("Default Unity layers checked for targets and blockers.")]
        [SerializeField]
        private LayerMask collisionMask = ~0;

        [Tooltip("Default choice for whether projectiles continue through valid targets.")]
        [SerializeField]
        private bool pierceTargets;

        [Tooltip("Default maximum number of valid targets a piercing projectile may hit.")]
        [SerializeField, Min(1)]
        private int maximumTargetHits = 1;

        [Tooltip("When enabled, colliders rejected by the spell's Target Filter stop the projectile. Use this for walls and cover.")]
        [SerializeField]
        private bool stopOnBlockedCollider = true;

        [Tooltip("Default maximum collision results checked during one movement step.")]
        [SerializeField, Min(1)]
        private int castBufferSize = 16;

        [Header("Emission")]
        [Tooltip("Default projectile count, spread pattern, and rapid-fire interval copied into each spell.")]
        [SerializeField] private ProjectileEmissionSettings emission =
            new ProjectileEmissionSettings();

        [Header("Travel")]
        [Tooltip("Default straight, homing, or boomerang movement copied into each spell.")]
        [SerializeField] private ProjectileMotionSettings motion =
            new ProjectileMotionSettings();

        [Header("Hit Shape")]
        [Tooltip("Default moving projectile, instant beam, or cone hit shape copied into each spell.")]
        [SerializeField] private ProjectileShapeSettings shotShape =
            new ProjectileShapeSettings();

        [Header("Effect Falloff")]
        [Tooltip("Default distance-based potency behavior copied into each spell.")]
        [SerializeField] private ProjectileFalloffSettings falloff =
            new ProjectileFalloffSettings();

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.Direction;

        public override Type SettingsType =>
            typeof(ProjectileDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new ProjectileDeliverySettings(
                PlayerTargeting, projectilePrefab, allowPrefabGameplayComponents,
                speed, range, collisionRadius, collisionMask, pierceTargets,
                maximumTargetHits, stopOnBlockedCollider, castBufferSize,
                emission, motion, shotShape, falloff);
        }

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return CreateExecution(context, CreateDefaultSettings());
        }

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context,
            SpellDeliverySettings settings)
        {
            ProjectileDeliverySettings resolved =
                settings as ProjectileDeliverySettings ??
                (ProjectileDeliverySettings)CreateDefaultSettings();
            return new Execution(resolved, context);
        }

        private void OnValidate()
        {
            speed = Mathf.Max(0.01f, speed);
            range = Mathf.Max(0.01f, range);
            collisionRadius = Mathf.Max(0f, collisionRadius);
            maximumTargetHits = Mathf.Max(1, maximumTargetHits);
            castBufferSize = Mathf.Max(1, castBufferSize);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellDeliverySettings settings)
        {
            base.CollectValidationIssues(issues, settings);
            if (issues == null)
                return;
            ProjectileDeliverySettings resolved =
                settings as ProjectileDeliverySettings ??
                (ProjectileDeliverySettings)CreateDefaultSettings();
            if (resolved.Emission.ProjectileCount > 1 &&
                resolved.Emission.Pattern ==
                    ProjectileEmissionPattern.Forward &&
                resolved.Emission.ShotInterval <= 0f)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "Multiple Forward shots with no Shot Interval overlap exactly. Choose Fan, Ring, Random Cone, or add an interval for rapid fire."));
            }
            if (resolved.ShotShape.HitShape == ProjectileHitShape.Cone &&
                resolved.MaximumTargetHits == 1)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "This Cone can affect only one target. Increase Maximum Target Hits if the cone should hit a group."));
            }
        }

        internal static SpellProjectile2D EmitShot(
            in SpellExecutionContext context,
            ProjectileDeliverySettings settings,
            Vector2 direction,
            int shotIndex)
        {
            Vector3 spawnPosition = context.Cast.Caster != null
                ? context.Cast.Caster.transform.position
                : Vector3.zero;
            spawnPosition.x = context.Cast.Origin.x;
            spawnPosition.y = context.Cast.Origin.y;

            if (settings.ShotShape.HitShape !=
                ProjectileHitShape.Projectile)
            {
                SpellInstantRangedShape2D.Fire(
                    context,
                    settings,
                    spawnPosition,
                    direction,
                    shotIndex);
                return null;
            }

            GameObject instance = settings.ProjectilePrefab != null
                ? UnityEngine.Object.Instantiate(
                    settings.ProjectilePrefab,
                    spawnPosition,
                    Quaternion.identity)
                : new GameObject($"{context.Spell.DisplayName} Projectile");

            instance.transform.position = spawnPosition;
            if (!settings.AllowPrefabGameplayComponents)
                SanitizeVisualInstance(instance);

            SpellProjectile2D projectile =
                instance.GetComponent<SpellProjectile2D>();
            if (projectile == null)
                projectile = instance.AddComponent<SpellProjectile2D>();

            projectile.Launch(
                context,
                direction,
                settings.Speed,
                settings.Range,
                settings.CollisionRadius,
                settings.CollisionMask,
                settings.PierceTargets,
                settings.MaximumTargetHits,
                settings.StopOnBlockedCollider,
                settings.CastBufferSize,
                context.Spell.Timing.TimeMode,
                settings.Motion,
                settings.Falloff);
            return projectile;
        }

        internal static Vector2 ResolveShotDirection(
            Vector2 baseDirection,
            ProjectileEmissionSettings emission,
            int shotIndex,
            int randomSeed)
        {
            Vector2 forward = baseDirection.sqrMagnitude > 0.000001f
                ? baseDirection.normalized
                : Vector2.right;
            int count = emission.ProjectileCount;
            if (count <= 1 || emission.Pattern ==
                ProjectileEmissionPattern.Forward)
            {
                return forward;
            }

            float angle;
            switch (emission.Pattern)
            {
                case ProjectileEmissionPattern.Ring:
                    angle = 360f * shotIndex / count;
                    break;
                case ProjectileEmissionPattern.RandomCone:
                {
                    var random = new System.Random(
                        unchecked(randomSeed * 397 + shotIndex * 7919));
                    angle = ((float)random.NextDouble() - 0.5f) *
                            emission.SpreadAngle;
                    break;
                }
                default:
                    angle = count == 1
                        ? 0f
                        : Mathf.Lerp(
                            -emission.SpreadAngle * 0.5f,
                            emission.SpreadAngle * 0.5f,
                            shotIndex / (float)(count - 1));
                    break;
            }

            return Quaternion.Euler(0f, 0f, angle) * forward;
        }

        internal static void SanitizeVisualInstance(GameObject instance)
        {
            MonoBehaviour[] behaviours =
                instance.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null &&
                    !(behaviour is SpellProjectile2D))
                {
                    behaviour.enabled = false;
                }
            }

            Rigidbody2D[] bodies =
                instance.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++)
                bodies[i].simulated = false;

            Collider2D[] colliders =
                instance.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly ProjectileDeliverySettings settings;
            private readonly SpellExecutionContext context;
            private readonly List<SpellProjectile2D> projectiles =
                new List<SpellProjectile2D>();
            private SpellProjectileEmitter2D emitter;

            public bool IsComplete
            {
                get
                {
                    if (emitter != null && !emitter.IsComplete)
                        return false;
                    for (int i = 0; i < projectiles.Count; i++)
                    {
                        if (projectiles[i] != null &&
                            !projectiles[i].IsComplete)
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }

            public Execution(
                ProjectileDeliverySettings deliverySettings,
                in SpellExecutionContext context)
            {
                settings = deliverySettings;
                this.context = context;
            }

            public void Begin()
            {
                ProjectileEmissionSettings emission = settings.Emission;
                int seed = unchecked(
                    (int)context.Cast.RootCastId ^
                    (context.Spell != null
                        ? context.Spell.GetInstanceID()
                        : 0));

                if (emission.ProjectileCount > 1 &&
                    emission.ShotInterval > 0f)
                {
                    var emitterObject = new GameObject(
                        $"{context.Spell.DisplayName} Emitter");
                    emitterObject.transform.position = context.Cast.Origin;
                    emitter = emitterObject.AddComponent<
                        SpellProjectileEmitter2D>();
                    emitter.Initialize(context, settings, seed);
                    return;
                }

                for (int i = 0; i < emission.ProjectileCount; i++)
                {
                    Vector2 direction = ResolveShotDirection(
                        context.Cast.AimDirection,
                        emission,
                        i,
                        seed);
                    SpellProjectile2D projectile = EmitShot(
                        context,
                        settings,
                        direction,
                        i);
                    if (projectile != null)
                        projectiles.Add(projectile);
                }
            }

            public void Tick(float deltaTime) { }

            public void End()
            {
                // Projectiles persist after the caster leaves its firing phase.
            }

            public void Cancel()
            {
                if (emitter != null)
                    emitter.Cancel();
                for (int i = 0; i < projectiles.Count; i++)
                {
                    if (projectiles[i] != null)
                        projectiles[i].Cancel();
                }
            }
        }
    }
}
