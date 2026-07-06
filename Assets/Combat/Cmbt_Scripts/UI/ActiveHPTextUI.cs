using TMPro;
using UnityEngine;

/// <summary>
/// Displays the active party member's current HP as text.
///
/// Put this over the existing active HP bar in the combat HUD.
/// This script only reads PartyManager. It does not change HP.
/// </summary>
[DisallowMultipleComponent]
public class ActiveHPTextUI : MonoBehaviour
{
    [Header("Display")]
    [SerializeField]
    private TMP_Text hpText;

    [SerializeField]
    private bool showMaximumHP = true;

    [SerializeField]
    private string prefix = "";

    [SerializeField]
    private string emptyText = "- / -";

    [Header("Optional Formatting")]
    [Tooltip("Example: HP {0} / {1}. Leave empty to use the default format.")]
    [SerializeField]
    private string customFormat = "";

    private int lastActiveIndex = int.MinValue;
    private int lastCurrentHP = int.MinValue;
    private int lastMaximumHP = int.MinValue;

    private void Awake()
    {
        if (hpText == null)
            hpText = GetComponent<TMP_Text>();

        if (hpText == null)
        {
            Debug.LogError(
                "ActiveHPTextUI: No TMP_Text assigned or found.",
                this
            );
        }
    }

    private void OnEnable()
    {
        lastActiveIndex = int.MinValue;
        lastCurrentHP = int.MinValue;
        lastMaximumHP = int.MinValue;

        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        if (hpText == null)
            return;

        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null ||
            partyManager.party == null ||
            partyManager.party.Count == 0)
        {
            SetEmpty();
            return;
        }

        int activeIndex = partyManager.activeIndex;

        if (activeIndex < 0 ||
            activeIndex >= partyManager.party.Count)
        {
            SetEmpty();
            return;
        }

        PartyManager.CharacterState active =
            partyManager.party[activeIndex];

        if (active == null ||
            active.def == null)
        {
            SetEmpty();
            return;
        }

        int maximumHP = Mathf.Max(1, active.def.maxHP);

        int currentHP = Mathf.Clamp(
            active.currentHP,
            0,
            maximumHP
        );

        if (!force &&
            activeIndex == lastActiveIndex &&
            currentHP == lastCurrentHP &&
            maximumHP == lastMaximumHP)
        {
            return;
        }

        lastActiveIndex = activeIndex;
        lastCurrentHP = currentHP;
        lastMaximumHP = maximumHP;

        if (!string.IsNullOrWhiteSpace(customFormat))
        {
            hpText.text = string.Format(
                customFormat,
                currentHP,
                maximumHP
            );

            return;
        }

        if (showMaximumHP)
        {
            hpText.text =
                $"{prefix}{currentHP} / {maximumHP}";
        }
        else
        {
            hpText.text =
                $"{prefix}{currentHP}";
        }
    }

    private void SetEmpty()
    {
        if (hpText == null)
            return;

        if (hpText.text != emptyText)
            hpText.text = emptyText;

        lastActiveIndex = int.MinValue;
        lastCurrentHP = int.MinValue;
        lastMaximumHP = int.MinValue;
    }
}
