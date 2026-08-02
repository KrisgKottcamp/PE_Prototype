using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared positive-health feedback for the combat pawn. Current healing skills
/// call this component, and future healing sources can reuse PlayHealingFeedback.
/// It provides a green silhouette flash, a light camera impulse, tiny rising
/// plus signs, and short emerald aura streaks without requiring a VFX prefab.
/// </summary>
[DisallowMultipleComponent]
public sealed class HealingFeedback2D : MonoBehaviour
{
    private const string FlashShaderResourceName =
        "HealingFlashGreen";

    [Header("Green Sprite Flash")]
    [SerializeField] private bool enableGreenFlash = true;
    [SerializeField, Min(0.01f)] private float flashDuration = 0.08f;
    [SerializeField] private Color flashColor =
        new Color(0.12f, 1f, 0.32f, 1f);
    [SerializeField] private SpriteRenderer[] targetRenderers;
    [SerializeField] private Shader flashShader;

    [Header("Healing Camera Shake")]
    [SerializeField] private CameraShakeSettings healingCameraShake =
        CameraShakeSettings.Create(0.10f, 0.08f);

    [Header("Tiny Plus Burst")]
    [SerializeField, Min(0)] private int plusCount = 9;
    [SerializeField] private Color plusColor =
        new Color(0.15f, 1f, 0.38f, 0.95f);
    [SerializeField, Min(0f)] private float plusSpawnRadius = 0.62f;
    [SerializeField] private Vector2 plusSizeRange =
        new Vector2(0.08f, 0.15f);
    [SerializeField] private Vector2 plusLifetimeRange =
        new Vector2(0.26f, 0.38f);
    [SerializeField] private Vector2 plusRiseSpeedRange =
        new Vector2(0.65f, 1.15f);

    [Header("Upward Emerald Aura")]
    [SerializeField, Min(0)] private int auraStreakCount = 10;
    [SerializeField] private Color auraColor =
        new Color(0.08f, 1f, 0.30f, 0.58f);
    [SerializeField, Min(0f)] private float auraSpawnRadius = 0.55f;
    [SerializeField] private Vector2 auraLifetimeRange =
        new Vector2(0.22f, 0.34f);
    [SerializeField] private Vector2 auraRiseSpeedRange =
        new Vector2(1.3f, 2.2f);
    [SerializeField] private Vector2 auraHeightRange =
        new Vector2(0.18f, 0.40f);

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Foreground";
    [SerializeField] private int sortingOrder = 140;
    [SerializeField] private float localVerticalOffset = -0.18f;

    [Header("Runtime Debug")]
    [SerializeField] private string debugLastPlay = "None";
    [SerializeField] private int debugActiveBurstVisuals;

    private sealed class BurstVisual
    {
        public Transform root;
        public SpriteRenderer[] renderers;
        public Color[] colors;
        public Vector2 velocity;
        public float lifetime;
        public float age;
        public float spinDegreesPerSecond;
        public bool isAuraStreak;
    }

    private Material flashMaterial;
    private Material[][] originalMaterials;
    private bool flashApplied;
    private Coroutine flashRoutine;
    private Coroutine burstRoutine;
    private Transform burstRoot;
    private Sprite runtimeUnitSprite;
    private Texture2D runtimeUnitTexture;
    private readonly List<BurstVisual> burstVisuals =
        new List<BurstVisual>(32);

    private void Awake()
    {
        ResolveTargets();
    }

    public void ConfigureTargets(SpriteRenderer[] renderers)
    {
        RestoreOriginalMaterials();
        targetRenderers = RemoveNullEntries(renderers);
        originalMaterials = null;
    }

    public void PlayHealingFeedback()
    {
        if (!isActiveAndEnabled)
            return;

        ResolveTargets();

        if (enableGreenFlash)
            PlayGreenFlash();

        CombatCameraShake.Request(
            healingCameraShake,
            transform.position,
            Vector2.up
        );

        if (burstRoutine != null)
            StopCoroutine(burstRoutine);

        ClearBurstVisuals();
        burstRoutine = StartCoroutine(HealingBurstRoutine());
        debugLastPlay = $"Played at {Time.unscaledTime:0.00}";
    }

