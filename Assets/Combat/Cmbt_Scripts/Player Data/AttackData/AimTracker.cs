using UnityEngine;

public class AimTracker : MonoBehaviour
{
    [SerializeField] private Transform aimOrigin; // defaults to transform
    public Vector2 AimDir { get; private set; } = Vector2.up;

    private Camera cam;

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
            AimDir = delta.normalized;
    }
}
