using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum OverworldEncounterSeedMode
{
    RandomEveryAttempt,
    Fixed
}

public class OverworldEncounter : MonoBehaviour
{
    [Header("Encounter Identity")]
    [Tooltip(
        "Unique across the game. Victory records this ID as defeated so " +
        "the matching overworld enemy is removed when the scene reloads."
    )]
    [SerializeField] private string encounterId =
        "TestMap01_Encounter_01";

    [Header("Combat Scene")]
    [SerializeField] private string arenaSceneName =
        "Combat_Arena_Master";

    [Header("Return Behavior")]
    [Tooltip(
        "Used only after defeat. The player cannot collide with or trigger " +
        "any overworld encounter for this many seconds."
    )]
    [SerializeField, Min(0f)]
    private float returnGracePeriodSeconds = 4f;

    [Header("Opening Advantage")]
    [Tooltip(
        "Assign the OverworldEnemyAwareness component on this same root " +
        "GameObject. Without it, encounters always begin neutrally."
    )]
    [SerializeField]
    private OverworldEnemyAwareness awareness;

    [Tooltip(
        "Fraction of the initially active character's maximum AP granted " +
        "when the player contacts the enemy from behind without detection."
    )]
    [SerializeField, Range(0f, 1f)]
    private float openingAdvantageAPPercent = 0.05f;

    [Header("Encounter Mode")]
    [SerializeField] private CombatEncounterMode encounterMode =
        CombatEncounterMode.Procedural;

    [Header("Procedural Encounter")]
    [SerializeField]
    private ProceduralEncounterProfile proceduralProfile;

    [SerializeField]
    private OverworldEncounterSeedMode seedMode =
        OverworldEncounterSeedMode.RandomEveryAttempt;

    [SerializeField] private int fixedSeed = 12345;

    [Header("Manual Encounter / Procedural Fallback")]
    [Tooltip(
        "Used as the main roster in Manual mode. In Procedural mode, " +
        "this becomes the fallback list if the profile allows fallback."
    )]
    [SerializeField]
    private List<EnemyDefinition> manualEnemies = new();

    [Header("Scene References")]
    [SerializeField]
    private OverworldEnemyWander wander;

    [SerializeField]
    private OverworldEncounterTransition encounterTransition;

    [Header("Detection")]
    [SerializeField] private string playerTag = "Player";

    [Header("Debug")]
    [SerializeField] private bool logEncounterStart = true;

    private EncounterStateRegistry stateRegistry;
    private Transform triggeringPlayer;
    private bool locallyTriggered;

    public string EncounterId => encounterId;
    public bool IsLocallyTriggered => locallyTriggered;

    private void Awake()
    {
        if (wander == null)
            wander = GetComponent<OverworldEnemyWander>();

        if (awareness == null)
            awareness = GetComponent<OverworldEnemyAwareness>();

        if (encounterTransition == null)
        {
            encounterTransition =
                GetComponent<OverworldEncounterTransition>();
        }

        if (encounterTransition == null)
        {
            encounterTransition =
                gameObject.AddComponent<
                    OverworldEncounterTransition
                >();
        }
    }

    private void Start()
    {
        stateRegistry = EncounterStateRegistry.GetOrCreate();

        if (stateRegistry.IsEncounterDefeated(encounterId))
        {
            RemoveDefeatedEncounterObject();
            return;
        }

        RestorePositionAfterCombatIfNeeded();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryStartFromCollider(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryStartFromCollider(other);
    }

    private void TryStartFromCollider(Collider2D other)
    {
        if (locallyTriggered)
            return;

        Transform playerRoot = ResolvePlayerRoot(other);

        if (playerRoot == null || PlayerHasEncounterGrace(playerRoot))
            return;

        TryStartEncounter(playerRoot);
    }

    public void TryStartEncounter(Transform playerRoot)
    {
        if (locallyTriggered ||
            playerRoot == null ||
            PlayerHasEncounterGrace(playerRoot))
        {
            return;
        }

        if (!ValidateEncounterConfiguration())
            return;

        if (stateRegistry == null)
            stateRegistry = EncounterStateRegistry.GetOrCreate();

        if (stateRegistry.IsEncounterDefeated(encounterId))
        {
            RemoveDefeatedEncounterObject();
            return;
        }

        bool playerHasOpeningAdvantage =
            awareness != null &&
            awareness.CanGrantRearAdvantage(playerRoot);

        if (!stateRegistry.TryBeginEncounter(encounterId))
            return;

        locallyTriggered = true;
        triggeringPlayer = playerRoot;

        CombatContext context = CombatContext.Instance;

        if (encounterMode == CombatEncounterMode.Procedural)
        {
            int seed = seedMode ==
                OverworldEncounterSeedMode.Fixed
                    ? fixedSeed
                    : 0;

            context.ConfigureProceduralEncounter(
                proceduralProfile,
                seed
            );

            context.enemiesToSpawn.Clear();

            if (manualEnemies != null)
                context.enemiesToSpawn.AddRange(manualEnemies);
        }
        else
        {
            context.ConfigureManualEncounter(manualEnemies);
        }

        context.returnSceneName =
            SceneManager.GetActiveScene().name;

        context.returnSpawnId = string.Empty;
        context.arenaSceneName = arenaSceneName;

        context.ConfigureOverworldEncounter(
            encounterId,
            triggeringPlayer.position,
            transform.position,
            returnGracePeriodSeconds,
            playerHasOpeningAdvantage,
            openingAdvantageAPPercent
        );

        if (logEncounterStart)
        {
            Debug.Log(
                $"OverworldEncounter: Starting '{encounterId}' from " +
                $"scene '{context.returnSceneName}'. " +
                $"Opening advantage={playerHasOpeningAdvantage}; " +
                $"advantage AP={openingAdvantageAPPercent:P0}; " +
                $"player return position=" +
                $"{context.overworldReturnPlayerPosition}; " +
                $"enemy return position=" +
                $"{context.overworldReturnEnemyPosition}.",
                this
            );
        }

        wander?.SetMovementLocked(true);

        encounterTransition.Play(
            triggeringPlayer,
            BeginArenaTransition
        );
    }

    private void BeginArenaTransition()
    {
        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError(
                "OverworldEncounter: SceneTransitionManager.Instance " +
                "was lost before the arena transition began.",
                this
            );

            stateRegistry?.CancelActiveEncounter();
            CombatContext.Instance?.ClearOverworldEncounterMetadata();
            encounterTransition?.RestoreActorsAfterCancelledTransition();
            awareness?.ResetAwareness();
            locallyTriggered = false;
            triggeringPlayer = null;
            return;
        }

        SceneTransitionManager.Instance.TransitionTo(
            arenaSceneName,
            string.Empty
        );
    }

