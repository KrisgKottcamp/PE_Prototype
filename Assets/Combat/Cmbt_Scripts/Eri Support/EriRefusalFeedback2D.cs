using System.Collections;
using UnityEngine;

/// <summary>
/// Brief world-space thumbs-down feedback used when Eri refuses a healing call
/// because reaching and channeling at the caller would be too dangerous.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriRefusalFeedback2D : MonoBehaviour
{
    private Transform visualRoot;
    private SpriteRenderer iconRenderer;
    private Texture2D iconTexture;
    private Sprite iconSprite;
    private Coroutine animationRoutine;

    private Vector2 localOffset = new Vector2(0f, 0.62f);
    private float duration = 1f;
    private float iconSize = 0.32f;
    private Color iconColor = new Color(1f, 0.78f, 0.12f, 1f);
    private int sortingOrderOffset = 20;
    private SpriteRenderer sourceRenderer;

    public void Configure(
        SpriteRenderer[] eriRenderers,
        Vector2 newLocalOffset,
        float newDuration,
        float newIconSize,
        Color newIconColor,
        int newSortingOrderOffset)
    {
        sourceRenderer = FirstValid(eriRenderers);
        localOffset = newLocalOffset;
        duration = Mathf.Max(0.15f, newDuration);
        iconSize = Mathf.Clamp(newIconSize, 0.08f, 0.80f);
        iconColor = newIconColor;
        sortingOrderOffset = newSortingOrderOffset;

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
            GameObject rootObject = new GameObject("EriRefusal_ThumbsDown");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;
            createdVisual = true;
        }

        if (iconSprite == null)
            CreateThumbsDownSprite();

        if (iconRenderer == null)
        {
            GameObject iconObject = new GameObject("EriRefusal_Icon");
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
        iconRenderer.color = iconColor;

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
                Vector3.up * Mathf.Lerp(0f, 0.14f, normalized);
            visualRoot.localScale = Vector3.one * iconSize * pop;

            Color color = iconColor;
            color.a *= fade;
            iconRenderer.color = color;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        visualRoot.gameObject.SetActive(false);
        animationRoutine = null;
    }

    private void CreateThumbsDownSprite()
    {
        const int size = 32;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.16f, 0.10f, 0.02f, 1f);
        Color[] pixels = new Color[size * size];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        // Dark silhouette first, then a smaller golden fill. This provides a
        // readable emoji-like thumb at combat-camera scale without font setup.
        DrawRect(pixels, size, 2, 13, 9, 24, outline);
        DrawRect(pixels, size, 8, 11, 27, 25, outline);
        DrawRect(pixels, size, 24, 13, 30, 23, outline);
        DrawRect(pixels, size, 11, 3, 20, 15, outline);

        DrawRect(pixels, size, 4, 15, 8, 22, Color.white);
        DrawRect(pixels, size, 10, 13, 25, 23, Color.white);
        DrawRect(pixels, size, 24, 15, 28, 21, Color.white);
        DrawRect(pixels, size, 13, 5, 18, 14, Color.white);

        // Clip a few corners to soften the block silhouette.
        SetPixel(pixels, size, 8, 11, clear);
        SetPixel(pixels, size, 27, 11, clear);
        SetPixel(pixels, size, 30, 13, clear);
        SetPixel(pixels, size, 11, 3, clear);
        SetPixel(pixels, size, 20, 3, clear);

        iconTexture = new Texture2D(
            size,
            size,
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
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        iconSprite.name = "Eri Refusal Thumbs Down";
        iconSprite.hideFlags = HideFlags.HideAndDontSave;
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
        if (visualRoot != null)
            Destroy(visualRoot.gameObject);

        if (iconSprite != null)
            Destroy(iconSprite);

        if (iconTexture != null)
            Destroy(iconTexture);
    }
}
