using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Smooth, path-aware locomotion shared by Eri's normal tactical movement and
/// healing response. Destinations may change often, but a path is only rebuilt
/// when the requested point changes enough to matter.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EriTacticalLocomotion : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private ArenaNavigationGrid navigationGrid;

    [Header("Motion Feel")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 5.2f;
    [SerializeField, Min(0.1f)] private float acceleration = 18f;
    [SerializeField, Min(0.1f)] private float deceleration = 22f;
    [SerializeField, Range(0.1f, 1f)] private float turnSharpness = 0.62f;
    [SerializeField, Min(0.05f)] private float arrivalBrakeDistance = 0.9f;
    [SerializeField, Min(0.02f)] private float arrivalDistance = 0.16f;

    [Header("Pathing")]
    [SerializeField, Min(0.05f)] private float pathRefreshSeconds = 0.32f;
    [SerializeField, Min(0.02f)] private float waypointArrivalDistance = 0.15f;
    [SerializeField, Min(0.05f)] private float destinationChangeThreshold = 0.32f;

    [Header("Danger-Aware Routing")]
    [Tooltip(
        "0 follows the quickest route. 1 strongly favors routes that keep " +
        "Eri away from enemies. A value around 0.65 is a good safety-first " +
        "balance without making her take absurd detours."
    )]
    [SerializeField, Range(0f, 1f)] private float routeSafetyPriority = 0.67f;

    [Tooltip("Enemies inside this radius add danger cost to a route.")]
    [SerializeField, Min(0.2f)] private float routeThreatRadius = 3.8f;

    [Tooltip(
        "Routes passing this close to an enemy receive a very large penalty."
    )]
    [SerializeField, Min(0.05f)] private float routeCriticalDistance = 1.55f;

    [Header("Stuck Recovery")]
    [SerializeField, Min(0.05f)] private float requiredProgress = 0.08f;
    [SerializeField, Min(0.1f)] private float progressTimeoutSeconds = 0.75f;
    [SerializeField, Range(1, 6)] private int maximumRecoveryAttempts = 4;
    [SerializeField, Min(0.1f)] private float recoverySidestepDistance = 0.9f;
    [SerializeField, Min(0.1f)] private float failedDestinationMemorySeconds = 1.5f;

    [Header("Runtime Debug")]
    [SerializeField] private bool hasDestination;
    [SerializeField] private Vector2 destination;
    [SerializeField] private Vector2 currentVelocity;
    [SerializeField] private string pathStatus = "Idle";
    [SerializeField] private string recoveryStatus = "None";
    [SerializeField] private int recoveryAttempts;
    [SerializeField] private bool needsNewDestination;
    [SerializeField] private int trackedRouteThreats;

    private readonly List<Vector2> path = new List<Vector2>();
    private readonly List<Vector2> routeThreatPositions =
        new List<Vector2>();
    private int pathIndex;
    private float speedMultiplier = 1f;
    private float nextPathRefreshTime;
    private float lastProgressTime;
    private float bestRemainingDistance = float.PositiveInfinity;

    private bool usingRecoveryTarget;
    private Vector2 recoveryTarget;
    private Vector2 failedDestination;
    private float failedDestinationUntil;

    public bool HasDestination => hasDestination;
    public bool NeedsNewDestination => needsNewDestination;
    public Vector2 Destination => destination;
    public Vector2 CurrentVelocity => currentVelocity;
    public float CurrentSpeed => currentVelocity.magnitude;
    public string PathStatus => pathStatus;
    public string RecoveryStatus => recoveryStatus;

    private Vector2 CurrentPosition =>
        body != null
            ? body.position
            : (Vector2)transform.position;

    private void Awake()
    {
        if (body == null)
            body = GetComponent<Rigidbody2D>();

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<ArenaNavigationGrid>(true);
        }
    }

    public void Configure(
        Rigidbody2D newBody,
        ArenaNavigationGrid grid)
    {
        if (newBody != null)
            body = newBody;

        if (grid != null)
            navigationGrid = grid;
    }

    public void ConfigureDangerRouting(
        float safetyPriority,
        float threatRadius,
        float criticalDistance)
    {
        routeSafetyPriority =
            Mathf.Clamp01(safetyPriority);
        routeThreatRadius =
            Mathf.Max(0.2f, threatRadius);
        routeCriticalDistance =
            Mathf.Clamp(
                criticalDistance,
                0.05f,
                routeThreatRadius
            );
    }

    public void SetRouteThreats(
        IReadOnlyList<Vector2> threatPositions)
    {
        routeThreatPositions.Clear();

        if (threatPositions != null)
        {
            for (int i = 0;
                 i < threatPositions.Count;
                 i++)
            {
                routeThreatPositions.Add(
                    threatPositions[i]
                );
            }
        }

        trackedRouteThreats =
            routeThreatPositions.Count;
    }

    public bool SetDestination(
        Vector2 requestedDestination,
        float requestedSpeedMultiplier = 1f,
        bool forceRefresh = false)
    {
        requestedSpeedMultiplier =
            Mathf.Clamp(requestedSpeedMultiplier, 0.35f, 1.5f);

        if (navigationGrid != null &&
            navigationGrid.IsBuilt)
        {
            requestedDestination =
                navigationGrid.FindNearestWalkablePosition(
                    requestedDestination
                );
        }

        bool destinationChanged =
            !hasDestination ||
            Vector2.Distance(
                destination,
                requestedDestination
            ) >= destinationChangeThreshold;

        speedMultiplier = requestedSpeedMultiplier;

        if (!destinationChanged && !forceRefresh)
            return true;

        if (Time.time < failedDestinationUntil &&
            Vector2.Distance(
                requestedDestination,
                failedDestination
            ) <= destinationChangeThreshold)
        {
            needsNewDestination = true;
            pathStatus = "Rejected recently failed destination";
            return false;
        }

        destination = requestedDestination;
        hasDestination = true;
        needsNewDestination = false;
        usingRecoveryTarget = false;
        recoveryAttempts = 0;
        recoveryStatus = "None";

        path.Clear();
        pathIndex = 0;
        nextPathRefreshTime = 0f;
        bestRemainingDistance =
            Vector2.Distance(CurrentPosition, destination);
        lastProgressTime = Time.time;
        pathStatus = "Destination assigned";
        return true;
    }

    public void ClearDestination(
        string reason = "Cleared",
        bool hardStop = false)
    {
        hasDestination = false;
        needsNewDestination = false;
        usingRecoveryTarget = false;
        recoveryAttempts = 0;
        path.Clear();
        pathIndex = 0;
        pathStatus = reason;
        recoveryStatus = "None";

        if (hardStop)
        {
            currentVelocity = Vector2.zero;
            StopRigidbodyVelocity();
        }
    }

    public bool HasArrived(float radius = -1f)
    {
        if (!hasDestination)
            return false;

        float acceptedRadius =
            radius > 0f
                ? radius
                : arrivalDistance;

        return Vector2.Distance(
            CurrentPosition,
            destination
        ) <= Mathf.Max(0.02f, acceptedRadius);
    }

    public bool IsDestinationRecentlyFailed(
        Vector2 position,
        float radius = 0.5f)
    {
        return Time.time < failedDestinationUntil &&
               Vector2.Distance(
                   position,
                   failedDestination
               ) <= Mathf.Max(0.05f, radius);
    }

    public void ForgetFailedDestination()
    {
        failedDestinationUntil = 0f;
        needsNewDestination = false;
    }

    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        if (body == null)
            return;

        if (!body.simulated)
        {
            currentVelocity = Vector2.zero;
            return;
        }

        if (!hasDestination ||
            needsNewDestination)
        {
            ApplySoftStop(deltaTime);
            return;
        }

        if (usingRecoveryTarget)
        {
            TickMoveTowards(
                recoveryTarget,
                moveSpeed * speedMultiplier * 0.9f,
                deltaTime
            );

            if (Vector2.Distance(
                    CurrentPosition,
                    recoveryTarget) <=
                waypointArrivalDistance)
            {
                usingRecoveryTarget = false;
                nextPathRefreshTime = 0f;
                lastProgressTime = Time.time;
                recoveryStatus = "Sidestep complete";
            }

            return;
        }

        RefreshPathIfNeeded();

        if (needsNewDestination)
        {
            ApplySoftStop(deltaTime);
            return;
        }

        Vector2 target = destination;

        while (pathIndex < path.Count &&
               Vector2.Distance(
                   CurrentPosition,
                   path[pathIndex]) <=
               waypointArrivalDistance)
        {
            pathIndex++;
        }

        if (pathIndex < path.Count)
            target = path[pathIndex];

        TickMoveTowards(
            target,
            moveSpeed * speedMultiplier,
            deltaTime
        );

        TickProgressWatchdog();
    }

    private void RefreshPathIfNeeded()
    {
        if (Time.time < nextPathRefreshTime)
            return;

        nextPathRefreshTime =
            Time.time + pathRefreshSeconds;

        path.Clear();
        pathIndex = 0;

        if (navigationGrid == null ||
            !navigationGrid.IsBuilt)
        {
            pathStatus = "No built grid: direct movement";
            return;
        }

        bool useDangerAwareRoute =
            routeThreatPositions.Count > 0 &&
            routeSafetyPriority > 0.001f;

        bool foundPath =
            useDangerAwareRoute
                ? navigationGrid.TryFindPathAvoidingThreats(
                    CurrentPosition,
                    destination,
                    routeThreatPositions,
                    routeSafetyPriority,
                    routeThreatRadius,
                    routeCriticalDistance,
                    path
                )
                : navigationGrid.TryFindPath(
                    CurrentPosition,
                    destination,
                    path
                );

        if (foundPath)
        {
            pathStatus =
                path.Count > 0
                    ? useDangerAwareRoute
                        ? $"Safe/fast path {path.Count} waypoint(s)"
                        : $"Path {path.Count} waypoint(s)"
                    : "Direct destination";
            return;
        }

        if (navigationGrid.HasClearPath(
                CurrentPosition,
                destination))
        {
            pathStatus = "Direct clear path";
            return;
        }

        RegisterMovementFailure("No path");
    }

    private void TickMoveTowards(
        Vector2 target,
        float targetSpeed,
        float deltaTime)
    {
        Vector2 delta = target - CurrentPosition;

        if (delta.sqrMagnitude <= 0.0001f)
        {
            ApplySoftStop(deltaTime);
            return;
        }

        Vector2 desiredVelocity =
            delta.normalized *
            Mathf.Max(0.1f, targetSpeed);

        float angle =
            currentVelocity.sqrMagnitude > 0.0001f
                ? Vector2.Angle(
                    currentVelocity.normalized,
                    desiredVelocity.normalized)
                : 0f;

        float turnPenalty =
            Mathf.Lerp(
                1f,
                turnSharpness,
                angle / 180f
            );

        currentVelocity =
            Vector2.MoveTowards(
                currentVelocity,
                desiredVelocity,
                acceleration *
                Mathf.Max(0.15f, turnPenalty) *
                deltaTime
            );

        float distanceToFinal =
            Vector2.Distance(
                CurrentPosition,
                destination
            );

        if (distanceToFinal < arrivalBrakeDistance)
        {
            float brake =
                Mathf.Clamp01(
                    distanceToFinal /
                    Mathf.Max(0.01f, arrivalBrakeDistance)
                );

            currentVelocity *=
                Mathf.Lerp(0.22f, 1f, brake);
        }

        MoveBody(
            CurrentPosition +
            currentVelocity * deltaTime
        );
    }

    private void ApplySoftStop(float deltaTime)
    {
        if (currentVelocity.sqrMagnitude <= 0.000001f)
        {
            currentVelocity = Vector2.zero;
            StopRigidbodyVelocity();
            return;
        }

        currentVelocity =
            Vector2.MoveTowards(
                currentVelocity,
                Vector2.zero,
                deceleration * deltaTime
            );

        MoveBody(
            CurrentPosition +
            currentVelocity * deltaTime
        );
    }

    private void TickProgressWatchdog()
    {
        if (HasArrived(arrivalDistance))
        {
            lastProgressTime = Time.time;
            bestRemainingDistance = 0f;
            return;
        }

        float remaining =
            Vector2.Distance(
                CurrentPosition,
                destination
            );

        if (remaining <=
            bestRemainingDistance - requiredProgress)
        {
            bestRemainingDistance = remaining;
            lastProgressTime = Time.time;
            return;
        }

        if (Time.time - lastProgressTime <
            progressTimeoutSeconds)
        {
            return;
        }

        RegisterMovementFailure("Progress timeout");
    }

    private void RegisterMovementFailure(string reason)
    {
        recoveryAttempts++;
        lastProgressTime = Time.time;
        bestRemainingDistance =
            Vector2.Distance(
                CurrentPosition,
                destination
            );

        if (recoveryAttempts <=
            maximumRecoveryAttempts)
        {
            Vector2 toDestination =
                destination - CurrentPosition;

            Vector2 perpendicular =
                toDestination.sqrMagnitude > 0.0001f
                    ? new Vector2(
                        -toDestination.y,
                        toDestination.x
                    ).normalized
                    : Vector2.right;

            int side =
                recoveryAttempts % 2 == 0
                    ? -1
                    : 1;

            float width =
                recoverySidestepDistance *
                (1f + 0.35f *
                    Mathf.Max(0, recoveryAttempts - 1));

            recoveryTarget =
                CurrentPosition +
                perpendicular * side * width;

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                recoveryTarget =
                    navigationGrid.FindNearestWalkablePosition(
                        recoveryTarget
                    );
            }

            usingRecoveryTarget = true;
            path.Clear();
            pathIndex = 0;
            nextPathRefreshTime = 0f;
            recoveryStatus =
                $"Sidestep {recoveryAttempts}: {reason}";
            pathStatus = recoveryStatus;
            return;
        }

        failedDestination = destination;
        failedDestinationUntil =
            Time.time +
            failedDestinationMemorySeconds;
        needsNewDestination = true;
        hasDestination = false;
        path.Clear();
        recoveryStatus =
            $"Requested new slot: {reason}";
        pathStatus = recoveryStatus;
    }

    private void MoveBody(Vector2 next)
    {
        if (body != null)
            body.MovePosition(next);
        else
            transform.position = next;
    }

    private void StopRigidbodyVelocity()
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
