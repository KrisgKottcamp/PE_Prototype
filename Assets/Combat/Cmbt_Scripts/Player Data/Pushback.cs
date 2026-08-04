using System.Collections;
using UnityEngine;

public class PushBack : MonoBehaviour
{
    [Header("AoE")]
    [SerializeField] private float radius = 3f;
    [SerializeField] private LayerMask enemyMask;

    [Header("Knockback")]
    [SerializeField] private float knockbackForce = 12f;
    [SerializeField] private float knockbackDuration = 0.3f;

    [Header("Projectile Reflect")]
    [SerializeField] private LayerMask reflectedProjectileLayer;
    [SerializeField] private int reflectedDamageBase = 10;

    [Header("Bubble VFX")]
    [SerializeField] private GameObject bubblePrefab;
    [SerializeField] private float bubbleFadeDuration = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private readonly Collider2D[] hitBuffer = new Collider2D[32];
    private readonly KnockbackReceiver2D[] dedup =
        new KnockbackReceiver2D[32];

    public bool Execute()
    {
        return Execute(Vector2.up, 360f);
    }

    public bool Execute(Vector2 aimDirection, float coneAngle)
    {
        Vector2 center = transform.position;
        Vector2 direction =
            aimDirection.sqrMagnitude > 0.0001f
                ? aimDirection.normalized
                : Vector2.up;

        // A zero angle preserves the original radial behavior for older skill
        // assets that have not opted into a directional cone.
        float resolvedConeAngle =
            coneAngle > 0.01f
                ? Mathf.Clamp(coneAngle, 0f, 360f)
                : 360f;

        SpawnBubble(center);

        int pushedEnemies = PushEnemies(
            center,
            direction,
            resolvedConeAngle
        );
        int reflectedProjectiles = ReflectProjectiles(
            center,
            direction,
            resolvedConeAngle
        );

        return pushedEnemies > 0 || reflectedProjectiles > 0;
    }

    private int PushEnemies(
        Vector2 center,
        Vector2 aimDirection,
        float coneAngle)
    {
        int count = Physics2D.OverlapCircleNonAlloc(
            center,
            radius,
            hitBuffer,
            enemyMask
        );

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];

            if (col == null)
                continue;

            KnockbackReceiver2D knockback =
                col.GetComponentInParent<KnockbackReceiver2D>();

            if (knockback == null)
                continue;

            if (!IsInsideAimCone(
                center,
                knockback.transform.position,
                aimDirection,
                coneAngle))
            {
                continue;
            }

            bool alreadyHit = false;

            for (int j = 0; j < uniqueCount; j++)
            {
                if (dedup[j] == knockback)
                {
                    alreadyHit = true;
                    break;
                }
            }

            if (alreadyHit)
                continue;

            if (uniqueCount >= dedup.Length)
                break;

            dedup[uniqueCount] = knockback;
            uniqueCount++;

            Vector2 direction =
                (Vector2)knockback.transform.position - center;

            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.up;
            else
                direction.Normalize();

            knockback.ApplyKnockback(
                direction,
                knockbackForce,
                knockbackDuration
            );
        }

        return uniqueCount;
    }

    private int ReflectProjectiles(
        Vector2 center,
        Vector2 aimDirection,
        float coneAngle)
    {
        int layer = LayerFromMask(reflectedProjectileLayer);

        ReflectDamageTracker tracker =
            new ReflectDamageTracker(reflectedDamageBase);

        Projectile[] projectiles =
            FindObjectsOfType<Projectile>(false);

        int reflectedCount = 0;

        for (int i = 0; i < projectiles.Length; i++)
        {
            Projectile projectile = projectiles[i];

            if (projectile == null ||
                projectile.Team != Projectile.ProjectileTeam.Enemy)
            {
                continue;
            }

            float distance = Vector2.Distance(
                center,
                projectile.transform.position
            );

            if (distance > radius)
                continue;

            if (!IsInsideAimCone(
                center,
                projectile.transform.position,
                aimDirection,
                coneAngle))
            {
                continue;
            }

            projectile.Reflect(transform, layer, tracker);
            reflectedCount++;
        }

        return reflectedCount;
    }

    private static bool IsInsideAimCone(
        Vector2 center,
        Vector2 target,
        Vector2 aimDirection,
        float coneAngle)
    {
        if (coneAngle >= 359.9f)
            return true;

        Vector2 offset = target - center;

        if (offset.sqrMagnitude < 0.0001f)
            return true;

        float minimumDot = Mathf.Cos(
            coneAngle * 0.5f * Mathf.Deg2Rad
        );

        return Vector2.Dot(
            offset.normalized,
            aimDirection
        ) >= minimumDot;
    }

    private static int LayerFromMask(LayerMask mask)
    {
        int value = mask.value;

        if (value == 0)
            return -1;

        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }

        return -1;
    }

    private void SpawnBubble(Vector2 center)
    {
        if (bubblePrefab == null)
            return;

        GameObject bubble = Instantiate(
            bubblePrefab,
            center,
            Quaternion.identity,
            transform
        );

        StartCoroutine(FadeBubble(bubble));
    }

    private IEnumerator FadeBubble(GameObject bubble)
    {
        if (bubble == null)
            yield break;

        SpriteRenderer sprite =
            bubble.GetComponentInChildren<SpriteRenderer>();

        if (sprite == null)
        {
            Destroy(bubble);
            yield break;
        }

        Color color = sprite.color;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, bubbleFadeDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            color.a = 1f - Mathf.Clamp01(elapsed / duration);
            sprite.color = color;
            yield return null;
        }

        Destroy(bubble);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
