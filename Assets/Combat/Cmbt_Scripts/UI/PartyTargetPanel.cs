using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class PartyTargetMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI listText;

    [Header("Input")]
    [SerializeField] private KeyCode confirmKey = KeyCode.Return;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode altUpKey = KeyCode.W;
    [SerializeField] private KeyCode altDownKey = KeyCode.S;

    [Header("Input Lock")]
    [Tooltip("Prevents the same keypress that opened this menu from instantly confirming/canceling.")]
    [SerializeField] private float openInputLockSeconds = 0.12f;

    private readonly List<int> indices = new();
    private Func<int, bool> filter;
    private Action<int> onConfirm;
    private Action onCancel;

    private int selected;
    private bool open;

    private float ignoreSubmitUntil;
    private float ignoreCancelUntil;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open(string title, Func<int, bool> filterFn, Action<int> confirm, Action cancel)
    {
        open = true;
        selected = 0;

        filter = filterFn;
        onConfirm = confirm;
        onCancel = cancel;

        if (titleText != null) titleText.text = title;

        RebuildList();
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshText();

        // Lock submit/cancel briefly to prevent instant selection on the same Return press
        float now = Time.unscaledTime;
        ignoreSubmitUntil = now + openInputLockSeconds;
        ignoreCancelUntil = now + openInputLockSeconds;
    }

    public void Close()
    {
        open = false;
        if (panelRoot != null) panelRoot.SetActive(false);

        indices.Clear();
        filter = null;
        onConfirm = null;
        onCancel = null;
    }

    private void Update()
    {
        if (!open) return;

        // Navigation always allowed
        if (Input.GetKeyDown(upKey) || Input.GetKeyDown(altUpKey))
        {
            selected = Mathf.Max(0, selected - 1);
            RefreshText();
        }

        if (Input.GetKeyDown(downKey) || Input.GetKeyDown(altDownKey))
        {
            selected = Mathf.Min(indices.Count - 1, selected + 1);
            RefreshText();
        }

        float now = Time.unscaledTime;

        // Cancel (blocked briefly after open)
        if (Input.GetKeyDown(cancelKey) && now >= ignoreCancelUntil)
        {
            onCancel?.Invoke();
            return;
        }

        // Confirm (blocked briefly after open)
        if (Input.GetKeyDown(confirmKey) && now >= ignoreSubmitUntil)
        {
            if (indices.Count == 0) return;
            int targetIndex = indices[Mathf.Clamp(selected, 0, indices.Count - 1)];
            onConfirm?.Invoke(targetIndex);
        }
    }

    private void RebuildList()
    {
        indices.Clear();

        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null) return;

        for (int i = 0; i < pm.party.Count; i++)
        {
            if (filter != null && !filter(i)) continue;
            indices.Add(i);
        }

        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, indices.Count - 1));
    }

    private void RefreshText()
    {
        if (listText == null) return;

        var pm = PartyManager.Instance;
        if (pm == null)
        {
            listText.text = "No party.";
            return;
        }

        if (indices.Count == 0)
        {
            listText.text = "No valid targets.";
            return;
        }

        var sb = new StringBuilder(256);

        for (int row = 0; row < indices.Count; row++)
        {
            int i = indices[row];
            var st = pm.party[i];
            int maxHp = st.def != null ? st.def.maxHP : 0;

            sb.Append(row == selected ? "> " : "  ");
            sb.Append(st.def != null ? st.def.displayName : $"Member {i}");
            sb.Append("  (HP ");
            sb.Append(st.currentHP);
            sb.Append("/");
            sb.Append(maxHp);
            sb.Append(")");
            if (i == pm.activeIndex) sb.Append("  [YOU]");
            sb.AppendLine();
        }

        listText.text = sb.ToString();
    }
}
