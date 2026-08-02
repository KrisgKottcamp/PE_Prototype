using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a soft additive aura behind the active character whenever an AP-cost
/// skill is affordable. Multiple blurred, expanded silhouettes create bloom
/// around the full sprite rather than a hard outline.
/// </summary>
[DisallowMultipleComponent]
public sealed class APReadyGlow2D : MonoBehaviour
{
    private const string GlowShaderResourceName = "APReadySoftGlow";
    private const int AuraLayerCount = 3;

    private sealed class GlowTarget
    {
        public SpriteRenderer source;
        public Transform root;
        public readonly List<SpriteRenderer> layers =
            new List<SpriteRenderer>(AuraLayerCount);
    }

    [Header("Targets")]
    [SerializeField] private SpriteRenderer[] targetRenderers;

    [Header("Soft Aura")]
    [SerializeField] private Color glowColor =
        new Color(0.56f, 0.92f, 1f, 0.72f);
    [SerializeField, Range(0.02f, 0.5f)] private float glowRadius = 0.18f;
    [SerializeField, Range(0.5f, 8f)] private float glowSoftness = 2.4f;
    [SerializeField, Range(0f, 1f)] private float glowIntensity = 0.26f;
    [SerializeField, Range(0f, 1f)] private float minimumPulseStrength = 0.76f;
    [SerializeField, Range(0f, 1f)] private float maximumPulseStrength = 1f;
    [SerializeField, Min(0.5f)] private float pulseDuration = 2.8f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;
    [SerializeField] private int sortingOrderOffset = -1;

    [Header("Runtime Debug")]
    [SerializeField] private bool ready;
    [SerializeField, Range(0f, 1f)] private float displayedEnvelope;
    [SerializeField] private int glowTargetCount;

    private readonly List<GlowTarget> glowTargets =
        new List<GlowTarget>();
    private Material glowMaterial;

    private void Awake()
    {
        ResolveTargets();
        RebuildGlowVisuals();
    }

    public void Configure(
        SpriteRenderer[] renderers,
        Color newGlowColor,
        float newGlowRadius,
        float newGlowSoftness,
        float newGlowIntensity,
        float newMinimumPulseStrength,
        float newMaximumPulseStrength,
        float newPulseDuration,
        float newFadeSpeed,
        int newSortingOrderOffset)
    {
        glowColor = newGlowColor;
        glowRadius = Mathf.Clamp(newGlowRadius, 0.02f, 0.5f);
        glowSoftness = Mathf.Clamp(newGlowSoftness, 0.5f, 8f);
        glowIntensity = Mathf.Clamp01(newGlowIntensity);
        minimumPulseStrength = Mathf.Clamp01(newMinimumPulseStrength);
        maximumPulseStrength = Mathf.Max(
            minimumPulseStrength,
            Mathf.Clamp01(newMaximumPulseStrength));
        pulseDuration = Mathf.Max(0.5f, newPulseDuration);
        fadeSpeed = Mathf.Max(0.1f, newFadeSpeed);
        sortingOrderOffset = newSortingOrderOffset;

        SpriteRenderer[] cleaned = RemoveNullEntries(renderers);

        if (!SameTargets(targetRenderers, cleaned))
        {
            targetRenderers = cleaned;
            RebuildGlowVisuals();
        }

        UpdateMaterialProperties();
        ApplyCurrentAppearance();
    }

    public void SetReady(
        bool isReady,
        bool immediate = false)
    {
        ready = isReady;

        if (!immediate)
            return;

        displayedEnvelope = ready ? 1f : 0f;
        ApplyCurrentAppearance();
    }

    private void Update()
    {
        displayedEnvelope = Mathf.Lerp(
            displayedEnvelope,
            ready ? 1f : 0f,
            1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));

