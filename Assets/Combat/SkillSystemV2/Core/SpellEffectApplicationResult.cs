using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellEffectApplicationStatus
    {
        Applied,
        PartialSuccess,
        MissingSpell,
        MissingTarget,
        TargetResolutionFailed,
        TargetRejected,
        NoEffectsConfigured,
        NoApplicableEffects,
        DeferredToDeliveryAnchor,
        AllEffectsRejected,
        EffectException
    }

    public enum SpellEffectSlotStatus
    {
        Applied,
        EmptySlot,
        PresenceEffectSkipped,
        DeliveryAnchorDeferred,
        Rejected,
        Exception
    }

    /// <summary>
    /// Allocation-free summary of one request to apply a group of effects.
    /// Existing callers may continue using the integer ApplyEffects API while
    /// diagnostics and new code consume this richer result.
    /// </summary>
    public readonly struct SpellEffectApplicationResult
    {
        public SpellEffectApplicationStatus Status { get; }
        public SpellDefinition Spell { get; }
        public GameObject RequestedTarget { get; }
        public GameObject ResolvedTarget { get; }
        public GameObject DetectedObject { get; }
        public int ConfiguredSlotCount { get; }
        public int AttemptedCount { get; }
        public int AppliedCount { get; }
        public int RejectedCount { get; }
        public int SkippedCount { get; }
        public int ExceptionCount { get; }
        public string Message { get; }

        public bool Succeeded => AppliedCount > 0;
        public bool HasProblems =>
            Status != SpellEffectApplicationStatus.Applied &&
            Status != SpellEffectApplicationStatus
                .DeferredToDeliveryAnchor;

        public SpellEffectApplicationResult(
            SpellEffectApplicationStatus status,
            SpellDefinition spell,
            GameObject requestedTarget,
            GameObject resolvedTarget,
            GameObject detectedObject,
            int configuredSlotCount,
            int attemptedCount,
            int appliedCount,
            int rejectedCount,
            int skippedCount,
            int exceptionCount,
            string message)
        {
            Status = status;
            Spell = spell;
            RequestedTarget = requestedTarget;
            ResolvedTarget = resolvedTarget;
            DetectedObject = detectedObject;
            ConfiguredSlotCount = Mathf.Max(0, configuredSlotCount);
            AttemptedCount = Mathf.Max(0, attemptedCount);
            AppliedCount = Mathf.Max(0, appliedCount);
            RejectedCount = Mathf.Max(0, rejectedCount);
            SkippedCount = Mathf.Max(0, skippedCount);
            ExceptionCount = Mathf.Max(0, exceptionCount);
            Message = message ?? string.Empty;
        }
    }

    public readonly struct SpellEffectSlotDiagnostic
    {
        public SpellDefinition Spell { get; }
        public EffectDefinition Effect { get; }
        public GameObject Target { get; }
        public int SlotIndex { get; }
        public SpellEffectSlotStatus Status { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public SpellEffectSlotDiagnostic(
            SpellDefinition spell,
            EffectDefinition effect,
            GameObject target,
            int slotIndex,
            SpellEffectSlotStatus status,
            string message,
            Exception exception = null)
        {
            Spell = spell;
            Effect = effect;
            Target = target;
            SlotIndex = slotIndex;
            Status = status;
            Message = message ?? string.Empty;
            Exception = exception;
        }
    }

    /// <summary>
    /// Optional runtime diagnostic stream. With no subscribers it produces no
    /// logs and allocates no collections. Tools and debug components can
    /// subscribe without coupling delivery code to presentation.
    /// </summary>
    public static partial class SpellRuntimeDiagnostics
    {
        public static event Action<SpellEffectApplicationResult>
            ApplicationCompleted;
        public static event Action<SpellEffectSlotDiagnostic>
            EffectSlotCompleted;

        internal static void ReportApplication(
            in SpellEffectApplicationResult result)
        {
            Action<SpellEffectApplicationResult> handlers =
                ApplicationCompleted;
            if (handlers == null)
                return;

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<SpellEffectApplicationResult>)subscribers[i])(
                        result);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        internal static void ReportEffectSlot(
            in SpellEffectSlotDiagnostic diagnostic)
        {
            Action<SpellEffectSlotDiagnostic> handlers =
                EffectSlotCompleted;
            if (handlers == null)
                return;

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<SpellEffectSlotDiagnostic>)subscribers[i])(
                        diagnostic);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSubscribers()
        {
            ApplicationCompleted = null;
            EffectSlotCompleted = null;
            DeliveryLifecycle = null;
            deliverySequence = 0L;
        }
    }
}
