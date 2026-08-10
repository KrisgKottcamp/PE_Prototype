using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellRunner : MonoBehaviour
    {
        private struct CooldownEntry
        {
            public float Remaining;
            public SpellTimeMode TimeMode;
        }

        private readonly struct QueuedCast
        {
            public SpellDefinition Spell { get; }
            public CastContext Context { get; }

            public QueuedCast(
                SpellDefinition spell,
                in CastContext context)
            {
                Spell = spell;
                Context = context;
            }
        }

        [SerializeField]
        private bool logRejectedCasts;

        [SerializeField, Min(1)]
        private int maximumQueuedTriggeredCasts = 16;

        private readonly Dictionary<string, CooldownEntry> cooldowns =
            new Dictionary<string, CooldownEntry>();

        private readonly List<string> cooldownKeyBuffer =
            new List<string>();

        private readonly List<SpellValidationIssue> validationBuffer =
            new List<SpellValidationIssue>();

        private readonly Queue<QueuedCast> triggeredCastQueue =
            new Queue<QueuedCast>();

        private SpellDefinition activeSpell;
        private CastContext activeContext;
        private SpellCastPhase currentPhase = SpellCastPhase.Idle;
        private float phaseTimeRemaining;
        private ISpellDeliveryExecution activeDelivery;

        public event Action<SpellCastEvent> CastStarted;
        public event Action<SpellCastEvent> PhaseChanged;
        public event Action<SpellCastEvent> CastCompleted;
        public event Action<SpellCastEvent> CastInterrupted;

        public bool IsCasting => activeSpell != null;
        public SpellDefinition ActiveSpell => activeSpell;
        public CastContext ActiveContext => activeContext;
        public SpellCastPhase CurrentPhase => currentPhase;
        public float PhaseTimeRemaining => Mathf.Max(0f, phaseTimeRemaining);
        public int QueuedTriggeredCastCount => triggeredCastQueue.Count;

        private void Update()
        {
            TickRuntime(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            triggeredCastQueue.Clear();
            Interrupt("SpellRunner was disabled.");
        }

        public bool TryCast(
            SpellDefinition spell,
            in CastContext requestedContext,
            out SpellCastFailure failure)
        {
            if (spell == null)
                return Reject(SpellCastFailure.MissingSpell, out failure);

            if (IsCasting)
                return Reject(SpellCastFailure.RunnerBusy, out failure);

            if (!ValidateDefinition(spell))
                return Reject(SpellCastFailure.InvalidDefinition, out failure);

            CastContext context = requestedContext.Caster == null
                ? requestedContext.WithCaster(gameObject)
                : requestedContext;

            if (!spell.ValidateContext(context, out string contextReason))
            {
                return Reject(
                    SpellCastFailure.InvalidContext,
                    out failure,
                    contextReason);
            }

            if (GetCooldownRemaining(spell) > 0f)
                return Reject(SpellCastFailure.OnCooldown, out failure);

            CastChainBudget budget = context.ChainBudget ??
                                     spell.CreateChainBudget();
            context = context.WithBudget(budget);

            if (!budget.CanActivate(context.ChainDepth))
            {
                return Reject(
                    SpellCastFailure.ChainBudgetExceeded,
                    out failure);
            }

            ISpellResourceProvider resourceProvider = null;
            SpellResourceCost cost = spell.ResourceCost;

            if (!cost.IsFree)
            {
                resourceProvider = FindResourceProvider(context.Caster);

                if (resourceProvider == null)
                {
                    return Reject(
                        SpellCastFailure.MissingResourceProvider,
                        out failure);
                }

                if (!resourceProvider.CanSpend(cost) ||
                    !resourceProvider.TrySpend(cost))
                {
                    return Reject(
                        SpellCastFailure.InsufficientResources,
                        out failure);
                }
            }

            if (!budget.TryConsumeActivation(context.ChainDepth))
            {
                resourceProvider?.Refund(cost);

                return Reject(
                    SpellCastFailure.ChainBudgetExceeded,
                    out failure);
            }

            activeSpell = spell;
            activeContext = context;
            activeDelivery = null;

            StartCooldown(spell);

            CastStarted?.Invoke(new SpellCastEvent(
                activeSpell,
                activeContext,
                SpellCastPhase.BuildUp));

            if (!IsCasting)
            {
                failure = SpellCastFailure.None;
                return true;
            }

            EnterPhase(SpellCastPhase.BuildUp);
            AdvanceInstantPhases();

            failure = SpellCastFailure.None;
            return true;
        }

        public bool Interrupt(string reason = "Cast interrupted.")
        {
            if (!IsCasting)
                return false;

            SpellDefinition interruptedSpell = activeSpell;
            CastContext interruptedContext = activeContext;
            SpellCastPhase interruptedPhase = currentPhase;

            CancelDelivery();
            ClearActiveCast();

            CastInterrupted?.Invoke(new SpellCastEvent(
                interruptedSpell,
                interruptedContext,
                interruptedPhase,
                reason));

            TryStartNextTriggeredCast();

            return true;
        }

        public bool QueueTriggeredCast(
            SpellDefinition spell,
            in CastContext requestedContext,
            out SpellCastFailure failure)
        {
            if (!IsCasting)
                return TryCast(spell, requestedContext, out failure);

            if (spell == null)
                return Reject(SpellCastFailure.MissingSpell, out failure);

            if (triggeredCastQueue.Count >=
                Mathf.Max(1, maximumQueuedTriggeredCasts))
            {
                return Reject(
                    SpellCastFailure.TriggeredQueueFull,
                    out failure);
            }

            if (!ValidateDefinition(spell))
                return Reject(SpellCastFailure.InvalidDefinition, out failure);

            CastContext context = requestedContext.Caster == null
                ? requestedContext.WithCaster(gameObject)
                : requestedContext;

            if (!spell.ValidateContext(context, out string contextReason))
            {
                return Reject(
                    SpellCastFailure.InvalidContext,
                    out failure,
                    contextReason);
            }

            CastChainBudget budget = context.ChainBudget ??
                                     spell.CreateChainBudget();
            context = context.WithBudget(budget);

            if (!budget.CanActivate(context.ChainDepth))
            {
                return Reject(
                    SpellCastFailure.ChainBudgetExceeded,
                    out failure);
            }

            triggeredCastQueue.Enqueue(new QueuedCast(spell, context));
            failure = SpellCastFailure.None;
            return true;
        }

        public float GetCooldownRemaining(SpellDefinition spell)
        {
            if (spell == null)
                return 0f;

            return cooldowns.TryGetValue(
                spell.CooldownKey,
                out CooldownEntry entry)
                ? Mathf.Max(0f, entry.Remaining)
                : 0f;
        }

        public bool IsOnCooldown(SpellDefinition spell)
        {
            return GetCooldownRemaining(spell) > 0f;
        }

        internal void TickRuntime(
            float scaledDeltaTime,
            float unscaledDeltaTime)
        {
            float safeScaledDelta = Mathf.Max(0f, scaledDeltaTime);
            float safeUnscaledDelta = Mathf.Max(0f, unscaledDeltaTime);

            TickCooldowns(safeScaledDelta, safeUnscaledDelta);

            if (!IsCasting)
                return;

            float castDelta = activeSpell.Timing.TimeMode ==
                              SpellTimeMode.Unscaled
                ? safeUnscaledDelta
                : safeScaledDelta;

            if (currentPhase == SpellCastPhase.Firing ||
                currentPhase == SpellCastPhase.Channeling)
            {
                TickDelivery(castDelta);
            }

            if (!IsCasting)
                return;

            phaseTimeRemaining -= castDelta;

            int transitionSafety = 0;
            while (IsCasting &&
                   phaseTimeRemaining <= 0f &&
                   transitionSafety < 8)
            {
                float overflow = -phaseTimeRemaining;
                AdvancePhase();

                if (IsCasting)
                    phaseTimeRemaining -= overflow;

                transitionSafety++;
            }
        }

        private bool ValidateDefinition(SpellDefinition spell)
        {
            validationBuffer.Clear();
            spell.CollectValidationIssues(validationBuffer);

            for (int i = 0; i < validationBuffer.Count; i++)
            {
                if (validationBuffer[i].Severity ==
                    SpellValidationSeverity.Error)
                {
                    if (logRejectedCasts)
                    {
                        Debug.LogWarning(
                            $"Cannot cast {spell.DisplayName}: " +
                            validationBuffer[i].Message,
                            this);
                    }

                    return false;
                }
            }

            return true;
        }

        private bool Reject(
            SpellCastFailure rejectedFailure,
            out SpellCastFailure failure,
            string detail = "")
        {
            failure = rejectedFailure;

            if (logRejectedCasts)
            {
                string suffix = string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : $" {detail}";

                Debug.LogWarning(
                    $"Spell cast rejected: {rejectedFailure}.{suffix}",
                    this);
            }

            return false;
        }

        private void StartCooldown(SpellDefinition spell)
        {
            if (spell.Cooldown <= 0f)
                return;

            cooldowns[spell.CooldownKey] = new CooldownEntry
            {
                Remaining = spell.Cooldown,
                TimeMode = spell.Timing.TimeMode
            };
        }

        private void TickCooldowns(
            float scaledDeltaTime,
            float unscaledDeltaTime)
        {
            if (cooldowns.Count == 0)
                return;

            cooldownKeyBuffer.Clear();
            cooldownKeyBuffer.AddRange(cooldowns.Keys);

            for (int i = 0; i < cooldownKeyBuffer.Count; i++)
            {
                string key = cooldownKeyBuffer[i];
                CooldownEntry entry = cooldowns[key];
                float delta = entry.TimeMode == SpellTimeMode.Unscaled
                    ? unscaledDeltaTime
                    : scaledDeltaTime;

                entry.Remaining -= delta;

                if (entry.Remaining <= 0f)
                    cooldowns.Remove(key);
                else
                    cooldowns[key] = entry;
            }
        }

        private void EnterPhase(SpellCastPhase phase)
        {
            currentPhase = phase;
            phaseTimeRemaining = activeSpell.Timing.GetDuration(phase);

            if (phase == SpellCastPhase.Firing && !BeginDelivery())
                return;

            PhaseChanged?.Invoke(new SpellCastEvent(
                activeSpell,
                activeContext,
                phase));
        }

        private void AdvanceInstantPhases()
        {
            int transitionSafety = 0;

            while (IsCasting &&
                   phaseTimeRemaining <= 0f &&
                   transitionSafety < 8)
            {
                AdvancePhase();
                transitionSafety++;
            }
        }

        private void AdvancePhase()
        {
            switch (currentPhase)
            {
                case SpellCastPhase.BuildUp:
                    EnterPhase(SpellCastPhase.Firing);
                    break;

                case SpellCastPhase.Firing:
                    EnterPhase(SpellCastPhase.Channeling);
                    break;

                case SpellCastPhase.Channeling:
                    EndDelivery();

                    if (IsCasting)
                        EnterPhase(SpellCastPhase.Recovery);
                    break;

                case SpellCastPhase.Recovery:
                    CompleteCast();
                    break;

                default:
                    Interrupt("SpellRunner entered an invalid phase.");
                    break;
            }
        }

        private bool BeginDelivery()
        {
            try
            {
                var executionContext = new SpellExecutionContext(
                    activeSpell,
                    activeContext);

                activeDelivery = activeSpell.Delivery.CreateExecution(
                    executionContext,
                    activeSpell.DeliverySettings);

                if (activeDelivery == null)
                {
                    Interrupt("Delivery returned no runtime execution.");
                    return false;
                }

                activeDelivery.Begin();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, activeSpell.Delivery);
                Interrupt("Delivery failed to begin.");
                return false;
            }
        }

        private void TickDelivery(float deltaTime)
        {
            if (activeDelivery == null)
                return;

            try
            {
                activeDelivery.Tick(deltaTime);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, activeSpell.Delivery);
                Interrupt("Delivery failed while executing.");
            }
        }

        private void EndDelivery()
        {
            if (activeDelivery == null)
                return;

            try
            {
                activeDelivery.End();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, activeSpell.Delivery);
                Interrupt("Delivery failed while ending.");
                return;
            }

            activeDelivery = null;
        }

        private void CancelDelivery()
        {
            if (activeDelivery == null)
                return;

            try
            {
                activeDelivery.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, activeSpell);
            }

            activeDelivery = null;
        }

        private void CompleteCast()
        {
            SpellDefinition completedSpell = activeSpell;
            CastContext completedContext = activeContext;

            EndDelivery();
            if (!IsCasting)
                return;

            ClearActiveCast();

            CastCompleted?.Invoke(new SpellCastEvent(
                completedSpell,
                completedContext,
                SpellCastPhase.Recovery));

            TryStartNextTriggeredCast();
        }

        private void TryStartNextTriggeredCast()
        {
            while (!IsCasting && triggeredCastQueue.Count > 0)
            {
                QueuedCast queued = triggeredCastQueue.Dequeue();
                if (TryCast(queued.Spell, queued.Context, out _))
                    return;
            }
        }

        private void ClearActiveCast()
        {
            activeSpell = null;
            activeContext = default;
            activeDelivery = null;
            currentPhase = SpellCastPhase.Idle;
            phaseTimeRemaining = 0f;
        }

        private static ISpellResourceProvider FindResourceProvider(
            GameObject caster)
        {
            if (caster == null)
                return null;

            MonoBehaviour[] behaviours =
                caster.GetComponentsInParent<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellResourceProvider provider)
                    return provider;
            }

            return null;
        }
    }
}