        ApplyCurrentAppearance();
    }

    private void ApplyCurrentAppearance()
    {
        float cycle =
            Time.unscaledTime *
            Mathf.PI * 2f /
            Mathf.Max(0.5f, pulseDuration);
        float pulse = Mathf.SmoothStep(
            0f,
            1f,
            0.5f + 0.5f * Mathf.Sin(cycle));
        float strength = displayedEnvelope * Mathf.Lerp(
            minimumPulseStrength,
            maximumPulseStrength,
            pulse);

        ApplyGlowAppearance(strength, pulse);
    }

    private void ResolveTargets()
    {
        targetRenderers = RemoveNullEntries(targetRenderers);

        if (targetRenderers.Length == 0)
        {
            targetRenderers = DamageFlash2D.FindLikelyCharacterSprites(
                transform);
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
            SpriteRenderer source = targetRenderers[targetIndex];

            if (source == null)
                continue;

            GameObject rootObject = new GameObject(
                $"APReadyGlow_{source.name}");
            rootObject.transform.SetParent(source.transform, false);

            GlowTarget target = new GlowTarget
            {
                source = source,
                root = rootObject.transform
            };

            for (int layerIndex = 0;
                 layerIndex < AuraLayerCount;
                 layerIndex++)
            {
                GameObject layerObject = new GameObject(
                    $"APReadyGlow_Aura_{layerIndex + 1}");
                layerObject.transform.SetParent(rootObject.transform, false);

                SpriteRenderer layer =
                    layerObject.AddComponent<SpriteRenderer>();
                layer.sharedMaterial = glowMaterial;
                layer.enabled = false;
                target.layers.Add(layer);
            }

            glowTargets.Add(target);
        }

        glowTargetCount = glowTargets.Count;
        ApplyGlowAppearance(0f, 0f);
    }

    private void ApplyGlowAppearance(float strength, float pulse)
    {
        float visibleStrength = strength <= 0.002f ? 0f : strength;
        float breathingRadius = glowRadius * Mathf.Lerp(0.90f, 1.08f, pulse);

        for (int targetIndex = 0;
             targetIndex < glowTargets.Count;
             targetIndex++)
        {
            GlowTarget target = glowTargets[targetIndex];
            SpriteRenderer source = target.source;

            if (source == null)
                continue;

            bool visible =
                visibleStrength > 0f &&
                source.enabled &&
                source.sprite != null &&
                source.gameObject.activeInHierarchy;

            for (int layerIndex = 0;
                 layerIndex < target.layers.Count;
                 layerIndex++)
            {
                SpriteRenderer layer = target.layers[layerIndex];
                float layer01 =
                    (layerIndex + 1f) / target.layers.Count;
                float layerFalloff = Mathf.Lerp(1f, 0.24f, layer01);

                SyncRenderer(source, layer, layerIndex);
                layer.enabled = visible;
                layer.transform.localPosition = Vector3.zero;
                layer.transform.localScale = Vector3.one *
                    (1f + breathingRadius * layer01);

                Color color = Color.white;
                color.a =
                    glowColor.a *
                    glowIntensity *
                    layerFalloff *
                    visibleStrength;
                layer.color = color;
            }
        }
    }

    private void SyncRenderer(
        SpriteRenderer source,
        SpriteRenderer layer,
        int layerIndex)
    {
        layer.sprite = source.sprite;
        layer.flipX = source.flipX;
        layer.flipY = source.flipY;
        layer.drawMode = source.drawMode;
        layer.size = source.size;
        layer.spriteSortPoint = source.spriteSortPoint;
        layer.maskInteraction = source.maskInteraction;
        layer.sortingLayerID = source.sortingLayerID;
        layer.sortingOrder =
            source.sortingOrder +
            sortingOrderOffset -
            layerIndex;
    }

    private bool EnsureMaterial()
    {
        if (glowMaterial != null)
            return true;

        Shader shader = Resources.Load<Shader>(GlowShaderResourceName);

        if (shader == null)
        {
            Debug.LogError(
                "APReadyGlow2D: Could not load APReadySoftGlow from Resources.",
                this);
            return false;
        }

        glowMaterial = new Material(shader)
        {
            name = $"AP Ready Soft Glow ({name})",
            hideFlags = HideFlags.HideAndDontSave
        };

        UpdateMaterialProperties();
        return true;
    }

    private void UpdateMaterialProperties()
    {
        if (glowMaterial == null)
            return;

        glowMaterial.SetColor(
            "_GlowColor",
            new Color(glowColor.r, glowColor.g, glowColor.b, 1f));
        glowMaterial.SetFloat("_GlowSoftness", glowSoftness);
    }

    private void ClearGlowVisuals()
    {
        for (int i = 0; i < glowTargets.Count; i++)
        {
            if (glowTargets[i].root != null)
                Destroy(glowTargets[i].root.gameObject);
        }

        glowTargets.Clear();
        glowTargetCount = 0;
    }

    private static bool SameTargets(
        SpriteRenderer[] first,
        SpriteRenderer[] second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first == null || second == null || first.Length != second.Length)
            return false;

        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
                return false;
        }

        return true;
    }

    private static SpriteRenderer[] RemoveNullEntries(
        SpriteRenderer[] source)
    {
        if (source == null || source.Length == 0)
            return new SpriteRenderer[0];

        List<SpriteRenderer> cleaned = new List<SpriteRenderer>();

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                cleaned.Add(source[i]);
        }

        return cleaned.ToArray();
    }

    private void OnDisable()
    {
        ready = false;
        displayedEnvelope = 0f;
        ApplyGlowAppearance(0f, 0f);
    }

    private void OnDestroy()
    {
        ClearGlowVisuals();

        if (glowMaterial != null)
            Destroy(glowMaterial);
    }
}
