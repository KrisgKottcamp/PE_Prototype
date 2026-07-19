using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Shared combat camera-impact service built on Cinemachine Impulse.
/// It creates one persistent impulse source and ensures the active Main Camera
/// has an external listener, so scene cameras do not need manual setup.
/// </summary>
[DisallowMultipleComponent]
public class CombatCameraShake : MonoBehaviour
{
    public static CombatCameraShake Instance { get; private set; }

    [Header("Global Camera Shake")]
    [SerializeField] private bool cameraShakeEnabled = true;

    [Tooltip("Multiplies every configured impact strength.")]
    [SerializeField, Range(0f, 2f)] private float globalStrengthMultiplier = 1f;

    [Tooltip("Safety cap for one impulse so overlapping heavy hits remain readable.")]
    [SerializeField, Min(0.05f)] private float maximumStrength = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool logRequests;
    [SerializeField] private string activeListenerCamera = "None";

    private CinemachineImpulseSource impulseSource;
    private Camera listenerCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        EnsureImpulseSource();
    }

    public static void Request(
        CameraShakeSettings settings,
        Vector3 worldPosition,
        Vector2 impactDirection,
        float strengthMultiplier = 1f)
    {
        if (!settings.enabled ||
            settings.strength <= 0f ||
            settings.duration <= 0f ||
            strengthMultiplier <= 0f)
        {
            return;
        }

        CombatCameraShake manager = GetOrCreateInstance();

        manager?.GenerateImpulse(
            settings,
            worldPosition,
            impactDirection,
            strengthMultiplier
        );
    }

    private static CombatCameraShake GetOrCreateInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindObjectOfType<CombatCameraShake>(true);

        if (Instance != null)
            return Instance;

        GameObject managerObject =
            new GameObject("Combat Camera Shake");

        return managerObject.AddComponent<CombatCameraShake>();
    }

    private void GenerateImpulse(
        CameraShakeSettings settings,
        Vector3 worldPosition,
        Vector2 impactDirection,
        float strengthMultiplier)
    {
        if (!isActiveAndEnabled || !cameraShakeEnabled)
            return;

        EnsureImpulseSource();

        if (impulseSource == null || !EnsureListener())
            return;

        float strength = Mathf.Clamp(
            settings.strength *
            Mathf.Max(0f, strengthMultiplier) *
            globalStrengthMultiplier,
            0f,
            Mathf.Max(0.05f, maximumStrength)
        );

        if (strength <= 0f)
            return;

        Vector2 direction = impactDirection;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Random.insideUnitCircle.normalized;
        else
            direction.Normalize();

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;

        CinemachineImpulseDefinition definition =
            impulseSource.ImpulseDefinition;

        definition.ImpulseType =
            CinemachineImpulseDefinition.ImpulseTypes.Uniform;

        definition.ImpulseShape =
            CinemachineImpulseDefinition.ImpulseShapes.Explosion;

        definition.ImpulseDuration =
            Mathf.Max(0.01f, settings.duration);

        definition.ImpulseChannel = 1;

        impulseSource.GenerateImpulseAtPositionWithVelocity(
            worldPosition,
            new Vector3(direction.x, direction.y, 0f) * strength
        );

        if (logRequests)
        {
            Debug.Log(
                $"Camera shake: {strength:0.00} for {settings.duration:0.00}s on {activeListenerCamera}.",
                this
            );
        }
    }

    private void EnsureImpulseSource()
    {
        if (impulseSource == null)
            impulseSource = GetComponent<CinemachineImpulseSource>();

        if (impulseSource == null)
            impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();

        // Combat shake should finish in real time even while hitstop reduces
        // Time.timeScale to a very small value.
        CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
    }

    private bool EnsureListener()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            activeListenerCamera = "No MainCamera";
            return false;
        }

        if (listenerCamera != mainCamera)
        {
            listenerCamera = mainCamera;
            activeListenerCamera = mainCamera.name;
        }

        CinemachineExternalImpulseListener listener =
            mainCamera.GetComponent<CinemachineExternalImpulseListener>();

        if (listener == null)
        {
            listener = mainCamera.gameObject.AddComponent<CinemachineExternalImpulseListener>();
            listener.ChannelMask = 1;
            listener.Gain = 1f;
            listener.Use2DDistance = true;
            listener.UseLocalSpace = true;
        }

        return listener != null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
