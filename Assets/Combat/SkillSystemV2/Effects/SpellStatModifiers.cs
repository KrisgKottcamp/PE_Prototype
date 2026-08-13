using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellActorStat
    {
        DamageDealt,
        DamageReceived,
        HealingDealt,
        HealingReceived,
        MovementSpeed,
        BasicAttackSpeed,
        SpellCastSpeed,
        ActionPointPickupValue,
        ActionPointCollectionRadius,
        ActionPointCost,
        KnockbackDealt,
        KnockbackReceived,
        SpellPlacementRange
    }

    public enum SpellStatOperation
    {
        Add,
        Multiply,
        Override
    }

    public enum SpellStatStackingPolicy
    {
        RefreshFromSameSource,
        Stack,
        KeepStrongest
    }

    [Serializable]
    public sealed class SpellStatModifierSettings : SpellEffectSettings
    {
        [Tooltip("The gameplay value changed by this effect.")]
        [SerializeField] private SpellActorStat stat =
            SpellActorStat.MovementSpeed;

        [Tooltip("Multiply scales the current value, Add adds to its multiplier, and Override replaces it. Multiply is recommended for most buffs and debuffs.")]
        [SerializeField] private SpellStatOperation operation =
            SpellStatOperation.Multiply;

        [Tooltip("The modifier value. Examples: Multiply 0.7 is 30% slower; Multiply 1.25 is 25% faster; Add 0.2 adds 20%.")]
        [SerializeField] private float value = 0.75f;

        [Tooltip("Seconds the modifier remains after it is applied. Area-presence modifiers instead remain exactly while the target is inside.")]
        [SerializeField, Min(0.02f)] private float duration = 3f;

        [Tooltip("How repeated applications of this exact effect source combine on the same target.")]
        [SerializeField] private SpellStatStackingPolicy stacking =
            SpellStatStackingPolicy.RefreshFromSameSource;

        [Tooltip("Maximum simultaneous stacks when Stacking is set to Stack.")]
        [SerializeField, Min(1)] private int maximumStacks = 1;

        public SpellActorStat Stat => stat;
        public SpellStatOperation Operation => operation;
        public float Value => value;
        public float Duration => Mathf.Max(0.02f, duration);
        public SpellStatStackingPolicy Stacking => stacking;
        public int MaximumStacks => Mathf.Max(1, maximumStacks);

        public SpellStatModifierSettings() { }

        public SpellStatModifierSettings(
            SpellActorStat modifiedStat,
            SpellStatOperation modifierOperation,
            float modifierValue,
            float modifierDuration,
            SpellStatStackingPolicy stackingPolicy =
                SpellStatStackingPolicy.RefreshFromSameSource,
            int maxStacks = 1)
        {
            stat = modifiedStat;
            operation = modifierOperation;
            value = modifierValue;
            duration = Mathf.Max(0.02f, modifierDuration);
            stacking = stackingPolicy;
            maximumStacks = Mathf.Max(1, maxStacks);
        }
    }

    public abstract class SpellStatModifierEffectDefinitionBase :
        EffectDefinition,
        IAreaPresenceEffectDefinition
    {
        [Tooltip("Default stat copied into each spell's independent settings.")]
        [SerializeField] private SpellActorStat stat =
            SpellActorStat.MovementSpeed;

        [Tooltip("Default calculation used by the modifier.")]
        [SerializeField] private SpellStatOperation operation =
            SpellStatOperation.Multiply;

        [Tooltip("Default modifier value copied into a spell.")]
        [SerializeField] private float value = 0.75f;

        [Tooltip("Default duration for non-area applications.")]
        [SerializeField, Min(0.02f)] private float duration = 3f;

        [Tooltip("Default repeated-application behavior.")]
        [SerializeField] private SpellStatStackingPolicy stacking =
            SpellStatStackingPolicy.RefreshFromSameSource;

        [Tooltip("Default stack limit when stacking is enabled.")]
        [SerializeField, Min(1)] private int maximumStacks = 1;

        public override Type SettingsType =>
            typeof(SpellStatModifierSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new SpellStatModifierSettings(
                stat,
                operation,
                value,
                duration,
                stacking,
                maximumStacks);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            SpellStatModifierSettings resolved = Resolve(settings);
            SpellStatModifierController controller =
                SpellStatModifierUtility.GetOrAddController(context.Target);
            if (controller == null)
                return false;

            UnityEngine.Object source = context.DeliveryRuntime != null
                ? context.DeliveryRuntime
                : this;
            controller.ApplyTimed(
                source,
                BuildSourceKey(context, resolved),
                resolved,
                resolved.Duration,
                context.Spell != null &&
                context.Spell.Timing.TimeMode == SpellTimeMode.Unscaled);
            return true;
        }

        public bool ApplyPresence(
            in SpellEffectContext context,
            Component source,
            SpellEffectSettings settings)
        {
            SpellStatModifierController controller =
                SpellStatModifierUtility.GetOrAddController(context.Target);
            if (controller == null || source == null)
                return false;

            SpellStatModifierSettings resolved = Resolve(settings);
            controller.SetPersistent(
                source,
                BuildPresenceKey(source, resolved),
                resolved);
            return true;
        }

        public void RemovePresence(
            GameObject target,
            Component source,
            SpellEffectSettings settings)
        {
            SpellStatModifierController controller =
                SpellStatModifierUtility.FindController(target);
            if (controller == null || source == null)
                return;

            SpellStatModifierSettings resolved = Resolve(settings);
            controller.Remove(
                source,
                BuildPresenceKey(source, resolved));
        }

        public override string DescribeApplicationFailure(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            return context.Target == null
                ? "Stat Modifier needs an actor target."
                : "The target could not host a Spell Stat Modifier Controller.";
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            if (issues == null)
                return;
            SpellStatModifierSettings resolved = Resolve(settings);
            if (resolved.Operation == SpellStatOperation.Multiply &&
                resolved.Value < 0f)
            {
                issues.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "A negative Multiply value is clamped to zero at runtime. Use zero to completely disable this stat."));
            }
        }

        private SpellStatModifierSettings Resolve(
            SpellEffectSettings settings)
        {
            return settings as SpellStatModifierSettings ??
                   (SpellStatModifierSettings)CreateDefaultSettings();
        }

        private string BuildSourceKey(
            in SpellEffectContext context,
            SpellStatModifierSettings settings)
        {
            long root = context.Cast.RootCastId;
            return $"{GetInstanceID()}:{(int)settings.Stat}:{root}";
        }

        private string BuildPresenceKey(
            Component source,
            SpellStatModifierSettings settings)
        {
            return $"{GetInstanceID()}:{(int)settings.Stat}:{source.GetInstanceID()}";
        }

        private void OnValidate()
        {
            duration = Mathf.Max(0.02f, duration);
            maximumStacks = Mathf.Max(1, maximumStacks);
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpellStatModifierController : MonoBehaviour
    {
        private sealed class Entry
        {
            public int SourceId;
            public string Key;
            public SpellActorStat Stat;
            public SpellStatOperation Operation;
            public float Value;
            public float ExpiresAt;
            public bool Persistent;
            public bool Unscaled;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public int ActiveModifierCount
        {
            get
            {
                Prune();
                return entries.Count;
            }
        }

        public float Evaluate(SpellActorStat stat, float baseValue = 1f)
        {
            Prune();
            float additive = 0f;
            float multiplier = 1f;
            bool hasOverride = false;
            float overrideValue = baseValue;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Stat != stat)
                    continue;

                switch (entry.Operation)
                {
                    case SpellStatOperation.Add:
                        additive += entry.Value;
                        break;
                    case SpellStatOperation.Override:
                        if (!hasOverride ||
                            IsStronger(entry.Value, overrideValue, baseValue))
                        {
                            overrideValue = entry.Value;
                            hasOverride = true;
                        }
                        break;
                    default:
                        multiplier *= entry.Value;
                        break;
                }
            }

            float resolvedBase = hasOverride ? overrideValue : baseValue;
            return Mathf.Max(0f, (resolvedBase + additive) * multiplier);
        }

        public void ApplyTimed(
            UnityEngine.Object source,
            string key,
            SpellStatModifierSettings settings,
            float duration,
            bool useUnscaledTime = false)
        {
            if (source == null || settings == null)
                return;

            string safeKey = string.IsNullOrWhiteSpace(key)
                ? source.GetInstanceID().ToString()
                : key;
            int sourceId = source.GetInstanceID();
            if (settings.Stacking ==
                SpellStatStackingPolicy.RefreshFromSameSource)
            {
                Entry existing = Find(sourceId, safeKey);
                if (existing != null)
                {
                    CopySettings(existing, settings);
                    existing.Unscaled = useUnscaledTime;
                    existing.ExpiresAt = CurrentTime(useUnscaledTime) +
                                         Mathf.Max(0.02f, duration);
                    existing.Persistent = false;
                    return;
                }
            }
            else if (settings.Stacking ==
                     SpellStatStackingPolicy.KeepStrongest)
            {
                Entry existing = FindByStat(settings.Stat);
                if (existing != null &&
                    !IsStronger(
                        settings.Value,
                        existing.Value,
                        settings.Operation == SpellStatOperation.Add
                            ? 0f
                            : 1f))
                {
                    existing.Unscaled = useUnscaledTime;
                    existing.ExpiresAt = Mathf.Max(
                        existing.ExpiresAt,
                        CurrentTime(useUnscaledTime) +
                        Mathf.Max(0.02f, duration));
                    return;
                }

                if (existing != null)
                    entries.Remove(existing);
            }
            else
            {
                TrimStacks(sourceId, safeKey, settings.MaximumStacks - 1);
                safeKey += $":{Time.frameCount}:{entries.Count}";
            }

            var entry = new Entry
            {
                SourceId = sourceId,
                Key = safeKey,
                ExpiresAt = CurrentTime(useUnscaledTime) +
                            Mathf.Max(0.02f, duration),
                Persistent = false,
                Unscaled = useUnscaledTime
            };
            CopySettings(entry, settings);
            entries.Add(entry);
        }

        public void SetPersistent(
            UnityEngine.Object source,
            string key,
            SpellStatModifierSettings settings)
        {
            if (source == null || settings == null)
                return;

            int sourceId = source.GetInstanceID();
            string safeKey = key ?? sourceId.ToString();
            Entry entry = Find(sourceId, safeKey);
            if (entry == null)
            {
                entry = new Entry
                {
                    SourceId = sourceId,
                    Key = safeKey
                };
                entries.Add(entry);
            }

            CopySettings(entry, settings);
            entry.Persistent = true;
            entry.ExpiresAt = float.PositiveInfinity;
        }

        public void Remove(UnityEngine.Object source, string key)
        {
            if (source == null)
                return;

            int sourceId = source.GetInstanceID();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].SourceId == sourceId &&
                    (string.IsNullOrWhiteSpace(key) ||
                     entries[i].Key == key))
                {
                    entries.RemoveAt(i);
                }
            }
        }

        private void Update()
        {
            Prune();
        }

        private void Prune()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (!entry.Persistent &&
                    entry.ExpiresAt <= CurrentTime(entry.Unscaled))
                    entries.RemoveAt(i);
            }
        }

        private static float CurrentTime(bool unscaled)
        {
            return unscaled ? Time.unscaledTime : Time.time;
        }

        private Entry Find(int sourceId, string key)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].SourceId == sourceId &&
                    entries[i].Key == key)
                {
                    return entries[i];
                }
            }
            return null;
        }

        private Entry FindByStat(SpellActorStat stat)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Stat == stat)
                    return entries[i];
            }
            return null;
        }

        private void TrimStacks(
            int sourceId,
            string keyPrefix,
            int keepCount)
        {
            int count = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (entry.SourceId != sourceId ||
                    !entry.Key.StartsWith(
                        keyPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (count >= Mathf.Max(0, keepCount))
                    entries.RemoveAt(i);
                else
                    count++;
            }
        }

        private static void CopySettings(
            Entry entry,
            SpellStatModifierSettings settings)
        {
            entry.Stat = settings.Stat;
            entry.Operation = settings.Operation;
            entry.Value = settings.Value;
        }

        private static bool IsStronger(
            float candidate,
            float current,
            float neutral)
        {
            return Mathf.Abs(candidate - neutral) >
                   Mathf.Abs(current - neutral);
        }
    }

    public static class SpellStatModifierUtility
    {
        public static float Evaluate(
            GameObject actor,
            SpellActorStat stat,
            float baseValue = 1f)
        {
            SpellStatModifierController controller = FindController(actor);
            return controller != null
                ? controller.Evaluate(stat, baseValue)
                : Mathf.Max(0f, baseValue);
        }

        public static SpellStatModifierController FindController(
            GameObject actor)
        {
            if (actor == null)
                return null;

            GameObject resolved = SpellTargetResolver.Resolve(actor) ?? actor;
            return resolved.GetComponent<SpellStatModifierController>() ??
                   resolved.GetComponentInParent<
                       SpellStatModifierController>(true);
        }

        public static SpellStatModifierController GetOrAddController(
            GameObject actor)
        {
            if (actor == null)
                return null;

            GameObject resolved = SpellTargetResolver.Resolve(actor) ?? actor;
            SpellStatModifierController controller =
                resolved.GetComponent<SpellStatModifierController>();
            return controller != null
                ? controller
                : resolved.AddComponent<SpellStatModifierController>();
        }
    }
}
