using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A deliberately small world-space AP display that stays behind combat
/// projectiles. A segmented lower arc shows approximate AP, while abstract
/// solid circles show affordable, inflated, and over-max skill states.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldAPStatusDisplay2D : MonoBehaviour
{
    private sealed class SkillNode
    {
        public Transform root;
        public SpriteRenderer body;
        public SpriteRenderer slash;
        public float normalizedCost;
        public int outwardStackIndex;
    }

    [Header("Placement")]
    [SerializeField] private Vector2 localOffset = new Vector2(0f, -0.30f);
    [SerializeField, Range(0.15f, 0.75f)] private float arcRadius = 0.34f;
    [SerializeField, Range(8, 32)] private int arcSegmentCount = 18;
    [SerializeField, Range(0.008f, 0.08f)] private float arcThickness = 0.022f;
    [SerializeField, Range(0.04f, 0.30f)] private float skillNodeSize = 0.11f;

    [Header("Inner Health Arc")]
    [SerializeField] private bool showHealthArc = true;
    [SerializeField, Range(0.03f, 0.25f)] private float healthArcRadiusOffset = 0.085f;
    [SerializeField, Range(8, 32)] private int healthArcSegmentCount = 18;
    [SerializeField, Range(0.008f, 0.08f)] private float healthArcThickness = 0.018f;
    [SerializeField] private Color healthFillColor =
        new Color(0.94f, 0.18f, 0.16f, 0.92f);
    [SerializeField] private Color emptyHealthColor =
        new Color(0.22f, 0.035f, 0.03f, 0.42f);

    [Header("Colors")]
    [SerializeField] private Color apFillColor =
        new Color(0.24f, 0.92f, 1f, 0.68f);
    [SerializeField] private Color emptyArcColor =
        new Color(0.08f, 0.14f, 0.16f, 0.30f);
    [SerializeField] private Color unavailableNodeColor =
        new Color(0.68f, 0.76f, 0.79f, 0.82f);
    [SerializeField] private Color affordableNodeColor =
        new Color(0.72f, 0.98f, 1f, 0.92f);
    [SerializeField] private Color overMaximumNodeColor =
        new Color(1f, 0.34f, 0.26f, 0.90f);
    [SerializeField] private Color lockedArcColor =
        new Color(0.92f, 0.16f, 0.12f, 0.88f);

    [Header("Brief Exact Readout")]
    [SerializeField] private bool showBriefAPNumber = true;
    [SerializeField, Min(0.1f)] private float apNumberDuration = 0.80f;
    [SerializeField] private Color apNumberColor =
        new Color(0.78f, 0.98f, 1f, 0.90f);

    [Header("Layering")]
    [SerializeField] private int sortingOrderOffset = -8;

    [Header("Runtime Debug")]
    [SerializeField] private int displayedCurrentAP;
    [SerializeField] private int displayedMaximumAP = 1;
    [SerializeField] private int displayedCurrentHP;
    [SerializeField] private int displayedMaximumHP = 1;
    [SerializeField] private int displayedAPSkillCount;
    [SerializeField] private bool menuOpen;
    [SerializeField] private bool allSkillsUnreachable;

    private readonly List<SpriteRenderer> arcSegments =
        new List<SpriteRenderer>(24);
    private readonly List<SpriteRenderer> healthArcSegments =
        new List<SpriteRenderer>(24);
    private readonly List<SkillNode> skillNodes =
        new List<SkillNode>(4);

    private Transform visualRoot;
    private TextMeshPro apNumberText;
    private Sprite solidSprite;
    private Sprite circleSprite;
    private Texture2D solidTexture;
    private Texture2D circleTexture;
    private SpriteRenderer sourceRenderer;
    private float apNumberRemaining;
    private int lastCurrentAP = -1;
    private bool presentationEnabled = true;

    private const float ArcStartDegrees = 205f;
    private const float ArcEndDegrees = 335f;

    private void Awake()
    {
        EnsureVisualResources();
        RebuildArcSegments();
    }

    public void Configure(
        SpriteRenderer[] characterRenderers,
        Vector2 newLocalOffset,
        float newArcRadius,
        int newArcSegmentCount,
        float newArcThickness,
        float newSkillNodeSize,
        bool newShowHealthArc,
        float newHealthArcRadiusOffset,
        int newHealthArcSegmentCount,
        float newHealthArcThickness,
        Color newHealthFillColor,
        Color newEmptyHealthColor,
        Color newAPFillColor,
        Color newEmptyArcColor,
        Color newUnavailableNodeColor,
        Color newAffordableNodeColor,
        Color newOverMaximumNodeColor,
        Color newLockedArcColor,
        bool newShowBriefAPNumber,
        float newAPNumberDuration,
        Color newAPNumberColor,
        int newSortingOrderOffset)
    {
        sourceRenderer = FirstValid(characterRenderers);
        localOffset = newLocalOffset;
        arcRadius = Mathf.Clamp(newArcRadius, 0.15f, 0.75f);
        int nextSegmentCount = Mathf.Clamp(newArcSegmentCount, 8, 32);
        bool segmentCountChanged = nextSegmentCount != arcSegmentCount;
        arcSegmentCount = nextSegmentCount;
        arcThickness = Mathf.Clamp(newArcThickness, 0.008f, 0.08f);
        skillNodeSize = Mathf.Clamp(newSkillNodeSize, 0.04f, 0.30f);
        showHealthArc = newShowHealthArc;
        healthArcRadiusOffset = Mathf.Clamp(
            newHealthArcRadiusOffset,
            0.03f,
            0.25f);
        int nextHealthSegmentCount = Mathf.Clamp(
            newHealthArcSegmentCount,
            8,
            32);
        bool healthSegmentCountChanged =
            nextHealthSegmentCount != healthArcSegmentCount;
        healthArcSegmentCount = nextHealthSegmentCount;
        healthArcThickness = Mathf.Clamp(
            newHealthArcThickness,
            0.008f,
            0.08f);
        healthFillColor = newHealthFillColor;
        emptyHealthColor = newEmptyHealthColor;
        apFillColor = newAPFillColor;
        emptyArcColor = newEmptyArcColor;
        unavailableNodeColor = newUnavailableNodeColor;
        affordableNodeColor = newAffordableNodeColor;
        overMaximumNodeColor = newOverMaximumNodeColor;
        lockedArcColor = newLockedArcColor;
        showBriefAPNumber = newShowBriefAPNumber;
        apNumberDuration = Mathf.Max(0.1f, newAPNumberDuration);
        apNumberColor = newAPNumberColor;
        sortingOrderOffset = newSortingOrderOffset;

        EnsureVisualResources();

        if (segmentCountChanged || arcSegments.Count != arcSegmentCount)
            RebuildArcSegments();

        if (healthSegmentCountChanged ||
            healthArcSegments.Count != healthArcSegmentCount)
        {
            RebuildHealthArcSegments();
        }

        LayoutArcSegments();
        LayoutHealthArcSegments();
        ApplySorting();
        RefreshVisuals();
    }

    public void SetPresentationEnabled(bool enabled)
    {
        presentationEnabled = enabled;

        if (visualRoot != null)
            visualRoot.gameObject.SetActive(enabled);
    }

    public void SetState(
        PartyManager.CharacterState active,
        CombatSkillSystem skillSystem,
        bool isMenuOpen)
    {
        menuOpen = isMenuOpen;

        if (active == null || active.def == null)
        {
            ClearState();
            return;
        }

        displayedMaximumAP = Mathf.Max(1, active.def.maxAP);
        displayedCurrentAP = Mathf.Clamp(
            active.currentAP,
            0,
            displayedMaximumAP);
        displayedMaximumHP = Mathf.Max(1, active.def.maxHP);
        displayedCurrentHP = Mathf.Clamp(
            active.currentHP,
            0,
            displayedMaximumHP);

        if (lastCurrentAP >= 0 && displayedCurrentAP != lastCurrentAP)
            apNumberRemaining = apNumberDuration;

        lastCurrentAP = displayedCurrentAP;
        UpdateSkillNodes(active, skillSystem);
        RefreshVisuals();
    }

    public void ClearState(bool immediate = false)
    {
        displayedCurrentAP = 0;
        displayedMaximumAP = 1;
        displayedCurrentHP = 0;
        displayedMaximumHP = 1;
        displayedAPSkillCount = 0;
        menuOpen = false;
        allSkillsUnreachable = false;
        lastCurrentAP = -1;

        if (immediate)
            apNumberRemaining = 0f;

        for (int i = 0; i < skillNodes.Count; i++)
            skillNodes[i].root.gameObject.SetActive(false);

        RefreshVisuals();
    }

    private void Update()
    {
        if (apNumberRemaining > 0f)
        {
            apNumberRemaining = Mathf.Max(
                0f,
                apNumberRemaining - Time.unscaledDeltaTime);
        }

        RefreshAPNumber();
    }

    private void UpdateSkillNodes(
        PartyManager.CharacterState active,
        CombatSkillSystem skillSystem)
    {
        int visibleIndex = 0;
        int reachableSkillCount = 0;
        Dictionary<int, int> stackCounts = new Dictionary<int, int>();

        if (active.unlockedSkills != null && skillSystem != null)
        {
            for (int i = 0; i < active.unlockedSkills.Count; i++)
            {
                SkillDefinition skill = active.unlockedSkills[i];

                if (skill == null ||
                    skill.executionType == SkillExecutionType.EriHealingCall)
                {
                    continue;
                }

                int scaledCost = skillSystem.GetScaledCost(skill);

                if (scaledCost <= 0 || scaledCost == int.MaxValue)
                    continue;

                EnsureSkillNodeCount(visibleIndex + 1);
                SkillNode node = skillNodes[visibleIndex];
                bool overMaximum = scaledCost > displayedMaximumAP;
                bool affordable = !overMaximum &&
                    displayedCurrentAP >= scaledCost;

                if (!overMaximum)
                    reachableSkillCount++;

                // Every over-max skill occupies the same endpoint bucket.
                // Equal reachable costs share a bucket and stack outward.
                int positionKey = overMaximum ? int.MaxValue : scaledCost;
                int stackIndex = stackCounts.TryGetValue(
                    positionKey,
                    out int existingCount)
                    ? existingCount
                    : 0;
                stackCounts[positionKey] = stackIndex + 1;

                node.root.gameObject.SetActive(true);
                node.slash.enabled = overMaximum;
                node.normalizedCost = overMaximum
                    ? 1f
                    : Mathf.Clamp01(
                        scaledCost / (float)displayedMaximumAP);
                node.outwardStackIndex = stackIndex;
                Color bodyColor = overMaximum
                    ? overMaximumNodeColor
                    : affordable
                        ? affordableNodeColor
                        : unavailableNodeColor;

                bodyColor.a = 1f;
                node.body.color = bodyColor;
                node.slash.color = Opaque(overMaximumNodeColor);
                visibleIndex++;
            }
        }

        displayedAPSkillCount = visibleIndex;
        allSkillsUnreachable =
            visibleIndex > 0 &&
            reachableSkillCount == 0;

        for (int i = visibleIndex; i < skillNodes.Count; i++)
            skillNodes[i].root.gameObject.SetActive(false);

        LayoutSkillNodes();
    }

    private void EnsureVisualResources()
    {
        if (visualRoot == null)
        {
            GameObject rootObject = new GameObject("WorldAPStatus");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;
        }

        visualRoot.localPosition = localOffset;

        if (solidSprite == null)
            CreateProceduralSprites();

        if (apNumberText == null)
            CreateAPNumberText();

        visualRoot.gameObject.SetActive(presentationEnabled);
    }

    private void CreateProceduralSprites()
    {
        solidTexture = CreateSolidTexture();
        solidSprite = CreateSprite(solidTexture, "World AP Solid");

        circleTexture = CreateDiscTexture();
        circleSprite = CreateSprite(circleTexture, "World AP Disc");
    }

    private void RebuildArcSegments()
    {
        for (int i = 0; i < arcSegments.Count; i++)
        {
            if (arcSegments[i] != null)
                Destroy(arcSegments[i].gameObject);
        }

        arcSegments.Clear();
        EnsureVisualResources();

        for (int i = 0; i < arcSegmentCount; i++)
        {
            GameObject segmentObject = new GameObject(
                $"WorldAP_ArcSegment_{i + 1}");
            segmentObject.transform.SetParent(visualRoot, false);

            SpriteRenderer segment =
                segmentObject.AddComponent<SpriteRenderer>();
            segment.sprite = solidSprite;
            arcSegments.Add(segment);
        }

        LayoutArcSegments();
        ApplySorting();
    }

    private void RebuildHealthArcSegments()
    {
        for (int i = 0; i < healthArcSegments.Count; i++)
        {
            if (healthArcSegments[i] != null)
                Destroy(healthArcSegments[i].gameObject);
        }

        healthArcSegments.Clear();
        EnsureVisualResources();

        for (int i = 0; i < healthArcSegmentCount; i++)
        {
            GameObject segmentObject = new GameObject(
                $"WorldAP_HPArcSegment_{i + 1}");
            segmentObject.transform.SetParent(visualRoot, false);

            SpriteRenderer segment =
                segmentObject.AddComponent<SpriteRenderer>();
            segment.sprite = solidSprite;
            healthArcSegments.Add(segment);
        }

        LayoutHealthArcSegments();
        ApplySorting();
    }

    private void LayoutArcSegments()
    {
        if (visualRoot == null)
            return;

        visualRoot.localPosition = localOffset;
        float spanRadians =
            (ArcEndDegrees - ArcStartDegrees) * Mathf.Deg2Rad;
        float segmentLength =
            arcRadius * spanRadians /
            Mathf.Max(1, arcSegmentCount) * 0.72f;

        for (int i = 0; i < arcSegments.Count; i++)
        {
            float normalized = arcSegments.Count <= 1
                ? 0.5f
                : i / (float)(arcSegments.Count - 1);
            float angle = Mathf.Lerp(
                ArcStartDegrees,
                ArcEndDegrees,
                normalized);
            float radians = angle * Mathf.Deg2Rad;
            Transform segment = arcSegments[i].transform;
            segment.localPosition = new Vector3(
                Mathf.Cos(radians) * arcRadius,
                Mathf.Sin(radians) * arcRadius,
                0f);
            segment.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
            segment.localScale = new Vector3(
                segmentLength,
                arcThickness,
                1f);
        }
    }

    private void LayoutHealthArcSegments()
    {
        float healthRadius = Mathf.Max(
            0.08f,
            arcRadius - healthArcRadiusOffset);
        float spanRadians =
            (ArcEndDegrees - ArcStartDegrees) * Mathf.Deg2Rad;
        float segmentLength =
            healthRadius * spanRadians /
            Mathf.Max(1, healthArcSegmentCount) * 0.72f;

        for (int i = 0; i < healthArcSegments.Count; i++)
        {
            float normalized = healthArcSegments.Count <= 1
                ? 0.5f
                : i / (float)(healthArcSegments.Count - 1);
            float angle = Mathf.Lerp(
                ArcStartDegrees,
                ArcEndDegrees,
                normalized);
            float radians = angle * Mathf.Deg2Rad;
            Transform segment = healthArcSegments[i].transform;
            segment.localPosition = new Vector3(
                Mathf.Cos(radians) * healthRadius,
                Mathf.Sin(radians) * healthRadius,
                0f);
            segment.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
            segment.localScale = new Vector3(
                segmentLength,
                healthArcThickness,
                1f);
        }
    }

    private void EnsureSkillNodeCount(int requiredCount)
    {
        while (skillNodes.Count < requiredCount)
        {
            GameObject nodeObject = new GameObject(
                $"WorldAP_SkillNode_{skillNodes.Count + 1}");
            nodeObject.transform.SetParent(visualRoot, false);

            SpriteRenderer body = CreateChildRenderer(
                "WorldAP_NodeSolidCircle",
                nodeObject.transform,
                circleSprite);
            SpriteRenderer slash = CreateChildRenderer(
                "WorldAP_OverMaximumSlash",
                nodeObject.transform,
                solidSprite);
            slash.transform.localRotation = Quaternion.Euler(0f, 0f, -42f);
            slash.transform.localScale = new Vector3(0.18f, 1.18f, 1f);

            skillNodes.Add(new SkillNode
            {
                root = nodeObject.transform,
                body = body,
                slash = slash
            });
        }

        ApplySorting();
    }

    private void LayoutSkillNodes()
    {
        if (displayedAPSkillCount <= 0)
            return;

        for (int i = 0; i < displayedAPSkillCount; i++)
        {
            SkillNode node = skillNodes[i];
            float angle = Mathf.Lerp(
                ArcStartDegrees,
                ArcEndDegrees,
                node.normalizedCost);
            float radians = angle * Mathf.Deg2Rad;
            float nodeRadius = arcRadius +
                node.outwardStackIndex * skillNodeSize * 1.15f;
            node.root.localPosition = new Vector3(
                Mathf.Cos(radians) * nodeRadius,
                Mathf.Sin(radians) * nodeRadius,
                0f);
            node.root.localRotation = Quaternion.identity;
            node.root.localScale = Vector3.one * skillNodeSize;
        }
    }

    private void CreateAPNumberText()
    {
        GameObject textObject = new GameObject("WorldAP_BriefNumber");
        textObject.transform.SetParent(visualRoot, false);
        textObject.transform.localPosition = new Vector3(0f, -0.13f, 0f);

        apNumberText = textObject.AddComponent<TextMeshPro>();
        apNumberText.alignment = TextAlignmentOptions.Center;
        apNumberText.fontSize = 1.15f;
        apNumberText.color = apNumberColor;
        apNumberText.rectTransform.sizeDelta = new Vector2(1.2f, 0.24f);
        apNumberText.raycastTarget = false;
    }

    private SpriteRenderer CreateChildRenderer(
        string objectName,
        Transform parent,
        Sprite sprite)
    {
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(parent, false);
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        return renderer;
    }

    private void RefreshVisuals()
    {
        if (!presentationEnabled || visualRoot == null)
            return;

        float ap01 = displayedCurrentAP / (float)Mathf.Max(1, displayedMaximumAP);
        float changeFlash = Mathf.Clamp01(
            apNumberRemaining / Mathf.Max(0.1f, apNumberDuration));

        for (int i = 0; i < arcSegments.Count; i++)
        {
            float threshold = (i + 1f) / arcSegments.Count;
            bool filled = ap01 + 0.0001f >= threshold;

            if (allSkillsUnreachable)
            {
                Color lockedEmpty = lockedArcColor;
                lockedEmpty.a *= 0.58f;
                arcSegments[i].color = filled
                    ? Color.Lerp(lockedArcColor, Color.white, 0.16f)
                    : lockedEmpty;
            }
            else
            {
                arcSegments[i].color = filled
                    ? Color.Lerp(
                        apFillColor,
                        Color.white,
                        changeFlash * 0.22f)
                    : emptyArcColor;
            }
        }

        float hp01 = displayedCurrentHP /
            (float)Mathf.Max(1, displayedMaximumHP);

        for (int i = 0; i < healthArcSegments.Count; i++)
        {
            healthArcSegments[i].gameObject.SetActive(showHealthArc);

            if (!showHealthArc)
                continue;

            float threshold = (i + 1f) / healthArcSegments.Count;
            healthArcSegments[i].color = hp01 + 0.0001f >= threshold
                ? healthFillColor
                : emptyHealthColor;
        }

        RefreshAPNumber();
    }

    private void RefreshAPNumber()
    {
        if (apNumberText == null)
            return;

        bool visible = showBriefAPNumber &&
            (menuOpen || apNumberRemaining > 0f);
        apNumberText.gameObject.SetActive(visible);

        if (!visible)
            return;

        apNumberText.text = menuOpen
            ? $"{displayedCurrentAP} / {displayedMaximumAP}"
            : displayedCurrentAP.ToString();

        Color color = apNumberColor;

        if (!menuOpen)
        {
            color.a *= Mathf.Clamp01(
                apNumberRemaining /
                Mathf.Min(0.22f, apNumberDuration));
        }

        apNumberText.color = color;
    }

    private void ApplySorting()
    {
        if (sourceRenderer == null)
            return;

        int layerID = sourceRenderer.sortingLayerID;
        int baseOrder = sourceRenderer.sortingOrder + sortingOrderOffset;

        for (int i = 0; i < arcSegments.Count; i++)
        {
            arcSegments[i].sortingLayerID = layerID;
            arcSegments[i].sortingOrder = baseOrder;
        }

        for (int i = 0; i < healthArcSegments.Count; i++)
        {
            healthArcSegments[i].sortingLayerID = layerID;
            healthArcSegments[i].sortingOrder = baseOrder;
        }

        for (int i = 0; i < skillNodes.Count; i++)
        {
            skillNodes[i].body.sortingLayerID = layerID;
            skillNodes[i].slash.sortingLayerID = layerID;
            skillNodes[i].body.sortingOrder = baseOrder + 1;
            skillNodes[i].slash.sortingOrder = baseOrder + 2;
        }

        Renderer textRenderer = apNumberText != null
            ? apNumberText.GetComponent<Renderer>()
            : null;

        if (textRenderer != null)
        {
            textRenderer.sortingLayerID = layerID;
            textRenderer.sortingOrder = baseOrder + 2;
        }
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

    private static Texture2D CreateSolidTexture()
    {
        Texture2D texture = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            true);
        texture.SetPixels(new[]
        {
            Color.white, Color.white,
            Color.white, Color.white
        });
        texture.Apply(false, true);
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    private static Texture2D CreateDiscTexture()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false,
            true);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(
                    ((x + 0.5f) / size) * 2f - 1f,
                    ((y + 0.5f) / size) * 2f - 1f);
                float radius = point.magnitude;
                // Mathf.SmoothStep interpolates FROM/TO using its third
                // argument; it is not GLSL's smoothstep(edge0, edge1, value).
                // Convert the outer edge to 0..1 first so the disc interior is
                // truly opaque and only its outermost pixel is antialiased.
                float edge01 = Mathf.InverseLerp(0.94f, 1f, radius);
                float smoothEdge =
                    edge01 * edge01 * (3f - 2f * edge01);
                float alpha = 1f - smoothEdge;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    private static Sprite CreateSprite(Texture2D texture, string spriteName)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Color Opaque(Color color)
    {
        color.a = 1f;
        return color;
    }

    private void OnDisable()
    {
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(presentationEnabled);
    }

    private void OnDestroy()
    {
        if (visualRoot != null)
            Destroy(visualRoot.gameObject);

        if (solidSprite != null)
            Destroy(solidSprite);
        if (circleSprite != null)
            Destroy(circleSprite);
        if (solidTexture != null)
            Destroy(solidTexture);
        if (circleTexture != null)
            Destroy(circleTexture);
    }
}
