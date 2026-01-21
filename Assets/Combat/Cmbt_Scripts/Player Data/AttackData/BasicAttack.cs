using UnityEngine;
using static CharacterDefinition;

public class BasicAttack : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0; // left click
    [SerializeField] private bool allowKeyboardAttackKey = true;
    [SerializeField] private KeyCode keyboardAttackKey = KeyCode.J;

    [Header("Attack Shape")]
    [SerializeField] private Transform hitOrigin;
    [SerializeField] private float range = 0.8f;
    [SerializeField] private float radius = 0.35f;
    [SerializeField] private int damage = 5;
    [SerializeField] private LayerMask enemyMask; // EnemyHurtbox

    [Header("Timing")]
    [SerializeField] private float swingCooldown = 0.18f;       // time between swings
    [SerializeField] private int swingsPerBurst = 3;            // 3-hit limit
    [SerializeField] private float burstRecoveryCooldown = 0.6f; // extra cooldown after 3rd swing

    [Header("Stun")]
    [SerializeField] private float stunSeconds = 0.25f;

    [Header("VFX (optional)")]
    [SerializeField] private GameObject attackVfxPrefab;
    [SerializeField] private float vfxAngleOffset = 0f; // if art faces up, try +90

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Camera cam;
    private Vector2 lastAimDir = Vector2.up;

    private float swingCdTimer;
    private float recoveryTimer;
    private int swingsRemaining;

    private readonly Collider2D[] hitCols = new Collider2D[16];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[16];

    private void Awake()
    {
        if (hitOrigin == null) hitOrigin = transform;
        swingsRemaining = Mathf.Max(1, swingsPerBurst);
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;
        if (pm.Active.def.basicAttackType != BasicAttackType.Melee) return;


        if (cam == null) cam = Camera.main;

        UpdateMouseAim();

        if (swingCdTimer > 0f) swingCdTimer -= Time.deltaTime;
        if (recoveryTimer > 0f)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f)
                swingsRemaining = Mathf.Max(1, swingsPerBurst);
        }

        bool pressed = Input.GetKeyDown(attackKey) || Input.GetMouseButtonDown(0);
        if (allowKeyboardAttackKey) pressed |= Input.GetKeyDown(keyboardAttackKey);

        if (!pressed) return;

        if (recoveryTimer > 0f) return;
        if (swingCdTimer > 0f) return;
        if (swingsRemaining <= 0) return;

        DoAttack(lastAimDir);

        swingsRemaining--;
        swingCdTimer = swingCooldown;

        if (swingsRemaining <= 0)
            recoveryTimer = burstRecoveryCooldown;
    }

    private void UpdateMouseAim()
    {
        if (cam == null) return;

        Vector3 mouse = Input.mousePosition;

        // For ortho camera, z is ignored, but setting it makes ScreenToWorldPoint reliable.
        mouse.z = -cam.transform.position.z;

        Vector3 world = cam.ScreenToWorldPoint(mouse);
        Vector2 delta = (Vector2)world - (Vector2)hitOrigin.position;

        if (delta.sqrMagnitude > 0.0001f)
            lastAimDir = delta.normalized;
    }

    private void DoAttack(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir = dir.normalized;

        Vector2 center = (Vector2)hitOrigin.position + dir * range;

        SpawnVfx(center, dir);

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitCols, enemyMask);
        if (count <= 0) return;

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            var col = hitCols[i];
            if (col == null) continue;

            var enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            // dedup per swing
            bool already = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueEnemies[j] == enemy) { already = true; break; }
            }
            if (already) continue;

            uniqueEnemies[uniqueCount++] = enemy;

            enemy.TakeDamage(damage);

            var stunnable = enemy.GetComponentInParent<EnemyStunnable>();
            if (stunnable != null)
                stunnable.Stun(stunSeconds);

            if (uniqueCount >= uniqueEnemies.Length) break;
        }

        if (uniqueCount > 0)
            GrantAP();
    }

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

    private void SpawnVfx(Vector2 pos, Vector2 dir)
    {
        if (attackVfxPrefab == null) return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + vfxAngleOffset;
        Instantiate(attackVfxPrefab, new Vector3(pos.x, pos.y, 0f), Quaternion.Euler(0f, 0f, angle));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Transform o = hitOrigin != null ? hitOrigin : transform;
        Vector2 dir = (lastAimDir.sqrMagnitude > 0.0001f) ? lastAimDir : Vector2.up;
        Vector2 center = (Vector2)o.position + dir.normalized * range;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, radius);
        Gizmos.DrawLine(o.position, center);
    }
#endif
}
