using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public sealed class LingeringAreaDeliverySettings : SpellDeliverySettings
    {
        [Tooltip("How far the persistent area reaches from its center.")]
        [SerializeField, Min(0.05f)] private float radius = 2f;
        [Tooltip("How many seconds the area exists before expiring.")]
        [SerializeField, Min(0.05f)] private float duration = 4f;
        [Tooltip("Seconds between repeated checks and non-presence effect applications.")]
        [SerializeField, Min(0.02f)] private float applicationInterval = 0.25f;
        [Tooltip("Unity layers searched for occupants that may receive effects.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Maximum colliders checked per area pulse. Increase only for very crowded areas.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;
        [Tooltip("Simple prototype color used to show the area's size in gameplay.")]
        [SerializeField] private Color zoneColor =
            new Color(0.3f, 0.65f, 1f, 0.24f);
        [Tooltip("How far above or below nearby sprites the prototype area graphic is drawn.")]
        [SerializeField] private int sortingOrderOffset = 20;
        [Tooltip("When enabled, the area applies effects immediately. Disable it when a Reaction should activate the area later.")]
        [SerializeField] private bool startsActive = true;

        public float Radius => Mathf.Max(0.05f, radius);
        public float Duration => Mathf.Max(0.05f, duration);
        public float ApplicationInterval => Mathf.Max(0.02f, applicationInterval);
        public LayerMask HitMask => hitMask;
        public int MaximumColliders => Mathf.Max(1, maximumColliders);
        public Color ZoneColor => zoneColor;
        public int SortingOrderOffset => sortingOrderOffset;
        public bool StartsActive => startsActive;

        public LingeringAreaDeliverySettings() { }
        public LingeringAreaDeliverySettings(PlayerTargetingDefinition targeting,
            float zoneRadius, float zoneDuration, float interval,
            LayerMask mask, int capacity, Color color, int sortingOffset,
            bool activeAtStart = true)
            : base(targeting)
        {
            radius = zoneRadius;
            duration = zoneDuration;
            applicationInterval = interval;
            hitMask = mask;
            maximumColliders = capacity;
            zoneColor = color;
            sortingOrderOffset = sortingOffset;
            startsActive = activeAtStart;
        }
    }

    [CreateAssetMenu(
        fileName = "Delivery_LingeringArea",
        menuName = "Project Eri/Skill System V2/Delivery/Lingering Area at Point")]
    public sealed class LingeringAreaDeliveryDefinition : DeliveryDefinition
    {
        [Tooltip("Default radius copied into a spell when this delivery is equipped.")]
        [SerializeField, Min(0.05f)] private float radius = 2f;
        [Tooltip("Default lifetime copied into a spell when this delivery is equipped.")]
        [SerializeField, Min(0.05f)] private float duration = 4f;
        [Tooltip("Default delay between repeated area applications.")]
        [SerializeField, Min(0.02f)] private float applicationInterval = 0.25f;
        [Tooltip("Default Unity layers searched for area occupants.")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Default maximum colliders checked per area pulse.")]
        [SerializeField, Min(1)] private int maximumColliders = 32;

        [Header("Prototype Visual")]
        [Tooltip("Default prototype color copied into each spell's inline settings.")]
        [SerializeField] private Color zoneColor =
            new Color(0.3f, 0.65f, 1f, 0.24f);
        [Tooltip("Default draw-order offset for the prototype area graphic.")]
        [SerializeField] private int sortingOrderOffset = 20;
        [Tooltip("Default starting active state copied into a spell.")]
        [SerializeField] private bool startsActive = true;

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
                hitMask, maximumColliders, zoneColor, sortingOrderOffset,
                startsActive);
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
                    settings.SortingOrderOffset,
                    settings.StartsActive);
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
    public sealed class SpellLingeringArea2D :
        MonoBehaviour,
        ISpellDeliveryInteractionVolume,
        ISpellDeliveryReactionHost
    {
        private sealed class ReactionRuntimeState
        {
            public bool Triggered;
            public float NextAllowedTime;
            public readonly HashSet<int> TriggeredSources = new HashSet<int>();
        }

        private sealed class ReactiveEffectGroupRuntimeState
        {
            public SpellReactiveEffectGroup Definition;
            public SpellReactiveEffectGroupSource2D Source;
            public bool Active;
            public readonly Dictionary<int, GameObject> PresenceTargets =
                new Dictionary<int, GameObject>();
            public readonly Dictionary<int, GameObject> CurrentTargets =
                new Dictionary<int, GameObject>();
            public readonly List<int> ExitBuffer = new List<int>();
        }

        private static Sprite circleSprite;

        private SpellExecutionContext context;
        private float radius;
        private float remaining;
        private float interval;
        private float untilNextApplication;
        private float untilNextInteractionStay;
        private LayerMask hitMask;
        private Collider2D[] hits;
        private SpellTimeMode timeMode;
        private readonly HashSet<int> appliedThisTick = new HashSet<int>();
        private readonly Dictionary<int, GameObject> presenceTargets =
            new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> currentPresenceTargets =
            new Dictionary<int, GameObject>();
        private readonly List<int> exitBuffer = new List<int>();
        private readonly Dictionary<int, GameObject> eventPresenceTargets =
            new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject>
            currentEventPresenceTargets =
                new Dictionary<int, GameObject>();
        private readonly List<int> eventExitBuffer = new List<int>();
        private readonly List<ReactionRuntimeState> reactionStates =
            new List<ReactionRuntimeState>();
        private readonly List<ReactiveEffectGroupRuntimeState>
            reactiveEffectGroupStates =
                new List<ReactiveEffectGroupRuntimeState>();
        private SpriteRenderer zoneVisual;
        private Color activeColor;
        private bool interactionActive;
        private bool initialized;
        private bool expirationEventDispatched;

        public int InteractionRuntimeId => GetInstanceID();
        public SpellExecutionContext InteractionExecutionContext => context;
        public Vector2 InteractionCenter => transform.position;
        public float InteractionRadius => radius;
        public bool InteractionActive => interactionActive;

        public void Initialize(
            in SpellExecutionContext executionContext,
            float zoneRadius,
            float zoneDuration,
            float tickInterval,
            LayerMask mask,
            int bufferSize,
            Color color,
            int sortingOrderOffset,
            bool startsActive)
        {
            context = executionContext;
            radius = Mathf.Max(0.05f, zoneRadius);
            remaining = Mathf.Max(0.05f, zoneDuration);
            interval = Mathf.Max(0.02f, tickInterval);
            untilNextApplication = 0f;
            untilNextInteractionStay = interval;
            hitMask = mask;
            hits = new Collider2D[Mathf.Max(1, bufferSize)];
            timeMode = context.Spell.Timing.TimeMode;
            InitializeReactiveEffectGroups();

            Renderer casterRenderer = context.Cast.Caster != null
                ? context.Cast.Caster.GetComponentInChildren<Renderer>(true)
                : null;
            if (casterRenderer != null)
                gameObject.layer = casterRenderer.gameObject.layer;

            activeColor = color;
            interactionActive = startsActive;
            zoneVisual = gameObject.AddComponent<SpriteRenderer>();
            zoneVisual.sprite = GetCircleSprite();
            zoneVisual.sortingLayerID = casterRenderer != null
                ? casterRenderer.sortingLayerID
                : 0;
            zoneVisual.sortingOrder = casterRenderer != null
                ? casterRenderer.sortingOrder + sortingOrderOffset
                : sortingOrderOffset;
            transform.localScale = Vector3.one * radius * 2f;
            RefreshVisualState();
            initialized = true;

            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                transform.position,
                context.Cast.AimDirection,
                this));
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.AreaCreated,
                null,
                transform.position,
                Vector2.zero,
                this));

            if (interactionActive)
                PulseEffectsOnOccupants();

            SpellDeliveryInteractionService.Register(this);
        }

        private void Update()
        {
            float delta = timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            remaining -= Mathf.Max(0f, delta);
            untilNextApplication -= Mathf.Max(0f, delta);
            untilNextInteractionStay -= Mathf.Max(0f, delta);

            if (interactionActive && untilNextApplication <= 0f)
            {
                ApplyPeriodicEffects();
                untilNextApplication = interval;
            }

            if (untilNextInteractionStay <= 0f)
            {
                SpellDeliveryInteractionService.EmitCircle(
                    context,
                    transform.position,
                    radius,
                    DeliveryContactPhase.Stay,
                    InteractionRuntimeId);
                untilNextInteractionStay = interval;
            }

            if (remaining <= 0f)
            {
                DispatchExpirationEvent();
                SpellDeliveryInteractionService.EmitCircle(
                    context,
                    transform.position,
                    radius,
                    DeliveryContactPhase.Expire,
                    InteractionRuntimeId);
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            if (interactionActive)
                RefreshPresenceEffects();
            else
                ClearPresenceEffects();
        }

        public void ReceiveInteraction(
            in DeliveryInteractionContext interaction)
        {
            IReadOnlyList<SpellReactionSlot> reactions =
                context.Spell.ReactionSlots;
            EnsureReactionStateCount(reactions.Count);
            float now = Time.unscaledTime;

            for (int i = 0; i < reactions.Count; i++)
            {
                SpellReactionSlot reaction = reactions[i];
                if (reaction == null || !reaction.Enabled ||
                    reaction.Responses.Count == 0 ||
                    !reaction.Filter.Matches(interaction))
                {
                    continue;
                }

                ReactionRuntimeState state = reactionStates[i];
                int sourceKey = ResolveSourceKey(interaction);
                if (state.NextAllowedTime > now ||
                    (reaction.TriggerPolicy ==
                         InteractionTriggerPolicy.OnceTotal &&
                     state.Triggered) ||
                    (reaction.TriggerPolicy ==
                         InteractionTriggerPolicy.OncePerSourceDelivery &&
                     state.TriggeredSources.Contains(sourceKey)))
                {
                    continue;
                }

                state.Triggered = true;
                state.TriggeredSources.Add(sourceKey);
                state.NextAllowedTime = now + reaction.Cooldown;

                IReadOnlyList<DeliveryInteractionResponse> responses =
                    reaction.Responses;
                for (int responseIndex = 0;
                     responseIndex < responses.Count;
                     responseIndex++)
                {
                    DeliveryInteractionResponse response =
                        responses[responseIndex];
                    if (response == null)
                        continue;

                    try
                    {
                        response.Execute(this, interaction);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, context.Spell);
                    }
                }
            }
        }

        public void SetInteractionActive(bool active)
        {
            if (interactionActive == active)
                return;

            interactionActive = active;
            RefreshVisualState();
            if (!active)
                ClearPresenceEffects();
            else
                untilNextApplication = 0f;
        }

        public void PulseEffectsOnOccupants()
        {
            if (!interactionActive)
                return;

            RefreshPresenceEffects();
            ApplyPeriodicEffects();
            untilNextApplication = interval;
        }

        public void SetReactiveEffectGroupActive(
            string groupId,
            bool active,
            bool applyToCurrentOccupantsImmediately)
        {
            ReactiveEffectGroupRuntimeState state =
                FindReactiveEffectGroup(groupId);
            if (state == null)
            {
                Debug.LogWarning(
                    $"Reactive Effect Group '{groupId}' was not found on " +
                    $"spell '{context.Spell.DisplayName}'.",
                    context.Spell);
                return;
            }

            if (!active)
            {
                ClearReactiveGroupPresence(state);
                state.Active = false;
                return;
            }

            state.Active = true;
            if (!interactionActive ||
                !applyToCurrentOccupantsImmediately)
            {
                return;
            }

            RefreshReactiveGroupPresence(state);
            ApplyReactiveGroupPeriodicEffects(state);
        }

        public bool IsReactiveEffectGroupActive(string groupId)
        {
            ReactiveEffectGroupRuntimeState state =
                FindReactiveEffectGroup(groupId);
            return state != null && state.Active;
        }

        public void TriggerEventEffectRoute(
            string routeId,
            in DeliveryInteractionContext interaction)
        {
            context.DispatchEventRoute(
                routeId,
                new SpellEventOccurrence(
                    SpellEventType.ManualReaction,
                    interaction.SourceCaster,
                    interaction.ContactPoint,
                    interaction.Source.Cast.HasAimDirection
                        ? interaction.Source.Cast.AimDirection
                        : Vector2.zero,
                    this));
        }

        public void DestroyDelivery()
        {
            Destroy(gameObject);
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

                if (!SpellTargetResolver.TryResolveValidTarget(
                        context,
                        hit.gameObject,
                        out GameObject target))
                {
                    continue;
                }

                int targetId = SpellTargetResolver.GetTargetId(target);
                if (targetId == 0 || !appliedThisTick.Add(targetId))
                    continue;

                Vector2 point = hit.ClosestPoint(transform.position);
                Vector2 normal = point - (Vector2)transform.position;
                context.ApplyNonPresenceEffects(
                    target,
                    point,
                    normal.sqrMagnitude > 0.000001f
                        ? normal.normalized
                        : Vector2.zero);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    target,
                    point,
                    normal,
                    this));
            }

            for (int groupIndex = 0;
                 groupIndex < reactiveEffectGroupStates.Count;
                 groupIndex++)
            {
                ReactiveEffectGroupRuntimeState state =
                    reactiveEffectGroupStates[groupIndex];
                if (state.Active)
                    ApplyReactiveGroupPeriodicEffects(state);
            }

            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.AreaPulse,
                null,
                transform.position,
                Vector2.zero,
                this));
        }

        private void RefreshPresenceEffects()
        {
            currentPresenceTargets.Clear();
            currentEventPresenceTargets.Clear();
            int count = FindOverlaps();

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                GameObject target = SpellTargetResolver.Resolve(hit.gameObject);
                if (target == null)
                    continue;

                int id = SpellTargetResolver.GetTargetId(target);
                if (id == 0)
                    continue;
                if (!currentEventPresenceTargets.ContainsKey(id))
                {
                    currentEventPresenceTargets.Add(id, target);
                    if (!eventPresenceTargets.ContainsKey(id))
                    {
                        Vector2 enterPoint = hit.ClosestPoint(
                            transform.position);
                        context.DispatchEvent(new SpellEventOccurrence(
                            SpellEventType.TargetEnteredArea,
                            target,
                            enterPoint,
                            enterPoint - (Vector2)transform.position,
                            this));
                    }
                }

                if (currentPresenceTargets.ContainsKey(id) ||
                    !context.Spell.TargetFilter.IsValid(
                        context.Cast,
                        target,
                        hit.gameObject))
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

            eventExitBuffer.Clear();
            foreach (KeyValuePair<int, GameObject> pair in
                     eventPresenceTargets)
            {
                if (!currentEventPresenceTargets.ContainsKey(pair.Key))
                    eventExitBuffer.Add(pair.Key);
            }

            for (int i = 0; i < eventExitBuffer.Count; i++)
            {
                int id = eventExitBuffer[i];
                if (eventPresenceTargets.TryGetValue(
                        id,
                        out GameObject target))
                {
                    context.DispatchEvent(new SpellEventOccurrence(
                        SpellEventType.TargetExitedArea,
                        target,
                        target != null
                            ? (Vector2)target.transform.position
                            : (Vector2)transform.position,
                        Vector2.zero,
                        this));
                }
                eventPresenceTargets.Remove(id);
            }

            foreach (KeyValuePair<int, GameObject> pair in
                     currentEventPresenceTargets)
            {
                eventPresenceTargets[pair.Key] = pair.Value;
            }

            for (int groupIndex = 0;
                 groupIndex < reactiveEffectGroupStates.Count;
                 groupIndex++)
            {
                ReactiveEffectGroupRuntimeState state =
                    reactiveEffectGroupStates[groupIndex];
                if (state.Active)
                    RefreshReactiveGroupPresence(state);
            }
        }

        private void ClearPresenceEffects()
        {
            foreach (GameObject target in presenceTargets.Values)
                RemovePresenceEffects(target);
            presenceTargets.Clear();
            currentPresenceTargets.Clear();
            eventPresenceTargets.Clear();
            currentEventPresenceTargets.Clear();

            for (int i = 0; i < reactiveEffectGroupStates.Count; i++)
                ClearReactiveGroupPresence(reactiveEffectGroupStates[i]);
        }

        private void InitializeReactiveEffectGroups()
        {
            reactiveEffectGroupStates.Clear();
            IReadOnlyList<SpellReactiveEffectGroup> groups =
                context.Spell.ReactiveEffectGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                SpellReactiveEffectGroup definition = groups[i];
                if (definition == null)
                    continue;

                SpellReactiveEffectGroupSource2D source =
                    gameObject.AddComponent<
                        SpellReactiveEffectGroupSource2D>();
                source.Initialize(definition.StableId);
                reactiveEffectGroupStates.Add(
                    new ReactiveEffectGroupRuntimeState
                    {
                        Definition = definition,
                        Source = source,
                        Active = definition.StartsActive
                    });
            }
        }

        private ReactiveEffectGroupRuntimeState FindReactiveEffectGroup(
            string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                return null;

            for (int i = 0; i < reactiveEffectGroupStates.Count; i++)
            {
                ReactiveEffectGroupRuntimeState state =
                    reactiveEffectGroupStates[i];
                if (string.Equals(
                        state.Definition.StableId,
                        groupId,
                        StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }

        private void ApplyReactiveGroupPeriodicEffects(
            ReactiveEffectGroupRuntimeState state)
        {
            int count = FindOverlaps();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                GameObject target = SpellTargetResolver.Resolve(hit.gameObject);
                int targetId = SpellTargetResolver.GetTargetId(target);
                if (target == null || targetId == 0 ||
                    !appliedThisTick.Add(targetId) ||
                    !state.Definition.IsValidTarget(
                        context.Spell,
                        context.Cast,
                        target,
                        hit.gameObject))
                {
                    continue;
                }

                Vector2 point = hit.ClosestPoint(transform.position);
                Vector2 normal = point - (Vector2)transform.position;
                context.ApplyNonPresenceEffectSlotsUnchecked(
                    state.Definition.EffectSlots,
                    target,
                    point,
                    normal.sqrMagnitude > 0.000001f
                        ? normal.normalized
                        : Vector2.zero);
            }
        }

        private void RefreshReactiveGroupPresence(
            ReactiveEffectGroupRuntimeState state)
        {
            state.CurrentTargets.Clear();
            int count = FindOverlaps();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                GameObject target = SpellTargetResolver.Resolve(
                    hit.gameObject);
                int targetId = SpellTargetResolver.GetTargetId(target);
                if (target == null || targetId == 0 ||
                    state.CurrentTargets.ContainsKey(
                        targetId) ||
                    !state.Definition.IsValidTarget(
                        context.Spell,
                        context.Cast,
                        target,
                        hit.gameObject))
                {
                    continue;
                }

                state.CurrentTargets.Add(targetId, target);
                ApplyReactiveGroupPresence(state, target, hit);
            }

            state.ExitBuffer.Clear();
            foreach (KeyValuePair<int, GameObject> pair in
                     state.PresenceTargets)
            {
                if (!state.CurrentTargets.ContainsKey(pair.Key))
                    state.ExitBuffer.Add(pair.Key);
            }

            for (int i = 0; i < state.ExitBuffer.Count; i++)
            {
                int id = state.ExitBuffer[i];
                if (state.PresenceTargets.TryGetValue(
                        id,
                        out GameObject target))
                {
                    RemoveReactiveGroupPresence(state, target);
                }
                state.PresenceTargets.Remove(id);
            }

            foreach (KeyValuePair<int, GameObject> pair in
                     state.CurrentTargets)
            {
                state.PresenceTargets[pair.Key] = pair.Value;
            }
        }

        private void ApplyReactiveGroupPresence(
            ReactiveEffectGroupRuntimeState state,
            GameObject target,
            Collider2D hit)
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

            IReadOnlyList<SpellEffectSlot> effects =
                state.Definition.EffectSlots;
            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                if (slot?.Effect is
                    IAreaPresenceEffectDefinition presence)
                {
                    try
                    {
                        presence.ApplyPresence(
                            effectContext,
                            state.Source,
                            slot.Settings);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, slot.Effect);
                    }
                }
            }
        }

        private void RemoveReactiveGroupPresence(
            ReactiveEffectGroupRuntimeState state,
            GameObject target)
        {
            if (target == null)
                return;

            IReadOnlyList<SpellEffectSlot> effects =
                state.Definition.EffectSlots;
            for (int i = 0; i < effects.Count; i++)
            {
                SpellEffectSlot slot = effects[i];
                if (slot?.Effect is
                    IAreaPresenceEffectDefinition presence)
                {
                    try
                    {
                        presence.RemovePresence(
                            target,
                            state.Source,
                            slot.Settings);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, slot.Effect);
                    }
                }
            }
        }

        private void ClearReactiveGroupPresence(
            ReactiveEffectGroupRuntimeState state)
        {
            foreach (GameObject target in state.PresenceTargets.Values)
                RemoveReactiveGroupPresence(state, target);
            state.PresenceTargets.Clear();
            state.CurrentTargets.Clear();
        }

        private void EnsureReactionStateCount(int count)
        {
            while (reactionStates.Count < count)
                reactionStates.Add(new ReactionRuntimeState());
        }

        private static int ResolveSourceKey(
            in DeliveryInteractionContext interaction)
        {
            if (interaction.SourceRuntimeId != 0)
                return interaction.SourceRuntimeId;

            unchecked
            {
                int spellId = interaction.SourceSpell != null
                    ? interaction.SourceSpell.GetInstanceID()
                    : 0;
                long rootId = interaction.Source.Cast.RootCastId;
                return (spellId * 397) ^
                       (int)(rootId ^ (rootId >> 32));
            }
        }

        private void RefreshVisualState()
        {
            if (zoneVisual == null)
                return;

            Color color = activeColor;
            if (!interactionActive)
                color.a *= 0.3f;
            zoneVisual.color = color;
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
            SpellDeliveryInteractionService.Unregister(this);
            DispatchExpirationEvent();
            ClearPresenceEffects();
        }

        private void DispatchExpirationEvent()
        {
            if (!initialized || expirationEventDispatched ||
                context.Spell == null)
            {
                return;
            }

            expirationEventDispatched = true;
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryExpired,
                null,
                transform.position,
                Vector2.zero,
                this));
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

    public sealed class SpellReactiveEffectGroupSource2D : MonoBehaviour
    {
        public string GroupId { get; private set; }

        public void Initialize(string groupId)
        {
            GroupId = groupId;
        }
    }
}
