using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float fadeInDuration = 0.25f;

    private bool isTransitioning;
    private string pendingSpawnId;

    private bool pendingUseWorldPosition;
    private Vector3 pendingWorldPosition;
    private float pendingEncounterGraceSeconds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (fadeCanvasGroup != null)
            DontDestroyOnLoad(fadeCanvasGroup.transform.root.gameObject);
    }

    public void TransitionTo(string sceneName, string spawnId)
    {
        if (isTransitioning)
            return;

        pendingSpawnId = spawnId;
        pendingUseWorldPosition = false;
        pendingWorldPosition = Vector3.zero;
        pendingEncounterGraceSeconds = 0f;

        StartCoroutine(DoTransition(sceneName));
    }

    public void TransitionToPosition(
        string sceneName,
        Vector3 worldPosition,
        float encounterGraceSeconds = 0f)
    {
        if (isTransitioning)
            return;

        pendingSpawnId = string.Empty;
        pendingUseWorldPosition = true;
        pendingWorldPosition = worldPosition;
        pendingEncounterGraceSeconds =
            Mathf.Max(0f, encounterGraceSeconds);

        StartCoroutine(DoTransition(sceneName));
    }

    private IEnumerator DoTransition(string sceneName)
    {
        isTransitioning = true;

        bool useWorldPosition = pendingUseWorldPosition;
        Vector3 worldPosition = pendingWorldPosition;
        float graceSeconds = pendingEncounterGraceSeconds;
        string spawnId = pendingSpawnId;

        if (fadeCanvasGroup == null)
        {
            Debug.LogError(
                "SceneTransitionManager: Fade CanvasGroup is not assigned."
            );

            ResetPendingTransition();
            yield break;
        }

        yield return Fade(1f, fadeOutDuration);

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"Scene '{sceneName}' cannot be loaded. Add it to " +
                "File > Build Profiles > Scenes."
            );

            yield return Fade(0f, fadeInDuration);
            ResetPendingTransition();
            yield break;
        }

        AsyncOperation loadOp = SceneManager.LoadSceneAsync(
            sceneName,
            LoadSceneMode.Single
        );

        if (loadOp == null)
        {
            Debug.LogError(
                $"LoadSceneAsync returned null for '{sceneName}'."
            );

            yield return Fade(0f, fadeInDuration);
            ResetPendingTransition();
            yield break;
        }

        while (!loadOp.isDone)
            yield return null;

        Transform playerTf = PlayerSingleton.Instance != null
            ? PlayerSingleton.Instance.transform
            : null;

        if (playerTf == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
                playerTf = playerObject.transform;
        }

        if (playerTf == null)
        {
            Debug.LogError(
                "SceneTransitionManager: No Player found (tag Player)."
            );

            yield return Fade(0f, fadeInDuration);
            ResetPendingTransition();
            yield break;
        }

        Vector3 oldPosition = playerTf.position;

        bool placed = useWorldPosition
            ? PlacePlayerAtWorldPosition(playerTf, worldPosition)
            : PlacePlayerAtSpawn(playerTf, spawnId);

        Vector3 newPosition = playerTf.position;
        Vector3 delta = newPosition - oldPosition;

        StopBody(playerTf);

        if (useWorldPosition && graceSeconds > 0f)
        {
            OverworldEncounterGracePeriod gracePeriod =
                playerTf.GetComponent<
                    OverworldEncounterGracePeriod
                >();

            if (gracePeriod == null)
            {
                gracePeriod =
                    playerTf.gameObject.AddComponent<
                        OverworldEncounterGracePeriod
                    >();
            }

            gracePeriod.BeginGracePeriod(graceSeconds);
        }

        // Allow scene Start methods to run. The matching OverworldEncounter
        // restores its saved position during this frame.
        yield return null;

        MapCameraBinder binder =
            FindObjectOfType<MapCameraBinder>(true);

        if (binder != null)
            binder.BindNow();

        CinemachineCore.OnTargetObjectWarped(playerTf, delta);

        CinemachineCamera cmCam =
            FindObjectOfType<CinemachineCamera>(true);

        if (cmCam != null)
            cmCam.PreviousStateIsValid = false;

        CinemachineConfiner2D confiner =
            FindObjectOfType<CinemachineConfiner2D>(true);

        if (confiner != null)
            confiner.InvalidateCache();

        if (useWorldPosition)
        {
            CombatContext.Instance?.ClearOverworldEncounterMetadata();
        }

        yield return null;
        yield return Fade(0f, fadeInDuration);

        if (!placed && !useWorldPosition)
        {
            Debug.LogWarning(
                $"Spawn '{spawnId}' not found in scene '{sceneName}'. " +
                $"Player stayed at {playerTf.position}."
            );
        }

        ResetPendingTransition();
    }

    private static bool PlacePlayerAtWorldPosition(
        Transform playerTf,
        Vector3 worldPosition)
    {
        Vector3 target = new(
            worldPosition.x,
            worldPosition.y,
            playerTf.position.z
        );

        Rigidbody2D body = playerTf.GetComponent<Rigidbody2D>();

        if (body != null)
            body.position = new Vector2(target.x, target.y);

        playerTf.position = target;
        return true;
    }

    private bool PlacePlayerAtSpawn(
        Transform playerTf,
        string spawnId)
    {
        SpawnPoint[] spawns =
            FindObjectsOfType<SpawnPoint>(true);

        for (int i = 0; i < spawns.Length; i++)
        {
            if (spawns[i].SpawnId != spawnId)
                continue;

            Vector3 spawnPosition = spawns[i].transform.position;
            Vector3 target = new(
                spawnPosition.x,
                spawnPosition.y,
                playerTf.position.z
            );

            Rigidbody2D body =
                playerTf.GetComponent<Rigidbody2D>();

            if (body != null)
                body.position = new Vector2(target.x, target.y);
            else
                playerTf.position = target;

            return true;
        }

        return false;
    }

    private static void StopBody(Transform playerTf)
    {
        Rigidbody2D body = playerTf.GetComponent<Rigidbody2D>();

        if (body == null)
            return;

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    private void ResetPendingTransition()
    {
        pendingSpawnId = string.Empty;
        pendingUseWorldPosition = false;
        pendingWorldPosition = Vector3.zero;
        pendingEncounterGraceSeconds = 0f;
        isTransitioning = false;
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        float startAlpha = fadeCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float normalized = duration <= 0f
                ? 1f
                : Mathf.Clamp01(elapsed / duration);

            fadeCanvasGroup.alpha = Mathf.Lerp(
                startAlpha,
                targetAlpha,
                normalized
            );

            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }
}
