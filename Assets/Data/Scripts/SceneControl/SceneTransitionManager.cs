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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Keep fade UI alive too
        if (fadeCanvasGroup != null)
            DontDestroyOnLoad(fadeCanvasGroup.transform.root.gameObject);
    }

    public void TransitionTo(string sceneName, string spawnId)
    {
        if (isTransitioning) return;
        pendingSpawnId = spawnId;
        StartCoroutine(DoTransition(sceneName));
    }

    private IEnumerator DoTransition(string sceneName)
    {
        isTransitioning = true;

        if (fadeCanvasGroup == null)
        {
            Debug.LogError("SceneTransitionManager: Fade CanvasGroup is not assigned.");
            isTransitioning = false;
            yield break;
        }

        yield return Fade(1f, fadeOutDuration);

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"Scene '{sceneName}' cannot be loaded. Add it to File > Build Profiles > Scenes.");
            yield return Fade(0f, fadeInDuration);
            isTransitioning = false;
            yield break;
        }

        var loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (loadOp == null)
        {
            Debug.LogError($"LoadSceneAsync returned null for '{sceneName}'.");
            yield return Fade(0f, fadeInDuration);
            isTransitioning = false;
            yield break;
        }

        while (!loadOp.isDone)
            yield return null;

        // Get player
        Transform playerTf = PlayerSingleton.Instance != null ? PlayerSingleton.Instance.transform : null;
        if (playerTf == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTf = p.transform;
        }

        if (playerTf == null)
        {
            Debug.LogError("SceneTransitionManager: No Player found (tag Player).");
            yield return Fade(0f, fadeInDuration);
            isTransitioning = false;
            yield break;
        }

        Vector3 oldPos = playerTf.position;
        bool placed = PlacePlayerAtSpawn(playerTf, pendingSpawnId);
        Vector3 newPos = playerTf.position;
        Vector3 delta = newPos - oldPos;

        // Wait a frame so scene objects (camera binder, confiner) run Start/OnEnable
        yield return null;

        // Rebind camera in the new scene (in case tag was wrong earlier or binder did not run)
        var binder = FindObjectOfType<MapCameraBinder>(true);
        if (binder != null) binder.BindNow();

        // Kill any leftover physics movement
        var rb2d = playerTf.GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            rb2d.linearVelocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }

        // Prevent �gcamera shake�h by telling Cinemachine the target warped
        CinemachineCore.OnTargetObjectWarped(playerTf, delta);

        var cmCam = FindObjectOfType<CinemachineCamera>(true);
        if (cmCam != null) cmCam.PreviousStateIsValid = false;

        var confiner = FindObjectOfType<CinemachineConfiner2D>(true);
        if (confiner != null) confiner.InvalidateCache();

        // Optional extra settle frame while still black
        yield return null;

        yield return Fade(0f, fadeInDuration);

        if (!placed)
            Debug.LogWarning($"Spawn '{pendingSpawnId}' not found in scene '{sceneName}'. Player stayed at {playerTf.position}.");

        isTransitioning = false;
    }

    private bool PlacePlayerAtSpawn(Transform playerTf, string spawnId)
    {
        var spawns = FindObjectsOfType<SpawnPoint>(true);
        for (int i = 0; i < spawns.Length; i++)
        {
            if (spawns[i].SpawnId != spawnId) continue;

            Vector3 s = spawns[i].transform.position;
            Vector3 target = new Vector3(s.x, s.y, playerTf.position.z);

            var rb2d = playerTf.GetComponent<Rigidbody2D>();
            if (rb2d != null)
                rb2d.position = new Vector2(target.x, target.y);
            else
                playerTf.position = target;

            return true;
        }

        return false;
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        float startAlpha = fadeCanvasGroup.alpha;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, k);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }
}
