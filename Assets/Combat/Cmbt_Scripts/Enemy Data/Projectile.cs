using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared tracker for reflected projectile damage.
/// Created once per PushBack activation and referenced by every reflected projectile.
/// 
/// First hit on an enemy = baseDamage.
/// Later reflected hits on the same enemy during the same PushBack activation are reduced.
/// </summary>
public class ReflectDamageTracker
{
    private readonly int baseDamage;
    private readonly Dictionary<EnemyHealth, int> hitCounts = new();

    public ReflectDamageTracker(int baseDamage)
    {
        this.baseDamage = Mathf.Max(1, baseDamage);
    }

    public int GetDamageFor(EnemyHealth enemy)
    {
        if (!hitCounts.TryGetValue(enemy, out int hits))
            hits = 0;

        hitCounts[enemy] = hits + 1;

        int dmg = baseDamage;

        for (int i = 0; i < hits; i++)
            dmg /= 2;

        return Mathf.Max(1, dmg);
    }
}

public class Projectile : MonoBehaviour
{
    public enum ProjectileTeam
    {
        Enemy,
        Player,
        Neutral
    }

    [Header("Movement")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private float speed = 7f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private float armDelay = 0.03f;

    [Header("Damage")]
    [SerializeField] private int damage = 8;
    [SerializeField] private ProjectileTeam team = ProjectileTeam.Enemy;
    [SerializeField] private bool damageAnyNonOwner = false;
    [SerializeField] private string[] validTargetTags = new[] { "Player", "PlayerCombatPawn" };

    [Header("Collision")]
    [SerializeField] private bool destroyOnSolidHit = true;
    [SerializeField] private LayerMask solidMask;

    [Header("Reflection Behavior")]
    [SerializeField] private bool reflectedProjectilesIgnoreLifetime = true;

    [Tooltip("Color applied when this projectile gets reflected by Push Back.")]
    [SerializeField] private Color reflectedTint = new Color(0.25f, 0.85f, 1f, 1f);

    [SerializeField] private bool tintSpriteRenderersOnReflect = true;
    [SerializeField] private bool tintTrailRenderersOnReflect = true;
    [SerializeField] private bool tintLineRenderersOnReflect = true;

    [Header("Reflected Hitstop")]
    [Tooltip("Applied when a Push Back-reflected projectile damages an enemy.")]
    [SerializeField] private HitstopSettings reflectedHitstop =
        HitstopSettings.Create(0.040f, 0.04f);

    private Vector2 direction = Vector2.right;
    private bool initialized = false;
    private float armedAt = 0f;
    private float destroyAt = float.MaxValue;
    private Transform ownerRoot;
    private ReflectDamageTracker reflectTracker;

    private bool hasBeenReflected = false;
    private SpeedModifier speedModifier;

    public ProjectileTeam Team => team;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        speedModifier = GetComponent<SpeedModifier>();
    }

    private void OnEnable()
    {
        armedAt = Time.time + Mathf.Max(0f, armDelay);
        destroyAt = Time.time + Mathf.Max(0.05f, lifetime);

        hasBeenReflected = false;
        reflectTracker = null;
    }

    public void Initialize(Vector2 dir, float newSpeed)
    {
        Initialize(dir, newSpeed, null, team);
    }

    public void Initialize(Vector2 dir, float newSpeed, GameObject ownerObj, ProjectileTeam projectileTeam)
    {
        direction = dir.sqrMagnitude > 0.0001f
            ? dir.normalized
            : Vector2.right;

        speed = Mathf.Max(0.01f, newSpeed);
        team = projectileTeam;
        ownerRoot = ownerObj != null ? ownerObj.transform.root : null;
        initialized = true;

        ApplyVelocity();
    }

