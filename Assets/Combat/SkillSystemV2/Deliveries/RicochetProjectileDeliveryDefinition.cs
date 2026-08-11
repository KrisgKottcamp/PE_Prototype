using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class RicochetProjectileDeliverySettings :
        SpellDeliverySettings
    {
        [Tooltip("Optional prefab used only as the ricocheting projectile's appearance.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("Travel speed in world units per second.")]
        [SerializeField, Min(0.01f)] private float speed = 10f;
        [Tooltip("Maximum total distance traveled across all bounces.")]
        [SerializeField, Min(0.01f)] private float range = 14f;
        [Tooltip("Radius used to sweep the projectile path for collisions.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.08f;
        [Tooltip("Unity layers checked for valid targets and bouncing surfaces.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Maximum number of surface reflections before the projectile stops.")]
        [SerializeField, Min(0)] private int maximumBounces = 3;
        [Tooltip("Fraction of speed kept after each surface reflection.")]
        [SerializeField, Range(0f, 1f)] private float bounceSpeedRetention = 1f;
        [Tooltip("Also reflect from valid targets after applying the spell's effects to them.")]
        [SerializeField] private bool bounceOffTargets;
        [Tooltip("Maximum different valid targets that can receive effects before the projectile stops.")]
        [SerializeField, Min(1)] private int maximumTargetHits = 4;
        [Tooltip("Allow player or enemy basic attacks to knock this projectile into a new direction and transfer ownership.")]
        [SerializeField] private bool deflectableByBasicAttacks = true;
        [Tooltip("Speed multiplier applied when a basic attack deflects the projectile.")]
        [SerializeField, Min(0.01f)] private float deflectionSpeedMultiplier = 1.1f;
        [Tooltip("Maximum collision results inspected during one movement step.")]
        [SerializeField, Min(1)] private int castBufferSize = 24;
        [Tooltip("Color of the prototype projectile when no visual prefab is assigned.")]
        [SerializeField] private Color prototypeColor = new Color(0.7f, 0.35f, 1f, 1f);

        public GameObject VisualPrefab => visualPrefab;
        public float Speed => Mathf.Max(0.01f, speed);
        public float Range => Mathf.Max(0.01f, range);
        public float CollisionRadius => Mathf.Max(0f, collisionRadius);
        public LayerMask CollisionMask => collisionMask;
        public int MaximumBounces => Mathf.Max(0, maximumBounces);
        public float BounceSpeedRetention => Mathf.Clamp01(bounceSpeedRetention);
        public bool BounceOffTargets => bounceOffTargets;
        public int MaximumTargetHits => Mathf.Max(1, maximumTargetHits);
        public bool DeflectableByBasicAttacks => deflectableByBasicAttacks;
        public float DeflectionSpeedMultiplier =>
            Mathf.Max(0.01f, deflectionSpeedMultiplier);
        public int CastBufferSize => Mathf.Max(1, castBufferSize);
        public Color PrototypeColor => prototypeColor;

        public RicochetProjectileDeliverySettings() { }

        public RicochetProjectileDeliverySettings(
            PlayerTargetingDefinition targeting,
            GameObject prefab,
            float projectileSpeed,
            float maximumRange,
            float radius,
            LayerMask mask,
            int bounceLimit,
            float retainedSpeed,
            bool bounceFromTargets,
            int targetHitLimit,
            bool canBasicAttacksDeflect,
            float deflectedSpeedMultiplier,
            int bufferSize) : base(targeting)
        {
            visualPrefab = prefab;
            speed = projectileSpeed;
            range = maximumRange;
            collisionRadius = radius;
            collisionMask = mask;
            maximumBounces = bounceLimit;
            bounceSpeedRetention = retainedSpeed;
            bounceOffTargets = bounceFromTargets;
            maximumTargetHits = targetHitLimit;
            deflectableByBasicAttacks = canBasicAttacksDeflect;
            deflectionSpeedMultiplier = deflectedSpeedMultiplier;
            castBufferSize = bufferSize;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_RicochetProjectile",
        menuName = "Project Eri/Skill System V2/Delivery/Ricocheting Projectile")]
    public sealed class RicochetProjectileDeliveryDefinition :
        DeliveryDefinition
    {
        [Tooltip("Default optional appearance prefab.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("Default travel speed.")]
        [SerializeField, Min(0.01f)] private float speed = 10f;
        [Tooltip("Default total travel distance across all reflections.")]
        [SerializeField, Min(0.01f)] private float range = 14f;
        [Tooltip("Default movement collision radius.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.08f;
        [Tooltip("Default collision and bounce layers.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Default maximum surface reflections.")]
        [SerializeField, Min(0)] private int maximumBounces = 3;
        [Tooltip("Default fraction of speed kept after reflection.")]
        [SerializeField, Range(0f, 1f)] private float bounceSpeedRetention = 1f;
        [Tooltip("Default choice for reflecting from valid targets after hitting them.")]
        [SerializeField] private bool bounceOffTargets;
        [Tooltip("Default maximum different targets hit.")]
        [SerializeField, Min(1)] private int maximumTargetHits = 4;
        [Tooltip("Default choice for allowing basic attacks to knock the projectile away and transfer ownership.")]
        [SerializeField] private bool deflectableByBasicAttacks = true;
        [Tooltip("Default speed multiplier after a basic-attack deflection.")]
        [SerializeField, Min(0.01f)] private float deflectionSpeedMultiplier = 1.1f;
        [Tooltip("Default maximum collision results checked per step.")]
        [SerializeField, Min(1)] private int castBufferSize = 24;

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.Direction;
        public override Type SettingsType =>
            typeof(RicochetProjectileDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new RicochetProjectileDeliverySettings(
                PlayerTargeting,
                visualPrefab,
                speed,
                range,
                collisionRadius,
                collisionMask,
                maximumBounces,
                bounceSpeedRetention,
                bounceOffTargets,
                maximumTargetHits,
                deflectableByBasicAttacks,
                deflectionSpeedMultiplier,
                castBufferSize);
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
            return new Execution(
                context,
                settings as RicochetProjectileDeliverySettings ??
                (RicochetProjectileDeliverySettings)CreateDefaultSettings());
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly RicochetProjectileDeliverySettings settings;
            private SpellRicochetProjectile2D runtime;

            public bool IsComplete => runtime == null || runtime.IsComplete;

            public Execution(
                in SpellExecutionContext executionContext,
                RicochetProjectileDeliverySettings deliverySettings)
            {
                context = executionContext;
                settings = deliverySettings;
            }

            public void Begin()
            {
                GameObject instance = settings.VisualPrefab != null
                    ? UnityEngine.Object.Instantiate(
                        settings.VisualPrefab,
                        context.Cast.Origin,
                        Quaternion.identity)
                    : new GameObject(
                        $"{context.Spell.DisplayName} Ricochet Projectile");
                if (settings.VisualPrefab != null)
                {
                    SpellDeliveryVisualUtility
                        .SanitizeVisualPrefabInstance(instance);
                }
                runtime = instance.GetComponent<SpellRicochetProjectile2D>();
                if (runtime == null)
                    runtime = instance.AddComponent<SpellRicochetProjectile2D>();
                runtime.enabled = true;
                runtime.Launch(context, settings);
            }

            public void Tick(float deltaTime) { }
            public void End() { }
            public void Cancel()
            {
                if (runtime != null)
                    runtime.Cancel();
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpellRicochetProjectile2D : MonoBehaviour,
        ISpellDeflectableDelivery
    {
        private SpellExecutionContext context;
        private RicochetProjectileDeliverySettings settings;
        private Vector2 direction;
        private float speed;
        private float distanceTravelled;
        private int bounceCount;
        private RaycastHit2D[] castBuffer;
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private readonly HashSet<int> contactedVolumes = new HashSet<int>();
        private LineRenderer fallbackRing;
        private float lastDeflectionTime = float.NegativeInfinity;
        private int recentlyBouncedColliderId;
        private int recentlyBouncedTargetId;
        private float collisionRearmDistance;
        private int casterImmunityUntilBounceCount;

        public bool IsComplete { get; private set; }

        public void Launch(
            in SpellExecutionContext executionContext,
            RicochetProjectileDeliverySettings deliverySettings)
        {
            context = executionContext;
            settings = deliverySettings;
            transform.position = context.Cast.Origin;
            direction = context.Cast.HasAimDirection
                ? context.Cast.AimDirection
                : Vector2.right;
            speed = settings.Speed;
            castBuffer = new RaycastHit2D[settings.CastBufferSize];
            recentlyBouncedColliderId = 0;
            recentlyBouncedTargetId = 0;
            collisionRearmDistance = 0f;
            // Ignore the caster while the projectile is leaving its launch
            // point. After one real bounce it may return and hit the caster
            // whenever the spell's Target Filter permits self-targeting.
            casterImmunityUntilBounceCount = bounceCount + 1;
            EnsureSensor();
            EnsureVisual();
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                transform.position,
                direction,
                this));
        }

        private void Update()
        {
            if (IsComplete)
                return;
            float delta = context.Spell != null &&
                          context.Spell.Timing.TimeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            Step(delta);
        }

        internal void Step(float deltaTime)
        {
            if (IsComplete || deltaTime <= 0f)
                return;
            float remaining = settings.Range - distanceTravelled;
            float stepDistance = Mathf.Min(
                speed * Mathf.Max(0f, deltaTime),
                remaining);
            if (stepDistance <= 0.0001f)
            {
                Complete(null, transform.position, -direction);
                return;
            }

            Vector2 origin = transform.position;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(settings.CollisionMask);
            filter.useTriggers = true;
            int count = settings.CollisionRadius > 0.0001f
                ? Physics2D.CircleCast(
                    origin,
                    settings.CollisionRadius,
                    direction,
                    filter,
                    castBuffer,
                    stepDistance)
                : Physics2D.Raycast(
                    origin,
                    direction,
                    filter,
                    castBuffer,
                    stepDistance);
            RaycastHit2D nearest = FindNearestValidCollision(count);
            if (nearest.collider == null)
            {
                Vector2 destination = origin + direction * stepDistance;
                EmitInteractions(origin, destination);
                transform.position = destination;
                distanceTravelled += stepDistance;
                UpdateFallbackVisual();
                return;
            }

            Vector2 point = nearest.point;
            Vector2 surfaceNormal = ResolveSurfaceNormal(nearest.normal);
            distanceTravelled += Mathf.Max(0f, nearest.distance);
            EmitInteractions(origin, point);
            transform.position = point;
            // A collision may be either a spell target or a bounce surface.
            // Do not let Target Filter = Any turn unmarked walls into targets:
            // a normal wall should always reach the reflection path below.
            bool hasSpellTarget = SpellTargetResolver.TryResolveSpellTarget(
                nearest.collider.gameObject,
                out GameObject subject);
            bool validTarget = hasSpellTarget && context.Spell != null &&
                               context.Spell.TargetFilter.IsValid(
                                   context.Cast,
                                   subject,
                                   nearest.collider.gameObject);
            int subjectId = SpellTargetResolver.GetTargetId(subject);
            if (validTarget && subjectId != 0 &&
                hitTargets.Add(subjectId))
            {
                context.ApplyEffects(subject, point, surfaceNormal);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    subject,
                    point,
                    surfaceNormal,
                    this));
                if (hitTargets.Count >= settings.MaximumTargetHits)
                {
                    Complete(subject, point, nearest.normal);
                    return;
                }
                if (!settings.BounceOffTargets)
                {
                    Complete(subject, point, nearest.normal);
                    return;
                }
            }
            else if (!validTarget)
            {
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.BlockingHit,
                    subject,
                    point,
                    surfaceNormal,
                    this));
            }

            if (bounceCount >= settings.MaximumBounces)
            {
                Complete(subject, point, surfaceNormal);
                return;
            }

            direction = Vector2.Reflect(direction, surfaceNormal).normalized;
            speed = Mathf.Max(0.01f,
                speed * settings.BounceSpeedRetention);
            bounceCount++;
            // A swept circle touches the surface while its center is still one
            // collision radius away.  Moving only along the reflected travel
            // direction fails to clear that radius on shallow-angle impacts,
            // causing a zero-distance re-hit that consumes the remaining
            // bounces. Move outward along the surface normal first instead.
            float separation = Mathf.Max(
                settings.CollisionRadius + 0.01f,
                0.01f);
            transform.position = point + surfaceNormal * separation +
                direction * 0.001f;
            RememberBounceSurface(nearest.collider, subject);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.Bounced,
                subject,
                point,
                surfaceNormal,
                this));
            UpdateFallbackVisual();
        }

        public bool TryDeflect(GameObject newCaster, Vector2 newDirection)
        {
            if (IsComplete || !settings.DeflectableByBasicAttacks ||
                newCaster == null || newDirection.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            if (Time.unscaledTime - lastDeflectionTime < 0.08f)
                return false;
            lastDeflectionTime = Time.unscaledTime;

            direction = newDirection.normalized;
            speed = Mathf.Max(0.01f,
                speed * settings.DeflectionSpeedMultiplier);
            hitTargets.Clear();
            contactedVolumes.Clear();
            recentlyBouncedColliderId = 0;
            recentlyBouncedTargetId = 0;
            collisionRearmDistance = 0f;
            // The new owner may be overlapping the projectile at the moment
            // of deflection. Give that owner the same one-bounce launch
            // immunity rather than permanent immunity.
            casterImmunityUntilBounceCount = bounceCount + 1;
            CastContext redirected = context.Cast
                .WithCaster(newCaster)
                .WithAimDirection(direction);
            context = new SpellExecutionContext(context.Spell, redirected);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.Deflected,
                newCaster,
                transform.position,
                direction,
                this));
            return true;
        }

        public void Cancel()
        {
            if (IsComplete)
                return;
            IsComplete = true;
            Destroy(gameObject);
        }

        private RaycastHit2D FindNearestValidCollision(int count)
        {
            RaycastHit2D nearest = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D candidate = castBuffer[i];
                if (candidate.collider == null ||
                    IsWaitingToRearmCollision(candidate.collider))
                {
                    continue;
                }

                bool belongsToCaster =
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        candidate.collider.gameObject);
                if (belongsToCaster &&
                    bounceCount < casterImmunityUntilBounceCount)
                {
                    continue;
                }

                if (nearest.collider == null ||
                    candidate.distance < nearest.distance)
                {
                    nearest = candidate;
                }
            }
            return nearest;
        }

        private void RememberBounceSurface(
            Collider2D collider,
            GameObject target)
        {
            recentlyBouncedColliderId = collider != null
                ? collider.GetInstanceID()
                : 0;
            recentlyBouncedTargetId = target != null
                ? target.GetInstanceID()
                : 0;

            // An enemy can have a body collider plus one or more hurtbox
            // colliders. Ignore that same physical contact until the projectile
            // has actually moved clear of it, rather than counting invisible
            // zero-distance re-bounces against every overlapping collider.
            collisionRearmDistance = distanceTravelled + Mathf.Max(
                settings.CollisionRadius * 2f + 0.02f,
                0.08f);
        }

        private bool IsWaitingToRearmCollision(Collider2D candidate)
        {
            if (candidate == null ||
                distanceTravelled >= collisionRearmDistance)
            {
                return false;
            }

            if (candidate.GetInstanceID() == recentlyBouncedColliderId)
                return true;

            if (recentlyBouncedTargetId == 0 ||
                !SpellTargetResolver.TryResolveSpellTarget(
                    candidate.gameObject,
                    out GameObject candidateTarget))
            {
                return false;
            }

            return candidateTarget.GetInstanceID() ==
                   recentlyBouncedTargetId;
        }

        private Vector2 ResolveSurfaceNormal(Vector2 reportedNormal)
        {
            // A cast starting inside/at a collider corner can occasionally
            // report an empty normal. Reversing is the only safe fallback: it
            // guarantees the projectile leaves the surface instead of being
            // trapped in repeated zero-distance collisions.
            return reportedNormal.sqrMagnitude > 0.000001f
                ? reportedNormal.normalized
                : -direction;
        }

        private void EmitInteractions(Vector2 start, Vector2 end)
        {
            SpellDeliveryInteractionService.EmitSegment(
                context,
                start,
                end,
                settings.CollisionRadius,
                DeliveryContactPhase.Impact,
                GetInstanceID(),
                contactedVolumes);
        }

        private void Complete(
            GameObject subject,
            Vector2 point,
            Vector2 normal)
        {
            if (IsComplete)
                return;
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStopped,
                subject,
                point,
                normal,
                this));
            IsComplete = true;
            Destroy(gameObject);
        }

        private void EnsureSensor()
        {
            CircleCollider2D sensor = GetComponent<CircleCollider2D>();
            if (sensor == null)
                sensor = gameObject.AddComponent<CircleCollider2D>();
            sensor.isTrigger = true;
            sensor.radius = Mathf.Max(0.08f, settings.CollisionRadius);
            sensor.enabled = true;
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.simulated = true;
            int projectileLayer = LayerMask.NameToLayer("Projectile");
            if (projectileLayer >= 0)
                gameObject.layer = projectileLayer;
        }

        private void EnsureVisual()
        {
            if (GetComponentInChildren<Renderer>(true) != null)
                return;
            fallbackRing = SpellDeliveryVisualUtility.CreateLine(
                gameObject,
                settings.PrototypeColor,
                0.045f,
                150,
                loop: true);
            UpdateFallbackVisual();
        }

        private void UpdateFallbackVisual()
        {
            if (fallbackRing != null)
            {
                SpellDeliveryVisualUtility.SetCircle(
                    fallbackRing,
                    transform.position,
                    Mathf.Max(0.1f, settings.CollisionRadius));
            }
        }
    }
}
