using UnityEngine;
using UnityEngine.SceneManagement;

public class BootstrapLoader : MonoBehaviour
{
    [SerializeField] private string firstSceneName = "TestMap_01";

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != firstSceneName)
            SceneManager.LoadScene(firstSceneName);
    }
}
