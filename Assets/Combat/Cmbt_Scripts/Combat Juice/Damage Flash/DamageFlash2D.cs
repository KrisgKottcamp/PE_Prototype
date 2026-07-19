using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporarily swaps selected SpriteRenderers to an unlit white-silhouette
/// shader. Original materials are restored after an unscaled-time flash.
/// </summary>
[DisallowMultipleComponent]
public class DamageFlash2D : MonoBehaviour
{
    private const string FlashShaderResourceName =
        "DamageFlashWhite";

    [Header("Flash Timing")]
    [Tooltip("Real-time white-flash duration. 0.05-0.07 seconds is roughly a few frames.")]
    [SerializeField, Min(0.01f)]
    private float flashDuration = 0.06f;

    [Header("Sprite Targets")]
    [Tooltip("Leave empty to use every SpriteRenderer beneath this object.")]
    [SerializeField]
    private SpriteRenderer[] targetRenderers;

    [Tooltip("Includes disabled child renderers when targets are found automatically.")]
    [SerializeField]
    private bool includeInactiveChildren = true;

    [Header("Optional Override")]
    [Tooltip("Normally loaded automatically from the included Resources shader.")]
    [SerializeField]
    private Shader flashShader;

    private Material flashMaterial;
    private Material[][] originalMaterials;
    private Coroutine flashRoutine;
    private bool flashApplied;

    public float FlashDuration => Mathf.Max(0.01f, flashDuration);
    public bool HasConfiguredTargets =>
        targetRenderers != null &&
        targetRenderers.Length > 0;

    /// <summary>
    /// Prefers renderers on objects named like character sprites and filters
    /// common utility visuals. This keeps health bars, hitboxes, and telegraphs
    /// out of the automatic full-character flash.
    /// </summary>
    public static SpriteRenderer[] FindLikelyCharacterSprites(
        Transform root)
    {
        if (root == null)
            return new SpriteRenderer[0];

        SpriteRenderer[] all =
            root.GetComponentsInChildren<SpriteRenderer>(true);

        List<SpriteRenderer> preferred =
            new List<SpriteRenderer>();

        List<SpriteRenderer> fallback =
            new List<SpriteRenderer>();

        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer candidate = all[i];

            if (candidate == null ||
                IsUtilityVisualName(candidate.gameObject.name))
            {
                continue;
            }

            fallback.Add(candidate);

            if (candidate.gameObject.name.IndexOf(
                "sprite",
                StringComparison.OrdinalIgnoreCase) >= 0)
            {
                preferred.Add(candidate);
            }
        }

        return preferred.Count > 0
            ? preferred.ToArray()
            : fallback.ToArray();
    }

    /// <summary>
    /// Allows health components to supply the exact character sprites at
    /// runtime, avoiding health bars, hitbox visuals, and other child sprites.
    /// </summary>
    public void ConfigureTargets(SpriteRenderer[] renderers)
    {
        if (flashApplied)
            RestoreOriginalMaterials();

        targetRenderers = RemoveNullEntries(renderers);
        originalMaterials = null;
    }

    public void PlayFlash()
    {
        if (!EnsureReady())
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        if (!flashApplied)
            CaptureAndApplyFlashMaterials();

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private bool EnsureReady()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<SpriteRenderer>(
                includeInactiveChildren
            );
        }

        targetRenderers = RemoveNullEntries(targetRenderers);

        if (targetRenderers.Length == 0)
            return false;

        if (flashMaterial != null)
            return true;

        if (flashShader == null)
        {
            flashShader = Resources.Load<Shader>(
                FlashShaderResourceName
            );
        }

        if (flashShader == null)
        {
            Debug.LogError(
                "DamageFlash2D: Could not load DamageFlashWhite shader from Resources.",
                this
            );
            return false;
        }

        flashMaterial = new Material(flashShader)
        {
            name = $"Damage Flash White ({name})",
            hideFlags = HideFlags.HideAndDontSave
        };

        return true;
    }

    private void CaptureAndApplyFlashMaterials()
    {
        originalMaterials =
            new Material[targetRenderers.Length][];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = targetRenderers[i];

            if (spriteRenderer == null)
                continue;

            originalMaterials[i] = spriteRenderer.sharedMaterials;
            spriteRenderer.sharedMaterial = flashMaterial;
        }

        flashApplied = true;
    }

    private IEnumerator FlashRoutine()
    {
        yield return new WaitForSecondsRealtime(FlashDuration);

        RestoreOriginalMaterials();
        flashRoutine = null;
    }

    private void RestoreOriginalMaterials()
    {
        if (!flashApplied)
            return;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = targetRenderers[i];

            if (spriteRenderer == null ||
                originalMaterials == null ||
                i >= originalMaterials.Length ||
                originalMaterials[i] == null)
            {
                continue;
            }

            spriteRenderer.sharedMaterials = originalMaterials[i];
        }

        originalMaterials = null;
        flashApplied = false;
    }

    private static SpriteRenderer[] RemoveNullEntries(
        SpriteRenderer[] source)
    {
        if (source == null || source.Length == 0)
            return new SpriteRenderer[0];

        int validCount = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                validCount++;
        }

        if (validCount == source.Length)
            return source;

        SpriteRenderer[] result =
            new SpriteRenderer[validCount];

        int index = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == null)
                continue;

            result[index] = source[i];
            index++;
        }

        return result;
    }

    private static bool IsUtilityVisualName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        string lower = objectName.ToLowerInvariant();

        return lower.Contains("health") ||
               lower.Contains("hpbar") ||
               lower.Contains("hitbox") ||
               lower.Contains("hurtbox") ||
               lower.Contains("telegraph") ||
               lower.Contains("debug") ||
               lower.Contains("preview") ||
               lower.Contains("shadow");
    }

    private void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        RestoreOriginalMaterials();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();

        if (flashMaterial != null)
            Destroy(flashMaterial);
    }
}
