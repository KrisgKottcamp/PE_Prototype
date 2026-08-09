using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Delivery_Projectile",
        menuName = "Project Eri/Skill System V2/Delivery/Projectile")]
    public sealed class ProjectileDeliveryDefinition : DeliveryDefinition
    {
        [SerializeField]
        private GameObject projectilePrefab;

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

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            return new Execution(this, context);
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
            private readonly ProjectileDeliveryDefinition definition;
            private readonly SpellExecutionContext context;
            private SpellProjectile2D projectile;

            public bool IsComplete => projectile == null || projectile.IsComplete;

            public Execution(
                ProjectileDeliveryDefinition definition,
                in SpellExecutionContext context)
            {
                this.definition = definition;
                this.context = context;
            }

            public void Begin()
            {
                Vector3 spawnPosition = context.Cast.Caster != null
                    ? context.Cast.Caster.transform.position
                    : Vector3.zero;
                spawnPosition.x = context.Cast.Origin.x;
                spawnPosition.y = context.Cast.Origin.y;

                GameObject instance = definition.projectilePrefab != null
                    ? Object.Instantiate(
                        definition.projectilePrefab,
                        spawnPosition,
                        Quaternion.identity)
                    : new GameObject($"{context.Spell.DisplayName} Projectile");

                instance.transform.position = spawnPosition;

                projectile = instance.GetComponent<SpellProjectile2D>();
                if (projectile == null)
                    projectile = instance.AddComponent<SpellProjectile2D>();

                projectile.Launch(
                    context,
                    context.Cast.AimDirection,
                    definition.speed,
                    definition.range,
                    definition.collisionRadius,
                    definition.collisionMask,
                    definition.pierceTargets,
                    definition.maximumTargetHits,
                    definition.stopOnBlockedCollider,
                    definition.castBufferSize,
                    context.Spell.Timing.TimeMode);
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
