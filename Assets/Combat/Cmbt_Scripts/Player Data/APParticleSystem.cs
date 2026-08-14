using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Converts basic-attack AP rewards into physical top-down pickups. A scene
/// instance can be added for Inspector tuning; otherwise one is created with
/// safe defaults the first time a basic attack lands.
/// </summary>
public class APParticleSystem : MonoBehaviour
{
    public static APParticleSystem Instance { get; private set; }

    [Header("Particle Prefab")]
    [Tooltip("Optional override. If empty, the system automatically loads Resources/APParticle.")]
    [SerializeField] private APParticlePickup particlePrefab;

    [Header("Reward Splitting")]
    [Tooltip("Preferred AP value of one physical particle. Rewards are split as evenly as possible.")]
    [SerializeField, Min(1)] private int preferredAPPerParticle = 4;

    [SerializeField, Min(1)] private int maximumParticlesPerBurst = 6;

    [Header("Top-Down Burst Physics")]
    [SerializeField, Min(0f)] private float minimumBurstSpeed = 2.2f;
    [SerializeField, Min(0f)] private float maximumBurstSpeed = 4.6f;
    [SerializeField, Range(0f, 1f)] private float outwardBias = 0.55f;
    [Tooltip("Used only if the AP particle prefab cannot be loaded.")]
    [SerializeField, Min(0f)] private float fallbackLinearDamping = 4.5f;
    [Tooltip("Used only if the AP particle prefab cannot be loaded.")]
    [SerializeField, Min(0.01f)] private float fallbackColliderRadius = 0.09f;

    [Header("Magnetization")]
    [SerializeField, Min(0f)] private float pickupDelay = 0.16f;
    [SerializeField, Min(0f)] private float magnetAcceleration = 34f;
    [SerializeField, Min(0.01f)] private float maximumMagnetSpeed = 12f;
    [SerializeField, Min(0.01f)] private float collectionDistance = 0.24f;

    [Header("Collision Filtering")]
    [Tooltip("AP pickup colliders exclude these layers. EnemyHurtbox is included by default so loose AP cannot lodge against enemies, including enemies spawned later.")]
    [SerializeField] private LayerMask enemyCollisionMask = 1 << 10;

    [Header("Lifetime")]
    [SerializeField, Min(0.1f)] private float particleLifetime = 12f;
    [SerializeField, Min(0f)] private float blinkDuration = 3f;

    [Header("Fallback Visual (Prefab Missing)")]
    [SerializeField] private Color particleColor = new Color(0.25f, 1f, 0.24f, 1f);
    [SerializeField, Min(0.01f)] private float visualSize = 0.16f;
    [Tooltip("Sprite sorting layer used by runtime AP particles. Project Eri combat arenas use Foreground.")]
    [SerializeField] private string sortingLayerName = "Foreground";
    [SerializeField] private int sortingOrder = 80;

