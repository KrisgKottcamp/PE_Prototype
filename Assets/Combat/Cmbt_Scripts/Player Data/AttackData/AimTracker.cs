using UnityEngine;

public class AimTracker : MonoBehaviour
{
    [SerializeField] private Transform aimOrigin; // defaults to transform
    public Vector2 AimDir { get; private set; } = Vector2.up;

    [Header("Cover Edge Snap (Suppress Aim)")]
    [Tooltip("Enable snapping aim to the nearest cover edge focal point.")]
    [SerializeField] private bool enableCoverSnap = true;
    [SerializeField] private LayerMask coverMask;
    [Tooltip("Max distance to scan for cover edges from the aim origin.")]
    [SerializeField] private float coverSnapScanRadius = 5f;
    [Tooltip("Max angle between raw aim and a cover edge for snap to activate (degrees).")]
    [SerializeField] private float coverSnapAngleThreshold = 12f;
    [Tooltip("Number of rays cast radially to detect cover.")]
    [SerializeField] private int coverSnapScanRays = 24;
    [Tooltip("Binary search iterations to refine edge.")]
    [SerializeField] private int coverSnapEdgeIterations = 8;
    [Tooltip("Lerp speed for smooth snap (0 = instant).")]
    [SerializeField] private float coverSnapSmoothSpeed = 20f;

    /// <summary>True when aim is currently snapped to a cover edge.</summary>
    public bool IsSnappedToCover { get; private set; }
    /// <summary>World-space point of the cover edge focal point (valid when IsSnappedToCover).</summary>
    public Vector2 SnapPoint { get; private set; }

    private Camera cam;
    private Vector2 rawAimDir;

    private void Awake()
    {
        if (aimOrigin == null) aimOrigin = transform;
    }

    private void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;

        Vector3 world = cam.ScreenToWorldPoint(mouse);
        Vector2 delta = (Vector2)world - (Vector2)aimOrigin.position;

        if (delta.sqrMagnitude > 0.0001f)
            rawAimDir = delta.normalized;

        Vector2 finalDir = rawAimDir;
        IsSnappedToCover = false;

        if (enableCoverSnap && coverMask.value != 0)
        {
            Vector2 edgeDir = FindNearestCoverEdgeDir(out bool found);
            if (found)
            {
                finalDir = edgeDir;
                IsSnappedToCover = true;
            }
        }

        if (coverSnapSmoothSpeed > 0f && AimDir != Vector2.zero)
        {
            float angle = Vector2.SignedAngle(AimDir, finalDir);
            float maxStep = coverSnapSmoothSpeed * Time.deltaTime * 360f;
            if (Mathf.Abs(angle) > maxStep)
                angle = Mathf.Sign(angle) * maxStep;

            AimDir = RotateVector(AimDir, angle * Mathf.Deg2Rad).normalized;
        }
        else
        {
            AimDir = finalDir;
        }
    }

    private Vector2 FindNearestCoverEdgeDir(out bool found)
    {
        found = false;
        Vector2 origin = aimOrigin.position;
        float angleStep = 360f / coverSnapScanRays;

        float bestAngleDiff = float.MaxValue;
        Vector2 bestEdgeDir = rawAimDir;

        for (int i = 0; i < coverSnapScanRays; i++)
        {
            float rayAngle = i * angleStep * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rayAngle), Mathf.Sin(rayAngle));

            RaycastHit2D hit = Physics2D.Raycast(origin, dir, coverSnapScanRadius, coverMask);
            if (hit.collider == null)
                continue;

            // Found cover — check adjacent rays for an edge transition
            for (int side = -1; side <= 1; side += 2)
            {
                float adjacentAngle = (i + side) * angleStep * Mathf.Deg2Rad;
                Vector2 adjacentDir = new Vector2(Mathf.Cos(adjacentAngle), Mathf.Sin(adjacentAngle));

                RaycastHit2D adjacentHit = Physics2D.Raycast(origin, adjacentDir, coverSnapScanRadius, coverMask);
                if (adjacentHit.collider != null)
                    continue;

                // Edge between blocked (rayAngle) and clear (adjacentAngle)
                float loAngle = rayAngle;
                float hiAngle = adjacentAngle;

                if (side == -1)
                {
                    loAngle = adjacentAngle;
                    hiAngle = rayAngle;
                }

                // Binary search to pinpoint edge
                for (int iter = 0; iter < coverSnapEdgeIterations; iter++)
                {
                    float midAngle = (loAngle + hiAngle) * 0.5f;
                    Vector2 midDir = new Vector2(Mathf.Cos(midAngle), Mathf.Sin(midAngle));

                    RaycastHit2D midHit = Physics2D.Raycast(origin, midDir, coverSnapScanRadius, coverMask);

                    if (midHit.collider != null)
                    {
                        if (side == -1)
                            hiAngle = midAngle;
                        else
                            loAngle = midAngle;
                    }
                    else
                    {
                        if (side == -1)
                            loAngle = midAngle;
                        else
                            hiAngle = midAngle;
                    }
                }

                // Edge direction is just past the cover (the clear side)
                float edgeAngle = (side == -1) ? loAngle : hiAngle;
                Vector2 edgeDir = new Vector2(Mathf.Cos(edgeAngle), Mathf.Sin(edgeAngle));

                // Check if this edge is close to where the player is aiming
                float angleDiff = Vector2.Angle(rawAimDir, edgeDir);
                if (angleDiff <= coverSnapAngleThreshold && angleDiff < bestAngleDiff)
                {
                    bestAngleDiff = angleDiff;
                    bestEdgeDir = edgeDir;

                    // Compute world-space snap point
                    RaycastHit2D edgeHit = Physics2D.Raycast(origin, edgeDir, coverSnapScanRadius, coverMask);
                    SnapPoint = edgeHit.collider != null
                        ? edgeHit.point
                        : origin + edgeDir * coverSnapScanRadius;

                    found = true;
                }
            }
        }

        return bestEdgeDir;
    }

    private static Vector2 RotateVector(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
