using ProjectEri.SkillSystemV2;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the project-wide normal-time baseline and prevents a temporary combat
/// slowdown from leaking into exploration or a newly loaded scene.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class GlobalGameplayTimeGuard : MonoBehaviour
{
    private const float NormalTimeScale = 1f;
    private const float DefaultFixedDeltaTime = 0.02f;
    private const float NormalScaleThreshold = 0.999f;
    private const float MinimumRecoveryDelay = 0.05f;

    private static GlobalGameplayTimeGuard instance;

    [Header("Orphaned Slow-Motion Recovery")]
    [Tooltip(
        "Restore normal time when the world remains slowed but no known " +
        "gameplay system owns that slowdown.")]
    [SerializeField] private bool recoverOrphanedSlowMotion = true;

    [Tooltip(
        "Unscaled grace period before recovery. This lets menu, focus, " +
        "targeting, and hitstop hand ownership to each other safely.")]
    [SerializeField, Min(MinimumRecoveryDelay)]
    private float orphanedRecoveryDelay = 0.35f;

    [SerializeField] private bool logRecoveries = true;

    private float normalFixedDeltaTime = DefaultFixedDeltaTime;
    private float orphanedSinceRealtime = -1f;

    private CombatSkillMenuController skillMenu;
    private PlayerFocusMode focusMode;
    private TargetingTimeScaleController targetingTime;

    public static float NormalFixedDeltaTime =>
        instance != null
            ? instance.normalFixedDeltaTime
            : DefaultFixedDeltaTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallBeforeSceneLoad()
    {
        if (instance != null)
            return;

        GlobalGameplayTimeGuard existing =
            FindObjectOfType<GlobalGameplayTimeGuard>(true);
        if (existing != null)
        {
            instance = existing;
            return;
        }

        GameObject owner = new GameObject("[Global Gameplay Time Guard]");
        owner.AddComponent<GlobalGameplayTimeGuard>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        normalFixedDeltaTime = ResolveNormalFixedDeltaTime(
            Time.timeScale,
            Time.fixedDeltaTime);

        SceneManager.sceneLoaded += OnSceneLoaded;
        NormalizeTime("play-mode startup", false);
    }

    private void Update()
    {
        if (!recoverOrphanedSlowMotion)
            return;

        float currentScale = Time.timeScale;

        // A true pause is deliberate (for example, combat results). Only
        // recover positive slow-motion values.
        if (currentScale <= 0f || currentScale >= NormalScaleThreshold)
        {
            orphanedSinceRealtime = -1f;
            return;
        }

        ResolveKnownTimeOwners();
        if (HasKnownTimeOwner())
        {
            orphanedSinceRealtime = -1f;
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (orphanedSinceRealtime < 0f)
        {
            orphanedSinceRealtime = now;
            return;
        }

        if (now - orphanedSinceRealtime <
            Mathf.Max(MinimumRecoveryDelay, orphanedRecoveryDelay))
        {
            return;
        }

        NormalizeTime("unowned slow motion", logRecoveries);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearCachedOwners();

        // Additive content can be loaded while a live gameplay owner is
        // intentionally slowing time. A replacement scene always starts from
        // the project baseline and must never inherit the previous scene's
        // menu, targeting, focus, test, or hitstop scale.
        if (mode == LoadSceneMode.Single)
            NormalizeTime($"scene load: {scene.name}", logRecoveries);
    }

    private void NormalizeTime(string reason, bool shouldLog)
    {
        float previousScale = Time.timeScale;
        float previousFixedDelta = Time.fixedDeltaTime;

        HitstopManager.ReleaseForExternalTimeControl();
        Time.timeScale = NormalTimeScale;
        Time.fixedDeltaTime = normalFixedDeltaTime;
        orphanedSinceRealtime = -1f;

        if (shouldLog &&
            (!Mathf.Approximately(previousScale, NormalTimeScale) ||
             !Mathf.Approximately(
                 previousFixedDelta,
                 normalFixedDeltaTime)))
        {
            Debug.LogWarning(
                $"GlobalGameplayTimeGuard restored normal time ({reason}). " +
                $"TimeScale {previousScale:0.####} -> 1, FixedDelta " +
                $"{previousFixedDelta:0.####} -> " +
                $"{normalFixedDeltaTime:0.####}.",
                this);
        }
    }

    private void ResolveKnownTimeOwners()
    {
        if (skillMenu == null)
            skillMenu = FindObjectOfType<CombatSkillMenuController>(true);

        if (focusMode == null)
            focusMode = FindObjectOfType<PlayerFocusMode>(true);

        if (targetingTime == null)
        {
            targetingTime =
                FindObjectOfType<TargetingTimeScaleController>(true);
        }
    }

    private bool HasKnownTimeOwner()
    {
        return HitstopManager.IsControllingTime ||
               (skillMenu != null && skillMenu.IsOpen) ||
               (focusMode != null &&
                focusMode.IsControllingGlobalTime) ||
               (targetingTime != null && targetingTime.HasRequests);
    }

    private void ClearCachedOwners()
    {
        skillMenu = null;
        focusMode = null;
        targetingTime = null;
        orphanedSinceRealtime = -1f;
    }

    private static float ResolveNormalFixedDeltaTime(
        float inheritedTimeScale,
        float inheritedFixedDelta)
    {
        float fixedDelta = inheritedFixedDelta > 0f
            ? inheritedFixedDelta
            : DefaultFixedDeltaTime;

        if (inheritedTimeScale > 0.0001f)
        {
            float unscaledCandidate = fixedDelta / inheritedTimeScale;
            if (unscaledCandidate >= 0.005f &&
                unscaledCandidate <= 0.05f)
            {
                return unscaledCandidate;
            }
        }

        return fixedDelta >= 0.005f && fixedDelta <= 0.05f
            ? fixedDelta
            : DefaultFixedDeltaTime;
    }

    [ContextMenu("Log Global Time Ownership")]
    private void LogGlobalTimeOwnership()
    {
        ResolveKnownTimeOwners();
        Debug.Log(
            $"Global Time: scale={Time.timeScale:0.####}, " +
            $"fixedDelta={Time.fixedDeltaTime:0.####}, " +
            $"hitstop={HitstopManager.IsControllingTime}, " +
            $"hitstopDeferred={HitstopManager.HasDeferredRestore}, " +
            $"skillMenu={(skillMenu != null && skillMenu.IsOpen)}, " +
            $"focus={(focusMode != null && focusMode.IsControllingGlobalTime)}, " +
            $"targeting={(targetingTime != null && targetingTime.HasRequests)}.",
            this);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Unity can retain these static values when Enter Play Mode Options
        // disable a domain reload. Always leave the Editor at the project
        // baseline when the persistent guard is torn down.
        Time.timeScale = NormalTimeScale;
        Time.fixedDeltaTime = normalFixedDeltaTime;
        instance = null;
    }
}
