using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds a normal, hand-editable Canvas row to EriSupportManager.
/// Use the custom Inspector's Build Editable Eri Row button once in Edit Mode.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriHealingPointsUI : MonoBehaviour
{
    [Header("Editable Canvas References")]
    [Tooltip("PartyHealthHud/RowsContainer. Used by the editor setup button and runtime ordering.")]
    [SerializeField] private RectTransform rowsContainer;
    [SerializeField] private GameObject rowRoot;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text healingPointsText;
    [SerializeField] private Image hpFill;
    [SerializeField] private CanvasGroup rowCanvasGroup;

    [Header("Displayed Text")]
    [SerializeField] private string eriName = "Eri";
    [SerializeField] private string healingPointsLabel = "Heals";
    [SerializeField] private string defeatedSuffix = " (Down)";
    [SerializeField] private bool showMaximumHealth = true;

    [Header("Health Animation")]
    [SerializeField] private bool smoothHealthChanges = true;
    [SerializeField, Min(0.1f)] private float healthSmoothSpeed = 12f;

    [Header("Row Behavior")]
    [Tooltip("Keeps Eri below the playable party when PartyHealthHUD rebuilds its rows.")]
    [SerializeField] private bool keepAsLastPartyRow = true;
    [SerializeField] private bool hideWhenSupportUnavailable = true;
    [SerializeField, Range(0f, 1f)] private float defeatedRowAlpha = 0.62f;

    [Header("Runtime Debug")]
    [SerializeField] private int currentHP;
    [SerializeField] private int maximumHP = 1;
    [SerializeField] private int healingPoints;
    [SerializeField] private int healingCapacity = 1;
    [SerializeField] private bool eriDefeated;
    [SerializeField] private string setupStatus = "Not validated";

    private float targetHealth01 = 1f;
    private float displayedHealth01 = 1f;
    private bool hasInitializedHealth;
    private bool hasSupportData;
    private float nextSupportRetryTime;
    private bool hasLoggedMissingReferences;
    private static Sprite generatedFillSprite;

    private void Awake()
    {
        ValidateReferences();
        EnsureHealthFillConfiguration();
    }

    private void OnEnable()
    {
        EnsureHealthFillConfiguration();

        EriSupportManager.HealingPointsChanged +=
            OnHealingPointsChanged;

        EriSupportManager.EriHealthChanged +=
            OnEriHealthChanged;

        EriSupportManager.EriDefeated +=
            OnEriDefeated;

        EriSupportManager.EriRevived +=
            OnEriRevived;

        RefreshAll(immediate: true);
    }

    private void OnDisable()
    {
        EriSupportManager.HealingPointsChanged -=
            OnHealingPointsChanged;

        EriSupportManager.EriHealthChanged -=
            OnEriHealthChanged;

        EriSupportManager.EriDefeated -=
            OnEriDefeated;

        EriSupportManager.EriRevived -=
            OnEriRevived;
    }

    private void Update()
    {
        if (!hasSupportData &&
            Time.unscaledTime >= nextSupportRetryTime)
        {
            nextSupportRetryTime =
                Time.unscaledTime + 0.25f;

            RefreshAll(immediate: true);
        }

        if (!hasInitializedHealth || hpFill == null)
            return;

        if (smoothHealthChanges)
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
        else
        {
            displayedHealth01 = targetHealth01;
        }

        ApplyHealthFill(displayedHealth01);
    }

    private void LateUpdate()
    {
        if (!keepAsLastPartyRow ||
            rowRoot == null)
        {
            return;
        }

        Transform rowTransform =
            rowRoot.transform;

        if (rowTransform.parent == null)
            return;

        if (rowTransform.GetSiblingIndex() !=
            rowTransform.parent.childCount - 1)
        {
            rowTransform.SetAsLastSibling();
        }
    }

    public void RefreshAll(bool immediate = false)
    {
        EriSupportManager support =
            EriSupportManager.Instance;

        if (support == null)
        {
            hasSupportData = false;

            if (rowRoot != null &&
                hideWhenSupportUnavailable)
            {
                rowRoot.SetActive(false);
            }

            return;
        }

        hasSupportData = true;

        if (rowRoot != null &&
            !rowRoot.activeSelf)
        {
            rowRoot.SetActive(true);
        }

        OnHealingPointsChanged(
            support.CurrentHealingPoints,
            support.UnlockedCapacity
        );

        OnEriHealthChanged(
            support.EriCurrentHP,
            support.EriMaximumHP,
            immediate
        );
    }

    private void OnHealingPointsChanged(
        int current,
        int capacity)
    {
        healingPoints = Mathf.Max(0, current);
        healingCapacity = Mathf.Max(1, capacity);

        if (healingPointsText != null)
        {
            healingPointsText.text =
                $"{healingPointsLabel}  " +
                $"{healingPoints}/" +
                $"{healingCapacity}";
        }
    }

    private void OnEriHealthChanged(
        int current,
        int maximum)
    {
        OnEriHealthChanged(
            current,
            maximum,
            immediate: false
        );
    }

    private void OnEriHealthChanged(
        int current,
        int maximum,
        bool immediate)
    {
        maximumHP = Mathf.Max(1, maximum);
        currentHP =
            Mathf.Clamp(current, 0, maximumHP);

        targetHealth01 =
            currentHP / (float)maximumHP;

        if (!hasInitializedHealth ||
            immediate ||
            !smoothHealthChanges)
        {
            displayedHealth01 = targetHealth01;
            hasInitializedHealth = true;
        }

        if (hpFill != null)
        {
            ApplyHealthFill(displayedHealth01);
        }

        SetDefeated(currentHP <= 0);
        RefreshHealthText();
    }

    private void OnEriDefeated()
    {
        SetDefeated(true);
        RefreshHealthText();
    }

    private void OnEriRevived()
    {
        RefreshAll(immediate: true);
    }

    private void SetDefeated(bool defeated)
    {
        eriDefeated = defeated;

        if (rowCanvasGroup != null)
        {
            rowCanvasGroup.alpha =
                defeated
                    ? defeatedRowAlpha
                    : 1f;
        }
    }

    private void RefreshHealthText()
    {
        if (nameText != null)
        {
            nameText.text =
                eriName +
                (eriDefeated
                    ? defeatedSuffix
                    : string.Empty);
        }

        if (hpText != null)
        {
            hpText.text =
                showMaximumHealth
                    ? $"{currentHP} / {maximumHP}"
                    : currentHP.ToString();
        }
    }

    private bool ValidateReferences()
    {
        bool valid =
            rowRoot != null &&
            nameText != null &&
            hpText != null &&
            healingPointsText != null &&
            hpFill != null;

        setupStatus =
            valid
                ? "Ready - editable Canvas row assigned"
                : "Use Build Editable Eri Row in the Inspector";

        if (!valid && !hasLoggedMissingReferences)
        {
            hasLoggedMissingReferences = true;

            Debug.LogWarning(
                "EriHealingPointsUI: Canvas references are incomplete. " +
                "Select this component in Edit Mode and click " +
                "Build Editable Eri Row.",
                this
            );
        }

        return valid;
    }

    private void EnsureHealthFillConfiguration()
    {
        if (hpFill == null)
            return;

        // Image.fillAmount is ignored when an Image has no source sprite.
        // Editable rows previously created a plain white Image, which made
        // the HP number update while the bar stayed visually full.
        if (hpFill.sprite == null)
        {
            hpFill.sprite =
                Resources.GetBuiltinResource<Sprite>(
                    "UI/Skin/UISprite.psd"
                );
        }

        if (hpFill.sprite == null)
        {
            if (generatedFillSprite == null)
            {
                generatedFillSprite =
                    Sprite.Create(
                        Texture2D.whiteTexture,
                        new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f),
                        1f
                    );

                generatedFillSprite.name =
                    "Eri HP Runtime Fill Sprite";
                generatedFillSprite.hideFlags =
                    HideFlags.HideAndDontSave;
            }

            hpFill.sprite = generatedFillSprite;
        }

        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod =
            Image.FillMethod.Horizontal;
        hpFill.fillOrigin = 0;
        hpFill.fillClockwise = true;
        hpFill.preserveAspect = false;
    }

    private void ApplyHealthFill(float health01)
    {
        if (hpFill == null)
            return;

        EnsureHealthFillConfiguration();
        hpFill.fillAmount =
            Mathf.Clamp01(health01);
    }
}
