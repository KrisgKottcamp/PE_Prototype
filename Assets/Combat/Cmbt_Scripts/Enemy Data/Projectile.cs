using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared tracker for reflected projectile damage. Created once per PushBack activation,
/// referenced by every reflected projectile. Halves damage per successive hit on the
/// same enemy (floor division, minimum 1).
/// </summary>
public class ReflectDamageTracker
{
    private readonly int baseDamage;
    private readonly Dictionary<EnemyHealth, int> hitCounts = new();

    public ReflectDamageTracker(int baseDamage)
    {
        this.baseDamage = Mathf.Max(1, baseDamage);
    }

    /// <summary>
    /// Returns the damage for the next hit on this enemy and increments the counter.
    /// First hit = baseDamage, then halved each time (integer division), minimum 1.
    /// </summary>
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

    private Vector2 direction = Vector2.right;
    private bool initialized = false;
    private float armedAt = 0f;
    private float destroyAt = float.MaxValue;
    private Transform ownerRoot;
    private ReflectDamageTracker reflectTracker;

    /// <summary>Current team. Use Reflect() to flip.</summary>
    public ProjectileTeam Team => team;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        armedAt = Time.time + Mathf.Max(0f, armDelay);
        destroyAt = Time.time + Mathf.Max(0.05f, lifetime);
    }

    // Backward-compatible initialize
    public void Initialize(Vector2 dir, float newSpeed)
    {
        Initialize(dir, newSpeed, null, team);
    }

    // Preferred initialize from shooter
    public void Initialize(Vector2 dir, float newSpeed, GameObject ownerObj, ProjectileTeam projectileTeam)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        speed = Mathf.Max(0.01f, newSpeed);
        team = projectileTeam;
        ownerRoot = ownerObj != null ? ownerObj.transform.root : null;
        initialized = true;
        ApplyVelocity();
    }

    private void FixedUpdate()
    {
        if (Time.time >= destroyAt) { Destroy(gameObject); return; }

        if (!initialized) return;

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = direction * speed;
#else
            rb.velocity = direction * speed;
#endif
        }
        else
        {
            transform.position += (Vector3)(direction * speed * Time.fixedDeltaTime);
        }
    }

    private void ApplyVelocity()
    {
        if (rb == null) return;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = direction * speed;
#else
        rb.velocity = direction * speed;
#endif
    }

    /// <summary>
    /// Flips this projectile's team, reverses its direction, sets the new owner
    /// so it won't damage them, and enables damageAnyNonOwner so it hits enemies.
    /// Optionally changes the layer so the projectile can physically collide with enemies.
    /// Clears all IgnoreCollision pairs with enemies so reflected bullets can hit
    /// even the enemy that originally fired them.
    /// Called by PushBack to turn enemy bullets against them.
    /// </summary>
    public void Reflect(Transform newOwner, int newLayer = -1, ReflectDamageTracker tracker = null)
    {
        // Flip team
        team = (team == ProjectileTeam.Enemy) ? ProjectileTeam.Player : ProjectileTeam.Enemy;

        // Reverse direction
        direction = -direction;

        // Rotate to face the new direction
        transform.rotation = Quaternion.FromToRotation(Vector3.right, direction);

        // Set new owner so the reflected projectile won't damage the player
        ownerRoot = newOwner != null ? newOwner.root : null;

        // Hit anything that isn't the new owner (i.e. enemies)
        damageAnyNonOwner = true;

        // Shared diminishing-damage tracker (null = use projectile's own damage)
        reflectTracker = tracker;

        // Change layer so Physics2D generates collisions with enemy hurtboxes
        if (newLayer >= 0)
            SetLayerRecursive(gameObject, newLayer);

        // Clear IgnoreCollision pairs that EnemyShooterDebug set at spawn time.
        // Without this, reflected projectiles pass through the enemy that fired them.
        ResetEnemyCollisionIgnores();

        // Brief arm delay so it doesn't collide with nearby objects at the instant of reflection
        armedAt = Time.time + Mathf.Max(0f, armDelay);

        // Reset lifetime so the reflected projectile has full flight time
        destroyAt = Time.time + Mathf.Max(0.05f, lifetime);

        // Apply reversed velocity immediately
        ApplyVelocity();
    }

    private void ResetEnemyCollisionIgnores()
    {
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>(true);
        if (myCols.Length == 0) return;

        var enemies = FindObjectsOfType<EnemyHealth>(false);
        for (int e = 0; e < enemies.Length; e++)
        {
            if (enemies[e] == null) continue;
            Collider2D[] enemyCols = enemies[e].GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < myCols.Length; i++)
                for (int j = 0; j < enemyCols.Length; j++)
                    if (myCols[i] != null && enemyCols[j] != null)
                        Physics2D.IgnoreCollision(myCols[i], enemyCols[j], false);
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleHit(other);
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        HandleHit(other.collider);
    }

    private void HandleHit(Collider2D other)
    {
        if (other == null) return;
        if (Time.time < armedAt) return;

        Transform hitRoot = other.transform.root;

        // Ignore owner
        if (ownerRoot != null && hitRoot == ownerRoot) return;

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

        if (destroyOnSolidHit && ((solidMask.value & (1 << other.gameObject.layer)) != 0))
        {
            Destroy(gameObject);
        }
    }

    private bool IsValidTargetTag(Collider2D other)
    {
        if (validTargetTags == null || validTargetTags.Length == 0) return false;

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

        // Try common damage entry points
        selfObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

        selfObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);

        selfObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);
    }

    private void ApplyReflectedDamage(Collider2D other)
    {
        var enemy = other.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            int dmg = reflectTracker.GetDamageFor(enemy);
            enemy.TakeDamage(dmg);
            return;
        }

        // Hit something without EnemyHealth (cover, etc.) — fall back to normal
        ApplyDamage(other);
    }
}