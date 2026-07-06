using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class PartyTargetPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI listText;

    [Header("Input")]
    [SerializeField] private KeyCode confirmKey = KeyCode.Space;
    [SerializeField] private KeyCode altConfirmKey = KeyCode.Return;
    [SerializeField] private bool confirmWithLeftClick = true;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode altUpKey = KeyCode.W;
    [SerializeField] private KeyCode altDownKey = KeyCode.S;

    [Header("Input Lock")]
    [Tooltip("Prevents the same keypress that opened this panel from instantly confirming/canceling.")]
    [SerializeField] private float openInputLockSeconds = 0.12f;

    [Header("Health Target Display")]
    [Tooltip("When enabled, party members already at maximum HP are shown but cannot be selected.")]
    [SerializeField] private bool preventSelectingFullHealthTargets = true;

    [Tooltip("Party members at or below this health percentage use the low-health color.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.50f;

    [Tooltip("Party members at or below this health percentage use the critical-health color.")]
    [SerializeField, Range(0f, 1f)] private float criticalHealthThreshold = 0.25f;

    [SerializeField] private Color highHealthColor = Color.white;
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.85f, 0.10f, 1f);
    [SerializeField] private Color criticalHealthColor = new Color(1f, 0.20f, 0.20f, 1f);
    [SerializeField] private Color fullHealthUnavailableColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Tooltip("Text shown beside a max-health target when it cannot be selected.")]
    [SerializeField] private string fullHealthTag = "[FULL]";

    [Tooltip("If true, adds LOW and CRITICAL tags in addition to changing color.")]
    [SerializeField] private bool showHealthConditionTags = false;

    [Tooltip("How often the open panel rechecks HP values while visible.")]
    [SerializeField, Min(0.02f)] private float openPanelRefreshInterval = 0.10f;

    private readonly List<TargetEntry> entries = new List<TargetEntry>();

    private Func<int, bool> filter;
    private Action<int> onConfirm;
    private Action onCancel;

    private int selected;
    private bool open;

    private float ignoreSubmitUntil;
    private float ignoreCancelUntil;
    private float nextRefreshTime;

    private struct TargetEntry
    {
        public int partyIndex;
        public bool selectable;
        public int currentHP;
        public int maximumHP;
        public float health01;
    }

    private void Awake()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (listText != null)
            listText.richText = true;
    }

    public void Open(
        string title,
        Func<int, bool> filterFn,
        Action<int> confirm,
        Action cancel)
    {
        open = true;
        selected = 0;

        filter = filterFn;
        onConfirm = confirm;
        onCancel = cancel;

        if (titleText != null)
            titleText.text = title;

        if (listText != null)
            listText.richText = true;

        RebuildList();

        if (panelRoot != null)
            panelRoot.SetActive(true);

        RefreshText();

        float now = Time.unscaledTime;
        ignoreSubmitUntil = now + openInputLockSeconds;
        ignoreCancelUntil = now + openInputLockSeconds;
        nextRefreshTime = 0f;
    }

    public void Close()
    {
        open = false;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        entries.Clear();
        filter = null;
        onConfirm = null;
        onCancel = null;
    }

    private void Update()
    {
        if (!open)
            return;

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime =
                Time.unscaledTime +
                openPanelRefreshInterval;

            RebuildList();
            RefreshText();
        }

        if (Input.GetKeyDown(upKey) || Input.GetKeyDown(altUpKey))
        {
            StepSelection(-1);
            RefreshText();
        }

        if (Input.GetKeyDown(downKey) || Input.GetKeyDown(altDownKey))
        {
            StepSelection(1);
            RefreshText();
        }

        float now = Time.unscaledTime;

        if (Input.GetKeyDown(cancelKey) &&
            now >= ignoreCancelUntil)
        {
            onCancel?.Invoke();
            return;
        }

        bool confirmPressed =
            Input.GetKeyDown(confirmKey) ||
            Input.GetKeyDown(altConfirmKey);

        if (confirmWithLeftClick)
            confirmPressed |= Input.GetMouseButtonDown(0);

        if (confirmPressed &&
            now >= ignoreSubmitUntil)
        {
            ConfirmSelection();
        }
    }

    private void RebuildList()
    {
        int previousPartyIndex =
            GetSelectedPartyIndex();

        entries.Clear();

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.party == null)
            return;

        for (int i = 0; i < pm.party.Count; i++)
        {
            if (filter != null && !filter(i))
                continue;

            PartyManager.CharacterState state =
                pm.party[i];

            if (state == null)
                continue;

            int maximumHP =
                state.def != null
                    ? Mathf.Max(1, state.def.maxHP)
                    : 1;

            int currentHP =
                Mathf.Clamp(
                    state.currentHP,
                    0,
                    maximumHP
                );

            bool isFullHealth =
                currentHP >= maximumHP;

            bool selectable =
                !preventSelectingFullHealthTargets ||
                !isFullHealth;

            entries.Add(
                new TargetEntry
                {
                    partyIndex = i,
                    selectable = selectable,
                    currentHP = currentHP,
                    maximumHP = maximumHP,
                    health01 = currentHP / (float)maximumHP
                }
            );
        }

        if (entries.Count == 0)
        {
            selected = 0;
            return;
        }

        selected =
            FindEntryIndexForPartyIndex(
                previousPartyIndex
            );

        if (selected < 0)
            selected = 0;

        if (!entries[selected].selectable)
            MoveSelectionToNearestSelectable();
    }

    private void StepSelection(int direction)
    {
        if (entries.Count == 0)
            return;

        if (!HasSelectableEntry())
            return;

        int clampedDirection =
            direction < 0
                ? -1
                : 1;

        int start =
            Mathf.Clamp(
                selected,
                0,
                entries.Count - 1
            );

        for (int step = 1; step <= entries.Count; step++)
        {
            int candidate =
                (start + clampedDirection * step + entries.Count) %
                entries.Count;

            if (entries[candidate].selectable)
            {
                selected = candidate;
                return;
            }
        }
    }

    private void MoveSelectionToNearestSelectable()
    {
        if (entries.Count == 0)
            return;

        selected =
            Mathf.Clamp(
                selected,
                0,
                entries.Count - 1
            );

        if (entries[selected].selectable)
            return;

        for (int distance = 1; distance < entries.Count; distance++)
        {
            int down = selected + distance;

            if (down < entries.Count &&
                entries[down].selectable)
            {
                selected = down;
                return;
            }

            int up = selected - distance;

            if (up >= 0 &&
                entries[up].selectable)
            {
                selected = up;
                return;
            }
        }
    }

    private void ConfirmSelection()
    {
        if (entries.Count == 0)
            return;

        selected =
            Mathf.Clamp(
                selected,
                0,
                entries.Count - 1
            );

        TargetEntry entry =
            entries[selected];

        if (!entry.selectable)
        {
            RefreshText();
            return;
        }

        onConfirm?.Invoke(entry.partyIndex);
    }

    private void RefreshText()
    {
        if (listText == null)
            return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.party == null)
        {
            listText.text = "No party.";
            return;
        }

        if (entries.Count == 0)
        {
            listText.text = "No valid targets.";
            return;
        }

        StringBuilder sb =
            new StringBuilder(256);

        bool hasSelectable =
            HasSelectableEntry();

        for (int row = 0; row < entries.Count; row++)
        {
            TargetEntry entry =
                entries[row];

            if (entry.partyIndex < 0 ||
                entry.partyIndex >= pm.party.Count)
            {
                continue;
            }

            PartyManager.CharacterState state =
                pm.party[entry.partyIndex];

            Color rowColor =
                GetEntryColor(entry);

            string colorHex =
                ColorUtility.ToHtmlStringRGB(rowColor);

            sb.Append("<color=#");
            sb.Append(colorHex);
            sb.Append(">");

            bool isSelected =
                row == selected &&
                entry.selectable &&
                hasSelectable;

            sb.Append(isSelected ? "> " : "  ");

            sb.Append(
                state != null && state.def != null
                    ? state.def.displayName
                    : $"Member {entry.partyIndex}"
            );

            sb.Append("  (HP ");
            sb.Append(entry.currentHP);
            sb.Append("/");
            sb.Append(entry.maximumHP);
            sb.Append(")");

            if (entry.partyIndex == pm.activeIndex)
                sb.Append("  [YOU]");

            if (!entry.selectable)
            {
                if (!string.IsNullOrWhiteSpace(fullHealthTag))
                {
                    sb.Append("  ");
                    sb.Append(fullHealthTag);
                }
            }
            else if (showHealthConditionTags)
            {
                if (entry.health01 <= criticalHealthThreshold)
                    sb.Append("  [CRITICAL]");
                else if (entry.health01 <= lowHealthThreshold)
                    sb.Append("  [LOW]");
            }

            sb.Append("</color>");
            sb.AppendLine();
        }

        if (!hasSelectable)
        {
            sb.AppendLine();
            sb.Append("<color=#");
            sb.Append(
                ColorUtility.ToHtmlStringRGB(
                    fullHealthUnavailableColor
                )
            );
            sb.Append(">Everyone is already at full HP.</color>");
        }

        listText.text = sb.ToString();
    }

    private Color GetEntryColor(TargetEntry entry)
    {
        if (!entry.selectable)
            return fullHealthUnavailableColor;

        float critical =
            Mathf.Min(
                criticalHealthThreshold,
                lowHealthThreshold
            );

        float low =
            Mathf.Max(
                criticalHealthThreshold,
                lowHealthThreshold
            );

        if (entry.health01 <= critical)
            return criticalHealthColor;

        if (entry.health01 <= low)
            return lowHealthColor;

        return highHealthColor;
    }

    private bool HasSelectableEntry()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].selectable)
                return true;
        }

        return false;
    }

    private int GetSelectedPartyIndex()
    {
        if (entries.Count == 0)
            return -1;

        if (selected < 0 ||
            selected >= entries.Count)
        {
            return -1;
        }

        return entries[selected].partyIndex;
    }

    private int FindEntryIndexForPartyIndex(int partyIndex)
    {
        if (partyIndex < 0)
            return -1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].partyIndex == partyIndex)
                return i;
        }

        return -1;
    }
}