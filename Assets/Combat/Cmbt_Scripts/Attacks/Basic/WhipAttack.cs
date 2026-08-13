using System.Collections;
using UnityEngine;
using ProjectEri.SkillSystemV2;
using static CharacterDefinition;

/// <summary>
/// Dominic's whip-style basic attack.
///
/// Behavior:
/// - Click starts a short windup.
/// - Whip extends toward the cursor.
/// - Cursor distance limits the reach up to the configured maximum.
/// - Walls and cover clamp the reach.
/// - Growing whip segments check for unique enemies.
/// - AP is granted per unique enemy hit.
/// - Attack Momentum is granted once per successful swing, regardless of
///   how many enemies that swing hits.
///
/// Phase 2 aggression change:
/// - The whip has movement commitment during windup/extension/active time.
/// - Taking accepted damage cancels the swing through CancelCurrentAttack().
/// </summary>
public class WhipAttack : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0;
    [SerializeField] private bool allowKeyboardAttackKey = true;
    [SerializeField] private KeyCode keyboardAttackKey = KeyCode.J;

    [Header("Whip Shape")]
    [SerializeField] private Transform hitOrigin;

    [Tooltip("Maximum whip reach.")]
    [SerializeField] private float range = 3.2f;

    [Tooltip("Thickness of the whip hitbox.")]
    [SerializeField] private float width = 0.45f;

    [SerializeField] private LayerMask enemyMask;
    [SerializeField] private int maxTargets = 4;

    [Header("Wall Blocking")]
    [Tooltip("Layers that block the whip, such as walls, cover, and obstacles.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Small offset so the whip stops slightly before the wall.")]
    [SerializeField] private float obstacleSkin = 0.03f;

    [Header("Hit")]
    [SerializeField] private int damage = 7;

    [Header("Basic Attack Enemy Reaction")]
    [SerializeField] private BasicAttackReactionSettings basicHitReaction =
        BasicAttackReactionSettings.Create(0.065f, 0.12f, 0.85f);

    [Header("Hitstop")]
    [Tooltip("Requested once when Dominic's whip first connects during a swing.")]
    [SerializeField] private HitstopSettings whipHitstop =
        HitstopSettings.Create(0.035f, 0.045f);

    [Header("Timing")]
    [Tooltip("Dominic can start an attack every 0.4 seconds by default.")]
    [SerializeField] private float attackCooldown = 0.4f;

    [Tooltip("Delay before the whip starts extending.")]
    [SerializeField] private float windupDelay = 0.07f;

    [Tooltip("How long the whip takes to extend to its chosen reach.")]
    [SerializeField] private float extendTime = 0.08f;

    [Tooltip("How long the whip remains active at full reach.")]
    [SerializeField] private float activeHoldTime = 0.05f;

    [Tooltip("How long the visual takes to retract toward the player.")]
    [SerializeField] private float retractTime = 0.05f;

    [Header("Commitment")]
    [SerializeField, Range(0.1f, 1f)] private float swingMoveMultiplier = 0.55f;

    [Tooltip("Extra movement commitment after the active part of the whip.")]
    [SerializeField, Min(0f)] private float movementCommitmentExtra = 0.04f;

    [Tooltip("Small recovery applied if the player is hit and the whip is canceled.")]
    [SerializeField, Min(0f)] private float damageCancelRecovery = 0.20f;

    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("AP")]
    [Tooltip("AP gained for each unique enemy hit by one whip swing.")]
    [SerializeField] private int apGainOnHit = 18;

    [Header("AP Retract Pull")]
    [Tooltip("When enabled, loose AP near the whip is pulled toward Dominic as the whip retracts.")]
    [SerializeField] private bool pullAPOnRetract = true;

    [Tooltip("Radius of the capsule-shaped capture area around the visible whip. AP only needs to be nearby, not touching the line.")]
    [SerializeField, Min(0f)] private float apPullRadius = 0.90f;

    [Tooltip("How quickly caught AP changes velocity toward Dominic.")]
    [SerializeField, Min(0f)] private float apPullAcceleration = 60f;

    [Tooltip("Maximum speed of AP caught by the retracting whip.")]
    [SerializeField, Min(0.01f)] private float apPullMaximumSpeed = 16f;

    [Tooltip("How long caught AP keeps being pulled after the whip finishes retracting.")]
    [SerializeField, Min(0f)] private float apPullPersistenceAfterRetract = 0.20f;

    [Header("Attack Momentum")]
    [Tooltip(
        "Raw Momentum reported once when a whip swing hits at least one enemy. " +
        "Hitting multiple enemies still awards this only once."
    )]
    [SerializeField, Min(0f)]
    private float momentumGainOnSuccessfulSwing = 8f;

    [Header("Whip Placeholder Visual")]
    [SerializeField] private LineRenderer whipLine;
    [SerializeField] private bool createLineIfMissing = true;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private float lineWidth = 0.14f;

    [Tooltip("Use Default first. A dedicated effects sorting layer can also work.")]
    [SerializeField] private string lineSortingLayerName = "Default";

    [SerializeField] private int lineSortingOrder = 9999;

    [Tooltip("Small offset so the line does not start inside the player's center.")]
    [SerializeField] private float lineStartOffset = 0.15f;

    [Tooltip("Negative values often pull the line closer to the camera in 2D.")]
    [SerializeField] private float lineZOffset = -1f;

    [Header("VFX Optional")]
    [SerializeField] private GameObject whipVfxPrefab;
    [SerializeField] private float vfxAngleOffset = 0f;
    [SerializeField] private float vfxForwardOffset = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logAttackFired = false;
    [SerializeField] private bool logApGain = false;
    [SerializeField] private bool logMomentumGain = false;
    [SerializeField] private int debugAPPullTargetsThisFrame;
    [SerializeField] private int debugAPPullTargetsPeak;

    private Camera cam;
    private Vector2 lastAimDir = Vector2.right;
    private Vector2 lastMouseWorld;
    private bool hasMouseWorld;
    private CombatBasicAttackRouter basicAttackRouter;

    private float cooldownTimer;
    private bool swingRunning;
    private Coroutine swingRoutine;

    private readonly Collider2D[] hitCols = new Collider2D[24];
    private readonly EnemyHealth[] uniqueEnemies = new EnemyHealth[24];

    private void Awake()
    {
        if (hitOrigin == null)
            hitOrigin = transform;

        basicAttackRouter = GetComponent<CombatBasicAttackRouter>();
        if (basicAttackRouter == null)
            basicAttackRouter = GetComponentInParent<CombatBasicAttackRouter>();
        ResolveAttackCommitment();
        SetupWhipLine();
    }

    private void OnEnable()
    {
        cooldownTimer = 0f;
        swingRunning = false;

        if (whipLine != null)
            whipLine.enabled = false;
    }

    private void OnDisable()
    {
        if (swingRoutine != null)
        {
            StopCoroutine(swingRoutine);
            swingRoutine = null;
        }

        swingRunning = false;

        if (whipLine != null)
            whipLine.enabled = false;
    }

    private void Update()
    {
        if (SpellBuildUpControl2D.IsBasicAttackBlocked(gameObject))
            return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.def == null)
            return;

        if (pm.Active.def.basicAttackType != BasicAttackType.Whip)
            return;

        if (cam == null)
            cam = Camera.main;

        UpdateMouseAim();

        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime * GetAttackSpeedMultiplier();

        bool pressed =
            Input.GetKeyDown(attackKey) ||
            Input.GetMouseButtonDown(0);

        if (allowKeyboardAttackKey)
            pressed |= Input.GetKeyDown(keyboardAttackKey);

        if (!pressed)
            return;

        if (!CanStartAttack())
            return;

        if (cooldownTimer > 0f || swingRunning)
            return;

        Vector2 dir =
            lastAimDir.sqrMagnitude > 0.0001f
                ? lastAimDir.normalized
                : Vector2.right;

        float desiredReach = GetCursorLimitedReach();
        desiredReach = GetWallBlockedReach(dir, desiredReach);

        cooldownTimer = attackCooldown;

        ApplySwingCommitment();
        basicAttackRouter?.RequestReleaseShake(dir);

        swingRoutine = StartCoroutine(
            WhipSwingRoutine(dir, desiredReach)
        );
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

    private void ApplySwingCommitment()
    {
        if (attackCommitment == null)
            ResolveAttackCommitment();

        if (attackCommitment == null)
            return;

        float duration =
            Mathf.Max(0f, windupDelay) +
            Mathf.Max(0f, extendTime) +
            Mathf.Max(0f, activeHoldTime) +
            Mathf.Max(0f, movementCommitmentExtra);

        attackCommitment.ApplyMovementCommitment(
            swingMoveMultiplier,
            duration
        );
    }

    public void CancelCurrentAttack()
    {
        if (swingRoutine != null)
        {
            StopCoroutine(swingRoutine);
            swingRoutine = null;
        }

        swingRunning = false;
        cooldownTimer = Mathf.Max(
            cooldownTimer,
            damageCancelRecovery
        );

        if (whipLine != null)
            whipLine.enabled = false;
    }

    private void UpdateMouseAim()
    {
        if (cam == null)
            return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;

        Vector3 world = cam.ScreenToWorldPoint(mouse);
        lastMouseWorld = world;
        hasMouseWorld = true;

        Vector2 origin =
            hitOrigin != null
                ? (Vector2)hitOrigin.position
                : (Vector2)transform.position;

        Vector2 delta = lastMouseWorld - origin;

        if (delta.sqrMagnitude > 0.0001f)
            lastAimDir = delta.normalized;
    }

    private float GetCursorLimitedReach()
    {
        if (!hasMouseWorld)
            return range;

        Vector2 origin =
            hitOrigin != null
                ? (Vector2)hitOrigin.position
                : (Vector2)transform.position;

        Vector2 toMouse = lastMouseWorld - origin;

        if (toMouse.sqrMagnitude <= 0.0001f)
            return lineStartOffset;

        return Mathf.Clamp(
            toMouse.magnitude,
            lineStartOffset,
            range
        );
    }

    private float GetWallBlockedReach(
        Vector2 dir,
        float desiredReach)
    {
        desiredReach = Mathf.Clamp(
            desiredReach,
            lineStartOffset,
            range
        );

        if (obstacleMask.value == 0)
            return desiredReach;

        Vector2 origin =
            hitOrigin != null
                ? (Vector2)hitOrigin.position
                : (Vector2)transform.position;

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            dir,
            desiredReach,
            obstacleMask
        );

        if (hit.collider == null)
            return desiredReach;

        return Mathf.Max(
            lineStartOffset,
            hit.distance - obstacleSkin
        );
    }

    private IEnumerator WhipSwingRoutine(
        Vector2 dir,
        float targetReach)
    {
        swingRunning = true;

        dir =
            dir.sqrMagnitude > 0.0001f
                ? dir.normalized
                : Vector2.right;

        targetReach = Mathf.Clamp(
            targetReach,
            lineStartOffset,
            range
        );

        int uniqueCount = 0;
        bool momentumGrantedThisSwing = false;
        bool hitstopRequestedThisSwing = false;
        debugAPPullTargetsThisFrame = 0;
        debugAPPullTargetsPeak = 0;

        if (logAttackFired)
        {
            Debug.Log(
                $"WhipAttack fired. Target reach: {targetReach}",
                this
            );
        }

        if (whipLine == null)
            SetupWhipLine();

        if (whipLine != null)
            whipLine.enabled = true;

        Vector3 origin = GetLineOrigin();
        Vector3 start =
            origin +
            (Vector3)(dir * lineStartOffset);

        SetLine(start, start);

        SpawnVfx(
            hitOrigin != null
                ? (Vector2)hitOrigin.position
                : (Vector2)transform.position,
            dir
        );

        if (windupDelay > 0f)
            yield return new WaitForSeconds(
                windupDelay / GetAttackSpeedMultiplier());

        Vector3 previousEnd = start;

        float t = 0f;

        while (t < extendTime)
        {
            t += Time.deltaTime * GetAttackSpeedMultiplier();

            float k =
                extendTime <= 0f
                    ? 1f
                    : Mathf.Clamp01(t / extendTime);

            origin = GetLineOrigin();

            start =
                origin +
                (Vector3)(dir * lineStartOffset);

            float blockedReach =
                GetWallBlockedReach(dir, targetReach);

            Vector3 currentEnd =
                origin +
                (Vector3)(
                    dir *
                    Mathf.Lerp(
                        lineStartOffset,
                        blockedReach,
                        k
                    )
                );

            SetLine(start, currentEnd);

            CheckWhipSegment(
                previousEnd,
                currentEnd,
                ref uniqueCount,
                ref momentumGrantedThisSwing,
                ref hitstopRequestedThisSwing
            );

            previousEnd = currentEnd;

            yield return null;
        }

        origin = GetLineOrigin();

        start =
            origin +
            (Vector3)(dir * lineStartOffset);

        float fullBlockedReach =
            GetWallBlockedReach(dir, targetReach);

        Vector3 fullEnd =
            origin +
            (Vector3)(dir * fullBlockedReach);

        SetLine(start, fullEnd);

        float holdTimer = 0f;

        while (holdTimer < activeHoldTime)
        {
            holdTimer += Time.deltaTime * GetAttackSpeedMultiplier();

            origin = GetLineOrigin();

            start =
                origin +
                (Vector3)(dir * lineStartOffset);

            fullBlockedReach =
                GetWallBlockedReach(dir, targetReach);

            fullEnd =
                origin +
                (Vector3)(dir * fullBlockedReach);

            SetLine(start, fullEnd);

            CheckWhipSegment(
                start,
                fullEnd,
                ref uniqueCount,
                ref momentumGrantedThisSwing,
                ref hitstopRequestedThisSwing
            );

            yield return null;
        }

        t = 0f;

        while (t < retractTime)
        {
            t += Time.deltaTime * GetAttackSpeedMultiplier();

            float k =
                retractTime <= 0f
                    ? 1f
                    : Mathf.Clamp01(t / retractTime);

            origin = GetLineOrigin();

            start =
                origin +
                (Vector3)(dir * lineStartOffset);

            fullBlockedReach =
                GetWallBlockedReach(dir, targetReach);

            fullEnd =
                origin +
                (Vector3)(dir * fullBlockedReach);

            Vector3 retractEnd =
                Vector3.Lerp(fullEnd, start, k);

            SetLine(start, retractEnd);

            PullAPDuringRetraction(
                start,
                retractEnd,
                Mathf.Max(0f, retractTime - t)
            );

            yield return null;
        }

        if (whipLine != null)
            whipLine.enabled = false;

        swingRunning = false;
        swingRoutine = null;
    }

    private void PullAPDuringRetraction(
        Vector3 lineStart,
        Vector3 lineEnd,
        float remainingRetractTime)
    {
        if (!pullAPOnRetract || apPullRadius <= 0f)
        {
            debugAPPullTargetsThisFrame = 0;
            return;
        }

        float persistence = Mathf.Max(
            0.02f,
            remainingRetractTime + apPullPersistenceAfterRetract
        );

        debugAPPullTargetsThisFrame =
            APParticleSystem.PullTowardCollectorAlongSegment(
                lineStart,
                lineEnd,
                apPullRadius,
                apPullAcceleration,
                apPullMaximumSpeed,
                persistence
            );

        debugAPPullTargetsPeak = Mathf.Max(
            debugAPPullTargetsPeak,
            debugAPPullTargetsThisFrame
        );
    }

    private void CheckWhipSegment(
        Vector3 from,
        Vector3 to,
        ref int uniqueCount,
        ref bool momentumGrantedThisSwing,
        ref bool hitstopRequestedThisSwing)
    {
        Vector2 a = from;
        Vector2 b = to;

        Vector2 delta = b - a;
        float length = delta.magnitude;

        if (length <= 0.001f || enemyMask.value == 0)
            return;

        Vector2 dir = delta / length;
        Vector2 center = (a + b) * 0.5f;
        Vector2 size = new Vector2(length, width);

        float angle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg;

        SpellDeflectionUtility.DeflectInBox(
            gameObject,
            center,
            size,
            angle,
            dir,
            ~0);

        int count = Physics2D.OverlapBoxNonAlloc(
            center,
            size,
            angle,
            hitCols,
            enemyMask
        );

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitCols[i];

            if (col == null)
                continue;

            EnemyHealth enemy =
                col.GetComponentInParent<EnemyHealth>();

            if (enemy == null)
                continue;

            if (HasAlreadyHit(enemy, uniqueCount))
                continue;

            if (uniqueCount >= uniqueEnemies.Length ||
                uniqueCount >= maxTargets)
            {
                break;
            }

            uniqueEnemies[uniqueCount] = enemy;
            uniqueCount++;

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

            if (!hitstopRequestedThisSwing)
            {
                HitstopManager.Request(whipHitstop);
                hitstopRequestedThisSwing = true;
            }

            EnemyStunnable stunnable =
                enemy.GetComponentInParent<EnemyStunnable>();

            if (stunnable != null)
                basicHitReaction.Apply(stunnable);

            SpawnAPForEnemyHit(enemy);

            // Momentum is deliberately once per swing, unlike AP, which
            // continues to stack for every unique enemy hit.
            if (!momentumGrantedThisSwing)
            {
                AttackMomentumManager manager =
                    AttackMomentumManager.Instance;

                if (manager != null)
                {
                    manager.RegisterMomentum(
                        momentumGainOnSuccessfulSwing
                    );

                    momentumGrantedThisSwing = true;

                    if (logMomentumGain)
                    {
                        Debug.Log(
                            $"Whip Momentum +{momentumGainOnSuccessfulSwing}",
                            this
                        );
                    }
                }
            }
        }
    }

    private float GetAttackSpeedMultiplier()
    {
        return Mathf.Max(
            0.02f,
            SpellStatModifierUtility.Evaluate(
                gameObject,
                SpellActorStat.BasicAttackSpeed,
                1f));
    }

    private bool HasAlreadyHit(
        EnemyHealth enemy,
        int uniqueCount)
    {
        for (int i = 0; i < uniqueCount; i++)
        {
            if (uniqueEnemies[i] == enemy)
                return true;
        }

        return false;
    }

    private void SpawnAPForEnemyHit(EnemyHealth enemy)
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null)
            return;

        PartyManager.CharacterState active = pm.Active;

        if (active == null || active.def == null)
            return;

        Vector2 enemyPosition = enemy != null
            ? (Vector2)enemy.transform.position
            : (Vector2)hitOrigin.position;

        APParticleSystem.SpawnReward(
            enemyPosition,
            Mathf.Max(0, apGainOnHit),
            enemyPosition - (Vector2)hitOrigin.position,
            enemy
        );

        if (logApGain)
        {
            string enemyName =
                enemy != null
                    ? enemy.name
                    : "enemy";

            Debug.Log(
                $"Whip knocked {apGainOnHit} collectible AP " +
                $"from {enemyName}.",
                this
            );
        }
    }

    private void SpawnVfx(
        Vector2 origin,
        Vector2 dir)
    {
        if (whipVfxPrefab == null)
            return;

        Vector3 position =
            origin +
            dir * vfxForwardOffset;

        float angle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg +
            vfxAngleOffset;

        Instantiate(
            whipVfxPrefab,
            position,
            Quaternion.Euler(0f, 0f, angle)
        );
    }

    private void SetupWhipLine()
    {
        if (whipLine == null && createLineIfMissing)
        {
            GameObject lineObject =
                new GameObject("WhipLine_Placeholder");

            lineObject.transform.SetParent(
                transform,
                false
            );

            whipLine =
                lineObject.AddComponent<LineRenderer>();
        }

        if (whipLine == null)
            return;

        whipLine.enabled = false;
        whipLine.useWorldSpace = true;
        whipLine.positionCount = 2;
        whipLine.loop = false;

        whipLine.startWidth = lineWidth;
        whipLine.endWidth = lineWidth;

        whipLine.startColor = lineColor;
        whipLine.endColor = lineColor;

        whipLine.numCapVertices = 6;
        whipLine.numCornerVertices = 6;
        whipLine.alignment = LineAlignment.View;
        whipLine.textureMode = LineTextureMode.Stretch;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find(
                "Universal Render Pipeline/Unlit"
            );
        }

        if (shader == null)
            shader = Shader.Find("Hidden/Internal-Colored");

        if (shader != null)
        {
            Material material = new Material(shader);
            material.color = lineColor;
            whipLine.material = material;
        }

        whipLine.sortingLayerName =
            lineSortingLayerName;

        whipLine.sortingOrder =
            lineSortingOrder;
    }

    private Vector3 GetLineOrigin()
    {
        Vector3 origin =
            hitOrigin != null
                ? hitOrigin.position
                : transform.position;

        origin.z += lineZOffset;
        return origin;
    }

    private void SetLine(
        Vector3 start,
        Vector3 end)
    {
        if (whipLine == null)
            return;

        whipLine.SetPosition(0, start);
        whipLine.SetPosition(1, end);
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

        Vector2 origin =
            originTransform.position;

        Vector2 dir =
            lastAimDir.sqrMagnitude > 0.0001f
                ? lastAimDir.normalized
                : Vector2.right;

        float gizmoReach = range;

        if (hasMouseWorld)
        {
            float mouseDistance =
                Vector2.Distance(
                    origin,
                    lastMouseWorld
                );

            gizmoReach = Mathf.Clamp(
                mouseDistance,
                lineStartOffset,
                range
            );
        }

        if (Application.isPlaying)
        {
            gizmoReach =
                GetWallBlockedReach(
                    dir,
                    gizmoReach
                );
        }

        Vector2 center =
            origin +
            dir * (gizmoReach * 0.5f);

        Vector2 size =
            new Vector2(gizmoReach, width);

        float angle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg;

        Gizmos.color = Color.magenta;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        Gizmos.matrix = Matrix4x4.TRS(
            center,
            Quaternion.Euler(0f, 0f, angle),
            Vector3.one
        );

        Gizmos.DrawWireCube(
            Vector3.zero,
            size
        );

        Gizmos.matrix = oldMatrix;

        Gizmos.DrawLine(
            origin,
            origin + dir * gizmoReach
        );
    }
#endif
}
