using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private float lifetime = 2.5f;

    [SerializeField] private LayerMask hitMask;
    [SerializeField] private int damage = 3;
    [SerializeField] private float stunSeconds = 0.15f;

    [Header("Spawn Safety")]
    [Tooltip("Prevents the projectile from damaging enemies immediately on spawn.")]
    [SerializeField] private float enemyHitArmDelay = 0.04f;

    [Tooltip("Projectile must travel this far before it can damage enemies.")]
    [SerializeField] private float enemyHitArmDistance = 0.25f;

    [Header("Projectile Breaking")]
    [Tooltip("If true, destroys enemy projectiles on contact and keeps flying. " +
             "Enable on power shot prefab. Requires the enemy projectile layer to " +
             "collide with this projectile's layer in Physics2D settings.")]
    [SerializeField] private bool breaksEnemyProjectiles = false;

    private Rigidbody2D rb;
    private SpeedModifier speedModifier;

    private Vector2 dir;
    private Vector2 spawnPosition;

    private float enemyArmedAt;
    private float distanceTravelled;

    private int ownerCharacterIndex = -1;
    private bool awardApOnHit = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        speedModifier = GetComponent<SpeedModifier>();

        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
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

        spawnPosition = rb != null ? rb.position : (Vector2)transform.position;
        distanceTravelled = 0f;

        enemyArmedAt = Time.time + Mathf.Max(0f, enemyHitArmDelay);

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        float moveSpeed = speed * GetSpeedMultiplier();

        Vector2 oldPos = rb.position;
        Vector2 newPos = oldPos + dir * moveSpeed * Time.fixedDeltaTime;

        distanceTravelled += Vector2.Distance(oldPos, newPos);

        rb.MovePosition(newPos);
    }

    private float GetSpeedMultiplier()
    {
        // SpeedModifier can be added after Awake by AoESlowEffect.
        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        return speedModifier != null ? speedModifier.Multiplier : 1f;
    }

    private bool EnemyHitboxIsArmed()
    {
        if (Time.time < enemyArmedAt)
            return false;

        if (distanceTravelled < enemyHitArmDistance)
            return false;

        return true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Destroy enemy projectiles on contact. Player projectile pierces through.
        // This is allowed immediately, even before enemy damage is armed.
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
            // Main fix:
            // Do not damage enemies during the spawn safety window.
            // This prevents Power Shot from hitting enemies behind/inside the player at spawn.
            if (!EnemyHitboxIsArmed())
                return;

            enemyHealth.TakeDamage(damage);

            EnemyStunnable stunnable = other.GetComponentInParent<EnemyStunnable>();

            if (stunnable != null)
                stunnable.Stun(stunSeconds);

            if (awardApOnHit &&
                PartyManager.Instance != null &&
                PartyManager.Instance.activeIndex == ownerCharacterIndex)
            {
                PartyManager pm = PartyManager.Instance;

                if (ownerCharacterIndex >= 0 && ownerCharacterIndex < pm.party.Count)
                {
                    var owner = pm.party[ownerCharacterIndex];

                    if (owner.def != null)
                    {
                        int maxAP = Mathf.Max(0, owner.def.maxAP);
                        int gain = Mathf.Max(0, owner.def.apGainOnBasicHit);

                        owner.currentAP = Mathf.Clamp(owner.currentAP + gain, 0, maxAP);
                    }
                }
            }

            Destroy(gameObject);
            return;
        }

        // Cover/walls should still destroy the projectile immediately.
        Destroy(gameObject);
    }
}