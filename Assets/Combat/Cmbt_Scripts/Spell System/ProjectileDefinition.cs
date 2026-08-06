using UnityEngine;

[CreateAssetMenu(menuName = "Game/Spells/Projectile Definition")]
public class ProjectileDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;

    [Header("Shape")]
    [Tooltip("Sprite used for the projectile body. A default circle is generated if null.")]
    public Sprite sprite;
    [Tooltip("Size of the projectile in world units.")]
    public Vector2 size = new Vector2(0.2f, 0.2f);
    [Tooltip("Collider radius. Matches half the smallest size axis by default.")]
    public float colliderRadius = 0.1f;

    [Header("Color")]
    public Color color = Color.white;
    [Tooltip("Emission color for glow. Set alpha to 0 to disable.")]
    public Color emissionColor = new Color(1f, 1f, 1f, 0f);

    [Header("Trail")]
    public bool hasTrail = true;
    [Tooltip("Trail length in seconds.")]
    public float trailTime = 0.15f;
    public float trailStartWidth = 0.12f;
    public float trailEndWidth = 0f;
    public Color trailStartColor = Color.white;
    public Color trailEndColor = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Trail material. Uses default sprite material if null.")]
    public Material trailMaterial;

    [Header("Animation")]
    [Tooltip("Animator controller for the projectile. Leave null for static sprite.")]
    public RuntimeAnimatorController animatorController;
    [Tooltip("Spin speed in degrees per second. 0 = face travel direction.")]
    public float spinSpeed;

    [Header("Impact")]
    [Tooltip("VFX prefab spawned on hit/destroy.")]
    public GameObject impactVfxPrefab;
    [Tooltip("How long the impact VFX lives.")]
    public float impactVfxLifetime = 0.5f;

    [Header("Sorting")]
    public string sortingLayerName = "Foreground";
    public int sortingOrder = 10;

    [Header("Physics Layer")]
    [Tooltip("Physics layer name for the projectile GameObject. Auto-resolved to 'Projectile' or 'PlayerProjectile' if empty.")]
    public string physicsLayerName = "";

    [Header("Collision Interactions")]
    [Tooltip("Projectile interacts with enemies (EnemyHurtbox layer).")]
    public bool interactWithEnemies = true;
    [Tooltip("Projectile interacts with terrain (Terrain layer).")]
    public bool interactWithTerrain;
    [Tooltip("Projectile interacts with walls (Obstacles / Walls layers).")]
    public bool interactWithWalls;
    [Tooltip("Projectile interacts with objects (Objects layer).")]
    public bool interactWithObjects;
    [Tooltip("Projectile interacts with interactables (Interactables layer).")]
    public bool interactWithInteractables;

    public GameObject CreateInstance(Vector2 position, float angleDeg)
    {
        GameObject obj = new GameObject(string.IsNullOrEmpty(displayName) ? "SpellProjectile" : displayName);
        obj.transform.position = position;
        obj.transform.rotation = Quaternion.Euler(0, 0, angleDeg);

        ResolvePhysicsLayer(obj);

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite != null ? sprite : CreateDefaultSprite();
        sr.color = color;
        sr.sortingLayerName = ResolveSortingLayer();
        sr.sortingOrder = sortingOrder;
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);

        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.radius = colliderRadius / Mathf.Max(0.01f, Mathf.Min(size.x, size.y));
        col.isTrigger = true;

        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (hasTrail)
        {
            TrailRenderer trail = obj.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.startWidth = trailStartWidth;
            trail.endWidth = trailEndWidth;
            trail.startColor = trailStartColor.a > 0f ? trailStartColor : color;
            trail.endColor = trailEndColor;
            trail.material = trailMaterial != null ? trailMaterial
                : new Material(Shader.Find("Sprites/Default"));
            trail.sortingLayerName = ResolveSortingLayer();
            trail.sortingOrder = sortingOrder - 1;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.minVertexDistance = 0.02f;
        }

        if (animatorController != null)
        {
            Animator animator = obj.AddComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
        }

        return obj;
    }

    public LayerMask BuildCollisionMask()
    {
        int mask = 0;

        if (interactWithEnemies)
            mask |= AddLayer("EnemyHurtbox");
        if (interactWithTerrain)
            mask |= AddLayer("Terrain");
        if (interactWithWalls)
        {
            mask |= AddLayer("Obstacles");
            mask |= AddLayer("Walls");
        }
        if (interactWithObjects)
            mask |= AddLayer("Objects");
        if (interactWithInteractables)
            mask |= AddLayer("Interactables");

        return mask;
    }

    private static int AddLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? 1 << layer : 0;
    }

    private void ResolvePhysicsLayer(GameObject obj)
    {
        if (!string.IsNullOrEmpty(physicsLayerName))
        {
            int layer = LayerMask.NameToLayer(physicsLayerName);
            if (layer >= 0)
            {
                obj.layer = layer;
                return;
            }
        }

        string[] candidates = { "Projectile", "PlayerProjectile" };
        for (int i = 0; i < candidates.Length; i++)
        {
            int layer = LayerMask.NameToLayer(candidates[i]);
            if (layer >= 0)
            {
                obj.layer = layer;
                return;
            }
        }
    }

    private string ResolveSortingLayer()
    {
        if (SortingLayer.NameToID(sortingLayerName) != 0 ||
            sortingLayerName == "Default")
            return sortingLayerName;

        string[] candidates = { "Foreground", "VFX", "Characters" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (SortingLayer.NameToID(candidates[i]) != 0)
                return candidates[i];
        }

        return "Default";
    }

    public void SpawnImpactVfx(Vector2 position)
    {
        if (impactVfxPrefab == null)
            return;

        GameObject vfx = Instantiate(impactVfxPrefab, position, Quaternion.identity);
        if (impactVfxLifetime > 0f)
            Destroy(vfx, impactVfxLifetime);
    }

    private static Sprite defaultSpriteCache;

    public static Sprite CreateFallbackSprite()
    {
        return CreateDefaultSprite();
    }

    private static Sprite CreateDefaultSprite()
    {
        if (defaultSpriteCache != null)
            return defaultSpriteCache;

        int res = 32;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float center = (res - 1) * 0.5f;
        float outerR = center;
        float innerR = center * 0.7f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                if (dist > outerR)
                    tex.SetPixel(x, y, Color.clear);
                else if (dist <= innerR)
                    tex.SetPixel(x, y, Color.white);
                else
                {
                    float a = 1f - (dist - innerR) / (outerR - innerR);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
        }

        tex.Apply();
        defaultSpriteCache = Sprite.Create(tex,
            new Rect(0, 0, res, res),
            new Vector2(0.5f, 0.5f), res);

        return defaultSpriteCache;
    }
}
