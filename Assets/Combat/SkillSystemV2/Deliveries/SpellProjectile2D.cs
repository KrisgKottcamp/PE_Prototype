using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellProjectile2D : MonoBehaviour
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
        private float distanceTravelled;
        private int targetHitCount;
        private bool launched;
        private SpellMotionRateModifier motionRateModifier;
        private CircleCollider2D slowZoneSensor;

        public bool IsComplete { get; private set; }

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
            castBuffer = new RaycastHit2D[Mathf.Max(1, bufferSize)];
            distanceTravelled = 0f;
            targetHitCount = 0;
            hitTargets.Clear();
            IsComplete = false;
            launched = true;
            motionRateModifier = GetComponent<SpellMotionRateModifier>();

            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            EnsureVisibleFallback();
            EnsureSlowZoneSensor();
        }

        public void Cancel()
        {
            Complete();
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

            float remainingRange = maximumDistance - distanceTravelled;
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
                Complete();
                return;
            }

            Vector2 origin = transform.position;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(collisionMask);
            filter.useTriggers = Physics2D.queriesHitTriggers;
            int count = Physics2D.CircleCast(
                origin,
                collisionRadius,
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
                                       resolved);

                if (!validTarget)
                {
                    if (stopOnBlockedCollider)
                    {
                        transform.position = hit.point;
                        Complete();
                        return;
                    }

                    continue;
                }

                int targetId = resolved.GetInstanceID();
                if (!hitTargets.Add(targetId))
                    continue;

                context.ApplyEffects(
                    resolved,
                    hit.point,
                    hit.normal);
                targetHitCount++;

                if (!pierceTargets || targetHitCount >= maximumTargetHits)
                {
                    transform.position = hit.point;
                    Complete();
                    return;
                }
            }

            transform.position = origin + direction * stepDistance;
            distanceTravelled += stepDistance;

            if (distanceTravelled >= maximumDistance - 0.0001f)
                Complete();
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

        private void Complete()
        {
            if (IsComplete)
                return;

            IsComplete = true;
            launched = false;
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
