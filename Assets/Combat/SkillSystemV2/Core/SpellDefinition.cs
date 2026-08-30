using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Spell_New",
        menuName = "Project Eri/Skill System V2/Spell Definition")]
    public sealed class SpellDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The name players and designers see in menus and Inspector summaries.")]
        [SerializeField]
        private string displayName = "New Spell";

        [Tooltip("A permanent unique name used by saves, progression, AI, and other systems. Generate it once, then avoid changing it.")]
        [SerializeField]
        private string stableId;

        [Tooltip("A short plain-English explanation of what the spell does.")]
        [SerializeField, TextArea(2, 5)]
        private string description;

        [Tooltip("The picture shown for this spell in menus and other UI.")]
        [SerializeField]
        private Sprite icon;

        [Tooltip("A simple organizational label such as Attack, Movement, Support, or Enemy Skill. Reactions and AI can also filter by it.")]
        [SerializeField]
        private string category = "Skill";

        [Header("Timing")]
        [Tooltip("Controls how long each part of casting lasts and whether those timers follow normal or unscaled time.")]
        [SerializeField]
        private SpellTiming timing;

        [Tooltip("Seconds after casting before this spell can be used again.")]
        [SerializeField, Min(0f)]
        private float cooldown;

        [Header("Resource Cost")]
        [Tooltip("What resource the caster spends and how much is required. Player spells normally use AP.")]
        [SerializeField]
        private SpellResourceCost resourceCost;

        [Header("Target Rules")]
        [Tooltip("The spell's normal rules for deciding which objects its Default Effects are allowed to affect.")]
        [SerializeField]
        private TargetFilter targetFilter = new TargetFilter(
            TargetRelationship.Enemies,
            requireTarget: false);

        [Tooltip("Per-spell limits for where target points may be placed. These override neither the delivery nor the targeting mode; they add stricter range and line-of-sight rules for this spell.")]
        [SerializeField]
        private SpellPlacementRules placementRules =
            new SpellPlacementRules();

        [Tooltip("Designer-authored hints that tell enemy AI when this spell is useful and how opponents may react to it. These do not change player spell behavior.")]
        [SerializeField]
        private SpellAIAffordance aiAffordance =
            new SpellAIAffordance();

        [Header("Composition")]
        [SerializeField, HideInInspector]
        private DeliveryDefinition delivery;

        [Tooltip("The reusable delivery module and this spell's independent copy of its settings.")]
        [SerializeField]
        private SpellDeliverySlot deliverySlot = new SpellDeliverySlot();

        [SerializeField, HideInInspector]
        private bool deliverySlotMigrated;

        [SerializeField, HideInInspector]
        private List<EffectDefinition> effects = new List<EffectDefinition>();

        [Tooltip("Effects applied at the delivery's normal effect moment.")]
        [SerializeField]
        private List<SpellEffectSlot> effectSlots =
            new List<SpellEffectSlot>();

        [Tooltip("Extra effect instructions that run at specific moments reported by this spell's own delivery.")]
        [SerializeField]
        private List<SpellEventEffectRoute> eventEffectRoutes =
            new List<SpellEventEffectRoute>();

        [Tooltip("Named sets of Lingering Area effects that Reactions can enable or disable.")]
        [SerializeField]
        private List<SpellReactiveEffectGroup> reactiveEffectGroups =
            new List<SpellReactiveEffectGroup>();

        [SerializeField, HideInInspector]
        private bool effectSlotsMigrated;

        [Tooltip("Responses to other V2 deliveries touching this spell's persistent delivery.")]
        [SerializeField]
        private List<SpellReactionSlot> reactionSlots =
            new List<SpellReactionSlot>();

        [NonSerialized]
        private List<EffectDefinition> effectView;

        [Header("Chain Safety")]
        [Tooltip("How many Trigger Secondary Spell steps may be nested below the original cast. This prevents accidental infinite chains.")]
        [SerializeField, Min(0)]
        private int maximumChainDepth = 3;

        [Tooltip("Maximum total spell activations allowed within one cast chain. This prevents loops and runaway combinations.")]
        [SerializeField, Min(1)]
        private int maximumRootActivations = 32;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();

        public string StableId => stableId;
        public string Description => description;
        public Sprite Icon => icon;
        public string Category => category;
        public SpellTiming Timing => timing;
        public float Cooldown => Mathf.Max(0f, cooldown);
        public SpellResourceCost ResourceCost => resourceCost;
        public TargetFilter TargetFilter => targetFilter;
        public SpellPlacementRules PlacementRules =>
            placementRules ??= new SpellPlacementRules();
        public SpellAIAffordance AIAffordance =>
            aiAffordance ??= new SpellAIAffordance();
        public SpellDeliverySlot DeliverySlot
        {
            get
            {
                EnsureDeliverySlot();
                return deliverySlot;
            }
        }
        public DeliveryDefinition Delivery => DeliverySlot.Delivery;
        public SpellDeliverySettings DeliverySettings => DeliverySlot.Settings;
        public PlayerTargetingDefinition PlayerTargeting =>
            DeliverySlot.PlayerTargeting;
        public IReadOnlyList<SpellEffectSlot> EffectSlots
        {
            get
            {
                EnsureEffectSlots();
                return effectSlots;
            }
        }
        public IReadOnlyList<SpellReactionSlot> ReactionSlots
        {
            get
            {
                reactionSlots ??= new List<SpellReactionSlot>();
                return reactionSlots;
            }
        }
        public IReadOnlyList<SpellEventEffectRoute> EventEffectRoutes
        {
            get
            {
                EnsureEventEffectRoutes();
                return eventEffectRoutes;
            }
        }
        public IReadOnlyList<SpellReactiveEffectGroup> ReactiveEffectGroups
        {
            get
            {
                EnsureReactiveEffectGroups();
                return reactiveEffectGroups;
            }
        }

        /// <summary>
        /// Compatibility view for older integrations. New code should use
        /// EffectSlots so it can access per-spell settings.
        /// </summary>
        public IReadOnlyList<EffectDefinition> Effects
        {
            get
            {
                EnsureEffectSlots();
                effectView ??= new List<EffectDefinition>();
                effectView.Clear();
                for (int i = 0; i < effectSlots.Count; i++)
                    effectView.Add(effectSlots[i]?.Effect);
                return effectView;
            }
        }
        public int MaximumChainDepth => Mathf.Max(0, maximumChainDepth);
        public int MaximumRootActivations =>
            Mathf.Max(1, maximumRootActivations);

        internal string CooldownKey => string.IsNullOrWhiteSpace(stableId)
            ? $"instance:{GetInstanceID()}"
            : stableId.Trim();

        public CastChainBudget CreateChainBudget()
        {
            return new CastChainBudget(
                MaximumChainDepth,
                MaximumRootActivations);
        }

        public bool TryGetSupplementalTargetingDelivery(
            out SpellDeliverySlot supplementalDelivery)
        {
            EnsureEffectSlots();
            if (TryFindSupplementalTargeting(
                    effectSlots,
                    out supplementalDelivery))
            {
                return true;
            }

            EnsureEventEffectRoutes();
            for (int i = 0; i < eventEffectRoutes.Count; i++)
            {
                SpellEventEffectRoute route = eventEffectRoutes[i];
                if (route != null && route.Enabled &&
                    TryFindSupplementalTargeting(
                        route.EffectSlots,
                        out supplementalDelivery))
                {
                    return true;
                }
            }

            EnsureReactiveEffectGroups();
            for (int i = 0; i < reactiveEffectGroups.Count; i++)
            {
                SpellReactiveEffectGroup group = reactiveEffectGroups[i];
                if (group != null && TryFindSupplementalTargeting(
                        group.EffectSlots,
                        out supplementalDelivery))
                {
                    return true;
                }
            }

            supplementalDelivery = null;
            return false;
        }

        private static bool TryFindSupplementalTargeting(
            IReadOnlyList<SpellEffectSlot> slots,
            out SpellDeliverySlot supplementalDelivery)
        {
            int count = slots?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                SpellEffectSlot slot = slots[i];
                if (!(slot?.Effect is
                        ISpellSupplementalTargetingEffectDefinition provider))
                {
                    continue;
                }

                SpellDeliverySlot candidate =
                    provider.ResolveSupplementalTargetingDelivery(
                        slot.Settings);
                if (candidate?.Delivery == null)
                    continue;

                supplementalDelivery = candidate;
                return true;
            }

            supplementalDelivery = null;
            return false;
        }

        [ContextMenu("Regenerate Stable ID")]
        public void RegenerateStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }

        public bool ValidateContext(
            in CastContext context,
            out string rejectionReason)
        {
            return TryResolveContext(
                context,
                out _,
                out rejectionReason);
        }

        public bool TryResolveContext(
            in CastContext context,
            out CastContext resolvedContext,
            out string rejectionReason)
        {
            resolvedContext = context;
            if (context.Caster == null)
            {
                rejectionReason = "CastContext has no caster.";
                return false;
            }

            EnsureDeliverySlot();
            if (Delivery == null)
            {
                rejectionReason = "Spell has no delivery definition.";
                return false;
            }

            if (!Delivery.ValidateContext(
                    resolvedContext,
                    DeliverySettings,
                    out rejectionReason))
            {
                return false;
            }

            if (!PlacementRules.Validate(
                    resolvedContext,
                    out rejectionReason))
            {
                return false;
            }

            if ((Delivery.TargetingRequirement &
                 CastTargetingRequirement.SelectedTarget) != 0)
            {
                GameObject selected = SpellTargetResolver.Resolve(
                    resolvedContext.SelectedTarget);
                if (selected == null ||
                    !TargetFilter.IsValid(
                        resolvedContext,
                        selected,
                        resolvedContext.SelectedTarget,
                        out rejectionReason))
                {
                    if (string.IsNullOrWhiteSpace(rejectionReason))
                    {
                        rejectionReason =
                            "Selected target is not valid for this spell.";
                    }
                    return false;
                }
            }

            EnsureEffectSlots();
            for (int i = 0; i < effectSlots.Count; i++)
            {
                SpellEffectSlot slot = effectSlots[i];
                if (!(slot?.Effect is
                        ISpellCastContextModifierEffectDefinition modifier))
                {
                    continue;
                }

                if (!modifier.TryModifyCastContext(
                        resolvedContext,
                        slot.Settings,
                        out CastContext modified,
                        out rejectionReason))
                {
                    resolvedContext = modified;
                    return false;
                }

                resolvedContext = modified;
            }

            rejectionReason = string.Empty;
            return true;
        }

        public void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            if (string.IsNullOrWhiteSpace(displayName))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "Display name is empty."));
            }

            if (string.IsNullOrWhiteSpace(stableId))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Stable ID is required for reliable cooldowns and migration."));
            }

            if (PlacementRules.RequireLineOfSight &&
                PlacementRules.LineOfSightMask.value == 0)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "Placement requires line of sight, but its blocking layer mask is Nothing. Add the Obstacles layer or disable the toggle."));
            }

            if (AIAffordance.UsableByAI)
            {
                if (AIAffordance.Intents == SpellAIIntent.None)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        "Enemy AI use is enabled, but no AI Intent explains when this spell is useful."));
                }
                if (AIAffordance.PreferredMaximumRange > 0f &&
                    AIAffordance.PreferredMaximumRange <
                    AIAffordance.PreferredMinimumRange)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Error,
                        "Enemy AI preferred maximum range is smaller than its preferred minimum range."));
                }
                if (AIAffordance.RequireActiveComboToCast &&
                    AIAffordance.ConsumesComboTags.Count == 0)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        "Require Active Combo To Cast is enabled, but this spell consumes no combo tags."));
                }
                if (AIAffordance.ProducesComboTags.Count > 0 &&
                    AIAffordance.ComboTagActivationEvent ==
                    SpellEventType.None)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Error,
                        "This spell produces combo tags, but its Combo Tag Activation Event is None."));
                }
                if (AIAffordance.RequireSquadConsumerForSetup &&
                    AIAffordance.ProducesComboTags.Count == 0)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        "Require Squad Consumer For Setup is enabled, but this spell produces no combo tags."));
                }
            }

            EnsureDeliverySlot();
            if (Delivery == null)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Assign a delivery definition."));
            }
            else
            {
                Delivery.CollectValidationIssues(
                    issues,
                    DeliverySettings);
            }

            EnsureEffectSlots();

            EnsureEventEffectRoutes();
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            var routesById = new Dictionary<string, SpellEventEffectRoute>(
                StringComparer.Ordinal);
            for (int routeIndex = 0;
                 routeIndex < eventEffectRoutes.Count;
                 routeIndex++)
            {
                SpellEventEffectRoute route = eventEffectRoutes[routeIndex];
                if (route == null)
                    continue;

                if (!routeIds.Add(route.StableId))
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Error,
                        $"Event Effect Recipe '{route.DisplayName}' has a duplicate stable ID."));
                }
                else
                {
                    routesById.Add(route.StableId, route);
                }

                IReadOnlyList<SpellEffectSlot> routeEffects =
                    route.EffectSlots;
                if (routeEffects.Count == 0)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        $"Event Effect Recipe '{route.DisplayName}' has no effects."));
                }

                for (int effectIndex = 0;
                     effectIndex < routeEffects.Count;
                     effectIndex++)
                {
                    SpellEffectSlot slot = routeEffects[effectIndex];
                    if (slot?.Effect == null)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Event Effect Recipe '{route.DisplayName}' has an empty effect slot."));
                        continue;
                    }

                    slot.Effect.CollectValidationIssues(
                        issues,
                        slot.Settings);

                    CollectEffectAnchorValidationIssues(
                        issues,
                        slot,
                        $"Event Effect Recipe '{route.DisplayName}'",
                        route.Trigger,
                        validateDeliveryEvent: false);

                    if (slot.DeliveryBinding ==
                            SpellEffectDeliveryBinding.DeliveredTargets &&
                        slot.Effect is IAreaPresenceEffectDefinition)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Event Effect Recipe '{route.DisplayName}' contains a presence effect. Put effects that must remain while inside an area in Default Effects or a Reactive Effect Group instead."));
                    }

                    if (slot.Settings is
                            CasterMovementEffectSettings movementSettings &&
                        movementSettings.DestinationSource ==
                            CasterMovementDestinationSource
                                .DeliveryEventPoint &&
                        slot.DeliveryBinding ==
                            SpellEffectDeliveryBinding.DeliveredTargets &&
                        route.Recipient != SpellEventRecipient.Caster)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Event Effect Recipe '{route.DisplayName}' uses Caster Movement from the delivery event point, but its recipient is not The Spell Caster."));
                    }

                    if (route.Recipient ==
                            SpellEventRecipient.WorldPoint &&
                        slot.DeliveryBinding ==
                            SpellEffectDeliveryBinding.DeliveredTargets &&
                        !slot.Effect.CanApplyWithoutRecipient(slot.Settings))
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Event Effect Recipe '{route.DisplayName}' applies to a World Point, but effect '{slot.Effect.DisplayName}' requires an object recipient."));
                    }
                }

                if (route.Recipient ==
                        SpellEventRecipient.SelectedTarget &&
                    Delivery != null &&
                    (Delivery.TargetingRequirement &
                     CastTargetingRequirement.SelectedTarget) == 0)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        $"Event Effect Recipe '{route.DisplayName}' applies to Selected Target, but this delivery does not require one."));
                }

                if (Delivery != null &&
                    !SpellEventSupport.DeliveryReports(
                        Delivery,
                        route.Trigger))
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        $"Event Effect Recipe '{route.DisplayName}' listens for an event that delivery '{Delivery.DisplayName}' does not report."));
                }
            }

            EnsureReactiveEffectGroups();
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            for (int groupIndex = 0;
                 groupIndex < reactiveEffectGroups.Count;
                 groupIndex++)
            {
                SpellReactiveEffectGroup group =
                    reactiveEffectGroups[groupIndex];
                if (group == null)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        $"Reactive Effect Group {groupIndex + 1} is empty."));
                    continue;
                }

                if (!groupIds.Add(group.StableId))
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Error,
                        $"Reactive Effect Group '{group.DisplayName}' has a duplicate stable ID."));
                }

                IReadOnlyList<SpellEffectSlot> groupEffects =
                    group.EffectSlots;
                if (groupEffects.Count == 0)
                {
                    issues.Add(new SpellValidationIssue(
                        SpellValidationSeverity.Warning,
                        $"Reactive Effect Group '{group.DisplayName}' has no effects."));
                }

                for (int effectIndex = 0;
                     effectIndex < groupEffects.Count;
                     effectIndex++)
                {
                    SpellEffectSlot slot = groupEffects[effectIndex];
                    if (slot?.Effect == null)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Reactive Effect Group '{group.DisplayName}' has an empty effect slot."));
                        continue;
                    }

                    slot.Effect.CollectValidationIssues(
                        issues,
                        slot.Settings);

                    if (slot.DeliveryBinding ==
                        SpellEffectDeliveryBinding.DeliveryAnchor)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Error,
                            $"Reactive Effect Group '{group.DisplayName}' " +
                            "uses Delivery Anchor. Reactive groups already " +
                            "inherit their area's exact presence lifetime; " +
                            "use Delivered Targets here."));
                    }
                }
            }

            reactionSlots ??= new List<SpellReactionSlot>();
            for (int reactionIndex = 0;
                 reactionIndex < reactionSlots.Count;
                 reactionIndex++)
            {
                SpellReactionSlot reaction = reactionSlots[reactionIndex];
                if (reaction == null)
                    continue;

                IReadOnlyList<DeliveryInteractionResponse> responses =
                    reaction.Responses;
                for (int responseIndex = 0;
                     responseIndex < responses.Count;
                     responseIndex++)
                {
                    DeliveryInteractionResponse response =
                        responses[responseIndex];
                    if (response is
                        SetReactiveEffectGroupActiveResponse groupAction)
                    {
                        if (string.IsNullOrWhiteSpace(groupAction.GroupId))
                        {
                            issues.Add(new SpellValidationIssue(
                                SpellValidationSeverity.Warning,
                                $"Reaction {reactionIndex + 1} has a Reactive Effect Group action with no group selected."));
                        }
                        else if (!groupIds.Contains(groupAction.GroupId))
                        {
                            issues.Add(new SpellValidationIssue(
                                SpellValidationSeverity.Error,
                                $"Reaction {reactionIndex + 1} references a Reactive Effect Group that no longer exists."));
                        }
                    }

                    if (!(response is
                            RunEventEffectRouteResponse routeAction))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(routeAction.RouteId))
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Reaction {reactionIndex + 1} has a Run Event Effect Recipe action with no recipe selected."));
                    }
                    else if (!routesById.TryGetValue(
                                 routeAction.RouteId,
                                 out SpellEventEffectRoute selectedRoute))
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Error,
                            $"Reaction {reactionIndex + 1} references an Event Effect Recipe that no longer exists."));
                    }
                    else if (selectedRoute.Trigger !=
                             SpellEventType.ManualReaction)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Reaction {reactionIndex + 1} manually runs recipe '{selectedRoute.DisplayName}', but that recipe's WHEN event is not Manual Reaction."));
                    }
                }
            }

            if (effectSlots.Count == 0)
            {
                bool hasAlternateEffects =
                    reactiveEffectGroups.Count > 0 ||
                    eventEffectRoutes.Count > 0;
                issues.Add(new SpellValidationIssue(
                    hasAlternateEffects
                        ? SpellValidationSeverity.Info
                        : SpellValidationSeverity.Warning,
                    hasAlternateEffects
                        ? "The spell has no Default Effects. Its Event Effect Recipes or Reactive Effect Groups can still apply effects."
                        : "The spell has no effects. This is valid for movement or presentation-only deliveries."));
            }
            else
            {
                for (int i = 0; i < effectSlots.Count; i++)
                {
                    SpellEffectSlot slot = effectSlots[i];
                    if (slot == null || slot.Effect == null)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Effect slot {i + 1} is empty."));
                    }
                    else
                    {
                        slot.Effect.CollectValidationIssues(
                            issues,
                            slot.Settings);
                        CollectEffectAnchorValidationIssues(
                            issues,
                            slot,
                            $"Effect slot {i + 1}",
                            slot.AnchorTrigger,
                            validateDeliveryEvent: true);
                        if (slot.Settings is
                                CasterMovementEffectSettings
                                    movementSettings &&
                            movementSettings.DestinationSource ==
                                CasterMovementDestinationSource
                                    .DeliveryEventPoint &&
                            slot.DeliveryBinding ==
                                SpellEffectDeliveryBinding.DeliveredTargets)
                        {
                            issues.Add(new SpellValidationIssue(
                                SpellValidationSeverity.Error,
                                "Caster Movement using Delivery Event Point must be placed inside an Event Effect Recipe, not Default Effects."));
                        }
                    }
                }
            }

            if (maximumRootActivations < 1)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Maximum root activations must be at least one."));
            }
        }

        private void CollectEffectAnchorValidationIssues(
            ICollection<SpellValidationIssue> issues,
            SpellEffectSlot slot,
            string ownerName,
            SpellEventType trigger,
            bool validateDeliveryEvent)
        {
            if (slot?.Effect == null ||
                slot.DeliveryBinding !=
                    SpellEffectDeliveryBinding.DeliveryAnchor)
            {
                return;
            }

            if (trigger == SpellEventType.None)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"{ownerName} uses Delivery Anchor but has no trigger " +
                    "event."));
            }
            else if (validateDeliveryEvent && Delivery != null &&
                     !SpellEventSupport.DeliveryReports(Delivery, trigger))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"{ownerName} creates an anchor on {trigger}, but " +
                    $"delivery '{Delivery.DisplayName}' does not report " +
                    "that event."));
            }

            if (slot.AnchorApplication ==
                    SpellEffectAnchorApplication.OnceAtAnchor &&
                !slot.Effect.CanApplyWithoutRecipient(slot.Settings))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"{ownerName} uses Once At Anchor, but effect " +
                    $"'{slot.Effect.DisplayName}' requires an object " +
                    "recipient."));
            }
            else if (slot.AnchorApplication ==
                         SpellEffectAnchorApplication.WhilePresent &&
                     !(slot.Effect is IAreaPresenceEffectDefinition))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Info,
                    $"{ownerName} uses While Present with " +
                    $"'{slot.Effect.DisplayName}'. That effect has no exact " +
                    "presence-removal contract, so it safely applies on " +
                    "entry instead."));
            }
        }

        private void OnValidate()
        {
            cooldown = Mathf.Max(0f, cooldown);
            maximumChainDepth = Mathf.Max(0, maximumChainDepth);
            maximumRootActivations = Mathf.Max(1, maximumRootActivations);

            if (effects == null)
                effects = new List<EffectDefinition>();

            EnsureEffectSlots();
            EnsureDeliverySlot();
            EnsureEventEffectRoutes();
            EnsureReactiveEffectGroups();
            reactionSlots ??= new List<SpellReactionSlot>();
        }

        public void ReplaceDelivery(SpellDeliverySlot slot)
        {
            deliverySlot = slot ?? new SpellDeliverySlot();
            delivery = null;
            deliverySlotMigrated = true;
            EnsureDeliverySlot();
        }

        public bool EnsureDeliverySlot()
        {
            bool changed = false;
            if (deliverySlot == null)
            {
                deliverySlot = new SpellDeliverySlot();
                changed = true;
            }

            if (!deliverySlotMigrated)
            {
                if (deliverySlot.Delivery == null && delivery != null)
                    deliverySlot = new SpellDeliverySlot(delivery);

                deliverySlotMigrated = true;
                changed = true;
            }

            changed |= deliverySlot.EnsureCompatibleSettings();
            return changed;
        }

        public void ReplaceEffectSlots(params SpellEffectSlot[] slots)
        {
            effectSlots = slots != null
                ? new List<SpellEffectSlot>(slots)
                : new List<SpellEffectSlot>();
            effects?.Clear();
            effectSlotsMigrated = true;
            EnsureEffectSlots();
        }

        public void ReplaceReactionSlots(params SpellReactionSlot[] slots)
        {
            reactionSlots = slots != null
                ? new List<SpellReactionSlot>(slots)
                : new List<SpellReactionSlot>();
        }

        public void ReplaceEventEffectRoutes(
            params SpellEventEffectRoute[] routes)
        {
            eventEffectRoutes = routes != null
                ? new List<SpellEventEffectRoute>(routes)
                : new List<SpellEventEffectRoute>();
            EnsureEventEffectRoutes();
        }

        public bool EnsureEventEffectRoutes()
        {
            bool changed = false;
            eventEffectRoutes ??= new List<SpellEventEffectRoute>();
            for (int i = 0; i < eventEffectRoutes.Count; i++)
            {
                if (eventEffectRoutes[i] == null)
                {
                    eventEffectRoutes[i] = new SpellEventEffectRoute();
                    changed = true;
                }

                changed |= eventEffectRoutes[i].EnsureValid();
            }

            return changed;
        }

        public void ReplaceReactiveEffectGroups(
            params SpellReactiveEffectGroup[] groups)
        {
            reactiveEffectGroups = groups != null
                ? new List<SpellReactiveEffectGroup>(groups)
                : new List<SpellReactiveEffectGroup>();
            EnsureReactiveEffectGroups();
        }

        public bool EnsureReactiveEffectGroups()
        {
            bool changed = false;
            reactiveEffectGroups ??= new List<SpellReactiveEffectGroup>();
            for (int i = 0; i < reactiveEffectGroups.Count; i++)
            {
                if (reactiveEffectGroups[i] == null)
                {
                    reactiveEffectGroups[i] =
                        new SpellReactiveEffectGroup();
                    changed = true;
                }

                changed |= reactiveEffectGroups[i].EnsureValid();
            }

            return changed;
        }

        public bool EnsureEffectSlots()
        {
            bool changed = false;
            effectSlots ??= new List<SpellEffectSlot>();
            effects ??= new List<EffectDefinition>();

            if (!effectSlotsMigrated)
            {
                if (effectSlots.Count == 0 && effects.Count > 0)
                {
                    for (int i = 0; i < effects.Count; i++)
                        effectSlots.Add(new SpellEffectSlot(effects[i]));
                }

                effectSlotsMigrated = true;
                changed = true;
            }

            for (int i = 0; i < effectSlots.Count; i++)
            {
                if (effectSlots[i] == null)
                {
                    effectSlots[i] = new SpellEffectSlot();
                    changed = true;
                }

                changed |= effectSlots[i].EnsureCompatibleSettings();
            }

            return changed;
        }
    }
}
