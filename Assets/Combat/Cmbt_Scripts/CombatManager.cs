using System.Collections.Generic;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject combatPlayerPawnPrefab;

    [Header("Scene References")]
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private Transform enemySpawnsParent;

    [Header("Debug")]
    [SerializeField] private bool debugKeys = true;
    [SerializeField] private KeyCode debugWinKey = KeyCode.K; // kill all enemies
    [SerializeField] private KeyCode debugLoseKey = KeyCode.L; // set party HP to 0

    private GameObject pawnInstance;
    private readonly List<EnemyHealth> livingEnemies = new();

    private void Start()
    {
        SpawnPlayerPawn();
        SpawnEnemiesFromContext();
    }

    private void Update()
    {
        if (!debugKeys) return;

        if (Input.GetKeyDown(debugWinKey))
            ForceWin();

        if (Input.GetKeyDown(debugLoseKey))
            ForceLose();
    }

    private void SpawnPlayerPawn()
    {
        Vector3 pos = playerSpawn ? playerSpawn.position : Vector3.zero;
        pawnInstance = Instantiate(combatPlayerPawnPrefab, pos, Quaternion.identity);
    }

    private void SpawnEnemiesFromContext()
    {
        livingEnemies.Clear();

        var ctx = CombatContext.Instance;
        if (ctx == null)
        {
            Debug.LogError("CombatManager: CombatContext.Instance missing (Bootstrap not loaded?).");
            return;
        }

        if (enemySpawnsParent == null)
        {
            Debug.LogError("CombatManager: enemySpawnsParent not assigned.");
            return;
        }

        int spawnCount = enemySpawnsParent.childCount;

        for (int i = 0; i < ctx.enemiesToSpawn.Count; i++)
        {
            var def = ctx.enemiesToSpawn[i];
            if (def == null || def.prefab == null)
            {
                Debug.LogWarning("CombatManager: EnemyDefinition or prefab missing.");
                continue;
            }

            Transform spawn = enemySpawnsParent.GetChild(i % spawnCount);

            GameObject enemyObj = Instantiate(def.prefab, spawn.position, Quaternion.identity);
            var hp = enemyObj.GetComponent<EnemyHealth>();
            if (hp != null)
            {
                hp.Init(def.maxHP);
                hp.OnDied += OnEnemyDied;
                livingEnemies.Add(hp);
            }
        }
    }

    private void OnEnemyDied(EnemyHealth enemy)
    {
        livingEnemies.Remove(enemy);

        if (livingEnemies.Count == 0)
            EndCombatVictory();
    }

    private void EndCombatVictory()
    {
        // TODO later: grant XP, rewards
        ExitCombatToOverworld();
    }

    private void EndCombatDefeat()
    {
        ExitCombatToOverworld();
    }

    private void ExitCombatToOverworld()
    {
        var ctx = CombatContext.Instance;
        if (ctx == null)
        {
            Debug.LogError("CombatManager: CombatContext.Instance missing.");
            return;
        }

        // Clean up pawn (optional, scene unload will destroy it anyway)
        if (pawnInstance != null) Destroy(pawnInstance);

        SceneTransitionManager.Instance.TransitionTo(ctx.returnSceneName, ctx.returnSpawnId);
    }

    private void ForceWin()
    {
        // Kill all enemies instantly
        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            if (livingEnemies[i] != null)
                livingEnemies[i].TakeDamage(999999);
        }
    }

    private void ForceLose()
    {
        // Skeleton: treat as defeat (later check party HP properly)
        EndCombatDefeat();
    }

    public static CombatManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
    public void NotifyPlayerDown()
    {
        var pm = PartyManager.Instance;
        if (pm == null)
        {
            EndCombatDefeat();
            return;
        }

        // If anyone alive, swap to them instead of defeat
        bool anyAlive = false;
        for (int i = 0; i < pm.party.Count; i++)
        {
            if (pm.party[i].currentHP > 0) { anyAlive = true; break; }
        }

        if (!anyAlive)
        {
            EndCombatDefeat();
            return;
        }

        // Force swap to a living member
        bool swapped = pm.SwapNextAlive();
        if (!swapped)
        {
            EndCombatDefeat();
            return;
        }

        // Optional: apply lockout so the swap is still risky
        var pawn = FindObjectOfType<CombatLockout>(true);
        pawn?.TriggerLockout();
    }



}
