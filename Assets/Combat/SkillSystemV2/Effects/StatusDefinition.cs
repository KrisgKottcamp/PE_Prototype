using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum StatusStackingMode
    {
        RefreshDuration,
        AddStacksAndRefresh,
        ReplaceStacksAndRefresh,
        IgnoreWhileActive
    }

    public readonly struct StatusRuntimeContext
    {
        public StatusDefinition Definition { get; }
        public GameObject Target { get; }
        public SpellEffectContext SourceEffectContext { get; }
        public int Stacks { get; }
        public float RemainingDuration { get; }

        public StatusRuntimeContext(
            StatusDefinition definition,
            GameObject target,
            in SpellEffectContext sourceEffectContext,
            int stacks,
            float remainingDuration)
        {
            Definition = definition;
            Target = target;
            SourceEffectContext = sourceEffectContext;
            Stacks = Mathf.Max(1, stacks);
            RemainingDuration = Mathf.Max(0f, remainingDuration);
        }
    }

    [CreateAssetMenu(
        fileName = "Status_New",
        menuName = "Project Eri/Skill System V2/Effects/Status")]
    public sealed class StatusDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string displayName = "New Status";

        [SerializeField]
        private string stableId;

        [SerializeField]
        private Sprite icon;

        [Header("Lifetime")]
        [Tooltip("Zero creates a permanent status until explicitly removed.")]
        [SerializeField, Min(0f)]
        private float defaultDuration = 5f;

        [SerializeField, Min(1)]
        private int maximumStacks = 1;

        [SerializeField]
        private StatusStackingMode stackingMode =
            StatusStackingMode.RefreshDuration;

        [SerializeField]
        private SpellTimeMode timeMode = SpellTimeMode.Scaled;

        [Header("Composed Effects")]
        [SerializeField]
        private List<EffectDefinition> onAppliedEffects =
            new List<EffectDefinition>();

        [SerializeField, Min(0f)]
        private float periodicInterval;

        [SerializeField]
        private List<EffectDefinition> periodicEffects =
            new List<EffectDefinition>();

        [SerializeField]
        private List<EffectDefinition> onRemovedEffects =
            new List<EffectDefinition>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public string StableId => stableId;
        public Sprite Icon => icon;
        public float DefaultDuration => Mathf.Max(0f, defaultDuration);
        public int MaximumStacks => Mathf.Max(1, maximumStacks);
        public StatusStackingMode StackingMode => stackingMode;
        public SpellTimeMode TimeMode => timeMode;
        public float PeriodicInterval => Mathf.Max(0f, periodicInterval);
        public bool IsPermanent => DefaultDuration <= 0f;

        internal string RuntimeKey => string.IsNullOrWhiteSpace(stableId)
            ? $"instance:{GetInstanceID()}"
            : stableId.Trim();

        [ContextMenu("Regenerate Stable ID")]
        public void RegenerateStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }

        public void CollectValidationIssues(List<SpellValidationIssue> issues)
        {
            if (issues == null)
                return;

            if (string.IsNullOrWhiteSpace(stableId))
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Status '{DisplayName}' needs a stable ID."));
            }

            if (periodicEffects != null &&
                periodicEffects.Count > 0 &&
                PeriodicInterval <= 0f)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Status '{DisplayName}' has periodic effects but no interval."));
            }
        }

        internal void InvokeApplied(in StatusRuntimeContext context)
        {
            ApplyComposedEffects(onAppliedEffects, context, 1f);
        }

        internal void InvokePeriodic(in StatusRuntimeContext context)
        {
            ApplyComposedEffects(
                periodicEffects,
                context,
                Mathf.Max(1, context.Stacks));
        }

        internal void InvokeRemoved(in StatusRuntimeContext context)
        {
            ApplyComposedEffects(onRemovedEffects, context, 1f);
        }

        private static void ApplyComposedEffects(
            List<EffectDefinition> effects,
            in StatusRuntimeContext statusContext,
            float stackScale)
        {
            if (effects == null || statusContext.Target == null)
                return;

            SpellEffectContext source = statusContext.SourceEffectContext;
            var effectContext = new SpellEffectContext(
                source.Spell,
                source.Cast,
                statusContext.Target,
                statusContext.Target.transform.position,
                source.HitNormal,
                source.PotencyScale * Mathf.Max(0f, stackScale));

            for (int i = 0; i < effects.Count; i++)
            {
                EffectDefinition effect = effects[i];
                if (effect == null)
                    continue;

                try
                {
                    effect.Apply(effectContext);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, effect);
                }
            }
        }

        private void OnValidate()
        {
            defaultDuration = Mathf.Max(0f, defaultDuration);
            maximumStacks = Mathf.Max(1, maximumStacks);
            periodicInterval = Mathf.Max(0f, periodicInterval);
            onAppliedEffects ??= new List<EffectDefinition>();
            periodicEffects ??= new List<EffectDefinition>();
            onRemovedEffects ??= new List<EffectDefinition>();
        }
    }
}
