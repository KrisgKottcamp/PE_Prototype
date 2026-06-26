using System;
using System.Collections;
using UnityEngine;

public class EnemyAttackTelegraph : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer[] spriteRenderers;

    [Header("Fallback Profile")]
    [SerializeField] private EnemyTelegraphProfile fallbackProfile;

    private Coroutine activeTelegraph;
    private Vector3 originalLocalPosition;
    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;
    private Color[] originalColors;

    public bool IsTelegraphing => activeTelegraph != null;

    private void Awake()
    {
        if (visualRoot == null) visualRoot = transform;
        if (spriteRenderers == null || spriteRenderers.Length == 0)
            spriteRenderers = visualRoot.GetComponentsInChildren<SpriteRenderer>(true);
        CacheOriginalValues();
    }

    private void OnDisable() => CancelTelegraph();

    private void CacheOriginalValues()
    {
        originalLocalPosition = visualRoot.localPosition;
        originalLocalScale = visualRoot.localScale;
        originalLocalRotation = visualRoot.localRotation;
        originalColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            if (spriteRenderers[i] != null) originalColors[i] = spriteRenderers[i].color;
    }

    public IEnumerator PlayTelegraphRoutine(EnemyTelegraphProfile profile, Vector2 lockedAttackDirection)
    {
        bool finished = false;
        PlayTelegraph(profile, lockedAttackDirection, () => finished = true);
        while (!finished) yield return null;
    }

    public void PlayTelegraph(EnemyTelegraphProfile profile, Vector2 lockedAttackDirection, Action onComplete)
    {
        if (activeTelegraph != null) StopCoroutine(activeTelegraph);
        EnemyTelegraphProfile resolved = profile != null ? profile : fallbackProfile;
        if (resolved == null) { onComplete?.Invoke(); return; }
        activeTelegraph = StartCoroutine(TelegraphRoutine(resolved, lockedAttackDirection, onComplete));
    }

    public void CancelTelegraph()
    {
        if (activeTelegraph != null) { StopCoroutine(activeTelegraph); activeTelegraph = null; }
        RestoreVisuals();
    }

    private IEnumerator TelegraphRoutine(EnemyTelegraphProfile profile, Vector2 lockedAttackDirection, Action onComplete)
    {
        float duration = Mathf.Max(0.01f, profile.duration);
        float elapsed = 0f;
        Vector2 direction = lockedAttackDirection.sqrMagnitude > 0.0001f ? lockedAttackDirection.normalized : Vector2.right;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            UpdatePosition(profile, elapsed, normalized, direction);
            UpdateScale(profile, normalized);
            UpdateRotation(profile, elapsed, normalized);
            UpdateFlash(profile, elapsed, normalized);
            yield return null;
        }
        RestoreVisuals();
        activeTelegraph = null;
        onComplete?.Invoke();
    }

    private void UpdatePosition(EnemyTelegraphProfile profile, float elapsed, float normalized, Vector2 direction)
    {
        Vector3 offset = Vector3.zero;
        if (profile.useShake)
        {
            float ramp = Mathf.Pow(normalized, Mathf.Max(0.01f, profile.shakeRampPower));
            float intensity = profile.shakeAmount * ramp;
            offset += new Vector3(Mathf.Sin(elapsed * profile.shakeFrequency) * intensity,
                                  Mathf.Cos(elapsed * profile.shakeFrequency * 1.37f) * intensity, 0f);
        }
        if (profile.useDirectionalLean)
        {
            float lean = Mathf.Sin(normalized * Mathf.PI) * profile.directionalLeanDistance;
            offset += (Vector3)(-direction * lean);
        }
        visualRoot.localPosition = originalLocalPosition + offset;
    }

    private void UpdateScale(EnemyTelegraphProfile profile, float normalized)
    {
        if (!profile.useScalePulse) { visualRoot.localScale = originalLocalScale; return; }
        float pulse = Mathf.Abs(Mathf.Sin(normalized * Mathf.PI * Mathf.Max(0.25f, profile.scalePulseCount)));
        Vector3 target = Vector3.Scale(originalLocalScale, profile.scaleMultiplier);
        visualRoot.localScale = Vector3.Lerp(originalLocalScale, target, pulse);
    }

    private void UpdateRotation(EnemyTelegraphProfile profile, float elapsed, float normalized)
    {
        if (!profile.useRotationWobble) { visualRoot.localRotation = originalLocalRotation; return; }
        float angle = Mathf.Sin(elapsed * profile.rotationWobbleFrequency * Mathf.PI * 2f)
                      * profile.rotationWobbleDegrees * Mathf.Lerp(0.35f, 1f, normalized);
        visualRoot.localRotation = originalLocalRotation * Quaternion.Euler(0f, 0f, angle);
    }

    private void UpdateFlash(EnemyTelegraphProfile profile, float elapsed, float normalized)
    {
        if (!profile.useFlash) return;
        float wave = profile.flashFrequency <= 0f ? 1f : Mathf.InverseLerp(-1f, 1f,
            Mathf.Sin(elapsed * profile.flashFrequency * Mathf.PI * 2f));
        float strength = Mathf.Lerp(0.1f, profile.maximumFlashStrength, normalized) * wave;
        for (int i = 0; i < spriteRenderers.Length; i++)
            if (spriteRenderers[i] != null)
                spriteRenderers[i].color = Color.Lerp(originalColors[i], profile.warningColor, strength);
    }

    private void RestoreVisuals()
    {
        if (visualRoot != null)
        {
            visualRoot.localPosition = originalLocalPosition;
            visualRoot.localScale = originalLocalScale;
            visualRoot.localRotation = originalLocalRotation;
        }
        if (spriteRenderers == null || originalColors == null) return;
        for (int i = 0; i < spriteRenderers.Length; i++)
            if (spriteRenderers[i] != null && i < originalColors.Length)
                spriteRenderers[i].color = originalColors[i];
    }
}
