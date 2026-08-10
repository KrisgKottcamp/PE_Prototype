using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using TMPro;
using ProjectEri.SkillSystemV2;

/// <summary>
/// Put this on your CombatHUD scene object, not on the pawn prefab.
/// Drives the skill list panel, slows time heavily, disables pawn control,
/// supports party-target, placement, and directional-aim skills.
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

    [Header("Skill System V2")]
    [Tooltip("When the spawned pawn has a PlayerSpellV2Bridge with equipped skills, show that loadout. An empty V2 loadout falls back to the legacy PartyManager skill list.")]
    [SerializeField] private bool preferV2LoadoutWhenAvailable = true;

    public bool IsOpen => isOpen;

    private bool isOpen;
    private bool selectingPartyTarget;
    private bool selectingPlacement;
    private bool selectingDirectionalAim;
    private bool selectingV2Spell;

    private int selectedIndex;

    private float prevTimeScale = 1f;
    private float prevFixedDelta = 0.02f;

    private CombatSkillSystem skillSystem;
    private PlayerSpellV2Bridge v2Bridge;
    private PlayerSpellV2Bridge subscribedV2Bridge;
    private CombatLockout pawnLockout;
    private MonoBehaviour[] pawnControlScripts;

    private CombatSkillSystem.PendingCast pendingCast;

    private float nextOpenPanelRefreshTime;

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

        if (selectingDirectionalAim)
        {
            if (Input.GetKeyDown(cancelKey))
            {
                CancelDirectionalAim();
                return;
            }

            bool aimConfirmPressed = Input.GetKeyDown(confirmKey);

            if (confirmWithLeftClick)
                aimConfirmPressed |= Input.GetMouseButtonDown(0);

            if (aimConfirmPressed)
                ConfirmDirectionalAim();

            return;
        }

        // The bridge owns aim refresh plus confirm/cancel input while a V2
        // delivery is targeting. Its events return control to this menu.
        if (selectingV2Spell)
            return;

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

        CachePawnRefs();

        if (pawnLockout != null &&
            pawnLockout.IsLockedOut)
        {
            return;
        }

        isOpen = true;
        selectingPartyTarget = false;
        selectingPlacement = false;
        selectingDirectionalAim = false;
        selectingV2Spell = false;
        selectedIndex = 0;
        nextOpenPanelRefreshTime = 0f;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(true);

        // The menu must record normal/focus time, never a temporary hitstop
        // scale. Otherwise closing it can restore near-zero time permanently.
        HitstopManager.ReleaseForExternalTimeControl();

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

        if (selectingDirectionalAim)
        {
            if (placement != null)
                placement.EndDirectionalAim();

            if (skillSystem != null && pendingCast != null)
                skillSystem.CancelCast(pendingCast);

            pendingCast = null;
            selectingDirectionalAim = false;
        }

        if (selectingV2Spell)
        {
            v2Bridge?.CancelTargeting();
            selectingV2Spell = false;
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
        selectingDirectionalAim = false;
        selectingV2Spell = false;

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
        PlayerSpellV2Bridge foundBridge =
            FindObjectOfType<PlayerSpellV2Bridge>(true);
        BindV2Bridge(foundBridge);

        if (skillSystem == null && v2Bridge == null)
        {
            pawnControlScripts = null;
            pawnLockout = null;
            return;
        }

        GameObject pawn = v2Bridge != null
            ? v2Bridge.gameObject
            : skillSystem.gameObject;

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
        if (UsesV2Loadout())
            return v2Bridge.SkillCount;

        PartyManager pm = PartyManager.Instance;

        if (pm == null ||
            pm.Active == null ||
            pm.Active.unlockedSkills == null)
        {
            return 0;
        }

        return pm.Active.unlockedSkills.Count;
    }

    private void ConfirmSelection()
    {
        if (UsesV2Loadout())
        {
            ConfirmV2Selection();
            return;
        }

        PartyManager pm = PartyManager.Instance;

        if (pm == null ||
            pm.Active == null ||
            pm.Active.unlockedSkills == null)
        {
            return;
        }

        List<SkillDefinition> skills = pm.Active.unlockedSkills;

        if (skills.Count == 0)
            return;

        selectedIndex =
            Mathf.Clamp(
                selectedIndex,
                0,
                skills.Count - 1
            );

        SkillDefinition skill = skills[selectedIndex];

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

        if (skill.requiresAimConfirmation)
        {
            BeginDirectionalAimSkill(skill);
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

    private void ConfirmV2Selection()
    {
        int count = v2Bridge != null ? v2Bridge.SkillCount : 0;
        if (count <= 0)
            return;

        selectedIndex = Mathf.Clamp(selectedIndex, 0, count - 1);
        SpellDefinition spell = v2Bridge.GetSkill(selectedIndex);
        if (spell == null)
            return;

        if (blockUnavailableSkillConfirm &&
            !v2Bridge.CanUse(spell, out _))
        {
            RefreshSkillText();
            return;
        }

        selectingV2Spell = true;
        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        if (!v2Bridge.BeginSpell(spell, out string rejectionReason))
        {
            selectingV2Spell = false;
            if (skillPanelRoot != null)
                skillPanelRoot.SetActive(true);

            Debug.LogWarning(
                $"CombatSkillMenuController: V2 spell '{spell.DisplayName}' could not begin: {rejectionReason}.",
                this);
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

        if (skill.executionType ==
            SkillExecutionType.EriHealingCall)
        {
            EriSupportManager support =
                EriSupportManager.Instance;

            if (support != null)
            {
                title =
                    $"Eri {support.CurrentHealingPoints}/" +
                    $"{support.UnlockedCapacity} - " +
                    "Choose Ally";
            }
        }

        partyTargetPanel.Open(
            title,
            filterFn: (i) =>
            {
                if (skill.executionType ==
                    SkillExecutionType.EriHealingCall &&
                    i == EriSupportManager.SelfTargetIndex)
                {
                    EriSupportManager support =
                        EriSupportManager.Instance;

                    // Keep Eri visible as a disabled [FULL] row when she does
                    // not currently need healing.
                    return support != null &&
                        !support.IsEriDefeated &&
                        support.CurrentHealingPoints > 0;
                }

                PartyManager pm = PartyManager.Instance;

                if (pm == null || pm.party == null)
                    return false;

                if (i < 0 || i >= pm.party.Count)
                    return false;

                PartyManager.CharacterState st = pm.party[i];

                if (st == null)
                    return false;

                if (skill.executionType ==
                    SkillExecutionType.EriHealingCall)
                {
                    return skillSystem.
                        CanUseEriHealingOnTarget(i);
                }

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
            },
            includeEri:
                skill.executionType ==
                SkillExecutionType.EriHealingCall
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

    private void BeginDirectionalAimSkill(SkillDefinition skill)
    {
        if (placement == null)
        {
            Debug.LogWarning(
                "CombatSkillMenuController: placement controller not assigned; directional aim cannot start."
            );

            return;
        }

        if (!skillSystem.BeginCast(
            skill,
            out pendingCast,
            deferCastVfx: true))
        {
            RefreshSkillText();
            return;
        }

        selectingDirectionalAim = true;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(false);

        placement.BeginDirectionalAim(
            skill.aimPreviewRange,
            skill.aimPreviewRadius,
            skill.aimPreviewConeAngle,
            skillSystem.transform
        );
    }

    private void ConfirmDirectionalAim()
    {
        if (placement == null ||
            !placement.ConfirmDirectionalAim(out Vector2 aimDirection))
        {
            return;
        }

        CombatSkillSystem.PendingCast cast = pendingCast;
        pendingCast = null;
        selectingDirectionalAim = false;

        skillSystem.ResolveCastWithAim(cast, aimDirection);

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

    private void CancelDirectionalAim()
    {
        if (placement != null)
            placement.EndDirectionalAim();

        if (skillSystem != null && pendingCast != null)
            skillSystem.CancelCast(pendingCast);

        pendingCast = null;
        selectingDirectionalAim = false;

        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(true);

        // Keep the menu open so time remains slowed after backing out of aim.
        RefreshSkillText();
    }

    private void RefreshSkillText()
    {
        if (listText == null)
            return;

        listText.richText = true;

        if (UsesV2Loadout())
        {
            RefreshV2SkillText();
            return;
        }

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

        selectedIndex =
            Mathf.Clamp(
                selectedIndex,
                0,
                skills.Count - 1
            );

        StringBuilder sb = new StringBuilder(256);

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];

            if (skill == null)
                continue;

            bool canUse =
                skillSystem != null &&
                skillSystem.CanUse(skill);

            bool isSelected =
                i == selectedIndex;

            Color rowColor =
                GetSkillRowColor(
                    isSelected,
                    canUse
                );

            string rowColorHex =
                ColorUtility.ToHtmlStringRGB(rowColor);

            sb.Append("<color=#");
            sb.Append(rowColorHex);
            sb.Append(">");

            if (isSelected && boldSelectedSkill)
                sb.Append("<b>");

            sb.Append(isSelected ? "> " : "  ");
            sb.Append(skill.displayName);
            sb.Append("  (");
            sb.Append(
                skillSystem != null
                    ? skillSystem.GetCostDisplay(skill)
                    : $"{skill.baseApCost} AP"
            );
            sb.Append(")");

            if (!canUse &&
                showNoApTag &&
                skill.executionType !=
                SkillExecutionType.EriHealingCall)
            {
                sb.Append("  [NO AP]");
            }

            if (skill.executionType ==
                SkillExecutionType.EriHealingCall)
            {
                sb.Append("  [ERI]");
            }

            if (skill.requiresPartyTarget)
                sb.Append("  [ALLY]");

            if (skill.usesPlacement)
                sb.Append("  [PLACE]");

            if (isSelected && boldSelectedSkill)
                sb.Append("</b>");

            sb.Append("</color>");
            sb.AppendLine();
        }

        listText.text = sb.ToString();
    }

    private void RefreshV2SkillText()
    {
        int count = v2Bridge != null ? v2Bridge.SkillCount : 0;
        if (count <= 0)
        {
            listText.text = "No V2 spells equipped.";
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, count - 1);
        StringBuilder sb = new StringBuilder(256);

        for (int i = 0; i < count; i++)
        {
            SpellDefinition spell = v2Bridge.GetSkill(i);
            if (spell == null)
                continue;

            bool canUse = v2Bridge.CanUse(spell, out SpellCastFailure failure);
            bool isSelected = i == selectedIndex;
            string color = ColorUtility.ToHtmlStringRGB(
                GetSkillRowColor(isSelected, canUse));

            sb.Append("<color=#");
            sb.Append(color);
            sb.Append(">");
            if (isSelected && boldSelectedSkill)
                sb.Append("<b>");

            sb.Append(isSelected ? "> " : "  ");
            sb.Append(spell.DisplayName);
            sb.Append("  (");
            sb.Append(v2Bridge.GetCostDisplay(spell));
            sb.Append(")");

            if (!canUse && showNoApTag)
            {
                sb.Append(failure == SpellCastFailure.InsufficientResources
                    ? "  [NO AP]"
                    : $"  [{failure.ToString().ToUpperInvariant()}]");
            }

            if (spell.Delivery != null &&
                spell.Delivery.TargetingRequirement != CastTargetingRequirement.None)
            {
                sb.Append("  [AIM]");
            }

            if (isSelected && boldSelectedSkill)
                sb.Append("</b>");
            sb.Append("</color>");
            sb.AppendLine();
        }

        listText.text = sb.ToString();
    }

    private bool UsesV2Loadout()
    {
        return preferV2LoadoutWhenAvailable &&
               v2Bridge != null &&
               v2Bridge.SkillCount > 0;
    }

    private void BindV2Bridge(PlayerSpellV2Bridge bridge)
    {
        if (subscribedV2Bridge == bridge)
        {
            v2Bridge = bridge;
            return;
        }

        if (subscribedV2Bridge != null)
        {
            subscribedV2Bridge.SpellConfirmed -= HandleV2SpellConfirmed;
            subscribedV2Bridge.SpellCancelled -= HandleV2SpellCancelled;
            subscribedV2Bridge.SpellRejected -= HandleV2SpellRejected;
        }

        subscribedV2Bridge = bridge;
        v2Bridge = bridge;

        if (subscribedV2Bridge != null)
        {
            subscribedV2Bridge.SpellConfirmed += HandleV2SpellConfirmed;
            subscribedV2Bridge.SpellCancelled += HandleV2SpellCancelled;
            subscribedV2Bridge.SpellRejected += HandleV2SpellRejected;
        }
    }

    private void HandleV2SpellConfirmed(SpellDefinition spell)
    {
        if (!selectingV2Spell)
            return;

        selectingV2Spell = false;
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

    private void HandleV2SpellCancelled(SpellDefinition spell)
    {
        if (!selectingV2Spell)
            return;

        selectingV2Spell = false;
        if (skillPanelRoot != null)
            skillPanelRoot.SetActive(true);
        RefreshSkillText();
    }

    private void HandleV2SpellRejected(
        SpellDefinition spell,
        SpellCastFailure failure)
    {
        // A rejected confirmation can remain in aim mode, so do not close it.
        // Refreshing is enough to expose cooldown/AP/busy state to the player.
        if (!selectingV2Spell)
            RefreshSkillText();
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
        if (selectingV2Spell)
            v2Bridge?.CancelTargeting();

        if (isOpen)
        {
            Time.timeScale = prevTimeScale;
            Time.fixedDeltaTime = prevFixedDelta;
            attackMomentum?.SetMomentumPaused(false);
            DisablePawnControl(false);
        }

        BindV2Bridge(null);
    }
}
