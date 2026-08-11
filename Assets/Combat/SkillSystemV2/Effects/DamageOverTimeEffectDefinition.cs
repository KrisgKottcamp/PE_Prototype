using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum DamageOverTimeStackingPolicy
    {
        RefreshDuration,
        StackIndependent,
        ReplaceIfStronger
    }

    [Serializable]
    public sealed class DamageOverTimeEffectSettings : SpellEffectSettings
    {
        [Tooltip("Optional damage category used by resistances, reactions, and other systems.")]
        [SerializeField]
        private DamageTypeDefinition damageType;

        [Tooltip("Health removed on each damage tick.")]
        [SerializeField, Min(0f)]
        private float damagePerTick = 5f;

        [Tooltip("Seconds between damage ticks.")]
        [SerializeField, Min(0.02f)]
        private float tickInterval = 0.5f;

        [Tooltip("How many seconds the damage-over-time effect remains on the target.")]
        [SerializeField, Min(0.02f)]
        private float duration = 3f;

        [Tooltip("Deal the first tick immediately instead of waiting one Tick Interval.")]
        [SerializeField]
        private bool tickImmediately = true;

        [Tooltip("Deal each tick even while the target reports that it is invulnerable.")]
        [SerializeField]
        private bool ignoreInvulnerability;

        [Tooltip("What happens when this same effect is applied again: refresh its timer, create another independent copy, or keep only the stronger version.")]
        [SerializeField]
        private DamageOverTimeStackingPolicy stackingPolicy =
            DamageOverTimeStackingPolicy.RefreshDuration;

        public DamageTypeDefinition DamageType => damageType;
        public float DamagePerTick => Mathf.Max(0f, damagePerTick);
        public float TickInterval => Mathf.Max(0.02f, tickInterval);
        public float Duration => Mathf.Max(0.02f, duration);
        public bool TickImmediately => tickImmediately;
        public bool IgnoreInvulnerability => ignoreInvulnerability;
        public DamageOverTimeStackingPolicy StackingPolicy => stackingPolicy;

        public DamageOverTimeEffectSettings() { }

        public DamageOverTimeEffectSettings(
            float perTick,
            float interval,
            float effectDuration,
            DamageTypeDefinition type = null,
            bool immediateTick = true,
            bool shouldIgnoreInvulnerability = false,
            DamageOverTimeStackingPolicy stacking =
                DamageOverTimeStackingPolicy.RefreshDuration)
        {
            damagePerTick = Mathf.Max(0f, perTick);
            tickInterval = Mathf.Max(0.02f, interval);
            duration = Mathf.Max(0.02f, effectDuration);
            damageType = type;
            tickImmediately = immediateTick;
            ignoreInvulnerability = shouldIgnoreInvulnerability;
            stackingPolicy = stacking;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_DamageOverTime",
        menuName = "Project Eri/Skill System V2/Effects/Damage Over Time")]
    public sealed class DamageOverTimeEffectDefinition : EffectDefinition
    {
        [Tooltip("Default Damage Type copied into a spell when this effect is equipped.")]
        [SerializeField] private DamageTypeDefinition damageType;
        [Tooltip("Default damage dealt per tick.")]
        [SerializeField, Min(0f)] private float damagePerTick = 5f;
        [Tooltip("Default seconds between damage ticks.")]
        [SerializeField, Min(0.02f)] private float tickInterval = 0.5f;
        [Tooltip("Default lifetime of the effect on a target.")]
        [SerializeField, Min(0.02f)] private float duration = 3f;
        [Tooltip("Default choice for whether the first tick happens immediately.")]
        [SerializeField] private bool tickImmediately = true;
        [Tooltip("Default choice for whether ticks bypass invulnerability.")]
        [SerializeField] private bool ignoreInvulnerability;
        [Tooltip("Default behavior when the same damage-over-time effect is applied again.")]
        [SerializeField] private DamageOverTimeStackingPolicy stackingPolicy =
            DamageOverTimeStackingPolicy.RefreshDuration;

        public override Type SettingsType =>
            typeof(DamageOverTimeEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new DamageOverTimeEffectSettings(
                damagePerTick,
                tickInterval,
                duration,
                damageType,
                tickImmediately,
                ignoreInvulnerability,
                stackingPolicy);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            if (context.Target == null ||
                SpellEffectReceiverResolver.Find<ISpellDamageReceiver>(
                    context.Target) == null)
            {
                return false;
            }

            DamageOverTimeEffectSettings resolved =
                settings as DamageOverTimeEffectSettings ??
                (DamageOverTimeEffectSettings)CreateDefaultSettings();

            if (resolved.StackingPolicy !=
                DamageOverTimeStackingPolicy.StackIndependent)
            {
                SpellDamageOverTimeRuntime[] existing =
                    context.Target.GetComponentsInChildren<
                        SpellDamageOverTimeRuntime>(true);
                for (int i = 0; i < existing.Length; i++)
                {
                    SpellDamageOverTimeRuntime runtime = existing[i];
                    if (runtime != null &&
                        runtime.Matches(this, context.Cast.Caster))
                    {
                        runtime.Refresh(
                            context,
                            resolved,
                            replaceOnlyIfStronger:
                                resolved.StackingPolicy ==
                                DamageOverTimeStackingPolicy.ReplaceIfStronger);
                        return true;
                    }
                }
            }

            SpellDamageOverTimeRuntime created =
                context.Target.AddComponent<SpellDamageOverTimeRuntime>();
            created.Initialize(this, context, resolved);
            return true;
        }

        public override bool DescribesDamageType(
            SpellEffectSettings settings,
            DamageTypeDefinition queriedType)
        {
            DamageOverTimeEffectSettings resolved =
                settings as DamageOverTimeEffectSettings ??
                (DamageOverTimeEffectSettings)CreateDefaultSettings();
            return resolved.DamageType == queriedType;
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, CreateDefaultSettings());
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            DamageOverTimeEffectSettings resolved =
                settings as DamageOverTimeEffectSettings ??
                (DamageOverTimeEffectSettings)CreateDefaultSettings();
            if (resolved.DamagePerTick <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    $"Damage Over Time effect '{DisplayName}' has zero " +
                    "damage per tick."));
            }
        }

        private void OnValidate()
        {
            damagePerTick = Mathf.Max(0f, damagePerTick);
            tickInterval = Mathf.Max(0.02f, tickInterval);
            duration = Mathf.Max(0.02f, duration);
        }
    }

    public sealed class SpellDamageOverTimeRuntime : MonoBehaviour
    {
        private DamageOverTimeEffectDefinition sourceDefinition;
        private GameObject sourceCaster;
        private SpellEffectContext context;
        private DamageTypeDefinition damageType;
        private float damagePerTick;
        private float tickInterval;
        private float remaining;
        private float untilNextTick;
        private bool ignoreInvulnerability;
        private SpellTimeMode timeMode;
        private bool initialized;

        public float Remaining => remaining;
        public float DamagePerTick => damagePerTick;

        public bool Matches(
            DamageOverTimeEffectDefinition definition,
            GameObject caster)
        {
            return sourceDefinition == definition && sourceCaster == caster;
        }

        public void Initialize(
            DamageOverTimeEffectDefinition definition,
            in SpellEffectContext effectContext,
            DamageOverTimeEffectSettings settings)
        {
            sourceDefinition = definition;
            sourceCaster = effectContext.Cast.Caster;
            ApplySettings(effectContext, settings);
            untilNextTick = settings.TickImmediately
                ? 0f
                : tickInterval;
            initialized = true;

            if (settings.TickImmediately)
            {
                ApplyTick();
                untilNextTick = tickInterval;
            }
        }

        public void Refresh(
            in SpellEffectContext effectContext,
            DamageOverTimeEffectSettings settings,
            bool replaceOnlyIfStronger)
        {
            if (replaceOnlyIfStronger &&
                settings.DamagePerTick < damagePerTick)
            {
                remaining = Mathf.Max(remaining, settings.Duration);
                return;
            }

            ApplySettings(effectContext, settings);
        }

        public void TickRuntime(float deltaTime)
        {
            if (!initialized)
                return;

            float safeDelta = Mathf.Max(0f, deltaTime);
            remaining -= safeDelta;
            untilNextTick -= safeDelta;

            while (remaining > 0f && untilNextTick <= 0f)
            {
                ApplyTick();
                untilNextTick += tickInterval;
            }

            if (remaining <= 0f)
                Destroy(this);
        }

        private void Update()
        {
            float delta = timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            TickRuntime(delta);
        }

        private void ApplySettings(
            in SpellEffectContext effectContext,
            DamageOverTimeEffectSettings settings)
        {
            context = effectContext;
            damageType = settings.DamageType;
            damagePerTick = settings.DamagePerTick *
                            effectContext.PotencyScale;
            tickInterval = settings.TickInterval;
            remaining = settings.Duration;
            ignoreInvulnerability = settings.IgnoreInvulnerability;
            timeMode = effectContext.Spell != null
                ? effectContext.Spell.Timing.TimeMode
                : SpellTimeMode.Scaled;
        }

        private void ApplyTick()
        {
            if (context.Target == null)
                return;

            ISpellDamageReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellDamageReceiver>(
                    context.Target);
            if (receiver == null)
                return;

            var request = new SpellDamageRequest(
                context,
                damageType,
                damagePerTick,
                ignoreInvulnerability);
            receiver.TryReceiveDamage(request, out _);
        }
    }
}
