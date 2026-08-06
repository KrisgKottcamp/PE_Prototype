using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visible targeting handles on the player combat pawn.
/// Each handle shows where an enemy would aim using a given targeting mode.
///
/// Handles are always visible during combat for debug/design iteration.
/// Enemies can register their active targeting mode to highlight which
/// handle they are currently using.
///
/// Attach to the player combat pawn (PlayerRoot or CombatPawn).
/// </summary>
public class PlayerTargetingHandles : MonoBehaviour
{
    [System.Serializable]
    public class HandleConfig
    {
        public EnemyTargetingMode mode;
        public Color color = Color.white;
        [Range(0.05f, 0.5f)]
        public float radius = 0.12f;
        [Range(0f, 1f)]
        public float idleAlpha = 0.25f;
        [Range(0f, 1f)]
        public float activeAlpha = 0.85f;
        public bool showLabel = true;
    }

    [Header("Handle Visuals")]
    [SerializeField] private HandleConfig[] handles = new[]
    {
        new HandleConfig { mode = EnemyTargetingMode.Single, color = Color.red, radius = 0.20f, idleAlpha = 0.55f },
        new HandleConfig { mode = EnemyTargetingMode.Average, color = Color.yellow, radius = 0.24f, idleAlpha = 0.55f },
        new HandleConfig { mode = EnemyTargetingMode.Predicted, color = Color.cyan, radius = 0.24f, idleAlpha = 0.55f },
        new HandleConfig { mode = EnemyTargetingMode.Select, color = Color.green, radius = 0.20f, idleAlpha = 0.55f },
        new HandleConfig { mode = EnemyTargetingMode.Suppress, color = new Color(1f, 0.5f, 0f), radius = 0.22f, idleAlpha = 0.55f },
    };

    [Header("Handle Appearance")]
    [SerializeField] private Sprite handleSprite;
    [Tooltip("Sorting layer for handle sprites.")]
    [SerializeField] private string sortingLayer = "UI";
    [SerializeField] private int baseSortingOrder = 1000;
    [Tooltip("Material for handle sprites. Leave empty for Sprites/Default.")]
    [SerializeField] private Material handleMaterial;

    [Header("Average Mode Settings")]
    [SerializeField] private float averageWindowSeconds = 5f;
    [SerializeField] private float sampleInterval = 0.05f;

    [Header("Predicted Mode Settings")]
    [SerializeField] private int velocityFrameCount = 8;
    [Tooltip("Reference projectile speed for intercept calculation.")]
    [SerializeField] private float referenceProjectileSpeed = 7f;
    [Tooltip("Assumed enemy distance for predicted handle placement.")]
    [SerializeField] private float assumedEnemyDistance = 6f;

    [Header("Suppress Mode Settings")]
    [SerializeField] private LayerMask coverMask;
    [Tooltip("Max distance to scan for nearby cover edges.")]
    [SerializeField] private float suppressScanRadius = 4f;
    [Tooltip("Number of rays cast radially to find cover.")]
    [SerializeField] private int suppressScanRays = 24;
    [Tooltip("Binary search iterations to refine edge point.")]
    [SerializeField] private int suppressEdgeIterations = 8;

    [Header("Ring Display")]
    [Tooltip("When true, idle handles orbit the player in a ring instead of stacking at center.")]
    [SerializeField] private bool useOrbitRing = true;
    [SerializeField] private float orbitRadius = 0.55f;

    [Header("Connector Lines")]
    [Tooltip("Draw a thin line from the player center to each active handle.")]
    [SerializeField] private bool showConnectorLines = true;
    [SerializeField] private float connectorLineWidth = 0.015f;

    [Header("Active Highlight")]
    [SerializeField] private float pulseSpeed = 4f;
    [SerializeField] private float pulseScaleMin = 0.9f;
    [SerializeField] private float pulseScaleMax = 1.3f;

    [Header("Debug")]
    [SerializeField] private bool alwaysShowAllHandles = true;
    [SerializeField] private bool logActiveTargeting = false;

    // --- Runtime ---
    private class HandleInstance
    {
        public EnemyTargetingMode mode;
        public HandleConfig config;
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public LineRenderer connector;
        public bool isActive;
        public float orbitAngle;
    }

    private readonly List<HandleInstance> handleInstances = new();

