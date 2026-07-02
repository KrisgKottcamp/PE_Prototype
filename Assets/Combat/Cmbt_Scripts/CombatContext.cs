using System.Collections.Generic;
using UnityEngine;

public enum CombatEncounterMode
{
    Manual,
    Procedural
}

public class CombatContext : MonoBehaviour
{
    public static CombatContext Instance { get; private set; }

    [Header("Return Destination")]
    public string returnSceneName;
    public string returnSpawnId;

    [Header("Combat Scene")]
    public string arenaSceneName = "Combat_Arena_Test";

    [Header("Encounter Mode")]
    [Tooltip(
        "Manual is intended for bosses and authored encounters. " +
        "Procedural uses a Procedural Encounter Profile."
    )]
    public CombatEncounterMode encounterMode =
        CombatEncounterMode.Manual;

    [Header("Manual Encounter")]
    public List<EnemyDefinition> enemiesToSpawn = new();

    [Header("Procedural Encounter")]
    public ProceduralEncounterProfile proceduralProfile;

    [Tooltip(
        "0 creates a new random seed. Any other value reproduces the same " +
        "roster, background, obstacle choices, and placement attempts."
    )]
    public int proceduralSeed = 0;

    public bool WantsProceduralEncounter =>
        encounterMode == CombatEncounterMode.Procedural &&
        proceduralProfile != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ConfigureManualEncounter(
        IEnumerable<EnemyDefinition> enemies)
    {
        encounterMode = CombatEncounterMode.Manual;
        proceduralProfile = null;
        proceduralSeed = 0;

        enemiesToSpawn.Clear();

        if (enemies == null)
            return;

        enemiesToSpawn.AddRange(enemies);
    }

    public void ConfigureProceduralEncounter(
        ProceduralEncounterProfile profile,
        int seed = 0)
    {
        encounterMode = CombatEncounterMode.Procedural;
        proceduralProfile = profile;
        proceduralSeed = seed;
    }
}
