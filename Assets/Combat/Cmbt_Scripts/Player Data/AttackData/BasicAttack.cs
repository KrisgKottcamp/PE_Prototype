using UnityEngine;
using ProjectEri.SkillSystemV2;
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

    [Header("Commitment")]
    [Tooltip("Brief movement multiplier applied when Audrey swings. 1 = no movement penalty.")]
    [SerializeField, Range(0.1f, 1f)] private float swingMoveMultiplier = 0.70f;

    [Tooltip("How long the movement commitment lasts after each swing.")]
    [SerializeField, Min(0f)] private float swingCommitmentDuration = 0.10f;

    [Tooltip("Small recovery applied if the player is hit and the current attack chain is canceled.")]
    [SerializeField, Min(0f)] private float damageCancelRecovery = 0.18f;

    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("Basic Attack Enemy Reaction")]
    [Tooltip("Tight flinch plus one projectile interrupt for hits one and two.")]
    [SerializeField] private BasicAttackReactionSettings normalHitReaction =
        BasicAttackReactionSettings.Create(0.055f, 0.14f, 0.75f);

    [Tooltip("Slightly stronger movement flinch on Audrey's third hit. Resolve still prevents repeated projectile cancellation.")]
    [SerializeField] private BasicAttackReactionSettings thirdHitReaction =
        BasicAttackReactionSettings.Create(0.09f, 0.14f, 0.75f);

    [Header("Physical Hit Feedback")]
    [Tooltip("Very small push applied by Audrey's first and second hits.")]
    [SerializeField, Min(0f)] private float normalHitKnockbackForce = 0.75f;

    [SerializeField, Min(0f)] private float normalHitKnockbackDuration = 0.07f;

    [Tooltip("Stronger push on the third hit. Keep this well below Imogen's heavy knockback force of 8.")]
    [SerializeField, Min(0f)] private float thirdHitKnockbackForce = 1.75f;

    [SerializeField, Min(0f)] private float thirdHitKnockbackDuration = 0.10f;

    [Header("Audrey Combo Hitstop")]
    [Tooltip(
        "One continuous hitstop/slow window shared by Audrey's three clicks. " +
        "Duration is also the maximum real-time gap allowed before the chain releases."
    )]
    [SerializeField] private HitstopSettings comboChainHitstop =
        HitstopSettings.Create(0.24f, 0.40f);

    [Tooltip("Real-time ease from normal speed into Audrey's combo slowdown.")]
    [SerializeField, Min(0f)]
    private float comboHitstopBlendIn = 0.045f;

    [Tooltip("Real-time ease back to normal speed when the combo window releases.")]
    [SerializeField, Min(0f)]
    private float comboHitstopBlendOut = 0.10f;

    [Tooltip(
        "Real-time tail after the third click. The previous click's remaining " +
        "continuation time may be slightly longer."
    )]
    [SerializeField, Min(0f)]
    private float thirdHitReleaseTail = 0.06f;

    [Header("Attack Momentum")]
    [SerializeField] private float momentumGainOnSuccessfulSwing = 8f;

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

    private bool comboHitstopActive;
    private float comboHitstopExpiresAtRealtime;
    private CombatBasicAttackRouter basicAttackRouter;

    private readonly Collider2D[] hitCols = new Collider2D[16];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[16];

    private void Awake()
    {
        if (hitOrigin == null) hitOrigin = transform;
        basicAttackRouter = GetComponent<CombatBasicAttackRouter>();
        if (basicAttackRouter == null)
            basicAttackRouter = GetComponentInParent<CombatBasicAttackRouter>();
        swingsRemaining = Mathf.Max(1, swingsPerBurst);
        ResolveAttackCommitment();
    }

    private void Update()
    {
        if (SpellBuildUpControl2D.IsBasicAttackBlocked(gameObject))
            return;

        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;
        if (pm.Active.def.basicAttackType != BasicAttackType.Melee) return;

        if (cam == null) cam = Camera.main;

        UpdateMouseAim();

        UpdateComboHitstopState();

        // Audrey must still be able to perform the next click while her
        // continuous combo window is slowing scaled game time.
        float attackDeltaTime = comboHitstopActive
            ? Time.unscaledDeltaTime
            : Time.deltaTime;
        attackDeltaTime *= SpellStatModifierUtility.Evaluate(
            gameObject,
            SpellActorStat.BasicAttackSpeed,
            1f);

        if (swingCdTimer > 0f)
            swingCdTimer -= attackDeltaTime;

        if (recoveryTimer > 0f)
        {
            recoveryTimer -= attackDeltaTime;
            if (recoveryTimer <= 0f)
                swingsRemaining = Mathf.Max(1, swingsPerBurst);
        }

        bool pressed = Input.GetKeyDown(attackKey) || Input.GetMouseButtonDown(0);
        if (allowKeyboardAttackKey) pressed |= Input.GetKeyDown(keyboardAttackKey);

        if (!pressed) return;

        if (!CanStartAttack()) return;
        if (recoveryTimer > 0f) return;
        if (swingCdTimer > 0f) return;
        if (swingsRemaining <= 0) return;

        bool isThirdHit = swingsRemaining == 1;
        bool chainWasActive = comboHitstopActive;

        ApplyAttackCommitment();
        basicAttackRouter?.RequestReleaseShake(lastAimDir);
        bool connected = DoAttack(lastAimDir, isThirdHit);

        if (connected || chainWasActive)
            RefreshComboHitstop(isThirdHit);

        swingsRemaining--;
        swingCdTimer = swingCooldown;

        if (swingsRemaining <= 0)
            recoveryTimer = burstRecoveryCooldown;
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
                swingMoveMultiplier,
                swingCommitmentDuration
            );
        }
    }

    public void CancelCurrentAttack()
    {
        comboHitstopActive = false;
        comboHitstopExpiresAtRealtime = 0f;
        swingsRemaining = Mathf.Max(1, swingsPerBurst);
        swingCdTimer = 0f;
        recoveryTimer = Mathf.Max(
            recoveryTimer,
            damageCancelRecovery
        );
    }

    private void UpdateMouseAim()
    {
        if (cam == null) return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;

        Vector3 world = cam.ScreenToWorldPoint(mouse);
        Vector2 delta = (Vector2)world - (Vector2)hitOrigin.position;

        if (delta.sqrMagnitude > 0.0001f)
            lastAimDir = delta.normalized;
    }

    private bool DoAttack(Vector2 dir, bool isThirdHit)
    {
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir = dir.normalized;

        Vector2 center = (Vector2)hitOrigin.position + dir * range;

        SpawnVfx(center, dir);

        bool deflectedDelivery = SpellDeflectionUtility.DeflectInCircle(
            gameObject,
            center,
            radius,
            dir,
            ~0) > 0;

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitCols, enemyMask);
        if (count <= 0) return deflectedDelivery;

        int uniqueCount = 0;

        for (int i = 0; i < count; i++)
        {
            var col = hitCols[i];
            if (col == null) continue;

            var enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            bool already = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (uniqueEnemies[j] == enemy) { already = true; break; }
            }
            if (already) continue;

            uniqueEnemies[uniqueCount++] = enemy;

            float dealt = SpellStatModifierUtility.Evaluate(
                gameObject,
                SpellActorStat.DamageDealt,
                1f);
            float received = SpellStatModifierUtility.Evaluate(
                enemy.gameObject,
                SpellActorStat.DamageReceived,
                1f);
            enemy.TakeDamage(Mathf.Max(
                0,
                Mathf.RoundToInt(damage * dealt * received)));

            var stunnable = enemy.GetComponentInParent<EnemyStunnable>();
            if (stunnable != null)
            {
                BasicAttackReactionSettings reaction = isThirdHit
                    ? thirdHitReaction
                    : normalHitReaction;

                reaction.Apply(stunnable);
            }

            ApplyHitKnockback(enemy, dir, isThirdHit);

            if (uniqueCount >= uniqueEnemies.Length) break;
        }

        if (uniqueCount > 0)
        {
            SpawnAPParticles(uniqueCount);
            AttackMomentumManager.Instance?.RegisterMomentum(momentumGainOnSuccessfulSwing);
        }

        return uniqueCount > 0 || deflectedDelivery;
    }

    private void RefreshComboHitstop(bool isThirdHit)
    {
        if (!comboChainHitstop.enabled)
        {
            comboHitstopActive = false;
            return;
        }

        HitstopSettings request = comboChainHitstop;

        if (isThirdHit)
            request.duration = Mathf.Max(0f, thirdHitReleaseTail);

        HitstopManager.RequestSustained(
            request,
            comboHitstopBlendIn,
            comboHitstopBlendOut
        );

        if (isThirdHit)
        {
            // No fourth click to wait for. The manager keeps only the short
            // finisher tail (or any time still remaining from click two).
            comboHitstopActive = false;
            comboHitstopExpiresAtRealtime = 0f;
            return;
        }

        comboHitstopActive = request.duration > 0f;
        comboHitstopExpiresAtRealtime =
            Time.realtimeSinceStartup + request.duration;
    }

    private void UpdateComboHitstopState()
    {
        if (!comboHitstopActive)
            return;

        if (Time.realtimeSinceStartup < comboHitstopExpiresAtRealtime)
            return;

        comboHitstopActive = false;
        comboHitstopExpiresAtRealtime = 0f;
    }

    private void ApplyHitKnockback(
        EnemyHealth enemy,
        Vector2 fallbackDirection,
        bool isThirdHit)
    {
        if (enemy == null)
            return;

        KnockbackReceiver2D receiver =
            enemy.GetComponentInParent<KnockbackReceiver2D>();

        if (receiver == null)
            return;

        Vector2 direction =
            (Vector2)enemy.transform.position -
            (Vector2)hitOrigin.position;

        if (direction.sqrMagnitude < 0.0001f)
            direction = fallbackDirection;
        else
            direction.Normalize();

        float force = isThirdHit
            ? thirdHitKnockbackForce
            : normalHitKnockbackForce;

        float duration = isThirdHit
            ? thirdHitKnockbackDuration
            : normalHitKnockbackDuration;

        receiver.ApplyKnockback(
            direction,
            force,
            duration
        );
    }

    private void SpawnAPParticles(int uniqueCount)
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        var active = pm.Active;
        if (active == null || active.def == null) return;

        int gain = Mathf.Max(0, active.def.apGainOnBasicHit);

        APParticleSystem.SpawnRewardAcrossEnemies(
            uniqueEnemies,
            uniqueCount,
            gain,
            hitOrigin.position
        );
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
