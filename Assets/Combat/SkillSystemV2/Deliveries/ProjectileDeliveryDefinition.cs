using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class ProjectileDeliverySettings : SpellDeliverySettings
    {
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private bool allowPrefabGameplayComponents;
        [SerializeField, Min(0.01f)] private float speed = 8f;
        [SerializeField, Min(0.01f)] private float range = 10f;
        [SerializeField, Min(0f)] private float collisionRadius = 0.08f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private bool pierceTargets;
        [SerializeField, Min(1)] private int maximumTargetHits = 1;
        [SerializeField] private bool stopOnBlockedCollider = true;
        [SerializeField, Min(1)] private int castBufferSize = 16;

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
    }

    [CreateAssetMenu(
        fileName = "Delivery_Projectile",
        menuName = "Project Eri/Skill System V2/Delivery/Projectile")]
    public sealed class ProjectileDeliveryDefinition : DeliveryDefinition
    {
        [SerializeField]
        private GameObject projectilePrefab;

        [Tooltip("Advanced compatibility escape hatch. Leave disabled so V2 strips legacy gameplay scripts, Rigidbody simulation, and colliders from an assigned projectile prefab while preserving its renderers and animation.")]
        [SerializeField]
        private bool allowPrefabGameplayComponents;

        [SerializeField, Min(0.01f)]
        private float speed = 8f;

        [SerializeField, Min(0.01f)]
        private float range = 10f;

        [SerializeField, Min(0f)]
        private float collisionRadius = 0.08f;

        [SerializeField]
        private LayerMask collisionMask = ~0;

        [SerializeField]
        private bool pierceTargets;

        [SerializeField, Min(1)]
        private int maximumTargetHits = 1;

        [Tooltip("When enabled, colliders rejected by the spell's Target Filter stop the projectile. Use this for walls and cover.")]
        [SerializeField]
        private bool stopOnBlockedCollider = true;

        [SerializeField, Min(1)]
        private int castBufferSize = 16;

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.Direction;

        public override Type SettingsType =>
            typeof(ProjectileDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new ProjectileDeliverySettings(
                PlayerTargeting, projectilePrefab, allowPrefabGameplayComponents,
                speed, range, collisionRadius, collisionMask, pierceTargets,
                maximumTargetHits, stopOnBlockedCollider, castBufferSize);
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

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly ProjectileDeliverySettings settings;
            private readonly SpellExecutionContext context;
            private SpellProjectile2D projectile;

            public bool IsComplete => projectile == null || projectile.IsComplete;

            public Execution(
                ProjectileDeliverySettings deliverySettings,
                in SpellExecutionContext context)
            {
                settings = deliverySettings;
                this.context = context;
            }

            public void Begin()
            {
                Vector3 spawnPosition = context.Cast.Caster != null
                    ? context.Cast.Caster.transform.position
                    : Vector3.zero;
                spawnPosition.x = context.Cast.Origin.x;
                spawnPosition.y = context.Cast.Origin.y;

                GameObject instance = settings.ProjectilePrefab != null
                    ? UnityEngine.Object.Instantiate(
                        settings.ProjectilePrefab,
                        spawnPosition,
                        Quaternion.identity)
                    : new GameObject($"{context.Spell.DisplayName} Projectile");

                instance.transform.position = spawnPosition;

                if (!settings.AllowPrefabGameplayComponents)
                    SanitizeVisualInstance(instance);

                projectile = instance.GetComponent<SpellProjectile2D>();
                if (projectile == null)
                    projectile = instance.AddComponent<SpellProjectile2D>();

                projectile.Launch(
                    context,
                    context.Cast.AimDirection,
                    settings.Speed,
                    settings.Range,
                    settings.CollisionRadius,
                    settings.CollisionMask,
                    settings.PierceTargets,
                    settings.MaximumTargetHits,
                    settings.StopOnBlockedCollider,
                    settings.CastBufferSize,
                    context.Spell.Timing.TimeMode);
            }

            private static void SanitizeVisualInstance(GameObject instance)
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

            public void Tick(float deltaTime) { }

            public void End()
            {
                // Projectiles persist after the caster leaves its firing phase.
            }

            public void Cancel()
            {
                if (projectile != null)
                    projectile.Cancel();
            }
        }
    }
}
