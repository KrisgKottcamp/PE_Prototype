using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Put this on your CombatHUD scene object, not on the pawn prefab.
/// Drives the skill list panel, slows time heavily, disables pawn control,
/// supports party-target skills, and supports placement skills.
/// </summary>
public class CombatSkillMenuController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode openKey = KeyCode.Tab;
    [SerializeField] private KeyCode confirmKey = KeyCode.Space;
    [SerializeField] private bool confirmWithLeftClick = true;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode altUpKey = KeyCode.W;
    [SerializeField] private KeyCode altDownKey = KeyCode.S;

    [Header("Time Slow")]
    [Range(0.01f, 1f)]
    [SerializeField] private float slowTimeScale = 0.12f;

    [Header("UI")]
    [SerializeField] private GameObject skillPanelRoot;
    [SerializeField] private TextMeshProUGUI listText;

    [Header("Skill Availability")]
    [SerializeField] private bool showNoApTag = true;
    [SerializeField] private bool blockUnavailableSkillConfirm = true;

    [Header("Skill Menu Colors")]
    [Tooltip("Currently selected skill with enough AP.")]
    [SerializeField]
    private Color selectedAffordableSkillColor =
        Color.white;

    [Tooltip("Not currently selected skill with enough AP.")]
    [SerializeField]
    private Color unselectedAffordableSkillColor =
        new Color(0.62f, 0.62f, 0.62f, 1f);

    [Tooltip("Any skill without enough AP.")]
    [SerializeField]
    private Color unavailableSkillColor =
        new Color(0.28f, 0.28f, 0.28f, 1f);

    [Header("Selected Skill Display")]
    [Tooltip("Optional bold markup on the currently selected row.")]
    [SerializeField] private bool boldSelectedSkill = false;

    [Tooltip("How often the open panel refreshes AP availability while it is visible.")]
    [SerializeField, Min(0.02f)]
    private float openPanelRefreshInterval = 0.08f;

    [Header("Party Target Panel")]
    [SerializeField] private PartyTargetPanel partyTargetPanel;

    [Header("Placement Controller")]
    [SerializeField] private PlacementController placement;

    [Header("Attack Momentum")]
    [SerializeField] private AttackMomentumManager attackMomentum;

    [Header("Optional")]
    [SerializeField] private bool closePanelAfterCast = true;

    public bool IsOpen => isOpen;

    private bool isOpen;
    private bool selectingPartyTarget;
    private bool selectingPlacement;

    private int selectedIndex;

    private float prevTimeScale = 1f;
    private float prevFixedDelta = 0.02f;

    private CombatSkillSystem skillSystem;
    private SpellCaster spellCaster;
    private AimTracker aimTracker;
    private CombatLockout pawnLockout;
    private MonoBehaviour[] pawnControlScripts;

    private CombatSkillSystem.PendingCast pendingCast;

    private float nextOpenPanelRefreshTime;

    // Unified menu entry that can be either a legacy skill or a new spell
    private struct MenuEntry
    {
        public SkillDefinition skill;
        public SpellDefinition spell;
        public bool IsSpell => spell != null;
        public string DisplayName => IsSpell ? spell.displayName : (skill != null ? skill.displayName : "???");
    }

    private readonly List<MenuEntry> menuEntries = new();

    private void Awake()
    {
        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        if (listText != null)
            listText.richText = true;
    }

    private void Update()
    {
        if (!isOpen)
        {
            if (Input.GetKeyDown(openKey))
                OpenSkillPanel();

            return;
        }

        if (selectingPartyTarget)
            return;

        if (selectingPlacement)
        {
            if (Input.GetKeyDown(cancelKey))
            {
                if (placement != null)
                    placement.EndPlacement();

                if (skillSystem != null && pendingCast != null)
                    skillSystem.CancelCast(pendingCast);

                pendingCast = null;
                selectingPlacement = false;

                CloseSkillPanel();
            }

            return;
        }

        if (Time.unscaledTime >= nextOpenPanelRefreshTime)
        {
            nextOpenPanelRefreshTime =
                Time.unscaledTime +
                openPanelRefreshInterval;

            RefreshSkillText();
        }

        if (Input.GetKeyDown(cancelKey))
        {
            CloseAll();
            return;
        }

        if (Input.GetKeyDown(upKey) || Input.GetKeyDown(altUpKey))
        {
            selectedIndex = Mathf.Max(0, selectedIndex - 1);
            RefreshSkillText();
        }

        if (Input.GetKeyDown(downKey) || Input.GetKeyDown(altDownKey))
        {
            int count = GetSkillCount();
            selectedIndex = Mathf.Min(count - 1, selectedIndex + 1);
            RefreshSkillText();
        }

        bool confirmPressed = Input.GetKeyDown(confirmKey);

        if (confirmWithLeftClick)
            confirmPressed |= Input.GetMouseButtonDown(0);

        if (confirmPressed)
            ConfirmSelection();
    }

    private void OpenSkillPanel()
    {
        if (isOpen)
            return;

        isOpen = true;
        selectingPartyTarget = false;
        selectingPlacement = false;
        selectedIndex = 0;
        nextOpenPanelRefreshTime = 0f;

        CachePawnRefs();

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(true);

        prevTimeScale = Time.timeScale;
        prevFixedDelta = Time.fixedDeltaTime;

        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = prevFixedDelta * Time.timeScale;

        DisablePawnControl(true);

        if (attackMomentum == null)
            attackMomentum = AttackMomentumManager.Instance;

        if (attackMomentum == null)
            attackMomentum = FindObjectOfType<AttackMomentumManager>(true);

        attackMomentum?.SetMomentumPaused(true);

        RefreshSkillText();
    }

    private void CloseAll()
    {
        if (selectingPartyTarget)
        {
            if (skillSystem != null && pendingCast != null)
                skillSystem.CancelCast(pendingCast);

            pendingCast = null;
            selectingPartyTarget = false;

            if (partyTargetPanel != null)
                partyTargetPanel.Close();

            if (skillPanelRoot != null)
                skillPanelRoot.SetActive(false);
        }

        if (selectingPlacement)
        {
            if (placement != null)
                placement.EndPlacement();

            if (skillSystem != null && pendingCast != null)
                skillSystem.CancelCast(pendingCast);

            pendingCast = null;
            selectingPlacement = false;
        }

        CloseSkillPanel();
    }

    private void CloseSkillPanel()
    {
        if (!isOpen)
            return;

        isOpen = false;
        selectingPartyTarget = false;
        selectingPlacement = false;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        Time.timeScale = prevTimeScale;
        Time.fixedDeltaTime = prevFixedDelta;

        attackMomentum?.SetMomentumPaused(false);
        DisablePawnControl(false);
    }

    private void CachePawnRefs()
    {
        GameObject pawn = null;

        GameObject playerObj = GameObject.FindGameObjectWithTag("PlayerCombatPawn");
        if (playerObj != null)
            pawn = playerObj;

        skillSystem = pawn != null ? pawn.GetComponent<CombatSkillSystem>()
            : FindObjectOfType<CombatSkillSystem>(true);

        if (pawn == null && skillSystem != null)
            pawn = skillSystem.gameObject;

        spellCaster = pawn != null ? pawn.GetComponent<SpellCaster>() : null;
        if (spellCaster == null && pawn != null)
            spellCaster = pawn.AddComponent<SpellCaster>();
        aimTracker = pawn != null ? pawn.GetComponent<AimTracker>() : null;

        if (skillSystem == null && spellCaster == null)
        {
            pawnControlScripts = null;
            pawnLockout = null;
            return;
        }

        if (pawn == null)
        {
            pawnControlScripts = null;
            pawnLockout = null;
            return;
        }

        pawnLockout = pawn.GetComponent<CombatLockout>();

        List<MonoBehaviour> list = new List<MonoBehaviour>();

        AddIfPresent(list, pawn.GetComponent<CombatPawnMover>());
        AddIfPresent(list, pawn.GetComponent<BasicAttack>());
        AddIfPresent(list, pawn.GetComponent<ProjectileBasicAttack>());
        AddIfPresent(list, pawn.GetComponent<HeavyComboAttack>());
        AddIfPresent(list, pawn.GetComponent<CombatPartyController>());

        MonoBehaviour[] behaviours = pawn.GetComponents<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];

            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;

            if (typeName == "WhipAttack")
                AddIfPresent(list, behaviour);
        }

        pawnControlScripts = list.ToArray();
    }

    private void AddIfPresent(
        List<MonoBehaviour> list,
        MonoBehaviour script)
    {
        if (script == null)
            return;

        if (list.Contains(script))
            return;

        list.Add(script);
    }

    private void DisablePawnControl(bool disable)
    {
        if (pawnControlScripts == null)
            return;

        if (disable)
        {
            for (int i = 0; i < pawnControlScripts.Length; i++)
            {
                if (pawnControlScripts[i] != null)
                    pawnControlScripts[i].enabled = false;
            }

            return;
        }

        if (pawnLockout != null && pawnLockout.IsLockedOut)
            return;

        for (int i = 0; i < pawnControlScripts.Length; i++)
        {
            if (pawnControlScripts[i] != null)
                pawnControlScripts[i].enabled = true;
        }
    }

    private int GetSkillCount()
    {
        RebuildMenuEntries();
        return menuEntries.Count;
    }

    private void RebuildMenuEntries()
    {
        menuEntries.Clear();

        PartyManager pm = PartyManager.Instance;
        if (pm == null || pm.Active == null)
            return;

        // Legacy skills first
        if (pm.Active.unlockedSkills != null)
        {
            for (int i = 0; i < pm.Active.unlockedSkills.Count; i++)
            {
                if (pm.Active.unlockedSkills[i] != null)
                    menuEntries.Add(new MenuEntry { skill = pm.Active.unlockedSkills[i] });
            }
        }

        // New spells
        if (pm.Active.unlockedSpells != null)
        {
            for (int i = 0; i < pm.Active.unlockedSpells.Count; i++)
            {
                if (pm.Active.unlockedSpells[i] != null)
                    menuEntries.Add(new MenuEntry { spell = pm.Active.unlockedSpells[i] });
            }
        }
    }

    private void ConfirmSelection()
    {
        RebuildMenuEntries();

        if (menuEntries.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, menuEntries.Count - 1);

        MenuEntry entry = menuEntries[selectedIndex];

        // --- New spell path ---
        if (entry.IsSpell)
        {
            ConfirmSpellSelection(entry.spell);
            return;
        }

        // --- Legacy skill path ---
        SkillDefinition skill = entry.skill;
        if (skill == null)
            return;

        if (skillSystem == null)
        {
            Debug.LogWarning(
                "CombatSkillMenuController: No CombatSkillSystem found. Is the combat pawn spawned?"
            );

            return;
        }

        if (blockUnavailableSkillConfirm &&
            !skillSystem.CanUse(skill))
        {
            RefreshSkillText();
            return;
        }

        if (skill.requiresPartyTarget)
        {
            BeginPartyTargetSkill(skill);
            return;
        }

        if (skill.usesPlacement)
        {
            BeginPlacementSkill(skill);
            return;
        }

        bool ok = skillSystem.TryUseSkill(skill);

        if (ok)
        {
            if (closePanelAfterCast)
                CloseSkillPanel();
            else
                RefreshSkillText();
        }
        else
        {
            RefreshSkillText();
        }
    }

    private void ConfirmSpellSelection(SpellDefinition spell)
    {
        if (spellCaster == null)
        {
            Debug.LogWarning(
                "CombatSkillMenuController: No SpellCaster found. Add SpellCaster to the combat pawn."
            );
            return;
        }

        if (!spellCaster.CanCast(spell))
        {
            RefreshSkillText();
            return;
        }

        Vector2 aimDir = aimTracker != null ? aimTracker.AimDir : Vector2.right;
        bool ok = spellCaster.Cast(spell, aimDir);

        if (ok)
        {
            if (closePanelAfterCast)
                CloseSkillPanel();
            else
                RefreshSkillText();
        }
        else
        {
            RefreshSkillText();
        }
    }

    private void BeginPartyTargetSkill(SkillDefinition skill)
    {
        if (partyTargetPanel == null)
        {
            Debug.LogWarning(
                "CombatSkillMenuController: PartyTargetPanel is not assigned."
            );

            return;
        }

        if (!skillSystem.BeginCast(skill, out pendingCast))
        {
            RefreshSkillText();
            return;
        }

        selectingPartyTarget = true;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        bool includeDowned =
            skill.includeDownedTargets;

        string title =
            GetAllyTargetTitle(skill);

        partyTargetPanel.Open(
            title,
            filterFn: (i) =>
            {
                PartyManager pm = PartyManager.Instance;

                if (pm == null || pm.party == null)
                    return false;

                if (i < 0 || i >= pm.party.Count)
                    return false;

                PartyManager.CharacterState st = pm.party[i];

                if (st == null)
                    return false;

                if (includeDowned)
                    return true;

                return st.currentHP > 0;
            },
            confirm: (targetIndex) =>
            {
                skillSystem.ResolveCast(pendingCast, targetIndex);
                pendingCast = null;

                selectingPartyTarget = false;
                partyTargetPanel.Close();

                if (closePanelAfterCast)
                {
                    CloseSkillPanel();
                }
                else
                {
                    if (skillPanelRoot != null)
                        skillPanelRoot.SetActive(true);

                    RefreshSkillText();
                }
            },
            cancel: () =>
            {
                skillSystem.CancelCast(pendingCast);
                pendingCast = null;

                selectingPartyTarget = false;
                partyTargetPanel.Close();

                if (skillPanelRoot != null)
                    skillPanelRoot.SetActive(true);

                RefreshSkillText();
            }
        );
    }

    private string GetAllyTargetTitle(SkillDefinition skill)
    {
        if (skill == null)
            return "Choose ally";

        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        string fieldName =
            "partyTarget" +
            "Menu" +
            "Title";

        FieldInfo field =
            skill.GetType().GetField(
                fieldName,
                flags
            );

        if (field != null &&
            field.FieldType == typeof(string))
        {
            string value =
                field.GetValue(skill) as string;

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        PropertyInfo property =
            skill.GetType().GetProperty(
                fieldName,
                flags
            );

        if (property != null &&
            property.PropertyType == typeof(string))
        {
            string value =
                property.GetValue(skill) as string;

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "Choose ally";
    }

    private void BeginPlacementSkill(SkillDefinition skill)
    {
        if (placement == null)
        {
            Debug.LogWarning(
                "CombatSkillMenuController: placement not assigned."
            );

            return;
        }

        if (!skillSystem.BeginCast(skill, out pendingCast))
        {
            RefreshSkillText();
            return;
        }

        selectingPlacement = true;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        Transform playerTf =
            skillSystem != null
                ? skillSystem.transform
                : null;

        float previewRadius =
            skill.executionType == SkillExecutionType.AoE
                ? skill.aoeRadius
                : skill.placementPreviewRadius;

        placement.BeginPlacement(
            previewRadius,
            skill.placementRange,
            skill.placementBlockMask,
            playerTf,
            confirm: (worldPos) =>
            {
                skillSystem.ResolveCastAtPosition(pendingCast, worldPos);
                pendingCast = null;
                selectingPlacement = false;

                if (closePanelAfterCast)
                {
                    CloseSkillPanel();
                }
                else
                {
                    if (skillPanelRoot != null)
                        skillPanelRoot.SetActive(true);

                    RefreshSkillText();
                }
            }
        );
    }

    private void RefreshSkillText()
    {
        if (listText == null)
            return;

        listText.richText = true;

        RebuildMenuEntries();

        if (menuEntries.Count == 0)
        {
            listText.text = "No skills.";
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, menuEntries.Count - 1);

        StringBuilder sb = new StringBuilder(256);

        for (int i = 0; i < menuEntries.Count; i++)
        {
            MenuEntry entry = menuEntries[i];
            bool isSelected = i == selectedIndex;

            string displayName;
            string costText;
            bool canUse;
            string tags = "";

            if (entry.IsSpell)
            {
                displayName = entry.spell.displayName;
                float cd = spellCaster != null ? spellCaster.GetCooldownRemaining(entry.spell) : 0f;
                canUse = spellCaster != null && spellCaster.CanCast(entry.spell);
                costText = cd > 0f ? $"CD {cd:0.0}s" : SpellBaseTag(entry.spell.spellBase);

                if (entry.spell.spellBase == SpellBase.Movement)
                    tags += "  [MOVE]";
                else if (entry.spell.spellBase == SpellBase.Stat)
                    tags += "  [BUFF]";
            }
            else
            {
                SkillDefinition skill = entry.skill;
                displayName = skill.displayName;

                int cost = skillSystem != null
                    ? skillSystem.GetScaledCost(skill)
                    : skill.baseApCost;

                canUse = skillSystem != null && skillSystem.CanUse(skill);
                costText = $"{cost} AP";

                if (!canUse && showNoApTag)
                    tags += "  [NO AP]";
                if (skill.requiresPartyTarget)
                    tags += "  [ALLY]";
                if (skill.usesPlacement)
                    tags += "  [PLACE]";
            }

            Color rowColor = GetSkillRowColor(isSelected, canUse);
            string rowColorHex = ColorUtility.ToHtmlStringRGB(rowColor);

            sb.Append("<color=#");
            sb.Append(rowColorHex);
            sb.Append(">");

            if (isSelected && boldSelectedSkill)
                sb.Append("<b>");

            sb.Append(isSelected ? "> " : "  ");
            sb.Append(displayName);
            sb.Append("  (");
            sb.Append(costText);
            sb.Append(")");
            sb.Append(tags);

            if (isSelected && boldSelectedSkill)
                sb.Append("</b>");

            sb.Append("</color>");
            sb.AppendLine();
        }

        listText.text = sb.ToString();
    }

    private static string SpellBaseTag(SpellBase spellBase)
    {
        switch (spellBase)
        {
            case SpellBase.Casting: return "CAST";
            case SpellBase.Movement: return "MOVE";
            case SpellBase.Stat: return "BUFF";
            default: return "SPELL";
        }
    }

    private Color GetSkillRowColor(
        bool isSelected,
        bool canUse)
    {
        if (!canUse)
            return unavailableSkillColor;

        if (isSelected)
            return selectedAffordableSkillColor;

        return unselectedAffordableSkillColor;
    }

    private void OnDisable()
    {
        if (isOpen)
        {
            Time.timeScale = prevTimeScale;
            Time.fixedDeltaTime = prevFixedDelta;
            attackMomentum?.SetMomentumPaused(false);
            DisablePawnControl(false);
        }
    }
}