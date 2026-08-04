using UnityEngine;

/// <summary>
/// Deliberately lightweight projectile for Eri's occasional finishing shot.
/// It awards no AP, momentum, hitstop, or camera shake.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriSupportBolt : MonoBehaviour
{
    private static Sprite cachedSprite;
    private static Material cachedTrailMaterial;

    private Vector2 direction;
    private float speed;
    private float expiresAt;
    private Vector2 travelOrigin;
    private float maximumTravelDistanceSquared;
    private int damage;
    private LayerMask collisionMask;
    private bool resolved;
    private Vector3 baseVisualScale;
    private SpriteRenderer coreRenderer;
    private TrailRenderer trailRenderer;

    public static void Spawn(
        Vector2 origin,
        Vector2 direction,
        int damage,
        float speed,
        float lifetime,
        LayerMask collisionMask,
        Color color,
        float visualSize)
    {
        GameObject boltObject =
            new GameObject("Eri Support Bolt");

        boltObject.transform.position = origin;

        int projectileLayer =
            LayerMask.NameToLayer("PlayerProjectile");

        if (projectileLayer >= 0)
            boltObject.layer = projectileLayer;

        SpriteRenderer renderer =
            boltObject.AddComponent<SpriteRenderer>();

        renderer.sprite = GetOrCreateSprite();
        renderer.color =
            Color.Lerp(color, Color.white, 0.28f);
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = 26;

        visualSize =
            Mathf.Max(0.08f, visualSize);

        boltObject.transform.localScale =
            new Vector3(
                visualSize * 1.35f,
                visualSize,
                1f
            );

        GameObject glowObject =
            new GameObject("Glow");

        glowObject.transform.SetParent(
            boltObject.transform,
            false
        );

        SpriteRenderer glow =
            glowObject.AddComponent<SpriteRenderer>();

        Color glowColor = color;
        glowColor.a *= 0.32f;
        glow.sprite = GetOrCreateSprite();
        glow.color = glowColor;
        glow.sortingLayerName = "Foreground";
        glow.sortingOrder = 25;
        glowObject.transform.localScale =
            new Vector3(2.35f, 2.35f, 1f);

        EriSupportBolt bolt =
            boltObject.AddComponent<EriSupportBolt>();

        bolt.coreRenderer = renderer;
        bolt.baseVisualScale =
            boltObject.transform.localScale;
        bolt.direction =
            direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.up;
        bolt.damage = Mathf.Max(1, damage);
        bolt.speed = Mathf.Max(0.1f, speed);
        float safeLifetime = Mathf.Max(0.1f, lifetime);
        bolt.expiresAt = Time.time + safeLifetime;
        bolt.travelOrigin = origin;
        float maximumTravelDistance =
            bolt.speed * safeLifetime * 1.25f + 0.5f;
        bolt.maximumTravelDistanceSquared =
            maximumTravelDistance * maximumTravelDistance;
        bolt.collisionMask = collisionMask;
        bolt.trailRenderer =
            AddTrail(
                boltObject,
                color,
                visualSize
            );

        float angle =
            Mathf.Atan2(
                bolt.direction.y,
                bolt.direction.x
            ) * Mathf.Rad2Deg;

        boltObject.transform.rotation =
            Quaternion.Euler(0f, 0f, angle);
    }

    private void Update()
    {
        if (resolved)
            return;

        // Keep expiry independent from physics collision resolution. This also
        // protects bolts that have already left every collider in the arena.
        if (Time.time >= expiresAt)
        {
            Resolve();
            return;
        }

        float pulse =
            1f +
            Mathf.Sin(Time.time * 24f) *
            0.07f;

        transform.localScale =
            baseVisualScale * pulse;
    }

    private void FixedUpdate()
    {
        if (resolved)
            return;

        if (Time.time >= expiresAt)
        {
            Resolve();
            return;
        }

        Vector2 current = transform.position;

        if ((current - travelOrigin).sqrMagnitude >=
            maximumTravelDistanceSquared)
        {
            Resolve();
            return;
        }

        float distance =
            speed * Time.fixedDeltaTime;

        if (collisionMask.value != 0)
        {
            RaycastHit2D hit =
                Physics2D.CircleCast(
                    current,
                    0.08f,
                    direction,
                    distance,
                    collisionMask
                );

            if (hit.collider != null)
            {
                EnemyHealth enemy =
                    hit.collider.
                        GetComponentInParent<EnemyHealth>();

                if (enemy != null)
                    enemy.TakeDamage(damage);

                Resolve();
                return;
            }
        }

        transform.position =
            current + direction * distance;
    }

    private void Resolve()
    {
        if (resolved)
            return;

        resolved = true;

        if (coreRenderer != null)
            coreRenderer.enabled = false;

        Transform glow =
            transform.Find("Glow");

        if (glow != null)
            glow.gameObject.SetActive(false);

        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
            Destroy(
                gameObject,
                Mathf.Max(0.05f, trailRenderer.time)
            );
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private static TrailRenderer AddTrail(
        GameObject boltObject,
        Color color,
        float visualSize)
    {
        Material material =
            GetOrCreateTrailMaterial();

        if (material == null)
            return null;

        TrailRenderer trail =
            boltObject.AddComponent<TrailRenderer>();

        trail.material = material;
        trail.time = 0.16f;
        trail.minVertexDistance = 0.025f;
        trail.startWidth = visualSize * 0.62f;
        trail.endWidth = 0f;
        trail.numCapVertices = 2;
        trail.numCornerVertices = 2;
        trail.alignment =
            LineAlignment.TransformZ;
        trail.textureMode =
            LineTextureMode.Stretch;
        trail.sortingLayerName = "Foreground";
        trail.sortingOrder = 24;

        Gradient gradient = new Gradient();

        Color trailStart = color;
        trailStart.a = 0.72f;

        Color trailEnd = color;
        trailEnd.a = 0f;

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(
                    Color.Lerp(
                        color,
                        Color.white,
                        0.20f
                    ),
                    0f
                ),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(
                    trailStart.a,
                    0f
                ),
                new GradientAlphaKey(
                    trailEnd.a,
                    1f
                )
            }
        );

        trail.colorGradient = gradient;
        return trail;
    }

    private static Material GetOrCreateTrailMaterial()
    {
        if (cachedTrailMaterial != null)
            return cachedTrailMaterial;

        Shader shader =
            Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader =
                Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit"
                );
        }

        if (shader == null)
            return null;

        cachedTrailMaterial =
            new Material(shader);
        cachedTrailMaterial.name =
            "EriSupportBoltTrail_Runtime";

        return cachedTrailMaterial;
    }

    private static Sprite GetOrCreateSprite()
    {
        if (cachedSprite != null)
            return cachedSprite;

        const int size = 16;
        Texture2D texture =
            new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false
            );

        texture.name = "EriSupportBolt_Runtime";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels =
            new Color[size * size];

        Vector2 center =
            new Vector2(
                (size - 1) * 0.5f,
                (size - 1) * 0.5f
            );

        float radius = size * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance =
                    Vector2.Distance(
                        new Vector2(x, y),
                        center
                    );

                float alpha =
                    Mathf.Clamp01(
                        1f - distance / radius
                    );

                alpha *= alpha;
                pixels[y * size + x] =
                    new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        cachedSprite =
            Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size
            );

        cachedSprite.name = "EriSupportBolt_Runtime";
        return cachedSprite;
    }
}
