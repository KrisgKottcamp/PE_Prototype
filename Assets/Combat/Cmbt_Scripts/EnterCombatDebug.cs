using UnityEngine;

public class EnterCombatDebug : MonoBehaviour
{
    [SerializeField] private KeyCode key = KeyCode.V;
    [SerializeField] private string returnSceneName = "TestMap_01";
    [SerializeField] private string returnSpawnId = "From_Map_02";

    [SerializeField] private string arenaSceneName = "Combat_Arena_Test";
    [SerializeField] private EnemyDefinition testEnemy;

    [Header("Debug Spawn")]
    [SerializeField] private int enemyCount = 3;

    private void Update()
    {
        if (!Input.GetKeyDown(key))
            return;

        if (CombatContext.Instance == null)
        {
            Debug.LogWarning("EnterCombatDebug: No CombatContext.Instance found.");
            return;
        }

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogWarning("EnterCombatDebug: No SceneTransitionManager.Instance found.");
            return;
        }

        CombatContext.Instance.returnSceneName = returnSceneName;
        CombatContext.Instance.returnSpawnId = returnSpawnId;
        CombatContext.Instance.arenaSceneName = arenaSceneName;

        CombatContext.Instance.enemiesToSpawn.Clear();

        if (testEnemy != null)
        {
            for (int i = 0; i < enemyCount; i++)
                CombatContext.Instance.enemiesToSpawn.Add(testEnemy);
        }

        SceneTransitionManager.Instance.TransitionTo(arenaSceneName, "");
    }
}