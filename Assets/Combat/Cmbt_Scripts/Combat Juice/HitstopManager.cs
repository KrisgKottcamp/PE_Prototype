using UnityEngine;

/// <summary>
/// Per-hit tuning data. Add this to any future attack source and request it
/// only after that source confirms that it dealt damage.
/// </summary>
[System.Serializable]
public struct HitstopSettings
{
    [Tooltip("Allows this attack's hitstop to be disabled without changing its tuning values.")]
    public bool enabled;

    [Tooltip("Real-time duration in seconds. This is unaffected by Time.timeScale.")]
    [Min(0f)]
    public float duration;

    [Tooltip("Temporary Time.timeScale during the hit. Smaller values feel heavier. Keep above zero so Unity can continue updating.")]
    [Range(0.001f, 1f)]
    public float timeScale;

    public HitstopSettings(bool enabled, float duration, float timeScale)
    {
        this.enabled = enabled;
        this.duration = Mathf.Max(0f, duration);
        this.timeScale = Mathf.Clamp(timeScale, 0.001f, 1f);
    }

    public static HitstopSettings Create(float duration, float timeScale)
    {
        return new HitstopSettings(true, duration, timeScale);
    }
}

/// <summary>
/// Shared hitstop service for all combat sources.
///
/// The first hit creates this automatically, so scene setup is optional.
/// Add it to a persistent scene object if global tuning controls are desired.
/// Overlapping hits extend the stop and keep the strongest requested scale.
/// </summary>
[DisallowMultipleComponent]
public class HitstopManager : MonoBehaviour
{
    public static HitstopManager Instance { get; private set; }

    [Header("Global Hitstop Tuning")]
    [SerializeField] private bool hitstopEnabled = true;

    [Tooltip("Multiplies every attack's configured duration. Useful for broad feel passes.")]
    [SerializeField, Range(0.1f, 3f)]
    private float globalDurationMultiplier = 1f;

    [Tooltip("Safety floor for requested time scales.")]
    [SerializeField, Range(0.001f, 0.25f)]
    private float minimumTimeScale = 0.005f;

    [Tooltip("Safety cap for one hitstop request. Overlapping hits may extend the total window.")]
    [SerializeField, Min(0.01f)]
    private float maximumRequestDuration = 0.15f;

    [Tooltip("Separate safety cap for deliberately sustained effects such as Audrey's three-click combo window.")]
    [SerializeField, Min(0.1f)]
    private float maximumSustainedDuration = 2f;

    [Header("Debug")]
    [SerializeField] private bool logRequests = false;

    private bool isStopping;
    private bool isReleasing;
    private bool isBlending;
    private float stopUntilRealtime;
    private float restoreTimeScale = 1f;
    private float appliedTimeScale = 1f;
    private float lastManagerScale = 1f;
    private float blendFromScale = 1f;
    private float blendToScale = 1f;
    private float blendStartedRealtime;
    private float blendDuration;
    private float releaseBlendDuration;

