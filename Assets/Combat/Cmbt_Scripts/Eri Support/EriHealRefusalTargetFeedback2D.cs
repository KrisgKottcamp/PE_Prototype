using System.Collections;
using UnityEngine;

/// <summary>
/// Brief crossed-out green healing icon displayed above the character whose
/// Call Eri request was refused. The sprite is generated at runtime so the
/// feature has no prefab, font, or imported texture dependency.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriHealRefusalTargetFeedback2D : MonoBehaviour
{
    private Transform visualRoot;
    private SpriteRenderer iconRenderer;
    private Texture2D iconTexture;
    private Sprite iconSprite;
    private Coroutine animationRoutine;

    private Vector2 localOffset = new Vector2(0f, 0.72f);
    private float duration = 1f;
    private float iconSize = 0.34f;
    private int sortingOrderOffset = 20;
    private SpriteRenderer sourceRenderer;

    public void Configure(
        Vector2 newLocalOffset,
        float newDuration,
        float newIconSize,
        int newSortingOrderOffset)
    {
        localOffset = newLocalOffset;
        duration = Mathf.Max(0.15f, newDuration);
        iconSize = Mathf.Clamp(newIconSize, 0.08f, 0.80f);
        sortingOrderOffset = newSortingOrderOffset;
        sourceRenderer = FindSourceRenderer();

        EnsureVisual();
        ApplyAppearance();
    }

    public void Play()
    {
        EnsureVisual();

        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate());
    }

    private void EnsureVisual()
    {
        bool createdVisual = false;

        if (visualRoot == null)
        {
            GameObject rootObject =
                new GameObject("HealingRefusal_CrossedPlus");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;
            createdVisual = true;
        }

        if (iconSprite == null)
            CreateCrossedHealingSprite();

        if (iconRenderer == null)
        {
            GameObject iconObject =
                new GameObject("HealingRefusal_Icon");
            iconObject.transform.SetParent(visualRoot, false);
            iconRenderer = iconObject.AddComponent<SpriteRenderer>();
            iconRenderer.sprite = iconSprite;
        }

        ApplyAppearance();

        if (createdVisual)
            visualRoot.gameObject.SetActive(false);
    }

    private void ApplyAppearance()
    {
        if (visualRoot == null || iconRenderer == null)
            return;

        visualRoot.localPosition = localOffset;
        visualRoot.localScale = Vector3.one * iconSize;
        iconRenderer.color = Color.white;

        if (sourceRenderer != null)
        {
            iconRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            iconRenderer.sortingOrder =
                sourceRenderer.sortingOrder + sortingOrderOffset;
        }
    }

    private IEnumerator Animate()
    {
        visualRoot.gameObject.SetActive(true);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pop = normalized < 0.16f
                ? Mathf.Lerp(0.58f, 1.10f, normalized / 0.16f)
                : Mathf.Lerp(1.10f, 1f, (normalized - 0.16f) / 0.30f);
            float fade = normalized < 0.62f
                ? 1f
                : 1f - Mathf.InverseLerp(0.62f, 1f, normalized);

            visualRoot.localPosition =
                (Vector3)localOffset +
                Vector3.up * Mathf.Lerp(0f, 0.12f, normalized);
            visualRoot.localScale = Vector3.one * iconSize * pop;

            Color color = Color.white;
            color.a = fade;
            iconRenderer.color = color;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        visualRoot.gameObject.SetActive(false);
        animationRoutine = null;
    }

    private void CreateCrossedHealingSprite()
    {
        const int textureSize = 32;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color plusOutline = new Color(0.02f, 0.18f, 0.07f, 1f);
        Color plusGreen = new Color(0.14f, 1f, 0.38f, 1f);
        Color slashOutline = new Color(0.20f, 0.025f, 0.02f, 1f);
        Color slashRed = new Color(1f, 0.16f, 0.11f, 1f);
        Color[] pixels = new Color[textureSize * textureSize];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        // Dark outer plus keeps the symbol readable over bright arenas.
        DrawRect(pixels, textureSize, 10, 3, 21, 28, plusOutline);
        DrawRect(pixels, textureSize, 3, 10, 28, 21, plusOutline);
        DrawRect(pixels, textureSize, 12, 5, 19, 26, plusGreen);
        DrawRect(pixels, textureSize, 5, 12, 26, 19, plusGreen);

        // Conventional red strike, drawn last so the refusal is unmistakable.
        DrawLine(pixels, textureSize, 5, 27, 27, 5, 5, slashOutline);
        DrawLine(pixels, textureSize, 5, 27, 27, 5, 2, slashRed);

        iconTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false,
            true);
        iconTexture.SetPixels(pixels);
        iconTexture.Apply(false, true);
        iconTexture.filterMode = FilterMode.Point;
        iconTexture.wrapMode = TextureWrapMode.Clamp;
        iconTexture.hideFlags = HideFlags.HideAndDontSave;

        iconSprite = Sprite.Create(
            iconTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        iconSprite.name = "Crossed Out Healing";
        iconSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private SpriteRenderer FindSourceRenderer()
    {
        SpriteRenderer[] renderers =
            DamageFlash2D.FindLikelyCharacterSprites(transform);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                return renderers[i];
        }

        return null;
    }

    private static void DrawRect(
        Color[] pixels,
        int textureSize,
        int minX,
        int minY,
        int maxX,
        int maxY,
        Color color)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
                SetPixel(pixels, textureSize, x, y, color);
        }
    }

    private static void DrawLine(
        Color[] pixels,
        int textureSize,
        int startX,
        int startY,
        int endX,
        int endY,
        int radius,
        Color color)
    {
        int steps = Mathf.Max(
            Mathf.Abs(endX - startX),
            Mathf.Abs(endY - startY));

        for (int step = 0; step <= steps; step++)
        {
            float t = steps == 0 ? 0f : step / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(startX, endX, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(startY, endY, t));

            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (offsetX * offsetX + offsetY * offsetY <=
                        radius * radius)
                    {
                        SetPixel(
                            pixels,
                            textureSize,
                            x + offsetX,
                            y + offsetY,
                            color);
                    }
                }
            }
        }
    }

    private static void SetPixel(
        Color[] pixels,
        int textureSize,
        int x,
        int y,
        Color color)
    {
        if (x < 0 || y < 0 ||
            x >= textureSize || y >= textureSize)
        {
            return;
        }

        pixels[y * textureSize + x] = color;
    }

    private void OnDisable()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        if (visualRoot != null)
            visualRoot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (iconSprite != null)
            Destroy(iconSprite);

        if (iconTexture != null)
            Destroy(iconTexture);
    }
}
