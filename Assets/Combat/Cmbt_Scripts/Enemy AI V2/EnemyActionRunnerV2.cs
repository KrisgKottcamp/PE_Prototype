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

        [Header("Runtime Debug")]
        [SerializeField] private EnemyActionKindV2 debugAction = EnemyActionKindV2.None;
        [SerializeField] private EnemyActionStatusV2 debugStatus = EnemyActionStatusV2.Idle;
        [SerializeField] private string debugReason = "Idle";
        [SerializeField] private int debugOrderId;
        [SerializeField] private float debugActionElapsed;

        private EnemyActionOrderV2 currentOrder;
        private float actionStartedAt;

        public EnemyActionKindV2 CurrentKind => debugAction;
        public EnemyActionStatusV2 Status => debugStatus;
        public string DebugReason => debugReason;
        public int CurrentOrderId => debugOrderId;
        public bool IsBusy => debugStatus == EnemyActionStatusV2.Running;

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
        }

        private void Update()
        {
            if (debugStatus != EnemyActionStatusV2.Running || currentOrder == null)
                return;

            debugActionElapsed = Time.time - actionStartedAt;

            switch (currentOrder.kind)
            {
                case EnemyActionKindV2.MoveToSlot:
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

            CancelCurrent("Replaced by new order");

            currentOrder = order.Clone();
            debugOrderId = currentOrder.orderId;
            debugAction = currentOrder.kind;
            debugStatus = EnemyActionStatusV2.Running;
            debugReason = currentOrder.reason;
            debugActionElapsed = 0f;
            actionStartedAt = Time.time;

            switch (currentOrder.kind)
            {
                case EnemyActionKindV2.MoveToSlot:
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
                    locomotion?.ClearDestination("Casting SkillSystemV2 skill");
                    if (skillExecutor == null ||
                        !skillExecutor.BeginSkill(
                            currentOrder.skillSpell,
                            currentOrder.skillCast))
                    {
                        Finish(
                            EnemyActionStatusV2.Failed,
                            skillExecutor != null
                                ? skillExecutor.DebugResult
                                : "Skill executor missing");
                        return false;
                    }
                    break;

                case EnemyActionKindV2.HoldLane:
                case EnemyActionKindV2.Guard:
                case EnemyActionKindV2.Recover:
                    locomotion?.ClearDestination(currentOrder.kind.ToString());
                    break;
            }

            return true;
        }

        public void CancelCurrent(string reason = "Cancelled")
        {
            locomotion?.ClearDestination(reason);
            combatExecutor?.CancelAttack(reason);
            skillExecutor?.CancelSkill(reason);

            if (debugStatus == EnemyActionStatusV2.Running)
                debugStatus = EnemyActionStatusV2.Cancelled;

            debugReason = reason;
            currentOrder = null;
            debugAction = EnemyActionKindV2.None;
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
            locomotion?.ClearDestination(reason);

            if (status != EnemyActionStatusV2.Running)
                combatExecutor?.CancelAttack(reason);

            if (status != EnemyActionStatusV2.Running &&
                status != EnemyActionStatusV2.Succeeded)
            {
                skillExecutor?.CancelSkill(reason);
            }

            debugStatus = status;
            debugReason = reason;
            currentOrder = null;
        }
    }
}
