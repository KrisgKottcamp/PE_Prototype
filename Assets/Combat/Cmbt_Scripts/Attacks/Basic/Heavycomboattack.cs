using System.Collections;
using UnityEngine;
using static CharacterDefinition;

/// <summary>
/// HeavyComboAttack — Imogen's basic attack.
///
/// One click fires the full two-hit combo automatically:
///   Hit 1: Immediate. Damage + stun. No knockback.
///   Hit 2: After comboPunchDelay seconds. Damage + stun + knockback away from player.
/// After hit 2 completes there is a comboCooldown before the player can attack again.
///
/// Phase 2 aggression change:
/// - Each punch has a short movement commitment.
/// - Taking accepted damage cancels the current combo through CancelCurrentAttack().
/// </summary>
public class HeavyComboAttack : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0;
    [SerializeField] private bool allowKeyboardKey = true;
    [SerializeField] private KeyCode keyboardAttackKey = KeyCode.J;

    [Header("Combo Timing")]
    [Tooltip("Delay in seconds between hit 1 and hit 2.")]
    [SerializeField] private float comboPunchDelay = 0.2f;

    [Tooltip("Cooldown in seconds after the combo finishes before the player can attack again.")]
    [SerializeField] private float comboCooldown = 0.9f;

    [Header("Commitment")]
    [SerializeField, Range(0.1f, 1f)] private float hit1MoveMultiplier = 0.60f;
    [SerializeField, Min(0f)] private float hit1CommitmentDuration = 0.14f;

    [SerializeField, Range(0.1f, 1f)] private float hit2MoveMultiplier = 0.45f;
    [SerializeField, Min(0f)] private float hit2CommitmentDuration = 0.20f;

    [Tooltip("Small recovery applied if the player is hit and the combo is canceled.")]
    [SerializeField, Min(0f)] private float damageCancelRecovery = 0.25f;

    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("Hit 1")]
    [Tooltip("Damage dealt by the first punch.")]
    [SerializeField] private int hit1Damage = 6;

    [Tooltip("Stun duration applied by the first punch.")]
    [SerializeField] private float hit1StunSeconds = 0.15f;

    [Header("Hit 2")]
    [Tooltip("Damage dealt by the second punch.")]
    [SerializeField] private int hit2Damage = 10;

    [Tooltip("Stun duration applied by the second punch.")]
    [SerializeField] private float hit2StunSeconds = 0.25f;

    [Tooltip(
        "Force of the knockback on the second hit. " +
        "Passed directly to KnockbackReceiver2D.ApplyKnockback."
    )]
    [SerializeField] private float knockbackForce = 8f;

    [Tooltip("Duration of the knockback movement on the second hit.")]
    [SerializeField] private float knockbackDuration = 0.18f;

    [Header("Attack Momentum")]
    [Tooltip(
        "Raw Momentum reported when hit 1 connects with at least one enemy. " +
        "AttackMomentumManager applies its global and tier scaling afterward."
    )]
    [SerializeField, Min(0f)] private float hit1MomentumGain = 8f;

    [Tooltip(
        "Raw Momentum reported when hit 2 connects with at least one enemy. " +
        "AttackMomentumManager applies its global and tier scaling afterward."
    )]
    [SerializeField, Min(0f)] private float hit2MomentumGain = 8f;

    [Tooltip(
        "Raw Momentum granted for each unique enemy projectile destroyed by a punch. " +
        "The challenge manager applies its global and tier scaling afterward."
    )]
    [SerializeField, Min(0f)]
    private float momentumPerProjectileDestroyed = 1f;

    [Tooltip(
        "Maximum raw projectile-destruction Momentum one punch can award. " +
        "This prevents dense bullet patterns from producing a large Momentum spike."
    )]
    [SerializeField, Min(0f)]
    private float maxProjectileMomentumPerHit = 3f;

    [Header("Hitbox")]
    [SerializeField] private Transform hitOrigin;

    [Tooltip("Distance from hitOrigin in the aim direction where the overlap circle is placed.")]
    [SerializeField] private float hitRange = 0.8f;

    [Tooltip("Radius of the overlap circle.")]
    [SerializeField] private float hitRadius = 0.45f;

    [SerializeField] private LayerMask enemyMask;

    [Tooltip(
        "Layer mask for enemy projectiles. Projectiles on this layer with " +
        "Team == Enemy are destroyed on hit."
    )]
    [SerializeField] private LayerMask projectileMask;

    [Header("VFX (optional)")]
    [SerializeField] private GameObject hit1VfxPrefab;
    [SerializeField] private GameObject hit2VfxPrefab;
    [SerializeField] private float vfxAngleOffset = 0f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Camera cam;
    private Vector2 lastAimDir = Vector2.up;
    private Vector2 comboAimDir;

    private float cooldownTimer;
    private bool comboRunning;
    private Coroutine comboRoutine;

    private readonly Collider2D[] hitCols = new Collider2D[16];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[16];
    private readonly Projectile[] uniqueProjectiles = new Projectile[16];

    private void Awake()
    {
        if (hitOrigin == null)
            hitOrigin = transform;

        ResolveAttackCommitment();
    }

    private void Update()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.def == null)
            return;

        if (pm.Active.def.basicAttackType != BasicAttackType.HeavyCombo)
            return;

        if (cam == null)
            cam = Camera.main;

        UpdateMouseAim();

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (comboRunning)
            return;

        bool pressed =
            Input.GetKeyDown(attackKey) ||
            Input.GetMouseButtonDown(0);

        if (allowKeyboardKey)
            pressed |= Input.GetKeyDown(keyboardAttackKey);

        if (!pressed)
            return;

        if (!CanStartAttack())
            return;

        // Both hits use the direction committed when the combo begins.
        comboAimDir = lastAimDir;
        comboRoutine = StartCoroutine(ComboRoutine());
    }

    private void ResolveAttackCommitment()
    {
        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();

        if (attackCommitment == null)
            attackCommitment = GetComponentInParent<PlayerAttackCommitment>();

        if (attackCommitment == null)
        {
            CombatPawn pawn = GetComponentInParent<CombatPawn>();

            if (pawn != null)
            {
                attackCommitment = pawn.GetComponent<PlayerAttackCommitment>();

                if (attackCommitment == null)
                    attackCommitment = pawn.gameObject.AddComponent<PlayerAttackCommitment>();
            }
        }

        if (attackCommitment == null)
            attackCommitment = gameObject.AddComponent<PlayerAttackCommitment>();
    }

    private bool CanStartAttack()
    {
        if (attackCommitment == null)
            ResolveAttackCommitment();

        return attackCommitment == null || attackCommitment.CanStartAttack;
    }

    private void ApplyAttackCommitment(float multiplier, float duration)
    {
        if (attackCommitment == null)
            ResolveAttackCommitment();

        if (attackCommitment != null)
        {
            attackCommitment.ApplyMovementCommitment(
                multiplier,
                duration
            );
        }
    }

    public void CancelCurrentAttack()
    {
        if (comboRoutine != null)
        {
            StopCoroutine(comboRoutine);
            comboRoutine = null;
        }

        comboRunning = false;
        cooldownTimer = Mathf.Max(
            cooldownTimer,
            damageCancelRecovery
        );
    }

    private IEnumerator ComboRoutine()
    {
        comboRunning = true;

        ApplyAttackCommitment(
            hit1MoveMultiplier,
            hit1CommitmentDuration
        );

        DoHit(
            hit1Damage,
            hit1StunSeconds,
            applyKnockback: false,
            comboAimDir,
            hit1MomentumGain
        );

        yield return new WaitForSeconds(comboPunchDelay);

        ApplyAttackCommitment(
            hit2MoveMultiplier,
            hit2CommitmentDuration
        );

        DoHit(
            hit2Damage,
            hit2StunSeconds,
            applyKnockback: true,
            comboAimDir,
            hit2MomentumGain
        );

        cooldownTimer = comboCooldown;
        comboRunning = false;
        comboRoutine = null;
    }

    private void DoHit(
        int damage,
        float stunSeconds,
        bool applyKnockback,
        Vector2 dir,
        float momentumGain)
    {
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.up;

        dir = dir.normalized;

        Vector2 center =
            (Vector2)hitOrigin.position +
            dir * hitRange;

        GameObject vfxPrefab =
            applyKnockback
                ? hit2VfxPrefab
                : hit1VfxPrefab;

        SpawnVfx(vfxPrefab, center, dir);

        int count = Physics2D.OverlapCircleNonAlloc(
            center,
            hitRadius,
            hitCols,
            enemyMask
        );

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitCols[i];

            if (col == null)
                continue;

            EnemyHealth enemy =
                col.GetComponentInParent<EnemyHealth>();

            if (enemy == null)
                continue;

            bool alreadyHit = false;

            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueEnemies[j] == enemy)
                {
                    alreadyHit = true;
                    break;
                }
            }

            if (alreadyHit)
                continue;

            uniqueEnemies[uniqueCount] = enemy;
            uniqueCount++;

            enemy.TakeDamage(damage);

            EnemyStunnable stunnable =
                enemy.GetComponentInParent<EnemyStunnable>();

            if (stunnable != null)
                stunnable.Stun(stunSeconds);

            if (applyKnockback)
            {
                KnockbackReceiver2D knockback =
                    enemy.GetComponentInParent<KnockbackReceiver2D>();

                if (knockback != null)
                {
                    Vector2 knockDir =
                        (Vector2)enemy.transform.position -
                        (Vector2)hitOrigin.position;

                    if (knockDir.sqrMagnitude < 0.0001f)
                        knockDir = dir;
                    else
                        knockDir.Normalize();

                    knockback.ApplyKnockback(
                        knockDir,
                        knockbackForce,
                        knockbackDuration
                    );
                }
            }

            if (uniqueCount >= uniqueEnemies.Length)
                break;
        }

        if (uniqueCount > 0)
        {
            GrantAP();

            AttackMomentumManager.Instance?.RegisterMomentum(
                momentumGain
            );
        }

        int destroyedProjectiles =
            DestroyEnemyProjectiles(center);

        if (destroyedProjectiles > 0)
        {
            float rawProjectileMomentum = Mathf.Min(
                destroyedProjectiles *
                Mathf.Max(0f, momentumPerProjectileDestroyed),
                Mathf.Max(0f, maxProjectileMomentumPerHit)
            );

            if (rawProjectileMomentum > 0f)
            {
                AttackMomentumManager.Instance?.RegisterMomentum(
                    rawProjectileMomentum
                );
            }
        }
    }

    private void GrantAP()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null)
            return;

        PartyManager.CharacterState active = pm.Active;

        if (active == null || active.def == null)
            return;

        int maxAP = Mathf.Max(0, active.def.maxAP);
        int gain = Mathf.Max(0, active.def.apGainOnBasicHit);

        active.currentAP = Mathf.Clamp(
            active.currentAP + gain,
            0,
            maxAP
        );
    }

    private int DestroyEnemyProjectiles(Vector2 center)
    {
        if (projectileMask.value == 0)
            return 0;

        int count = Physics2D.OverlapCircleNonAlloc(
            center,
            hitRadius,
            hitCols,
            projectileMask
        );

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitCols[i];

            if (col == null)
                continue;

            Projectile projectile =
                col.GetComponentInParent<Projectile>();

            if (projectile == null)
                continue;

            if (projectile.Team != Projectile.ProjectileTeam.Enemy)
                continue;

            bool alreadyDestroyed = false;

            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueProjectiles[j] == projectile)
                {
                    alreadyDestroyed = true;
                    break;
                }
            }

            if (alreadyDestroyed)
                continue;

            if (uniqueCount >= uniqueProjectiles.Length)
                break;

            uniqueProjectiles[uniqueCount] = projectile;
            uniqueCount++;

            Destroy(projectile.gameObject);
        }

        return uniqueCount;
    }

    private void UpdateMouseAim()
    {
        if (cam == null)
            return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;

        Vector3 world = cam.ScreenToWorldPoint(mouse);

        Vector2 delta =
            (Vector2)world -
            (Vector2)hitOrigin.position;

        if (delta.sqrMagnitude > 0.0001f)
            lastAimDir = delta.normalized;
    }

    private void SpawnVfx(
        GameObject prefab,
        Vector2 position,
        Vector2 dir)
    {
        if (prefab == null)
            return;

        float angle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg +
            vfxAngleOffset;

        Instantiate(
            prefab,
            new Vector3(position.x, position.y, 0f),
            Quaternion.Euler(0f, 0f, angle)
        );
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform originTransform =
            hitOrigin != null
                ? hitOrigin
                : transform;

        Vector2 dir =
            lastAimDir.sqrMagnitude > 0.0001f
                ? lastAimDir
                : Vector2.up;

        Vector2 center =
            (Vector2)originTransform.position +
            dir.normalized * hitRange;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, hitRadius);
        Gizmos.DrawLine(originTransform.position, center);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            center,
            center + dir * knockbackForce * 0.06f
        );
    }
#endif
}
