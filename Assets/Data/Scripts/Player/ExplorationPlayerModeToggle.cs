using UnityEngine;
using UnityEngine.SceneManagement;

public class ExplorationPlayerModeToggle : MonoBehaviour
{
    [Header("Disable these during combat")]
    [SerializeField] private MonoBehaviour[] scriptsToDisable;
    [SerializeField] private SpriteRenderer[] spriteRenderersToHide;
    [SerializeField] private Collider2D[] collidersToDisable;

    [Header("Combat scene detection")]
    [SerializeField] private string combatScenePrefix = "Combat"; // "Combat_Arena_Test" matches this

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForScene(scene.name);
    }

    private void ApplyForScene(string sceneName)
    {
        bool inCombat = sceneName.StartsWith(combatScenePrefix);

        // Disable exploration control scripts (TopDownMover, SortByY if needed, etc.)
        foreach (var s in scriptsToDisable)
            if (s) s.enabled = !inCombat;

        // Hide the sprite(s)
        foreach (var r in spriteRenderersToHide)
            if (r) r.enabled = !inCombat;

        // Disable colliders (so it does not interfere with combat collisions)
        foreach (var c in collidersToDisable)
            if (c) c.enabled = !inCombat;
    }
}
