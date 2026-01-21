using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Drop-in CombatSkillMenuController
/// Put this on your CombatHUD (scene object), not on the pawn prefab.
/// It drives a skill list menu, slows time heavily, disables pawn control,
/// and supports a second generic PartyTargetMenu for skills that require an ally target.
/// </summary>
public class CombatSkillMenuController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode openKey = KeyCode.I;
    [SerializeField] private KeyCode confirmKey = KeyCode.Return;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode altUpKey = KeyCode.W;
    [SerializeField] private KeyCode altDownKey = KeyCode.S;

    [Header("Time Slow")]
    [Range(0.01f, 1f)]
    [SerializeField] private float slowTimeScale = 0.12f;

    [Header("UI")]
    [SerializeField] private GameObject skillPanelRoot;      // Skill menu panel root
    [SerializeField] private TextMeshProUGUI listText;       // Skill list text

    [Header("Party Target Menu (generic)")]
    [SerializeField] private PartyTargetMenu partyTargetMenu; // Your generic party picker

    [Header("Optional")]
    [SerializeField] private bool closeMenuAfterCast = true;  // If false, menu stays open after casting non-target skills

    private bool isOpen;
    private bool selectingPartyTarget;

    private int selectedIndex;

    // Saved time state so we can restore exactly
    private float prevTimeScale = 1f;
    private float prevFixedDelta = 0.02f;

    // Cached references discovered at runtime
    private CombatSkillSystem skillSystem;
    private CombatLockout pawnLockout;
    private MonoBehaviour[] pawnControlScripts;

    // Pending cast for party-target skills
    private CombatSkillSystem.PendingCast pendingCast;

    private void Awake()
    {
        if (skillPanelRoot != null) skillPanelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen)
        {
            if (Input.GetKeyDown(openKey))
                OpenSkillMenu();
            return;
        }

        // When the party target menu is open, it handles inputs.
        if (selectingPartyTarget)
            return;

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

        if (Input.GetKeyDown(confirmKey))
        {
            ConfirmSelection();
        }
    }

    private void OpenSkillMenu()
    {
        if (isOpen) return;

        isOpen = true;
        selectingPartyTarget = false;
        selectedIndex = 0;

        CachePawnRefs();

        if (skillPanelRoot != null) skillPanelRoot.SetActive(true);

        // Save and apply time slow
        prevTimeScale = Time.timeScale;
        prevFixedDelta = Time.fixedDeltaTime;

        Time.timeScale = slowTimeScale;
        Time.fixedDeltaTime = prevFixedDelta * Time.timeScale;

        DisablePawnControl(true);

        RefreshSkillText();
    }

    private void CloseAll()
    {
        // If we were in the middle of picking a party target, refund and close that menu too
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

        CloseSkillMenu();
    }

    private void CloseSkillMenu()
    {
        if (!isOpen) return;

        isOpen = false;
        selectingPartyTarget = false;

        if (skillPanelRoot != null) skillPanelRoot.SetActive(false);

        // Restore time exactly
        Time.timeScale = prevTimeScale;
        Time.fixedDeltaTime = prevFixedDelta;

        DisablePawnControl(false);

    }

    private void CachePawnRefs()
    {
        // Skill system lives on the pawn
        skillSystem = FindObjectOfType<CombatSkillSystem>(true);

        if (skillSystem == null)
        {
            pawnControlScripts = null;
            pawnLockout = null;
            return;
        }

        var pawn = skillSystem.gameObject;

        pawnLockout = pawn.GetComponent<CombatLockout>();

        // Add here any scripts you want disabled while the menu is open.
        // Keep it flexible: if a script is missing, it is ignored.
        var list = new System.Collections.Generic.List<MonoBehaviour>();

        var mover = pawn.GetComponent<TopDownMover>();
        if (mover != null) list.Add(mover);

        var melee = pawn.GetComponent<BasicAttack>();
        if (melee != null) list.Add(melee);

        var proj = pawn.GetComponent<ProjectileBasicAttack>();
        if (proj != null) list.Add(proj);

        var swap = pawn.GetComponent<CombatPartyController>();
        if (swap != null) list.Add(swap);

        pawnControlScripts = list.ToArray();
    }

    private void DisablePawnControl(bool disable)
    {
        if (pawnControlScripts == null) return;

        if (disable)
        {
            for (int i = 0; i < pawnControlScripts.Length; i++)
                if (pawnControlScripts[i] != null) pawnControlScripts[i].enabled = false;
            return;
        }

        // Re-enable, but do not fight an active lockout.
        // If lockout is currently active, let it re-enable scripts when it ends.
        if (pawnLockout != null && pawnLockout.IsLockedOut)
            return;

        for (int i = 0; i < pawnControlScripts.Length; i++)
            if (pawnControlScripts[i] != null) pawnControlScripts[i].enabled = true;
    }

    private int GetSkillCount()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.unlockedSkills == null) return 0;
        return pm.Active.unlockedSkills.Count;
    }

    private void ConfirmSelection()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.unlockedSkills == null) return;

        var skills = pm.Active.unlockedSkills;
        if (skills.Count == 0) return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, skills.Count - 1);

        var skill = skills[selectedIndex];
        if (skill == null) return;

        if (skillSystem == null)
        {
            Debug.LogWarning("CombatSkillMenuController: No CombatSkillSystem found. Is the combat pawn spawned?");
            return;
        }

        // Party-target skill flow
        if (skill.requiresPartyTarget)
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

            // Hide skill panel while picking target
            if (skillPanelRoot != null) skillPanelRoot.SetActive(false);

            bool includeDowned = skill.includeDownedTargets;
            string title = string.IsNullOrWhiteSpace(skill.partyTargetMenuTitle) ? "Choose ally" : skill.partyTargetMenuTitle;

            partyTargetMenu.Open(
                title,
                filterFn: (i) =>
                {
                    var st = PartyManager.Instance.party[i];
                    if (includeDowned) return true;
                    return st.currentHP > 0;
                },
                confirm: (targetIndex) =>
                {
                    // Resolve and close all menus
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
                        // Return to skill menu still slowed
                        if (skillPanelRoot != null) skillPanelRoot.SetActive(true);
                        RefreshSkillText();
                    }
                },
                cancel: () =>
                {
                    // Refund AP, return to skill menu
                    skillSystem.CancelCast(pendingCast);
                    pendingCast = null;

                    selectingPartyTarget = false;
                    partyTargetMenu.Close();

                    if (skillPanelRoot != null) skillPanelRoot.SetActive(true);
                    RefreshSkillText();
                }
            );

            return;
        }

        // Non-party-target skill
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

    private void RefreshSkillText()
    {
        if (listText == null) return;

        var pm = PartyManager.Instance;
        if (pm == null)
        {
            listText.text = "No PartyManager.";
            return;
        }

        var skills = pm.Active.unlockedSkills;
        if (skills == null || skills.Count == 0)
        {
            listText.text = "No skills.";
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, skills.Count - 1);

        var sb = new StringBuilder(256);

        for (int i = 0; i < skills.Count; i++)
        {
            var s = skills[i];
            if (s == null) continue;

            int cost = (skillSystem != null) ? skillSystem.GetScaledCost(s) : s.baseApCost;
            bool canUse = (skillSystem != null) && skillSystem.CanUse(s);

            sb.Append(i == selectedIndex ? "> " : "  ");
            sb.Append(s.displayName);
            sb.Append("  (");
            sb.Append(cost);
            sb.Append(" AP)");
            if (!canUse) sb.Append("  [NO AP]");
            if (s.requiresPartyTarget) sb.Append("  [ALLY]");
            sb.AppendLine();
        }

        listText.text = sb.ToString();
    }

    private void OnDisable()
    {
        // Safety: never leave the game slowed if this object is disabled
        if (isOpen)
        {
            Time.timeScale = prevTimeScale;
            Time.fixedDeltaTime = prevFixedDelta;
        }
    }
}
