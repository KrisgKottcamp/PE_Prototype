using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum GrenadeCollisionMode
    {
        Regular,
        Sticky,
        Bouncy
    }

    [Serializable]
    public sealed class GrenadeDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("Optional prefab used only as the grenade's appearance.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("How fast the grenade travels toward the clicked point.")]
        [SerializeField, Min(0.01f)] private float speed = 8f;
        [Tooltip("Visual height of the grenade at the middle of its initial throw. This is a top-down presentation effect; it does not change collision height.")]
        [SerializeField, Min(0f)] private float throwArcHeight = 0.35f;
        [Tooltip("How many degrees the visible grenade spins per second while it is in its initial throw.")]
        [SerializeField] private float throwSpinDegreesPerSecond = 540f;
        [Tooltip("Seconds from the throw until detonation. The fuse starts immediately.")]
        [SerializeField, Min(0.01f)] private float fuseDuration = 1.5f;
        [Tooltip("Radius used while checking the grenade's movement for collisions.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.1f;
        [Tooltip("How far the grenade's effects reach when it detonates.")]
        [SerializeField, Min(0.01f)] private float explosionRadius = 1.75f;
        [Tooltip("Unity layers that can block, catch, or bounce the grenade while it travels.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Unity layers searched for recipients when the grenade explodes. Target Rules decide which of those recipients are actually affected.")]
        [SerializeField] private LayerMask explosionTargetMask = ~0;
        [Tooltip("Regular stops at a collision, Sticky attaches to it, and Bouncy reflects away from it.")]
        [SerializeField] private GrenadeCollisionMode collisionMode;
        [Tooltip("Fraction of speed kept after each bounce. One keeps full speed.")]
        [SerializeField, Range(0f, 1f)] private float bounceSpeedRetention = 0.8f;
        [Tooltip("Maximum number of surface bounces before a Bouncy grenade stops moving.")]
        [SerializeField, Min(0)] private int maximumBounces = 4;
        [Tooltip("Maximum colliders checked during movement and explosion searches.")]
        [SerializeField, Min(1)] private int castBufferSize = 32;
        [Tooltip("Color of the prototype grenade ring when no visual prefab is assigned.")]
        [SerializeField] private Color prototypeColor = new Color(1f, 0.65f, 0.15f, 1f);

        public GameObject VisualPrefab => visualPrefab;
        public float Speed => Mathf.Max(0.01f, speed);
        public float ThrowArcHeight => Mathf.Max(0f, throwArcHeight);
        public float ThrowSpinDegreesPerSecond => throwSpinDegreesPerSecond;
        public float FuseDuration => Mathf.Max(0.01f, fuseDuration);
        public float CollisionRadius => Mathf.Max(0f, collisionRadius);
        public float ExplosionRadius => Mathf.Max(0.01f, explosionRadius);
        public LayerMask CollisionMask => collisionMask;
        // Existing inline grenade settings predate this field. Treat their
        // missing/zero serialized value as Everything so upgrading a spell
        // never silently turns its explosion target search off.
        public LayerMask ExplosionTargetMask => explosionTargetMask.value != 0
            ? explosionTargetMask
            : ~0;
        public GrenadeCollisionMode CollisionMode => collisionMode;
        public float BounceSpeedRetention => Mathf.Clamp01(bounceSpeedRetention);
        public int MaximumBounces => Mathf.Max(0, maximumBounces);
        public int CastBufferSize => Mathf.Max(1, castBufferSize);
        public Color PrototypeColor => prototypeColor;

        public GrenadeDeliverySettings() { }

        // Kept for existing code/content that constructs grenade settings with
        // the original parameter list. New spells use the overload below with
        // explicit throw-presentation controls.
        public GrenadeDeliverySettings(
            PlayerTargetingDefinition targeting,
            GameObject prefab,
            float travelSpeed,
            float fuse,
            float bodyRadius,
            float blastRadius,
            LayerMask mask,
            GrenadeCollisionMode mode,
            float retainedSpeed,
            int bounceLimit,
            int bufferSize) : this(
                targeting,
                prefab,
                travelSpeed,
                0.35f,
                540f,
                fuse,
                bodyRadius,
                blastRadius,
                mask,
                mode,
                retainedSpeed,
                bounceLimit,
                bufferSize)
        {
        }

        public GrenadeDeliverySettings(
            PlayerTargetingDefinition targeting,
            GameObject prefab,
            float travelSpeed,
            float arcHeight,
            float spinDegreesPerSecond,
            float fuse,
            float bodyRadius,
            float blastRadius,
            LayerMask mask,
            GrenadeCollisionMode mode,
            float retainedSpeed,
            int bounceLimit,
            int bufferSize,
            LayerMask explosionMask = default) : base(targeting)
        {
            visualPrefab = prefab;
            speed = travelSpeed;
            throwArcHeight = arcHeight;
            throwSpinDegreesPerSecond = spinDegreesPerSecond;
            fuseDuration = fuse;
            collisionRadius = bodyRadius;
            explosionRadius = blastRadius;
            collisionMask = mask;
            explosionTargetMask = explosionMask.value == 0
                ? ~0
                : explosionMask;
            collisionMode = mode;
            bounceSpeedRetention = retainedSpeed;
            maximumBounces = bounceLimit;
            castBufferSize = bufferSize;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_Grenade",
        menuName = "Project Eri/Skill System V2/Delivery/Grenade")]
    public sealed class GrenadeDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default optional appearance prefab copied into the spell.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("Default travel speed.")]
        [SerializeField, Min(0.01f)] private float speed = 8f;
        [Tooltip("Default visual height at the middle of the initial throw.")]
        [SerializeField, Min(0f)] private float throwArcHeight = 0.35f;
        [Tooltip("Default visible spin speed during the initial throw.")]
        [SerializeField] private float throwSpinDegreesPerSecond = 540f;
        [Tooltip("Default fuse duration starting when thrown.")]
        [SerializeField, Min(0.01f)] private float fuseDuration = 1.5f;
        [Tooltip("Default movement collision radius.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.1f;
        [Tooltip("Default effect radius at detonation.")]
        [SerializeField, Min(0.01f)] private float explosionRadius = 1.75f;
        [Tooltip("Default layers checked while the grenade moves and bounces.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Default layers searched for recipients at detonation. Target Rules provide the final filter.")]
        [SerializeField] private LayerMask explosionTargetMask = ~0;
        [Tooltip("Default Regular, Sticky, or Bouncy behavior.")]
        [SerializeField] private GrenadeCollisionMode collisionMode;
        [Tooltip("Default fraction of speed kept per bounce.")]
        [SerializeField, Range(0f, 1f)] private float bounceSpeedRetention = 0.8f;
        [Tooltip("Default maximum surface bounces.")]
        [SerializeField, Min(0)] private int maximumBounces = 4;
        [Tooltip("Default maximum collision results checked per movement step.")]
        [SerializeField, Min(1)] private int castBufferSize = 32;

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.TargetPoint;
        public override Type SettingsType => typeof(GrenadeDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new GrenadeDeliverySettings(
                PlayerTargeting,
                visualPrefab,
                speed,
                throwArcHeight,
                throwSpinDegreesPerSecond,
                fuseDuration,
                collisionRadius,
                explosionRadius,
                collisionMask,
                collisionMode,
                bounceSpeedRetention,
                maximumBounces,
                castBufferSize,
                explosionTargetMask);
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
                settings as GrenadeDeliverySettings ??
                (GrenadeDeliverySettings)CreateDefaultSettings());
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly GrenadeDeliverySettings settings;
            private SpellGrenade2D runtime;

            public bool IsComplete => runtime == null || runtime.IsComplete;

            public Execution(
                in SpellExecutionContext executionContext,
                GrenadeDeliverySettings deliverySettings)
            {
                context = executionContext;
                settings = deliverySettings;
            }

            public void Begin()
            {
                var instance = new GameObject(
                    $"{context.Spell.DisplayName} Grenade");
                instance.transform.position = context.Cast.Origin;
                Transform visualRoot = null;
                if (settings.VisualPrefab != null)
                {
                    GameObject visual = UnityEngine.Object.Instantiate(
                        settings.VisualPrefab,
                        instance.transform);
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    SpellDeliveryVisualUtility
                        .SanitizeVisualPrefabInstance(visual);
                    visualRoot = visual.transform;
                }
                runtime = instance.AddComponent<SpellGrenade2D>();
                runtime.SetVisualRoot(visualRoot);
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
    public sealed class SpellGrenade2D : MonoBehaviour
    {
        private SpellExecutionContext context;
        private GrenadeDeliverySettings settings;
        private Vector2 direction;
        private float speed;
        private float fuseRemaining;
        private float straightDistanceRemaining;
        private int bounceCount;
        private bool moving;
        private RaycastHit2D[] castBuffer;
        private Collider2D[] overlapBuffer;
        private LineRenderer fallbackRing;
        private Transform visualRoot;
        private Vector3 visualRootBasePosition;
        private Quaternion visualRootBaseRotation;
        private float initialThrowDistance;
        private float throwDistanceTravelled;
        private float totalDistanceTravelled;
        private int recentlyBouncedColliderId;
        private float collisionRearmDistance;

        public bool IsComplete { get; private set; }

        public void SetVisualRoot(Transform assignedVisualRoot)
        {
            visualRoot = assignedVisualRoot;
        }

        public void Launch(
            in SpellExecutionContext executionContext,
            GrenadeDeliverySettings deliverySettings)
        {
            context = executionContext;
            settings = deliverySettings;
            transform.position = context.Cast.Origin;
            Vector2 offset = context.Cast.TargetPoint - context.Cast.Origin;
            direction = offset.sqrMagnitude > 0.000001f
                ? offset.normalized
                : Vector2.right;
            straightDistanceRemaining = offset.magnitude;
            initialThrowDistance = straightDistanceRemaining;
            throwDistanceTravelled = 0f;
            totalDistanceTravelled = 0f;
            speed = settings.Speed;
            fuseRemaining = settings.FuseDuration;
            moving = straightDistanceRemaining > 0.001f;
            castBuffer = new RaycastHit2D[settings.CastBufferSize];
            overlapBuffer = new Collider2D[settings.CastBufferSize];
            recentlyBouncedColliderId = 0;
            collisionRearmDistance = 0f;
            EnsureVisual();
            CacheVisualTransform();
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
            if (IsComplete)
                return;
            float delta = Mathf.Max(0f, deltaTime);
            fuseRemaining -= delta;
            if (fuseRemaining <= 0f)
            {
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TimerExpired,
                    null,
                    transform.position,
                    Vector2.zero,
                    this));
                Detonate();
                return;
            }

            if (moving && delta > 0f)
                Move(speed * delta);
            UpdateThrowVisual();
            UpdateFallbackVisual();
        }

        public void Cancel()
        {
            if (IsComplete)
                return;
            IsComplete = true;
            Destroy(gameObject);
        }

        private void Move(float requestedDistance)
        {
            float distance = bounceCount == 0
                ? Mathf.Min(requestedDistance, straightDistanceRemaining)
                : requestedDistance;
            if (distance <= 0f)
            {
                moving = false;
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
                    distance)
                : Physics2D.Raycast(
                    origin,
                    direction,
                    filter,
                    castBuffer,
                    distance);
            RaycastHit2D nearest = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D candidate = castBuffer[i];
                if (candidate.collider == null ||
                    IsWaitingToRearmCollision(candidate.collider) ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        candidate.collider.gameObject))
                {
                    continue;
                }
                if (nearest.collider == null ||
                    candidate.distance < nearest.distance)
                {
                    nearest = candidate;
                }
            }

            if (nearest.collider == null)
            {
                Vector2 destination = origin + direction * distance;
                SpellDeliveryInteractionService.EmitSegment(
                    context,
                    origin,
                    destination,
                    settings.CollisionRadius,
                    DeliveryContactPhase.Impact,
                    GetInstanceID());
                transform.position = destination;
                totalDistanceTravelled += distance;
                if (bounceCount == 0)
                {
                    throwDistanceTravelled += distance;
                    straightDistanceRemaining -= distance;
                    if (straightDistanceRemaining <= 0.001f)
                        moving = false;
                }
                return;
            }

            Vector2 point = nearest.point;
            Vector2 surfaceNormal = ResolveSurfaceNormal(nearest.normal);
            totalDistanceTravelled += Mathf.Max(0f, nearest.distance);
            if (bounceCount == 0)
                throwDistanceTravelled += Mathf.Max(0f, nearest.distance);
            transform.position = point;
            SpellDeliveryInteractionService.EmitSegment(
                context,
                origin,
                point,
                settings.CollisionRadius,
                DeliveryContactPhase.Impact,
                GetInstanceID());
            GameObject subject = SpellTargetResolver.Resolve(
                nearest.collider.gameObject);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.BlockingHit,
                subject,
                point,
                surfaceNormal,
                this));

            if (settings.CollisionMode == GrenadeCollisionMode.Sticky)
            {
                moving = false;
                transform.SetParent(nearest.collider.transform, true);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.Stuck,
                    subject,
                    point,
                    surfaceNormal,
                    this));
                return;
            }

            if (settings.CollisionMode == GrenadeCollisionMode.Bouncy &&
                bounceCount < settings.MaximumBounces)
            {
                direction = Vector2.Reflect(direction, surfaceNormal).normalized;
                speed = Mathf.Max(0.01f,
                    speed * settings.BounceSpeedRetention);
                bounceCount++;
                float separation = Mathf.Max(
                    settings.CollisionRadius + 0.01f,
                    0.01f);
                transform.position = point + surfaceNormal * separation +
                    direction * 0.001f;
                recentlyBouncedColliderId = nearest.collider.GetInstanceID();
                collisionRearmDistance = totalDistanceTravelled + Mathf.Max(
                    settings.CollisionRadius * 2f + 0.02f,
                    0.08f);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.Bounced,
                    subject,
                    point,
                    surfaceNormal,
                    this));
                return;
            }

            moving = false;
        }

        private bool IsWaitingToRearmCollision(Collider2D candidate)
        {
            return candidate != null &&
                   totalDistanceTravelled < collisionRearmDistance &&
                   candidate.GetInstanceID() == recentlyBouncedColliderId;
        }

        private Vector2 ResolveSurfaceNormal(Vector2 reportedNormal)
        {
            return reportedNormal.sqrMagnitude > 0.000001f
                ? reportedNormal.normalized
                : -direction;
        }

        private void Detonate()
        {
            if (IsComplete)
                return;
            Vector2 center = transform.position;
            SpellDeliveryInteractionService.EmitCircle(
                context,
                center,
                settings.ExplosionRadius,
                DeliveryContactPhase.Impact,
                GetInstanceID());
            var filter = new ContactFilter2D();
            filter.SetLayerMask(settings.ExplosionTargetMask);
            filter.useTriggers = true;
            int count = Physics2D.OverlapCircle(
                center,
                settings.ExplosionRadius,
                filter,
                overlapBuffer);
            var affected = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapBuffer[i];
                if (hit == null ||
                    !SpellTargetResolver.TryResolveValidTarget(
                        context,
                        hit.gameObject,
                        out GameObject target))
                {
                    continue;
                }
                int targetId = SpellTargetResolver.GetTargetId(target);
                if (targetId == 0 || !affected.Add(targetId))
                    continue;
                Vector2 point = hit.ClosestPoint(center);
                Vector2 normal = (Vector2)target.transform.position - center;
                context.ApplyEffects(
                    target,
                    hit.gameObject,
                    point,
                    normal.normalized,
                    this);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    target,
                    point,
                    normal,
                    this));
            }
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.Detonated,
                null,
                center,
                Vector2.zero,
                this));
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStopped,
                null,
                center,
                Vector2.zero,
                this));
            IsComplete = true;
            Destroy(gameObject);
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

        private void CacheVisualTransform()
        {
            if (visualRoot == null)
                return;

            visualRootBasePosition = visualRoot.localPosition;
            visualRootBaseRotation = visualRoot.localRotation;
        }

        private void UpdateThrowVisual()
        {
            if (visualRoot == null)
                return;

            float progress = initialThrowDistance > 0.0001f &&
                             bounceCount == 0 && moving
                ? Mathf.Clamp01(throwDistanceTravelled / initialThrowDistance)
                : 1f;
            float lift = settings.ThrowArcHeight * 4f * progress *
                         (1f - progress);
            float travelTime = settings.Speed > 0.0001f
                ? throwDistanceTravelled / settings.Speed
                : 0f;
            float spin = bounceCount == 0
                ? travelTime * settings.ThrowSpinDegreesPerSecond
                : 0f;
            visualRoot.localPosition = visualRootBasePosition +
                                       Vector3.up * lift;
            visualRoot.localRotation = visualRootBaseRotation *
                                       Quaternion.Euler(0f, 0f, spin);
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
