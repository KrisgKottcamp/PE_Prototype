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
    public int proceduralSeed;

    [Header("Overworld Encounter Runtime Data")]
    [Tooltip(
        "Unique ID of the overworld enemy that started the current fight. " +
        "Empty when combat came from a debug or non-overworld route."
    )]
    public string activeEncounterId;

    public Vector3 overworldReturnPlayerPosition;
    public Vector3 overworldReturnEnemyPosition;

    [Min(0f)]
    public float overworldReturnGraceSeconds = 4f;

    public bool hasExactOverworldReturn;

    [Tooltip(
        "True after CombatManager has recorded whether the overworld " +
        "encounter ended in victory or defeat."
    )]
    public bool hasOverworldEncounterResult;

    [Tooltip(
        "True only when the player won the current overworld encounter."
    )]
    public bool overworldEncounterWon;

    [Header("Opening Advantage Runtime Data")]
    [Tooltip(
        "True when the player contacted the enemy from its rear without " +
        "ever entering that enemy's vision cone."
    )]
    public bool playerOpeningAdvantage;

    [Tooltip(
        "Fraction of the initially active character's maximum AP granted " +
        "once when combat begins. 0.05 means five percent."
    )]
    [Range(0f, 1f)]
    public float openingAdvantageAPPercent = 0.05f;

    [SerializeField]
    private bool openingAdvantageConsumed;

    public bool WantsProceduralEncounter =>
        encounterMode == CombatEncounterMode.Procedural &&
        proceduralProfile != null;

    public bool HasOverworldEncounter =>
        !string.IsNullOrWhiteSpace(activeEncounterId);

    public bool HasExactOverworldReturn =>
        HasOverworldEncounter && hasExactOverworldReturn;

    public bool ShouldRestoreOverworldEnemy =>
        HasExactOverworldReturn &&
        hasOverworldEncounterResult &&
        !overworldEncounterWon;

    public float OverworldReturnGraceSecondsForResult =>
        ShouldRestoreOverworldEnemy
            ? Mathf.Max(0f, overworldReturnGraceSeconds)
            : 0f;

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
        ClearOverworldEncounterMetadata();

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
        ClearOverworldEncounterMetadata();

        encounterMode = CombatEncounterMode.Procedural;
        proceduralProfile = profile;
        proceduralSeed = seed;
    }

    public void ConfigureOverworldEncounter(
        string encounterId,
        Vector3 playerReturnPosition,
        Vector3 enemyReturnPosition,
        float graceSeconds,
        bool hasPlayerOpeningAdvantage,
        float advantageAPPercent)
    {
        activeEncounterId = encounterId;
        overworldReturnPlayerPosition = playerReturnPosition;
        overworldReturnEnemyPosition = enemyReturnPosition;
        overworldReturnGraceSeconds = Mathf.Max(0f, graceSeconds);
        hasExactOverworldReturn = true;

        hasOverworldEncounterResult = false;
        overworldEncounterWon = false;

        playerOpeningAdvantage = hasPlayerOpeningAdvantage;
        openingAdvantageAPPercent = Mathf.Clamp01(advantageAPPercent);
        openingAdvantageConsumed = false;
    }

    public bool TryConsumeOpeningAdvantage(
        out float advantageAPPercent)
    {
        advantageAPPercent = 0f;

        if (!HasOverworldEncounter ||
            !playerOpeningAdvantage ||
            openingAdvantageConsumed)
        {
            return false;
        }

        openingAdvantageConsumed = true;
        advantageAPPercent = Mathf.Clamp01(openingAdvantageAPPercent);
        return advantageAPPercent > 0f;
    }

    public void SetOverworldEncounterResult(bool playerWon)
    {
        if (!HasOverworldEncounter)
            return;

        hasOverworldEncounterResult = true;
        overworldEncounterWon = playerWon;
    }

    public bool TryGetOverworldEnemyReturnPosition(
        string encounterId,
        out Vector3 returnPosition)
    {
        returnPosition = overworldReturnEnemyPosition;

        return ShouldRestoreOverworldEnemy &&
               string.Equals(
                   activeEncounterId,
                   encounterId,
                   System.StringComparison.Ordinal
               );
    }

    public void ClearOverworldEncounterMetadata()
    {
        activeEncounterId = string.Empty;
        overworldReturnPlayerPosition = Vector3.zero;
        overworldReturnEnemyPosition = Vector3.zero;
        overworldReturnGraceSeconds = 0f;
        hasExactOverworldReturn = false;
        hasOverworldEncounterResult = false;
        overworldEncounterWon = false;

        playerOpeningAdvantage = false;
        openingAdvantageAPPercent = 0.05f;
        openingAdvantageConsumed = false;
    }
}
