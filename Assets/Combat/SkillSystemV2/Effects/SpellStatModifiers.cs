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

        [Tooltip("When enabled, remove this modifier when its specifically targeted party member switches out. When disabled, its duration continues while that member is inactive and it resumes only if that same member becomes active again.")]
        [SerializeField] private bool resetWhenTargetBecomesInactive;

        [Tooltip("Apply this modifier through the shared party pawn so every active party member receives it. Leave disabled for effects intended for one selected member.")]
        [SerializeField] private bool applyToAllPartyMembers;

        public SpellActorStat Stat => stat;
        public SpellStatOperation Operation => operation;
        public float Value => value;
        public float Duration => Mathf.Max(0.02f, duration);
        public SpellStatStackingPolicy Stacking => stacking;
        public int MaximumStacks => Mathf.Max(1, maximumStacks);
        public bool ResetWhenTargetBecomesInactive =>
            resetWhenTargetBecomesInactive;
        public bool ApplyToAllPartyMembers => applyToAllPartyMembers;

        public SpellStatModifierSettings() { }

        public SpellStatModifierSettings(
            SpellActorStat modifiedStat,
            SpellStatOperation modifierOperation,
            float modifierValue,
            float modifierDuration,
            SpellStatStackingPolicy stackingPolicy =
                SpellStatStackingPolicy.RefreshFromSameSource,
            int maxStacks = 1,
            bool resetOnInactive = false,
            bool applyToAllParty = false)
        {
            stat = modifiedStat;
            operation = modifierOperation;
            value = modifierValue;
            duration = Mathf.Max(0.02f, modifierDuration);
            stacking = stackingPolicy;
            maximumStacks = Mathf.Max(1, maxStacks);
            resetWhenTargetBecomesInactive = resetOnInactive;
            applyToAllPartyMembers = applyToAllParty;
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

        [Tooltip("Default behavior when a specifically targeted party member switches out. Enable to remove the modifier; disable to let its duration continue while inactive.")]
        [SerializeField] private bool resetWhenTargetBecomesInactive;

        [Tooltip("Default party scope. Enable only for modifiers intentionally shared by every active party member.")]
        [SerializeField] private bool applyToAllPartyMembers;

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
                maximumStacks,
                resetWhenTargetBecomesInactive,
                applyToAllPartyMembers);
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
            GameObject modifierTarget = ResolveModifierTarget(
                context.Target,
                resolved);
            SpellStatModifierController controller =
                SpellStatModifierUtility.GetOrAddController(modifierTarget);
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
            SpellStatModifierSettings resolved = Resolve(settings);
            GameObject modifierTarget = ResolveModifierTarget(
                context.Target,
                resolved);
            SpellStatModifierController controller =
                SpellStatModifierUtility.GetOrAddController(modifierTarget);
            if (controller == null || source == null)
                return false;

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
            SpellStatModifierSettings resolved = Resolve(settings);
            GameObject modifierTarget = ResolveModifierTarget(
                target,
                resolved);
            SpellStatModifierController controller =
                SpellStatModifierUtility.FindController(modifierTarget);
            if (controller == null || source == null)
                return;

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

        private static GameObject ResolveModifierTarget(
            GameObject target,
            SpellStatModifierSettings settings)
        {
            if (target == null)
                return null;

            GameObject resolved =
                SpellTargetResolver.Resolve(target) ?? target;
            if (settings != null &&
                !settings.ApplyToAllPartyMembers &&
                SpellTargetResolver.HasExplicitIdentity(resolved))
            {
                return resolved;
            }

            MonoBehaviour[] behaviours =
                resolved.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is
                        ISpellStatModifierTargetRouter router))
                {
                    continue;
                }

                GameObject routed = router.ResolveStatModifierTarget(
                    settings != null &&
                    settings.ApplyToAllPartyMembers);
                return routed != null ? routed : resolved;
            }

            return resolved;
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
        private static readonly List<SpellStatModifierController>
            registeredControllers =
                new List<SpellStatModifierController>();

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
            public bool ResetWhenTargetBecomesInactive;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private bool activationStateInitialized;
        private bool previousActivationState;

        public int ActiveModifierCount
        {
            get
            {
                EnsureRegistered();
                RefreshActivationState();
                Prune();
                return entries.Count;
            }
        }

        public float Evaluate(SpellActorStat stat, float baseValue = 1f)
        {
            float additive = 0f;
            float multiplier = 1f;
            bool hasOverride = false;
            float overrideValue = baseValue;

            Accumulate(
                stat,
                baseValue,
                ref additive,
                ref multiplier,
                ref hasOverride,
                ref overrideValue);

            float resolvedBase = hasOverride ? overrideValue : baseValue;
            return Mathf.Max(0f, (resolvedBase + additive) * multiplier);
        }

        internal void Accumulate(
            SpellActorStat stat,
            float baseValue,
            ref float additive,
            ref float multiplier,
            ref bool hasOverride,
            ref float overrideValue)
        {
            EnsureRegistered();
            RefreshActivationState();
            Prune();

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
        }

        internal static int RegisteredControllerCount
        {
            get
            {
                PruneRegisteredControllers();
                return registeredControllers.Count;
            }
        }

        internal static SpellStatModifierController GetRegisteredController(
            int index)
        {
            return index >= 0 && index < registeredControllers.Count
                ? registeredControllers[index]
                : null;
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

            EnsureRegistered();
            CaptureActivationState();

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

            EnsureRegistered();
            CaptureActivationState();

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
            RefreshActivationState();
            Prune();
        }

        private void OnEnable()
        {
            activationStateInitialized = false;
            EnsureRegistered();
        }

        private void OnDisable()
        {
            activationStateInitialized = false;
            registeredControllers.Remove(this);
        }

        internal void RefreshActivationState()
        {
            if (!TryResolveActivationState(out bool isActive))
            {
                activationStateInitialized = false;
                return;
            }

            if (!activationStateInitialized)
            {
                previousActivationState = isActive;
                activationStateInitialized = true;
                return;
            }

            if (previousActivationState && !isActive)
                RemoveResetOnInactiveEntries();

            previousActivationState = isActive;
        }

        /// <summary>
        /// EditMode does not guarantee that a newly-added MonoBehaviour has
        /// received OnEnable before another object queries it. Modifier
        /// application therefore registers explicitly instead of relying on
        /// callback timing. This is also harmless protection after a disabled
        /// domain-reload workflow clears static state.
        /// </summary>
        internal void EnsureRegistered()
        {
            if (isActiveAndEnabled &&
                !registeredControllers.Contains(this))
            {
                registeredControllers.Add(this);
            }
        }

        private void CaptureActivationState()
        {
            if (activationStateInitialized ||
                !TryResolveActivationState(out bool isActive))
            {
                return;
            }

            previousActivationState = isActive;
            activationStateInitialized = true;
        }

        private bool TryResolveActivationState(out bool isActive)
        {
            isActive = true;
            bool foundGate = false;
            MonoBehaviour[] behaviours =
                GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is
                        ISpellStatModifierActivationGate gate))
                {
                    continue;
                }

                foundGate = true;
                if (!gate.AreSpellStatModifiersActive)
                    isActive = false;
            }

            return foundGate;
        }

        private void RemoveResetOnInactiveEntries()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ResetWhenTargetBecomesInactive)
                    entries.RemoveAt(i);
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegisteredControllers()
        {
            registeredControllers.Clear();
        }

        private static void PruneRegisteredControllers()
        {
            for (int i = registeredControllers.Count - 1; i >= 0; i--)
            {
                if (registeredControllers[i] == null)
                    registeredControllers.RemoveAt(i);
            }
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
            entry.ResetWhenTargetBecomesInactive =
                settings.ResetWhenTargetBecomesInactive;
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
            if (actor == null)
                return Mathf.Max(0f, baseValue);

            GameObject resolvedActor =
                SpellTargetResolver.Resolve(actor) ?? actor;
            SpellStatModifierController primary = FindController(actor);
            float additive = 0f;
            float multiplier = 1f;
            bool hasOverride = false;
            float overrideValue = baseValue;

            AccumulateIfActive(
                primary,
                stat,
                baseValue,
                ref additive,
                ref multiplier,
                ref hasOverride,
                ref overrideValue);

            int controllerCount =
                SpellStatModifierController.RegisteredControllerCount;
            for (int i = 0; i < controllerCount; i++)
            {
                SpellStatModifierController controller =
                    SpellStatModifierController.GetRegisteredController(i);
                if (controller == null)
                    continue;

                controller.RefreshActivationState();
                if (controller == primary ||
                    !SpellTargetResolver.IsSameHierarchy(
                        resolvedActor,
                        controller.gameObject))
                {
                    continue;
                }

                AccumulateIfActive(
                    controller,
                    stat,
                    baseValue,
                    ref additive,
                    ref multiplier,
                    ref hasOverride,
                    ref overrideValue);
            }

            float resolvedBase = hasOverride ? overrideValue : baseValue;
            return Mathf.Max(0f, (resolvedBase + additive) * multiplier);
        }

        private static void AccumulateIfActive(
            SpellStatModifierController controller,
            SpellActorStat stat,
            float baseValue,
            ref float additive,
            ref float multiplier,
            ref bool hasOverride,
            ref float overrideValue)
        {
            if (controller == null || !controller.isActiveAndEnabled)
                return;

            controller.RefreshActivationState();
            if (!IsContributionActive(controller))
                return;

            controller.Accumulate(
                stat,
                baseValue,
                ref additive,
                ref multiplier,
                ref hasOverride,
                ref overrideValue);
        }

        private static bool IsContributionActive(
            SpellStatModifierController controller)
        {
            MonoBehaviour[] behaviours =
                controller.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is
                        ISpellStatModifierActivationGate gate &&
                    !gate.AreSpellStatModifiersActive)
                {
                    return false;
                }
            }

            return true;
        }

        public static SpellStatModifierController FindController(
            GameObject actor)
        {
            if (actor == null)
                return null;

            GameObject resolved = SpellTargetResolver.Resolve(actor) ?? actor;
            SpellStatModifierController direct =
                resolved.GetComponent<SpellStatModifierController>();
            if (direct != null ||
                SpellTargetResolver.HasExplicitIdentity(resolved))
            {
                return direct;
            }

            return resolved.GetComponentInParent<
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
            if (controller == null)
            {
                controller =
                    resolved.AddComponent<SpellStatModifierController>();
            }

            controller.EnsureRegistered();
            return controller;
        }
    }

    public static class SpellActionPointPickupUtility
    {
        public static int ResolveRewardValue(
            GameObject actor,
            int baseAmount)
        {
            if (baseAmount <= 0)
                return 0;

            float multiplier = SpellStatModifierUtility.Evaluate(
                actor,
                SpellActorStat.ActionPointPickupValue,
                1f);
            return Mathf.Max(
                0,
                Mathf.RoundToInt(baseAmount * multiplier));
        }

        public static int ResolveParticleCount(
            int baseTotalAP,
            int resolvedTotalAP,
            int preferredAPPerParticle,
            int maximumParticles)
        {
            if (resolvedTotalAP <= 0)
                return 0;

            int preferredValue = Mathf.Max(1, preferredAPPerParticle);
            int maximumCount = Mathf.Min(
                Mathf.Max(1, maximumParticles),
                resolvedTotalAP);
            if (baseTotalAP <= 0)
            {
                return Mathf.Clamp(
                    Mathf.CeilToInt(
                        resolvedTotalAP / (float)preferredValue),
                    1,
                    maximumCount);
            }

            int baseParticleCount = Mathf.Clamp(
                Mathf.CeilToInt(
                    baseTotalAP / (float)preferredValue),
                1,
                Mathf.Min(
                    Mathf.Max(1, maximumParticles),
                    baseTotalAP));
            if (resolvedTotalAP == baseTotalAP)
                return Mathf.Min(baseParticleCount, maximumCount);

            float scaledCount = baseParticleCount *
                (resolvedTotalAP / (float)baseTotalAP);
            int resolvedCount = resolvedTotalAP > baseTotalAP
                ? Mathf.CeilToInt(scaledCount)
                : Mathf.FloorToInt(scaledCount);
            return Mathf.Clamp(resolvedCount, 1, maximumCount);
        }
    }
}
