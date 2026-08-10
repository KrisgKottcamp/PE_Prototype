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
        [SerializeField]
        private string displayName = "New Spell";

        [SerializeField]
        private string stableId;

        [SerializeField, TextArea(2, 5)]
        private string description;

        [SerializeField]
        private Sprite icon;

        [SerializeField]
        private string category = "Skill";

        [Header("Timing")]
        [SerializeField]
        private SpellTiming timing;

        [SerializeField, Min(0f)]
        private float cooldown;

        [Header("Resource Cost")]
        [SerializeField]
        private SpellResourceCost resourceCost;

        [Header("Target Rules")]
        [SerializeField]
        private TargetFilter targetFilter = new TargetFilter(
            TargetRelationship.Enemies,
            requireTarget: false);

        [Header("Composition")]
        [SerializeField, HideInInspector]
        private DeliveryDefinition delivery;

        [SerializeField]
        private SpellDeliverySlot deliverySlot = new SpellDeliverySlot();

        [SerializeField, HideInInspector]
        private bool deliverySlotMigrated;

        [SerializeField, HideInInspector]
        private List<EffectDefinition> effects = new List<EffectDefinition>();

        [SerializeField]
        private List<SpellEffectSlot> effectSlots =
            new List<SpellEffectSlot>();

        [SerializeField, HideInInspector]
        private bool effectSlotsMigrated;

        [NonSerialized]
        private List<EffectDefinition> effectView;

        [Header("Chain Safety")]
        [SerializeField, Min(0)]
        private int maximumChainDepth = 3;

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

        [ContextMenu("Regenerate Stable ID")]
        public void RegenerateStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }

        public bool ValidateContext(
            in CastContext context,
            out string rejectionReason)
        {
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

            return Delivery.ValidateContext(
                context,
                DeliverySettings,
                out rejectionReason);
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

            if (effectSlots.Count == 0)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "The spell has no effects. This is valid for movement or presentation-only deliveries."));
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

        private void OnValidate()
        {
            cooldown = Mathf.Max(0f, cooldown);
            maximumChainDepth = Mathf.Max(0, maximumChainDepth);
            maximumRootActivations = Mathf.Max(1, maximumRootActivations);

            if (effects == null)
                effects = new List<EffectDefinition>();

            EnsureEffectSlots();
            EnsureDeliverySlot();
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