    public bool IsStopping => isStopping;

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
    }

    private void Update()
    {
        if (!isStopping)
            return;

        // Yield to a true pause or another controller that deliberately takes
        // ownership of time scale during our effect.
        if (!Mathf.Approximately(Time.timeScale, lastManagerScale))
        {
            isStopping = false;
            isReleasing = false;
            isBlending = false;
            return;
        }

        float now = Time.realtimeSinceStartup;

        if (!isReleasing && now >= stopUntilRealtime)
            BeginRelease(now);

        if (isBlending)
            UpdateBlend(now);
    }

    /// <summary>
    /// Requests hitstop using per-source tuning. The optional multiplier is
    /// useful for scaling one preset by damage without creating many presets.
    /// </summary>
    public static void Request(
        HitstopSettings settings,
        float durationMultiplier = 1f)
    {
        if (!settings.enabled ||
            settings.duration <= 0f ||
            durationMultiplier <= 0f)
        {
            return;
        }

        HitstopManager manager = GetOrCreateInstance();
        manager?.BeginHitstop(
            settings,
            durationMultiplier,
            bypassSingleHitDurationCap: false,
            blendIn: 0f,
            blendOut: 0f
        );
    }

    /// <summary>
    /// Sustained variant for a refreshed combo window. It uses a separate
    /// duration safety cap and supports smooth unscaled-time transitions.
    /// </summary>
    public static void RequestSustained(
        HitstopSettings settings,
        float blendIn,
        float blendOut,
        float durationMultiplier = 1f)
    {
        if (!settings.enabled ||
            settings.duration <= 0f ||
            durationMultiplier <= 0f)
        {
            return;
        }

        HitstopManager manager = GetOrCreateInstance();
        manager?.BeginHitstop(
            settings,
            durationMultiplier,
            bypassSingleHitDurationCap: true,
            blendIn: blendIn,
            blendOut: blendOut
        );
    }

    /// <summary>
    /// Ends the current hitstop before a longer-lived time controller records
    /// its baseline. Without this handoff, a menu or focus mode can remember a
    /// temporary hitstop/blend scale and later restore it as if it were normal
    /// gameplay speed, leaving the simulation permanently slow and stuttery.
    /// </summary>
    public static void ReleaseForExternalTimeControl()
    {
        if (Instance == null || !Instance.isStopping)
            return;

        Instance.FinishHitstop();
    }

    private static HitstopManager GetOrCreateInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindObjectOfType<HitstopManager>(true);

        if (Instance != null)
            return Instance;

        GameObject managerObject =
            new GameObject("Hitstop Manager");

        return managerObject.AddComponent<HitstopManager>();
    }

    private void BeginHitstop(
        HitstopSettings settings,
        float durationMultiplier,
        bool bypassSingleHitDurationCap,
        float blendIn,
        float blendOut)
    {
        if (!isActiveAndEnabled ||
            !hitstopEnabled ||
            Time.timeScale <= 0f)
            return;

        float durationCap = bypassSingleHitDurationCap
            ? Mathf.Max(
                0.5f,
                maximumSustainedDuration
            )
            : maximumRequestDuration;

        float duration = Mathf.Clamp(
            settings.duration *
            Mathf.Max(0f, durationMultiplier) *
            globalDurationMultiplier,
            0f,
            durationCap
        );

        if (duration <= 0f)
            return;

        float requestedScale = Mathf.Clamp(
            settings.timeScale,
            minimumTimeScale,
            1f
        );

        float now = Time.realtimeSinceStartup;
        float safeBlendIn = Mathf.Max(0f, blendIn);
        float safeBlendOut = Mathf.Max(0f, blendOut);

        if (!isStopping)
        {
            isStopping = true;
            isReleasing = false;
            restoreTimeScale = Time.timeScale;
            appliedTimeScale = Mathf.Min(
                restoreTimeScale,
                requestedScale
            );
            stopUntilRealtime = now + duration;
            releaseBlendDuration = safeBlendOut;

            StartBlend(
                restoreTimeScale,
                appliedTimeScale,
                safeBlendIn,
                now
            );
        }
        else
        {
            float previousTargetScale = appliedTimeScale;

            appliedTimeScale = Mathf.Min(
                appliedTimeScale,
                requestedScale
            );
            stopUntilRealtime = Mathf.Max(
                stopUntilRealtime,
                now + duration
            );

            releaseBlendDuration = Mathf.Max(
                releaseBlendDuration,
                safeBlendOut
            );

            if (isReleasing ||
                appliedTimeScale < previousTargetScale)
            {
                isReleasing = false;

                StartBlend(
                    Time.timeScale,
                    appliedTimeScale,
                    safeBlendIn,
                    now
                );
            }
        }

        if (logRequests)
        {
            Debug.Log(
                $"Hitstop: {duration:0.000}s at {appliedTimeScale:0.000} scale.",
                this
            );
        }
    }

    private void StartBlend(
        float from,
        float to,
        float duration,
        float now)
    {
        blendFromScale = from;
        blendToScale = to;
        blendStartedRealtime = now;
        blendDuration = Mathf.Max(0f, duration);

        if (blendDuration <= 0f)
        {
            isBlending = false;
            SetManagerScale(to);
            return;
        }

        isBlending = true;
        SetManagerScale(from);
    }

    private void UpdateBlend(float now)
    {
        float t = blendDuration <= 0f
            ? 1f
            : Mathf.Clamp01(
                (now - blendStartedRealtime) /
                blendDuration
            );

        float eased = t * t * (3f - 2f * t);

        SetManagerScale(
            Mathf.Lerp(
                blendFromScale,
                blendToScale,
                eased
            )
        );

        if (t < 1f)
            return;

        isBlending = false;

        if (isReleasing)
            FinishHitstop();
    }

    private void BeginRelease(float now)
    {
        isReleasing = true;

        StartBlend(
            Time.timeScale,
            restoreTimeScale,
            releaseBlendDuration,
            now
        );

        if (!isBlending)
            FinishHitstop();
    }

    private void SetManagerScale(float scale)
    {
        // Never change Time.fixedDeltaTime here. Hitstop controls only the
        // simulation scale; the project's physics frequency remains untouched.
        lastManagerScale = scale;
        Time.timeScale = scale;
    }

    private void FinishHitstop()
    {
        if (!isStopping)
            return;

        // Do not overwrite a pause or another system that deliberately changed
        // time scale during the very short hitstop window.
        if (Mathf.Approximately(Time.timeScale, lastManagerScale))
            Time.timeScale = restoreTimeScale;

        isStopping = false;
        isReleasing = false;
        isBlending = false;
        lastManagerScale = restoreTimeScale;
    }

    private void OnDisable()
    {
        FinishHitstop();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        FinishHitstop();
        Instance = null;
    }
}
