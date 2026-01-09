using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

public class MapCameraBinder : MonoBehaviour
{
    [Header("Scene Camera")]
    [SerializeField] private CinemachineCamera cineCamera;
    [SerializeField] private CinemachineConfiner2D confiner;

    [Header("Scene Bounds")]
    [SerializeField] private Collider2D cameraBounds;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindNow();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindNow();
    }

    public void BindNow()
    {
        if (cineCamera == null) cineCamera = FindObjectOfType<CinemachineCamera>(true);
        if (confiner == null) confiner = FindObjectOfType<CinemachineConfiner2D>(true);

        Transform playerTf = null;
        if (PlayerSingleton.Instance != null) playerTf = PlayerSingleton.Instance.transform;
        else
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTf = p.transform;
        }

        if (cineCamera != null && playerTf != null)
            cineCamera.Follow = playerTf;

        if (confiner != null && cameraBounds != null)
        {
            confiner.BoundingShape2D = cameraBounds;
            confiner.InvalidateCache();
        }
    }
}
