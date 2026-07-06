using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates and updates one PartyHealthRow for every member stored in
/// the persistent PartyManager.
///
/// This version can hide the currently active party member so the
/// project's existing large active-character HUD remains the primary
/// display while inactive party members appear as smaller rows.
/// </summary>
[DisallowMultipleComponent]
public class PartyHealthHUD : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private PartyHealthRow rowPrefab;
    [SerializeField] private Transform rowsContainer;

    [Header("Displayed Members")]
    [Tooltip(
        "When enabled, the currently active party member is hidden from " +
        "this compact list. Use this when another HUD already shows the " +
        "active character at a larger size."
    )]
    [SerializeField] private bool hideActiveMember = true;

    [Header("Optional Visibility")]
    [Tooltip(
        "Optional CanvasGroup used to hide the HUD while PartyManager " +
        "does not exist or contains no party members."
    )]
    [SerializeField] private CanvasGroup hudCanvasGroup;

    [SerializeField] private bool hideWhenPartyUnavailable = true;

    [Header("Roster Checking")]
    [Tooltip(
        "How often the HUD checks whether party members were added, removed, " +
        "or replaced. HP itself still refreshes every frame."
    )]
    [SerializeField, Min(0.05f)] private float rosterCheckInterval = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool logRosterRebuilds = false;
    [SerializeField] private int debugDisplayedPartyMembers;

    private readonly List<PartyHealthRow> rows = new List<PartyHealthRow>();

    private int lastRosterSignature = int.MinValue;
    private float nextRosterCheckTime;
    private bool hasLoggedMissingPrefab;

    private void Awake()
    {
        if (rowsContainer == null)
            rowsContainer = transform;
    }

    private void OnEnable()
    {
        nextRosterCheckTime = 0f;
        lastRosterSignature = int.MinValue;
        TryRebuildRows(force: true);
    }

    private void Update()
    {
        PartyManager partyManager = PartyManager.Instance;

        if (!HasUsableParty(partyManager))
        {
            SetHudVisible(false);

            if (rows.Count > 0)
                ClearRows();

            lastRosterSignature = int.MinValue;
            return;
        }

        SetHudVisible(true);

        if (Time.unscaledTime >= nextRosterCheckTime)
        {
            nextRosterCheckTime = Time.unscaledTime + rosterCheckInterval;
            TryRebuildRows(force: false);
        }

        RefreshRows(partyManager);
    }

    public void ForceRebuild()
    {
        lastRosterSignature = int.MinValue;
        TryRebuildRows(force: true);
    }

    public void RefreshNow()
    {
        PartyManager partyManager = PartyManager.Instance;

        if (!HasUsableParty(partyManager))
            return;

        RefreshRows(partyManager);
    }

    private void TryRebuildRows(bool force)
    {
        PartyManager partyManager = PartyManager.Instance;

        if (!HasUsableParty(partyManager))
            return;

        int rosterSignature = CalculateRosterSignature(partyManager);

        bool rowCountMismatch = rows.Count != partyManager.party.Count;

        if (!force &&
            !rowCountMismatch &&
            rosterSignature == lastRosterSignature)
        {
            return;
        }

        RebuildRows(partyManager, rosterSignature);
    }

    private void RebuildRows(
        PartyManager partyManager,
        int rosterSignature)
    {
        if (rowPrefab == null)
        {
            if (!hasLoggedMissingPrefab)
            {
                hasLoggedMissingPrefab = true;

                Debug.LogError(
                    "PartyHealthHUD: Row Prefab is not assigned.",
                    this
                );
            }

            return;
        }

        if (rowsContainer == null)
        {
            Debug.LogError(
                "PartyHealthHUD: Rows Container is not assigned.",
                this
            );

            return;
        }

        ClearRows();

        for (int i = 0; i < partyManager.party.Count; i++)
        {
            PartyManager.CharacterState state = partyManager.party[i];

            PartyHealthRow row = Instantiate(rowPrefab, rowsContainer);

            row.name =
                state != null &&
                state.def != null &&
                !string.IsNullOrWhiteSpace(state.def.displayName)
                    ? $"PartyHealthRow_{state.def.displayName}"
                    : $"PartyHealthRow_{i + 1}";

            row.Bind(state, i);
            rows.Add(row);
        }

        lastRosterSignature = rosterSignature;

        if (logRosterRebuilds)
        {
            Debug.Log(
                $"PartyHealthHUD: Built {rows.Count} party health rows.",
                this
            );
        }
    }

    private void RefreshRows(PartyManager partyManager)
    {
        int activeIndex = partyManager.activeIndex;

        int count = Mathf.Min(rows.Count, partyManager.party.Count);
        int visibleCount = 0;

        for (int i = 0; i < count; i++)
        {
            PartyHealthRow row = rows[i];

            if (row == null)
                continue;

            bool isActive = i == activeIndex;
            bool shouldBeVisible = !hideActiveMember || !isActive;

            if (row.gameObject.activeSelf != shouldBeVisible)
                row.gameObject.SetActive(shouldBeVisible);

            if (!shouldBeVisible)
                continue;

            PartyManager.CharacterState state = partyManager.party[i];

            if (!row.IsBoundTo(state, i))
                row.Bind(state, i);

            row.Refresh(isActive);
            visibleCount++;
        }

        debugDisplayedPartyMembers = visibleCount;
    }

    private void ClearRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            PartyHealthRow row = rows[i];

            if (row != null)
                Destroy(row.gameObject);
        }

        rows.Clear();
        debugDisplayedPartyMembers = 0;
    }

    private bool HasUsableParty(PartyManager partyManager)
    {
        return
            partyManager != null &&
            partyManager.party != null &&
            partyManager.party.Count > 0;
    }

    private int CalculateRosterSignature(PartyManager partyManager)
    {
        unchecked
        {
            int hash = 17;

            hash = hash * 31 + partyManager.party.Count;

            for (int i = 0; i < partyManager.party.Count; i++)
            {
                PartyManager.CharacterState state = partyManager.party[i];

                CharacterDefinition definition =
                    state != null ? state.def : null;

                hash =
                    hash * 31 +
                    (definition != null ? definition.GetInstanceID() : 0);
            }

            return hash;
        }
    }

    private void SetHudVisible(bool hasParty)
    {
        if (hudCanvasGroup == null)
            return;

        bool visible = hasParty || !hideWhenPartyUnavailable;

        hudCanvasGroup.alpha = visible ? 1f : 0f;
        hudCanvasGroup.interactable = visible;
        hudCanvasGroup.blocksRaycasts = visible;
    }
}
