using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Keeps the shared combat pawn's runtime SpellLoadout synchronized with the
/// currently active party member's CharacterDefinition. Character Definitions
/// remain the authored source of truth; the component loadout is only the
/// runtime view consumed by player input and SpellRunner.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpellLoadout))]
public sealed class CharacterDefinitionSpellLoadoutBinder : MonoBehaviour
{
    [SerializeField]
    [Tooltip("When enabled, a Character Definition that has not opted into Skill System V2 restores the pawn's original manually configured loadout.")]
    private bool preservePawnFallbackLoadout = true;

    private readonly List<SpellDefinition> fallbackSkills =
        new List<SpellDefinition>();

    private SpellLoadout spellLoadout;
    private SpellDefinition fallbackBasicAttack;
    private PartyManager observedParty;
    private CharacterDefinition appliedDefinition;
    private int appliedPartyIndex = int.MinValue;
    private bool fallbackCaptured;

    private void Awake()
    {
        ResolveLoadout();
        CaptureFallback();
    }

    private void OnEnable()
    {
        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    public void RefreshNow()
    {
        Refresh(force: true);
    }

    private void Refresh(bool force)
    {
        ResolveLoadout();
        CaptureFallback();

        PartyManager partyManager = PartyManager.Instance;
        if (spellLoadout == null || partyManager == null ||
            partyManager.party == null || partyManager.party.Count == 0 ||
            partyManager.activeIndex < 0 ||
            partyManager.activeIndex >= partyManager.party.Count)
        {
            return;
        }

        PartyManager.CharacterState active =
            partyManager.party[partyManager.activeIndex];
        CharacterDefinition definition = active != null
            ? active.def
            : null;

        if (!force && observedParty == partyManager &&
            appliedPartyIndex == partyManager.activeIndex &&
            appliedDefinition == definition)
        {
            return;
        }

        observedParty = partyManager;
        appliedPartyIndex = partyManager.activeIndex;
        appliedDefinition = definition;

        if (definition != null && definition.useSkillSystemV2Loadout)
        {
            spellLoadout.ReplaceLoadout(
                definition.skillSystemV2BasicAttack,
                definition.equippedSpellsV2);
            return;
        }

        if (preservePawnFallbackLoadout)
        {
            spellLoadout.ReplaceLoadout(
                fallbackBasicAttack,
                fallbackSkills);
        }
        else
        {
            spellLoadout.ReplaceLoadout(null, null);
        }
    }

    private void ResolveLoadout()
    {
        if (spellLoadout == null)
            spellLoadout = GetComponent<SpellLoadout>();
    }

    private void CaptureFallback()
    {
        if (fallbackCaptured || spellLoadout == null)
            return;

        fallbackCaptured = true;
        fallbackBasicAttack = spellLoadout.BasicAttack;
        fallbackSkills.Clear();
        IReadOnlyList<SpellDefinition> skills =
            spellLoadout.EquippedSkills;
        for (int i = 0; i < skills.Count; i++)
        {
            if (skills[i] != null)
                fallbackSkills.Add(skills[i]);
        }
    }
}
