using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays one persistent PartyManager.CharacterState.
///
/// This component never changes gameplay health. It only reads and displays:
/// - Character name
/// - Portrait
/// - Current and maximum HP
/// - Active-character state
/// - Knocked-out state
/// </summary>
[DisallowMultipleComponent]
public class PartyHealthRow : MonoBehaviour
{
    [Header("Required Display References")]
    [SerializeField]
    private Image hpFill;

    [SerializeField]
    private TMP_Text nameText;

    [SerializeField]
    private TMP_Text hpText;

    [Header("Optional Display References")]
    [SerializeField]
    private Image portraitImage;

    [SerializeField]
    private GameObject activeHighlight;

    [SerializeField]
    private GameObject knockedOutOverlay;

    [SerializeField]
    private CanvasGroup rowCanvasGroup;

    [Header("Health Bar")]
    [SerializeField]
    private bool smoothHealthChanges = true;

    [SerializeField, Min(0.01f)]
    private float healthSmoothSpeed = 12f;

    [SerializeField]
    private bool showMaximumHealth = true;

    [Header("Knocked Out Appearance")]
    [SerializeField, Range(0f, 1f)]
    private float knockedOutAlpha = 0.55f;

    [SerializeField, Range(0f, 1f)]
    private float normalAlpha = 1f;

    [Header("Missing Data")]
    [SerializeField]
    private string missingNameText = "Unknown";

    [SerializeField]
    private string missingHealthText = "- / -";

    private PartyManager.CharacterState boundState;
    private CharacterDefinition lastDefinition;
    private int boundPartyIndex = -1;

    private int lastCurrentHP = int.MinValue;
    private int lastMaximumHP = int.MinValue;
    private bool lastIsActive;
    private bool lastIsKnockedOut;

    private float displayedHealth01 = 1f;
    private bool hasInitializedFill;

    public int BoundPartyIndex => boundPartyIndex;

    public PartyManager.CharacterState BoundState =>
        boundState;

    private void Awake()
    {
        ValidateSetup();
    }

    /// <summary>
    /// Connects this row to one entry in PartyManager.party.
    /// </summary>
    public void Bind(
        PartyManager.CharacterState state,
        int partyIndex)
    {
        boundState = state;
        boundPartyIndex = partyIndex;

        lastDefinition = null;
        lastCurrentHP = int.MinValue;
        lastMaximumHP = int.MinValue;
        lastIsActive = false;
        lastIsKnockedOut = false;
        hasInitializedFill = false;

        Refresh(
            isActive: false,
            immediate: true
        );
    }

    public bool IsBoundTo(
        PartyManager.CharacterState state,
        int partyIndex)
    {
        return
            ReferenceEquals(boundState, state) &&
            boundPartyIndex == partyIndex;
    }

    /// <summary>
    /// Refreshes this row from its bound CharacterState.
    /// Call every frame or whenever party state changes.
    /// </summary>
    public void Refresh(
        bool isActive,
        bool immediate = false)
    {
        if (boundState == null ||
            boundState.def == null)
        {
            ShowMissingState();
            return;
        }

        CharacterDefinition definition =
            boundState.def;

        if (definition != lastDefinition)
        {
            lastDefinition = definition;
            RefreshIdentity(definition);
        }

        int maximumHP =
            Mathf.Max(
                1,
                definition.maxHP
            );

        int currentHP =
            Mathf.Clamp(
                boundState.currentHP,
                0,
                maximumHP
            );

        bool isKnockedOut =
            currentHP <= 0;

        float targetHealth01 =
            currentHP /
            (float)maximumHP;

        if (!hasInitializedFill ||
            immediate ||
            !smoothHealthChanges)
        {
            displayedHealth01 =
                targetHealth01;

            hasInitializedFill = true;
        }
        else
        {
            float interpolation =
                1f -
                Mathf.Exp(
                    -healthSmoothSpeed *
                    Time.unscaledDeltaTime
                );

            displayedHealth01 =
                Mathf.Lerp(
                    displayedHealth01,
                    targetHealth01,
                    interpolation
                );
        }

        if (hpFill != null)
        {
            hpFill.fillAmount =
                Mathf.Clamp01(
                    displayedHealth01
                );
        }

        if (currentHP != lastCurrentHP ||
            maximumHP != lastMaximumHP)
        {
            lastCurrentHP = currentHP;
            lastMaximumHP = maximumHP;

            if (hpText != null)
            {
                hpText.text =
                    showMaximumHealth
                        ? $"{currentHP} / {maximumHP}"
                        : currentHP.ToString();
            }
        }

        if (isActive != lastIsActive)
        {
            lastIsActive = isActive;

            if (activeHighlight != null)
            {
                activeHighlight.SetActive(
                    isActive
                );
            }
        }

        if (isKnockedOut !=
            lastIsKnockedOut)
        {
            lastIsKnockedOut =
                isKnockedOut;

            if (knockedOutOverlay != null)
            {
                knockedOutOverlay.SetActive(
                    isKnockedOut
                );
            }

            if (rowCanvasGroup != null)
            {
                rowCanvasGroup.alpha =
                    isKnockedOut
                        ? knockedOutAlpha
                        : normalAlpha;
            }
        }
    }

    private void RefreshIdentity(
        CharacterDefinition definition)
    {
        if (nameText != null)
        {
            nameText.text =
                string.IsNullOrWhiteSpace(
                    definition.displayName
                )
                    ? $"Party Member {boundPartyIndex + 1}"
                    : definition.displayName;
        }

        if (portraitImage != null)
        {
            Sprite portrait =
                definition.portraitSprite != null
                    ? definition.portraitSprite
                    : definition.combatSprite;

            portraitImage.sprite = portrait;
            portraitImage.enabled =
                portrait != null;

            portraitImage.preserveAspect = true;
        }
    }

    private void ShowMissingState()
    {
        lastDefinition = null;
        lastCurrentHP = int.MinValue;
        lastMaximumHP = int.MinValue;

        displayedHealth01 = 0f;
        hasInitializedFill = true;

        if (nameText != null)
            nameText.text = missingNameText;

        if (hpText != null)
            hpText.text = missingHealthText;

        if (hpFill != null)
            hpFill.fillAmount = 0f;

        if (portraitImage != null)
        {
            portraitImage.sprite = null;
            portraitImage.enabled = false;
        }

        if (activeHighlight != null)
            activeHighlight.SetActive(false);

        if (knockedOutOverlay != null)
            knockedOutOverlay.SetActive(false);

        if (rowCanvasGroup != null)
            rowCanvasGroup.alpha = normalAlpha;
    }

    private void ValidateSetup()
    {
        if (hpFill == null)
        {
            Debug.LogError(
                "PartyHealthRow: HP Fill is not assigned.",
                this
            );
        }

        if (nameText == null)
        {
            Debug.LogWarning(
                "PartyHealthRow: Name Text is not assigned.",
                this
            );
        }

        if (hpText == null)
        {
            Debug.LogWarning(
                "PartyHealthRow: HP Text is not assigned.",
                this
            );
        }
    }
}
