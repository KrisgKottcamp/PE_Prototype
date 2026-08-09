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
        [SerializeField]
        private DeliveryDefinition delivery;

        [SerializeField]
        private List<EffectDefinition> effects = new List<EffectDefinition>();

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
        public DeliveryDefinition Delivery => delivery;
        public IReadOnlyList<EffectDefinition> Effects =>
            effects ?? (IReadOnlyList<EffectDefinition>)Array.Empty<EffectDefinition>();
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

            if (delivery == null)
            {
                rejectionReason = "Spell has no delivery definition.";
                return false;
            }

            return delivery.ValidateContext(context, out rejectionReason);
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

            if (delivery == null)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Assign a delivery definition."));
            }
            else
            {
                delivery.CollectValidationIssues(issues);
            }

            if (effects == null || effects.Count == 0)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "The spell has no effects. This is valid for movement or presentation-only deliveries."));
            }
            else
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] == null)
                    {
                        issues.Add(new SpellValidationIssue(
                            SpellValidationSeverity.Warning,
                            $"Effect slot {i + 1} is empty."));
                    }
                    else
                    {
                        effects[i].CollectValidationIssues(issues);
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
        }
    }
}
