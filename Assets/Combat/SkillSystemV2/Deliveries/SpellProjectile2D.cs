using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellProjectile2D : MonoBehaviour,
        ISpellSpatialForceTarget,
        ISpellDeliveryRadiusProvider,
        ISpellDeliveryGeometryProvider
    {
        private static Sprite fallbackSprite;
        private SpellExecutionContext context;
        private Vector2 direction;
        private float speed;
        private float maximumDistance;
        private float collisionRadius;
        private LayerMask collisionMask;
        private bool pierceTargets;
        private int maximumTargetHits;
        private bool stopOnBlockedCollider;
        private SpellTimeMode timeMode;
        private RaycastHit2D[] castBuffer;
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private readonly HashSet<int> contactedDeliveryVolumes =
            new HashSet<int>();
        private float distanceTravelled;
        private int targetHitCount;
        private bool launched;
        private SpellMotionRateModifier motionRateModifier;
        private CircleCollider2D slowZoneSensor;
        private ProjectileMotionSettings motionSettings;
        private ProjectileFalloffSettings falloffSettings;
        private Collider2D[] homingBuffer;
        private bool returningToCaster;

        public bool IsComplete { get; private set; }
        public GameObject SpatialForceTargetObject => gameObject;
        public float DeliveryRadius => collisionRadius;

        public bool TryGetDeliveryGeometry(
            out SpellDeliveryGeometry geometry)
        {
            geometry = SpellDeliveryGeometry.FollowCircle(
                transform,
                collisionRadius);
            return launched && !IsComplete;
        }

        public void Launch(
            in SpellExecutionContext executionContext,
            Vector2 aimDirection,
            float projectileSpeed,
            float range,
            float radius,
            LayerMask mask,
            bool shouldPierceTargets,
            int maximumHits,
            bool shouldStopOnBlockedCollider,
            int bufferSize,
            SpellTimeMode projectileTimeMode)
        {
            Launch(
                executionContext,
                aimDirection,
                projectileSpeed,
                range,
                radius,
                mask,
                shouldPierceTargets,
                maximumHits,
                shouldStopOnBlockedCollider,
                bufferSize,
                projectileTimeMode,
                new ProjectileMotionSettings(),
                new ProjectileFalloffSettings());
        }

        public void Launch(
            in SpellExecutionContext executionContext,
            Vector2 aimDirection,
            float projectileSpeed,
            float range,
            float radius,
            LayerMask mask,
            bool shouldPierceTargets,
            int maximumHits,
            bool shouldStopOnBlockedCollider,
            int bufferSize,
            SpellTimeMode projectileTimeMode,
            ProjectileMotionSettings projectileMotion,
            ProjectileFalloffSettings projectileFalloff)
        {
            context = executionContext;
            direction = aimDirection.sqrMagnitude > 0.000001f
                ? aimDirection.normalized
                : Vector2.right;
            speed = Mathf.Max(0.01f, projectileSpeed);
            maximumDistance = Mathf.Max(0.01f, range);
            collisionRadius = Mathf.Max(0f, radius);
            collisionMask = mask;
            pierceTargets = shouldPierceTargets;
            maximumTargetHits = Mathf.Max(1, maximumHits);
            stopOnBlockedCollider = shouldStopOnBlockedCollider;
            timeMode = projectileTimeMode;
            motionSettings = projectileMotion ??
                             new ProjectileMotionSettings();
            falloffSettings = projectileFalloff ??
                              new ProjectileFalloffSettings();
            castBuffer = new RaycastHit2D[Mathf.Max(1, bufferSize)];
            homingBuffer = new Collider2D[Mathf.Max(8, bufferSize)];
            distanceTravelled = 0f;
            targetHitCount = 0;
            hitTargets.Clear();
            contactedDeliveryVolumes.Clear();
            returningToCaster = false;
            IsComplete = false;
            launched = true;
            motionRateModifier = GetComponent<SpellMotionRateModifier>();

            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            EnsureVisibleFallback();
            EnsureSlowZoneSensor();
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                transform.position,
                direction,
                this).WithGeometry(
                    SpellDeliveryGeometry.FollowCircle(
                        transform,
                        collisionRadius)));
        }

        public void Cancel()
        {
            Complete(
                null,
                transform.position,
                -direction,
                reportStopped: false);
        }

        private void Update()
        {
            if (!launched || IsComplete)
                return;

            float deltaTime = timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            Step(Mathf.Max(0f, deltaTime));
        }

        internal void Step(float deltaTime)
        {
            if (!launched || IsComplete || deltaTime <= 0f)
                return;

            UpdateMotionDirection(deltaTime);
            if (IsComplete)
                return;
            float travelLimit = motionSettings.Pattern ==
                                ProjectileMotionPattern.Boomerang
                ? maximumDistance * 2f
                : maximumDistance;
            float remainingRange = travelLimit - distanceTravelled;
            if (motionRateModifier == null)
                motionRateModifier = GetComponent<SpellMotionRateModifier>();
            float speedMultiplier = motionRateModifier != null
                ? motionRateModifier.Multiplier
                : 1f;
            float stepDistance = Mathf.Min(
                speed * speedMultiplier * deltaTime,
                remainingRange);

            if (stepDistance <= 0f)
            {
                Complete(
                    null,
                    transform.position,
                    -direction,
                    reportStopped: true);
                return;
            }

            Vector2 origin = transform.position;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(collisionMask);
            filter.useTriggers = true;
            int count = collisionRadius > 0.0001f
                ? Physics2D.CircleCast(
                    origin,
                    collisionRadius,
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
            SortHitsByDistance(count);

            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = castBuffer[i];
                if (hit.collider == null)
                    continue;

                GameObject resolved = SpellTargetResolver.Resolve(
                    hit.collider.gameObject);
                if (resolved == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        resolved))
                {
                    continue;
                }

                bool validTarget = context.Spell != null &&
                                   context.Spell.TargetFilter.IsValid(
                                       context.Cast,
                                       resolved,
                                       hit.collider.gameObject);

                if (!validTarget)
                {
                    if (stopOnBlockedCollider)
                    {
                        EmitDeliveryInteractions(origin, hit.point);
                        transform.position = hit.point;
                        context.DispatchEvent(new SpellEventOccurrence(
                            SpellEventType.BlockingHit,
                            resolved,
                            hit.point,
                            hit.normal,
                            this).WithGeometry(
                                SpellDeliveryGeometry.Circle(
                                    hit.point,
                                    collisionRadius)));
                        Complete(
                            resolved,
                            hit.point,
                            hit.normal,
                            reportStopped: true);
                        return;
                    }

                    continue;
                }

                int targetId = SpellTargetResolver.GetTargetId(resolved);
                if (targetId == 0)
                    continue;
                if (!hitTargets.Add(targetId))
                    continue;

                context.ApplyEffects(
                    resolved,
                    hit.collider.gameObject,
                    hit.point,
                    hit.normal,
                    this,
                    falloffSettings.Evaluate(
                        (distanceTravelled + hit.distance) /
                        maximumDistance));
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    resolved,
                    hit.point,
                    hit.normal,
                    this).WithGeometry(
                        SpellDeliveryGeometry.Circle(
                            hit.point,
                            collisionRadius)));
                targetHitCount++;

                if (!pierceTargets || targetHitCount >= maximumTargetHits)
                {
                    EmitDeliveryInteractions(origin, hit.point);
                    transform.position = hit.point;
                    Complete(
                        resolved,
                        hit.point,
                        hit.normal,
                        reportStopped: true);
                    return;
                }
            }

            Vector2 destination = origin + direction * stepDistance;
            EmitDeliveryInteractions(origin, destination);
            transform.position = destination;
            distanceTravelled += stepDistance;

            if (distanceTravelled >= travelLimit - 0.0001f)
            {
                Complete(
                    null,
                    destination,
                    -direction,
                    reportStopped: true);
            }
        }

        private void UpdateMotionDirection(float deltaTime)
        {
            if (motionSettings == null)
                return;

            switch (motionSettings.Pattern)
            {
                case ProjectileMotionPattern.Homing:
                    UpdateHomingDirection(deltaTime);
                    break;
                case ProjectileMotionPattern.Boomerang:
                    UpdateBoomerangDirection();
                    break;
            }

            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void UpdateHomingDirection(float deltaTime)
        {
            if (homingBuffer == null || context.Spell == null)
                return;

            var filter = new ContactFilter2D();
            filter.SetLayerMask(collisionMask);
            filter.useTriggers = true;
            int count = Physics2D.OverlapCircle(
                transform.position,
                motionSettings.HomingAcquireRadius,
                filter,
                homingBuffer);

            GameObject closest = null;
            float closestSqr = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider2D candidateCollider = homingBuffer[i];
                if (candidateCollider == null)
                    continue;
                GameObject candidate = SpellTargetResolver.Resolve(
                    candidateCollider.gameObject);
                if (candidate == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        candidate) ||
                    !context.Spell.TargetFilter.IsValid(
                        context.Cast,
                        candidate,
                        candidateCollider.gameObject))
                {
                    continue;
                }

                float sqr = ((Vector2)candidate.transform.position -
                             (Vector2)transform.position).sqrMagnitude;
                if (sqr >= closestSqr)
                    continue;
                closestSqr = sqr;
                closest = candidate;
            }

            if (closest == null)
                return;

            Vector2 desired = (Vector2)closest.transform.position -
                              (Vector2)transform.position;
            if (desired.sqrMagnitude <= 0.000001f)
                return;

            float currentAngle = Mathf.Atan2(direction.y, direction.x) *
                                 Mathf.Rad2Deg;
            float desiredAngle = Mathf.Atan2(desired.y, desired.x) *
                                 Mathf.Rad2Deg;
            float nextAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                desiredAngle,
                motionSettings.HomingTurnRate * deltaTime);
            float radians = nextAngle * Mathf.Deg2Rad;
            direction = new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians));
        }

        private void UpdateBoomerangDirection()
        {
            if (!returningToCaster &&
                distanceTravelled >= maximumDistance *
                motionSettings.ReturnAtRangeFraction)
            {
                returningToCaster = true;
            }

            if (!returningToCaster || context.Cast.Caster == null)
                return;

            Vector2 toCaster =
                (Vector2)context.Cast.Caster.transform.position -
                (Vector2)transform.position;
            if (toCaster.magnitude <= motionSettings.ReturnCatchRadius)
            {
                Complete(
                    context.Cast.Caster,
                    transform.position,
                    -direction,
                    reportStopped: true);
                return;
            }

            direction = toCaster.normalized;
        }

        private void EmitDeliveryInteractions(Vector2 start, Vector2 end)
        {
            SpellDeliveryInteractionService.EmitSegment(
                context,
                start,
                end,
                collisionRadius,
                DeliveryContactPhase.Impact,
                GetInstanceID(),
                contactedDeliveryVolumes);
        }

        private void SortHitsByDistance(int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                int nearest = i;
                for (int j = i + 1; j < count; j++)
                {
                    if (castBuffer[j].distance < castBuffer[nearest].distance)
                        nearest = j;
                }

                if (nearest == i)
                    continue;

                RaycastHit2D swap = castBuffer[i];
                castBuffer[i] = castBuffer[nearest];
                castBuffer[nearest] = swap;
            }
        }

        private void Complete(
            GameObject subject,
            Vector2 stopPoint,
            Vector2 stopNormal,
            bool reportStopped)
        {
            if (IsComplete)
                return;

            if (reportStopped)
            {
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.DeliveryStopped,
                    subject,
                    stopPoint,
                    stopNormal,
                    this).WithGeometry(
                        SpellDeliveryGeometry.Circle(
                            stopPoint,
                            collisionRadius)));
            }

            IsComplete = true;
            launched = false;
            // EditMode tests own their fixture objects and clean them up with
            // DestroyImmediate during TearDown. Calling delayed Destroy here
            // produces an unexpected Unity error log and fails the test even
            // when the projectile and recipe behaved correctly.
            if (Application.isPlaying)
                Destroy(gameObject);
        }

        private void EnsureVisibleFallback()
        {
            if (GetComponentInChildren<Renderer>(true) != null)
                return;

            Renderer casterRenderer = context.Cast.Caster != null
                ? context.Cast.Caster.GetComponentInChildren<Renderer>(true)
                : null;
            if (context.Cast.Caster != null && gameObject.layer == 0)
            {
                gameObject.layer = casterRenderer != null
                    ? casterRenderer.gameObject.layer
                    : context.Cast.Caster.layer;
            }

            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = GetFallbackSprite();
            renderer.color = new Color(0.25f, 0.9f, 1f, 1f);
            renderer.sortingOrder = 150;
            if (casterRenderer != null)
            {
                renderer.sortingLayerID = casterRenderer.sortingLayerID;
                renderer.sortingOrder = casterRenderer.sortingOrder + 50;
            }
            transform.localScale = Vector3.one * 0.22f;
        }

        private void EnsureSlowZoneSensor()
        {
            int projectileLayer = LayerMask.NameToLayer("PlayerProjectile");
            if (slowZoneSensor == null)
            {
                var sensorObject = new GameObject("Skill V2 Slow Sensor");
                sensorObject.transform.SetParent(transform, false);
                if (projectileLayer >= 0)
                    sensorObject.layer = projectileLayer;
                Rigidbody2D sensorBody = sensorObject.AddComponent<Rigidbody2D>();
                sensorBody.bodyType = RigidbodyType2D.Kinematic;
                sensorBody.gravityScale = 0f;
                slowZoneSensor = sensorObject.AddComponent<CircleCollider2D>();
            }

            float maximumScale = Mathf.Max(
                0.01f,
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y));
            slowZoneSensor.radius =
                Mathf.Max(0.05f, collisionRadius) / maximumScale;
            slowZoneSensor.isTrigger = true;
            slowZoneSensor.enabled = true;
        }

        private static Sprite GetFallbackSprite()
        {
            if (fallbackSprite != null)
                return fallbackSprite;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime V2 Projectile",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.46f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float normalized = Vector2.Distance(new Vector2(x, y), center) / radius;
                    byte alpha = normalized <= 1f
                        ? (byte)Mathf.RoundToInt(Mathf.Clamp01((1f - normalized) * 3f) * 255f)
                        : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            fallbackSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            fallbackSprite.name = "Runtime V2 Projectile Sprite";
            fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
            return fallbackSprite;
        }

        private void OnDestroy()
        {
            IsComplete = true;
            launched = false;
        }
    }
}
