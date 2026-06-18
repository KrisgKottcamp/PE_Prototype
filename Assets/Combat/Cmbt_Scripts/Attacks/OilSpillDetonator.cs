using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oil spill trigger.
/// 
/// The oil spill does no direct damage while sitting on the ground.
/// If hit by a projectile with PowerShotOilIgniter, it detonates,
/// finds enemies currently inside the oil radius, then applies tick damage over time.
/// 
/// No separate burn-status script required.
/// </summary>
public class OilSpillDetonator : MonoBehaviour
{
    private class BurnHandle
    {
        public MonoBehaviour runner;
        public Coroutine coroutine;
    }

    private static readonly Dictionary<EnemyHealth, BurnHandle> activeBurns = new();

    [Header("Burn Damage Over Time")]
    [SerializeField] private int damagePerTick = 6;
    [SerializeField] private float burnDuration = 3f;
    [SerializeField] private float tickInterval = 0.5f;

    [Tooltip("If true, detonating oil on an enemy that is already burning restarts that burn timer.")]
    [SerializeField] private bool refreshExistingBurn = true;

    [Tooltip("If true, enemies take damage immediately, then continue taking ticks. If false, first tick happens after tickInterval.")]
    [SerializeField] private bool tickImmediately = true;

    [Header("Explosion Area")]
    [SerializeField] private float explosionRadius = 2.5f;
    [SerializeField] private LayerMask enemyMask;
    [SerializeField] private int maxTargets = 32;

    [Header("Ignition")]
    [SerializeField] private bool destroyIgnitingProjectile = true;

    [Tooltip("Use the same CircleCollider2D that AoEZone uses.")]
    [SerializeField] private CircleCollider2D zoneCollider;

    [Header("After Detonation")]
    [Tooltip("Hide the oil visual after it detonates, while burn damage continues from CombatManager.")]
    [SerializeField] private bool hideOilAfterDetonation = true;

    [SerializeField] private SpriteRenderer[] oilRenderersToHide;
    [SerializeField] private Collider2D[] oilCollidersToDisable;

    [Header("VFX")]
    [SerializeField] private GameObject explosionVfxPrefab;
    [SerializeField] private float explosionVfxZ = 0f;

    [Header("Debug")]
    [SerializeField] private bool logDetonation = false;
    [SerializeField] private bool drawGizmos = true;

    private bool hasDetonated;

    private readonly Collider2D[] hitBuffer = new Collider2D[64];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[64];

    private void Awake()
    {
        if (zoneCollider == null)
            zoneCollider = GetComponent<CircleCollider2D>();

        if (oilRenderersToHide == null || oilRenderersToHide.Length == 0)
            oilRenderersToHide = GetComponentsInChildren<SpriteRenderer>(true);

        if (oilCollidersToDisable == null || oilCollidersToDisable.Length == 0)
            oilCollidersToDisable = GetComponentsInChildren<Collider2D>(true);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasDetonated) return;
        if (other == null) return;

        PowerShotOilIgniter igniter = other.GetComponentInParent<PowerShotOilIgniter>();
        if (igniter == null) return;

        Detonate();

