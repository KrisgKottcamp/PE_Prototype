using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Put this on your CombatHUD scene object, not on the pawn prefab.
/// Drives the skill list menu, slows time heavily, disables pawn control,
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

    [Header("Party Target Menu")]
    [SerializeField] private PartyTargetMenu partyTargetMenu;

    [Header("Placement Controller")]
    [SerializeField] private PlacementController placement;

    [Header("Attack Momentum")]
    [SerializeField] private AttackMomentumManager attackMomentum;

    [Header("Optional")]
    [SerializeField] private bool closeMenuAfterCast = true;

    public bool IsOpen => isOpen;

    private bool isOpen;
    private bool selectingPartyTarget;
    private bool selectingPlacement;

    private int selectedIndex;

    private float prevTimeScale = 1f;
    private float prevFixedDelta = 0.02f;

    private CombatSkillSystem skillSystem;
    private CombatLockout pawnLockout;
    private MonoBehaviour[] pawnControlScripts;

    private CombatSkillSystem.PendingCast pendingCast;

    private void Awake()
    {
        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen)
        {
            if (Input.GetKeyDown(openKey))
                OpenSkillMenu();

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

                CloseSkillMenu();
            }

            return;
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

    private void OpenSkillMenu()
    {
        if (isOpen) return;

        isOpen = true;
        selectingPartyTarget = false;
        selectingPlacement = false;
        selectedIndex = 0;

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

            if (partyTargetMenu != null)
                partyTargetMenu.Close();

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

        CloseSkillMenu();
    }

    private void CloseSkillMenu()
    {
        if (!isOpen) return;

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
        skillSystem = FindObjectOfType<CombatSkillSystem>(true);

        if (skillSystem == null)
        {
            pawnControlScripts = null;
            pawnLockout = null;
            return;
        }

        GameObject pawn = skillSystem.gameObject;

        pawnLockout = pawn.GetComponent<CombatLockout>();

        List<MonoBehaviour> list = new();

        AddIfPresent(list, pawn.GetComponent<CombatPawnMover>());
        AddIfPresent(list, pawn.GetComponent<BasicAttack>());
        AddIfPresent(list, pawn.GetComponent<ProjectileBasicAttack>());
        AddIfPresent(list, pawn.GetComponent<HeavyComboAttack>());
        AddIfPresent(list, pawn.GetComponent<CombatPartyController>());

        // Optional Dominic script support without hard dependency.
        // This lets the project compile even if WhipAttack is not in an older branch.
        MonoBehaviour[] behaviours = pawn.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;

            if (typeName == "WhipAttack")
                AddIfPresent(list, behaviour);
        }

        pawnControlScripts = list.ToArray();
    }

    private void AddIfPresent(List<MonoBehaviour> list, MonoBehaviour script)
    {
        if (script == null) return;
        if (list.Contains(script)) return;

        list.Add(script);
    }

    private void DisablePawnControl(bool disable)
    {
        if (pawnControlScripts == null) return;

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
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.unlockedSkills == null)
            return 0;

        return pm.Active.unlockedSkills.Count;
    }

    private void ConfirmSelection()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.unlockedSkills == null)
            return;

        List<SkillDefinition> skills = pm.Active.unlockedSkills;

        if (skills.Count == 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, skills.Count - 1);

        SkillDefinition skill = skills[selectedIndex];

        if (skill == null)
            return;

        if (skillSystem == null)
        {
            Debug.LogWarning("CombatSkillMenuController: No CombatSkillSystem found. Is the combat pawn spawned?");
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
            if (closeMenuAfterCast)
                CloseSkillMenu();
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
        if (partyTargetMenu == null)
        {
            Debug.LogWarning("CombatSkillMenuController: partyTargetMenu not assigned.");
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

        bool includeDowned = skill.includeDownedTargets;
        string title = string.IsNullOrWhiteSpace(skill.partyTargetMenuTitle)
            ? "Choose ally"
            : skill.partyTargetMenuTitle;

        partyTargetMenu.Open(
            title,
            filterFn: (i) =>
            {
                PartyManager pm = PartyManager.Instance;
                if (pm == null || pm.party == null) return false;
                if (i < 0 || i >= pm.party.Count) return false;

                PartyManager.CharacterState st = pm.party[i];
                if (st == null) return false;

                if (includeDowned) return true;

                return st.currentHP > 0;
            },
            confirm: (targetIndex) =>
            {
                skillSystem.ResolveCast(pendingCast, targetIndex);
                pendingCast = null;

                selectingPartyTarget = false;
                partyTargetMenu.Close();

                if (closeMenuAfterCast)
                {
                    CloseSkillMenu();
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
                partyTargetMenu.Close();

                if (skillPanelRoot != null)
                    skillPanelRoot.SetActive(true);

                RefreshSkillText();
            }
        );
    }

    private void BeginPlacementSkill(SkillDefinition skill)
    {
        if (placement == null)
        {
            Debug.LogWarning("CombatSkillMenuController: placement not assigned.");
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

        Transform playerTf = skillSystem != null
            ? skillSystem.transform
            : null;

        float previewRadius = skill.executionType == SkillExecutionType.AoE
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

                if (closeMenuAfterCast)
                {
                    CloseSkillMenu();
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
        if (listText == null) return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null)
        {
            listText.text = "No PartyManager.";
            return;
        }

        List<SkillDefinition> skills = pm.Active.unlockedSkills;

        if (skills == null || skills.Count == 0)
        {
            listText.text = "No skills.";
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, skills.Count - 1);

        StringBuilder sb = new(256);

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition s = skills[i];
            if (s == null) continue;

            int cost = skillSystem != null
                ? skillSystem.GetScaledCost(s)
                : s.baseApCost;

            bool canUse = skillSystem != null && skillSystem.CanUse(s);

            sb.Append(i == selectedIndex ? "> " : "  ");
            sb.Append(s.displayName);
            sb.Append("  (");
            sb.Append(cost);
            sb.Append(" AP)");

            if (!canUse)
                sb.Append("  [NO AP]");

            if (s.requiresPartyTarget)
                sb.Append("  [ALLY]");

            if (s.usesPlacement)
                sb.Append("  [PLACE]");

            sb.AppendLine();
        }

        listText.text = sb.ToString();
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