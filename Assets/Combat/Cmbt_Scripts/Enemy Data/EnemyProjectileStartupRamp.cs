using UnityEngine;

/// <summary>
/// Gives enemy projectiles a readable startup:
/// the whole pattern fades in while stationary, holds briefly, then accelerates
/// to full speed.
///
/// This intentionally uses the existing SpeedModifier path when a Projectile
/// component is present, because Project Eri's Projectile.FixedUpdate already
/// multiplies its speed by SpeedModifier.Multiplier.
///
/// If a projectile prefab does not have Projectile.cs but does have a Rigidbody2D,
/// this script also applies a fallback Rigidbody2D velocity ramp.
/// </summary>
[DisallowMultipleComponent]
public class EnemyProjectileStartupRamp : MonoBehaviour
{
    [Header("Runtime Debug")]
    [SerializeField] private bool isConfigured;
    [SerializeField] private float debugCurrentMultiplier = 1f;
    [SerializeField] private float debugElapsed;
    [SerializeField] private float debugCurrentAlphaMultiplier = 1f;

    private Vector2 direction = Vector2.right;
    private float fullSpeed = 7f;
    private float holdTime = 0.06f;
    private float rampDuration = 0.28f;
    private float initialMultiplier = 0.05f;
    private float easePower = 2f;
    private bool removeWhenReflected = true;
    private bool useVisualFade = true;
    private float visualFadeDuration = 0.18f;
    private float initialVisualAlpha = 0.1f;
    private bool waitForVisualFadeBeforeMovement = true;

    private float startTime;
    private int speedSourceId;
    private SpeedModifier speedModifier;
    private Rigidbody2D rb;
    private Projectile projectile;
    private SpriteRenderer[] visualRenderers;
    private Color[] originalVisualColors;

    private bool ownsSpeedModifierSource;
    private bool visualColorsCaptured;

    public void Configure(
        Vector2 projectileDirection,
        float projectileFullSpeed,
        float startupHoldTime,
        float startupRampDuration,
        float startupInitialMultiplier,
        float startupEasePower,
        bool removeStartupWhenReflected,
        bool fadeVisualsIn,
        float fadeDuration,
        float startingAlpha,
        bool waitForFadeBeforeMovement)
    {
        direction =
            projectileDirection.sqrMagnitude > 0.0001f
                ? projectileDirection.normalized
                : Vector2.right;

        fullSpeed = Mathf.Max(0.01f, projectileFullSpeed);
        holdTime = Mathf.Max(0f, startupHoldTime);
        rampDuration = Mathf.Max(0f, startupRampDuration);
        initialMultiplier = Mathf.Clamp01(startupInitialMultiplier);
        easePower = Mathf.Max(0.1f, startupEasePower);
        removeWhenReflected = removeStartupWhenReflected;
        useVisualFade = fadeVisualsIn;
        visualFadeDuration = Mathf.Max(0.01f, fadeDuration);
        initialVisualAlpha = Mathf.Clamp01(startingAlpha);
        waitForVisualFadeBeforeMovement = waitForFadeBeforeMovement;

        startTime = Time.time;
        isConfigured = true;

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (projectile == null)
            projectile = GetComponent<Projectile>();

        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        if (speedModifier == null)
            speedModifier = gameObject.AddComponent<SpeedModifier>();

        if (speedSourceId == 0)
            speedSourceId = SpeedModifier.GenerateSourceId();

        ownsSpeedModifierSource = true;
        CaptureVisualColors();
        ApplyCurrentMultiplier();
        ApplyVisualFade();
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        projectile = GetComponent<Projectile>();
        speedModifier = GetComponent<SpeedModifier>();
    }

    private void Update()
    {
        if (!isConfigured)
            return;

        if (removeWhenReflected &&
            projectile != null &&
            projectile.Team != Projectile.ProjectileTeam.Enemy)
        {
            FinishAndRemove();
            return;
        }

        ApplyCurrentMultiplier();
        ApplyVisualFade();

        float startupDuration =
            GetMovementStartDelay() +
            holdTime +
            rampDuration;

        if (useVisualFade)
            startupDuration = Mathf.Max(startupDuration, visualFadeDuration);

        if (debugElapsed >= startupDuration)
            FinishAndRemove();
    }

