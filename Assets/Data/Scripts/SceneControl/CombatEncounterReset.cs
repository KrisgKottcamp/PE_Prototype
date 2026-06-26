using UnityEngine;

/// <summary>
/// Optionally resets persistent party combat state whenever a combat scene begins.
///
/// Add this component to the same GameObject as CombatManager.
/// Its early execution order makes the reset happen before normal combat Start()
/// methods build HUD values or spawn the shared combat pawn.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class CombatEncounterReset : MonoBehaviour
{
    [Header("Fresh Encounter")]
    [Tooltip(
        "Master switch. Disable this to preserve party state between encounters."
    )]
    [SerializeField]
    private bool resetPartyAtCombatStart = true;

    [Tooltip(
        "Restore every party member to their CharacterDefinition maximum HP."
    )]
    [SerializeField]
    private bool restorePartyToMaximumHP = true;

    [Tooltip(
        "Reset every party member's AP to zero."
    )]
    [SerializeField]
    private bool resetPartyAP = true;

    [Tooltip(
        "Reset every party member's escalating skill-cost multiplier to 1."
    )]
    [SerializeField]
    private bool resetSkillCostMultipliers = true;

    [Header("Optional")]
    [Tooltip(
        "Also return control to the first party-list entry at the start of combat."
    )]
    [SerializeField]
    private bool resetActivePartyMember = false;

    [Tooltip(
        "Print each reset to the Console while testing."
    )]
    [SerializeField]
    private bool logReset = false;

    private void Awake()
    {
        ApplyResetIfEnabled();
    }

    [ContextMenu("Apply Fresh Encounter Reset Now")]
    public void ApplyResetIfEnabled()
    {
        if (!resetPartyAtCombatStart)
            return;

        PartyManager partyManager =
            PartyManager.Instance;

        if (partyManager == null)
        {
            Debug.LogWarning(
                "CombatEncounterReset: PartyManager.Instance is missing.",
                this
            );

            return;
        }

        if (partyManager.party == null ||
            partyManager.party.Count == 0)
        {
            Debug.LogWarning(
                "CombatEncounterReset: PartyManager has no party members.",
                this
            );

            return;
        }

        for (int i = 0; i < partyManager.party.Count; i++)
        {
            PartyManager.CharacterState state =
                partyManager.party[i];

            if (state == null)
                continue;

            if (restorePartyToMaximumHP &&
                state.def != null)
            {
                state.currentHP =
                    Mathf.Max(0, state.def.maxHP);
            }

            if (resetPartyAP)
                state.currentAP = 0;

            if (resetSkillCostMultipliers)
                state.skillCostMultiplier = 1f;

            if (logReset)
            {
                string characterName =
                    state.def != null
                        ? state.def.name
                        : $"Party index {i}";

                Debug.Log(
                    $"Fresh encounter reset: {characterName}, " +
                    $"HP={state.currentHP}, " +
                    $"AP={state.currentAP}, " +
                    $"SkillCostMultiplier={state.skillCostMultiplier:0.##}",
                    this
                );
            }
        }

        if (resetActivePartyMember)
        {
            partyManager.activeIndex = Mathf.Clamp(
                0,
                0,
                partyManager.party.Count - 1
            );
        }
    }
}
