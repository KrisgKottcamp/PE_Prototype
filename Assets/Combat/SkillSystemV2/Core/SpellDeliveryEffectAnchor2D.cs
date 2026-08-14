using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Creates independent, geometry-aware effect lifetimes without keeping a
    /// transient delivery alive. This is intentionally effect-agnostic: every
    /// effect slot may run once at creation, on entry, periodically, or with
    /// exact presence ownership when its effect supports that contract.
    /// </summary>
    public static class SpellDeliveryEffectAnchorService
    {
        private static readonly Dictionary<string, SpellDeliveryEffectAnchor2D>
            anchors =
                new Dictionary<string, SpellDeliveryEffectAnchor2D>(
                    StringComparer.Ordinal);
        private static readonly List<string> removeKeys =
            new List<string>();
        private static readonly HashSet<string> consumedKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> consumedOrder =
            new Queue<string>();
        private const int MaximumRememberedKeys = 4096;

        public static int ActiveAnchorCount
        {
            get
            {
                PruneDestroyed();
                SpellDeliveryEffectAnchor2D[] active =
                    UnityEngine.Object.FindObjectsOfType<
                        SpellDeliveryEffectAnchor2D>(true);
                int count = 0;
                for (int i = 0; i < active.Length; i++)
                {
                    if (active[i] != null && !active[i].IsComplete)
                        count++;
                }
                return count;
            }
        }

        public static int ActivateDefaultEffects(
            in SpellExecutionContext context,
            in SpellEventOccurrence occurrence)
        {
            return Activate(
                context,
                context.Spell != null
                    ? context.Spell.EffectSlots
                    : null,
                occurrence,
                "default",
                honorSlotTrigger: true);
        }

        public static int ActivateEventEffects(
            in SpellExecutionContext context,
            IReadOnlyList<SpellEffectSlot> slots,
            in SpellEventOccurrence occurrence,
            string routeId)
        {
            return Activate(
                context,
                slots,
                occurrence,
                string.IsNullOrWhiteSpace(routeId)
                    ? "event"
                    : $"event:{routeId}",
                honorSlotTrigger: false);
        }

        internal static void Unregister(
            string key,
            SpellDeliveryEffectAnchor2D anchor)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (anchors.TryGetValue(key, out SpellDeliveryEffectAnchor2D
                    registered) && registered == anchor)
            {
                anchors.Remove(key);
            }
        }

        public static void ClearAllAnchors()
        {
            SpellDeliveryEffectAnchor2D[] active =
                UnityEngine.Object.FindObjectsOfType<
                    SpellDeliveryEffectAnchor2D>(true);
            anchors.Clear();
            consumedKeys.Clear();
            consumedOrder.Clear();
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i] == null)
                    continue;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(active[i].gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(active[i].gameObject);
            }
        }

        private static int Activate(
            in SpellExecutionContext context,
            IReadOnlyList<SpellEffectSlot> slots,
            in SpellEventOccurrence occurrence,
            string groupId,
            bool honorSlotTrigger)
        {
            if (context.SuppressGameplayEffects || context.Spell == null ||
                slots == null)
            {
                return 0;
            }

            PruneDestroyed();
            int createdCount = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                SpellEffectSlot slot = slots[i];
                if (slot?.Effect == null ||
                    slot.DeliveryBinding !=
                        SpellEffectDeliveryBinding.DeliveryAnchor ||
                    (honorSlotTrigger &&
                     slot.AnchorTrigger != occurrence.Type))
                {
                    continue;
                }

                SpellDeliveryGeometry geometry = ResolveGeometry(
                        occurrence)
                    .WithSizeOverride(slot.AnchorSizeOverride);
                int runtimeId = ResolveRuntimeId(occurrence, geometry);
                string key = BuildKey(
                    context,
                    groupId,
                    i,
                    runtimeId,
                    occurrence,
                    slot.AnchorMultiplicity);
                if (!string.IsNullOrEmpty(key) &&
                    (consumedKeys.Contains(key) ||
                     (anchors.TryGetValue(
                          key,
                          out SpellDeliveryEffectAnchor2D existing) &&
                      existing != null)))
                {
                    continue;
                }

                var instance = new GameObject(
                    $"{context.Spell.DisplayName} {slot.Effect.DisplayName} " +
                    "Effect Anchor");
                SpellDeliveryEffectAnchor2D anchor =
                    instance.AddComponent<SpellDeliveryEffectAnchor2D>();
                if (!string.IsNullOrEmpty(key))
                {
                    anchors[key] = anchor;
                    RememberConsumedKey(key);
                }
                anchor.Initialize(
                    context,
                    slot,
                    i,
                    occurrence,
                    geometry,
                    key);
                createdCount++;
            }
            return createdCount;
        }

        private static void RememberConsumedKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !consumedKeys.Add(key))
                return;

            consumedOrder.Enqueue(key);
            while (consumedOrder.Count > MaximumRememberedKeys)
            {
                string oldest = consumedOrder.Dequeue();
                consumedKeys.Remove(oldest);
            }
        }

        private static SpellDeliveryGeometry ResolveGeometry(
            in SpellEventOccurrence occurrence)
        {
            if (occurrence.HasGeometry)
                return occurrence.Geometry;
            if (occurrence.DeliveryRuntime is
                    ISpellDeliveryGeometryProvider provider &&
                provider.TryGetDeliveryGeometry(
                    out SpellDeliveryGeometry provided))
            {
                return provided;
            }
            return SpellDeliveryGeometry.Point(occurrence.Point);
        }

        private static int ResolveRuntimeId(
            in SpellEventOccurrence occurrence,
            in SpellDeliveryGeometry geometry)
        {
            if (occurrence.DeliveryRuntime != null)
                return occurrence.DeliveryRuntime.GetInstanceID();
            if (geometry.FollowTransform != null)
                return geometry.FollowTransform.GetInstanceID();
            return 0;
        }

        private static string BuildKey(
            in SpellExecutionContext context,
            string groupId,
            int slotIndex,
            int runtimeId,
            in SpellEventOccurrence occurrence,
            SpellEffectAnchorMultiplicity multiplicity)
        {
            if (multiplicity ==
                SpellEffectAnchorMultiplicity.PerEventOccurrence)
            {
                return string.Empty;
            }

            int spellId = context.Spell != null
                ? context.Spell.GetInstanceID()
                : 0;
            int casterId = context.Cast.Caster != null
                ? context.Cast.Caster.GetInstanceID()
                : 0;
            int resolvedRuntimeId = multiplicity ==
                SpellEffectAnchorMultiplicity.OncePerRootCast
                    ? 0
                    : runtimeId;
            return $"{spellId}:{context.Cast.RootCastId}:{casterId}:" +
                   $"{groupId}:{slotIndex}:{resolvedRuntimeId}:" +
                   $"{(int)occurrence.Type}";
        }

        private static void PruneDestroyed()
        {
            removeKeys.Clear();
            foreach (KeyValuePair<string, SpellDeliveryEffectAnchor2D> pair
                     in anchors)
            {
                if (pair.Value == null)
                    removeKeys.Add(pair.Key);
            }
            for (int i = 0; i < removeKeys.Count; i++)
                anchors.Remove(removeKeys[i]);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            anchors.Clear();
            consumedKeys.Clear();
            consumedOrder.Clear();
            removeKeys.Clear();
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpellDeliveryEffectAnchor2D : MonoBehaviour,
        ISpellDeliveryGeometryProvider,
        ISpellDeliveryRadiusProvider
    {
        private sealed class Occupant
        {
            public int Id;
            public GameObject Target;
            public GameObject DetectedObject;
            public bool PresenceApplied;
        }

        private SpellExecutionContext context;
        private SpellEffectSlot slot;
        private int slotIndex;
        private SpellEventOccurrence occurrence;
        private SpellDeliveryGeometry geometryTemplate;
        private SpellDeliveryGeometry geometrySnapshot;
        private string registryKey;
        private float remaining;
        private float untilNextPulse;
        private bool initialized;
        private bool cleanedUp;
        private Collider2D[] overlapBuffer;
        private readonly Dictionary<int, Occupant> occupants =
            new Dictionary<int, Occupant>();
        private readonly HashSet<int> currentIds = new HashSet<int>();
        private readonly HashSet<int> everApplied = new HashSet<int>();
        private readonly List<int> exitIds = new List<int>();

        public bool IsComplete { get; private set; }
        public float Remaining => remaining;
        public SpellDeliveryGeometry Geometry => geometrySnapshot;
        public float DeliveryRadius =>
            Mathf.Max(0.01f, geometrySnapshot.CharacteristicSize);

        public void Initialize(
            in SpellExecutionContext executionContext,
            SpellEffectSlot effectSlot,
            int effectSlotIndex,
            in SpellEventOccurrence sourceOccurrence,
            in SpellDeliveryGeometry geometry,
            string key)
        {
            context = executionContext;
            slot = effectSlot;
            slotIndex = effectSlotIndex;
            occurrence = sourceOccurrence;
            geometryTemplate = geometry;
            geometrySnapshot = geometry.FollowTransform != null
                ? geometry.Snapshot()
                : geometry;
            registryKey = key;
            remaining = slot.AnchorDuration;
            untilNextPulse = 0f;
            overlapBuffer = new Collider2D[64];
            transform.position = geometrySnapshot.BoundingCenter;
            initialized = true;

            if (slot.AnchorApplication ==
                SpellEffectAnchorApplication.OnceAtAnchor)
            {
                ApplyAtWorldPoint();
                return;
            }

            RefreshOccupants(applyNewEntries: true);
            if (slot.AnchorApplication ==
                SpellEffectAnchorApplication.Periodic)
            {
                ApplyPeriodic();
                untilNextPulse = slot.AnchorInterval;
            }
        }

        public bool TryGetDeliveryGeometry(
            out SpellDeliveryGeometry geometry)
        {
            geometry = geometrySnapshot;
            return initialized && !IsComplete;
        }

        private void Update()
        {
            if (!initialized || IsComplete)
                return;
            float delta = context.Spell != null &&
                          context.Spell.Timing.TimeMode ==
                          SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            Step(Mathf.Max(0f, delta));
        }

        internal void Step(float deltaTime)
        {
            if (!initialized || IsComplete)
                return;

            remaining -= Mathf.Max(0f, deltaTime);
            if (remaining <= 0f)
            {
                Complete();
                return;
            }

            RefreshGeometry();
            if (slot.AnchorApplication ==
                SpellEffectAnchorApplication.OnceAtAnchor)
            {
                return;
            }

            RefreshOccupants(applyNewEntries: true);
            if (slot.AnchorApplication !=
                SpellEffectAnchorApplication.Periodic)
            {
                return;
            }

            untilNextPulse -= Mathf.Max(0f, deltaTime);
            while (untilNextPulse <= 0f && !IsComplete)
            {
                ApplyPeriodic();
                untilNextPulse += slot.AnchorInterval;
            }
        }

        private void RefreshGeometry()
        {
            if (geometryTemplate.FollowTransform != null)
                geometrySnapshot = geometryTemplate.Snapshot();
            transform.position = geometrySnapshot.BoundingCenter;
        }

        private void RefreshOccupants(bool applyNewEntries)
        {
            currentIds.Clear();
            PreserveAdmittedPresenceOccupants();
            float searchRadius = Mathf.Max(
                0.01f,
                geometrySnapshot.BoundingRadius);
            var filter = new ContactFilter2D();
            filter.useTriggers = true;
            filter.SetLayerMask(
                context.Spell.TargetFilter.UsesLayerMask
                    ? context.Spell.TargetFilter.AllowedLayers
                    : (LayerMask)(~0));
            int count;
            do
            {
                count = Physics2D.OverlapCircle(
                    geometrySnapshot.BoundingCenter,
                    searchRadius,
                    filter,
                    overlapBuffer);
                if (count < overlapBuffer.Length ||
                    overlapBuffer.Length >= 1024)
                {
                    break;
                }
                Array.Resize(
                    ref overlapBuffer,
                    Mathf.Min(1024, overlapBuffer.Length * 2));
            }
            while (true);
            for (int i = 0; i < count; i++)
            {
                Collider2D detected = overlapBuffer[i];
                if (detected == null ||
                    !geometrySnapshot.Contains(detected) ||
                    !SpellTargetResolver.TryResolveValidTarget(
                        context,
                        detected.gameObject,
                        out GameObject target))
                {
                    continue;
                }

                int id = SpellTargetResolver.GetTargetId(target);
                if (id == 0 || !currentIds.Add(id))
                    continue;

                if (occupants.TryGetValue(id, out Occupant existing))
                {
                    existing.Target = target;
                    existing.DetectedObject = detected.gameObject;
                    continue;
                }

                var occupant = new Occupant
                {
                    Id = id,
                    Target = target,
                    DetectedObject = detected.gameObject
                };
                occupants.Add(id, occupant);
                if (applyNewEntries &&
                    (slot.ReapplyAfterExit || everApplied.Add(id)))
                {
                    ApplyOnEnter(occupant, detected);
                }
            }

            exitIds.Clear();
            foreach (KeyValuePair<int, Occupant> pair in occupants)
            {
                if (!currentIds.Contains(pair.Key))
                    exitIds.Add(pair.Key);
            }
            for (int i = 0; i < exitIds.Count; i++)
            {
                int id = exitIds[i];
                Occupant occupant = occupants[id];
                RemovePresence(occupant);
                occupants.Remove(id);
            }
        }

        /// <summary>
        /// Target filters decide whether an object may enter an anchor. Once
        /// an exact While Present effect owns state on that object, its exit
        /// must be decided by physical geometry rather than mutable gameplay
        /// properties. Effects such as projectile allegiance conversion may
        /// legitimately change team or layer while Spatial Force, Stat
        /// Modifier, or Movement Speed still owns presence on the target.
        /// </summary>
        private void PreserveAdmittedPresenceOccupants()
        {
            if (slot.AnchorApplication !=
                SpellEffectAnchorApplication.WhilePresent)
            {
                return;
            }

            foreach (KeyValuePair<int, Occupant> pair in occupants)
            {
                Occupant occupant = pair.Value;
                if (!occupant.PresenceApplied ||
                    !IsPhysicallyInside(occupant))
                {
                    continue;
                }

                currentIds.Add(pair.Key);
            }
        }

        private bool IsPhysicallyInside(Occupant occupant)
        {
            if (occupant == null || occupant.Target == null)
                return false;

            Collider2D detected = occupant.DetectedObject != null
                ? occupant.DetectedObject.GetComponent<Collider2D>()
                : null;
            if (IsUsableInsideCollider(detected))
                return true;

            Collider2D[] colliders =
                occupant.Target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (IsUsableInsideCollider(colliders[i]))
                    return true;
            }

            return false;
        }

        private bool IsUsableInsideCollider(Collider2D collider)
        {
            return collider != null &&
                   collider.enabled &&
                   collider.gameObject.activeInHierarchy &&
                   geometrySnapshot.Contains(collider);
        }

        private void ApplyOnEnter(Occupant occupant, Collider2D detected)
        {
            if (slot.AnchorApplication ==
                    SpellEffectAnchorApplication.WhilePresent &&
                slot.Effect is IAreaPresenceEffectDefinition presence)
            {
                SpellEffectContext effectContext = BuildContext(
                    occupant.Target,
                    detected);
                try
                {
                    occupant.PresenceApplied = presence.ApplyPresence(
                        effectContext,
                        this,
                        slot.Settings);
                    Report(
                        occupant.Target,
                        occupant.PresenceApplied
                            ? SpellEffectSlotStatus.Applied
                            : SpellEffectSlotStatus.Rejected,
                        occupant.PresenceApplied
                            ? "The anchored presence effect applied."
                            : slot.Effect.DescribeApplicationFailure(
                                effectContext,
                                slot.Settings));
                }
                catch (Exception exception)
                {
                    ReportException(occupant.Target, exception);
                }
                return;
            }

            if (slot.AnchorApplication ==
                SpellEffectAnchorApplication.Periodic)
            {
                return;
            }

            ApplyEffect(occupant.Target, detected);
        }

        private void ApplyPeriodic()
        {
            foreach (Occupant occupant in occupants.Values)
            {
                Collider2D detected = occupant.DetectedObject != null
                    ? occupant.DetectedObject.GetComponent<Collider2D>()
                    : null;
                ApplyEffect(occupant.Target, detected);
            }
        }

        private void ApplyEffect(GameObject target, Collider2D detected)
        {
            SpellEffectContext effectContext = BuildContext(target, detected);
            try
            {
                bool applied = slot.Effect.Apply(
                    effectContext,
                    slot.Settings);
                Report(
                    target,
                    applied
                        ? SpellEffectSlotStatus.Applied
                        : SpellEffectSlotStatus.Rejected,
                    applied
                        ? "The delivery-anchor effect applied."
                        : slot.Effect.DescribeApplicationFailure(
                            effectContext,
                            slot.Settings));
            }
            catch (Exception exception)
            {
                ReportException(target, exception);
            }
        }

        private void ApplyAtWorldPoint()
        {
            if (!slot.Effect.CanApplyWithoutRecipient(slot.Settings))
            {
                Report(
                    null,
                    SpellEffectSlotStatus.Rejected,
                    "Once At Anchor requires an effect that supports a " +
                    "world point without an object recipient.");
                return;
            }

            var effectContext = new SpellEffectContext(
                context.Spell,
                context.Cast,
                null,
                geometrySnapshot.BoundingCenter,
                occurrence.Normal,
                1f,
                occurrence.Type,
                occurrence.Subject,
                this);
            try
            {
                bool applied = slot.Effect.Apply(
                    effectContext,
                    slot.Settings);
                Report(
                    null,
                    applied
                        ? SpellEffectSlotStatus.Applied
                        : SpellEffectSlotStatus.Rejected,
                    applied
                        ? "The effect applied once at the delivery anchor."
                        : slot.Effect.DescribeApplicationFailure(
                            effectContext,
                            slot.Settings));
            }
            catch (Exception exception)
            {
                ReportException(null, exception);
            }
        }

        private SpellEffectContext BuildContext(
            GameObject target,
            Collider2D detected)
        {
            Vector2 hitPoint = detected != null
                ? geometrySnapshot.ResolveHitPoint(detected)
                : target != null
                    ? (Vector2)target.transform.position
                    : geometrySnapshot.BoundingCenter;
            return new SpellEffectContext(
                context.Spell,
                context.Cast,
                target,
                hitPoint,
                geometrySnapshot.ResolveHitNormal(target),
                1f,
                occurrence.Type,
                occurrence.Subject,
                this);
        }

        private void RemovePresence(Occupant occupant)
        {
            if (!occupant.PresenceApplied ||
                !(slot.Effect is IAreaPresenceEffectDefinition presence) ||
                occupant.Target == null)
            {
                return;
            }

            try
            {
                presence.RemovePresence(
                    occupant.Target,
                    this,
                    slot.Settings);
            }
            catch (Exception exception)
            {
                ReportException(occupant.Target, exception);
            }
            occupant.PresenceApplied = false;
        }

        private void Complete()
        {
            if (IsComplete)
                return;
            IsComplete = true;
            Cleanup();
            if (Application.isPlaying)
                Destroy(gameObject);
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (cleanedUp)
                return;
            cleanedUp = true;
            foreach (Occupant occupant in occupants.Values)
                RemovePresence(occupant);
            occupants.Clear();
            SpellDeliveryEffectAnchorService.Unregister(registryKey, this);
        }

        private void Report(
            GameObject target,
            SpellEffectSlotStatus status,
            string message)
        {
            SpellRuntimeDiagnostics.ReportEffectSlot(
                new SpellEffectSlotDiagnostic(
                    context.Spell,
                    slot.Effect,
                    target,
                    slotIndex,
                    status,
                    message));
        }

        private void ReportException(
            GameObject target,
            Exception exception)
        {
            Report(
                target,
                SpellEffectSlotStatus.Exception,
                "The delivery-anchor effect threw an exception.");
            Debug.LogException(exception, slot.Effect);
        }
    }
}
