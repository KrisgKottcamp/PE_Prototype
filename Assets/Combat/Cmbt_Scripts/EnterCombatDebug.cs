using UnityEngine;

public class EnterCombatDebug : MonoBehaviour
{
    [SerializeField] private KeyCode key = KeyCode.V;

    [Header("Return Destination")]
    [SerializeField]
    private string returnSceneName = "TestMap_01";

    [SerializeField]
    private string returnSpawnId = "From_Map_02";

    [Header("Combat Scene")]
    [SerializeField]
    private string arenaSceneName = "Combat_Arena_Test";

    [Header("Encounter Mode")]
    [SerializeField]
    private CombatEncounterMode encounterMode =
        CombatEncounterMode.Procedural;

    [Header("Procedural Test")]
    [SerializeField]
    private ProceduralEncounterProfile proceduralProfile;

    [Tooltip(
        "0 creates a different encounter. Use a fixed number while debugging."
    )]
    [SerializeField]
    private int proceduralSeed = 12345;

    [Header("Manual Test / Boss Test")]
    [SerializeField]
    private EnemyDefinition testEnemy;

    [SerializeField, Min(1)]
    private int enemyCount = 3;

    private void Update()
    {
        if (!Input.GetKeyDown(key))
            return;

        CombatContext context =
            CombatContext.Instance;

        if (context == null)
        {
            Debug.LogWarning(
                "EnterCombatDebug: No CombatContext.Instance found."
            );

            return;
        }

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogWarning(
                "EnterCombatDebug: No SceneTransitionManager.Instance found."
            );

            return;
        }

        context.returnSceneName = returnSceneName;
        context.returnSpawnId = returnSpawnId;
        context.arenaSceneName = arenaSceneName;

        if (encounterMode ==
            CombatEncounterMode.Procedural)
        {
            if (proceduralProfile == null)
            {
                Debug.LogWarning(
                    "EnterCombatDebug: Procedural mode selected " +
                    "without a Procedural Encounter Profile."
                );

                return;
            }

            context.ConfigureProceduralEncounter(
                proceduralProfile,
                proceduralSeed
            );
        }
        else
        {
            context.encounterMode =
                CombatEncounterMode.Manual;

            context.proceduralProfile = null;
            context.proceduralSeed = 0;
            context.enemiesToSpawn.Clear();

            if (testEnemy != null)
            {
                for (int i = 0; i < enemyCount; i++)
                    context.enemiesToSpawn.Add(testEnemy);
            }
        }

        SceneTransitionManager.Instance.TransitionTo(
            arenaSceneName,
            ""
        );
    }
}
