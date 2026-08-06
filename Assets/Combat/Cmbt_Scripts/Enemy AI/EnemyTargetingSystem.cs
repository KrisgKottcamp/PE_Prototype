using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides 5 targeting modes for enemy abilities.
/// Attach to any enemy that needs targeting beyond raw position.
/// Call GetTargetPoint(mode) from EnemyBrain or EnemyShooterDebug.
/// </summary>
public class EnemyTargetingSystem : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";

    [Header("Average Mode")]
    [Tooltip("How many seconds of position history to average over.")]
    [SerializeField] private float averageWindowSeconds = 5f;
    [Tooltip("How often a position sample is recorded.")]
    [SerializeField] private float sampleInterval = 0.05f;

    [Header("Predicted Mode")]
    [Tooltip("Number of recent frames used to estimate player velocity.")]
    [SerializeField] private int velocityFrameCount = 8;

    [Header("Suppress Mode")]
    [SerializeField] private LayerMask coverMask;
    [Tooltip("Maximum angle offset from direct aim when suppressing (degrees).")]
    [SerializeField] private float maxSuppressAngleDeg = 25f;
    [Tooltip("Number of raycasts used to find the cover edge.")]
    [SerializeField] private int suppressRayCount = 12;
    [Tooltip("Only suppress when exactly one layer of cover blocks LOS.")]
    [SerializeField] private bool requireSingleCoverLayer = true;

    [Header("Select Mode")]
    [Tooltip("The currently selected entity for non-projectile abilities.")]
    [SerializeField] private Transform selectedEntity;

    [Header("Projectile Reference")]
    [Tooltip("Speed of the projectile used for intercept calculations.")]
    [SerializeField] private float projectileSpeed = 7f;

    // --- Position history for Average mode ---
    private struct PositionSample
    {
        public float time;
        public Vector2 position;
    }

    private readonly List<PositionSample> positionHistory = new(128);
    private float nextSampleTime;

    // --- Velocity tracking for Predicted mode ---
    private readonly List<Vector2> recentPositions = new(16);
    private float lastVelocityRecordTime;

    // --- Cached results (updated each frame) ---
    private Vector2 cachedPlayerVelocity;
    private float nextTargetSearchTime;

    public Transform Target => target;
    public Vector2 PlayerVelocity => cachedPlayerVelocity;
    public float ProjectileSpeed { get => projectileSpeed; set => projectileSpeed = Mathf.Max(0.01f, value); }

    private void Update()
    {
        ResolveTarget();

        if (target == null)
            return;

        RecordPositionSample();
        UpdateVelocityEstimate();
    }

    // --------------------------------------------------
    // Public API
    // --------------------------------------------------

    /// <summary>
    /// Returns the world-space point an enemy should aim at for the given mode.
    /// </summary>
    public Vector2 GetTargetPoint(EnemyTargetingMode mode)
    {
        if (target == null)
            return transform.position;

        switch (mode)
        {
            case EnemyTargetingMode.Single:
                return GetSingleTarget();

            case EnemyTargetingMode.Average:
                return GetAverageTarget();

            case EnemyTargetingMode.Predicted:
                return GetPredictedTarget();

            case EnemyTargetingMode.Select:
                return GetSelectTarget();

            case EnemyTargetingMode.Suppress:
                return GetSuppressTarget();

            default:
                return GetSingleTarget();
        }
    }

    /// <summary>
    /// Returns the aim direction from this enemy to the target point.
    /// </summary>
    public Vector2 GetAimDirection(EnemyTargetingMode mode)
    {
        Vector2 targetPoint = GetTargetPoint(mode);
        Vector2 origin = transform.position;
        Vector2 delta = targetPoint - origin;

        return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;
    }

    public void SetTarget(Transform newTarget)
    {
        if (target == newTarget)
            return;

        target = newTarget;
        positionHistory.Clear();
        recentPositions.Clear();
    }

    public void SetSelectedEntity(Transform entity)
    {
        selectedEntity = entity;
    }

    public void SyncProjectileSpeed(float speed)
    {
        projectileSpeed = Mathf.Max(0.01f, speed);
    }

    // --------------------------------------------------
    // Single: aim at current position
    // --------------------------------------------------

    private Vector2 GetSingleTarget()
    {
        return target.position;
    }

    // --------------------------------------------------
    // Average: mean position over the last N seconds
    // --------------------------------------------------

    private Vector2 GetAverageTarget()
    {
        PruneOldSamples();

        if (positionHistory.Count == 0)
            return GetSingleTarget();

        Vector2 sum = Vector2.zero;

        for (int i = 0; i < positionHistory.Count; i++)
            sum += positionHistory[i].position;

        return sum / positionHistory.Count;
    }

    // --------------------------------------------------
    // Predicted: intercept point based on velocity
    // --------------------------------------------------

    private Vector2 GetPredictedTarget()
    {
        Vector2 playerPos = target.position;
        Vector2 enemyPos = transform.position;
        Vector2 playerVel = cachedPlayerVelocity;

        if (playerVel.sqrMagnitude < 0.001f || projectileSpeed <= 0f)
            return playerPos;

        // Solve quadratic for time-to-intercept:
        // |playerPos + playerVel*t - enemyPos| = projectileSpeed * t
        Vector2 relPos = playerPos - enemyPos;
        float a = Vector2.Dot(playerVel, playerVel) - projectileSpeed * projectileSpeed;
        float b = 2f * Vector2.Dot(relPos, playerVel);
        float c = Vector2.Dot(relPos, relPos);

        float discriminant = b * b - 4f * a * c;

        if (discriminant < 0f)
            return playerPos;

        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / (2f * a);
        float t2 = (-b + sqrtDisc) / (2f * a);

        float interceptTime = -1f;

        if (t1 > 0.001f && t2 > 0.001f)
            interceptTime = Mathf.Min(t1, t2);
        else if (t1 > 0.001f)
            interceptTime = t1;
        else if (t2 > 0.001f)
            interceptTime = t2;

        if (interceptTime <= 0f)
            return playerPos;

        // Cap intercept time to avoid aiming at absurd distances
        interceptTime = Mathf.Min(interceptTime, 3f);

        return playerPos + playerVel * interceptTime;
    }

    // --------------------------------------------------
    // Select: entity-based targeting for non-projectile spells
    // --------------------------------------------------

    private Vector2 GetSelectTarget()
    {
        if (selectedEntity != null)
            return selectedEntity.position;

        return GetSingleTarget();
    }

    // --------------------------------------------------
    // Suppress: fire at the edge of cover
    // --------------------------------------------------

    private Vector2 GetSuppressTarget()
    {
        Vector2 origin = transform.position;
        Vector2 playerPos = target.position;
        Vector2 directDir = (playerPos - origin).normalized;

        if (coverMask.value == 0)
            return playerPos;

        // Check if direct LOS is blocked
        RaycastHit2D directHit = Physics2D.Raycast(origin, directDir, 100f, coverMask);

        if (directHit.collider == null)
            return playerPos; // No cover, just aim directly

        if (requireSingleCoverLayer)
        {
            RaycastHit2D[] allHits = Physics2D.RaycastAll(origin, directDir, 100f, coverMask);
            if (allHits.Length > 1)
                return playerPos; // Multiple cover layers, don't suppress
        }

        // Binary search for the cover edge on both sides
        Vector2 bestSuppressPoint = playerPos;
        float bestAngle = float.MaxValue;
        bool foundEdge = false;

        for (int side = -1; side <= 1; side += 2)
        {
            float edgeAngle = FindCoverEdgeAngle(origin, directDir, side);

            if (edgeAngle < 0f)
                continue;

            if (edgeAngle > maxSuppressAngleDeg)
                continue;

            if (edgeAngle < bestAngle)
            {
                bestAngle = edgeAngle;
                float radians = edgeAngle * side * Mathf.Deg2Rad;
                Vector2 suppressDir = RotateVector(directDir, radians);
                float distToPlayer = Vector2.Distance(origin, playerPos);
                bestSuppressPoint = origin + suppressDir * distToPlayer;
                foundEdge = true;
            }
        }

        return foundEdge ? bestSuppressPoint : playerPos;
    }

    private float FindCoverEdgeAngle(Vector2 origin, Vector2 directDir, int side)
    {
        float lo = 0f;
        float hi = maxSuppressAngleDeg;

        bool foundClear = false;

        // First check if there's a clear angle within range
        for (int i = 1; i <= suppressRayCount; i++)
        {
            float angle = (hi * i / suppressRayCount) * side;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 testDir = RotateVector(directDir, rad);

            RaycastHit2D hit = Physics2D.Raycast(origin, testDir, 100f, coverMask);

            if (hit.collider == null)
            {
                hi = Mathf.Abs(angle);
                foundClear = true;
                break;
            }
        }

        if (!foundClear)
            return -1f;

        // Binary search between lo (blocked) and hi (clear) to find the edge
        for (int iteration = 0; iteration < 8; iteration++)
        {
            float mid = (lo + hi) * 0.5f;
            float rad = mid * side * Mathf.Deg2Rad;
            Vector2 testDir = RotateVector(directDir, rad);

            RaycastHit2D hit = Physics2D.Raycast(origin, testDir, 100f, coverMask);

            if (hit.collider != null)
                lo = mid;
            else
                hi = mid;
        }

        // Return the angle just past the cover edge (use hi, which is clear)
        return hi;
    }

    // --------------------------------------------------
    // Position sampling
    // --------------------------------------------------

    private void RecordPositionSample()
    {
        if (target == null)
            return;

        if (Time.time < nextSampleTime)
            return;

        nextSampleTime = Time.time + sampleInterval;

        positionHistory.Add(new PositionSample
        {
            time = Time.time,
            position = target.position
        });

        PruneOldSamples();
    }

    private void PruneOldSamples()
    {
        float cutoff = Time.time - averageWindowSeconds;
        int removeCount = 0;

        for (int i = 0; i < positionHistory.Count; i++)
        {
            if (positionHistory[i].time < cutoff)
                removeCount++;
            else
                break;
        }

        if (removeCount > 0)
            positionHistory.RemoveRange(0, removeCount);
    }

    // --------------------------------------------------
    // Velocity estimation
    // --------------------------------------------------

    private void UpdateVelocityEstimate()
    {
        if (target == null)
            return;

        // Record position every fixed frame
        if (Mathf.Approximately(Time.time, lastVelocityRecordTime))
            return;

        lastVelocityRecordTime = Time.time;

        recentPositions.Add(target.position);

        while (recentPositions.Count > velocityFrameCount)
            recentPositions.RemoveAt(0);

        if (recentPositions.Count < 2)
        {
            cachedPlayerVelocity = Vector2.zero;
            return;
        }

        // Average velocity over recent frames
        Vector2 totalDelta = recentPositions[recentPositions.Count - 1] - recentPositions[0];
        float totalTime = (recentPositions.Count - 1) * Time.deltaTime;

        if (totalTime > 0.001f)
            cachedPlayerVelocity = totalDelta / totalTime;
        else
            cachedPlayerVelocity = Vector2.zero;
    }

    // --------------------------------------------------
    // Target resolution
    // --------------------------------------------------

    private void ResolveTarget()
    {
        if (target != null)
            return;

        if (!autoFindPlayer)
            return;

        if (Time.time < nextTargetSearchTime)
            return;

        nextTargetSearchTime = Time.time + 0.2f;

        GameObject playerObj = null;

        try { playerObj = GameObject.FindWithTag(playerTag); }
        catch (UnityException) { return; }

        if (playerObj != null)
            target = playerObj.transform;
    }

    // --------------------------------------------------
    // Utilities
    // --------------------------------------------------

    private static Vector2 RotateVector(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
