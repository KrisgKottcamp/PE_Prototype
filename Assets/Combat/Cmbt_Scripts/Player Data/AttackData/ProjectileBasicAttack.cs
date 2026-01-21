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

    [Header("Projectile")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private PlayerProjectile projectilePrefab;
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float projectileLifetime = 2.5f;
    [SerializeField] private float muzzleForwardOffset = 0.15f;
    [SerializeField] private LayerMask projectileHitMask; // EnemyHurtbox + Obstacles

    [Header("Hit Effects")]
    [SerializeField] private int damage = 3;
    [SerializeField] private float stunSeconds = 0.15f;

    [Header("Aim")]
    [SerializeField] private float angleOffset = 0f; // if art faces up, try 90

    private Camera cam;
    private Vector2 aimDir = Vector2.up;

    private int shotsRemaining;
    private float shotTimer;
    private float recoveryTimer;

    private void Awake()
    {
        if (muzzle == null) muzzle = transform;
        shotsRemaining = Mathf.Max(1, shotsPerBurst);
    }

    private void OnEnable()
    {
        shotsRemaining = Mathf.Max(1, shotsPerBurst);
        shotTimer = 0f;
        recoveryTimer = 0f;
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;
        if (pm.Active.def.basicAttackType != BasicAttackType.Projectile) return;

        if (cam == null) cam = Camera.main;
        UpdateMouseAim();

        if (shotTimer > 0f) shotTimer -= Time.deltaTime;

        if (recoveryTimer > 0f)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f) shotsRemaining = Mathf.Max(1, shotsPerBurst);
        }

        bool pressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(fireKey);
        if (!pressed) return;

        if (recoveryTimer > 0f) return;
        if (shotTimer > 0f) return;
        if (shotsRemaining <= 0) return;
        if (projectilePrefab == null) return;

        Fire();

        shotsRemaining--;
        shotTimer = shotCooldown;

        if (shotsRemaining <= 0) recoveryTimer = burstRecovery;
    }

    private void UpdateMouseAim()
    {
        if (cam == null) return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;
        Vector3 world = cam.ScreenToWorldPoint(mouse);

        Vector2 delta = (Vector2)world - (Vector2)muzzle.position;
        if (delta.sqrMagnitude > 0.0001f) aimDir = delta.normalized;
    }

    private void Fire()
    {
        Vector2 dir = aimDir.sqrMagnitude > 0.0001f ? aimDir : Vector2.up;

        Vector3 spawnPos = muzzle.position + (Vector3)(dir * muzzleForwardOffset);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;

        var proj = Instantiate(projectilePrefab, spawnPos, Quaternion.Euler(0f, 0f, angle));

        int ownerIndex = PartyManager.Instance != null ? PartyManager.Instance.activeIndex : -1;

        // Last bool = award AP on hit (true for basic attacks)
        proj.Fire(dir, ownerIndex, damage, stunSeconds, projectileSpeed, projectileLifetime, projectileHitMask, true);
    }

    // Optional: let router/character def override stats later
    public void Configure(PlayerProjectile prefab, int dmg, float stun, float speed, float life)
    {
        projectilePrefab = prefab;
        damage = dmg;
        stunSeconds = stun;
        projectileSpeed = speed;
        projectileLifetime = life;
    }
}