    private readonly List<APParticlePickup> activeParticles = new();
    private Sprite runtimeSprite;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveParticlePrefab();
        runtimeSprite = CreateCircularSprite();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (runtimeSprite != null)
        {
            Texture2D texture = runtimeSprite.texture;
            Destroy(runtimeSprite);
            Destroy(texture);
        }
    }

    public static void SpawnReward(
        Vector2 origin,
        int totalAP,
        Vector2 outwardDirection,
        EnemyHealth sourceEnemy = null)
    {
        int resolvedTotalAP = ResolveRewardValue(totalAP);
        if (resolvedTotalAP <= 0)
            return;

        SpawnResolvedReward(
            origin,
            resolvedTotalAP,
            outwardDirection,
            sourceEnemy
        );
    }

    public static void SpawnRewardAcrossEnemies(
        EnemyHealth[] enemies,
        int enemyCount,
        int totalAP,
        Vector2 attackOrigin)
    {
        if (enemies == null || enemyCount <= 0 || totalAP <= 0)
            return;

        int validCount = 0;

        for (int i = 0; i < enemyCount && i < enemies.Length; i++)
        {
            if (enemies[i] != null)
                validCount++;
        }

        if (validCount <= 0)
            return;

        int remainingAP = ResolveRewardValue(totalAP);
        if (remainingAP <= 0)
            return;

        int remainingEnemies = validCount;

        for (int i = 0; i < enemyCount && i < enemies.Length; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null)
                continue;

            int share = Mathf.CeilToInt(
                remainingAP / (float)remainingEnemies
            );

            remainingAP -= share;
            remainingEnemies--;

            Vector2 enemyPosition = enemy.transform.position;
            Vector2 outward = enemyPosition - attackOrigin;

            SpawnResolvedReward(
                enemyPosition,
                share,
                outward,
                enemy
            );
        }
    }

    private static int ResolveRewardValue(int totalAP)
    {
        if (totalAP <= 0)
            return 0;

        APParticleCollector collector = APParticleCollector.Current;
        return collector != null
            ? collector.ResolvePickupValue(totalAP)
            : totalAP;
    }

    private static void SpawnResolvedReward(
        Vector2 origin,
        int resolvedTotalAP,
        Vector2 outwardDirection,
        EnemyHealth sourceEnemy)
    {
        if (resolvedTotalAP <= 0)
            return;

        EnsureInstance().SpawnBurst(
            origin,
            resolvedTotalAP,
            outwardDirection,
            sourceEnemy
        );
    }

    /// <summary>
    /// Tags loose AP near a world-space segment for a temporary, stronger pull
    /// toward the current collector. Used by Dominic's retracting whip without
    /// bypassing the normal AP ownership or collection rules.
    /// </summary>
    public static int PullTowardCollectorAlongSegment(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        float radius,
        float acceleration,
        float maximumSpeed,
        float persistence)
    {
        if (Instance == null || radius <= 0f || persistence <= 0f)
            return 0;

        return Instance.PullParticlesAlongSegment(
            segmentStart,
            segmentEnd,
            radius,
            acceleration,
            maximumSpeed,
            persistence
        );
    }

    private static APParticleSystem EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        APParticleSystem existing =
            FindObjectOfType<APParticleSystem>();

        if (existing != null)
            return existing;

        GameObject root = new GameObject("AP Particle System");
        return root.AddComponent<APParticleSystem>();
    }

    private int PullParticlesAlongSegment(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        float radius,
        float acceleration,
        float maximumSpeed,
        float persistence)
    {
        int pulledCount = 0;
        float radiusSquared = radius * radius;
        Vector2 segment = segmentEnd - segmentStart;
        float segmentLengthSquared = segment.sqrMagnitude;

        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            APParticlePickup pickup = activeParticles[i];

            if (pickup == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            Vector2 point = pickup.Position;
            float along = segmentLengthSquared > 0.0001f
                ? Mathf.Clamp01(
                    Vector2.Dot(point - segmentStart, segment) /
                    segmentLengthSquared
                )
                : 0f;

            Vector2 closest = segmentStart + segment * along;

            if ((point - closest).sqrMagnitude > radiusSquared)
                continue;

            if (pickup.RequestExternalCollectorPull(
                acceleration,
                maximumSpeed,
                persistence))
            {
                pulledCount++;
            }
        }

        return pulledCount;
    }

    private void SpawnBurst(
        Vector2 origin,
        int totalAP,
        Vector2 outwardDirection,
        EnemyHealth sourceEnemy)
    {
        int particleCount =
            SpellActionPointPickupUtility.ResolveParticleCount(
                totalAP,
                preferredAPPerParticle,
                maximumParticlesPerBurst);

        int baseValue = totalAP / particleCount;
        int remainder = totalAP % particleCount;
        Vector2 biasDirection = outwardDirection.sqrMagnitude > 0.0001f
            ? outwardDirection.normalized
            : Vector2.zero;

        for (int i = 0; i < particleCount; i++)
        {
            int value = baseValue + (i < remainder ? 1 : 0);
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            Vector2 launchDirection = Vector2.Lerp(
                randomDirection,
                biasDirection,
                outwardBias
            ).normalized;

            if (launchDirection.sqrMagnitude < 0.0001f)
                launchDirection = Random.insideUnitCircle.normalized;

            float speed = Random.Range(
                Mathf.Min(minimumBurstSpeed, maximumBurstSpeed),
                Mathf.Max(minimumBurstSpeed, maximumBurstSpeed)
            );

            CreateParticle(
                origin,
                value,
                launchDirection * speed,
                sourceEnemy
            );
        }
    }

    private void CreateParticle(
        Vector2 position,
        int value,
        Vector2 initialVelocity,
        EnemyHealth sourceEnemy)
    {
        bool usingPrefab = particlePrefab != null;
        APParticlePickup pickup;
        GameObject particleObject;

        if (usingPrefab)
        {
            pickup = Instantiate(
                particlePrefab,
                new Vector3(position.x, position.y, 0f),
                Quaternion.identity,
                transform
            );

            particleObject = pickup.gameObject;
            particleObject.name = "AP Particle";
        }
        else
        {
            particleObject = new GameObject("AP Particle");
            particleObject.transform.SetParent(transform, true);
            particleObject.transform.position = new Vector3(
                position.x,
                position.y,
                0f
            );

            pickup = particleObject.AddComponent<APParticlePickup>();
        }

        SpriteRenderer renderer =
            particleObject.GetComponent<SpriteRenderer>();

        if (renderer == null)
            renderer = particleObject.AddComponent<SpriteRenderer>();

        // The supplied prefab intentionally uses the generated circle until
        // final art is assigned directly to its SpriteRenderer.
        if (renderer.sprite == null)
            renderer.sprite = runtimeSprite;

        if (!usingPrefab)
        {
            renderer.color = particleColor;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            particleObject.transform.localScale =
                Vector3.one * visualSize;
        }

        Rigidbody2D body = particleObject.GetComponent<Rigidbody2D>();

        if (body == null)
            body = particleObject.AddComponent<Rigidbody2D>();

        CircleCollider2D pickupCollider =
            particleObject.GetComponent<CircleCollider2D>();

        if (pickupCollider == null)
            pickupCollider = particleObject.AddComponent<CircleCollider2D>();

        // Collider layer overrides apply to every present and future enemy on
        // the excluded layer, unlike pairwise IgnoreCollision calls which only
        // know about enemies that already exist at spawn time.
        LayerMask excludedLayers = pickupCollider.excludeLayers;
        excludedLayers.value |= enemyCollisionMask.value;
        pickupCollider.excludeLayers = excludedLayers;

        if (!usingPrefab)
        {
            body.gravityScale = 0f;
            body.linearDamping = fallbackLinearDamping;
            body.angularDamping = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            pickupCollider.radius = fallbackColliderRadius /
                Mathf.Max(visualSize, 0.01f);
        }

        if (sourceEnemy != null)
        {
            Collider2D[] sourceColliders =
                sourceEnemy.GetComponentsInChildren<Collider2D>();

            for (int i = 0; i < sourceColliders.Length; i++)
            {
                if (sourceColliders[i] != null)
                {
                    Physics2D.IgnoreCollision(
                        pickupCollider,
                        sourceColliders[i]
                    );
                }
            }
        }

        APParticleCollector collector = APParticleCollector.Current;

        if (collector != null)
        {
            Collider2D[] playerColliders =
                collector.GetComponentsInChildren<Collider2D>();

            for (int i = 0; i < playerColliders.Length; i++)
            {
                if (playerColliders[i] != null)
                {
                    Physics2D.IgnoreCollision(
                        pickupCollider,
                        playerColliders[i]
                    );
                }
            }
        }

        pickup.Configure(
            this,
            value,
            initialVelocity,
            pickupDelay,
            particleLifetime,
            blinkDuration,
            magnetAcceleration,
            maximumMagnetSpeed,
            collectionDistance
        );

        activeParticles.Add(pickup);
    }

    private void ResolveParticlePrefab()
    {
        if (particlePrefab != null)
            return;

        particlePrefab = Resources.Load<APParticlePickup>("APParticle");

        if (particlePrefab == null)
        {
            Debug.LogWarning(
                "APParticleSystem: Resources/APParticle prefab was not found. " +
                "Using the runtime fallback particle.",
                this
            );
        }
    }

    public void Unregister(APParticlePickup pickup)
    {
        activeParticles.Remove(pickup);
    }

    public void ClearAll()
    {
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            if (activeParticles[i] != null)
                Destroy(activeParticles[i].gameObject);
        }

        activeParticles.Clear();
    }

    private static Sprite CreateCircularSprite()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false
        );

        texture.name = "Runtime AP Particle";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(
            (size - 1) * 0.5f,
            (size - 1) * 0.5f
        );
        float radius = size * 0.46f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    center
                );

                float alpha = Mathf.Clamp01(radius - distance + 1f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );
    }
}
