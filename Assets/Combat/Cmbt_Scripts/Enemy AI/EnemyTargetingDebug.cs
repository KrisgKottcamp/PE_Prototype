using UnityEngine;

/// <summary>
/// Debug visualization for EnemyTargetingSystem.
/// Draws Gizmos and optional runtime LineRenderers showing:
/// - Active targeting mode and computed aim point
/// - Position history trail (Average mode)
/// - Intercept geometry (Predicted mode)
/// - Suppress angle and cover edge rays (Suppress mode)
/// - Selected entity highlight (Select mode)
///
/// Attach alongside EnemyTargetingSystem. Enable/disable at will.
/// </summary>
[RequireComponent(typeof(EnemyTargetingSystem))]
public class EnemyTargetingDebug : MonoBehaviour
{
    [Header("Debug Display")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private bool showRuntimeLines = false;
    [SerializeField] private EnemyTargetingMode debugMode = EnemyTargetingMode.Single;

    [Header("Gizmo Colors")]
    [SerializeField] private Color singleColor = Color.red;
    [SerializeField] private Color averageColor = Color.yellow;
    [SerializeField] private Color predictedColor = Color.cyan;
    [SerializeField] private Color selectColor = Color.green;
    [SerializeField] private Color suppressColor = new Color(1f, 0.5f, 0f, 1f);
    [SerializeField] private Color historyTrailColor = new Color(1f, 1f, 0f, 0.4f);

    [Header("Gizmo Sizes")]
    [SerializeField] private float targetPointRadius = 0.15f;
    [SerializeField] private float originRadius = 0.08f;

    [Header("Runtime Line (optional)")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float lineWidth = 0.03f;

    [Header("Show All Modes Simultaneously")]
    [SerializeField] private bool showAllModes = false;

    private EnemyTargetingSystem targeting;

    private void Awake()
    {
        targeting = GetComponent<EnemyTargetingSystem>();

        if (aimLine != null)
        {
            aimLine.startWidth = lineWidth;
            aimLine.endWidth = lineWidth;
            aimLine.positionCount = 2;
            aimLine.enabled = false;
        }
    }

    private void Update()
    {
        if (!showRuntimeLines || aimLine == null || targeting == null)
        {
            if (aimLine != null)
                aimLine.enabled = false;
            return;
        }

        if (targeting.Target == null)
        {
            aimLine.enabled = false;
            return;
        }

        Vector2 origin = transform.position;
        Vector2 targetPoint = targeting.GetTargetPoint(debugMode);

        aimLine.enabled = true;
        aimLine.startColor = GetColorForMode(debugMode);
        aimLine.endColor = GetColorForMode(debugMode);
        aimLine.SetPosition(0, origin);
        aimLine.SetPosition(1, (Vector3)targetPoint);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        if (targeting == null)
            targeting = GetComponent<EnemyTargetingSystem>();

        if (targeting == null || targeting.Target == null)
            return;

        if (showAllModes)
        {
            DrawModeGizmo(EnemyTargetingMode.Single);
            DrawModeGizmo(EnemyTargetingMode.Average);
            DrawModeGizmo(EnemyTargetingMode.Predicted);
            DrawModeGizmo(EnemyTargetingMode.Suppress);
            DrawModeGizmo(EnemyTargetingMode.Select);
        }
        else
        {
            DrawModeGizmo(debugMode);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos)
            return;

        if (targeting == null)
            targeting = GetComponent<EnemyTargetingSystem>();

        if (targeting == null || targeting.Target == null)
            return;

        // Always show all modes when selected
        DrawModeGizmo(EnemyTargetingMode.Single);
        DrawModeGizmo(EnemyTargetingMode.Average);
        DrawModeGizmo(EnemyTargetingMode.Predicted);
        DrawModeGizmo(EnemyTargetingMode.Suppress);
        DrawModeGizmo(EnemyTargetingMode.Select);

        DrawVelocityArrow();
    }

    private void DrawModeGizmo(EnemyTargetingMode mode)
    {
        Vector2 origin = transform.position;
        Vector2 targetPoint = targeting.GetTargetPoint(mode);
        Color color = GetColorForMode(mode);

        Gizmos.color = color;

        // Draw aim line
        Gizmos.DrawLine(origin, targetPoint);

        // Draw target point
        Gizmos.DrawWireSphere(targetPoint, targetPointRadius);

        // Draw small label-like marker at origin
        Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
        Gizmos.DrawWireSphere(origin, originRadius);

        // Mode-specific extras
        switch (mode)
        {
            case EnemyTargetingMode.Predicted:
                DrawPredictedExtras(origin, targetPoint);
                break;

            case EnemyTargetingMode.Suppress:
                DrawSuppressExtras(origin);
                break;

            case EnemyTargetingMode.Average:
                DrawAverageExtras(targetPoint);
                break;
        }
    }

    private void DrawPredictedExtras(Vector2 origin, Vector2 interceptPoint)
    {
        if (targeting.Target == null)
            return;

        Vector2 playerPos = targeting.Target.position;
        Vector2 velocity = targeting.PlayerVelocity;

        // Draw player's velocity vector
        Gizmos.color = new Color(predictedColor.r, predictedColor.g, predictedColor.b, 0.6f);
        Gizmos.DrawLine(playerPos, playerPos + velocity * 0.5f);

        // Draw dotted line from player current pos to intercept
        Gizmos.color = new Color(predictedColor.r, predictedColor.g, predictedColor.b, 0.3f);
        Gizmos.DrawLine(playerPos, interceptPoint);

        // Mark intercept with a cross
        float crossSize = targetPointRadius * 0.7f;
        Gizmos.color = predictedColor;
        Gizmos.DrawLine(
            interceptPoint + new Vector2(-crossSize, -crossSize),
            interceptPoint + new Vector2(crossSize, crossSize)
        );
        Gizmos.DrawLine(
            interceptPoint + new Vector2(-crossSize, crossSize),
            interceptPoint + new Vector2(crossSize, -crossSize)
        );
    }

    private void DrawSuppressExtras(Vector2 origin)
    {
        if (targeting.Target == null)
            return;

        Vector2 playerPos = targeting.Target.position;
        Vector2 directDir = (playerPos - origin).normalized;
        float dist = Vector2.Distance(origin, playerPos);

        // Draw the direct (blocked) line
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawLine(origin, origin + directDir * dist);

        // Draw suppress cone edges
        Gizmos.color = new Color(suppressColor.r, suppressColor.g, suppressColor.b, 0.3f);
        float maxRad = 25f * Mathf.Deg2Rad;
        Vector2 leftEdge = RotateVec(directDir, -maxRad);
        Vector2 rightEdge = RotateVec(directDir, maxRad);
        Gizmos.DrawLine(origin, origin + leftEdge * dist * 0.6f);
        Gizmos.DrawLine(origin, origin + rightEdge * dist * 0.6f);
    }

    private void DrawAverageExtras(Vector2 averagePoint)
    {
        if (targeting.Target == null)
            return;

        Vector2 currentPos = targeting.Target.position;

        // Draw line from current position to average
        Gizmos.color = new Color(historyTrailColor.r, historyTrailColor.g, historyTrailColor.b, 0.5f);
        Gizmos.DrawLine(currentPos, averagePoint);

        // Draw a filled sphere at the average point
        Gizmos.color = averageColor;
        Gizmos.DrawSphere(averagePoint, targetPointRadius * 0.6f);
    }

    private void DrawVelocityArrow()
    {
        if (targeting.Target == null)
            return;

        Vector2 playerPos = targeting.Target.position;
        Vector2 velocity = targeting.PlayerVelocity;

        if (velocity.sqrMagnitude < 0.01f)
            return;

        Gizmos.color = Color.white;
        Vector2 tip = playerPos + velocity * 0.4f;
        Gizmos.DrawLine(playerPos, tip);

        // Arrow head
        Vector2 back = -velocity.normalized * 0.1f;
        Vector2 perp = new Vector2(-velocity.normalized.y, velocity.normalized.x) * 0.05f;
        Gizmos.DrawLine(tip, tip + back + perp);
        Gizmos.DrawLine(tip, tip + back - perp);
    }

    private static Vector2 RotateVec(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
#endif

    private Color GetColorForMode(EnemyTargetingMode mode)
    {
        switch (mode)
        {
            case EnemyTargetingMode.Single: return singleColor;
            case EnemyTargetingMode.Average: return averageColor;
            case EnemyTargetingMode.Predicted: return predictedColor;
            case EnemyTargetingMode.Select: return selectColor;
            case EnemyTargetingMode.Suppress: return suppressColor;
            default: return Color.white;
        }
    }
}
