using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a softly fading colored halo behind the active combat sprite by
/// drawing several solid-color copies just outside its silhouette.
/// PlayerAPBarUI supplies the breathing intensity; this component is visual only.
/// </summary>
[DisallowMultipleComponent]
public sealed class RotationPressureGlow2D : MonoBehaviour
{
    private const string ColorShaderResourceName =
        "HealingFlashGreen";

    private sealed class GlowTarget
    {
        public SpriteRenderer source;
        public Transform root;
        public SpriteRenderer center;
        public readonly List<SpriteRenderer> outline =
            new List<SpriteRenderer>(8);
    }

    [Header("Targets")]
    [SerializeField] private SpriteRenderer[] targetRenderers;

    [Header("Appearance")]
    [SerializeField] private Color glowColor =
        new Color(1f, 0.78f, 0.12f, 0.82f);
    [SerializeField, Min(0.005f)] private float outlineDistance = 0.045f;
    [SerializeField, Range(0f, 0.25f)] private float expansion = 0.055f;
    [SerializeField, Range(0f, 1f)] private float outlineAlpha = 0.72f;
    [SerializeField, Range(0f, 1f)] private float centerAlpha = 0.16f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 7f;
    [SerializeField] private int sortingOrderOffset = -1;

    [Header("Runtime Debug")]
    [SerializeField, Range(0f, 1f)] private float targetIntensity;
    [SerializeField, Range(0f, 1f)] private float displayedIntensity;
    [SerializeField] private int glowTargetCount;

    private readonly List<GlowTarget> glowTargets =
        new List<GlowTarget>();

    private Material glowMaterial;

    private static readonly Vector2[] OutlineDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(-1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
        new Vector2(0.7071f, 0.7071f),
        new Vector2(-0.7071f, 0.7071f),
        new Vector2(0.7071f, -0.7071f),
        new Vector2(-0.7071f, -0.7071f)
    };

    private void Awake()
    {
        ResolveTargets();
        RebuildGlowVisuals();
    }

    public void Configure(
        SpriteRenderer[] renderers,
        Color newGlowColor,
        float newOutlineDistance,
        float newExpansion,
        float newOutlineAlpha,
        float newCenterAlpha,
        float newFadeSpeed,
        int newSortingOrderOffset)
    {
        glowColor = newGlowColor;
        outlineDistance =
            Mathf.Max(0.005f, newOutlineDistance);
        expansion =
            Mathf.Clamp(newExpansion, 0f, 0.25f);
        outlineAlpha =
            Mathf.Clamp01(newOutlineAlpha);
        centerAlpha =
            Mathf.Clamp01(newCenterAlpha);
        fadeSpeed =
            Mathf.Max(0.1f, newFadeSpeed);
        sortingOrderOffset =
            newSortingOrderOffset;

        SpriteRenderer[] cleaned =
            RemoveNullEntries(renderers);

        if (!SameTargets(targetRenderers, cleaned))
        {
            targetRenderers = cleaned;
            RebuildGlowVisuals();
        }

        UpdateMaterialColor();
        ApplyGlowAppearance();
    }

    public void SetIntensity(
        float intensity,
        bool immediate = false)
    {
        targetIntensity =
            Mathf.Clamp01(intensity);

        if (!immediate)
            return;

        displayedIntensity = targetIntensity;
        ApplyGlowAppearance();
    }

    private void Update()
    {
        displayedIntensity =
            Mathf.Lerp(
                displayedIntensity,
                targetIntensity,
                1f - Mathf.Exp(
                    -fadeSpeed *
                    Time.unscaledDeltaTime
                )
            );

        ApplyGlowAppearance();
    }

    private void ResolveTargets()
    {
        targetRenderers =
            RemoveNullEntries(targetRenderers);

        if (targetRenderers.Length == 0)
        {
            targetRenderers =
                DamageFlash2D.
                    FindLikelyCharacterSprites(
                        transform
                    );
        }
    }

    private void RebuildGlowVisuals()
    {
        ClearGlowVisuals();

        if (!EnsureMaterial())
            return;

        for (int targetIndex = 0;
             targetIndex < targetRenderers.Length;
             targetIndex++)
        {
            SpriteRenderer source =
                targetRenderers[targetIndex];

            if (source == null)
                continue;

            GameObject rootObject =
                new GameObject(
                    $"RotationPressureGlow_{source.name}"
                );

            rootObject.transform.SetParent(
                source.transform,
                false
            );

            GlowTarget target =
                new GlowTarget
                {
                    source = source,
                    root = rootObject.transform,
                    center = CreateGlowRenderer(
                        "RotationPressureGlow_Center",
                        rootObject.transform
                    )
                };

            for (int i = 0;
                 i < OutlineDirections.Length;
                 i++)
            {
                SpriteRenderer outlineRenderer =
                    CreateGlowRenderer(
                        $"RotationPressureGlow_Outline_{i + 1}",
                        rootObject.transform
                    );

                target.outline.Add(
                    outlineRenderer
                );
            }

            glowTargets.Add(target);
        }

        glowTargetCount = glowTargets.Count;
        ApplyGlowAppearance();
    }

