using UnityEngine;

/// <summary>
/// Authoritative mouse-aim source for the shared combat pawn.
/// Existing attacks can continue reading AimDir, while the combat camera and
/// reticle can share the exact same cursor and world-aim information.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class AimTracker : MonoBehaviour
{
    [Header("Aim Source")]
    [SerializeField] private Transform aimOrigin;

    [Tooltip("Optional camera override. Leave empty to use the active Main Camera.")]
    [SerializeField] private Camera aimCamera;

    [Tooltip("Refresh Camera.main automatically if the assigned camera is missing or disabled.")]
    [SerializeField] private bool automaticallyUseMainCamera = true;

    public Vector2 AimDir { get; private set; } = Vector2.up;
    public Vector2 AimWorldPosition { get; private set; }
    public Vector2 CursorScreenPosition { get; private set; }
    public bool IsCursorInsideGameView { get; private set; }
    public Camera AimCamera => aimCamera;
    public Transform AimOrigin => aimOrigin != null ? aimOrigin : transform;

    private void Awake()
    {
        if (aimOrigin == null)
            aimOrigin = transform;

        AimWorldPosition =
            (Vector2)AimOrigin.position + AimDir;
    }

    private void OnEnable()
    {
        RefreshCameraIfNeeded();
        RefreshAimImmediately();
    }

    private void Update()
    {
        RefreshAimImmediately();
    }

    /// <summary>
    /// Refreshes the cached cursor, world position, and normalized aim direction.
    /// Public so another system can force a same-frame refresh when necessary.
    /// </summary>
    public void RefreshAimImmediately()
    {
        RefreshCameraIfNeeded();

        CursorScreenPosition = Input.mousePosition;
        IsCursorInsideGameView =
            CursorScreenPosition.x >= 0f &&
            CursorScreenPosition.y >= 0f &&
            CursorScreenPosition.x <= Screen.width &&
            CursorScreenPosition.y <= Screen.height;

        if (aimCamera == null)
            return;

        Vector3 origin = AimOrigin.position;
        Ray cursorRay = aimCamera.ScreenPointToRay(
            new Vector3(
                CursorScreenPosition.x,
                CursorScreenPosition.y,
                0f
            )
        );

        Plane combatPlane = new Plane(
            Vector3.forward,
            new Vector3(0f, 0f, origin.z)
        );

        if (!combatPlane.Raycast(cursorRay, out float rayDistance))
            return;

        Vector3 worldPoint = cursorRay.GetPoint(rayDistance);
        AimWorldPosition = new Vector2(worldPoint.x, worldPoint.y);

        Vector2 delta =
            AimWorldPosition - (Vector2)origin;

        if (delta.sqrMagnitude > 0.0001f)
            AimDir = delta.normalized;
    }

    public void SetAimCamera(Camera newCamera)
    {
        aimCamera = newCamera;
        RefreshAimImmediately();
    }

    private void RefreshCameraIfNeeded()
    {
        if (!automaticallyUseMainCamera)
            return;

        Camera activeMainCamera = Camera.main;

        if (activeMainCamera != null &&
            aimCamera != activeMainCamera)
        {
            aimCamera = activeMainCamera;
            return;
        }

        if (aimCamera == null || !aimCamera.isActiveAndEnabled)
            aimCamera = activeMainCamera;
    }
}