    // Position history for Average
    private struct PosSample { public float time; public Vector2 pos; }
    private readonly List<PosSample> posHistory = new(128);
    private float nextSampleTime;

    // Velocity tracking for Predicted
    private struct VelocitySample { public float time; public Vector2 pos; }
    private readonly List<VelocitySample> velocitySamples = new(16);
    private Vector2 cachedVelocity;

    // Active targeting registrations from enemies
    private readonly Dictionary<int, EnemyTargetingMode> activeTargeters = new();
    private readonly HashSet<EnemyTargetingMode> activeModesThisFrame = new();

    private Material runtimeMaterial;

    // --------------------------------------------------
    // Lifecycle
    // --------------------------------------------------

    private void Awake()
    {
        CreateHandleObjects();
    }

    private void Update()
    {
        RecordPositionSample();
        UpdateVelocity();
        UpdateActiveModesSet();
        UpdateHandlePositions();
        UpdateHandleVisuals();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < handleInstances.Count; i++)
        {
            if (handleInstances[i].gameObject != null)
                Destroy(handleInstances[i].gameObject);
        }

        handleInstances.Clear();

        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    // --------------------------------------------------
    // Public API — called by EnemyTargetingSystem
    // --------------------------------------------------

    /// <summary>
    /// An enemy registers that it is actively targeting the player with this mode.
    /// Call every frame the enemy is targeting, or call once and Unregister when done.
    /// </summary>
    public void RegisterActiveTargeting(int enemyId, EnemyTargetingMode mode)
    {
        activeTargeters[enemyId] = mode;

        if (logActiveTargeting)
            Debug.Log($"PlayerTargetingHandles: Enemy {enemyId} targeting with {mode}");
    }

    /// <summary>
    /// An enemy stops targeting the player.
    /// </summary>
    public void UnregisterTargeting(int enemyId)
    {
        activeTargeters.Remove(enemyId);
    }

    /// <summary>
    /// Set the reference projectile speed (used for Predicted handle placement).
    /// Call when an enemy's projectile speed is known.
    /// </summary>
    public void SetReferenceProjectileSpeed(float speed)
    {
        referenceProjectileSpeed = Mathf.Max(0.01f, speed);
    }

    // --------------------------------------------------
    // Handle creation
    // --------------------------------------------------

    private void CreateHandleObjects()
    {
        if (handles == null || handles.Length == 0)
            return;

        for (int i = 0; i < handles.Length; i++)
        {
            HandleConfig config = handles[i];
            if (config == null)
                continue;

            GameObject handleObj = new GameObject($"TargetHandle_{config.mode}");
            handleObj.transform.SetParent(transform, false);
            handleObj.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = handleObj.AddComponent<SpriteRenderer>();
            sr.sprite = GetHandleSprite();
            sr.sharedMaterial = GetHandleMaterial();
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = baseSortingOrder + i;
            sr.color = config.color;

            float diameter = config.radius * 2f;
            handleObj.transform.localScale = new Vector3(diameter, diameter, 1f);

            // Connector line from player center to handle
            LineRenderer lr = null;
            if (showConnectorLines)
            {
                lr = handleObj.AddComponent<LineRenderer>();
                lr.sharedMaterial = GetHandleMaterial();
                lr.startWidth = connectorLineWidth;
                lr.endWidth = connectorLineWidth;
                lr.positionCount = 2;
                lr.sortingLayerName = sortingLayer;
                lr.sortingOrder = baseSortingOrder - 1;
                lr.useWorldSpace = true;
                lr.startColor = config.color;
                lr.endColor = config.color;
                lr.enabled = false;
            }

            HandleInstance instance = new HandleInstance
            {
                mode = config.mode,
                config = config,
                gameObject = handleObj,
                renderer = sr,
                connector = lr,
                isActive = false,
                orbitAngle = (360f / handles.Length) * i
            };

            handleInstances.Add(instance);
        }
    }

    // --------------------------------------------------
    // Position computation per mode
    // --------------------------------------------------

    private Vector2 ComputeHandlePosition(EnemyTargetingMode mode)
    {
        Vector2 playerPos = transform.position;

        switch (mode)
        {
            case EnemyTargetingMode.Single:
                return playerPos;

            case EnemyTargetingMode.Average:
                return ComputeAveragePosition();

            case EnemyTargetingMode.Predicted:
                return ComputePredictedPosition();

            case EnemyTargetingMode.Select:
                return playerPos;

            case EnemyTargetingMode.Suppress:
                return ComputeSuppressPosition();

            default:
                return playerPos;
        }
    }

