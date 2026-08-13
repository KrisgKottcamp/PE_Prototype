using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemyLocomotionV2 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private ArenaNavigationGrid navigationGrid;
        [SerializeField] private EnemyAgentV2 owner;
        [SerializeField] private EnemyAIV2Profile profile;
        [SerializeField] private EnemySlowReceiverV2 slowReceiver;
        [SerializeField] private KnockbackReceiver2D knockbackReceiver;
        [SerializeField] private SpellActorMotionController2D forcedMotion;

        [Header("Runtime Debug")]
        [SerializeField] private bool hasDestination;
        [SerializeField] private Vector2 destination;
        [SerializeField] private string debugPathStatus = "Idle";
        [SerializeField] private int debugRemainingWaypoints;
        [SerializeField] private int debugMovementFailures;
        [SerializeField] private string debugRecovery = "None";
        [SerializeField] private string debugLastFailureReason = "None";
        [SerializeField] private float debugBodySeparationMagnitude;
        [SerializeField] private float debugLeaseSeparationMagnitude;
        [SerializeField] private float debugRemainingDistance;

        [Header("Runtime Debug - Motion Feel v3.5.2")]
        [SerializeField] private Vector2 debugCurrentVelocity;
        [SerializeField] private float debugCurrentSpeed;
        [SerializeField] private string debugMotionMode = "Idle";

        [Header("Runtime Debug - Slow Zone v4.4")]
        [SerializeField] private bool debugIsSlowed;
        [SerializeField] private float debugSlowMovementMultiplier = 1f;
        [SerializeField] private string debugSlowSource = "None";

        private readonly List<Vector2> path = new List<Vector2>();
        private int pathIndex;
        private float nextPathRefreshTime;
        private float lastProgressTime;
        private float bestRemainingDistance = float.PositiveInfinity;
        private bool failed;
        private Vector2 temporaryRecoveryTarget;
        private bool usingRecoveryTarget;
        private float failedDestinationUntil;
        private Vector2 failedDestination;
        private bool alreadyRetriedSnappedDestination;
        private Vector2 currentVelocity;

        public bool HasDestination => hasDestination;
        public bool Failed => failed;
        public string DebugPathStatus => debugPathStatus;
        public string DebugRecovery => debugRecovery;
        public string DebugLastFailureReason => debugLastFailureReason;
        public int MovementFailureCount => debugMovementFailures;
        public Vector2 Destination => destination;
        public Vector2 CurrentVelocity => currentVelocity;
        public float CurrentSpeed => currentVelocity.magnitude;
        public bool IsSlowed => debugIsSlowed;
        public float SlowMovementMultiplier => debugSlowMovementMultiplier;
        public float DistanceToDestination =>
            hasDestination
                ? Vector2.Distance(CurrentPosition, destination)
                : 0f;

        private Vector2 CurrentPosition =>
            body != null
                ? body.position
                : (Vector2)transform.position;

        private float EffectiveMoveSpeed(float baseSpeed)
        {
            float speed = baseSpeed;

            if (profile != null && profile.useCombatTempoScaling)
                speed *= Mathf.Clamp(profile.enemyMovementTempoMultiplier, 0.45f, 1.25f);

            speed *= GetSlowMovementMultiplier();
            speed *= SpellStatModifierUtility.Evaluate(
                gameObject,
                SpellActorStat.MovementSpeed,
                1f);
            return speed;
        }

        private float GetSlowMovementMultiplier()
        {
            if (profile == null || !profile.respectSlowZones || slowReceiver == null)
                return 1f;

            return Mathf.Clamp(slowReceiver.MovementSpeedMultiplier, 0.02f, 5f);
        }

        private void RefreshSlowDebug()
        {
            if (profile == null || !profile.respectSlowZones || slowReceiver == null)
            {
                debugIsSlowed = false;
                debugSlowMovementMultiplier = 1f;
                debugSlowSource = "None";
                return;
            }

            debugSlowMovementMultiplier = Mathf.Clamp(slowReceiver.MovementSpeedMultiplier, 0.02f, 5f);
            debugIsSlowed = slowReceiver.IsSlowed;
            debugSlowSource = slowReceiver.DebugStrongestSource;
        }

        private void Awake()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();

            if (owner == null)
                owner = GetComponent<EnemyAgentV2>();

            if (slowReceiver == null)
                slowReceiver = GetComponent<EnemySlowReceiverV2>();

            if (knockbackReceiver == null)
                knockbackReceiver = GetComponent<KnockbackReceiver2D>();

            if (forcedMotion == null)
                forcedMotion = GetComponent<SpellActorMotionController2D>();

            if (navigationGrid == null)
                navigationGrid = FindObjectOfType<ArenaNavigationGrid>(true);
        }

        private void FixedUpdate()
        {
            RefreshSlowDebug();
            debugCurrentVelocity = currentVelocity;
            debugCurrentSpeed = currentVelocity.magnitude;

            if (knockbackReceiver == null)
                knockbackReceiver = GetComponent<KnockbackReceiver2D>();

            if (forcedMotion == null)
                forcedMotion = GetComponent<SpellActorMotionController2D>();

            if (forcedMotion != null && forcedMotion.IsControllingMotion)
            {
                currentVelocity = Vector2.zero;
                debugCurrentVelocity = Vector2.zero;
                debugCurrentSpeed = 0f;
                debugMotionMode = "Spell Forced Movement";
                return;
            }

            if (knockbackReceiver != null &&
                knockbackReceiver.IsKnockbackActive)
            {
                currentVelocity = Vector2.zero;
                debugCurrentVelocity = Vector2.zero;
                debugCurrentSpeed = 0f;
                debugMotionMode = "Knockback";
                return;
            }

            if (profile == null)
            {
                ApplySoftStop(Mathf.Max(0.05f, Time.fixedDeltaTime));
                return;
            }

            if (!hasDestination || failed)
            {
                ApplySoftStop(Time.fixedDeltaTime);
                return;
            }

            debugRemainingDistance = Vector2.Distance(CurrentPosition, destination);

            if (usingRecoveryTarget)
            {
                TickMoveTowards(temporaryRecoveryTarget, EffectiveMoveSpeed(profile.moveSpeed * 0.85f), Time.fixedDeltaTime);

                if (Vector2.Distance(CurrentPosition, temporaryRecoveryTarget) <= 0.16f)
                {
                    usingRecoveryTarget = false;
                    nextPathRefreshTime = 0f;
                    debugRecovery = "Sidestep completed";
                }

                return;
            }

            RefreshPathIfNeeded();

            if (failed)
            {
                ApplySoftStop(Time.fixedDeltaTime);
                return;
            }

            Vector2 moveTarget = destination;

            if (pathIndex < path.Count)
            {
                moveTarget = path[pathIndex];

                if (Vector2.Distance(CurrentPosition, moveTarget) <=
                    Mathf.Max(0.05f, profile.waypointArrivalRadius))
                {
                    pathIndex++;

                    if (pathIndex < path.Count)
                        moveTarget = path[pathIndex];
                    else
                        moveTarget = destination;
                }
            }

            TickMoveTowards(moveTarget, EffectiveMoveSpeed(profile.moveSpeed), Time.fixedDeltaTime);
            TickProgressWatchdog();

            debugRemainingWaypoints = Mathf.Max(0, path.Count - pathIndex);
        }

        public void Configure(
            EnemyAIV2Profile newProfile,
            ArenaNavigationGrid grid,
            EnemyAgentV2 newOwner)
        {
            profile = newProfile;
            navigationGrid = grid;
            owner = newOwner;

            if (slowReceiver == null)
                slowReceiver = GetComponent<EnemySlowReceiverV2>();
        }

        public bool SetDestination(Vector2 newDestination)
        {
            if (profile == null)
                return false;

            if (IsDestinationRecentlyFailed(newDestination, 0.35f))
            {
                debugPathStatus = "Rejected recently failed destination";
                failed = true;
                debugLastFailureReason = debugPathStatus;
                return false;
            }

            if (navigationGrid != null && navigationGrid.IsBuilt)
                newDestination = navigationGrid.FindNearestWalkablePosition(newDestination);

            destination = newDestination;
            hasDestination = true;
            failed = false;
            usingRecoveryTarget = false;
            alreadyRetriedSnappedDestination = false;
            debugMovementFailures = 0;
            debugRecovery = "None";
            debugLastFailureReason = "None";
            path.Clear();
            pathIndex = 0;
            nextPathRefreshTime = 0f;
            bestRemainingDistance = Vector2.Distance(CurrentPosition, destination);
            lastProgressTime = Time.time;
            debugPathStatus = "Destination assigned";
            debugMotionMode = "Accelerating to destination";
            return true;
        }

        public bool IsDestinationRecentlyFailed(Vector2 position, float radius)
        {
            return Time.time < failedDestinationUntil &&
                   Vector2.Distance(position, failedDestination) <= Mathf.Max(0.01f, radius);
        }

        public bool HasArrived(float arrivalRadius)
        {
            if (!hasDestination)
                return false;

            return Vector2.Distance(CurrentPosition, destination) <=
                   Mathf.Max(0.05f, arrivalRadius);
        }

        public void ClearDestination(string reason = "Cleared", bool hardStop = false)
        {
            hasDestination = false;
            failed = false;
            usingRecoveryTarget = false;
            alreadyRetriedSnappedDestination = false;
            path.Clear();
            pathIndex = 0;
            debugPathStatus = reason;
            debugMotionMode = hardStop ? "Hard stop" : "Coasting / decelerating";

            if (hardStop)
                StopBodyImmediate();
        }

        private void RefreshPathIfNeeded()
        {
            if (Time.time < nextPathRefreshTime)
                return;

            nextPathRefreshTime =
                Time.time + Mathf.Max(0.05f, profile.pathRefreshSeconds);

            path.Clear();
            pathIndex = 0;

            if (navigationGrid != null && navigationGrid.IsBuilt)
            {
                if (navigationGrid.TryFindPath(CurrentPosition, destination, path))
                {
                    debugPathStatus = path.Count > 0
                        ? $"Path {path.Count} waypoint(s)"
                        : "Direct destination";
                    return;
                }

                if (navigationGrid.HasClearPath(CurrentPosition, destination))
                {
                    debugPathStatus = "Direct clear path";
                    return;
                }

                if (profile.retrySnappedDestinationBeforeFailing && !alreadyRetriedSnappedDestination)
                {
                    Vector2 snapped = navigationGrid.FindNearestWalkablePosition(destination);
                    alreadyRetriedSnappedDestination = true;

                    if (Vector2.Distance(snapped, destination) > 0.05f)
                    {
                        destination = snapped;
                        debugRecovery = "Retried snapped destination";
                        debugPathStatus = debugRecovery;
                        nextPathRefreshTime = 0f;
                        return;
                    }
                }

                RegisterMovementFailure("No path");
                return;
            }

            debugPathStatus = "No built grid: direct movement";
        }

        private void TickMoveTowards(Vector2 target, float speed, float deltaTime)
        {
            Vector2 delta = target - CurrentPosition;

            if (delta.sqrMagnitude <= 0.0001f)
            {
                ApplySoftStop(deltaTime);
                return;
            }

            Vector2 direction = delta.normalized;

            Vector2 bodySeparation = ComputeBodySeparationDirection();
            Vector2 leaseSeparation = owner != null && owner.Director != null
                ? owner.Director.GetDestinationSeparationDirection(owner, CurrentPosition)
                : Vector2.zero;

            debugBodySeparationMagnitude = bodySeparation.magnitude;
            debugLeaseSeparationMagnitude = leaseSeparation.magnitude;

            direction += bodySeparation * profile.separationWeight;
            direction += leaseSeparation * profile.assignedSlotRepulsionWeight;

            if (direction.sqrMagnitude > 0.0001f)
                direction.Normalize();

            float desiredSpeed = Mathf.Max(0.1f, speed);
            float acceleration = Mathf.Max(0.1f, profile.motionAcceleration);
            float turnSharpness = Mathf.Max(0.1f, profile.motionTurnSharpness);

            Vector2 desiredVelocity = direction * desiredSpeed;

            // Stage 3.5.2: the reference mixed-AI clip felt better because
            // enemies accelerated, curved, and decelerated instead of snapping
            // between stop/start MovePosition steps. This intentionally keeps
            // that arcade skirmish feel inside pure V2 control.
            float angle = currentVelocity.sqrMagnitude > 0.0001f
                ? Vector2.Angle(currentVelocity.normalized, desiredVelocity.normalized)
                : 0f;
            float turnPenalty = Mathf.Lerp(1f, Mathf.Clamp01(turnSharpness), angle / 180f);
            float maxDelta = acceleration * Mathf.Max(0.15f, turnPenalty) * deltaTime;
            currentVelocity = Vector2.MoveTowards(currentVelocity, desiredVelocity, maxDelta);
            ApplySlowVelocityClamp(desiredSpeed, deltaTime);

            // Prevent overshooting tiny final targets while still allowing
            // visible easing into position.
            if (delta.magnitude < Mathf.Max(0.08f, profile.motionArrivalBrakeDistance))
            {
                float brakeFactor = Mathf.Clamp01(delta.magnitude / Mathf.Max(0.01f, profile.motionArrivalBrakeDistance));
                currentVelocity *= Mathf.Lerp(0.25f, 1f, brakeFactor);
            }

            MoveBody(CurrentPosition + currentVelocity * deltaTime);
            debugMotionMode = "Velocity chase";
        }

        private void ApplySlowVelocityClamp(float slowedDesiredSpeed, float deltaTime)
        {
            if (profile == null || !profile.respectSlowZones || !profile.slowZoneClampVelocity || !debugIsSlowed)
                return;

            float currentSpeed = currentVelocity.magnitude;
            if (currentSpeed <= 0.0001f)
                return;

            float allowedSpeed = Mathf.Max(
                0.03f,
                slowedDesiredSpeed * Mathf.Max(1f, profile.slowZoneVelocityOvershootAllowance)
            );

            if (currentSpeed <= allowedSpeed)
                return;

            float newSpeed = Mathf.MoveTowards(
                currentSpeed,
                allowedSpeed,
                Mathf.Max(0.1f, profile.slowZoneEntryBrake) * deltaTime
            );

            currentVelocity = currentVelocity.normalized * newSpeed;
            debugMotionMode = "Slow-zone velocity clamp";

            if (profile.logSlowZoneEffects)
            {
                Debug.Log(
                    $"[Enemy AI V2] {name}: slow zone clamped speed {currentSpeed:0.00}->{newSpeed:0.00} source={debugSlowSource}",
                    this
                );
            }
        }

        private void ApplySoftStop(float deltaTime)
        {
            if (currentVelocity.sqrMagnitude <= 0.000001f)
            {
                currentVelocity = Vector2.zero;
                StopRigidbodyVelocityOnly();
                debugMotionMode = "Idle";
                return;
            }

            float deceleration = profile != null
                ? Mathf.Max(0.1f, profile.motionDeceleration)
                : 18f;

            if (profile != null && debugIsSlowed)
                deceleration *= Mathf.Max(1f, profile.slowZoneCoastBrakeMultiplier);

            currentVelocity = Vector2.MoveTowards(
                currentVelocity,
                Vector2.zero,
                deceleration * deltaTime
            );

            MoveBody(CurrentPosition + currentVelocity * deltaTime);
            debugMotionMode = "Decelerating";
        }

        private void MoveBody(Vector2 next)
        {
            if (body != null)
                body.MovePosition(next);
            else
                transform.position = next;
        }

        private Vector2 ComputeBodySeparationDirection()
        {
            if (owner == null || owner.Director == null)
                return Vector2.zero;

            IReadOnlyList<EnemyAgentV2> agents = owner.Director.Agents;
            Vector2 result = Vector2.zero;
            Vector2 current = CurrentPosition;
            float radius = Mathf.Max(0.05f, profile.separationRadius);

            for (int i = 0; i < agents.Count; i++)
            {
                EnemyAgentV2 other = agents[i];

                if (other == null || other == owner || !other.IsAlive)
                    continue;

                Vector2 away = current - (Vector2)other.transform.position;
                float distance = away.magnitude;

                if (distance <= 0.001f || distance >= radius)
                    continue;

                result += away.normalized * (1f - distance / radius);
            }

            return result;
        }

        private void TickProgressWatchdog()
        {
            float remaining = Vector2.Distance(CurrentPosition, destination);

            if (remaining <= bestRemainingDistance -
                Mathf.Max(0.01f, profile.progressRequiredDistance))
            {
                bestRemainingDistance = remaining;
                lastProgressTime = Time.time;
                return;
            }

            if (Time.time - lastProgressTime <
                Mathf.Max(0.1f, profile.progressTimeoutSeconds))
            {
                return;
            }

            RegisterMovementFailure("Progress timeout");
        }

        private void RegisterMovementFailure(string reason)
        {
            debugMovementFailures++;
            debugLastFailureReason = reason;
            lastProgressTime = Time.time;
            bestRemainingDistance = Vector2.Distance(CurrentPosition, destination);

            if (debugMovementFailures < Mathf.Max(1, profile.maximumMovementFailures))
            {
                Vector2 toDestination = destination - CurrentPosition;
                Vector2 perpendicular =
                    toDestination.sqrMagnitude > 0.0001f
                        ? new Vector2(-toDestination.y, toDestination.x).normalized
                        : Vector2.right;

                int side = (debugMovementFailures % 2 == 0) ? -1 : 1;
                float sidestepDistance = Mathf.Max(0.05f, profile.recoverySidestepDistance);

                if (profile.useWiderSecondSidestep && debugMovementFailures >= 2)
                    sidestepDistance *= Mathf.Max(1f, profile.secondSidestepMultiplier);

                temporaryRecoveryTarget =
                    CurrentPosition +
                    perpendicular * side * sidestepDistance;

                if (navigationGrid != null && navigationGrid.IsBuilt)
                {
                    temporaryRecoveryTarget =
                        navigationGrid.FindNearestWalkablePosition(temporaryRecoveryTarget);
                }

                usingRecoveryTarget = true;
                path.Clear();
                pathIndex = 0;
                nextPathRefreshTime = 0f;
                debugRecovery = $"Sidestep {debugMovementFailures}: {reason}";
                debugPathStatus = debugRecovery;
                return;
            }

            failed = true;
            failedDestination = destination;
            failedDestinationUntil =
                Time.time +
                Mathf.Max(0.1f, profile.failedDestinationMemorySeconds);
            debugRecovery = $"Failed after {debugMovementFailures}: {reason}";
            debugPathStatus = debugRecovery;
            ApplySoftStop(Time.fixedDeltaTime);
        }

        private void StopBodyImmediate()
        {
            currentVelocity = Vector2.zero;
            StopRigidbodyVelocityOnly();
        }

        private void StopRigidbodyVelocityOnly()
        {
            if (body == null)
                return;

#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = Vector2.zero;
#else
            body.velocity = Vector2.zero;
#endif
        }
    }
}
