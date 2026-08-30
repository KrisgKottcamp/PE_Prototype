using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemyActionRunnerV2 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnemyAgentV2 owner;
        [SerializeField] private EnemyLocomotionV2 locomotion;
        [SerializeField] private EnemyCombatExecutorV2 combatExecutor;
        [SerializeField] private EnemySkillExecutorV2 skillExecutor;
        [SerializeField] private EnemySpellTargetingSolverV2 targetingSolver;
        [SerializeField] private SpellRunner spellRunner;
        [SerializeField] private EnemyHealth enemyHealth;

        [Header("Runtime Debug")]
        [SerializeField] private EnemyActionKindV2 debugAction = EnemyActionKindV2.None;
        [SerializeField] private EnemyActionStatusV2 debugStatus = EnemyActionStatusV2.Idle;
        [SerializeField] private string debugReason = "Idle";
        [SerializeField] private string debugLastIgnoredInterruption = "None";
        [SerializeField] private int debugOrderId;
        [SerializeField] private float debugActionElapsed;

        private EnemyActionOrderV2 currentOrder;
        private float actionStartedAt;
        private float skillStartedAt;
        private float nextApproachRefreshAt;
        private bool approachSkillStarted;
        private EnemyActionRunnerV2 heldSupportTargetRunner;

        public EnemyActionKindV2 CurrentKind => debugAction;
        public EnemyActionStatusV2 Status => debugStatus;
        public string DebugReason => debugReason;
        public int CurrentOrderId => debugOrderId;
        public bool IsBusy => debugStatus == EnemyActionStatusV2.Running;
        public bool HasProtectedSupportCommitment =>
            IsBusy && !CanPlanningCancelCurrent(
                CurrentKind,
                IsSupportAction(currentOrder));

        private void Awake()
        {
            if (owner == null)
                owner = GetComponent<EnemyAgentV2>();

            if (locomotion == null)
                locomotion = GetComponent<EnemyLocomotionV2>();

            if (combatExecutor == null)
                combatExecutor = GetComponent<EnemyCombatExecutorV2>();

            if (skillExecutor == null)
                skillExecutor = GetComponent<EnemySkillExecutorV2>();

            if (targetingSolver == null)
                targetingSolver =
                    GetComponent<EnemySpellTargetingSolverV2>();

            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();

            if (enemyHealth == null)
                enemyHealth = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();
            if (enemyHealth == null)
                enemyHealth = GetComponent<EnemyHealth>();

            if (spellRunner != null)
                spellRunner.PhaseChanged += HandleSpellPhaseChanged;
            if (enemyHealth != null)
                enemyHealth.OnDamaged += HandleOwnerDamaged;
        }

        private void OnDisable()
        {
            if (spellRunner != null)
                spellRunner.PhaseChanged -= HandleSpellPhaseChanged;
            if (enemyHealth != null)
                enemyHealth.OnDamaged -= HandleOwnerDamaged;
            CancelCurrentInternal("Action runner disabled");
        }

        private void Update()
        {
            if (debugStatus != EnemyActionStatusV2.Running || currentOrder == null)
                return;

            debugActionElapsed = Time.time - actionStartedAt;

            switch (currentOrder.kind)
            {
                case EnemyActionKindV2.MoveToSlot:
                case EnemyActionKindV2.EvadeThreat:
                    TickMoveToSlot();
                    break;

                case EnemyActionKindV2.HoldLane:
                case EnemyActionKindV2.Guard:
                case EnemyActionKindV2.Recover:
                    TickTimedAction();
                    break;

                case EnemyActionKindV2.AttackPattern:
                    TickAttack();
                    break;

                case EnemyActionKindV2.CastSkill:
                    TickSkill();
                    break;

                case EnemyActionKindV2.ApproachAndCastSkill:
                    TickApproachAndCastSkill();
                    break;

                case EnemyActionKindV2.HoldForSupport:
                    TickSupportHold();
                    break;

                case EnemyActionKindV2.FluidPressure:
                    TickFluidPressure();
                    break;

                default:
                    Finish(EnemyActionStatusV2.Failed, "Unsupported action");
                    break;
            }
        }

        public bool AssignOrder(EnemyActionOrderV2 order)
        {
            if (order == null)
                return false;

            bool hasSupportCommitment =
                IsBusy && IsSupportAction(currentOrder);
            bool verifiedThreatInterrupt =
                hasSupportCommitment &&
                CanThreatInterruptSupportAction(order);
            if (hasSupportCommitment && !verifiedThreatInterrupt)
            {
                debugLastIgnoredInterruption =
                    $"Ignored {order.kind}: active Support commitment";
                return false;
            }

            if (IsBusy && !CanReplaceCurrentAction(
                    CurrentKind,
                    order.kind))
            {
                debugLastIgnoredInterruption =
                    $"Ignored {order.kind}: {CurrentKind} is protected";
                return false;
            }

            CancelCurrentInternal(verifiedThreatInterrupt
                ? $"Support interrupted by imminent threat " +
                  $"{order.threatId} (score {order.threatScore:0.00}, " +
                  $"impact {order.threatTimeToImpact:0.00}s)"
                : "Replaced by new order");

            currentOrder = order.Clone();
            debugOrderId = currentOrder.orderId;
            debugAction = currentOrder.kind;
            debugStatus = EnemyActionStatusV2.Running;
            debugReason = currentOrder.reason;
            debugActionElapsed = 0f;
            actionStartedAt = Time.time;
            skillStartedAt = actionStartedAt;
            nextApproachRefreshAt = 0f;
            approachSkillStarted = false;

            switch (currentOrder.kind)
            {
                case EnemyActionKindV2.MoveToSlot:
                case EnemyActionKindV2.EvadeThreat:
                    if (locomotion == null ||
                        !locomotion.SetDestination(currentOrder.targetPosition))
                    {
                        Finish(EnemyActionStatusV2.Failed, "Move destination rejected");
                        return false;
                    }
                    break;

                case EnemyActionKindV2.AttackPattern:
                    if (combatExecutor == null || owner == null ||
                        !combatExecutor.BeginAttack(
                            owner.PlayerTarget,
                            currentOrder.patternName,
                            currentOrder.shotsPerBurst,
                            currentOrder.intraBurstInterval,
                            currentOrder.burstCooldown,
                            currentOrder.resetShooterAimSamples,
                            currentOrder.aimLagSeconds,
                            currentOrder.overridePatternShape,
                            currentOrder.fanBullets,
                            currentOrder.fanArcDegrees,
                            currentOrder.ringBullets,
                            currentOrder.angularSpeedDegPerTick))
                    {
                        Finish(EnemyActionStatusV2.Failed, "Attack could not begin");
                        return false;
                    }
                    break;

                case EnemyActionKindV2.FluidPressure:
                {
                    // Stage 3.5: move and fire at the same time. The move is a
                    // tactical intent, not a hard prerequisite. This keeps enemies
                    // alive second-to-second instead of waiting for perfect slots.
                    bool moveAccepted = locomotion != null &&
                                        locomotion.SetDestination(currentOrder.targetPosition);

                    bool attackAccepted = combatExecutor != null && owner != null &&
                                          combatExecutor.BeginAttack(
                                              owner.PlayerTarget,
                                              currentOrder.patternName,
                                              currentOrder.shotsPerBurst,
                                              currentOrder.intraBurstInterval,
                                              currentOrder.burstCooldown,
                                              currentOrder.resetShooterAimSamples,
                                              currentOrder.aimLagSeconds,
                                              currentOrder.overridePatternShape,
                                              currentOrder.fanBullets,
                                              currentOrder.fanArcDegrees,
                                              currentOrder.ringBullets,
                                              currentOrder.angularSpeedDegPerTick);

                    if (!moveAccepted && !attackAccepted)
                    {
                        Finish(EnemyActionStatusV2.Failed, "Fluid pressure could not move or attack");
                        return false;
                    }

                    if (!moveAccepted)
                        debugReason = currentOrder.reason + " (attack-only: move rejected)";
                    else if (!attackAccepted)
                        debugReason = currentOrder.reason + " (move-only: attack rejected)";

                    break;
                }

                case EnemyActionKindV2.CastSkill:
                    if (!TryAcquireSupportTargetHold())
                    {
                        Finish(
                            EnemyActionStatusV2.Failed,
                            "Support target could not hold for the cast");
                        return false;
                    }
                    locomotion?.ClearDestination("Casting SkillSystemV2 skill");
                    if (skillExecutor == null ||
                        !skillExecutor.BeginSkill(
                            currentOrder.skillSpell,
                            currentOrder.skillCast,
                            currentOrder.comboReservation))
                    {
                        Finish(
                            EnemyActionStatusV2.Failed,
                            skillExecutor != null
                                ? skillExecutor.DebugResult
                                : "Skill executor missing");
                        return false;
                    }
                    break;

                case EnemyActionKindV2.ApproachAndCastSkill:
                    if (locomotion == null || skillExecutor == null ||
                        targetingSolver == null ||
                        currentOrder.skillSpell == null ||
                        currentOrder.skillApproachTarget == null)
                    {
                        Finish(
                            EnemyActionStatusV2.Failed,
                            "Approach-and-cast runtime is incomplete");
                        return false;
                    }
                    if (IsWithinApproachCastRange())
                    {
                        if (!TryStartApproachedSkill())
                            return false;
                    }
                    else if (!locomotion.SetDestination(
                                 currentOrder.skillApproachTarget
                                     .transform.position))
                    {
                        Finish(
                            EnemyActionStatusV2.Failed,
                            "Support approach destination rejected");
                        return false;
                    }
                    break;

                case EnemyActionKindV2.HoldLane:
                case EnemyActionKindV2.Guard:
                case EnemyActionKindV2.Recover:
                case EnemyActionKindV2.HoldForSupport:
                    locomotion?.ClearDestination(currentOrder.kind.ToString());
                    break;
            }

            return true;
        }

        /// <summary>
        /// Cancels an ordinary tactical order during replanning, but never a
        /// committed Support cast/approach or its coordinated target hold.
        /// Lifecycle shutdown and verified threat reactions use the explicit
        /// cancellation paths instead.
        /// </summary>
        public bool TryCancelForPlanning(string reason)
        {
            if (HasProtectedSupportCommitment)
            {
                debugLastIgnoredInterruption =
                    $"Ignored planner cancellation: {reason}";
                return false;
            }

            CancelCurrentInternal(reason);
            return true;
        }

        /// <summary>
        /// Backward-compatible tactical cancellation entry point. It obeys
        /// the same Support commitment lock as squad replanning.
        /// </summary>
        public void CancelCurrent(string reason = "Cancelled")
        {
            TryCancelForPlanning(reason);
        }

        /// <summary>
        /// Reserved for genuine runtime lifecycle changes such as disabling
        /// the agent or switching the entire AI backend off.
        /// </summary>
        public void ForceCancelCurrent(string reason)
        {
            CancelCurrentInternal(reason);
        }

        public static bool CanPlanningCancelCurrent(
            EnemyActionKindV2 currentKind,
            bool currentSpellHasSupportIntent)
        {
            return currentKind != EnemyActionKindV2.HoldForSupport &&
                   !currentSpellHasSupportIntent;
        }

        /// <summary>
        /// Protects deliberate movement and spell commitments from the next
        /// ordinary squad-planning tick. Threat evasion may still interrupt a
        /// cast, preserving the authored reaction/personality rules.
        /// </summary>
        public static bool CanReplaceCurrentAction(
            EnemyActionKindV2 current,
            EnemyActionKindV2 incoming)
        {
            // A threat reaction must remain active long enough to reach its
            // chosen safe point instead of oscillating at the hazard edge.
            if (current == EnemyActionKindV2.EvadeThreat)
                return incoming == EnemyActionKindV2.EvadeThreat;

            // Both skill paths own their buildup/channel/recovery lifecycle.
            // Previously only ApproachAndCastSkill was protected, so a skill
            // started while already in range used CastSkill and was replaced
            // before a one-second heal buildup could complete.
            if (current == EnemyActionKindV2.CastSkill ||
                current == EnemyActionKindV2.ApproachAndCastSkill)
            {
                return incoming == EnemyActionKindV2.EvadeThreat;
            }

            // A coordinated support target is deliberately stationary until
            // the support delivery resolves or its caster releases the hold.
            if (current == EnemyActionKindV2.HoldForSupport)
                return false;

            return true;
        }

        private void CancelCurrentInternal(string reason)
        {
            ReleaseHeldSupportTarget(reason);
            locomotion?.ClearDestination(reason);
            combatExecutor?.CancelAttack(reason);
            skillExecutor?.CancelSkill(reason);
            ReleaseCurrentComboReservation();

            if (debugStatus == EnemyActionStatusV2.Running)
                debugStatus = EnemyActionStatusV2.Cancelled;

            debugReason = reason;
            currentOrder = null;
            debugAction = EnemyActionKindV2.None;
            approachSkillStarted = false;
        }

        public bool TryBeginSupportHold(
            GameObject supportCaster,
            float maximumDuration)
        {
            if (supportCaster == null || supportCaster == gameObject)
                return false;

            if (IsBusy &&
                CurrentKind == EnemyActionKindV2.HoldForSupport &&
                currentOrder != null &&
                currentOrder.supportHoldOwner == supportCaster)
            {
                return true;
            }

            return AssignOrder(new EnemyActionOrderV2
            {
                orderId = -Mathf.Abs(supportCaster.GetInstanceID()),
                kind = EnemyActionKindV2.HoldForSupport,
                timeoutSeconds = Mathf.Max(0.25f, maximumDuration),
                durationSeconds = Mathf.Max(0.25f, maximumDuration),
                supportHoldOwner = supportCaster,
                reason = $"Holding for support from {supportCaster.name}"
            });
        }

        public void ReleaseSupportHold(
            GameObject supportCaster,
            string reason)
        {
            if (currentOrder == null ||
                currentOrder.kind != EnemyActionKindV2.HoldForSupport ||
                currentOrder.supportHoldOwner != supportCaster)
            {
                return;
            }

            CancelCurrentInternal(reason);
        }

        private void TickMoveToSlot()
        {
            if (locomotion == null)
            {
                Finish(EnemyActionStatusV2.Failed, "Locomotion missing");
                return;
            }

            if (locomotion.Failed)
            {
                Finish(EnemyActionStatusV2.Failed, locomotion.DebugPathStatus);
                return;
            }

            if (locomotion.HasArrived(currentOrder.arrivalRadius))
            {
                Finish(EnemyActionStatusV2.Succeeded, "Arrived at tactical slot");
                return;
            }

            if (HasTimedOut())
                Finish(EnemyActionStatusV2.Failed, "Move action timeout");
        }

        private void TickTimedAction()
        {
            float duration = Mathf.Max(0.01f, currentOrder.durationSeconds);

            if (Time.time - actionStartedAt >= duration)
                Finish(EnemyActionStatusV2.Succeeded, $"{currentOrder.kind} complete");
        }

        private void TickSupportHold()
        {
            if (currentOrder == null ||
                currentOrder.supportHoldOwner == null)
            {
                Finish(
                    EnemyActionStatusV2.Cancelled,
                    "Support caster is no longer available");
                return;
            }

            locomotion?.ClearDestination("Holding for allied support");
            if (HasTimedOut())
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    "Support hold timed out");
            }
        }

        private void TickAttack()
        {
            if (combatExecutor == null)
            {
                Finish(EnemyActionStatusV2.Failed, "Combat executor missing");
                return;
            }

            EnemyActionStatusV2 result =
                combatExecutor.TickAttack(currentOrder.timeoutSeconds);

            if (result == EnemyActionStatusV2.Succeeded)
                Finish(result, combatExecutor.DebugResult);
            else if (result == EnemyActionStatusV2.Failed)
                Finish(result, combatExecutor.DebugResult);
        }

        private void TickSkill()
        {
            if (skillExecutor == null)
            {
                Finish(EnemyActionStatusV2.Failed, "Skill executor missing");
                return;
            }

            EnemyActionStatusV2 result = skillExecutor.TickSkill(
                currentOrder.timeoutSeconds,
                Time.time - actionStartedAt);
            if (result == EnemyActionStatusV2.Succeeded ||
                result == EnemyActionStatusV2.Failed)
            {
                Finish(result, skillExecutor.DebugResult);
            }
        }

        private void TickApproachAndCastSkill()
        {
            if (currentOrder == null ||
                currentOrder.skillApproachTarget == null ||
                !currentOrder.skillApproachTarget.activeInHierarchy)
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    "Approach target is no longer available");
                return;
            }

            if (approachSkillStarted)
            {
                // Once a Support buildup begins, the validated actor target is
                // locked into the CastContext. Do not reinterpret ordinary
                // separation or squad movement as an interruption. The target
                // hold prevents intentional movement, and only verified threat
                // preemption may cancel the caster.
                if (!IsSupportAction(currentOrder))
                {
                    float permittedRange = Mathf.Max(
                        0.1f,
                        currentOrder.skillApproachRange) + 0.35f;
                    float currentDistance = Vector2.Distance(
                        transform.position,
                        currentOrder.skillApproachTarget.transform.position);
                    if (currentDistance > permittedRange)
                    {
                        skillExecutor.CancelSkill(
                            "Target left close-cast range during buildup");
                        Finish(
                            EnemyActionStatusV2.Failed,
                            "Target left close-cast range during buildup");
                        return;
                    }
                }

                EnemyActionStatusV2 result = skillExecutor.TickSkill(
                    currentOrder.skillCastTimeoutSeconds,
                    Time.time - skillStartedAt);
                if (result == EnemyActionStatusV2.Succeeded ||
                    result == EnemyActionStatusV2.Failed)
                {
                    Finish(result, skillExecutor.DebugResult);
                }
                return;
            }

            if (!SupportTargetStillNeedsSpell())
            {
                Finish(
                    EnemyActionStatusV2.Cancelled,
                    "Support target no longer needs healing");
                return;
            }

            if (locomotion == null || locomotion.Failed)
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    locomotion != null
                        ? locomotion.DebugPathStatus
                        : "Locomotion missing");
                return;
            }

            if (IsWithinApproachCastRange())
            {
                TryStartApproachedSkill();
                return;
            }

            if (Time.time >= nextApproachRefreshAt)
            {
                nextApproachRefreshAt = Time.time + 0.15f;
                if (!locomotion.SetDestination(
                        currentOrder.skillApproachTarget.transform.position))
                {
                    Finish(
                        EnemyActionStatusV2.Failed,
                        "Moving support target could not be approached");
                    return;
                }
                debugReason =
                    $"Approaching {currentOrder.skillApproachTarget.name} " +
                    $"for {currentOrder.skillSpell.DisplayName}";
            }

            if (HasTimedOut())
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    "Approach-and-cast action timed out");
            }
        }

        private bool TryStartApproachedSkill()
        {
            if (currentOrder == null || targetingSolver == null ||
                skillExecutor == null ||
                currentOrder.skillApproachTarget == null)
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    "Approach-and-cast runtime is incomplete");
                return false;
            }

            GameObject target = currentOrder.skillApproachTarget;
            if (!target.activeInHierarchy || !SupportTargetStillNeedsSpell())
            {
                Finish(
                    EnemyActionStatusV2.Cancelled,
                    "Support target became invalid before cast");
                return false;
            }

            if (!targetingSolver.TryResolveBestContext(
                    currentOrder.skillSpell,
                    target,
                    target.transform.position,
                    out CastContext resolved,
                    out _,
                    out string rejection))
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    string.IsNullOrWhiteSpace(rejection)
                        ? "Approached skill could not rebuild its cast"
                        : rejection);
                return false;
            }

            if (!TryAcquireSupportTargetHold())
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    "Support target could not hold for the cast");
                return false;
            }

            locomotion?.ClearDestination("In range; beginning skill buildup");
            if (!skillExecutor.BeginSkill(
                    currentOrder.skillSpell,
                    resolved,
                    currentOrder.comboReservation))
            {
                Finish(
                    EnemyActionStatusV2.Failed,
                    skillExecutor.DebugResult);
                return false;
            }

            currentOrder.skillCast = resolved;
            approachSkillStarted = true;
            skillStartedAt = Time.time;
            debugReason =
                $"In range; casting {currentOrder.skillSpell.DisplayName} " +
                $"on {target.name}";
            return true;
        }

        private bool IsWithinApproachCastRange()
        {
            if (currentOrder == null ||
                currentOrder.skillApproachTarget == null)
            {
                return false;
            }

            float range = Mathf.Max(
                0.1f,
                currentOrder.skillApproachRange);
            return Vector2.Distance(
                transform.position,
                currentOrder.skillApproachTarget.transform.position) <=
                range;
        }

        private bool SupportTargetStillNeedsSpell()
        {
            if (currentOrder == null || currentOrder.skillSpell == null ||
                currentOrder.skillApproachTarget == null)
            {
                return false;
            }

            SpellAIAffordance guidance =
                currentOrder.skillSpell.AIAffordance;
            if (guidance == null ||
                (guidance.Intents & SpellAIIntent.Support) == 0)
            {
                return true;
            }

            EnemyHealth health =
                currentOrder.skillApproachTarget.GetComponent<EnemyHealth>();
            return health == null ||
                   (health.CurrentHP > 0 &&
                    health.CurrentHP < health.MaxHP);
        }

        private bool TryAcquireSupportTargetHold()
        {
            if (currentOrder == null || currentOrder.skillSpell == null)
                return true;

            SpellAIAffordance guidance =
                currentOrder.skillSpell.AIAffordance;
            if (guidance == null ||
                (guidance.Intents & SpellAIIntent.Support) == 0 ||
                !guidance.RequestTargetHoldDuringSupportCast)
            {
                return true;
            }

            GameObject target = currentOrder.skillApproachTarget;
            if (target == null ||
                SpellTargetResolver.IsSameHierarchy(gameObject, target))
            {
                return true;
            }

            EnemyActionRunnerV2 targetRunner =
                target.GetComponent<EnemyActionRunnerV2>();
            if (targetRunner == null)
                return false;

            float maximumDuration = Mathf.Max(
                0.25f,
                currentOrder.skillCastTimeoutSeconds + 0.5f);
            if (!targetRunner.TryBeginSupportHold(
                    gameObject,
                    maximumDuration))
            {
                return false;
            }

            heldSupportTargetRunner = targetRunner;
            return true;
        }

        private void ReleaseHeldSupportTarget(string reason)
        {
            if (heldSupportTargetRunner == null)
                return;

            EnemyActionRunnerV2 targetRunner = heldSupportTargetRunner;
            heldSupportTargetRunner = null;
            targetRunner.ReleaseSupportHold(
                gameObject,
                reason);
        }

        private void HandleSpellPhaseChanged(SpellCastEvent castEvent)
        {
            if (castEvent.Phase == SpellCastPhase.Firing)
            {
                ReleaseHeldSupportTarget(
                    "Allied support delivery resolved");
            }
        }

        private void HandleOwnerDamaged(
            EnemyHealth health,
            int amount)
        {
            if (amount <= 0 || currentOrder == null ||
                !IsBusy || currentOrder.skillSpell == null)
            {
                return;
            }

            SpellAIAffordance guidance =
                currentOrder.skillSpell.AIAffordance;
            if (guidance == null ||
                (guidance.Intents & SpellAIIntent.Support) == 0 ||
                !guidance.InterruptSupportCastWhenDamaged)
            {
                return;
            }

            CancelCurrentInternal(
                $"Support cast interrupted by {amount} damage");
        }

        private static bool IsSupportAction(EnemyActionOrderV2 order)
        {
            if (order == null || order.skillSpell == null)
            {
                return false;
            }

            SpellAIAffordance guidance =
                order.skillSpell.AIAffordance;
            return guidance != null &&
                   (guidance.Intents & SpellAIIntent.Support) != 0;
        }

        private bool CanThreatInterruptSupportAction(
            EnemyActionOrderV2 incoming)
        {
            if (incoming == null ||
                incoming.kind != EnemyActionKindV2.EvadeThreat ||
                incoming.threatId == 0 || currentOrder == null ||
                currentOrder.skillSpell == null)
            {
                return false;
            }

            SpellAIAffordance guidance =
                currentOrder.skillSpell.AIAffordance;
            if (guidance == null ||
                !guidance.InterruptSupportCastForImminentThreat ||
                !SpellAIThreatService.IsThreatActive(incoming.threatId))
            {
                return false;
            }

            return MeetsSupportThreatInterruptThreshold(
                incoming.threatScore,
                guidance.SupportCastThreatInterruptScore,
                incoming.threatTimeToImpact,
                guidance.SupportCastThreatInterruptWindow,
                incoming.threatIsInside);
        }

        public static bool MeetsSupportThreatInterruptThreshold(
            float threatScore,
            float requiredScore,
            float timeToImpact,
            float maximumTimeToImpact,
            bool isInside)
        {
            if (Mathf.Clamp01(threatScore) <
                Mathf.Clamp01(requiredScore))
            {
                return false;
            }

            return isInside ||
                   Mathf.Max(0f, timeToImpact) <=
                   Mathf.Max(0f, maximumTimeToImpact);
        }


        private void TickFluidPressure()
        {
            if (currentOrder == null)
                return;

            bool attackStillRunning = combatExecutor != null && combatExecutor.IsRunning;

            if (attackStillRunning)
            {
                EnemyActionStatusV2 attackResult =
                    combatExecutor.TickAttack(currentOrder.timeoutSeconds);

                if (attackResult == EnemyActionStatusV2.Succeeded)
                {
                    if (ShouldKeepFluidMovementAlive())
                    {
                        debugReason = "Fluid shot complete; carrying movement momentum";
                        return;
                    }

                    Finish(EnemyActionStatusV2.Succeeded,
                        "Fluid pressure complete: " + combatExecutor.DebugResult);
                    return;
                }

                if (attackResult == EnemyActionStatusV2.Failed)
                {
                    // If the movement was accepted, the action still made the enemy
                    // visibly useful; fail so the director can choose a cleaner follow-up.
                    Finish(EnemyActionStatusV2.Failed,
                        "Fluid pressure attack failed: " + combatExecutor.DebugResult);
                    return;
                }
            }
            else if (locomotion != null && locomotion.HasDestination)
            {
                // Rare case: attack failed to start but the movement was accepted.
                // Let the enemy keep moving briefly rather than snapping idle.
                if (locomotion.Failed)
                {
                    Finish(EnemyActionStatusV2.Failed, locomotion.DebugPathStatus);
                    return;
                }

                if (locomotion.HasArrived(currentOrder.arrivalRadius))
                {
                    Finish(EnemyActionStatusV2.Succeeded, "Fluid movement reached slot");
                    return;
                }
            }
            else
            {
                Finish(EnemyActionStatusV2.Failed, "Fluid pressure lost both attack and movement");
                return;
            }

            if (HasTimedOut())
                Finish(EnemyActionStatusV2.Failed, "Fluid pressure timeout");
        }

        private bool ShouldKeepFluidMovementAlive()
        {
            if (currentOrder == null || owner == null || owner.Profile == null)
                return false;

            EnemyAIV2Profile profile = owner.Profile;

            if (!profile.fluidPressureRequiresMinimumMovementTime)
                return false;

            if (Time.time - actionStartedAt >= Mathf.Max(0f, profile.minimumFluidMovementSeconds))
                return false;

            if (locomotion == null || !locomotion.HasDestination || locomotion.Failed)
                return false;

            if (locomotion.HasArrived(currentOrder.arrivalRadius))
                return false;

            return true;
        }

        private bool HasTimedOut()
        {
            return Time.time - actionStartedAt >=
                   Mathf.Max(0.1f, currentOrder.timeoutSeconds);
        }

        private void Finish(EnemyActionStatusV2 status, string reason)
        {
            ReleaseHeldSupportTarget(reason);
            locomotion?.ClearDestination(reason);

            if (status != EnemyActionStatusV2.Running)
                combatExecutor?.CancelAttack(reason);

            if (status != EnemyActionStatusV2.Running &&
                status != EnemyActionStatusV2.Succeeded)
            {
                skillExecutor?.CancelSkill(reason);
            }

            ReleaseCurrentComboReservation();

            debugStatus = status;
            debugReason = reason;
            currentOrder = null;
            approachSkillStarted = false;
        }

        private void ReleaseCurrentComboReservation()
        {
            if (currentOrder == null)
                return;
            SpellAIComboCoordinator.ReleaseReservation(
                currentOrder.comboReservation,
                gameObject);
        }
    }
}
