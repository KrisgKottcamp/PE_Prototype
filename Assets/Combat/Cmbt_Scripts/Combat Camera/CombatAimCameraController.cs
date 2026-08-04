using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Converts the mouse's screen-space position into a smooth, resolution-safe
/// look-ahead target for a 2D Cinemachine Camera.
///
/// Add this component to the combat scene's Cinemachine Camera. It preserves
/// the existing camera pipeline, Confiner, and Impulse shake by changing only
/// the camera's Follow target.
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public sealed class CombatAimCameraController : MonoBehaviour
{
    [Header("Aim Source")]
    [Tooltip("Optional explicit combat AimTracker. Normally left empty because the combat pawn is spawned at runtime.")]
    [SerializeField] private AimTracker aimTrackerOverride;

    [SerializeField] private bool automaticallyFindAimTracker = true;

    [SerializeField, Min(0.05f)]
    private float aimTrackerSearchInterval = 0.25f;

    [Header("Aim-Weighted Framing")]
    [Tooltip("Maximum camera lead as a fraction of the full screen size. X=.16 lets the camera shift up to 16% of the visible width toward the cursor.")]
    [SerializeField]
    private Vector2 maximumLeadScreenFraction =
        new Vector2(0.16f, 0.12f);

    [Tooltip("Cursor movement inside this normalized center radius produces no camera lead.")]
    [SerializeField, Range(0f, 0.75f)]
    private float centerDeadZone = 0.10f;

    [Tooltip("Values above 1 keep movement gentle near the center while preserving full lead near the edge.")]
    [SerializeField, Range(0.25f, 4f)]
    private float leadResponseExponent = 1.35f;

    [Tooltip("Seconds used to smooth the invisible Follow target. Reduce for a snappier Nuclear Throne-like response; increase for a softer Gungeon-like response.")]
    [SerializeField, Min(0.01f)]
    private float followSmoothTime = 0.12f;

    [Tooltip("Maximum speed of the invisible Follow target in world units per second. Set to 0 for no cap.")]
    [SerializeField, Min(0f)]
    private float maximumFollowSpeed = 100f;

    [Tooltip("Keeps the camera responsive during Focus, menus, and hitstop.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Tooltip("Return the lead toward the player if the pointer leaves the Game view.")]
    [SerializeField] private bool recenterWhenCursorLeavesGameView = true;

    [Tooltip("Snap instead of visibly scrolling if the pawn or camera target teleports farther than this distance.")]
    [SerializeField, Min(0f)]
    private float teleportSnapDistance = 6f;

    [Header("Pixel Stability")]
    [Tooltip("Snaps the smoothed Follow target to the sprite pixel grid to prevent fractional camera positions from blurring pixel art.")]
    [SerializeField] private bool pixelSnapCameraTarget = true;

    [Tooltip("Pixels Per Unit used by the combat character sprites.")]
    [SerializeField, Min(1f)]
    private float assetsPixelsPerUnit = 100f;

    [Header("Cinemachine Binding")]
    [Tooltip("Normally resolved from this GameObject automatically.")]
    [SerializeField] private CinemachineCamera cinemachineCamera;

    [Tooltip("Reassert the aim target if another runtime script replaces the Follow target during combat.")]
    [SerializeField] private bool maintainFollowBinding = true;

    [Tooltip("Restore the camera's previous Follow target when this experiment is disabled.")]
    [SerializeField] private bool restoreOriginalFollowOnDisable = true;

    [Header("Runtime Debug")]
    [SerializeField] private string boundAimTracker = "None";
    [SerializeField] private Vector2 debugNormalizedCursor;
    [SerializeField] private Vector2 debugLeadWorldOffset;
    [SerializeField] private Vector2 debugTargetPosition;

    private AimTracker activeAimTracker;
    private Transform cameraTarget;
    private Transform originalFollowTarget;
    private Vector2 followVelocity;
    private float nextAimTrackerSearchTime;
    private bool capturedOriginalFollow;

    private void Awake()
    {
        if (cinemachineCamera == null)
            cinemachineCamera = GetComponent<CinemachineCamera>();
    }

    private void OnEnable()
    {
        nextAimTrackerSearchTime = 0f;
        TryResolveAimTracker(forceSearch: true);
    }

    private void LateUpdate()
    {
        if (!TryResolveAimTracker(forceSearch: false))
            return;

        EnsureCameraTarget();

        if (cameraTarget == null || cinemachineCamera == null)
            return;

        if (maintainFollowBinding &&
            cinemachineCamera.Follow != cameraTarget)
        {
            cinemachineCamera.Follow = cameraTarget;
        }

        Camera outputCamera =
            activeAimTracker.AimCamera != null
                ? activeAimTracker.AimCamera
                : Camera.main;

        Vector2 playerPosition =
            activeAimTracker.AimOrigin.position;

        Vector2 leadOffset = CalculateLeadOffset(outputCamera);
        Vector2 desiredPosition = playerPosition + leadOffset;
        Vector2 currentPosition = cameraTarget.position;

        bool shouldSnap =
            teleportSnapDistance > 0f &&
            Vector2.Distance(currentPosition, desiredPosition) >
                teleportSnapDistance;

        if (shouldSnap)
        {
            currentPosition = desiredPosition;
            followVelocity = Vector2.zero;
        }
        else
        {
            float deltaTime =
                useUnscaledTime
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;

            float speedCap =
                maximumFollowSpeed > 0f
                    ? maximumFollowSpeed
                    : Mathf.Infinity;

            currentPosition = Vector2.SmoothDamp(
                currentPosition,
                desiredPosition,
                ref followVelocity,
                followSmoothTime,
                speedCap,
                Mathf.Max(0.0001f, deltaTime)
            );
        }

        Vector2 renderedPosition =
            pixelSnapCameraTarget
                ? SnapToPixelGrid(currentPosition)
                : currentPosition;

        cameraTarget.position = new Vector3(
            renderedPosition.x,
            renderedPosition.y,
            activeAimTracker.AimOrigin.position.z
        );

        debugLeadWorldOffset = leadOffset;
        debugTargetPosition = renderedPosition;
    }

    private bool TryResolveAimTracker(bool forceSearch)
    {
        if (activeAimTracker != null &&
            activeAimTracker.isActiveAndEnabled)
        {
            return true;
        }

        activeAimTracker = null;
        boundAimTracker = "None";

        if (aimTrackerOverride != null &&
            aimTrackerOverride.isActiveAndEnabled)
        {
            BindAimTracker(aimTrackerOverride);
            return true;
        }

        if (!automaticallyFindAimTracker)
            return false;

        if (!forceSearch &&
            Time.unscaledTime < nextAimTrackerSearchTime)
        {
            return false;
        }

        nextAimTrackerSearchTime =
            Time.unscaledTime + aimTrackerSearchInterval;

        AimTracker found =
            FindFirstObjectByType<AimTracker>(
                FindObjectsInactive.Exclude
            );

        if (found == null)
            return false;

        BindAimTracker(found);
        return true;
    }

    private void BindAimTracker(AimTracker tracker)
    {
        activeAimTracker = tracker;
        boundAimTracker = tracker.name;
        EnsureCameraTarget();

        if (cameraTarget == null || cinemachineCamera == null)
            return;

        Vector3 startPosition = tracker.AimOrigin.position;
        cameraTarget.position = startPosition;
        followVelocity = Vector2.zero;

        if (!capturedOriginalFollow)
        {
            originalFollowTarget = cinemachineCamera.Follow;
            capturedOriginalFollow = true;
        }

        cinemachineCamera.Follow = cameraTarget;
    }

    private void EnsureCameraTarget()
    {
        if (cameraTarget != null)
            return;

        GameObject targetObject =
            new GameObject("Combat Camera Aim Target");

        cameraTarget = targetObject.transform;

        if (activeAimTracker != null)
            cameraTarget.position = activeAimTracker.AimOrigin.position;
    }

    private Vector2 CalculateLeadOffset(Camera outputCamera)
    {
        if (outputCamera == null ||
            Screen.width <= 0 ||
            Screen.height <= 0)
        {
            debugNormalizedCursor = Vector2.zero;
            return Vector2.zero;
        }

        if (recenterWhenCursorLeavesGameView &&
            !activeAimTracker.IsCursorInsideGameView)
        {
            debugNormalizedCursor = Vector2.zero;
            return Vector2.zero;
        }

        Vector2 cursor = activeAimTracker.CursorScreenPosition;
        Vector2 normalized = new Vector2(
            ((cursor.x / Screen.width) - 0.5f) * 2f,
            ((cursor.y / Screen.height) - 0.5f) * 2f
        );

        if (normalized.sqrMagnitude > 1f)
            normalized.Normalize();

        debugNormalizedCursor = normalized;

        float magnitude = Mathf.Clamp01(normalized.magnitude);

        if (magnitude <= centerDeadZone)
            return Vector2.zero;

        float remappedMagnitude = Mathf.InverseLerp(
            centerDeadZone,
            1f,
            magnitude
        );

        remappedMagnitude = Mathf.Pow(
            remappedMagnitude,
            leadResponseExponent
        );

        Vector2 shapedCursor =
            normalized.normalized * remappedMagnitude;

        Vector2 viewWorldSize = GetViewWorldSize(
            outputCamera,
            activeAimTracker.AimOrigin.position.z
        );

        return new Vector2(
            shapedCursor.x *
                viewWorldSize.x *
                maximumLeadScreenFraction.x,
            shapedCursor.y *
                viewWorldSize.y *
                maximumLeadScreenFraction.y
        );
    }

    private static Vector2 GetViewWorldSize(
        Camera outputCamera,
        float combatPlaneZ)
    {
        if (outputCamera.orthographic)
        {
            float height = outputCamera.orthographicSize * 2f;
            return new Vector2(height * outputCamera.aspect, height);
        }

        float depth = Mathf.Abs(
            outputCamera.transform.position.z - combatPlaneZ
        );

        Vector3 lowerLeft = outputCamera.ViewportToWorldPoint(
            new Vector3(0f, 0f, depth)
        );

        Vector3 upperRight = outputCamera.ViewportToWorldPoint(
            new Vector3(1f, 1f, depth)
        );

        return new Vector2(
            Mathf.Abs(upperRight.x - lowerLeft.x),
            Mathf.Abs(upperRight.y - lowerLeft.y)
        );
    }

    private Vector2 SnapToPixelGrid(Vector2 position)
    {
        float pixelsPerUnit = Mathf.Max(1f, assetsPixelsPerUnit);

        return new Vector2(
            Mathf.Round(position.x * pixelsPerUnit) / pixelsPerUnit,
            Mathf.Round(position.y * pixelsPerUnit) / pixelsPerUnit
        );
    }

    private void RestoreOriginalFollowTarget()
    {
        if (!restoreOriginalFollowOnDisable ||
            cinemachineCamera == null ||
            !capturedOriginalFollow)
        {
            return;
        }

        if (cinemachineCamera.Follow == cameraTarget)
            cinemachineCamera.Follow = originalFollowTarget;
    }

    private void OnDisable()
    {
        RestoreOriginalFollowTarget();

        if (cameraTarget != null)
        {
            Destroy(cameraTarget.gameObject);
            cameraTarget = null;
        }

        activeAimTracker = null;
        followVelocity = Vector2.zero;
        boundAimTracker = "None";
        capturedOriginalFollow = false;
        originalFollowTarget = null;
    }

    private void OnValidate()
    {
        maximumLeadScreenFraction.x = Mathf.Max(
            0f,
            maximumLeadScreenFraction.x
        );

        maximumLeadScreenFraction.y = Mathf.Max(
            0f,
            maximumLeadScreenFraction.y
        );

        aimTrackerSearchInterval = Mathf.Max(
            0.05f,
            aimTrackerSearchInterval
        );

        followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
        maximumFollowSpeed = Mathf.Max(0f, maximumFollowSpeed);
        teleportSnapDistance = Mathf.Max(0f, teleportSnapDistance);
        assetsPixelsPerUnit = Mathf.Max(1f, assetsPixelsPerUnit);
    }
}
