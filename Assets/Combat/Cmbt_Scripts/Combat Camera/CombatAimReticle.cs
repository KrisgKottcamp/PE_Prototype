using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space combat cursor that shares the authoritative AimTracker input.
/// Add it to the Main Camera. If no sprite is assigned, it creates a crisp
/// ring-and-crosshair reticle at runtime.
/// </summary>
[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public sealed class CombatAimReticle : MonoBehaviour
{
    [Header("Aim Source")]
    [Tooltip("Optional explicit combat AimTracker. Normally left empty because the combat pawn is spawned at runtime.")]
    [SerializeField] private AimTracker aimTrackerOverride;

    [SerializeField] private bool automaticallyFindAimTracker = true;

    [SerializeField, Min(0.05f)]
    private float aimTrackerSearchInterval = 0.25f;

    [Header("Reticle Appearance")]
    [Tooltip("Optional custom sprite. Leave empty to use the generated ring-and-crosshair reticle.")]
    [SerializeField] private Sprite reticleSprite;

    [SerializeField] private Vector2 reticleSizePixels =
        new Vector2(36f, 36f);

    [SerializeField] private Color reticleColor = Color.white;

    [SerializeField] private Color outlineColor =
        new Color(0f, 0f, 0f, 0.9f);

    [SerializeField] private Vector2 outlineDistance =
        new Vector2(1.25f, -1.25f);

    [SerializeField] private int canvasSortingOrder = 32000;

    [Tooltip("Optional idle rotation. Leave at 0 for a fixed reticle.")]
    [SerializeField] private float rotationDegreesPerSecond = 0f;

    [Header("Cursor Behavior")]
    [SerializeField] private bool hideSystemCursor = true;
    [SerializeField] private bool hideWhenPointerLeavesGameView = true;
    [SerializeField] private bool restorePreviousCursorStateOnDisable = true;

    [Header("Runtime Debug")]
    [SerializeField] private string boundAimTracker = "None";
    [SerializeField] private bool reticleVisible;
    [SerializeField] private bool suppressed;

    private static CombatAimReticle activeInstance;

    private AimTracker activeAimTracker;
    private GameObject canvasObject;
    private Image reticleImage;
    private Outline reticleOutline;
    private Sprite generatedSprite;
    private Texture2D generatedTexture;
    private float nextAimTrackerSearchTime;
    private bool applicationFocused = true;
    private bool ownsCursorState;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;

    public bool IsSuppressed => suppressed;

    private void OnEnable()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning(
                "CombatAimReticle: Another active reticle already owns the combat cursor. Disabling this duplicate.",
                this
            );

            enabled = false;
            return;
        }

        activeInstance = this;
        applicationFocused = Application.isFocused;
        CaptureCursorState();
        BuildReticleUI();
        nextAimTrackerSearchTime = 0f;
        TryResolveAimTracker(forceSearch: true);
        RefreshVisibilityAndPosition();
    }

    private void LateUpdate()
    {
        TryResolveAimTracker(forceSearch: false);
        RefreshVisibilityAndPosition();

        if (reticleVisible &&
            reticleImage != null &&
            !Mathf.Approximately(rotationDegreesPerSecond, 0f))
        {
            reticleImage.rectTransform.Rotate(
                0f,
                0f,
                rotationDegreesPerSecond * Time.unscaledDeltaTime
            );
        }
    }

    public void SetSuppressed(bool shouldSuppress)
    {
        suppressed = shouldSuppress;
        RefreshVisibilityAndPosition();
    }

    private bool TryResolveAimTracker(bool forceSearch)
    {
        if (activeAimTracker != null &&
            activeAimTracker.isActiveAndEnabled)
        {
            return true;
        }

        activeAimTracker = null;
        boundAimTracker = "None";

        if (aimTrackerOverride != null &&
            aimTrackerOverride.isActiveAndEnabled)
        {
            BindAimTracker(aimTrackerOverride);
            return true;
        }

        if (!automaticallyFindAimTracker)
            return false;

        if (!forceSearch &&
            Time.unscaledTime < nextAimTrackerSearchTime)
        {
            return false;
        }

        nextAimTrackerSearchTime =
            Time.unscaledTime + aimTrackerSearchInterval;

        AimTracker found =
            FindFirstObjectByType<AimTracker>(
                FindObjectsInactive.Exclude
            );

        if (found == null)
            return false;

        BindAimTracker(found);
        return true;
    }

    private void BindAimTracker(AimTracker tracker)
    {
        activeAimTracker = tracker;
        boundAimTracker = tracker.name;
    }

    private void BuildReticleUI()
    {
        if (canvasObject != null)
            return;

        canvasObject = new GameObject(
            "Combat Aim Reticle Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler)
        );

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler =
            canvasObject.GetComponent<CanvasScaler>();

        scaler.uiScaleMode =
            CanvasScaler.ScaleMode.ConstantPixelSize;

        GameObject imageObject = new GameObject(
            "Reticle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline)
        );

        imageObject.transform.SetParent(canvasObject.transform, false);

        reticleImage = imageObject.GetComponent<Image>();
        reticleImage.raycastTarget = false;
        reticleImage.preserveAspect = true;
        reticleImage.color = reticleColor;
        reticleImage.sprite =
            reticleSprite != null
                ? reticleSprite
                : CreateGeneratedReticleSprite();

        RectTransform rect = reticleImage.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(
            Mathf.Max(1f, reticleSizePixels.x),
            Mathf.Max(1f, reticleSizePixels.y)
        );

        reticleOutline = imageObject.GetComponent<Outline>();
        reticleOutline.effectColor = outlineColor;
        reticleOutline.effectDistance = outlineDistance;
        reticleOutline.useGraphicAlpha = true;
    }

    private void RefreshVisibilityAndPosition()
    {
        if (reticleImage == null)
            return;

        bool pointerAllowed =
            activeAimTracker != null &&
            (!hideWhenPointerLeavesGameView ||
             activeAimTracker.IsCursorInsideGameView);

        reticleVisible =
            applicationFocused &&
            !suppressed &&
            pointerAllowed;

        reticleImage.enabled = reticleVisible;

        if (reticleOutline != null)
            reticleOutline.enabled = reticleVisible;

        if (reticleVisible)
        {
            reticleImage.rectTransform.position =
                activeAimTracker.CursorScreenPosition;
        }

        ApplySystemCursorState(reticleVisible);
    }

    private void CaptureCursorState()
    {
        if (ownsCursorState)
            return;

        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        ownsCursorState = true;
    }

    private void ApplySystemCursorState(bool customReticleIsVisible)
    {
        if (!hideSystemCursor)
            return;

        if (customReticleIsVisible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void RestoreCursorState()
    {
        if (!ownsCursorState)
            return;

        if (restorePreviousCursorStateOnDisable)
        {
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        ownsCursorState = false;
    }

    private Sprite CreateGeneratedReticleSprite()
    {
        const int textureSize = 128;
        const float ringRadius = 25f;
        const float ringHalfThickness = 2f;
        const float tickInner = 33f;
        const float tickOuter = 47f;
        const float tickHalfThickness = 1.65f;
        const float centerDotRadius = 2.25f;

        generatedTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false
        );

        generatedTexture.name = "Generated Combat Aim Reticle";
        generatedTexture.filterMode = FilterMode.Bilinear;
        generatedTexture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[textureSize * textureSize];
        float center = (textureSize - 1) * 0.5f;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float radius = Mathf.Sqrt((dx * dx) + (dy * dy));

                float ringAlpha = Mathf.Clamp01(
                    ringHalfThickness + 0.75f -
                    Mathf.Abs(radius - ringRadius)
                );

                float horizontalTick =
                    Mathf.Abs(dy) <= tickHalfThickness &&
                    Mathf.Abs(dx) >= tickInner &&
                    Mathf.Abs(dx) <= tickOuter
                        ? 1f
                        : 0f;

                float verticalTick =
                    Mathf.Abs(dx) <= tickHalfThickness &&
                    Mathf.Abs(dy) >= tickInner &&
                    Mathf.Abs(dy) <= tickOuter
                        ? 1f
                        : 0f;

                float centerDot = Mathf.Clamp01(
                    centerDotRadius + 0.75f - radius
                );

                float alpha = Mathf.Max(
                    ringAlpha,
                    Mathf.Max(
                        horizontalTick,
                        Mathf.Max(verticalTick, centerDot)
                    )
                );

                pixels[(y * textureSize) + x] =
                    new Color(1f, 1f, 1f, alpha);
            }
        }

        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(false, true);

        generatedSprite = Sprite.Create(
            generatedTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f
        );

        generatedSprite.name = "Generated Combat Aim Reticle";
        return generatedSprite;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationFocused = hasFocus;
        RefreshVisibilityAndPosition();
    }

    private void OnDisable()
    {
        reticleVisible = false;
        RestoreCursorState();

        if (canvasObject != null)
        {
            Destroy(canvasObject);
            canvasObject = null;
        }

        if (generatedSprite != null)
        {
            Destroy(generatedSprite);
            generatedSprite = null;
        }

        if (generatedTexture != null)
        {
            Destroy(generatedTexture);
            generatedTexture = null;
        }

        reticleImage = null;
        reticleOutline = null;
        activeAimTracker = null;
        boundAimTracker = "None";

        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnValidate()
    {
        aimTrackerSearchInterval = Mathf.Max(
            0.05f,
            aimTrackerSearchInterval
        );

        reticleSizePixels.x = Mathf.Max(1f, reticleSizePixels.x);
        reticleSizePixels.y = Mathf.Max(1f, reticleSizePixels.y);
    }
}
