using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class TripWireDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("How long the placed wire remains active. Zero means it remains until triggered or removed by another system.")]
        [SerializeField, Min(0f)] private float duration = 10f;
        [Tooltip("How close an object must come to the line to count as crossing it.")]
        [SerializeField, Min(0.01f)] private float triggerWidth = 0.12f;
        [Tooltip("Unity layers searched for objects crossing the wire.")]
        [SerializeField] private LayerMask triggerMask = ~0;
        [Tooltip("Maximum colliders checked around the wire at once.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [Tooltip("Remove the wire after its first valid crossing. Disable this to let different objects trigger it repeatedly.")]
        [SerializeField] private bool singleUse = true;
        [Tooltip("Minimum wait before the same object can trigger a reusable wire again after leaving it.")]
        [SerializeField, Min(0f)] private float retriggerDelay = 0.25f;
        [Tooltip("Color used by the prototype wire visual.")]
        [SerializeField] private Color lineColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [Tooltip("Render order for the prototype wire visual.")]
        [SerializeField] private int sortingOrder = 20;

        public float Duration => Mathf.Max(0f, duration);
        public float TriggerWidth => Mathf.Max(0.01f, triggerWidth);
        public LayerMask TriggerMask => triggerMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);
        public bool SingleUse => singleUse;
        public float RetriggerDelay => Mathf.Max(0f, retriggerDelay);
        public Color LineColor => lineColor;
        public int SortingOrder => sortingOrder;

        public TripWireDeliverySettings() { }

        public TripWireDeliverySettings(
            PlayerTargetingDefinition targeting,
            float wireDuration,
            float width,
            LayerMask mask,
            int capacity,
            bool removeAfterTrigger = true,
            float repeatDelay = 0.25f,
            Color visualColor = default,
            int visualOrder = 20) : base(targeting)
        {
            duration = wireDuration;
            triggerWidth = width;
            triggerMask = mask;
            maximumColliders = capacity;
            singleUse = removeAfterTrigger;
            retriggerDelay = repeatDelay;
            lineColor = visualColor == default
                ? new Color(0.2f, 0.9f, 1f, 0.9f)
                : visualColor;
            sortingOrder = visualOrder;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_TripWire",
        menuName = "Project Eri/Skill System V2/Delivery/Trip Wire")]
    public sealed class TripWireDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default lifetime copied into each spell that equips this delivery.")]
        [SerializeField, Min(0f)] private float duration = 10f;
        [Tooltip("Default distance from the line that counts as crossing.")]
        [SerializeField, Min(0.01f)] private float triggerWidth = 0.12f;
        [Tooltip("Default Unity layers searched for crossing objects.")]
        [SerializeField] private LayerMask triggerMask = ~0;
        [Tooltip("Default maximum colliders checked around the wire.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [Tooltip("Default choice for removing the wire after its first valid crossing.")]
        [SerializeField] private bool singleUse = true;
        [Tooltip("Default reusable-wire delay before one object may trigger again.")]
        [SerializeField, Min(0f)] private float retriggerDelay = 0.25f;
        [Tooltip("Default prototype line color.")]
        [SerializeField] private Color lineColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [Tooltip("Default prototype line render order.")]
        [SerializeField] private int sortingOrder = 20;

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.MultipleTargetPoints |
            CastTargetingRequirement.TargetPoint;
        public override Type SettingsType => typeof(TripWireDeliverySettings);

        public override SpellDeliverySettings CreateDefaultSettings()
        {
            var settings = new TripWireDeliverySettings(
                PlayerTargeting,
                duration,
                triggerWidth,
                triggerMask,
                maximumColliders,
                singleUse,
                retriggerDelay,
                lineColor,
                sortingOrder);
            return settings;
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
                settings as TripWireDeliverySettings ??
                (TripWireDeliverySettings)CreateDefaultSettings());
        }

        private sealed class Execution : ISpellDeliveryExecution
        {
            private readonly SpellExecutionContext context;
            private readonly TripWireDeliverySettings settings;
            private SpellTripWire2D runtime;

            public bool IsComplete => runtime == null || runtime.IsComplete;

            public Execution(
                in SpellExecutionContext executionContext,
                TripWireDeliverySettings deliverySettings)
            {
                context = executionContext;
                settings = deliverySettings;
            }

            public void Begin()
            {
                if (context.Cast.TargetingPayload == null ||
                    !context.Cast.TargetingPayload.TryGetPoint(0, out Vector2 start) ||
                    !context.Cast.TargetingPayload.TryGetPoint(1, out Vector2 end))
                {
                    return;
                }

                var instance = new GameObject(
                    $"{context.Spell.DisplayName} Trip Wire");
                runtime = instance.AddComponent<SpellTripWire2D>();
                runtime.Initialize(context, settings, start, end);
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
    public sealed class SpellTripWire2D : MonoBehaviour
    {
        private SpellExecutionContext context;
        private TripWireDeliverySettings settings;
        private Vector2 start;
        private Vector2 end;
        private float remaining;
        private Collider2D[] buffer;
        private readonly HashSet<int> inside = new HashSet<int>();
        private readonly HashSet<int> currentInside = new HashSet<int>();
        private readonly Dictionary<int, float> lastTriggerTime =
            new Dictionary<int, float>();
        private LineRenderer line;

        public bool IsComplete { get; private set; }

        public void Initialize(
            in SpellExecutionContext executionContext,
            TripWireDeliverySettings deliverySettings,
            Vector2 first,
            Vector2 second)
        {
            context = executionContext;
            settings = deliverySettings;
            start = first;
            end = second;
            remaining = settings.Duration;
            buffer = new Collider2D[settings.MaximumColliders];
            transform.position = (start + end) * 0.5f;
            line = SpellDeliveryVisualUtility.CreateLine(
                gameObject,
                settings.LineColor,
                settings.TriggerWidth,
                settings.SortingOrder);
            // Trip wires belong with the arena/world artwork rather than the
            // default renderer layer.  Explicitly select the project's World
            // sorting layer so the runtime-created LineRenderer remains visible
            // and has predictable depth against character sprites.
            line.sortingLayerID = SortingLayer.NameToID("World");
            SpellDeliveryVisualUtility.SetSegment(line, start, end);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                transform.position,
                end - start,
                this));
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.Armed,
                null,
                transform.position,
                end - start,
                this));
            SpellDeliveryInteractionService.EmitSegment(
                context,
                start,
                end,
                settings.TriggerWidth,
                DeliveryContactPhase.Enter,
                GetInstanceID());
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

            if (settings.Duration > 0f)
            {
                remaining -= Mathf.Max(0f, deltaTime);
                if (remaining <= 0f)
                {
                    Complete(SpellEventType.DeliveryExpired);
                    return;
                }
            }

            Vector2 segment = end - start;
            float distance = segment.magnitude;
            Vector2 center = (start + end) * 0.5f;
            float angle = Mathf.Atan2(segment.y, segment.x) * Mathf.Rad2Deg;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(settings.TriggerMask);
            filter.useTriggers = true;
            int count = Physics2D.OverlapBox(
                center,
                new Vector2(distance, settings.TriggerWidth * 2f),
                angle,
                filter,
                buffer);
            currentInside.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = buffer[i];
                if (hit == null)
                    continue;

                if (!SpellTargetResolver.TryResolveValidTarget(
                        context,
                        hit.gameObject,
                        out GameObject target))
                {
                    continue;
                }

                int id = SpellTargetResolver.GetTargetId(target);
                if (id == 0)
                    continue;
                currentInside.Add(id);
                if (inside.Contains(id) || !CanRetrigger(id))
                    continue;

                lastTriggerTime[id] = Time.unscaledTime;
                Vector2 point = hit.ClosestPoint(center);
                Vector2 normal = target.transform.position - transform.position;
                context.ApplyEffects(
                    target,
                    hit.gameObject,
                    point,
                    normal.sqrMagnitude > 0.000001f
                        ? normal.normalized
                        : Vector2.zero);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetCrossed,
                    target,
                    point,
                    normal,
                    this));
                if (settings.SingleUse)
                {
                    Complete(SpellEventType.DeliveryStopped);
                    return;
                }
            }

            inside.Clear();
            foreach (int id in currentInside)
                inside.Add(id);
        }

        public void Cancel()
        {
            Complete(SpellEventType.DeliveryStopped, report: false);
        }

        private bool CanRetrigger(int id)
        {
            return !lastTriggerTime.TryGetValue(id, out float last) ||
                   Time.unscaledTime - last >= settings.RetriggerDelay;
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
                    end - start,
                    this));
            }
            if (Application.isPlaying)
                Destroy(gameObject);
        }
    }
}
