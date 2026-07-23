using System.Collections.Generic;
using UnityEngine;

public class EncounterStateRegistry : MonoBehaviour
{
    public static EncounterStateRegistry Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;

    private readonly HashSet<string> defeatedEncounterIds = new();

    private string activeEncounterId = string.Empty;
    private bool encounterTransitionLocked;

    public string ActiveEncounterId => activeEncounterId;
    public bool EncounterTransitionLocked => encounterTransitionLocked;

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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static EncounterStateRegistry GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        EncounterStateRegistry existing =
            FindObjectOfType<EncounterStateRegistry>(true);

        if (existing != null)
            return existing;

        GameObject registryObject =
            new(nameof(EncounterStateRegistry));

        return registryObject.AddComponent<EncounterStateRegistry>();
    }

    public bool IsEncounterDefeated(string encounterId)
    {
        return !string.IsNullOrWhiteSpace(encounterId) &&
               defeatedEncounterIds.Contains(encounterId);
    }

    public bool TryBeginEncounter(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
        {
            Debug.LogWarning(
                "EncounterStateRegistry: Cannot begin an encounter " +
                "without a non-empty encounter ID."
            );

            return false;
        }

        if (IsEncounterDefeated(encounterId))
            return false;

        if (encounterTransitionLocked)
            return false;

        activeEncounterId = encounterId;
        encounterTransitionLocked = true;

        if (logStateChanges)
        {
            Debug.Log(
                $"EncounterStateRegistry: Began encounter " +
                $"'{activeEncounterId}'.",
                this
            );
        }

        return true;
    }

    public void ResolveActiveEncounter(bool playerWon)
    {
        string resolvedId = activeEncounterId;

        if (playerWon && !string.IsNullOrWhiteSpace(resolvedId))
            defeatedEncounterIds.Add(resolvedId);

        activeEncounterId = string.Empty;
        encounterTransitionLocked = false;

        if (!logStateChanges ||
            string.IsNullOrWhiteSpace(resolvedId))
        {
            return;
        }

        if (playerWon)
        {
            Debug.Log(
                $"EncounterStateRegistry: Encounter '{resolvedId}' was " +
                "defeated and will be removed from the overworld.",
                this
            );
        }
        else
        {
            Debug.Log(
                $"EncounterStateRegistry: Encounter '{resolvedId}' was " +
                "not defeated and remains available for a retry.",
                this
            );
        }
    }

    public void CancelActiveEncounter()
    {
        string cancelledId = activeEncounterId;

        activeEncounterId = string.Empty;
        encounterTransitionLocked = false;

        if (logStateChanges &&
            !string.IsNullOrWhiteSpace(cancelledId))
        {
            Debug.Log(
                $"EncounterStateRegistry: Cancelled encounter " +
                $"'{cancelledId}'.",
                this
            );
        }
    }
}
