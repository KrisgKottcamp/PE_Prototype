using System;
using System.Collections.Generic;
using UnityEngine;

public class SpellCaster : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-resolved from ProjectileShooter muzzle, CombatSkillSystem vfxOrigin, or 'Muzzle' child.")]
    [SerializeField] private Transform firePoint;
    [Tooltip("Auto-resolved to 'EnemyHurtbox' layer if left at 0.")]
    [SerializeField] private LayerMask defaultHitMask;

    [Header("Movement Spell")]
    [SerializeField] private Rigidbody2D casterBody;
    [Tooltip("Auto-resolved from ProjectileShooter's spawnObstacleMask if unset.")]
    [SerializeField] private LayerMask movementBlockMask;

    [Header("Auto-Resolve")]
    [Tooltip("Layer name used for hit detection when defaultHitMask is unset.")]
    [SerializeField] private string autoHitMaskLayer = "EnemyHurtbox";
    [Tooltip("Layer names used for movement blocking when movementBlockMask is unset.")]
    [SerializeField] private string[] autoMovementBlockLayers = new[] { "Obstacles", "Wall", "CombatWall", "Obstacle", "Cover", "Walls" };

    public SpellPhase CurrentPhase { get; private set; } = SpellPhase.Idle;
    public SpellDefinition ActiveSpell { get; private set; }
    public bool IsCasting => CurrentPhase != SpellPhase.Idle;

    public event Action<SpellPhase> OnPhaseChanged;
    public event Action<SpellDefinition> OnSpellComplete;

    private float phaseTimer;
    private float totalCastTimer;
    private const float MAX_CAST_DURATION = 15f;
    private int fireIndex;
    private float fireDelayTimer;
    private Vector2 aimDirection = Vector2.right;
    private float currentAngleOffset;
    private bool hasFiredInitialShot;

    // Chain queue
    private readonly Queue<SpellDefinition> chainQueue = new();

    // Cooldown tracking
    private readonly Dictionary<SpellDefinition, float> cooldowns = new();

    private EffectReceiver selfEffectReceiver;
    private AimTracker aimTracker;
    private PlayerMoveSpeedModifierReceiverV2 moveSpeedReceiver;
    private Camera mainCam;

    // Dash state (driven directly from Update, no separate component)
    private bool dashActive;
    private Vector2 dashDirection;
    private float dashSpeed;
    private float dashDistance;
    private Vector2 dashStartPos;
    private CombatPawnMover cachedMover;

    private void Awake()
    {
        ResolveFirePoint();
        ResolveHitMask();
        ResolveMovementBlockMask();

        if (casterBody == null)
            casterBody = GetComponent<Rigidbody2D>();

        aimTracker = GetComponent<AimTracker>();
        moveSpeedReceiver = GetComponent<PlayerMoveSpeedModifierReceiverV2>();
        mainCam = Camera.main;

        selfEffectReceiver = GetComponent<EffectReceiver>();
        if (selfEffectReceiver == null)
            selfEffectReceiver = gameObject.AddComponent<EffectReceiver>();
    }

    private void ResolveFirePoint()
    {
        if (firePoint != null)
            return;

        // Try ProjectileShooter's muzzle (player pawn)
        ProjectileShooter shooter = GetComponent<ProjectileShooter>();
        if (shooter != null)
        {
            System.Reflection.FieldInfo muzzleField = typeof(ProjectileShooter)
                .GetField("muzzle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (muzzleField != null)
            {
                Transform muzzle = muzzleField.GetValue(shooter) as Transform;
                if (muzzle != null)
                {
                    firePoint = muzzle;
                    return;
                }
            }
        }

        // Try CombatSkillSystem's vfxOrigin
        CombatSkillSystem skillSystem = GetComponent<CombatSkillSystem>();
        if (skillSystem != null)
        {
            System.Reflection.FieldInfo vfxField = typeof(CombatSkillSystem)
                .GetField("vfxOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (vfxField != null)
            {
                Transform vfx = vfxField.GetValue(skillSystem) as Transform;
                if (vfx != null)
                {
                    firePoint = vfx;
                    return;
                }
            }
        }

        // Try child named "Muzzle" (enemy pattern)
        Transform muzzleChild = FindChildByName(transform, "muzzle");
        if (muzzleChild != null)
        {
            firePoint = muzzleChild;
            return;
        }

        firePoint = transform;
    }

    private void ResolveHitMask()
    {
        if (defaultHitMask.value != 0)
            return;

        int layer = LayerMask.NameToLayer(autoHitMaskLayer);
        if (layer >= 0)
            defaultHitMask = 1 << layer;
        else
            Debug.LogWarning($"SpellCaster: Layer '{autoHitMaskLayer}' not found. Set defaultHitMask manually.", this);
    }

    private void ResolveMovementBlockMask()
    {
        if (movementBlockMask.value != 0)
            return;

        ProjectileShooter shooter = GetComponent<ProjectileShooter>();
        if (shooter != null)
        {
            System.Reflection.FieldInfo obstacleField = typeof(ProjectileShooter)
                .GetField("spawnObstacleMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (obstacleField != null)
            {
                LayerMask mask = (LayerMask)obstacleField.GetValue(shooter);
                if (mask.value != 0)
                {
                    movementBlockMask = mask;
                    return;
                }
            }
        }

        int combined = 0;
        for (int i = 0; i < autoMovementBlockLayers.Length; i++)
        {
            int layer = LayerMask.NameToLayer(autoMovementBlockLayers[i]);
            if (layer >= 0)
                combined |= 1 << layer;
        }

        if (combined != 0)
            movementBlockMask = combined;
    }

    private void ValidateSpellBase(SpellDefinition spell)
    {
        if (spell.spellBase == SpellBase.Casting && spell.displayName != null)
        {
            string lower = spell.displayName.ToLowerInvariant();
            if (lower.Contains("dash") || lower.Contains("blink") || lower.Contains("teleport") || lower.Contains("boost"))
            {
                Debug.LogWarning(
                    $"SpellCaster: Spell '{spell.displayName}' has spellBase=Casting but its name suggests Movement. " +
                    "Set spellBase to Movement on the SpellDefinition asset if this is a movement spell.", this);
            }
        }
    }

    private static Transform FindChildByName(Transform parent, string nameLC)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.ToLowerInvariant().Contains(nameLC))
                return child;

            Transform deeper = FindChildByName(child, nameLC);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private void Update()
    {
        UpdateCooldowns();

        if (dashActive)
            UpdateDash();

        if (CurrentPhase == SpellPhase.Idle)
            return;

        phaseTimer -= Time.deltaTime;
        totalCastTimer += Time.deltaTime;

        if (totalCastTimer > MAX_CAST_DURATION)
        {
            Debug.LogWarning($"SpellCaster: Spell '{ActiveSpell?.displayName}' exceeded {MAX_CAST_DURATION}s — force ending. Phase was {CurrentPhase}.", this);
            chainQueue.Clear();
            EndSpell();
            return;
        }

        if (CurrentPhase == SpellPhase.Firing)
            UpdateFiring();

        if (CurrentPhase == SpellPhase.Channeling && ActiveSpell != null &&
            ActiveSpell.modifiers.channeling &&
            ActiveSpell.modifiers.channelMode == ChannelMode.Continuous)
        {
            UpdateFiring();
        }

        if (phaseTimer <= 0f)
            AdvancePhase();
    }

    private void UpdateDash()
    {
        if (casterBody == null)
        {
            EndDashState();
            return;
        }

        float traveled = Vector2.Distance(dashStartPos, casterBody.position);
        float remaining = dashDistance - traveled;
        if (remaining <= 0f)
        {
            EndDashState();
            return;
        }

        float step = dashSpeed * Time.deltaTime;
        if (step > remaining)
            step = remaining;

        Vector2 origin = casterBody.position + dashDirection * 0.05f;
        RaycastHit2D hit = Physics2D.Raycast(origin, dashDirection, step + 0.1f, movementBlockMask);
        if (hit.collider != null)
        {
            casterBody.position = hit.point - dashDirection * 0.05f;
            EndDashState();
            return;
        }

        casterBody.position += dashDirection * step;
    }

    private void EndDashState()
    {
        dashActive = false;

        if (casterBody != null)
            casterBody.linearVelocity = Vector2.zero;

        if (cachedMover != null)
            cachedMover.enabled = true;
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    public bool CanCast(SpellDefinition spell)
    {
        if (spell == null || IsCasting)
            return false;

        if (cooldowns.TryGetValue(spell, out float remaining) && remaining > 0f)
            return false;

        return true;
    }

    public float GetCooldownRemaining(SpellDefinition spell)
    {
        if (spell == null)
            return 0f;
        cooldowns.TryGetValue(spell, out float remaining);
        return Mathf.Max(0f, remaining);
    }

    public bool Cast(SpellDefinition spell, Vector2 direction)
    {
        if (spell != null && spell.spellBase == SpellBase.Movement)
            return CastMovement(spell, direction);

        if (!CanCast(spell))
            return false;

        ValidateSpellBase(spell);

        aimDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        chainQueue.Clear();
        if (spell.chain != null)
        {
            for (int i = 0; i < spell.chain.Count; i++)
            {
                if (spell.chain[i] != null)
                    chainQueue.Enqueue(spell.chain[i]);
            }
        }

        BeginSpell(spell);
        return true;
    }

    private bool CastMovement(SpellDefinition spell, Vector2 direction)
    {
        if (spell == null)
            return false;

        if (cooldowns.TryGetValue(spell, out float remaining) && remaining > 0f)
            return false;

        if (dashActive)
            return false;

        if (IsCasting)
        {
            chainQueue.Clear();
            EndSpell();
        }

        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 casterPos = casterBody != null ? casterBody.position : (Vector2)transform.position;
        Vector2 toMouse = mouseWorld - casterPos;
        aimDirection = toMouse.sqrMagnitude > 0.0001f ? toMouse.normalized : Vector2.right;

        ActiveSpell = spell;
        ExecuteMovementSpell(mouseWorld);
        cooldowns[spell] = spell.cooldown;
        ActiveSpell = null;
        OnSpellComplete?.Invoke(spell);
        return true;
    }

    private Vector2 GetMouseWorldPosition()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return transform.position;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -mainCam.transform.position.z;
        return mainCam.ScreenToWorldPoint(mouse);
    }

    public void SetAimDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
            aimDirection = direction.normalized;
    }

    public void InterruptCast()
    {
        if (!IsCasting)
            return;

        chainQueue.Clear();
        EndSpell();
    }

    // -------------------------------------------------------
    // Phase state machine
    // -------------------------------------------------------

    private void BeginSpell(SpellDefinition spell)
    {
        ActiveSpell = spell;
        fireIndex = 0;
        fireDelayTimer = 0f;
        currentAngleOffset = 0f;
        hasFiredInitialShot = false;
        totalCastTimer = 0f;

        // Ensure build-up has at least one frame so the Firing phase runs properly
        float buildUp = spell.buildUpDuration > 0f ? spell.buildUpDuration : Time.deltaTime;
        SetPhase(SpellPhase.BuildUp, buildUp);

        if (spell.buildUpVfxPrefab != null)
            Instantiate(spell.buildUpVfxPrefab, firePoint.position, Quaternion.identity);

        if (!string.IsNullOrEmpty(spell.buildUpAnimTrigger))
            TriggerAnimation(spell.buildUpAnimTrigger);
    }

    private void AdvancePhase()
    {
        switch (CurrentPhase)
        {
            case SpellPhase.BuildUp:
                // Firing phase must last long enough for all fire events
                float firingTime = ActiveSpell.firingDuration;
                if (ActiveSpell.spellBase == SpellBase.Casting && ActiveSpell.fireCount > 1)
                {
                    float multiFireTime = (ActiveSpell.fireCount - 1) * ActiveSpell.fireDelay + 0.05f;
                    firingTime = Mathf.Max(firingTime, multiFireTime);
                }
                // Ensure at least one frame for the firing phase
                firingTime = Mathf.Max(firingTime, Time.deltaTime);
                SetPhase(SpellPhase.Firing, firingTime);
                if (ActiveSpell.firingVfxPrefab != null)
                    Instantiate(ActiveSpell.firingVfxPrefab, firePoint.position, GetAimRotation());
                if (!string.IsNullOrEmpty(ActiveSpell.firingAnimTrigger))
                    TriggerAnimation(ActiveSpell.firingAnimTrigger);
                ExecuteSpell();
                break;

            case SpellPhase.Firing:
                if (ActiveSpell.channelingDuration > 0f)
                    SetPhase(SpellPhase.Channeling, ActiveSpell.channelingDuration);
                else
                    GoToAntefire();
                break;

            case SpellPhase.Channeling:
                GoToAntefire();
                break;

            case SpellPhase.Antefire:
                FinishSpell();
                break;
        }
    }

    private void GoToAntefire()
    {
        SetPhase(SpellPhase.Antefire, ActiveSpell.antefireDuration);
        if (ActiveSpell.antefireVfxPrefab != null)
            Instantiate(ActiveSpell.antefireVfxPrefab, firePoint.position, Quaternion.identity);
        if (!string.IsNullOrEmpty(ActiveSpell.antefireAnimTrigger))
            TriggerAnimation(ActiveSpell.antefireAnimTrigger);
    }

    private void FinishSpell()
    {
        SpellDefinition completed = ActiveSpell;
        cooldowns[completed] = completed.cooldown;

        if (chainQueue.Count > 0)
        {
            SpellDefinition next = chainQueue.Dequeue();
            BeginSpell(next);
        }
        else
        {
            EndSpell();
        }

        OnSpellComplete?.Invoke(completed);
    }

    private void EndSpell()
    {
        ActiveSpell = null;
        SetPhase(SpellPhase.Idle, 0f);
    }

    private void SetPhase(SpellPhase phase, float duration)
    {
        CurrentPhase = phase;
        phaseTimer = Mathf.Max(0f, duration);
        OnPhaseChanged?.Invoke(phase);
    }

    // -------------------------------------------------------
    // Spell execution (routing by SpellBase)
    // -------------------------------------------------------

    private void ExecuteSpell()
    {
        if (ActiveSpell == null)
            return;

        switch (ActiveSpell.spellBase)
        {
            case SpellBase.Casting:
                // Fire the first shot immediately, then UpdateFiring handles the rest
                if (!hasFiredInitialShot)
                {
                    FireCastingSpell();
                    hasFiredInitialShot = true;
                    fireIndex = 1;
                    fireDelayTimer = ActiveSpell.fireDelay;
                    currentAngleOffset += ActiveSpell.modifiers.continuousAngleDeg;
                }
                break;
            case SpellBase.Movement:
                ExecuteMovementSpell();
                break;
            case SpellBase.Stat:
                ExecuteStatSpell();
                break;
        }
    }

    // -------------------------------------------------------
    // Casting spells
    // -------------------------------------------------------

    private void UpdateFiring()
    {
        if (ActiveSpell == null || ActiveSpell.spellBase != SpellBase.Casting)
            return;

        if (fireIndex >= ActiveSpell.fireCount)
            return;

        fireDelayTimer -= Time.deltaTime;
        if (fireDelayTimer > 0f)
            return;

        if (!ActiveSpell.lockAimDirection)
            RefreshAimDirection();

        FireCastingSpell();
        fireIndex++;
        fireDelayTimer = ActiveSpell.fireDelay;
        currentAngleOffset += ActiveSpell.modifiers.continuousAngleDeg;
    }

    private void RefreshAimDirection()
    {
        if (aimTracker == null)
            aimTracker = GetComponent<AimTracker>();

        if (aimTracker != null)
        {
            Vector2 dir = aimTracker.AimDir;
            if (dir.sqrMagnitude > 0.0001f)
                aimDirection = dir.normalized;
        }
    }

    private void FireCastingSpell()
    {
        SpellDefinition spell = ActiveSpell;
        Vector2 origin = firePoint.position;

        float baseAngle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;

        float finalAngle = baseAngle + spell.modifiers.initialAngleDeg + currentAngleOffset;
        if (spell.modifiers.setAngleDeg >= 0f)
            finalAngle = spell.modifiers.setAngleDeg + currentAngleOffset;

        switch (spell.projectileType)
        {
            case CastProjectileType.Bullet:
                FireBulletByPattern(origin, finalAngle, spell);
                break;

            case CastProjectileType.Cone:
                FireCone(origin, finalAngle, spell);
                break;

            case CastProjectileType.Circle:
                FireCircleAoE(origin, spell);
                break;

            case CastProjectileType.Bomb:
                FireBomb(origin, finalAngle, spell);
                break;

            case CastProjectileType.Beam:
                FireBeam(origin, finalAngle, spell);
                break;

            case CastProjectileType.Swipe:
                FireSwipe(origin, finalAngle, spell);
                break;

            case CastProjectileType.Helix:
                FireHelix(origin, finalAngle, spell);
                break;

            case CastProjectileType.Mine:
                FireMine(origin, spell);
                break;

            case CastProjectileType.TripWire:
                FireTripWire(origin, finalAngle, spell);
                break;

            case CastProjectileType.Zone:
                FireZone(origin, spell);
                break;
        }
    }

    private void FireBulletByPattern(Vector2 origin, float angle, SpellDefinition spell)
    {
        switch (spell.pattern)
        {
            case ProjectilePattern.Line:
                SpawnProjectile(origin, angle, spell);
                break;
            case ProjectilePattern.Circle:
                int count = Mathf.Max(1, spell.projectileCount);
                float step = 360f / count;
                for (int i = 0; i < count; i++)
                    SpawnProjectile(origin, angle + step * i, spell);
                break;
            case ProjectilePattern.Cone:
                FireCone(origin, angle, spell);
                break;
        }
    }

    private void FireCone(Vector2 origin, float centerAngle, SpellDefinition spell)
    {
        int count = Mathf.Max(1, spell.projectileCount);
        float totalArc = spell.coneDegree;

        if (count == 1)
        {
            SpawnProjectile(origin, centerAngle, spell);
            return;
        }

        float startAngle = centerAngle - totalArc * 0.5f;
        float step = totalArc / (count - 1);

        for (int i = 0; i < count; i++)
            SpawnProjectile(origin, startAngle + step * i, spell);
    }

    private static readonly Collider2D[] circleHitBuffer = new Collider2D[32];

    private void FireCircleAoE(Vector2 origin, SpellDefinition spell)
    {
        float radius = spell.range;
        LayerMask mask = GetHitMask(spell);

        int count = Physics2D.OverlapCircleNonAlloc(origin, radius, circleHitBuffer, mask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = circleHitBuffer[i];
            if (col == null)
                continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
                enemy.TakeDamage(spell.damage);

            EffectReceiver receiver = col.GetComponentInParent<EffectReceiver>();
            if (receiver != null && spell.onHitEffects != null)
            {
                for (int j = 0; j < spell.onHitEffects.Count; j++)
                {
                    if (spell.onHitEffects[j] != null)
                        receiver.ApplyEffect(spell.onHitEffects[j]);
                }
            }
        }

        if (spell.firingVfxPrefab != null)
            Instantiate(spell.firingVfxPrefab, origin, Quaternion.identity);
    }

    private void FireBomb(Vector2 origin, float angle, SpellDefinition spell)
    {
        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 offset = mouseWorld - origin;
        float dist = offset.magnitude;
        if (dist > spell.range)
            mouseWorld = origin + offset.normalized * spell.range;

        GameObject bombObj = new GameObject("SpellBomb");
        bombObj.transform.position = mouseWorld;

        SpellBomb bomb = bombObj.AddComponent<SpellBomb>();
        bomb.Initialize(spell, spell.bombDelay, spell.bombRadius, spell.damage,
            spell.damageType, spell.onHitEffects, GetHitMask(spell));
    }

    private void FireMine(Vector2 origin, SpellDefinition spell)
    {
        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 offset = mouseWorld - origin;
        if (offset.magnitude > spell.range)
            mouseWorld = origin + offset.normalized * spell.range;

        GameObject mineObj = new GameObject("SpellMine");
        mineObj.transform.position = mouseWorld;

        SpellMine mine = mineObj.AddComponent<SpellMine>();
        mine.Initialize(spell, spell.mineDetectionRadius, spell.mineFuseDelay,
            spell.bombRadius, spell.damage, spell.damageType,
            spell.onHitEffects, GetHitMask(spell));
    }

    private void FireTripWire(Vector2 origin, float angle, SpellDefinition spell)
    {
        Vector2 dir = AngleToDir(angle);

        GameObject wireObj = new GameObject("SpellTripWire");
        wireObj.transform.position = origin;

        SpellTripWire wire = wireObj.AddComponent<SpellTripWire>();
        wire.Initialize(spell, origin, dir, spell.range,
            spell.damage, spell.damageType,
            spell.onHitEffects, GetHitMask(spell), movementBlockMask);
    }

    private void FireZone(Vector2 origin, SpellDefinition spell)
    {
        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 offset = mouseWorld - origin;
        if (offset.magnitude > spell.range)
            mouseWorld = origin + offset.normalized * spell.range;

        GameObject zoneObj = new GameObject("SpellZone");
        zoneObj.transform.position = mouseWorld;

        SpellZone zone = zoneObj.AddComponent<SpellZone>();
        zone.Initialize(spell, spell.bombRadius, spell.zoneDuration,
            spell.zoneTickRate, spell.damage, spell.damageType,
            spell.onHitEffects, GetHitMask(spell),
            spell.modifiers.vortex, spell.modifiers.vortexStrength);
    }

    private void FireBeam(Vector2 origin, float angle, SpellDefinition spell)
    {
        Vector2 dir = AngleToDir(angle);
        LayerMask mask = GetHitMask(spell);

        float maxDist = spell.range;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, maxDist, mask);
        float actualDist = hit.collider != null ? hit.distance : maxDist;

        GameObject beamObj = new GameObject("SpellBeam");
        beamObj.transform.position = origin;

        Color beamColor = spell.projectileVisual != null
            ? spell.projectileVisual.color : new Color(0.8f, 0.9f, 1f, 0.8f);

        SpellBeam beam = beamObj.AddComponent<SpellBeam>();
        beam.Initialize(spell, origin, dir, actualDist, spell.beamWidth,
            spell.damage, spell.damageType, spell.onHitEffects, mask,
            spell.hitBehavior == ProjectileHitBehavior.Phase, beamColor);
    }

    private void FireSwipe(Vector2 origin, float centerAngle, SpellDefinition spell)
    {
        GameObject swipeObj = new GameObject("SpellSwipe");
        swipeObj.transform.position = origin;
        swipeObj.transform.SetParent(transform);

        SpellSwipe swipe = swipeObj.AddComponent<SpellSwipe>();
        swipe.Initialize(spell, origin, centerAngle, spell.swipeDegree, spell.range,
            spell.firingDuration, spell.damage, spell.damageType,
            spell.onHitEffects, GetHitMask(spell));
    }

    private void FireHelix(Vector2 origin, float angle, SpellDefinition spell)
    {
        int count = Mathf.Max(1, spell.projectileCount);
        float step = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float spawnAngle = angle + step * i;
            SpawnHelixProjectile(origin, spawnAngle, spell);
        }
    }

    // -------------------------------------------------------
    // Projectile spawning
    // -------------------------------------------------------

    private void SpawnProjectile(Vector2 origin, float angleDeg, SpellDefinition spell)
    {
        Vector2 dir = AngleToDir(angleDeg);

        GameObject obj = CreateProjectileObject(origin, angleDeg, spell);

        SpellProjectile proj = obj.GetComponent<SpellProjectile>();
        if (proj == null)
            proj = obj.AddComponent<SpellProjectile>();

        proj.Initialize(
            dir, spell.projectileSpeed, spell.range / Mathf.Max(0.01f, spell.projectileSpeed),
            spell.damage, spell.damageType, spell.hitBehavior,
            spell.modifiers, spell.onHitEffects, GetHitMask(spell),
            spell.modifiers.secondaryEffect, spell.projectileVisual, spell
        );
    }

    private void SpawnHelixProjectile(Vector2 origin, float angleDeg, SpellDefinition spell)
    {
        Vector2 dir = AngleToDir(angleDeg);

        GameObject obj = CreateProjectileObject(origin, angleDeg, spell);

        SpellProjectileHelix helix = obj.GetComponent<SpellProjectileHelix>();
        if (helix == null)
            helix = obj.AddComponent<SpellProjectileHelix>();

        helix.Initialize(
            origin, dir, spell.projectileSpeed, spell.range / Mathf.Max(0.01f, spell.projectileSpeed),
            spell.helixInitialRadius, spell.helixGrowthRate,
            spell.damage, spell.damageType, spell.hitBehavior,
            spell.onHitEffects, GetHitMask(spell), spell.projectileVisual
        );
    }

    private GameObject CreateProjectileObject(Vector2 origin, float angleDeg, SpellDefinition spell)
    {
        if (spell.projectileVisual != null && spell.projectileVisual.sprite != null)
            return spell.projectileVisual.CreateInstance(origin, angleDeg);

        if (spell.projectilePrefab != null)
            return Instantiate(spell.projectilePrefab, origin, Quaternion.Euler(0, 0, angleDeg));

        if (spell.projectileVisual != null)
            return spell.projectileVisual.CreateInstance(origin, angleDeg);

        return CreateFallbackProjectile(origin, angleDeg);
    }

    private GameObject CreateFallbackProjectile(Vector2 origin, float angleDeg)
    {
        GameObject obj = new GameObject("SpellProjectile");
        obj.transform.position = origin;
        obj.transform.rotation = Quaternion.Euler(0, 0, angleDeg);

        int projLayer = LayerMask.NameToLayer("Projectile");
        if (projLayer < 0)
            projLayer = LayerMask.NameToLayer("PlayerProjectile");
        if (projLayer >= 0)
            obj.layer = projLayer;

        string sortLayer = ResolveFallbackSortingLayer();

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ProjectileDefinition.CreateFallbackSprite();
        sr.color = new Color(0.8f, 0.9f, 1f, 1f);
        sr.sortingLayerName = sortLayer;
        sr.sortingOrder = 10;
        obj.transform.localScale = new Vector3(0.3f, 0.3f, 1f);

        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        col.isTrigger = true;

        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        TrailRenderer trail = obj.AddComponent<TrailRenderer>();
        trail.time = 0.15f;
        trail.startWidth = 0.12f;
        trail.endWidth = 0f;
        trail.startColor = new Color(0.8f, 0.9f, 1f, 0.8f);
        trail.endColor = new Color(0.8f, 0.9f, 1f, 0f);
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.sortingLayerName = sortLayer;
        trail.sortingOrder = 9;
        trail.numCornerVertices = 2;
        trail.numCapVertices = 2;
        trail.minVertexDistance = 0.02f;

        return obj;
    }

    private static string ResolveFallbackSortingLayer()
    {
        string[] candidates = { "Foreground", "VFX", "Characters" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (SortingLayer.NameToID(candidates[i]) != 0)
                return candidates[i];
        }
        return "Default";
    }

    // -------------------------------------------------------
    // Movement spells
    // -------------------------------------------------------

    private void ExecuteMovementSpell(Vector2 mouseWorld = default)
    {
        if (ActiveSpell == null)
            return;

        switch (ActiveSpell.movementType)
        {
            case MovementSpellType.Dash:
                ExecuteDash();
                break;
            case MovementSpellType.Boost:
                ExecuteBoost();
                break;
            case MovementSpellType.Blink:
                ExecuteBlink();
                break;
            case MovementSpellType.Teleport:
                ExecuteTeleport(mouseWorld);
                break;
        }

        ApplyEffectList(ActiveSpell.movementEffects, selfEffectReceiver);
    }

    private void ExecuteDash()
    {
        if (casterBody == null)
            return;

        cachedMover = GetComponent<CombatPawnMover>();
        if (cachedMover != null)
            cachedMover.enabled = false;

        dashDirection = aimDirection;
        dashSpeed = ActiveSpell.dashSpeed;
        dashDistance = ActiveSpell.movementDistance;
        dashStartPos = casterBody.position;
        dashActive = true;
    }

    private void ExecuteBoost()
    {
        if (moveSpeedReceiver == null)
            moveSpeedReceiver = GetComponent<PlayerMoveSpeedModifierReceiverV2>();

        if (moveSpeedReceiver != null)
        {
            moveSpeedReceiver.ApplyBoost(this, ActiveSpell.boostMultiplier, ActiveSpell.boostDuration);
        }
    }

    private void ExecuteBlink()
    {
        if (casterBody == null)
            return;

        Vector2 origin = casterBody.position;
        float dist = ActiveSpell.movementDistance;

        RaycastHit2D hit = Physics2D.Raycast(origin, aimDirection, dist, movementBlockMask);

        Vector2 target;
        if (hit.collider != null)
            target = hit.point - aimDirection * 0.1f;
        else
            target = origin + aimDirection * dist;

        casterBody.position = target;
    }

    private void ExecuteTeleport(Vector2 mouseWorld)
    {
        if (casterBody == null)
            return;

        Vector2 origin = casterBody.position;
        Vector2 offset = mouseWorld - origin;
        float dist = offset.magnitude;
        float maxDist = ActiveSpell.movementDistance;

        if (dist > maxDist)
            mouseWorld = origin + offset.normalized * maxDist;

        casterBody.position = mouseWorld;
    }

    // -------------------------------------------------------
    // Stat spells
    // -------------------------------------------------------

    private void ExecuteStatSpell()
    {
        if (ActiveSpell == null)
            return;

        EffectReceiver target = selfEffectReceiver;

        ApplyEffectList(ActiveSpell.applyEffects, target);

        if (ActiveSpell.removeEffectIds != null)
        {
            for (int i = 0; i < ActiveSpell.removeEffectIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(ActiveSpell.removeEffectIds[i]))
                    target?.RemoveEffect(ActiveSpell.removeEffectIds[i]);
            }
        }
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    private void ApplyEffectList(List<EffectDefinition> effects, EffectReceiver receiver)
    {
        if (effects == null || receiver == null)
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null)
                receiver.ApplyEffect(effects[i]);
        }
    }

    private LayerMask GetHitMask(SpellDefinition spell)
    {
        return spell.hitMask.value != 0 ? spell.hitMask : defaultHitMask;
    }

    private Quaternion GetAimRotation()
    {
        float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0, 0, angle);
    }

    private static Vector2 AngleToDir(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    private void TriggerAnimation(string trigger)
    {
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
            anim.SetTrigger(trigger);
    }

    private void UpdateCooldowns()
    {
        float dt = Time.deltaTime;
        List<SpellDefinition> keys = new(cooldowns.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            float val = cooldowns[keys[i]] - dt;
            if (val <= 0f)
                cooldowns.Remove(keys[i]);
            else
                cooldowns[keys[i]] = val;
        }
    }
}
