using System.Collections;
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

    [Header("Healing Flash")]
    [SerializeField] private bool flashHealthBarOnHeal = true;
    [SerializeField] private Color healingFlashColor =
        new Color(0.12f, 1f, 0.30f, 1f);
    [SerializeField, Min(0.05f)] private float healingFlashDuration = 1f;
    [SerializeField, Min(0f)] private float healingFlashPulseCount = 3f;

    [Header("Rotation Ready Cue")]
    [Tooltip("Lets this inactive living character advertise that switching is efficient.")]
    [SerializeField] private bool enableRotationReadyCue = true;
    [SerializeField] private Color rotationReadyColor =
        new Color(0.38f, 1f, 0.86f, 1f);
    [SerializeField, Range(0f, 1f)] private float rotationReadyTint = 0.42f;
    [SerializeField, Range(0f, 0.2f)] private float rotationReadyScale = 0.065f;
    [SerializeField, Min(0.1f)] private float rotationPulseSpeed = 1.8f;
    [SerializeField, Min(0.1f)] private float oneShotPulseDuration = 0.72f;

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
    private Color baseHPFillColor = Color.white;
    private bool hasCapturedBaseHPColor;
    private Coroutine healingFlashRoutine;

    private Color basePortraitColor = Color.white;
    private Vector3 basePortraitScale = Vector3.one;
    private bool hasCapturedPortraitAppearance;
    private float rotationCueStrength;
    private float rotationOneShotRemaining;

    public int BoundPartyIndex => boundPartyIndex;

    public PartyManager.CharacterState BoundState =>
        boundState;

    private void Awake()
    {
        ValidateSetup();
        CaptureBaseHPFillColor();
        CaptureBasePortraitAppearance();
    }

    private void OnEnable()
    {
        PartyManager.PartyMemberHealed += HandlePartyMemberHealed;
    }

    private void OnDisable()
    {
        PartyManager.PartyMemberHealed -= HandlePartyMemberHealed;
        StopHealingFlash(restoreColor: true);
        rotationCueStrength = 0f;
        rotationOneShotRemaining = 0f;
        RestorePortraitAppearance();
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

        TickRotationReadyCue(isKnockedOut);
    }

    /// <summary>
    /// Called by PartyHealthHUD. Continuous strength is used for the quiet
    /// shimmer; oneShot adds the stronger threshold-crossing pulse.
    /// </summary>
    public void SetRotationReadyCue(
        float strength,
        bool oneShot)
    {
        rotationCueStrength =
            enableRotationReadyCue
                ? Mathf.Clamp01(strength)
                : 0f;

        if (oneShot && enableRotationReadyCue)
        {
            rotationOneShotRemaining =
                Mathf.Max(
                    rotationOneShotRemaining,
                    oneShotPulseDuration
                );
        }
    }

    private void RefreshIdentity(
        CharacterDefinition definition)
    {
        RestorePortraitAppearance();

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

            CaptureBasePortraitAppearance(
                force: true
            );
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

        rotationCueStrength = 0f;
        rotationOneShotRemaining = 0f;
        RestorePortraitAppearance();
    }

    private void TickRotationReadyCue(
        bool isKnockedOut)
    {
        if (portraitImage == null)
            return;

        CaptureBasePortraitAppearance();

        if (rotationOneShotRemaining > 0f)
        {
            rotationOneShotRemaining =
                Mathf.Max(
                    0f,
                    rotationOneShotRemaining -
                    Time.unscaledDeltaTime
                );
        }

        float oneShot = 0f;

        if (rotationOneShotRemaining > 0f)
        {
            float duration =
                Mathf.Max(
                    0.1f,
                    oneShotPulseDuration
                );

            float elapsed01 =
                1f -
                rotationOneShotRemaining /
                duration;

            oneShot =
                Mathf.Sin(
                    Mathf.Clamp01(elapsed01) *
                    Mathf.PI
                );
        }

        float effectiveStrength =
            isKnockedOut
                ? 0f
                : Mathf.Max(
                    rotationCueStrength,
                    oneShot
                );

        if (effectiveStrength <= 0.001f)
        {
            RestorePortraitAppearance();
            return;
        }

        float pulse =
            0.5f +
            0.5f * Mathf.Sin(
                Time.unscaledTime *
                rotationPulseSpeed *
                Mathf.PI * 2f
            );

        float visualStrength =
            effectiveStrength *
            Mathf.Lerp(0.58f, 1f, pulse);

        portraitImage.color =
            Color.Lerp(
                basePortraitColor,
                rotationReadyColor,
                rotationReadyTint *
                visualStrength
            );

        portraitImage.rectTransform.localScale =
            basePortraitScale *
            (1f +
             rotationReadyScale *
             visualStrength);
    }

    private void CaptureBasePortraitAppearance(
        bool force = false)
    {
        if (portraitImage == null ||
            (hasCapturedPortraitAppearance &&
             !force))
        {
            return;
        }

        basePortraitColor =
            portraitImage.color;
        basePortraitScale =
            portraitImage.rectTransform.
                localScale;
        hasCapturedPortraitAppearance = true;
    }

    private void RestorePortraitAppearance()
    {
        if (portraitImage == null ||
            !hasCapturedPortraitAppearance)
        {
            return;
        }

        portraitImage.color =
            basePortraitColor;
        portraitImage.rectTransform.localScale =
            basePortraitScale;
    }

    private void HandlePartyMemberHealed(
        int healedPartyIndex,
        int restoredAmount)
    {
        if (!flashHealthBarOnHeal || restoredAmount <= 0 ||
            healedPartyIndex != boundPartyIndex || hpFill == null)
        {
            return;
        }

        StartHealingFlash();
    }

    private void StartHealingFlash()
    {
        CaptureBaseHPFillColor();
        StopHealingFlash(restoreColor: true);
        healingFlashRoutine = StartCoroutine(HealingFlashRoutine());
    }

    private IEnumerator HealingFlashRoutine()
    {
        float duration = Mathf.Max(0.05f, healingFlashDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pulse = healingFlashPulseCount > 0f
                ? Mathf.Lerp(
                    0.68f,
                    1f,
                    Mathf.Abs(Mathf.Cos(
                        normalized * Mathf.PI * healingFlashPulseCount
                    ))
                )
                : 1f;
            float strength = (1f - normalized) * pulse;

            hpFill.color = Color.Lerp(
                baseHPFillColor,
                healingFlashColor,
                strength
            );

            yield return null;
        }

        hpFill.color = baseHPFillColor;
        healingFlashRoutine = null;
    }

    private void CaptureBaseHPFillColor()
    {
        if (hasCapturedBaseHPColor || hpFill == null)
            return;

        baseHPFillColor = hpFill.color;
        hasCapturedBaseHPColor = true;
    }

    private void StopHealingFlash(bool restoreColor)
    {
        if (healingFlashRoutine != null)
        {
            StopCoroutine(healingFlashRoutine);
            healingFlashRoutine = null;
        }

        if (restoreColor && hpFill != null && hasCapturedBaseHPColor)
            hpFill.color = baseHPFillColor;
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
