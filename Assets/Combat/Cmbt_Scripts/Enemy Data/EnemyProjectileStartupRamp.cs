using UnityEngine;

/// <summary>
/// Gives enemy projectiles a readable startup:
/// the whole pattern appears first, creeps slowly, then accelerates to full speed.
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

    private Vector2 direction = Vector2.right;
    private float fullSpeed = 7f;
    private float holdTime = 0.06f;
    private float rampDuration = 0.28f;
    private float initialMultiplier = 0.05f;
    private float easePower = 2f;
    private bool removeWhenReflected = true;

    private float startTime;
    private int speedSourceId;
    private SpeedModifier speedModifier;
    private Rigidbody2D rb;
    private Projectile projectile;

    private bool ownsSpeedModifierSource;

    public void Configure(
        Vector2 projectileDirection,
        float projectileFullSpeed,
        float startupHoldTime,
        float startupRampDuration,
        float startupInitialMultiplier,
        float startupEasePower,
        bool removeStartupWhenReflected)
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
        ApplyCurrentMultiplier();
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

        if (debugElapsed >= holdTime + rampDuration)
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
        if (elapsed < holdTime)
            return initialMultiplier;

        if (rampDuration <= 0f)
            return 1f;

        float t = Mathf.Clamp01(
            (elapsed - holdTime) / rampDuration
        );

        float eased = Mathf.Pow(t, easePower);

        return Mathf.Lerp(
            initialMultiplier,
            1f,
            eased
        );
    }

    private void FinishAndRemove()
    {
        isConfigured = false;
        debugCurrentMultiplier = 1f;

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
        if (speedModifier != null &&
            ownsSpeedModifierSource &&
            speedSourceId != 0)
        {
            speedModifier.RemoveSlow(speedSourceId);
        }

        ownsSpeedModifierSource = false;
    }
}
