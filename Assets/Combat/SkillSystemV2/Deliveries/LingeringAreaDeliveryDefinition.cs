using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class LingeringAreaDeliverySettings : SpellDeliverySettings
    {
        [SerializeField, Min(0.05f)] private float radius = 2f;
        [SerializeField, Min(0.05f)] private float duration = 4f;
        [SerializeField, Min(0.02f)] private float applicationInterval = 0.25f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [SerializeField] private Color zoneColor =
            new Color(0.3f, 0.65f, 1f, 0.24f);
        [SerializeField] private int sortingOrderOffset = 20;

        public float Radius => Mathf.Max(0.05f, radius);
        public float Duration => Mathf.Max(0.05f, duration);
        public float ApplicationInterval => Mathf.Max(0.02f, applicationInterval);
        public LayerMask HitMask => hitMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);
        public Color ZoneColor => zoneColor;
        public int SortingOrderOffset => sortingOrderOffset;

        public LingeringAreaDeliverySettings() { }
        public LingeringAreaDeliverySettings(PlayerTargetingDefinition targeting,
            float zoneRadius, float zoneDuration, float interval,
            LayerMask mask, int capacity, Color color, int sortingOffset)
            : base(targeting)
        {
            radius = zoneRadius;
            duration = zoneDuration;
            applicationInterval = interval;
            hitMask = mask;
            maximumColliders = capacity;
            zoneColor = color;
            sortingOrderOffset = sortingOffset;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_LingeringArea",
        menuName = "Project Eri/Skill System V2/Delivery/Lingering Area at Point")]
    public sealed class LingeringAreaDeliveryDefinition : DeliveryDefinition
    {
        [SerializeField, Min(0.05f)] private float radius = 2f;
        [SerializeField, Min(0.05f)] private float duration = 4f;
        [SerializeField, Min(0.02f)] private float applicationInterval = 0.25f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField, Min(1)] private int maximumColliders = 32;

        [Header("Prototype Visual")]
        [SerializeField] private Color zoneColor =
            new Color(0.3f, 0.65f, 1f, 0.24f);
        [SerializeField] private int sortingOrderOffset = 20;

        public float Radius => Mathf.Max(0.05f, radius);
        public float Duration => Mathf.Max(0.05f, duration);

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.TargetPoint;

        public override Type SettingsType =>
            typeof(LingeringAreaDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new LingeringAreaDeliverySettings(
                PlayerTargeting, radius, duration, applicationInterval,
                hitMask, maximumColliders, zoneColor, sortingOrderOffset);
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
            LingeringAreaDeliverySettings resolved =
                settings as LingeringAreaDeliverySettings ??
                (LingeringAreaDeliverySettings)CreateDefaultSettings();
            return new Execution(resolved, context);
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.05f, radius);
            duration = Mathf.Max(0.05f, duration);
            applicationInterval = Mathf.Max(0.02f, applicationInterval);
            maximumColliders = Mathf.Max(1, maximumColliders);
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly LingeringAreaDeliverySettings settings;
            private readonly SpellExecutionContext context;
            private GameObject zone;

            public bool IsComplete { get; private set; }

            public Execution(
                LingeringAreaDeliverySettings deliverySettings,
                in SpellExecutionContext context)
            {
                settings = deliverySettings;
                this.context = context;
            }

            public void Begin()
            {
                zone = new GameObject($"{context.Spell.DisplayName} Zone");
                zone.transform.position = new Vector3(
                    context.Cast.TargetPoint.x,
                    context.Cast.TargetPoint.y,
                    context.Cast.Caster != null
                        ? context.Cast.Caster.transform.position.z
                        : 0f);

                SpellLingeringArea2D runtime =
                    zone.AddComponent<SpellLingeringArea2D>();
                runtime.Initialize(
                    context,
                    settings.Radius,
                    settings.Duration,
                    settings.ApplicationInterval,
                    settings.HitMask,
                    settings.MaximumColliders,
                    settings.ZoneColor,
                    settings.SortingOrderOffset);
                IsComplete = true;
            }

            public void Tick(float deltaTime) { }
            public void End() { }

            public void Cancel()
            {
                if (zone != null)
                    UnityEngine.Object.Destroy(zone);
                IsComplete = true;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpellLingeringArea2D : MonoBehaviour
    {
        private static Sprite circleSprite;

        private SpellExecutionContext context;
        private float radius;
        private float remaining;
        private float interval;
        private float untilNextApplication;
        private LayerMask hitMask;
        private Collider2D[] hits;
        private SpellTimeMode timeMode;
        private readonly HashSet<int> appliedThisTick = new HashSet<int>();
        private readonly Dictionary<int, GameObject> presenceTargets =
            new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> currentPresenceTargets =
            new Dictionary<int, GameObject>();
        private readonly List<int> exitBuffer = new List<int>();

        public void Initialize(
            in SpellExecutionContext executionContext,
            float zoneRadius,
            float zoneDuration,
            float tickInterval,
            LayerMask mask,
            int bufferSize,
            Color color,
            int sortingOrderOffset)
        {
            context = executionContext;
            radius = Mathf.Max(0.05f, zoneRadius);
            remaining = Mathf.Max(0.05f, zoneDuration);
            interval = Mathf.Max(0.02f, tickInterval);
            untilNextApplication = 0f;
            hitMask = mask;
            hits = new Collider2D[Mathf.Max(1, bufferSize)];
            timeMode = context.Spell.Timing.TimeMode;

            Renderer casterRenderer = context.Cast.Caster != null
                ? context.Cast.Caster.GetComponentInChildren<Renderer>(true)
                : null;
            if (casterRenderer != null)
                gameObject.layer = casterRenderer.gameObject.layer;

            SpriteRenderer visual = gameObject.AddComponent<SpriteRenderer>();
            visual.sprite = GetCircleSprite();
            visual.color = color;
            visual.sortingLayerID = casterRenderer != null
                ? casterRenderer.sortingLayerID
                : 0;
            visual.sortingOrder = casterRenderer != null
                ? casterRenderer.sortingOrder + sortingOrderOffset
                : sortingOrderOffset;
            transform.localScale = Vector3.one * radius * 2f;

            RefreshPresenceEffects();
            ApplyPeriodicEffects();
        }

        private void Update()
        {
            float delta = timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            remaining -= Mathf.Max(0f, delta);
            untilNextApplication -= Mathf.Max(0f, delta);

            if (untilNextApplication <= 0f)
            {
                ApplyPeriodicEffects();
                untilNextApplication = interval;
            }

            if (remaining <= 0f)
                Destroy(gameObject);
        }

        private void LateUpdate()
        {
            RefreshPresenceEffects();
        }

        private int FindOverlaps()
        {
            appliedThisTick.Clear();
            var filter = new ContactFilter2D();
            filter.SetLayerMask(hitMask);
            // Hurtboxes and both projectile families use trigger colliders.
            // Include them even if the project's global raycast setting is
            // configured to ignore triggers.
            filter.useTriggers = true;
            return Physics2D.OverlapCircle(
                transform.position,
                radius,
                filter,
                hits);
        }

        private void ApplyPeriodicEffects()
        {
            int count = FindOverlaps();

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                GameObject target = SpellTargetResolver.Resolve(hit.gameObject);
                if (target == null ||
                    !appliedThisTick.Add(target.GetInstanceID()))
                {
                    continue;
                }

                Vector2 point = hit.ClosestPoint(transform.position);
                Vector2 normal = point - (Vector2)transform.position;
                context.ApplyNonPresenceEffects(
                    target,
                    point,
                    normal.sqrMagnitude > 0.000001f
                        ? normal.normalized
                        : Vector2.zero);
            }
        }

        private void RefreshPresenceEffects()
        {
            currentPresenceTargets.Clear();
            int count = FindOverlaps();

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                GameObject target = SpellTargetResolver.Resolve(hit.gameObject);
                if (target == null)
                    continue;

                int id = target.GetInstanceID();
                if (currentPresenceTargets.ContainsKey(id) ||
                    !context.Spell.TargetFilter.IsValid(context.Cast, target))
                {
                    continue;
                }

                currentPresenceTargets.Add(id, target);
                ApplyPresenceEffects(target, hit);
            }

            exitBuffer.Clear();
            foreach (KeyValuePair<int, GameObject> pair in presenceTargets)
            {
                if (!currentPresenceTargets.ContainsKey(pair.Key))
                    exitBuffer.Add(pair.Key);
            }

            for (int i = 0; i < exitBuffer.Count; i++)
            {
                int id = exitBuffer[i];
                if (presenceTargets.TryGetValue(id, out GameObject target))
                    RemovePresenceEffects(target);
                presenceTargets.Remove(id);
            }

            foreach (KeyValuePair<int, GameObject> pair in currentPresenceTargets)
                presenceTargets[pair.Key] = pair.Value;
        }

        private void ApplyPresenceEffects(GameObject target, Collider2D hit)
        {
            Vector2 point = hit.ClosestPoint(transform.position);
            Vector2 normal = point - (Vector2)transform.position;
            var effectContext = new SpellEffectContext(
                context.Spell,
                context.Cast,
                target,
                point,
                normal.sqrMagnitude > 0.000001f
                    ? normal.normalized
                    : Vector2.zero,
                1f);

            var effects = context.Spell.EffectSlots;
            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                EffectDefinition effect = slot?.Effect;
                if (effect is IAreaPresenceEffectDefinition presence)
                {
                    try
                    {
                        presence.ApplyPresence(
                            effectContext,
                            this,
                            slot.Settings);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, effect);
                    }
                }
            }
        }

        private void RemovePresenceEffects(GameObject target)
        {
            if (target == null)
                return;

            var effects = context.Spell.EffectSlots;
            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                EffectDefinition effect = slot?.Effect;
                if (effect is IAreaPresenceEffectDefinition presence)
                {
                    try
                    {
                        presence.RemovePresence(
                            target,
                            this,
                            slot.Settings);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, effect);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            foreach (GameObject target in presenceTargets.Values)
                RemovePresenceEffects(target);
            presenceTargets.Clear();
        }

        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
                return circleSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime V2 Lingering Area",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float edge = size * 0.48f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    byte alpha = distance <= edge ? (byte)255 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            circleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            circleSprite.name = "Runtime V2 Lingering Area Sprite";
            circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return circleSprite;
        }
    }
}
