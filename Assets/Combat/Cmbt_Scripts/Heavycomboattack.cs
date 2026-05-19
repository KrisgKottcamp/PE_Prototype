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
/// AP is granted independently per hit, but only if that hit connects with at least one enemy.
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
    [Tooltip("Force of the knockback on the second hit. " +
             "Passed directly to KnockbackReceiver2D.ApplyKnockback.")]
    [SerializeField] private float knockbackForce = 8f;
    [Tooltip("Duration of the knockback movement on the second hit.")]
    [SerializeField] private float knockbackDuration = 0.18f;

    [Header("Hitbox")]
    [SerializeField] private Transform hitOrigin;
    [Tooltip("Distance from hitOrigin in the aim direction where the overlap circle is placed.")]
    [SerializeField] private float hitRange = 0.8f;
    [Tooltip("Radius of the overlap circle.")]
    [SerializeField] private float hitRadius = 0.45f;
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("Layer mask for enemy projectiles. Projectiles on this layer with Team == Enemy are destroyed on hit.")]
    [SerializeField] private LayerMask projectileMask;

    [Header("VFX (optional)")]
    [SerializeField] private GameObject hit1VfxPrefab;
    [SerializeField] private GameObject hit2VfxPrefab;
    [SerializeField] private float vfxAngleOffset = 0f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // Runtime state
    private Camera cam;
    private Vector2 lastAimDir = Vector2.up;
    private Vector2 comboAimDir;        // locked at the moment of the click, used for both hits
    private float cooldownTimer = 0f;
    private bool comboRunning = false;

    private readonly Collider2D[] hitCols = new Collider2D[16];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[16];

    // ----------------------------
    // Unity
    // ----------------------------

    private void Awake()
    {
        if (hitOrigin == null) hitOrigin = transform;
    }

    private void Update()
    {
        // Only active when Imogen (HeavyCombo type) is the active character
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;
        if (pm.Active.def.basicAttackType != BasicAttackType.HeavyCombo) return;

        if (cam == null) cam = Camera.main;
        UpdateMouseAim();

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (comboRunning) return;

        bool pressed = Input.GetKeyDown(attackKey) || Input.GetMouseButtonDown(0);
        if (allowKeyboardKey) pressed |= Input.GetKeyDown(keyboardAttackKey);
        if (!pressed) return;

        // Lock the aim direction at the moment of the click so both hits fire in the same direction
        comboAimDir = lastAimDir;
        StartCoroutine(ComboRoutine());
    }

    // ----------------------------
    // Combo coroutine
    // ----------------------------

    private IEnumerator ComboRoutine()
    {
        comboRunning = true;

        // --- Hit 1: immediate ---
        DoHit(hit1Damage, hit1StunSeconds, applyKnockback: false, comboAimDir);

        // --- Wait before hit 2 ---
        yield return new WaitForSeconds(comboPunchDelay);

        // --- Hit 2: knockback ---
        DoHit(hit2Damage, hit2StunSeconds, applyKnockback: true, comboAimDir);

        // --- Cooldown ---
        cooldownTimer = comboCooldown;
        comboRunning = false;
    }

    // ----------------------------
    // Hit logic
    // ----------------------------

    private void DoHit(int damage, float stunSeconds, bool applyKnockback, Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir = dir.normalized;

        Vector2 center = (Vector2)hitOrigin.position + dir * hitRange;

        // VFX
        GameObject vfxPrefab = applyKnockback ? hit2VfxPrefab : hit1VfxPrefab;
        SpawnVfx(vfxPrefab, center, dir);

        // Enemy overlap check
        int count = Physics2D.OverlapCircleNonAlloc(center, hitRadius, hitCols, enemyMask);

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            var col = hitCols[i];
            if (col == null) continue;

            var enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            // Deduplicate per swing
            bool already = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueEnemies[j] == enemy) { already = true; break; }
            }
            if (already) continue;
            uniqueEnemies[uniqueCount++] = enemy;

            // Damage
            enemy.TakeDamage(damage);

            // Stun
            var stunnable = enemy.GetComponentInParent<EnemyStunnable>();
            if (stunnable != null) stunnable.Stun(stunSeconds);

            // Knockback on hit 2 — direction is away from the player toward the enemy
            if (applyKnockback)
            {
                var knockback = enemy.GetComponentInParent<KnockbackReceiver2D>();
                if (knockback != null)
                {
                    Vector2 knockDir = ((Vector2)enemy.transform.position - (Vector2)hitOrigin.position).normalized;
                    if (knockDir.sqrMagnitude < 0.0001f) knockDir = dir;
                    knockback.ApplyKnockback(knockDir, knockbackForce, knockbackDuration);
                }
            }

            if (uniqueCount >= uniqueEnemies.Length) break;
        }

        // Grant AP if at least one enemy was hit
        if (uniqueCount > 0) GrantAP();

        // Destroy enemy projectiles inside the hitbox
        DestroyEnemyProjectiles(center);
    }

    // ----------------------------
    // AP
    // ----------------------------

    private void GrantAP()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        var active = pm.Active;
        if (active == null || active.def == null) return;

        int maxAP = Mathf.Max(0, active.def.maxAP);
        int gain = Mathf.Max(0, active.def.apGainOnBasicHit);
        active.currentAP = Mathf.Clamp(active.currentAP + gain, 0, maxAP);
    }

    // ----------------------------
    // Projectile destruction
    // ----------------------------

    private void DestroyEnemyProjectiles(Vector2 center)
    {
        if (projectileMask.value == 0) return;

        // Reuse hitCols — enemy processing is already done
        int count = Physics2D.OverlapCircleNonAlloc(center, hitRadius, hitCols, projectileMask);

        for (int i = 0; i < count; i++)
        {
            var col = hitCols[i];
            if (col == null) continue;

            var proj = col.GetComponentInParent<Projectile>();
            if (proj == null) continue;

            if (proj.Team == Projectile.ProjectileTeam.Enemy)
                Destroy(proj.gameObject);
        }
    }

    // ----------------------------
    // Aim
    // ----------------------------

    private void UpdateMouseAim()
    {
        if (cam == null) return;
        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;
        Vector3 world = cam.ScreenToWorldPoint(mouse);
        Vector2 delta = (Vector2)world - (Vector2)hitOrigin.position;
        if (delta.sqrMagnitude > 0.0001f) lastAimDir = delta.normalized;
    }

    // ----------------------------
    // VFX
    // ----------------------------

    private void SpawnVfx(GameObject prefab, Vector2 pos, Vector2 dir)
    {
        if (prefab == null) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + vfxAngleOffset;
        Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), Quaternion.Euler(0f, 0f, angle));
    }

    // ----------------------------
    // Gizmos
    // ----------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Transform o = hitOrigin != null ? hitOrigin : transform;
        Vector2 dir = lastAimDir.sqrMagnitude > 0.0001f ? lastAimDir : Vector2.up;
        Vector2 center = (Vector2)o.position + dir.normalized * hitRange;

        // Hit 1 range shown in yellow
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, hitRadius);
        Gizmos.DrawLine(o.position, center);

        // Knockback direction indicator shown in red
        Gizmos.color = Color.red;
        Gizmos.DrawLine(center, center + dir * knockbackForce * 0.06f);
    }
#endif
}