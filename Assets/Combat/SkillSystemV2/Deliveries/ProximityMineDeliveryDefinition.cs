using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class ProximityMineDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("Optional prefab used only as the placed mine's visible center sprite.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("Seconds after placement before the mine can notice targets.")]
        [SerializeField, Min(0f)] private float armingDelay = 0.4f;
        [Tooltip("How close a valid object must come to trigger the armed mine.")]
        [SerializeField, Min(0.01f)] private float triggerRadius = 1.25f;
        [Tooltip("Optional warning time between detecting an object and applying the mine's effects.")]
        [SerializeField, Min(0f)] private float detonationDelay = 0.15f;
        [Tooltip("How far from the mine its effects reach when it detonates.")]
        [SerializeField, Min(0.01f)] private float effectRadius = 1.75f;
        [Tooltip("Maximum lifetime after placement. Zero means the mine waits indefinitely.")]
        [SerializeField, Min(0f)] private float lifetime = 20f;
        [Tooltip("Unity layers searched both for trigger objects and detonation recipients.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Maximum colliders checked during proximity and detonation searches.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [Tooltip("Remove the mine after it detonates. Disable this to create a repeating proximity device.")]
        [SerializeField] private bool singleUse = true;
        [Tooltip("Delay before a reusable mine arms again after detonating.")]
        [SerializeField, Min(0f)] private float rearmDelay = 1f;
        [Tooltip("Color used by the small fallback mine marker when no Visual Prefab is assigned.")]
        [FormerlySerializedAs("ringColor")]
        [SerializeField] private Color markerColor = new Color(1f, 0.45f, 0.1f, 0.85f);
        [Tooltip("Render order for the mine sprite or fallback marker.")]
        [SerializeField] private int sortingOrder = 20;

        public GameObject VisualPrefab => visualPrefab;
        public float ArmingDelay => Mathf.Max(0f, armingDelay);
        public float TriggerRadius => Mathf.Max(0.01f, triggerRadius);
        public float DetonationDelay => Mathf.Max(0f, detonationDelay);
        public float EffectRadius => Mathf.Max(0.01f, effectRadius);
        public float Lifetime => Mathf.Max(0f, lifetime);
        public LayerMask HitMask => hitMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);
        public bool SingleUse => singleUse;
        public float RearmDelay => Mathf.Max(0f, rearmDelay);
        public Color MarkerColor => markerColor;
        public int SortingOrder => sortingOrder;

        public ProximityMineDeliverySettings() { }

        public ProximityMineDeliverySettings(
            PlayerTargetingDefinition targeting,
            float armAfter,
            float proximity,
            float warningDelay,
            float blastRadius,
            float maximumLifetime,
            LayerMask mask,
            int capacity,
            bool removeAfterDetonation = true,
            float reusableDelay = 1f,
            Color visualColor = default,
            int visualOrder = 20,
            GameObject prefab = null) : base(targeting)
        {
            visualPrefab = prefab;
            armingDelay = armAfter;
            triggerRadius = proximity;
            detonationDelay = warningDelay;
            effectRadius = blastRadius;
            lifetime = maximumLifetime;
            hitMask = mask;
            maximumColliders = capacity;
            singleUse = removeAfterDetonation;
            rearmDelay = reusableDelay;
            markerColor = visualColor == default
                ? new Color(1f, 0.45f, 0.1f, 0.85f)
                : visualColor;
            sortingOrder = visualOrder;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_ProximityMine",
        menuName = "Project Eri/Skill System V2/Delivery/Proximity Mine")]
    public sealed class ProximityMineDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default optional prefab used as the placed mine's visible center sprite.")]
        [SerializeField] private GameObject visualPrefab;
        [Tooltip("Default delay before a placed mine becomes active.")]
        [SerializeField, Min(0f)] private float armingDelay = 0.4f;
        [Tooltip("Default distance at which the mine notices valid objects.")]
        [SerializeField, Min(0.01f)] private float triggerRadius = 1.25f;
        [Tooltip("Default warning delay after the mine notices an object.")]
        [SerializeField, Min(0f)] private float detonationDelay = 0.15f;
        [Tooltip("Default radius that receives the mine's effects.")]
        [SerializeField, Min(0.01f)] private float effectRadius = 1.75f;
        [Tooltip("Default maximum lifetime. Zero means unlimited.")]
        [SerializeField, Min(0f)] private float lifetime = 20f;
        [Tooltip("Default Unity layers searched by the mine.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Default maximum colliders checked at once.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [Tooltip("Default choice for removing the mine after detonation.")]
        [SerializeField] private bool singleUse = true;
        [Tooltip("Default delay before a reusable mine arms again.")]
        [SerializeField, Min(0f)] private float rearmDelay = 1f;
        [Tooltip("Default fallback marker color when no Visual Prefab is assigned.")]
        [FormerlySerializedAs("ringColor")]
        [SerializeField] private Color markerColor = new Color(1f, 0.45f, 0.1f, 0.85f);
        [Tooltip("Default render order for the mine sprite or fallback marker.")]
        [SerializeField] private int sortingOrder = 20;

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.TargetPoint;
        public override Type SettingsType =>
            typeof(ProximityMineDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            return new ProximityMineDeliverySettings(
                PlayerTargeting,
                armingDelay,
                triggerRadius,
                detonationDelay,
                effectRadius,
                lifetime,
                hitMask,
                maximumColliders,
                singleUse,
                rearmDelay,
                markerColor,
                sortingOrder,
                visualPrefab);
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
                settings as ProximityMineDeliverySettings ??
                (ProximityMineDeliverySettings)CreateDefaultSettings());
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly ProximityMineDeliverySettings settings;
            private SpellProximityMine2D runtime;

            public bool IsComplete => runtime == null || runtime.IsComplete;

            public Execution(
                in SpellExecutionContext executionContext,
                ProximityMineDeliverySettings deliverySettings)
            {
                context = executionContext;
                settings = deliverySettings;
            }

            public void Begin()
            {
                GameObject instance = settings.VisualPrefab != null
                    ? UnityEngine.Object.Instantiate(
                        settings.VisualPrefab,
                        context.Cast.TargetPoint,
                        Quaternion.identity)
                    : new GameObject(
                        $"{context.Spell.DisplayName} Proximity Mine");
                if (settings.VisualPrefab != null)
                    SpellDeliveryVisualUtility.SanitizeVisualPrefabInstance(instance);
                runtime = instance.GetComponent<SpellProximityMine2D>();
                if (runtime == null)
                    runtime = instance.AddComponent<SpellProximityMine2D>();
                runtime.enabled = true;
                runtime.Initialize(context, settings);
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
    public sealed class SpellProximityMine2D : MonoBehaviour
    {
        private enum MineState
        {
            Arming,
            Armed,
            Triggered,
            Rearming
        }

        private SpellExecutionContext context;
        private ProximityMineDeliverySettings settings;
        private Collider2D[] buffer;
        private MineState state;
        private float stateTimer;
        private float lifetimeRemaining;
        private GameObject triggeringTarget;
        private GameObject triggeringDetectedObject;
        private static Sprite fallbackMarkerSprite;

        public bool IsComplete { get; private set; }

        public void Initialize(
            in SpellExecutionContext executionContext,
            ProximityMineDeliverySettings deliverySettings)
        {
            context = executionContext;
            settings = deliverySettings;
            buffer = new Collider2D[settings.MaximumColliders];
            lifetimeRemaining = settings.Lifetime;
            EnsureVisual();
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                transform.position,
                Vector2.zero,
                this));
            SpellDeliveryInteractionService.EmitPoint(
                context,
                transform.position,
                DeliveryContactPhase.Enter,
                GetInstanceID());
            BeginArming(settings.ArmingDelay);
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
            if (settings.Lifetime > 0f)
            {
                lifetimeRemaining -= delta;
                if (lifetimeRemaining <= 0f)
                {
                    Complete(SpellEventType.DeliveryExpired);
                    return;
                }
            }

            if (state == MineState.Armed)
            {
                TryFindTrigger();
                return;
            }

            stateTimer -= delta;
            if (stateTimer > 0f)
                return;

            if (state == MineState.Triggered)
            {
                Detonate();
            }
            else if (state == MineState.Arming ||
                     state == MineState.Rearming)
            {
                state = MineState.Armed;
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.Armed,
                    null,
                    transform.position,
                    Vector2.zero,
                    this));
            }
        }

        public void Cancel()
        {
            Complete(SpellEventType.DeliveryStopped, report: false);
        }

        private void BeginArming(float delay)
        {
            state = state == MineState.Triggered
                ? MineState.Rearming
                : MineState.Arming;
            stateTimer = Mathf.Max(0f, delay);
            if (stateTimer <= 0f)
            {
                state = MineState.Armed;
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.Armed,
                    null,
                    transform.position,
                    Vector2.zero,
                    this));
            }
        }

        private void TryFindTrigger()
        {
            int count = Overlap(settings.TriggerRadius);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = buffer[i];
                if (hit == null ||
                    !SpellTargetResolver.TryResolveValidTarget(
                        context,
                        hit.gameObject,
                        out GameObject target))
                {
                    continue;
                }

                triggeringTarget = target;
                triggeringDetectedObject = hit.gameObject;
                state = MineState.Triggered;
                stateTimer = settings.DetonationDelay;
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.ProximityTriggered,
                    target,
                    hit.ClosestPoint(transform.position),
                    (Vector2)target.transform.position -
                    (Vector2)transform.position,
                    this));
                if (stateTimer <= 0f)
                    Detonate();
                return;
            }
        }

        private void Detonate()
        {
            Vector2 center = transform.position;
            SpellDeliveryInteractionService.EmitCircle(
                context,
                center,
                settings.EffectRadius,
                DeliveryContactPhase.Impact,
                GetInstanceID());
            var affected = new HashSet<int>();

            // The object that armed the mine is authoritative. A fresh
            // overlap query can miss a moving trigger or resolve a different
            // hurtbox object during the warning delay, which previously made
            // the mine disappear without applying any effects.
            TryApplyBlastTarget(
                triggeringTarget,
                center,
                affected,
                requireInsideRadius: false,
                detectedObject: triggeringDetectedObject);

            int count = Overlap(settings.EffectRadius);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = buffer[i];
                GameObject target = hit != null
                    ? SpellTargetResolver.Resolve(hit.gameObject)
                    : null;
                TryApplyBlastTarget(
                    target,
                    center,
                    affected,
                    requireInsideRadius: false,
                    sourceCollider: hit,
                    detectedObject: hit != null ? hit.gameObject : null);
            }

            // The combat player's hurtbox can be disabled/reparented during
            // swaps and other legacy combat transitions. If the caster is
            // physically inside the blast and the spell allows it, do not let
            // that collider bookkeeping make a self-damaging mine harmless.
            GameObject caster = context.Cast.Caster;
            TryApplyBlastTarget(
                caster,
                center,
                affected,
                requireInsideRadius: true);

            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.Detonated,
                triggeringTarget,
                center,
                Vector2.zero,
                this));

            if (settings.SingleUse)
                Complete(SpellEventType.DeliveryStopped);
            else
            {
                triggeringTarget = null;
                triggeringDetectedObject = null;
                state = MineState.Triggered;
                BeginArming(settings.RearmDelay);
            }
        }

        private bool TryApplyBlastTarget(
            GameObject target,
            Vector2 center,
            HashSet<int> affected,
            bool requireInsideRadius,
            Collider2D sourceCollider = null,
            GameObject detectedObject = null)
        {
            if (target == null || context.Spell == null ||
                !context.Spell.TargetFilter.IsValid(
                    context.Cast,
                    target,
                    detectedObject != null ? detectedObject : target))
            {
                return false;
            }

            int targetId = SpellTargetResolver.GetTargetId(target);
            if (targetId == 0 || affected.Contains(targetId))
                return false;

            Vector2 targetPosition = target.transform.position;
            Vector2 offset = targetPosition - center;
            if (requireInsideRadius &&
                offset.sqrMagnitude >
                settings.EffectRadius * settings.EffectRadius)
            {
                return false;
            }

            affected.Add(targetId);
            Vector2 point = sourceCollider != null
                ? sourceCollider.ClosestPoint(center)
                : targetPosition;
            Vector2 normal = offset.sqrMagnitude > 0.000001f
                ? offset.normalized
                : Vector2.zero;
            context.ApplyEffects(target, point, normal);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.TargetHit,
                target,
                point,
                offset,
                this));
            return true;
        }

        private int Overlap(float radius)
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(settings.HitMask);
            filter.useTriggers = true;
            return Physics2D.OverlapCircle(
                transform.position,
                radius,
                filter,
                buffer);
        }

        private void EnsureVisual()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                var marker = gameObject.AddComponent<SpriteRenderer>();
                marker.sprite = GetFallbackMarkerSprite();
                marker.color = settings.MarkerColor;
                marker.transform.localScale = Vector3.one * 0.18f;
                renderers = new Renderer[] { marker };
            }

            int worldSortingLayer = SortingLayer.NameToID("World");
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                renderers[i].sortingLayerID = worldSortingLayer;
                renderers[i].sortingOrder = settings.SortingOrder;
            }
        }

        private static Sprite GetFallbackMarkerSprite()
        {
            if (fallbackMarkerSprite != null)
                return fallbackMarkerSprite;

            var texture = new Texture2D(1, 1)
            {
                name = "Skill V2 Mine Marker Texture",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            fallbackMarkerSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            fallbackMarkerSprite.name = "Skill V2 Mine Marker Sprite";
            fallbackMarkerSprite.hideFlags = HideFlags.HideAndDontSave;
            return fallbackMarkerSprite;
        }

        private void Complete(SpellEventType eventType, bool report = true)
        {
            if (IsComplete)
                return;
            IsComplete = true;
            if (report)
            {
                context.DispatchEvent(new SpellEventOccurrence(
                    eventType,
                    null,
                    transform.position,
                    Vector2.zero,
                    this));
            }
            if (Application.isPlaying)
                Destroy(gameObject);
        }
    }
}
