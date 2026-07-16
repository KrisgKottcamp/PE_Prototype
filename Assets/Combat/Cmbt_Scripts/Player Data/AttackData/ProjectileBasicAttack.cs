using UnityEngine;
using static CharacterDefinition;

public class ProjectileBasicAttack : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;

    [Header("Burst")]
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float shotCooldown = 0.12f;
    [SerializeField] private float burstRecovery = 0.6f;

    [Header("Commitment")]
    [Tooltip("Brief movement multiplier applied when Eri fires. 1 = no movement penalty.")]
    [SerializeField, Range(0.1f, 1f)] private float shotMoveMultiplier = 0.82f;

    [Tooltip("How long the movement commitment lasts after each projectile shot.")]
    [SerializeField, Min(0f)] private float shotCommitmentDuration = 0.07f;

    [Tooltip("Small recovery applied if the player is hit and the current burst chain is canceled.")]
    [SerializeField, Min(0f)] private float damageCancelRecovery = 0.18f;

    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("Projectile")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private PlayerProjectile projectilePrefab;
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float projectileLifetime = 2.5f;
    [SerializeField] private float muzzleForwardOffset = 0.15f;
    [SerializeField] private LayerMask projectileHitMask;

    [Header("Hit Effects")]
    [SerializeField] private int damage = 3;
    [SerializeField] private float stunSeconds = 0.15f;

    [Header("Attack Momentum")]
    [Tooltip(
        "Raw Momentum awarded when one basic projectile hits an enemy. " +
        "This does not start Active Average skill scoring."
    )]
    [SerializeField, Min(0f)]
    private float momentumGainOnHit = 2f;

    [Header("Aim")]
    [SerializeField] private float angleOffset = 0f;

    private Camera cam;
    private Vector2 aimDir = Vector2.up;

    private int shotsRemaining;
    private float shotTimer;
    private float recoveryTimer;

    private void Awake()
    {
        if (muzzle == null)
            muzzle = transform;

        shotsRemaining = Mathf.Max(1, shotsPerBurst);
        ResolveAttackCommitment();
    }

    private void OnEnable()
    {
        shotsRemaining = Mathf.Max(1, shotsPerBurst);
        shotTimer = 0f;
        recoveryTimer = 0f;
    }

    private void Update()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null ||
            pm.Active == null ||
            pm.Active.def == null)
        {
            return;
        }

        if (pm.Active.def.basicAttackType !=
            BasicAttackType.Projectile)
        {
            return;
        }

        if (cam == null)
            cam = Camera.main;

        UpdateMouseAim();

        if (shotTimer > 0f)
            shotTimer -= Time.deltaTime;

        if (recoveryTimer > 0f)
        {
            recoveryTimer -= Time.deltaTime;

            if (recoveryTimer <= 0f)
            {
                shotsRemaining =
                    Mathf.Max(1, shotsPerBurst);
            }
        }

        bool pressed =
            Input.GetMouseButtonDown(0) ||
            Input.GetKeyDown(fireKey);

        if (!pressed ||
            !CanStartAttack() ||
            recoveryTimer > 0f ||
            shotTimer > 0f ||
            shotsRemaining <= 0 ||
            projectilePrefab == null)
        {
            return;
        }

        ApplyAttackCommitment();
        Fire();

        shotsRemaining--;
        shotTimer = shotCooldown;

        if (shotsRemaining <= 0)
            recoveryTimer = burstRecovery;
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

    private void ApplyAttackCommitment()
    {
        if (attackCommitment == null)
            ResolveAttackCommitment();

        if (attackCommitment != null)
        {
            attackCommitment.ApplyMovementCommitment(
                shotMoveMultiplier,
                shotCommitmentDuration
            );
        }
    }

    public void CancelCurrentAttack()
    {
        shotsRemaining = Mathf.Max(1, shotsPerBurst);
        shotTimer = 0f;
        recoveryTimer = Mathf.Max(
            recoveryTimer,
            damageCancelRecovery
        );
    }

    private void UpdateMouseAim()
    {
        if (cam == null)
            return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;

        Vector3 world =
            cam.ScreenToWorldPoint(mouse);

        Vector2 delta =
            (Vector2)world -
            (Vector2)muzzle.position;

        if (delta.sqrMagnitude > 0.0001f)
            aimDir = delta.normalized;
    }

    private void Fire()
    {
        Vector2 direction =
            aimDir.sqrMagnitude > 0.0001f
                ? aimDir
                : Vector2.up;

        Vector3 spawnPosition =
            muzzle.position +
            (Vector3)(
                direction * muzzleForwardOffset
            );

        float angle =
            Mathf.Atan2(direction.y, direction.x) *
            Mathf.Rad2Deg +
            angleOffset;

        PlayerProjectile projectile = Instantiate(
            projectilePrefab,
            spawnPosition,
            Quaternion.Euler(0f, 0f, angle)
        );

        PartyManager pm = PartyManager.Instance;

        int ownerIndex =
            pm != null
                ? pm.activeIndex
                : -1;

        projectile.Fire(
            direction,
            ownerIndex,
            damage,
            stunSeconds,
            projectileSpeed,
            projectileLifetime,
            projectileHitMask,
            awardAp: true,
            momentumGain: momentumGainOnHit,
            startActiveScoringOnHit: false
        );
    }

    public void Configure(
        PlayerProjectile prefab,
        int dmg,
        float stun,
        float speed,
        float life)
    {
        projectilePrefab = prefab;
        damage = dmg;
        stunSeconds = stun;
        projectileSpeed = speed;
        projectileLifetime = life;
    }
}