        if (destroyIgnitingProjectile)
        {
            PlayerProjectile playerProjectile = igniter.GetComponentInParent<PlayerProjectile>();

            if (playerProjectile != null)
                Destroy(playerProjectile.gameObject);
            else
                Destroy(igniter.gameObject);
        }
    }

    public void Detonate()
    {
        if (hasDetonated) return;

        hasDetonated = true;

        if (logDetonation)
            Debug.Log("Oil spill detonated. Applying tick damage over time.");

        Vector2 center = transform.position;
        float radius = GetExplosionRadius();

        SpawnExplosionVfx(center);
        ApplyBurnToEnemies(center, radius);

        if (hideOilAfterDetonation)
            HideOil();

        // Burn coroutines are run on CombatManager when possible, so the oil can vanish.
        Destroy(gameObject, 0.05f);
    }

    private float GetExplosionRadius()
    {
        if (zoneCollider == null)
            return explosionRadius;

        float scale = Mathf.Max(
            Mathf.Abs(zoneCollider.transform.lossyScale.x),
            Mathf.Abs(zoneCollider.transform.lossyScale.y)
        );

        return Mathf.Max(0.01f, zoneCollider.radius * scale);
    }

    private void ApplyBurnToEnemies(Vector2 center, float radius)
    {
        if (enemyMask.value == 0)
        {
            Debug.LogWarning("OilSpillDetonator: enemyMask is empty. Set it to EnemyHurtbox.");
            return;
        }

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitBuffer, enemyMask);

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null) continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            bool alreadyHitThisDetonation = false;

            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueEnemies[j] == enemy)
                {
                    alreadyHitThisDetonation = true;
                    break;
                }
            }

            if (alreadyHitThisDetonation) continue;

            uniqueEnemies[uniqueCount] = enemy;
            uniqueCount++;

            StartBurn(enemy);

            if (uniqueCount >= uniqueEnemies.Length || uniqueCount >= maxTargets)
                break;
        }
    }

    private void StartBurn(EnemyHealth enemy)
    {
        if (enemy == null) return;

        MonoBehaviour runner = CombatManager.Instance != null
            ? CombatManager.Instance
            : this;

        if (refreshExistingBurn && activeBurns.TryGetValue(enemy, out BurnHandle existing))
        {
            if (existing != null && existing.runner != null && existing.coroutine != null)
                existing.runner.StopCoroutine(existing.coroutine);

            activeBurns.Remove(enemy);
        }
        else if (!refreshExistingBurn && activeBurns.ContainsKey(enemy))
        {
            return;
        }

        Coroutine routine = runner.StartCoroutine(BurnRoutine(enemy));

        activeBurns[enemy] = new BurnHandle
        {
            runner = runner,
            coroutine = routine
        };
    }

    private IEnumerator BurnRoutine(EnemyHealth enemy)
    {
        EnemyHealth key = enemy;

        float elapsed = 0f;
        float safeTickInterval = Mathf.Max(0.05f, tickInterval);
        float safeDuration = Mathf.Max(0.05f, burnDuration);

        if (!tickImmediately)
        {
            yield return new WaitForSeconds(safeTickInterval);
            elapsed += safeTickInterval;
        }

        while (enemy != null && enemy.CurrentHP > 0 && elapsed < safeDuration)
        {
            if (damagePerTick > 0)
                enemy.TakeDamage(damagePerTick);

            yield return new WaitForSeconds(safeTickInterval);
            elapsed += safeTickInterval;
        }

        activeBurns.Remove(key);
    }

    private void HideOil()
    {
        if (oilRenderersToHide != null)
        {
            for (int i = 0; i < oilRenderersToHide.Length; i++)
            {
                if (oilRenderersToHide[i] != null)
                    oilRenderersToHide[i].enabled = false;
            }
        }

        if (oilCollidersToDisable != null)
        {
            for (int i = 0; i < oilCollidersToDisable.Length; i++)
            {
                if (oilCollidersToDisable[i] != null)
                    oilCollidersToDisable[i].enabled = false;
            }
        }
    }

    private void SpawnExplosionVfx(Vector2 center)
    {
        if (explosionVfxPrefab == null) return;

        Vector3 pos = new Vector3(center.x, center.y, explosionVfxZ);
        Instantiate(explosionVfxPrefab, pos, Quaternion.identity);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        float radius = explosionRadius;

        if (zoneCollider != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(zoneCollider.transform.lossyScale.x),
                Mathf.Abs(zoneCollider.transform.lossyScale.y)
            );

            radius = zoneCollider.radius * scale;
        }

        Gizmos.color = new Color(1f, 0.55f, 0.05f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}