    private void FixedUpdate()
    {
        bool shouldUseLifetime = !hasBeenReflected || !reflectedProjectilesIgnoreLifetime;

        if (shouldUseLifetime && Time.time >= destroyAt)
        {
            Destroy(gameObject);
            return;
        }

        if (!initialized) return;

        float moveSpeed = speed * GetSpeedMultiplier();

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = direction * moveSpeed;
#else
            rb.velocity = direction * moveSpeed;
#endif
        }
        else
        {
            transform.position += (Vector3)(direction * moveSpeed * Time.fixedDeltaTime);
        }
    }

    private float GetSpeedMultiplier()
    {
        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        return speedModifier != null ? speedModifier.Multiplier : 1f;
    }

    private void ApplyVelocity()
    {
        if (rb == null) return;

        float moveSpeed = speed * GetSpeedMultiplier();

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = direction * moveSpeed;
#else
        rb.velocity = direction * moveSpeed;
#endif
    }

    /// <summary>
    /// Called by PushBack.
    /// Reverses direction, changes ownership, changes layer, clears enemy collision ignores,
    /// tints the projectile, and optionally disables lifetime despawn.
    /// </summary>
    public void Reflect(Transform newOwner, int newLayer = -1, ReflectDamageTracker tracker = null)
    {
        hasBeenReflected = true;

        team = team == ProjectileTeam.Enemy
            ? ProjectileTeam.Player
            : ProjectileTeam.Enemy;

        // Push Back sends the projectile back along its original path.
        direction = -direction;

        transform.rotation = Quaternion.FromToRotation(Vector3.right, direction);

        ownerRoot = newOwner != null ? newOwner.root : null;

        // Reflected projectiles should be able to hit enemies.
        damageAnyNonOwner = true;
        reflectTracker = tracker;

        if (newLayer >= 0)
            SetLayerRecursive(gameObject, newLayer);

        ResetEnemyCollisionIgnores();
        ApplyReflectedVisualTint();

        armedAt = Time.time + Mathf.Max(0f, armDelay);

        if (reflectedProjectilesIgnoreLifetime)
            destroyAt = float.MaxValue;
        else
            destroyAt = Time.time + Mathf.Max(0.05f, lifetime);

        ApplyVelocity();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ProcessProjectileHit(other);
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        ProcessProjectileHit(other.collider);
    }

    private void ProcessProjectileHit(Collider2D other)
    {
        if (other == null) return;
        if (Time.time < armedAt) return;

        Transform hitRoot = other.transform.root;

        if (ownerRoot != null && hitRoot == ownerRoot)
            return;

        if (hasBeenReflected)
        {
            ProcessReflectedHit(other);
            return;
        }

        ProcessNormalHit(other);
    }

    private void ProcessReflectedHit(Collider2D other)
    {
        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();

        if (enemy != null)
        {
            if (reflectTracker != null)
            {
                int reflectedDamage = reflectTracker.GetDamageFor(enemy);
                enemy.TakeDamage(reflectedDamage);
            }
            else
            {
                enemy.TakeDamage(damage);
            }

            HitstopManager.Request(reflectedHitstop);

            Destroy(gameObject);
            return;
        }

        if (destroyOnSolidHit && IsSolid(other))
        {
            Destroy(gameObject);
            return;
        }

        // Reflected projectiles ignore random triggers.
        // They only despawn on enemy hit or wall/cover hit.
    }

    private void ProcessNormalHit(Collider2D other)
    {
        bool isValidTarget = damageAnyNonOwner || IsValidTargetTag(other);

        if (isValidTarget)
        {
            if (reflectTracker != null)
                ApplyReflectedDamage(other);
            else
                ApplyDamage(other);

            Destroy(gameObject);
            return;
        }

        if (destroyOnSolidHit && IsSolid(other))
            Destroy(gameObject);
    }

    private bool IsSolid(Collider2D other)
    {
        return (solidMask.value & (1 << other.gameObject.layer)) != 0;
    }

    private bool IsValidTargetTag(Collider2D other)
    {
        if (validTargetTags == null || validTargetTags.Length == 0)
            return false;

        string tagSelf = other.tag;
        string tagRoot = other.transform.root.tag;

        for (int i = 0; i < validTargetTags.Length; i++)
        {
            string t = validTargetTags[i];

            if (string.IsNullOrEmpty(t)) continue;

            if (tagSelf == t || tagRoot == t)
                return true;
        }

        return false;
    }

    private void ApplyDamage(Collider2D other)
    {
        GameObject selfObj = other.gameObject;
        GameObject rootObj = other.transform.root.gameObject;

        selfObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        selfObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
        selfObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);

        if (rootObj != selfObj)
        {
            rootObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            rootObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
            rootObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);
        }
    }

    private void ApplyReflectedDamage(Collider2D other)
    {
        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();

        if (enemy != null)
        {
            int reflectedDamage = reflectTracker != null
                ? reflectTracker.GetDamageFor(enemy)
                : damage;

            enemy.TakeDamage(reflectedDamage);
            HitstopManager.Request(reflectedHitstop);
            return;
        }

        ApplyDamage(other);
    }

    private void ResetEnemyCollisionIgnores()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);

        if (myCols.Length == 0)
            return;

        EnemyHealth[] enemies = FindObjectsOfType<EnemyHealth>(false);

        for (int e = 0; e < enemies.Length; e++)
        {
            if (enemies[e] == null) continue;

            Collider2D[] enemyCols = enemies[e].GetComponentsInChildren<Collider2D>(true);

            for (int i = 0; i < myCols.Length; i++)
            {
                if (myCols[i] == null) continue;

                for (int j = 0; j < enemyCols.Length; j++)
                {
                    if (enemyCols[j] == null) continue;

                    Physics2D.IgnoreCollision(myCols[i], enemyCols[j], false);
                }
            }
        }
    }

    private void ApplyReflectedVisualTint()
    {
        if (tintSpriteRenderersOnReflect)
        {
            SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>(true);

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;

                Color c = reflectedTint;
                c.a = sprites[i].color.a;
                sprites[i].color = c;
            }
        }

        if (tintTrailRenderersOnReflect)
        {
            TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);

            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] == null) continue;

                Color start = reflectedTint;
                Color end = reflectedTint;

                start.a = trails[i].startColor.a;
                end.a = trails[i].endColor.a;

                trails[i].startColor = start;
                trails[i].endColor = end;
            }
        }

        if (tintLineRenderersOnReflect)
        {
            LineRenderer[] lines = GetComponentsInChildren<LineRenderer>(true);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;

                Color start = reflectedTint;
                Color end = reflectedTint;

                start.a = lines[i].startColor.a;
                end.a = lines[i].endColor.a;

                lines[i].startColor = start;
                lines[i].endColor = end;
            }
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
