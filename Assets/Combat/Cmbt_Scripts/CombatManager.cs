using System.Collections.Generic;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject combatPlayerPawnPrefab;

    [Header("Scene References")]
    [SerializeField] private Transform playerSpawn;

    [Tooltip(
        "Used only by Manual encounters, including boss arenas."
    )]
    [SerializeField] private Transform enemySpawnsParent;

    [Header("Procedural Encounters")]
    [SerializeField]
    private ProceduralArenaGenerator proceduralArenaGenerator;

    [SerializeField]
    private bool logProceduralGeneration = true;

    [Header("Attack Momentum")]
    [SerializeField] private AttackMomentumManager attackMomentum;

    [Tooltip(
        "Exact Momentum awarded whenever an enemy is defeated. " +
        "This bypasses Global Gain Scale and tier gain penalties."
    )]
    [SerializeField, Min(0f)]
    private float enemyDefeatMomentumBoost = 12f;

    [Header("Five-Star Rank")]
    [SerializeField] private CombatRankSettings rankSettings;
    [SerializeField] private CombatResultsPanel resultsPanel;

    [Tooltip(
        "When enabled, target time is calculated from the Rank Settings asset " +
        "and the number of enemies successfully spawned."
    )]
    [SerializeField]
    private bool calculateTargetTimeFromEnemyCount = true;

    [Tooltip(
        "Used only when automatic target-time calculation is disabled, " +
        "or when no Rank Settings asset is assigned."
    )]
    [SerializeField, Min(1f)]
    private float targetTimeOverrideSeconds = 60f;

    [Header("Debug")]
    [SerializeField] private bool debugKeys = true;
    [SerializeField] private KeyCode debugWinKey = KeyCode.K;
    [SerializeField] private KeyCode debugLoseKey = KeyCode.L;
    [SerializeField] private bool logRankBreakdown = true;

    private GameObject pawnInstance;
    private readonly List<EnemyHealth> livingEnemies = new();

    private int encounterEnemyCount;
    private int totalDamageTaken;
    private int combinedPartyMaxHP;
    private int partyMemberDeaths;

    private bool combatEnded;
    private float normalFixedDeltaTime;

    public static CombatManager Instance { get; private set; }

    public int TotalDamageTaken => totalDamageTaken;
    public int CombinedPartyMaxHP => combinedPartyMaxHP;
    public int PartyMemberDeaths => partyMemberDeaths;

    private void Awake()
    {
        Instance = this;
        normalFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void Start()
    {
        combatEnded = false;
        totalDamageTaken = 0;
        partyMemberDeaths = 0;
        combinedPartyMaxHP = CalculateCombinedPartyMaxHP();

        if (resultsPanel != null)
            resultsPanel.HideImmediately();

        SpawnPlayerPawn();
        ApplyOpeningAdvantage();
        InitializeAttackMomentum();
        SpawnEncounterFromContext();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        RestoreNormalTime();
    }

    private void Update()
    {
        if (!debugKeys || combatEnded)
            return;

        if (Input.GetKeyDown(debugWinKey))
            ForceWin();

        if (Input.GetKeyDown(debugLoseKey))
            ForceLose();
    }

    private int CalculateCombinedPartyMaxHP()
    {
        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null ||
            partyManager.party == null)
        {
            return 1;
        }

        int total = 0;

        for (int i = 0; i < partyManager.party.Count; i++)
        {
            PartyManager.CharacterState state =
                partyManager.party[i];

            if (state != null && state.def != null)
                total += Mathf.Max(0, state.def.maxHP);
        }

        return Mathf.Max(1, total);
    }

    private void ApplyOpeningAdvantage()
    {
        CombatContext context = CombatContext.Instance;

        if (context == null ||
            !context.TryConsumeOpeningAdvantage(
                out float advantageAPPercent
            ))
        {
            return;
        }

        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null ||
            partyManager.party == null ||
            partyManager.party.Count == 0)
        {
            Debug.LogWarning(
                "CombatManager: Opening advantage was requested, " +
                "but PartyManager has no party state.",
                this
            );

            return;
        }

        PartyManager.CharacterState openingCharacter =
            partyManager.Active;

        if (openingCharacter == null ||
            openingCharacter.def == null)
        {
            Debug.LogWarning(
                "CombatManager: Opening advantage was requested, " +
                "but the active character state is invalid.",
                this
            );

            return;
        }

        int maximumAP = Mathf.Max(0, openingCharacter.def.maxAP);

        if (maximumAP <= 0)
            return;

        int bonusAP = Mathf.Max(
            1,
            Mathf.CeilToInt(maximumAP * advantageAPPercent)
        );

        int previousAP = openingCharacter.currentAP;
        openingCharacter.currentAP = Mathf.Clamp(
            Mathf.Max(previousAP, bonusAP),
            0,
            maximumAP
        );

        Debug.Log(
            $"CombatManager: Player opening advantage set " +
            $"'{openingCharacter.def.displayName}' to at least " +
            $"{bonusAP} AP ({advantageAPPercent:P0} of max AP). " +
            $"Previous AP={previousAP}; current AP=" +
            $"{openingCharacter.currentAP}. This applies only to the " +
            "character active when the encounter begins.",
            this
        );
    }

    private void InitializeAttackMomentum()
    {
        if (attackMomentum == null)
        {
            attackMomentum =
                FindObjectOfType<AttackMomentumManager>(true);
        }

        if (attackMomentum == null)
        {
            attackMomentum =
                gameObject.AddComponent<AttackMomentumManager>();
        }

        attackMomentum.BeginEncounter();
    }

    private void SpawnPlayerPawn()
    {
        Vector3 position =
            playerSpawn != null
                ? playerSpawn.position
                : Vector3.zero;

        pawnInstance = Instantiate(
            combatPlayerPawnPrefab,
            position,
            Quaternion.identity
        );
    }

    private void SpawnEncounterFromContext()
    {
        livingEnemies.Clear();
        encounterEnemyCount = 0;

        CombatContext context =
            CombatContext.Instance;

        if (context == null)
        {
            Debug.LogError(
                "CombatManager: CombatContext.Instance missing " +
                "(Bootstrap not loaded?)."
            );

            return;
        }

        if (context.WantsProceduralEncounter)
        {
            bool generated =
                TrySpawnProceduralEncounter(context);

            if (generated)
                return;

            bool allowFallback =
                context.proceduralProfile != null &&
                context.proceduralProfile
                    .fallbackToManualEncounter;

            if (!allowFallback)
                return;

            Debug.LogWarning(
                "CombatManager: Procedural generation failed. " +
                "Falling back to the manual enemy list.",
                this
            );
        }

        SpawnManualEnemies(context);
    }

    private bool TrySpawnProceduralEncounter(
        CombatContext context)
    {
        if (proceduralArenaGenerator == null)
        {
            proceduralArenaGenerator =
                FindObjectOfType<
                    ProceduralArenaGenerator
                >(true);
        }

        if (proceduralArenaGenerator == null)
        {
            Debug.LogError(
                "CombatManager: Procedural encounter requested, " +
                "but no ProceduralArenaGenerator exists in the arena.",
                this
            );

            return false;
        }

        ProceduralGenerationResult result =
            proceduralArenaGenerator.GenerateAndSpawn(
                context.proceduralProfile,
                context.proceduralSeed
            );

        if (!result.success)
        {
            Debug.LogError(
                $"CombatManager: Procedural generation failed. " +
                $"Reason: {result.failureReason} " +
                $"Seed: {result.seed}",
                this
            );

            return false;
        }

        for (int i = 0;
             i < result.spawnedEnemies.Count;
             i++)
        {
            RegisterSpawnedEnemy(
                result.spawnedEnemies[i]
            );
        }

        if (logProceduralGeneration)
        {
            Debug.Log(
                $"CombatManager accepted procedural encounter. " +
                $"Seed={result.seed}, " +
                $"Enemies={encounterEnemyCount}, " +
                $"Obstacles={result.spawnedObstacleCount}, " +
                $"TacticalPoints=" +
                $"{result.activeTacticalPointCount}.",
                this
            );
        }

        return encounterEnemyCount > 0;
    }

    private void SpawnManualEnemies(
        CombatContext context)
    {
        if (enemySpawnsParent == null)
        {
            Debug.LogError(
                "CombatManager: Manual encounter requested, " +
                "but enemySpawnsParent is not assigned."
            );

            return;
        }

        int spawnCount =
            enemySpawnsParent.childCount;

        if (spawnCount <= 0)
        {
            Debug.LogError(
                "CombatManager: Manual encounter requested, " +
                "but enemySpawnsParent has no spawn children."
            );

            return;
        }

        for (int i = 0;
             i < context.enemiesToSpawn.Count;
             i++)
        {
            EnemyDefinition definition =
                context.enemiesToSpawn[i];

            if (definition == null ||
                definition.prefab == null)
            {
                Debug.LogWarning(
                    "CombatManager: EnemyDefinition or prefab missing."
                );

                continue;
            }

            Transform spawn =
                enemySpawnsParent.GetChild(
                    i % spawnCount
                );

            GameObject enemyObject = Instantiate(
                definition.prefab,
                spawn.position,
                Quaternion.identity
            );

            EnemyHealth health =
                enemyObject.GetComponent<EnemyHealth>();

            if (health == null)
            {
                Debug.LogWarning(
                    $"Manual enemy prefab '{definition.prefab.name}' " +
                    $"does not contain EnemyHealth.",
                    enemyObject
                );

                continue;
            }

            health.Init(definition.maxHP);
            RegisterSpawnedEnemy(health);
        }
    }

    private void RegisterSpawnedEnemy(
        EnemyHealth health)
    {
        if (health == null)
            return;

        health.OnDied += OnEnemyDied;

        livingEnemies.Add(health);
        encounterEnemyCount++;
    }

    private void OnEnemyDied(EnemyHealth enemy)
    {
        if (combatEnded)
            return;

        if (enemy != null)
            enemy.OnDied -= OnEnemyDied;

        livingEnemies.Remove(enemy);

        attackMomentum?.RegisterGuaranteedBonus(
            enemyDefeatMomentumBoost
        );

        if (livingEnemies.Count == 0)
            EndCombatVictory();
    }

    public void NotifyPlayerDamaged(int actualDamage)
    {
        if (combatEnded || actualDamage <= 0)
            return;

        totalDamageTaken += actualDamage;
    }

    public void NotifyPlayerDown()
    {
        if (combatEnded)
            return;

        partyMemberDeaths++;

        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null)
        {
            EndCombatDefeat();
            return;
        }

        bool anyAlive = false;

        for (int i = 0; i < partyManager.party.Count; i++)
        {
            if (partyManager.party[i].currentHP > 0)
            {
                anyAlive = true;
                break;
            }
        }

        if (!anyAlive)
        {
            EndCombatDefeat();
            return;
        }

        bool swapped = partyManager.SwapNextAlive();

        if (!swapped)
        {
            EndCombatDefeat();
            return;
        }

        CombatLockout lockout =
            FindObjectOfType<CombatLockout>(true);

        lockout?.TriggerLockout();
    }

    private void EndCombatVictory()
    {
        if (combatEnded)
            return;

        combatEnded = true;
        ResolveOverworldEncounter(true);

        AttackMomentumResult encounterResult =
            attackMomentum != null
                ? attackMomentum.CompleteEncounter(true)
                : default;

        float targetTime = ResolveTargetTime();

        CombatRankResult rankResult =
            CombatRankCalculator.Calculate(
                encounterResult,
                targetTime,
                totalDamageTaken,
                combinedPartyMaxHP,
                partyMemberDeaths,
                rankSettings
            );

        DisableCombatControls();
        PauseForResults();

        if (logRankBreakdown)
        {
            Debug.Log(
                $"Combat rank: {rankResult.stars} stars. " +
                $"BaseStars={rankResult.uncappedStars}, " +
                $"DeathCap={rankResult.deathStarCap}, " +
                $"Actual={rankResult.actualTimeSeconds:0.00}s, " +
                $"Target={rankResult.targetTimeSeconds:0.00}s, " +
                $"Time={rankResult.timeScore01:P1}, " +
                $"Momentum={rankResult.momentumScore01:P1}, " +
                $"Clean={rankResult.cleanPlayScore01:P1}, " +
                $"Damage={rankResult.totalDamageTaken}/" +
                $"{rankResult.combinedPartyMaxHP}, " +
                $"Deaths={rankResult.partyMemberDeaths}, " +
                $"Overall={rankResult.overallScore01:P1}, " +
                $"FourGate={rankResult.passedFourStarGate}, " +
                $"FiveGate={rankResult.passedFiveStarGate}",
                this
            );
        }

        if (resultsPanel != null)
        {
            resultsPanel.Show(
                encounterResult,
                rankResult,
                ContinueAfterVictory
            );
        }
        else
        {
            Debug.LogWarning(
                "CombatManager: No CombatResultsPanel assigned. " +
                "Returning to exploration immediately.",
                this
            );

            ContinueAfterVictory();
        }
    }

    private void EndCombatDefeat()
    {
        if (combatEnded)
            return;

        combatEnded = true;
        ResolveOverworldEncounter(false);
        attackMomentum?.CompleteEncounter(false);

        RestoreNormalTime();
        ExitCombatToOverworld();
    }

    private void ResolveOverworldEncounter(bool playerWon)
    {
        CombatContext context = CombatContext.Instance;

        if (context == null || !context.HasOverworldEncounter)
            return;

        context.SetOverworldEncounterResult(playerWon);

        EncounterStateRegistry registry =
            EncounterStateRegistry.Instance;

        if (registry != null)
            registry.ResolveActiveEncounter(playerWon);

        // Do not clear CombatContext overworld metadata here. The return
        // transition still needs the exact player position. On defeat it
        // also needs the enemy position and grace duration.
        // SceneTransitionManager clears the metadata after the overworld
        // scene has loaded.
    }

    private float ResolveTargetTime()
    {
        if (calculateTargetTimeFromEnemyCount &&
            rankSettings != null)
        {
            return rankSettings.CalculateTargetTime(
                encounterEnemyCount
            );
        }

        return Mathf.Max(
            1f,
            targetTimeOverrideSeconds
        );
    }

    private void PauseForResults()
    {
        Time.timeScale = 0f;
    }

    private void RestoreNormalTime()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = normalFixedDeltaTime;
    }

    private void ContinueAfterVictory()
    {
        RestoreNormalTime();
        ExitCombatToOverworld();
    }

    private void DisableCombatControls()
    {
        if (pawnInstance != null)
        {
            SetChildComponentEnabled<CombatPawnMover>(false);
            SetChildComponentEnabled<BasicAttack>(false);
            SetChildComponentEnabled<ProjectileBasicAttack>(false);
            SetChildComponentEnabled<HeavyComboAttack>(false);
            SetChildComponentEnabled<CombatPartyController>(false);
            SetChildComponentEnabled<PlayerFocusMode>(false);

            MonoBehaviour[] pawnScripts =
                pawnInstance.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < pawnScripts.Length; i++)
            {
                MonoBehaviour script = pawnScripts[i];

                if (script != null &&
                    script.GetType().Name == "WhipAttack")
                {
                    script.enabled = false;
                    break;
                }
            }
        }

        CombatSkillMenuController skillMenu =
            FindObjectOfType<CombatSkillMenuController>(true);

        if (skillMenu != null)
            skillMenu.enabled = false;
    }

    private void SetChildComponentEnabled<T>(bool enabled)
        where T : MonoBehaviour
    {
        if (pawnInstance == null)
            return;

        T component =
            pawnInstance.GetComponentInChildren<T>(true);

        if (component != null)
            component.enabled = enabled;
    }

    private void ExitCombatToOverworld()
    {
        CombatContext context = CombatContext.Instance;

        if (context == null)
        {
            Debug.LogError(
                "CombatManager: CombatContext.Instance missing."
            );

            return;
        }

        if (pawnInstance != null)
            Destroy(pawnInstance);

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError(
                "CombatManager: SceneTransitionManager.Instance missing."
            );

            return;
        }

        if (context.HasExactOverworldReturn)
        {
            SceneTransitionManager.Instance.TransitionToPosition(
                context.returnSceneName,
                context.overworldReturnPlayerPosition,
                context.OverworldReturnGraceSecondsForResult
            );
        }
        else
        {
            SceneTransitionManager.Instance.TransitionTo(
                context.returnSceneName,
                context.returnSpawnId
            );
        }
    }

    private void ForceWin()
    {
        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            if (livingEnemies[i] != null)
                livingEnemies[i].TakeDamage(999999);
        }
    }

    private void ForceLose()
    {
        EndCombatDefeat();
    }
}