    private SpriteRenderer CreateGlowRenderer(
        string objectName,
        Transform parent)
    {
        GameObject child =
            new GameObject(objectName);

        child.transform.SetParent(
            parent,
            false
        );

        SpriteRenderer renderer =
            child.AddComponent<SpriteRenderer>();

        renderer.sharedMaterial = glowMaterial;
        renderer.enabled = false;
        return renderer;
    }

    private void ApplyGlowAppearance()
    {
        float visibleIntensity =
            displayedIntensity <= 0.002f
                ? 0f
                : displayedIntensity;

        for (int targetIndex = 0;
             targetIndex < glowTargets.Count;
             targetIndex++)
        {
            GlowTarget target =
                glowTargets[targetIndex];
            SpriteRenderer source = target.source;

            if (source == null)
                continue;

            bool visible =
                visibleIntensity > 0f &&
                source.enabled &&
                source.sprite != null &&
                source.gameObject.activeInHierarchy;

            SyncRenderer(
                source,
                target.center
            );

            target.center.enabled = visible;
            target.center.transform.localPosition =
                Vector3.zero;
            target.center.transform.localScale =
                Vector3.one * (1f + expansion);

            Color centerColor = Color.white;
            centerColor.a =
                glowColor.a *
                centerAlpha *
                visibleIntensity;
            target.center.color = centerColor;

            for (int i = 0;
                 i < target.outline.Count;
                 i++)
            {
                SpriteRenderer outlineRenderer =
                    target.outline[i];

                SyncRenderer(
                    source,
                    outlineRenderer
                );

                outlineRenderer.enabled = visible;
                outlineRenderer.transform.localPosition =
                    (Vector3)(
                        OutlineDirections[i] *
                        outlineDistance
                    );
                outlineRenderer.transform.localScale =
                    Vector3.one *
                    (1f + expansion * 0.35f);

                Color outlineColor = Color.white;
                outlineColor.a =
                    glowColor.a *
                    outlineAlpha *
                    visibleIntensity;
                outlineRenderer.color =
                    outlineColor;
            }
        }
    }

    private void SyncRenderer(
        SpriteRenderer source,
        SpriteRenderer glow)
    {
        glow.sprite = source.sprite;
        glow.flipX = source.flipX;
        glow.flipY = source.flipY;
        glow.drawMode = source.drawMode;
        glow.size = source.size;
        glow.spriteSortPoint =
            source.spriteSortPoint;
        glow.maskInteraction =
            source.maskInteraction;
        glow.sortingLayerID =
            source.sortingLayerID;
        glow.sortingOrder =
            source.sortingOrder +
            sortingOrderOffset;
    }

    private bool EnsureMaterial()
    {
        if (glowMaterial != null)
            return true;

        Shader shader =
            Resources.Load<Shader>(
                ColorShaderResourceName
            );

        if (shader == null)
        {
            Debug.LogError(
                "RotationPressureGlow2D: Could not load the existing " +
                "solid-color sprite shader.",
                this
            );
            return false;
        }

        glowMaterial =
            new Material(shader)
            {
                name =
                    $"Rotation Pressure Glow ({name})",
                hideFlags =
                    HideFlags.HideAndDontSave
            };

        UpdateMaterialColor();
        return true;
    }

    private void UpdateMaterialColor()
    {
        if (glowMaterial == null)
            return;

        glowMaterial.SetColor(
            "_FlashColor",
            new Color(
                glowColor.r,
                glowColor.g,
                glowColor.b,
                1f
            )
        );
    }

    private void ClearGlowVisuals()
    {
        for (int i = 0;
             i < glowTargets.Count;
             i++)
        {
            GlowTarget target = glowTargets[i];

            if (target.root != null)
                Destroy(target.root.gameObject);
        }

        glowTargets.Clear();
        glowTargetCount = 0;
    }

    private static bool SameTargets(
        SpriteRenderer[] a,
        SpriteRenderer[] b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a == null || b == null ||
            a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static SpriteRenderer[] RemoveNullEntries(
        SpriteRenderer[] source)
    {
        if (source == null || source.Length == 0)
            return new SpriteRenderer[0];

        List<SpriteRenderer> cleaned =
            new List<SpriteRenderer>();

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                cleaned.Add(source[i]);
        }

        return cleaned.ToArray();
    }

    private void OnDisable()
    {
        targetIntensity = 0f;
        displayedIntensity = 0f;
        ApplyGlowAppearance();
    }

    private void OnDestroy()
    {
        ClearGlowVisuals();

        if (glowMaterial != null)
            Destroy(glowMaterial);
    }
}
