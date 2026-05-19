using System.Collections;
using UnityEngine;

/// <summary>
/// Imogen's Push Back skill.
///
/// AoE centered on the player pawn:
///   1. Spawns a bubble sprite that fades out over bubbleFadeDuration.
///   2. Knocks back every enemy in radius via KnockbackReceiver2D.
///   3. Reflects every enemy-team Projectile in radius (flips team,
///      reverses direction, enables damageAnyNonOwner so it hurts enemies).
///
/// Lives on the combat pawn. Called by CombatSkillSystem when a skill
/// with executionType == SkillExecutionType.PushBack is resolved.
/// </summary>
public class PushBack : MonoBehaviour
{
    [Header("AoE")]
    [SerializeField] private float radius = 3f;
    [SerializeField] private LayerMask enemyMask;

    [Header("Knockback")]
    [SerializeField] private float knockbackForce = 12f;
    [SerializeField] private float knockbackDuration = 0.3f;

    [Header("Projectile Reflect")]
    [Tooltip("Layer to reassign reflected projectiles to. Must collide with EnemyHurtbox " +
             "in your Physics2D collision matrix. Use the same layer as PlayerProjectile. " +
             "Pick exactly one layer.")]
    [SerializeField] private LayerMask reflectedProjectileLayer;

    [Tooltip("Base damage of the first reflected projectile to hit an enemy. " +
             "Each subsequent hit on the same enemy deals half (floored), minimum 1.")]
    [SerializeField] private int reflectedDamageBase = 10;

    [Header("Bubble VFX")]
    [Tooltip("Prefab with a SpriteRenderer. Spawned on the player, fades out, then destroyed.")]
    [SerializeField] private GameObject bubblePrefab;
    [SerializeField] private float bubbleFadeDuration = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private readonly Collider2D[] hitBuffer = new Collider2D[32];
    private readonly KnockbackReceiver2D[] dedup = new KnockbackReceiver2D[32];

    // --------------------------------------------------
    // Public API (called by CombatSkillSystem)
    // --------------------------------------------------

    public void Execute()
    {
        Vector2 center = (Vector2)transform.position;

        SpawnBubble(center);
        PushEnemies(center);
        ReflectProjectiles(center);
    }

    // --------------------------------------------------
    // Enemy knockback
    // --------------------------------------------------

    private void PushEnemies(Vector2 center)
    {
        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitBuffer, enemyMask);
        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            var col = hitBuffer[i];
            if (col == null) continue;

            var knockback = col.GetComponentInParent<KnockbackReceiver2D>();
            if (knockback == null) continue;

            // Deduplicate — one push per enemy even if multiple colliders overlap
            bool already = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (dedup[j] == knockback) { already = true; break; }
            }
            if (already) continue;
            if (uniqueCount < dedup.Length)
                dedup[uniqueCount++] = knockback;

            Vector2 dir = (Vector2)knockback.transform.position - center;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir = dir.normalized;

            knockback.ApplyKnockback(dir, knockbackForce, knockbackDuration);
        }
    }

    // --------------------------------------------------
    // Projectile reflection
    // --------------------------------------------------

    private void ReflectProjectiles(Vector2 center)
    {
        int layer = LayerFromMask(reflectedProjectileLayer);
        var tracker = new ReflectDamageTracker(reflectedDamageBase);
        var projectiles = FindObjectsOfType<Projectile>(false);

        for (int i = 0; i < projectiles.Length; i++)
        {
            var proj = projectiles[i];
            if (proj == null) continue;
            if (proj.Team != Projectile.ProjectileTeam.Enemy) continue;

            float dist = Vector2.Distance(center, (Vector2)proj.transform.position);
            if (dist > radius) continue;

            proj.Reflect(transform, layer, tracker);
        }
    }

    // --------------------------------------------------
    // Helpers
    // --------------------------------------------------

    /// <summary>Extracts a single layer index from a LayerMask. Returns -1 if empty.</summary>
    private static int LayerFromMask(LayerMask mask)
    {
        int val = mask.value;
        if (val == 0) return -1;
        for (int i = 0; i < 32; i++)
            if ((val & (1 << i)) != 0) return i;
        return -1;
    }

    // --------------------------------------------------
    // Bubble VFX
    // --------------------------------------------------

    private void SpawnBubble(Vector2 center)
    {
        if (bubblePrefab == null) return;

        // Parent to pawn so the bubble follows the player while fading
        GameObject bubble = Instantiate(bubblePrefab, center, Quaternion.identity, transform);
        StartCoroutine(FadeBubble(bubble));
    }

    private IEnumerator FadeBubble(GameObject bubble)
    {
        if (bubble == null) yield break;

        var sr = bubble.GetComponentInChildren<SpriteRenderer>();
        if (sr == null)
        {
            Destroy(bubble);
            yield break;
        }

        Color c = sr.color;
        float elapsed = 0f;

        while (elapsed < bubbleFadeDuration)
        {
            elapsed += Time.deltaTime;
            c.a = 1f - Mathf.Clamp01(elapsed / bubbleFadeDuration);
            sr.color = c;
            yield return null;
        }

        Destroy(bubble);
    }

    // --------------------------------------------------
    // Gizmos
    // --------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawSphere(transform.position, 0.1f);
    }
#endif
}