    private Vector2 ComputeAveragePosition()
    {
        PruneOldSamples();

        if (posHistory.Count == 0)
            return transform.position;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < posHistory.Count; i++)
            sum += posHistory[i].pos;

        return sum / posHistory.Count;
    }

    private Vector2 ComputePredictedPosition()
    {
        Vector2 playerPos = transform.position;

        // Only show intercept if player is actually moving
        float speed = cachedVelocity.magnitude;
        if (speed < 0.05f)
            return playerPos;

        // Time for a projectile to reach the player from assumed distance
        float timeToReach = assumedEnemyDistance / Mathf.Max(0.01f, referenceProjectileSpeed);
        timeToReach = Mathf.Min(timeToReach, 3f);

        // Intercept point: where the player will be when the projectile arrives
        Vector2 intercept = playerPos + cachedVelocity * timeToReach;

        // Clamp max offset so it doesn't fly off screen
        Vector2 offset = intercept - playerPos;
        float maxOffset = assumedEnemyDistance * 0.5f;
        if (offset.magnitude > maxOffset)
            intercept = playerPos + offset.normalized * maxOffset;

        return intercept;
    }

    private Vector2 ComputeSuppressPosition()
    {
        Vector2 playerPos = transform.position;

        if (coverMask.value == 0)
            return playerPos;

        // Cast rays radially to find all cover edges near the player
        float angleStep = 360f / suppressScanRays;
        float closestEdgeDist = float.MaxValue;
        Vector2 closestEdgePoint = playerPos;
        bool foundEdge = false;

        for (int i = 0; i < suppressScanRays; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            RaycastHit2D hit = Physics2D.Raycast(playerPos, dir, suppressScanRadius, coverMask);

            if (hit.collider == null)
                continue;

            // Found cover in this direction — now find its nearest edge
            // Binary search between a clear ray and this blocked ray
            Vector2 edgePoint = FindNearestCoverEdge(playerPos, dir, i, angleStep);

            float dist = Vector2.Distance(playerPos, edgePoint);
            if (dist < closestEdgeDist)
            {
                closestEdgeDist = dist;
                closestEdgePoint = edgePoint;
                foundEdge = true;
            }
        }

        return foundEdge ? closestEdgePoint : playerPos;
    }

    private Vector2 FindNearestCoverEdge(Vector2 origin, Vector2 blockedDir, int rayIndex, float angleStep)
    {
        // Check adjacent rays to find where cover transitions from hit to miss
        float blockedAngle = rayIndex * angleStep;

        // Try both sides
        Vector2 bestEdge = origin + blockedDir * suppressScanRadius;
        float bestDist = float.MaxValue;

        for (int side = -1; side <= 1; side += 2)
        {
            float adjacentAngle = (blockedAngle + angleStep * side) * Mathf.Deg2Rad;
            Vector2 adjacentDir = new Vector2(Mathf.Cos(adjacentAngle), Mathf.Sin(adjacentAngle));

            RaycastHit2D adjacentHit = Physics2D.Raycast(origin, adjacentDir, suppressScanRadius, coverMask);

            // If the adjacent ray is clear, the edge is between blocked and clear
            if (adjacentHit.collider != null)
                continue;

            // Binary search between blocked angle and clear angle
            float loAngle = blockedAngle * Mathf.Deg2Rad;
            float hiAngle = adjacentAngle;

            // Ensure we're searching the right arc
            if (side == -1)
            {
                loAngle = adjacentAngle;
                hiAngle = blockedAngle * Mathf.Deg2Rad;
            }

            for (int iter = 0; iter < suppressEdgeIterations; iter++)
            {
                float midAngle = (loAngle + hiAngle) * 0.5f;
                Vector2 midDir = new Vector2(Mathf.Cos(midAngle), Mathf.Sin(midAngle));

                RaycastHit2D midHit = Physics2D.Raycast(origin, midDir, suppressScanRadius, coverMask);

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

            // The edge is at the boundary — cast one final ray at the blocked side of convergence
            float edgeAngle = (side == -1) ? hiAngle : loAngle;
            Vector2 edgeDir = new Vector2(Mathf.Cos(edgeAngle), Mathf.Sin(edgeAngle));
            RaycastHit2D edgeHit = Physics2D.Raycast(origin, edgeDir, suppressScanRadius, coverMask);

            Vector2 edgePoint;
            if (edgeHit.collider != null)
                edgePoint = edgeHit.point;
            else
                edgePoint = origin + edgeDir * suppressScanRadius * 0.5f;

            float dist = Vector2.Distance(origin, edgePoint);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestEdge = edgePoint;
            }
        }

        return bestEdge;
    }

    // --------------------------------------------------
    // Handle updates
    // --------------------------------------------------

    private void UpdateHandlePositions()
    {
        for (int i = 0; i < handleInstances.Count; i++)
        {
            HandleInstance inst = handleInstances[i];
            if (inst.gameObject == null)
                continue;

            Vector2 targetPos = ComputeHandlePosition(inst.mode);
            bool isAtPlayerCenter = (targetPos - (Vector2)transform.position).sqrMagnitude < 0.001f;

            // If the handle would sit at the player center, use orbit ring
            if (isAtPlayerCenter && useOrbitRing && !inst.isActive)
            {
                float angle = inst.orbitAngle * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * orbitRadius;
                targetPos = (Vector2)transform.position + offset;
            }

            inst.gameObject.transform.position = new Vector3(targetPos.x, targetPos.y, 0f);
        }
    }

    private void UpdateHandleVisuals()
    {
        float pulse = Mathf.Lerp(pulseScaleMin, pulseScaleMax,
            (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f);

        Vector3 playerCenter = transform.position;

        for (int i = 0; i < handleInstances.Count; i++)
        {
            HandleInstance inst = handleInstances[i];
            if (inst.renderer == null)
                continue;

            bool isActive = activeModesThisFrame.Contains(inst.mode);
            inst.isActive = isActive;

            bool shouldShow = alwaysShowAllHandles || isActive;
            inst.renderer.enabled = shouldShow;

            if (!shouldShow)
            {
                if (inst.connector != null)
                    inst.connector.enabled = false;
                continue;
            }

            // Alpha
            float alpha = isActive ? inst.config.activeAlpha : inst.config.idleAlpha;
            Color c = inst.config.color;
            c.a = alpha;
            inst.renderer.color = c;

            // Scale pulse when active
            float diameter = inst.config.radius * 2f;
            float scale = isActive ? diameter * pulse : diameter;
            inst.gameObject.transform.localScale = new Vector3(scale, scale, 1f);

            // Connector line
            if (inst.connector != null)
            {
                bool showLine = shouldShow &&
                    ((Vector2)inst.gameObject.transform.position - (Vector2)playerCenter).sqrMagnitude > 0.01f;

                inst.connector.enabled = showLine;

                if (showLine)
                {
                    Color lineColor = c;
                    lineColor.a = alpha * 0.6f;
                    inst.connector.startColor = lineColor;
                    inst.connector.endColor = lineColor;
                    inst.connector.SetPosition(0, playerCenter);
                    inst.connector.SetPosition(1, inst.gameObject.transform.position);
                }
            }
        }
    }

    private void UpdateActiveModesSet()
    {
        activeModesThisFrame.Clear();

        foreach (var kvp in activeTargeters)
            activeModesThisFrame.Add(kvp.Value);
    }

    // --------------------------------------------------
    // Position sampling (mirrors EnemyTargetingSystem logic)
    // --------------------------------------------------

    private void RecordPositionSample()
    {
        if (Time.time < nextSampleTime)
            return;

        nextSampleTime = Time.time + sampleInterval;

        posHistory.Add(new PosSample
        {
            time = Time.time,
            pos = transform.position
        });

        PruneOldSamples();
    }

    private void PruneOldSamples()
    {
        float cutoff = Time.time - averageWindowSeconds;
        int removeCount = 0;

        for (int i = 0; i < posHistory.Count; i++)
        {
            if (posHistory[i].time < cutoff)
                removeCount++;
            else
                break;
        }

        if (removeCount > 0)
            posHistory.RemoveRange(0, removeCount);
    }

    private void UpdateVelocity()
    {
        velocitySamples.Add(new VelocitySample
        {
            time = Time.time,
            pos = transform.position
        });

        while (velocitySamples.Count > velocityFrameCount)
            velocitySamples.RemoveAt(0);

        if (velocitySamples.Count < 2)
        {
            cachedVelocity = Vector2.zero;
            return;
        }

        VelocitySample oldest = velocitySamples[0];
        VelocitySample newest = velocitySamples[velocitySamples.Count - 1];

        float elapsed = newest.time - oldest.time;

        if (elapsed < 0.001f)
        {
            cachedVelocity = Vector2.zero;
            return;
        }

        cachedVelocity = (newest.pos - oldest.pos) / elapsed;
    }

    // --------------------------------------------------
    // Sprite / Material helpers
    // --------------------------------------------------

    private Sprite GetHandleSprite()
    {
        if (handleSprite != null)
            return handleSprite;

        // Tactical crosshair reticle — visible at small sizes
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color clear = Color.clear;
        Color fill = Color.white;

        // Clear entire texture
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        float center = size * 0.5f;
        float outerRadius = center - 1f;
        float innerRadius = outerRadius * 0.45f;
        float ringThickness = 2.5f;
        float crosshairThickness = 1.8f;
        float crosshairInnerGap = innerRadius * 0.6f;
        float dotRadius = 2.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = 0f;

                // Outer ring
                float ringDist = Mathf.Abs(dist - outerRadius);
                if (ringDist < ringThickness)
                {
                    alpha = Mathf.Max(alpha, 1f - (ringDist / ringThickness));
                }

                // Inner ring (smaller, thinner)
                float innerRingDist = Mathf.Abs(dist - innerRadius);
                if (innerRingDist < ringThickness * 0.6f)
                {
                    alpha = Mathf.Max(alpha, (1f - (innerRingDist / (ringThickness * 0.6f))) * 0.7f);
                }

                // Crosshair lines (4 arms extending from inner gap to outer ring)
                float absX = Mathf.Abs(dx);
                float absY = Mathf.Abs(dy);

                // Horizontal arms
                if (absY < crosshairThickness && absX > crosshairInnerGap && absX < outerRadius)
                {
                    float armAlpha = 1f - (absY / crosshairThickness);
                    alpha = Mathf.Max(alpha, armAlpha * 0.9f);
                }

                // Vertical arms
                if (absX < crosshairThickness && absY > crosshairInnerGap && absY < outerRadius)
                {
                    float armAlpha = 1f - (absX / crosshairThickness);
                    alpha = Mathf.Max(alpha, armAlpha * 0.9f);
                }

                // Center dot
                if (dist < dotRadius)
                {
                    alpha = Mathf.Max(alpha, 1f - (dist / dotRadius));
                }

                // Diamond tick marks at 45-degree angles
                float diag = Mathf.Abs(absX - absY);
                float diagDist = (absX + absY) * 0.707f; // distance along diagonal
                if (diag < 1.5f && diagDist > innerRadius * 0.8f && diagDist < outerRadius * 0.7f)
                {
                    float tickAlpha = (1f - (diag / 1.5f)) * 0.5f;
                    alpha = Mathf.Max(alpha, tickAlpha);
                }

                if (alpha > 0.01f)
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        handleSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );

        return handleSprite;
    }

    private Material GetHandleMaterial()
    {
        if (handleMaterial != null)
            return handleMaterial;

        if (runtimeMaterial != null)
            return runtimeMaterial;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        runtimeMaterial = new Material(shader)
        {
            name = "TargetHandle_RuntimeMaterial"
        };

        return runtimeMaterial;
    }

    // --------------------------------------------------
    // Editor Gizmos
    // --------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        // Draw velocity arrow
        if (cachedVelocity.sqrMagnitude > 0.01f)
        {
            Vector2 pos = transform.position;
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, pos + cachedVelocity * 0.3f);
        }

        // Draw orbit ring
        if (useOrbitRing)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            DrawWireCircle((Vector2)transform.position, orbitRadius, 24);
        }
    }

    private static void DrawWireCircle(Vector2 center, float radius, int segments)
    {
        float step = 360f / segments;

        for (int i = 0; i < segments; i++)
        {
            float a1 = i * step * Mathf.Deg2Rad;
            float a2 = (i + 1) * step * Mathf.Deg2Rad;

            Vector3 p1 = new Vector3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0f);
            Vector3 p2 = new Vector3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0f);

            Gizmos.DrawLine(p1, p2);
        }
    }
#endif
}
