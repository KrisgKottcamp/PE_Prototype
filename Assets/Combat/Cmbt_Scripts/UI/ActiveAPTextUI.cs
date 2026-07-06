using TMPro;
using UnityEngine;

/// <summary>
/// Displays the active party member's current AP as text.
///
/// Put this beside the existing AP bar in the combat HUD.
/// This script only reads PartyManager. It does not change AP.
/// </summary>
[DisallowMultipleComponent]
public class ActiveAPTextUI : MonoBehaviour
{
    [Header("Display")]
    [SerializeField]
    private TMP_Text apText;

    [SerializeField]
    private bool showMaximumAP = true;

    [SerializeField]
    private string prefix = "";

    [SerializeField]
    private string emptyText = "- / -";

    [Header("Optional Formatting")]
    [Tooltip("Example: AP {0} / {1}. Leave empty to use the default format.")]
    [SerializeField]
    private string customFormat = "";

    private int lastActiveIndex = int.MinValue;
    private int lastCurrentAP = int.MinValue;
    private int lastMaximumAP = int.MinValue;

    private void Awake()
    {
        if (apText == null)
            apText = GetComponent<TMP_Text>();

        if (apText == null)
        {
            Debug.LogError(
                "ActiveAPTextUI: No TMP_Text assigned or found.",
                this
            );
        }
    }

    private void OnEnable()
    {
        lastActiveIndex = int.MinValue;
        lastCurrentAP = int.MinValue;
        lastMaximumAP = int.MinValue;

        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        if (apText == null)
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

        int maximumAP = Mathf.Max(1, active.def.maxAP);

        int currentAP = Mathf.Clamp(
            active.currentAP,
            0,
            maximumAP
        );

        if (!force &&
            activeIndex == lastActiveIndex &&
            currentAP == lastCurrentAP &&
            maximumAP == lastMaximumAP)
        {
            return;
        }

        lastActiveIndex = activeIndex;
        lastCurrentAP = currentAP;
        lastMaximumAP = maximumAP;

        if (!string.IsNullOrWhiteSpace(customFormat))
        {
            apText.text = string.Format(
                customFormat,
                currentAP,
                maximumAP
            );

            return;
        }

        if (showMaximumAP)
        {
            apText.text =
                $"{prefix}{currentAP} / {maximumAP}";
        }
        else
        {
            apText.text =
                $"{prefix}{currentAP}";
        }
    }

    private void SetEmpty()
    {
        if (apText == null)
            return;

        if (apText.text != emptyText)
            apText.text = emptyText;

        lastActiveIndex = int.MinValue;
        lastCurrentAP = int.MinValue;
        lastMaximumAP = int.MinValue;
    }
}
