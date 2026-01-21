using UnityEngine;

public class ExitTrigger : MonoBehaviour
{
    [SerializeField] private string targetSceneName = "Map_02";
    [SerializeField] private string targetSpawnId = "From_Map_01";

    private bool used;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (used) return;

        // Accept Player collider or any child collider under the Player root.
        bool isPlayer =
            other.CompareTag("Player") ||
            other.GetComponentInParent<PlayerSingleton>() != null;

        if (!isPlayer) return;

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("ExitTrigger: No SceneTransitionManager.Instance found.");
            return;
        }

        used = true;
        SceneTransitionManager.Instance.TransitionTo(targetSceneName, targetSpawnId);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // Allows re-entering if you step out and back in.
        bool isPlayer =
            other.CompareTag("Player") ||
            other.GetComponentInParent<PlayerSingleton>() != null;

        if (isPlayer) used = false;
    }
}