    private void RestorePositionAfterCombatIfNeeded()
    {
        CombatContext context = CombatContext.Instance;

        if (context == null ||
            !context.TryGetOverworldEnemyReturnPosition(
                encounterId,
                out Vector3 returnPosition
            ))
        {
            return;
        }

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        Vector3 target = new(
            returnPosition.x,
            returnPosition.y,
            transform.position.z
        );

        if (body != null)
            body.position = new Vector2(target.x, target.y);

        transform.position = target;

        wander?.SetMovementLocked(false);
        wander?.ResetHomePosition();
        awareness?.ResetAwareness();

        if (logEncounterStart)
        {
            Debug.Log(
                $"OverworldEncounter: Restored '{encounterId}' to " +
                $"{target} after combat.",
                this
            );
        }
    }

    private void RemoveDefeatedEncounterObject()
    {
        if (logEncounterStart)
        {
            Debug.Log(
                $"OverworldEncounter: Removing defeated encounter " +
                $"'{encounterId}' from the overworld.",
                this
            );
        }

        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private bool ValidateEncounterConfiguration()
    {
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            Debug.LogError(
                "OverworldEncounter: Encounter ID is empty.",
                this
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(arenaSceneName))
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': Arena Scene " +
                "Name is empty.",
                this
            );

            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(arenaSceneName))
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': Scene " +
                $"'{arenaSceneName}' is not enabled in the Build " +
                "Profile.",
                this
            );

            return false;
        }

        if (CombatContext.Instance == null)
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': " +
                "CombatContext.Instance is missing. Start through " +
                "Bootstrap and confirm CombatContext is persistent.",
                this
            );

            return false;
        }

        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': " +
                "SceneTransitionManager.Instance is missing.",
                this
            );

            return false;
        }

        if (encounterMode == CombatEncounterMode.Procedural &&
            proceduralProfile == null)
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': Procedural " +
                "mode is selected without a Procedural Encounter " +
                "Profile.",
                this
            );

            return false;
        }

        if (encounterMode == CombatEncounterMode.Manual &&
            (manualEnemies == null || manualEnemies.Count == 0))
        {
            Debug.LogError(
                $"OverworldEncounter '{encounterId}': Manual mode " +
                "is selected without any Enemy Definitions.",
                this
            );

            return false;
        }

        return true;
    }

    private static bool PlayerHasEncounterGrace(Transform playerRoot)
    {
        OverworldEncounterGracePeriod gracePeriod =
            playerRoot.GetComponent<OverworldEncounterGracePeriod>();

        if (gracePeriod == null)
        {
            gracePeriod =
                playerRoot.GetComponentInChildren<
                    OverworldEncounterGracePeriod
                >(true);
        }

        return gracePeriod != null && gracePeriod.IsActive;
    }

    private Transform ResolvePlayerRoot(Collider2D other)
    {
        if (other == null)
            return null;

        Transform persistentPlayer =
            PlayerSingleton.Instance != null
                ? PlayerSingleton.Instance.transform
                : null;

        if (persistentPlayer != null)
        {
            if (other.transform == persistentPlayer ||
                other.transform.IsChildOf(persistentPlayer))
            {
                return persistentPlayer;
            }

            if (other.attachedRigidbody != null)
            {
                Transform bodyTransform =
                    other.attachedRigidbody.transform;

                if (bodyTransform == persistentPlayer ||
                    bodyTransform.IsChildOf(persistentPlayer))
                {
                    return persistentPlayer;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(playerTag) &&
            other.CompareTag(playerTag))
        {
            return other.attachedRigidbody != null
                ? other.attachedRigidbody.transform
                : other.transform;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        returnGracePeriodSeconds =
            Mathf.Max(0f, returnGracePeriodSeconds);

        openingAdvantageAPPercent =
            Mathf.Clamp01(openingAdvantageAPPercent);

        if (string.IsNullOrWhiteSpace(encounterId))
            encounterId = gameObject.name;
    }
#endif
}
