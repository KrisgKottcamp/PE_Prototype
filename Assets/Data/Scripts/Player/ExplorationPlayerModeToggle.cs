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
        ApplyForCombatState(IsCombatSceneLoaded());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The combat HUD is loaded additively after the arena. Looking only at
        // the callback scene would treat that HUD as overworld content and
        // re-enable the persistent overworld player.
        ApplyForCombatState(IsCombatSceneLoaded());
    }

    private bool IsCombatSceneLoaded()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.name.StartsWith(combatScenePrefix))
                return true;
        }

        return false;
    }

    private void ApplyForCombatState(bool inCombat)
    {
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
