using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellDeliveryLifecycleStage
    {
        CastStarted,
        ExecutionCreated,
        DeliveryStarted,
        PointReached,
        TargetHit,
        BlockingHit,
        DeliveryStopped,
        AreaCreated,
        AreaPulse,
        TargetEnteredArea,
        TargetExitedArea,
        DeliveryExpired,
        ManualReaction,
        Armed,
        TargetCrossed,
        ProximityTriggered,
        TimerExpired,
        Bounced,
        Stuck,
        Deflected,
        Detonated,
        ExecutionEnded,
        CastCompleted,
        CastInterrupted,
        CastRejected,
        Cancelled,
        FailedToCreate,
        FailedToBegin,
        FailedDuringTick,
        FailedToEnd,
        FailedToCancel
    }

    /// <summary>
    /// One ordered delivery milestone. Existing delivery events feed this
    /// stream automatically; SpellRunner adds lifecycle and exception states
    /// that delivery event recipes cannot represent.
    /// </summary>
    public readonly struct SpellDeliveryDiagnostic
    {
        public long Sequence { get; }
        public int Frame { get; }
        public SpellDeliveryLifecycleStage Stage { get; }
        public SpellEventType SourceEvent { get; }
        public SpellDefinition Spell { get; }
        public DeliveryDefinition Delivery { get; }
        public GameObject Caster { get; }
        public GameObject Subject { get; }
        public Vector2 Point { get; }
        public Component DeliveryRuntime { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public bool IsFailure =>
            Stage == SpellDeliveryLifecycleStage.FailedToCreate ||
            Stage == SpellDeliveryLifecycleStage.CastRejected ||
            Stage == SpellDeliveryLifecycleStage.FailedToBegin ||
            Stage == SpellDeliveryLifecycleStage.FailedDuringTick ||
            Stage == SpellDeliveryLifecycleStage.FailedToEnd ||
            Stage == SpellDeliveryLifecycleStage.FailedToCancel;

        public bool IsTargetActivity =>
            Stage == SpellDeliveryLifecycleStage.TargetHit ||
            Stage == SpellDeliveryLifecycleStage.BlockingHit ||
            Stage == SpellDeliveryLifecycleStage.AreaPulse ||
            Stage == SpellDeliveryLifecycleStage.TargetEnteredArea ||
            Stage == SpellDeliveryLifecycleStage.TargetExitedArea ||
            Stage == SpellDeliveryLifecycleStage.TargetCrossed;

        public SpellDeliveryDiagnostic(
            long sequence,
            SpellDeliveryLifecycleStage stage,
            SpellEventType sourceEvent,
            SpellDefinition spell,
            DeliveryDefinition delivery,
            GameObject caster,
            GameObject subject,
            Vector2 point,
            Component deliveryRuntime,
            string message,
            Exception exception = null)
        {
            Sequence = Math.Max(0L, sequence);
            Frame = Time.frameCount;
            Stage = stage;
            SourceEvent = sourceEvent;
            Spell = spell;
            Delivery = delivery;
            Caster = caster;
            Subject = subject;
            Point = point;
            DeliveryRuntime = deliveryRuntime;
            Message = message ?? string.Empty;
            Exception = exception;
        }
    }

    public static partial class SpellRuntimeDiagnostics
    {
        private static long deliverySequence;

        public static event Action<SpellDeliveryDiagnostic>
            DeliveryLifecycle;

        internal static void ReportDeliveryEvent(
            SpellDefinition spell,
            in CastContext cast,
            in SpellEventOccurrence occurrence)
        {
            if (DeliveryLifecycle == null || spell == null ||
                occurrence.Type == SpellEventType.None)
            {
                return;
            }

            ReportDeliveryState(
                spell,
                cast,
                MapStage(occurrence.Type),
                DescribeEvent(occurrence.Type),
                occurrence.Subject,
                occurrence.Point,
                occurrence.DeliveryRuntime,
                occurrence.Type);
        }

        internal static void ReportDeliveryState(
            SpellDefinition spell,
            in CastContext cast,
            SpellDeliveryLifecycleStage stage,
            string message,
            GameObject subject = null,
            Vector2 point = default,
            Component deliveryRuntime = null,
            SpellEventType sourceEvent = SpellEventType.None,
            Exception exception = null)
        {
            Action<SpellDeliveryDiagnostic> handlers = DeliveryLifecycle;
            if (handlers == null)
                return;

            var diagnostic = new SpellDeliveryDiagnostic(
                ++deliverySequence,
                stage,
                sourceEvent,
                spell,
                spell != null ? spell.Delivery : null,
                cast.Caster,
                subject,
                point,
                deliveryRuntime,
                message,
                exception);
            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<SpellDeliveryDiagnostic>)subscribers[i])(
                        diagnostic);
                }
                catch (Exception listenerException)
                {
                    Debug.LogException(listenerException);
                }
            }
        }

        private static SpellDeliveryLifecycleStage MapStage(
            SpellEventType eventType)
        {
            switch (eventType)
            {
                case SpellEventType.CastStarted:
                    return SpellDeliveryLifecycleStage.CastStarted;
                case SpellEventType.DeliveryStarted:
                    return SpellDeliveryLifecycleStage.DeliveryStarted;
                case SpellEventType.PointReached:
                    return SpellDeliveryLifecycleStage.PointReached;
                case SpellEventType.TargetHit:
                    return SpellDeliveryLifecycleStage.TargetHit;
                case SpellEventType.BlockingHit:
                    return SpellDeliveryLifecycleStage.BlockingHit;
                case SpellEventType.DeliveryStopped:
                    return SpellDeliveryLifecycleStage.DeliveryStopped;
                case SpellEventType.AreaCreated:
                    return SpellDeliveryLifecycleStage.AreaCreated;
                case SpellEventType.AreaPulse:
                    return SpellDeliveryLifecycleStage.AreaPulse;
                case SpellEventType.TargetEnteredArea:
                    return SpellDeliveryLifecycleStage.TargetEnteredArea;
                case SpellEventType.TargetExitedArea:
                    return SpellDeliveryLifecycleStage.TargetExitedArea;
                case SpellEventType.DeliveryExpired:
                    return SpellDeliveryLifecycleStage.DeliveryExpired;
                case SpellEventType.ManualReaction:
                    return SpellDeliveryLifecycleStage.ManualReaction;
                case SpellEventType.Armed:
                    return SpellDeliveryLifecycleStage.Armed;
                case SpellEventType.TargetCrossed:
                    return SpellDeliveryLifecycleStage.TargetCrossed;
                case SpellEventType.ProximityTriggered:
                    return SpellDeliveryLifecycleStage.ProximityTriggered;
                case SpellEventType.TimerExpired:
                    return SpellDeliveryLifecycleStage.TimerExpired;
                case SpellEventType.Bounced:
                    return SpellDeliveryLifecycleStage.Bounced;
                case SpellEventType.Stuck:
                    return SpellDeliveryLifecycleStage.Stuck;
                case SpellEventType.Deflected:
                    return SpellDeliveryLifecycleStage.Deflected;
                case SpellEventType.Detonated:
                    return SpellDeliveryLifecycleStage.Detonated;
                default:
                    return SpellDeliveryLifecycleStage.DeliveryStopped;
            }
        }

        private static string DescribeEvent(SpellEventType eventType)
        {
            switch (eventType)
            {
                case SpellEventType.CastStarted:
                    return "The cast was accepted and started.";
                case SpellEventType.DeliveryStarted:
                    return "The delivery entered gameplay.";
                case SpellEventType.PointReached:
                    return "The delivery reached its target point.";
                case SpellEventType.TargetHit:
                    return "The delivery reported a valid target hit.";
                case SpellEventType.BlockingHit:
                    return "The delivery hit a blocking collider.";
                case SpellEventType.DeliveryStopped:
                    return "The delivery stopped normally.";
                case SpellEventType.AreaCreated:
                    return "The persistent area was created.";
                case SpellEventType.AreaPulse:
                    return "The area completed an effect pulse.";
                case SpellEventType.TargetEnteredArea:
                    return "A valid target entered the area.";
                case SpellEventType.TargetExitedArea:
                    return "A target exited the area.";
                case SpellEventType.DeliveryExpired:
                    return "The delivery reached its configured lifetime.";
                case SpellEventType.ManualReaction:
                    return "A reaction was activated manually.";
                case SpellEventType.Armed:
                    return "The placed delivery is armed.";
                case SpellEventType.TargetCrossed:
                    return "A valid target crossed the trip wire.";
                case SpellEventType.ProximityTriggered:
                    return "A valid target triggered the proximity check.";
                case SpellEventType.TimerExpired:
                    return "The delivery timer expired.";
                case SpellEventType.Bounced:
                    return "The delivery bounced from a collider.";
                case SpellEventType.Stuck:
                    return "The delivery stuck to a collider.";
                case SpellEventType.Deflected:
                    return "The delivery was deflected.";
                case SpellEventType.Detonated:
                    return "The delivery detonated.";
                default:
                    return $"The delivery reported {eventType}.";
            }
        }
    }
}
