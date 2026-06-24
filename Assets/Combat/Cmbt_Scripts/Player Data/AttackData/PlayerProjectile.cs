using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerProjectile : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 10f;
    [SerializeField] private float lifetime = 2.5f;

    [Header("Hit")]
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private int damage = 3;
    [SerializeField] private float stunSeconds = 0.15f;

    [Header("Projectile Breaking")]
    [Tooltip("If true, destroys enemy projectiles on contact and keeps flying. Enable on the Power Shot prefab.")]
    [SerializeField] private bool breaksEnemyProjectiles = false;

    [Header("Close-Range Aim Assist")]
    [Tooltip("Enable this on Power Shot only. Performs a narrow forward sweep from the muzzle so enemies intentionally aimed at can be hit even when the projectile spawns past them.")]
    [SerializeField] private bool enableCloseRangeAimAssist = false;

    [Tooltip("How far forward from the muzzle the close-range sweep checks.")]
    [SerializeField] private float closeRangeAssistDistance = 1f;

    [Tooltip("Thickness of the close-range forward sweep. Keep this fairly narrow.")]
    [SerializeField] private float closeRangeAssistRadius = 0.18f;

    [Tooltip("How closely the enemy must line up with the aim direction. 0.8 means the enemy must be clearly in front of the cursor direction.")]
    [Range(0f, 1f)]
    [SerializeField] private float minimumForwardAimDot = 0.8f;

    [Tooltip("Walls and cover that can block the close-range assist.")]
    [SerializeField] private LayerMask closeRangeObstacleMask;

    [Tooltip("Prints which close enemy was accepted or rejected.")]
    [SerializeField] private bool logCloseRangeAssist = false;

    [Header("Spawn Overlap Protection")]
    [Tooltip("Prevents the projectile's normal trigger collision from hitting enemies behind the committed aim direction when the projectile first spawns.")]
    [SerializeField] private bool protectAgainstRearSpawnHits = true;

    [Tooltip("How far the projectile travels before normal collision no longer needs the strict spawn-direction check.")]
    [SerializeField] private float spawnProtectionTravelDistance = 0.45f;

    [Tooltip("During spawn protection, enemy centers must be at least this aligned with the shot direction. Usually match the close-range aim dot.")]
    [Range(0f, 1f)]
    [SerializeField] private float spawnCollisionMinimumAimDot = 0.8f;

    private Rigidbody2D rb;
    private Vector2 dir;

    private int ownerCharacterIndex = -1;
    private bool awardApOnHit = true;

    private Vector2 launchOrigin;
    private bool hasLaunchOrigin;
    private bool hitResolved;

    private readonly RaycastHit2D[] closeRangeHits = new RaycastHit2D[24];

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    /// <summary>
    /// Called by ProjectileShooter before Fire().
    /// Preserves the true muzzle position even though the projectile is instantiated forward.
    /// </summary>
    public void SetLaunchOrigin(Vector2 origin)
    {
        launchOrigin = origin;
        hasLaunchOrigin = true;
    }

    public void Fire(
        Vector2 direction,
        int ownerIndex,
        int dmg,
        float stun,
        float projectileSpeed,
        float life,
        LayerMask mask,
        bool awardAp)
    {
        dir = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.up;

        ownerCharacterIndex = ownerIndex;
        damage = Mathf.Max(0, dmg);
        stunSeconds = Mathf.Max(0f, stun);
        speed = Mathf.Max(0.01f, projectileSpeed);
        lifetime = Mathf.Max(0.05f, life);
        hitMask = mask;
        awardApOnHit = awardAp;

        if (!hasLaunchOrigin)
        {
            launchOrigin = transform.position;
            hasLaunchOrigin = true;
        }

        if (enableCloseRangeAimAssist && TryResolveCloseRangeEnemy())
            return;

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (hitResolved)
            return;

        rb.MovePosition(rb.position + dir * speed * Time.fixedDeltaTime);
    }

    private bool TryResolveCloseRangeEnemy()
    {
        float distance = Mathf.Max(0f, closeRangeAssistDistance);
        float radius = Mathf.Max(0.01f, closeRangeAssistRadius);

        if (distance <= 0f || hitMask.value == 0)
            return false;

        int hitCount = Physics2D.CircleCastNonAlloc(
            launchOrigin,
            radius,
            dir,
            closeRangeHits,
            distance,
            hitMask
        );

        EnemyHealth bestEnemy = null;
        Collider2D bestCollider = null;
        float bestDistance = float.PositiveInfinity;
        float bestAimDot = -1f;

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = closeRangeHits[i].collider;
            if (col == null)
                continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null)
                continue;

            if (!IsEnemyClearlyInAimDirection(col, minimumForwardAimDot, out float aimDot))
            {
                LogAssist($"Rejected {enemy.name}: outside committed aim direction.");
                continue;
            }

            if (ObstacleBlocksEnemy(col))
            {
                LogAssist($"Rejected {enemy.name}: obstacle blocks muzzle line.");
                continue;
            }

            float candidateDistance = Mathf.Max(0f, closeRangeHits[i].distance);

            if (candidateDistance < bestDistance ||
                (Mathf.Approximately(candidateDistance, bestDistance) && aimDot > bestAimDot))
            {
                bestEnemy = enemy;
                bestCollider = col;
                bestDistance = candidateDistance;
                bestAimDot = aimDot;
            }
        }

        if (bestEnemy == null)
            return false;

        LogAssist($"Accepted {bestEnemy.name}. Distance={bestDistance:0.00}, AimDot={bestAimDot:0.00}");
        ResolveEnemyHit(bestEnemy, bestCollider);
        return true;
    }

    private bool IsEnemyClearlyInAimDirection(
        Collider2D enemyCollider,
        float requiredAimDot,
        out float aimDot)
    {
        aimDot = -1f;

        if (enemyCollider == null)
            return false;

        Vector2 enemyCenter = enemyCollider.bounds.center;
        Vector2 toEnemy = enemyCenter - launchOrigin;

        if (toEnemy.sqrMagnitude <= 0.0001f)
            return false;

        // The enemy's center must actually be in front of Audrey's committed aim.
        float forwardDistance = Vector2.Dot(toEnemy, dir);
        if (forwardDistance <= 0.01f)
            return false;

        aimDot = Vector2.Dot(dir, toEnemy.normalized);
        return aimDot >= requiredAimDot;
    }

    private bool IsInsideSpawnProtectionWindow()
    {
        if (!protectAgainstRearSpawnHits || !hasLaunchOrigin)
            return false;

        float forwardTravel = Vector2.Dot((Vector2)transform.position - launchOrigin, dir);
        return forwardTravel <= Mathf.Max(0f, spawnProtectionTravelDistance);
    }

    private bool ShouldRejectSpawnOverlapEnemy(Collider2D enemyCollider)
    {
        if (!IsInsideSpawnProtectionWindow())
            return false;

        bool clearlyAimed = IsEnemyClearlyInAimDirection(
            enemyCollider,
            spawnCollisionMinimumAimDot,
            out float aimDot
        );

        if (!clearlyAimed)
        {
            EnemyHealth enemy = enemyCollider != null
                ? enemyCollider.GetComponentInParent<EnemyHealth>()
                : null;

            string enemyName = enemy != null ? enemy.name : "Unknown enemy";
            LogAssist(
                $"Ignored spawn-overlap collision with {enemyName}. " +
                $"The enemy is behind or outside the committed aim cone. AimDot={aimDot:0.00}"
            );

            return true;
        }

        return false;
    }

    private bool ObstacleBlocksEnemy(Collider2D enemyCollider)
    {
        if (closeRangeObstacleMask.value == 0 || enemyCollider == null)
            return false;

        Vector2 enemyCenter = enemyCollider.bounds.center;
        Vector2 toEnemy = enemyCenter - launchOrigin;
        float distance = toEnemy.magnitude;

        if (distance <= 0.001f)
            return false;

        float obstacleRadius = Mathf.Max(0.01f, closeRangeAssistRadius * 0.45f);

        RaycastHit2D block = Physics2D.CircleCast(
            launchOrigin,
            obstacleRadius,
            toEnemy / distance,
            distance,
            closeRangeObstacleMask
        );

        return block.collider != null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hitResolved || other == null)
            return;

        if (breaksEnemyProjectiles)
        {
            Projectile enemyProj = other.GetComponentInParent<Projectile>();

            if (enemyProj != null && enemyProj.Team == Projectile.ProjectileTeam.Enemy)
            {
                Destroy(enemyProj.gameObject);
                return;
            }
        }

        if (((1 << other.gameObject.layer) & hitMask.value) == 0)
            return;

        EnemyHealth enemyHealth = other.GetComponentInParent<EnemyHealth>();

        if (enemyHealth != null)
        {
            // This closes the loophole left by the earlier fix:
            // normal trigger collisions at spawn must obey the same committed
            // cursor direction as the close-range assist.
            if (ShouldRejectSpawnOverlapEnemy(other))
                return;

            ResolveEnemyHit(enemyHealth, other);
            return;
        }

        hitResolved = true;
        Destroy(gameObject);
    }

    private void ResolveEnemyHit(EnemyHealth enemyHealth, Collider2D hitCollider)
    {
        if (hitResolved || enemyHealth == null)
            return;

        hitResolved = true;
        enemyHealth.TakeDamage(damage);

        EnemyStunnable stunnable = hitCollider != null
            ? hitCollider.GetComponentInParent<EnemyStunnable>()
            : enemyHealth.GetComponentInParent<EnemyStunnable>();

        if (stunnable != null)
            stunnable.Stun(stunSeconds);

        GrantApIfAllowed();
        Destroy(gameObject);
    }

    private void GrantApIfAllowed()
    {
        if (!awardApOnHit)
            return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.party == null)
            return;

        if (pm.activeIndex != ownerCharacterIndex)
            return;

        if (ownerCharacterIndex < 0 || ownerCharacterIndex >= pm.party.Count)
            return;

        PartyManager.CharacterState owner = pm.party[ownerCharacterIndex];

        if (owner == null || owner.def == null)
            return;

        int maxAP = Mathf.Max(0, owner.def.maxAP);
        int gain = Mathf.Max(0, owner.def.apGainOnBasicHit);
        owner.currentAP = Mathf.Clamp(owner.currentAP + gain, 0, maxAP);
    }

    private void LogAssist(string message)
    {
        if (logCloseRangeAssist)
            Debug.Log($"PlayerProjectile: {message}", this);
    }
}