    private void ResolveTargets()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers =
                DamageFlash2D.FindLikelyCharacterSprites(transform);
        }

        targetRenderers = RemoveNullEntries(targetRenderers);
    }

    private void PlayGreenFlash()
    {
        // Healing can occur immediately after damage (especially Eri's
        // automatic self-heal). Restore any white flash before capturing the
        // sprite's original material for the green flash.
        DamageFlash2D damageFlash = GetComponent<DamageFlash2D>();
        damageFlash?.CancelFlashAndRestore();

        if (!EnsureFlashReady())
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        if (!flashApplied)
            CaptureAndApplyFlashMaterials();

        flashRoutine = StartCoroutine(GreenFlashRoutine());
    }

    /// <summary>
    /// Ends only the green sprite flash. Healing particles and camera feedback
    /// continue normally. DamageFlash2D calls this before applying white.
    /// </summary>
    public void CancelGreenFlashAndRestore()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        RestoreOriginalMaterials();
    }

    private bool EnsureFlashReady()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            return false;

        if (flashMaterial != null)
        {
            flashMaterial.SetColor("_FlashColor", flashColor);
            return true;
        }

        if (flashShader == null)
            flashShader = Resources.Load<Shader>(FlashShaderResourceName);

        if (flashShader == null)
        {
            Debug.LogError(
                "HealingFeedback2D: Could not load HealingFlashGreen shader from Resources.",
                this
            );

            return false;
        }

        flashMaterial = new Material(flashShader)
        {
            name = $"Healing Flash Green ({name})",
            hideFlags = HideFlags.HideAndDontSave
        };

        flashMaterial.SetColor("_FlashColor", flashColor);
        return true;
    }

    private void CaptureAndApplyFlashMaterials()
    {
        originalMaterials = new Material[targetRenderers.Length][];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            SpriteRenderer renderer = targetRenderers[i];

            if (renderer == null)
                continue;

            originalMaterials[i] = renderer.sharedMaterials;
            renderer.sharedMaterial = flashMaterial;
        }

        flashApplied = true;
    }

    private IEnumerator GreenFlashRoutine()
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0.01f, flashDuration)
        );

        RestoreOriginalMaterials();
        flashRoutine = null;
    }

    private IEnumerator HealingBurstRoutine()
    {
        EnsureRuntimeSprite();

        burstRoot = new GameObject("Healing Feedback Burst").transform;
        burstRoot.SetParent(transform, false);
        burstRoot.localPosition =
            new Vector3(0f, localVerticalOffset, 0f);

        for (int i = 0; i < plusCount; i++)
            CreatePlusVisual(i);

        for (int i = 0; i < auraStreakCount; i++)
            CreateAuraStreak(i);

        while (burstVisuals.Count > 0)
        {
            float delta = Mathf.Max(0f, Time.unscaledDeltaTime);

            for (int i = burstVisuals.Count - 1; i >= 0; i--)
            {
                BurstVisual visual = burstVisuals[i];
                visual.age += delta;

                if (visual.root == null || visual.age >= visual.lifetime)
                {
                    if (visual.root != null)
                        Destroy(visual.root.gameObject);

                    burstVisuals.RemoveAt(i);
                    continue;
                }

                float normalized = Mathf.Clamp01(
                    visual.age / Mathf.Max(0.01f, visual.lifetime)
                );

                visual.root.localPosition +=
                    (Vector3)(visual.velocity * delta);

                visual.root.Rotate(
                    0f,
                    0f,
                    visual.spinDegreesPerSecond * delta
                );

                float fade = GetBurstFade(normalized);
                float scale = visual.isAuraStreak
                    ? Mathf.Lerp(0.72f, 1.18f, normalized)
                    : Mathf.Lerp(0.62f, 1.05f, Mathf.Clamp01(normalized * 4f)) *
                      Mathf.Lerp(1f, 0.72f, normalized);

                visual.root.localScale = new Vector3(
                    scale,
                    visual.isAuraStreak
                        ? Mathf.Lerp(0.75f, 1.35f, normalized)
                        : scale,
                    1f
                );

                for (int rendererIndex = 0;
                     rendererIndex < visual.renderers.Length;
                     rendererIndex++)
                {
                    SpriteRenderer renderer =
                        visual.renderers[rendererIndex];

                    if (renderer == null)
                        continue;

                    Color color = visual.colors[rendererIndex];
                    color.a *= fade;
                    renderer.color = color;
                }
            }

            debugActiveBurstVisuals = burstVisuals.Count;
            yield return null;
        }

        if (burstRoot != null)
            Destroy(burstRoot.gameObject);

        burstRoot = null;
        burstRoutine = null;
        debugActiveBurstVisuals = 0;
    }

    private void CreatePlusVisual(int index)
    {
        Transform root = new GameObject($"Healing Plus {index + 1}").transform;
        root.SetParent(burstRoot, false);

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt(Random.value) * plusSpawnRadius;
        root.localPosition = new Vector3(
            Mathf.Cos(angle) * radius,
            Mathf.Sin(angle) * radius * 0.58f,
            0f
        );
        root.localRotation = Quaternion.Euler(
            0f,
            0f,
            Random.Range(-16f, 16f)
        );

        float minimumSize = Mathf.Min(plusSizeRange.x, plusSizeRange.y);
        float maximumSize = Mathf.Max(plusSizeRange.x, plusSizeRange.y);
        float size = Mathf.Max(0.02f, Random.Range(minimumSize, maximumSize));
        float thickness = size * 0.30f;

        Color glowColor = plusColor;
        glowColor.a *= 0.20f;

        SpriteRenderer[] renderers = new SpriteRenderer[4];
        Color[] colors = new Color[4];

        renderers[0] = CreateQuad(
            root,
            "Glow Horizontal",
            new Vector2(size * 1.65f, thickness * 1.75f),
            glowColor,
            sortingOrder - 1
        );
        colors[0] = glowColor;

        renderers[1] = CreateQuad(
            root,
            "Glow Vertical",
            new Vector2(thickness * 1.75f, size * 1.65f),
            glowColor,
            sortingOrder - 1
        );
        colors[1] = glowColor;

        renderers[2] = CreateQuad(
            root,
            "Core Horizontal",
            new Vector2(size, thickness),
            plusColor,
            sortingOrder
        );
        colors[2] = plusColor;

        renderers[3] = CreateQuad(
            root,
            "Core Vertical",
            new Vector2(thickness, size),
            plusColor,
            sortingOrder
        );
        colors[3] = plusColor;

        burstVisuals.Add(new BurstVisual
        {
            root = root,
            renderers = renderers,
            colors = colors,
            velocity = new Vector2(
                Random.Range(-0.16f, 0.16f),
                Random.Range(
                    Mathf.Min(plusRiseSpeedRange.x, plusRiseSpeedRange.y),
                    Mathf.Max(plusRiseSpeedRange.x, plusRiseSpeedRange.y)
                )
            ),
            lifetime = Random.Range(
                Mathf.Min(plusLifetimeRange.x, plusLifetimeRange.y),
                Mathf.Max(plusLifetimeRange.x, plusLifetimeRange.y)
            ),
            spinDegreesPerSecond = Random.Range(-36f, 36f),
            isAuraStreak = false
        });
    }

    private void CreateAuraStreak(int index)
    {
        Transform root =
            new GameObject($"Healing Aura Streak {index + 1}").transform;
        root.SetParent(burstRoot, false);

        root.localPosition = new Vector3(
            Random.Range(-auraSpawnRadius, auraSpawnRadius),
            Random.Range(-0.20f, 0.16f),
            0f
        );

        float height = Random.Range(
            Mathf.Min(auraHeightRange.x, auraHeightRange.y),
            Mathf.Max(auraHeightRange.x, auraHeightRange.y)
        );
        float width = Random.Range(0.018f, 0.038f);

        Color glowColor = auraColor;
        glowColor.a *= 0.24f;

        SpriteRenderer[] renderers = new SpriteRenderer[2];
        Color[] colors = new Color[2];

        renderers[0] = CreateQuad(
            root,
            "Aura Glow",
            new Vector2(width * 3.2f, height * 1.15f),
            glowColor,
            sortingOrder - 2
        );
        colors[0] = glowColor;

        renderers[1] = CreateQuad(
            root,
            "Aura Core",
            new Vector2(width, height),
            auraColor,
            sortingOrder - 1
        );
        colors[1] = auraColor;

        burstVisuals.Add(new BurstVisual
        {
            root = root,
            renderers = renderers,
            colors = colors,
            velocity = new Vector2(
                Random.Range(-0.05f, 0.05f),
                Random.Range(
                    Mathf.Min(auraRiseSpeedRange.x, auraRiseSpeedRange.y),
                    Mathf.Max(auraRiseSpeedRange.x, auraRiseSpeedRange.y)
                )
            ),
            lifetime = Random.Range(
                Mathf.Min(auraLifetimeRange.x, auraLifetimeRange.y),
                Mathf.Max(auraLifetimeRange.x, auraLifetimeRange.y)
            ),
            spinDegreesPerSecond = 0f,
            isAuraStreak = true
        });
    }

    private SpriteRenderer CreateQuad(
        Transform parent,
        string objectName,
        Vector2 size,
        Color color,
        int order)
    {
        GameObject quad = new GameObject(objectName);
        quad.transform.SetParent(parent, false);
        quad.transform.localScale = new Vector3(
            Mathf.Max(0.001f, size.x),
            Mathf.Max(0.001f, size.y),
            1f
        );

        SpriteRenderer renderer = quad.AddComponent<SpriteRenderer>();
        renderer.sprite = runtimeUnitSprite;
        renderer.color = color;
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = order;
        return renderer;
    }

    private void EnsureRuntimeSprite()
    {
        if (runtimeUnitSprite != null)
            return;

        runtimeUnitTexture = new Texture2D(
            1,
            1,
            TextureFormat.RGBA32,
            false
        )
        {
            name = "Healing Feedback Unit Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        runtimeUnitTexture.SetPixel(0, 0, Color.white);
        runtimeUnitTexture.Apply(false, true);

        runtimeUnitSprite = Sprite.Create(
            runtimeUnitTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f
        );

        runtimeUnitSprite.name = "Healing Feedback Unit Sprite";
        runtimeUnitSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private static float GetBurstFade(float normalized)
    {
        float fadeIn = Mathf.Clamp01(normalized / 0.12f);
        float fadeOut = 1f - Mathf.Clamp01(
            (normalized - 0.48f) / 0.52f
        );

        return Mathf.SmoothStep(0f, 1f, fadeIn) *
               Mathf.SmoothStep(0f, 1f, fadeOut);
    }

    private void RestoreOriginalMaterials()
    {
        if (!flashApplied)
            return;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] == null ||
                originalMaterials == null ||
                i >= originalMaterials.Length ||
                originalMaterials[i] == null)
            {
                continue;
            }

            targetRenderers[i].sharedMaterials = originalMaterials[i];
        }

        originalMaterials = null;
        flashApplied = false;
    }

    private void ClearBurstVisuals()
    {
        for (int i = burstVisuals.Count - 1; i >= 0; i--)
        {
            if (burstVisuals[i].root != null)
                Destroy(burstVisuals[i].root.gameObject);
        }

        burstVisuals.Clear();

        if (burstRoot != null)
            Destroy(burstRoot.gameObject);

        burstRoot = null;
        debugActiveBurstVisuals = 0;
    }

    private static SpriteRenderer[] RemoveNullEntries(
        SpriteRenderer[] source)
    {
        if (source == null || source.Length == 0)
            return new SpriteRenderer[0];

        List<SpriteRenderer> valid = new List<SpriteRenderer>();

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                valid.Add(source[i]);
        }

        return valid.ToArray();
    }

    private void OnDisable()
    {
        CancelGreenFlashAndRestore();

        if (burstRoutine != null)
        {
            StopCoroutine(burstRoutine);
            burstRoutine = null;
        }

        ClearBurstVisuals();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();
        ClearBurstVisuals();

        if (flashMaterial != null)
            Destroy(flashMaterial);

        if (runtimeUnitSprite != null)
            Destroy(runtimeUnitSprite);

        if (runtimeUnitTexture != null)
            Destroy(runtimeUnitTexture);
    }
}
