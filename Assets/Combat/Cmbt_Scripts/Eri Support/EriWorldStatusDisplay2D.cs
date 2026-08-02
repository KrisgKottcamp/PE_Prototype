using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space status arcs parented directly to Eri's combat body.
/// The inner arc shows persistent HP. The outer arc uses one segment per
/// unlocked healing point so both remaining charges and capacity are readable.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriWorldStatusDisplay2D : MonoBehaviour
{
    private readonly List<SpriteRenderer> healthSegments =
        new List<SpriteRenderer>(24);
    private readonly List<SpriteRenderer> healingPointSegments =
        new List<SpriteRenderer>(12);

    private Transform visualRoot;
    private SpriteRenderer sourceRenderer;
    private Texture2D solidTexture;
    private Sprite solidSprite;

    private Vector2 localOffset;
    private float healthArcRadius;
    private int healthSegmentCount;
    private float healthArcThickness;
    private Color healthFillColor;
    private Color healthEmptyColor;
    private float healingArcRadius;
    private float healingArcThickness;
    private Color healingFillColor;
    private Color healingEmptyColor;
    private int sortingOrderOffset;
    private bool presentationEnabled = true;

    private int currentHP;
    private int maximumHP = 1;
    private int currentHealingPoints;
    private int healingCapacity = 1;

    private const float ArcStartDegrees = 205f;
    private const float ArcEndDegrees = 335f;
    private const float SegmentCoverage = 0.72f;

    public void Configure(
        SpriteRenderer[] eriRenderers,
        Vector2 newLocalOffset,
        float newHealthArcRadius,
        int newHealthSegmentCount,
        float newHealthArcThickness,
        Color newHealthFillColor,
        Color newHealthEmptyColor,
        float newHealingArcRadius,
        float newHealingArcThickness,
        Color newHealingFillColor,
        Color newHealingEmptyColor,
        int newSortingOrderOffset)
    {
        sourceRenderer = FirstValid(eriRenderers);
        localOffset = newLocalOffset;
        healthArcRadius = Mathf.Clamp(newHealthArcRadius, 0.08f, 0.80f);
        int nextHealthSegmentCount = Mathf.Clamp(
            newHealthSegmentCount,
            8,
            32);
        bool rebuildHealth =
            nextHealthSegmentCount != healthSegmentCount;
        healthSegmentCount = nextHealthSegmentCount;
        healthArcThickness = Mathf.Clamp(
            newHealthArcThickness,
            0.008f,
            0.08f);
        healthFillColor = newHealthFillColor;
        healthEmptyColor = newHealthEmptyColor;
        healingArcRadius = Mathf.Max(
            healthArcRadius + 0.03f,
            Mathf.Clamp(newHealingArcRadius, 0.10f, 0.90f));
        healingArcThickness = Mathf.Clamp(
            newHealingArcThickness,
            0.008f,
            0.10f);
        healingFillColor = newHealingFillColor;
        healingEmptyColor = newHealingEmptyColor;
        sortingOrderOffset = newSortingOrderOffset;

        EnsureVisualResources();

        if (rebuildHealth || healthSegments.Count != healthSegmentCount)
            RebuildHealthSegments();

        LayoutHealthSegments();
        LayoutHealingPointSegments();
        ApplySorting();
        RefreshState();
    }

    public void SetPresentationEnabled(bool enabled)
    {
        presentationEnabled = enabled;
        RefreshRootVisibility();
    }

    private void Awake()
    {
        EnsureVisualResources();
    }

    private void Update()
    {
        RefreshState();
    }

    private void RefreshState()
    {
        if (!presentationEnabled)
            return;

        EriSupportManager support = EriSupportManager.Instance;

        if (visualRoot == null)
            EnsureVisualResources();

        if (support == null)
        {
            visualRoot.gameObject.SetActive(false);
            return;
        }

        if (support.IsEriDefeated)
        {
            visualRoot.gameObject.SetActive(false);
            return;
        }

        visualRoot.gameObject.SetActive(true);
        currentHP = support.EriCurrentHP;
        maximumHP = Mathf.Max(1, support.EriMaximumHP);
        currentHealingPoints = support.CurrentHealingPoints;
        int nextCapacity = Mathf.Max(1, support.UnlockedCapacity);

        if (nextCapacity != healingCapacity ||
            healingPointSegments.Count != nextCapacity)
        {
            healingCapacity = nextCapacity;
            RebuildHealingPointSegments();
        }

        RefreshColors();
    }

    private void EnsureVisualResources()
    {
        if (visualRoot == null)
        {
            GameObject rootObject = new GameObject("EriWorldStatus");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;
        }

        visualRoot.localPosition = localOffset;

        if (solidSprite != null)
            return;

        solidTexture = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            true);
        solidTexture.SetPixels(new[]
        {
            Color.white, Color.white,
            Color.white, Color.white
        });
        solidTexture.Apply(false, true);
        solidTexture.filterMode = FilterMode.Bilinear;
        solidTexture.hideFlags = HideFlags.HideAndDontSave;

        solidSprite = Sprite.Create(
            solidTexture,
            new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f),
            2f);
        solidSprite.name = "Eri World Status Segment";
        solidSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private void RebuildHealthSegments()
    {
        DestroyRenderers(healthSegments);

        for (int i = 0; i < healthSegmentCount; i++)
        {
            healthSegments.Add(CreateSegment(
                $"EriWorldStatus_HP_{i + 1}"));
        }

        LayoutHealthSegments();
        ApplySorting();
    }

    private void RebuildHealingPointSegments()
    {
        DestroyRenderers(healingPointSegments);

        for (int i = 0; i < healingCapacity; i++)
        {
            healingPointSegments.Add(CreateSegment(
                $"EriWorldStatus_HealingPoint_{i + 1}"));
        }

        LayoutHealingPointSegments();
        ApplySorting();
    }

    private SpriteRenderer CreateSegment(string objectName)
    {
        GameObject segmentObject = new GameObject(objectName);
        segmentObject.transform.SetParent(visualRoot, false);
        SpriteRenderer renderer =
            segmentObject.AddComponent<SpriteRenderer>();
        renderer.sprite = solidSprite;
        return renderer;
    }

    private void LayoutHealthSegments()
    {
        LayoutArc(
            healthSegments,
            healthArcRadius,
            healthArcThickness);
    }

    private void LayoutHealingPointSegments()
    {
        LayoutArc(
            healingPointSegments,
            healingArcRadius,
            healingArcThickness);
    }

    private void LayoutArc(
        List<SpriteRenderer> segments,
        float radius,
        float thickness)
    {
        if (visualRoot == null || segments.Count == 0)
            return;

        visualRoot.localPosition = localOffset;
        float spanRadians =
            (ArcEndDegrees - ArcStartDegrees) * Mathf.Deg2Rad;
        float segmentLength =
            radius * spanRadians /
            segments.Count * SegmentCoverage;

        for (int i = 0; i < segments.Count; i++)
        {
            float normalized = segments.Count <= 1
                ? 0.5f
                : i / (float)(segments.Count - 1);
            float angle = Mathf.Lerp(
                ArcStartDegrees,
                ArcEndDegrees,
                normalized);
            float radians = angle * Mathf.Deg2Rad;
            Transform segment = segments[i].transform;
            segment.localPosition = new Vector3(
                Mathf.Cos(radians) * radius,
                Mathf.Sin(radians) * radius,
                0f);
            segment.localRotation = Quaternion.Euler(
                0f,
                0f,
                angle + 90f);
            segment.localScale = new Vector3(
                segmentLength,
                thickness,
                1f);
        }
    }

    private void RefreshColors()
    {
        float hp01 = currentHP / (float)Mathf.Max(1, maximumHP);

        for (int i = 0; i < healthSegments.Count; i++)
        {
            float threshold = (i + 1f) / healthSegments.Count;
            healthSegments[i].color = hp01 + 0.0001f >= threshold
                ? healthFillColor
                : healthEmptyColor;
        }

        for (int i = 0; i < healingPointSegments.Count; i++)
        {
            healingPointSegments[i].color = i < currentHealingPoints
                ? healingFillColor
                : healingEmptyColor;
        }
    }

    private void ApplySorting()
    {
        if (sourceRenderer == null)
            return;

        int layerID = sourceRenderer.sortingLayerID;
        int order = sourceRenderer.sortingOrder + sortingOrderOffset;

        ApplySorting(healthSegments, layerID, order);
        ApplySorting(healingPointSegments, layerID, order);
    }

    private static void ApplySorting(
        List<SpriteRenderer> renderers,
        int layerID,
        int order)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            renderers[i].sortingLayerID = layerID;
            renderers[i].sortingOrder = order;
        }
    }

    private static void DestroyRenderers(
        List<SpriteRenderer> renderers)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
                Destroy(renderers[i].gameObject);
        }

        renderers.Clear();
    }

    private static SpriteRenderer FirstValid(SpriteRenderer[] renderers)
    {
        if (renderers == null)
            return null;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                return renderers[i];
        }

        return null;
    }

    private void OnDisable()
    {
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshRootVisibility();
    }

    private void RefreshRootVisibility()
    {
        if (visualRoot == null)
            return;

        EriSupportManager support = EriSupportManager.Instance;
        bool shouldShow =
            presentationEnabled &&
            support != null &&
            !support.IsEriDefeated;
        visualRoot.gameObject.SetActive(shouldShow);
    }

    private void OnDestroy()
    {
        if (visualRoot != null)
            Destroy(visualRoot.gameObject);

        if (solidSprite != null)
            Destroy(solidSprite);

        if (solidTexture != null)
            Destroy(solidTexture);
    }
}