    private void FixedUpdate()
    {
        if (!isConfigured)
            return;

        // Normal Project Eri Projectile.cs controls its own Rigidbody velocity,
        // so the SpeedModifier is enough. This fallback is only for odd prefab
        // variants that use a Rigidbody2D without Projectile.cs.
        if (projectile != null || rb == null)
            return;

        Vector2 velocity =
            direction * fullSpeed * debugCurrentMultiplier;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif
    }

    private void ApplyCurrentMultiplier()
    {
        debugElapsed = Mathf.Max(0f, Time.time - startTime);
        debugCurrentMultiplier = CalculateMultiplier(debugElapsed);

        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        if (speedModifier == null)
            speedModifier = gameObject.AddComponent<SpeedModifier>();

        if (speedSourceId == 0)
            speedSourceId = SpeedModifier.GenerateSourceId();

        speedModifier.ApplySlow(
            speedSourceId,
            debugCurrentMultiplier
        );

        ownsSpeedModifierSource = true;
    }

    private float CalculateMultiplier(float elapsed)
    {
        float movementStartTime =
            GetMovementStartDelay() + holdTime;

        // During the reveal and readable hold, the projectile is spawned but has
        // not been fired yet. This guarantees the player sees the threat before
        // it can travel into them at close range.
        if (elapsed < movementStartTime)
            return 0f;

        if (rampDuration <= 0f)
            return 1f;

        float t = Mathf.Clamp01(
            (elapsed - movementStartTime) / rampDuration
        );

        float eased = Mathf.Pow(t, easePower);

        return Mathf.Lerp(
            initialMultiplier,
            1f,
            eased
        );
    }

    private float GetMovementStartDelay()
    {
        return useVisualFade && waitForVisualFadeBeforeMovement
            ? visualFadeDuration
            : 0f;
    }

    private void CaptureVisualColors()
    {
        visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalVisualColors = new Color[visualRenderers.Length];

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            if (visualRenderers[i] != null)
                originalVisualColors[i] = visualRenderers[i].color;
        }

        visualColorsCaptured = true;
    }

    private void ApplyVisualFade()
    {
        if (!visualColorsCaptured)
            CaptureVisualColors();

        float t = useVisualFade
            ? Mathf.Clamp01(debugElapsed / visualFadeDuration)
            : 1f;

        t = Mathf.SmoothStep(0f, 1f, t);
        debugCurrentAlphaMultiplier = Mathf.Lerp(initialVisualAlpha, 1f, t);

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            SpriteRenderer renderer = visualRenderers[i];
            if (renderer == null)
                continue;

            Color color = originalVisualColors[i];
            color.a *= debugCurrentAlphaMultiplier;
            renderer.color = color;
        }
    }

    private void RestoreVisualColors()
    {
        if (!visualColorsCaptured || visualRenderers == null || originalVisualColors == null)
            return;

        for (int i = 0; i < visualRenderers.Length && i < originalVisualColors.Length; i++)
        {
            if (visualRenderers[i] != null)
                visualRenderers[i].color = originalVisualColors[i];
        }

        debugCurrentAlphaMultiplier = 1f;
    }

    private void FinishAndRemove()
    {
        isConfigured = false;
        debugCurrentMultiplier = 1f;
        RestoreVisualColors();

        if (speedModifier != null &&
            ownsSpeedModifierSource &&
            speedSourceId != 0)
        {
            speedModifier.RemoveSlow(speedSourceId);
        }

        ownsSpeedModifierSource = false;

        if (projectile == null && rb != null)
        {
            Vector2 velocity = direction * fullSpeed;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = velocity;
#else
            rb.velocity = velocity;
#endif
        }

        Destroy(this);
    }

    private void OnDisable()
    {
        RestoreVisualColors();

        if (speedModifier != null &&
            ownsSpeedModifierSource &&
            speedSourceId != 0)
        {
            speedModifier.RemoveSlow(speedSourceId);
        }

        ownsSpeedModifierSource = false;
    }
}
