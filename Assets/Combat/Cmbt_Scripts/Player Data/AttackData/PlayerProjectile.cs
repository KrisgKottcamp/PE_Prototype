using UnityEngine;
using ProjectEri.SkillSystemV2;

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
    private BasicAttackReactionSettings basicAttackReaction;
    private CameraShakeSettings cameraShakeOnEnemyHit;

    [Header("Projectile Breaking")]
    [Tooltip("If true, destroys enemy projectiles on contact and keeps flying. Enable on the Power Shot prefab only if desired.")]
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
    [Tooltip("Prevents the projectile's normal trigger collision from hitting enemies behind the committed aim direction when the projectile first spawns. This is off by default and only needed for special skill projectiles.")]
    [SerializeField] private bool protectAgainstRearSpawnHits = false;

    [Tooltip("How far the projectile travels before normal collision no longer needs the strict spawn-direction check.")]
    [SerializeField] private float spawnProtectionTravelDistance = 0.45f;

    [Tooltip("During spawn protection, enemy centers must be at least this aligned with the shot direction. Usually match the close-range aim dot.")]
    [Range(0f, 1f)]
    [SerializeField] private float spawnCollisionMinimumAimDot = 0.8f;

    [Header("Spawn Wall/Cover Clip Protection")]
    [Tooltip("Off by default so normal basic projectiles such as Phil's are not affected. ProjectileShooter arms this only for Power Shot-style skill projectiles.")]
    [SerializeField] private bool protectAgainstSpawnObstacleClips = false;

    [Tooltip("Solid wall/cover layers ignored only when they are side/back spawn clips during the first small distance of travel.")]
    [SerializeField] private LayerMask spawnObstacleProtectionMask;

    [Tooltip("How far the projectile may travel before side-wall spawn clip protection expires.")]
    [SerializeField] private float spawnObstacleProtectionTravelDistance = 0.55f;

    [Tooltip("If the obstacle lies this far forward in the shot direction, it is treated as a real wall hit and is not ignored.")]
    [Range(-1f, 1f)]
    [SerializeField] private float spawnObstacleForwardDotThreshold = 0.55f;

    [Tooltip("Obstacle contacts closer than this forward distance from the muzzle are treated as side/back spawn clips, not real forward wall hits.")]
    [SerializeField] private float spawnObstacleMinimumForwardDistance = 0.05f;

    [Tooltip("Prints whether a first-frame wall/cover contact was ignored or accepted.")]
    [SerializeField] private bool logSpawnObstacleProtection = false;

    [Header("Debug")]
    [SerializeField] private bool logHits = false;

    private Rigidbody2D rb;
    private Vector2 dir;
    private SpeedModifier speedModifier;

    private int ownerCharacterIndex = -1;
    private bool awardApOnHit = true;

    private float momentumGainOnHit;
    private bool startsActiveScoringOnHit;
    private HitstopSettings hitstopOnEnemyHit;

    private Vector2 launchOrigin;
    private bool hasLaunchOrigin;
    private bool hitResolved;

    private Vector2 spawnObstacleLaunchOrigin;
    private Vector2 spawnObstacleLaunchDirection = Vector2.up;

    private readonly RaycastHit2D[] closeRangeHits =
        new RaycastHit2D[24];

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        speedModifier = GetComponent<SpeedModifier>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode =
            CollisionDetectionMode2D.Continuous;
    }

    /// <summary>
    /// Called by ProjectileShooter before Fire().
    /// Preserves the true muzzle position even though the projectile may be
    /// instantiated forward for safe wall-hug spawning.
    /// </summary>
    public void SetLaunchOrigin(Vector2 origin)
    {
        launchOrigin = origin;
        hasLaunchOrigin = true;
    }

    /// <summary>
    /// Compatibility entry point. This arms temporary obstacle-clip protection
    /// only when a shooter explicitly calls it. The prefab default remains off,
    /// so normal projectile basics such as Phil's are unaffected.
    /// </summary>
    public void SetSpawnObstacleProtection(
        LayerMask obstacleMask,
        float travelDistance)
    {
        ArmSpawnObstacleClipProtection(
            obstacleMask,
            hasLaunchOrigin ? launchOrigin : (Vector2)transform.position,
            dir.sqrMagnitude > 0.0001f ? dir : Vector2.up,
            travelDistance,
            spawnObstacleForwardDotThreshold,
            spawnObstacleMinimumForwardDistance
        );
    }

    public void ArmSpawnObstacleClipProtection(
        LayerMask obstacleMask,
        Vector2 origin,
        Vector2 direction,
        float travelDistance,
        float forwardDotThreshold,
        float minimumForwardDistance)
    {
        if (obstacleMask.value == 0)
            return;

        protectAgainstSpawnObstacleClips = true;
        spawnObstacleProtectionMask = obstacleMask;
        spawnObstacleLaunchOrigin = origin;
        spawnObstacleLaunchDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.up;
        spawnObstacleProtectionTravelDistance = Mathf.Max(0f, travelDistance);
        spawnObstacleForwardDotThreshold = Mathf.Clamp(forwardDotThreshold, -1f, 1f);
        spawnObstacleMinimumForwardDistance = Mathf.Max(0f, minimumForwardDistance);
    }

    public void Fire(
        Vector2 direction,
        int ownerIndex,
        int dmg,
        float stun,
        float projectileSpeed,
        float life,
        LayerMask mask,
        bool awardAp,
        float momentumGain = 0f,
        bool startActiveScoringOnHit = false,
        HitstopSettings hitstop = default,
        CameraShakeSettings cameraShake = default)
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
        momentumGainOnHit = Mathf.Max(0f, momentumGain);
        startsActiveScoringOnHit = startActiveScoringOnHit;
        hitstopOnEnemyHit = hitstop;
        cameraShakeOnEnemyHit = cameraShake;

        if (!hasLaunchOrigin)
        {
            launchOrigin = transform.position;
            hasLaunchOrigin = true;
        }

        if (protectAgainstSpawnObstacleClips &&
            spawnObstacleLaunchDirection.sqrMagnitude <= 0.0001f)
        {
            spawnObstacleLaunchOrigin = launchOrigin;
            spawnObstacleLaunchDirection = dir;
        }

        if (enableCloseRangeAimAssist &&
            TryResolveCloseRangeEnemy())
        {
            return;
        }

        Destroy(gameObject, lifetime);
    }

    public void ConfigureBasicAttackReaction(
        BasicAttackReactionSettings reaction)
    {
        basicAttackReaction = reaction;
    }

    private void FixedUpdate()
    {
        if (hitResolved)
            return;

        rb.MovePosition(
            rb.position +
            dir * speed * GetSpeedMultiplier() * Time.fixedDeltaTime
        );
    }

    private float GetSpeedMultiplier()
    {
        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        return speedModifier != null
            ? Mathf.Max(0f, speedModifier.Multiplier)
            : 1f;
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

            if (!IsEnemyClearlyInAimDirection(
                    col,
                    minimumForwardAimDot,
                    out float aimDot))
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
                (Mathf.Approximately(candidateDistance, bestDistance) &&
                 aimDot > bestAimDot))
            {
                bestEnemy = enemy;
                bestCollider = col;
                bestDistance = candidateDistance;
                bestAimDot = aimDot;
            }
        }

        if (bestEnemy == null)
            return false;

        LogAssist(
            $"Accepted {bestEnemy.name}. " +
            $"Distance={bestDistance:0.00}, AimDot={bestAimDot:0.00}"
        );

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

        float forwardTravel = Vector2.Dot(
            (Vector2)transform.position - launchOrigin,
            dir
        );

        return forwardTravel <=
               Mathf.Max(0f, spawnProtectionTravelDistance);
    }

    private bool IsInsideSpawnObstacleProtectionWindow()
    {
        if (!protectAgainstSpawnObstacleClips ||
            spawnObstacleProtectionMask.value == 0)
        {
            return false;
        }

        Vector2 activeDir = spawnObstacleLaunchDirection.sqrMagnitude > 0.0001f
            ? spawnObstacleLaunchDirection.normalized
            : dir;

        float forwardTravel = Vector2.Dot(
            (Vector2)transform.position - spawnObstacleLaunchOrigin,
            activeDir
        );

        return forwardTravel <=
               Mathf.Max(0f, spawnObstacleProtectionTravelDistance);
    }

    private bool ShouldIgnoreSpawnObstacleClip(Collider2D obstacleCollider)
    {
        if (!IsInsideSpawnObstacleProtectionWindow() ||
            obstacleCollider == null)
        {
            return false;
        }

        if (((1 << obstacleCollider.gameObject.layer) &
             spawnObstacleProtectionMask.value) == 0)
        {
            return false;
        }

        Vector2 activeDir = spawnObstacleLaunchDirection.sqrMagnitude > 0.0001f
            ? spawnObstacleLaunchDirection.normalized
            : dir;

        Vector2 closestPoint = obstacleCollider.ClosestPoint(spawnObstacleLaunchOrigin);
        Vector2 toObstacle = closestPoint - spawnObstacleLaunchOrigin;

        if (toObstacle.sqrMagnitude <= 0.000001f)
            toObstacle = (Vector2)obstacleCollider.bounds.center - spawnObstacleLaunchOrigin;

        float forwardDistance = Vector2.Dot(toObstacle, activeDir);
        float alignment = toObstacle.sqrMagnitude > 0.000001f
            ? Vector2.Dot(activeDir, toObstacle.normalized)
            : -1f;

        bool directForwardWall =
            forwardDistance >= spawnObstacleMinimumForwardDistance &&
            alignment >= spawnObstacleForwardDotThreshold;

        if (directForwardWall)
        {
            if (logSpawnObstacleProtection)
            {
                Debug.Log(
                    $"PlayerProjectile: accepted direct wall hit '{obstacleCollider.name}'. " +
                    $"Forward={forwardDistance:0.00}, Dot={alignment:0.00}",
                    this
                );
            }

            return false;
        }

        if (logSpawnObstacleProtection)
        {
            Debug.Log(
                $"PlayerProjectile: ignored side/back spawn clip '{obstacleCollider.name}'. " +
                $"Forward={forwardDistance:0.00}, Dot={alignment:0.00}",
                this
            );
        }

        return true;
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
                $"The enemy is behind or outside the committed aim cone. " +
                $"AimDot={aimDot:0.00}"
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

        PlayerSpellV2Bridge ownerBridge =
            FindFirstObjectByType<PlayerSpellV2Bridge>();
        if (ownerBridge != null && SpellDeflectionUtility.TryDeflect(
                other.gameObject,
                ownerBridge.gameObject,
                dir))
        {
            hitResolved = true;
            Destroy(gameObject);
            return;
        }

        if (breaksEnemyProjectiles)
        {
            Projectile enemyProj = other.GetComponentInParent<Projectile>();

            if (enemyProj != null &&
                enemyProj.Team == Projectile.ProjectileTeam.Enemy)
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
            if (ShouldRejectSpawnOverlapEnemy(other))
                return;

            ResolveEnemyHit(enemyHealth, other);
            return;
        }

        if (ShouldIgnoreSpawnObstacleClip(other))
            return;

        if (logHits)
        {
            Debug.Log(
                $"PlayerProjectile destroyed by {other.name} on layer " +
                $"{LayerMask.LayerToName(other.gameObject.layer)}.",
                this
            );
        }

        hitResolved = true;
        Destroy(gameObject);
    }

    private void ResolveEnemyHit(
        EnemyHealth enemyHealth,
        Collider2D hitCollider)
    {
        if (hitResolved || enemyHealth == null)
            return;

        hitResolved = true;

        // EnemyHealth normally flashes when damage is accepted. Phil's basic
        // should still provide visual hit confirmation when tuned to 0 damage.
        if (damage <= 0)
            enemyHealth.PlayHitFlash();

        float receivedMultiplier = SpellStatModifierUtility.Evaluate(
            enemyHealth.gameObject,
            SpellActorStat.DamageReceived,
            1f);
        enemyHealth.TakeDamage(Mathf.Max(
            0,
            Mathf.RoundToInt(damage * receivedMultiplier)));
        HitstopManager.Request(hitstopOnEnemyHit);

        CombatCameraShake.Request(
            cameraShakeOnEnemyHit,
            enemyHealth.transform.position,
            dir
        );

        EnemyStunnable stunnable = hitCollider != null
            ? hitCollider.GetComponentInParent<EnemyStunnable>()
            : enemyHealth.GetComponentInParent<EnemyStunnable>();

        if (stunnable != null)
        {
            if (basicAttackReaction.enabled)
                basicAttackReaction.Apply(stunnable);
            else
                stunnable.Stun(stunSeconds);
        }

        SpawnApIfAllowed(enemyHealth);
        GrantMomentumIfAllowed();
        Destroy(gameObject);
    }

    private void GrantMomentumIfAllowed()
    {
        if (momentumGainOnHit <= 0f)
            return;

        AttackMomentumManager manager = AttackMomentumManager.Instance;

        if (manager == null)
            return;

        if (startsActiveScoringOnHit)
            manager.RegisterSuccessfulSkill(momentumGainOnHit);
        else
            manager.RegisterMomentum(momentumGainOnHit);
    }

    private void SpawnApIfAllowed(EnemyHealth enemy)
    {
        if (!awardApOnHit)
            return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.party == null)
            return;

        if (ownerCharacterIndex < 0 ||
            ownerCharacterIndex >= pm.party.Count)
        {
            return;
        }

        PartyManager.CharacterState owner = pm.party[ownerCharacterIndex];

        if (owner == null || owner.def == null)
            return;

        int gain = Mathf.Max(0, owner.def.apGainOnBasicHit);

        Vector2 origin = enemy != null
            ? (Vector2)enemy.transform.position
            : (Vector2)transform.position;

        APParticleSystem.SpawnReward(
            origin,
            gain,
            dir,
            enemy
        );
    }

    private void LogAssist(string message)
    {
        if (logCloseRangeAssist)
            Debug.Log($"PlayerProjectile: {message}", this);
    }
}
