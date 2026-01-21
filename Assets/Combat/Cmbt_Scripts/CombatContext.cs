using System.Collections.Generic;
using UnityEngine;

public class CombatContext : MonoBehaviour
{
    public static CombatContext Instance { get; private set; }

    [Header("Return Destination")]
    public string returnSceneName;
    public string returnSpawnId;

    [Header("Combat Scene")]
    public string arenaSceneName = "Combat_Arena_Test";

    [Header("Encounter")]
    public List<EnemyDefinition> enemiesToSpawn = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
