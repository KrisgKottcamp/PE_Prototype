using System.Collections;
using UnityEngine;

/// <summary>
/// CombatSkillSystem
/// - Supports skills that:
///   1) Spawn VFX (cast + impact)
///   2) Do melee hitbox damage (only if hitbox overlaps enemies)
///   3) Fire a projectile (damage happens only on projectile collision)
///   4) Heal a chosen party member (generic party target support)
///   5) Apply a timed SpeedBoost to the shared combat pawn
///
/// Intended usage:
/// - For normal skills (no party target): call TryUseSkill(skill)
/// - For party-target skills (like Health Friend):
///     BeginCast(skill, out pending)
///     open party picker
///     on confirm: ResolveCast(pending, targetIndex)
///     on cancel: CancelCast(pending)
///
/// AP is spent immediately on BeginCast/TryUseSkill.
/// If you cancel a pending cast, AP is refunded.
/// Skill cost multiplier is applied when the cast resolves (so cancel does not increase costs).
/// </summary>
public class CombatSkillSystem : MonoBehaviour
{
    [Header("Scene Refs (optional)")]
    [SerializeField] private AimTracker aim;
    [SerializeField] private ProjectileShooter shooter;
    [SerializeField] private Transform vfxOrigin;
    [SerializeField] private HealingFeedback2D healingFeedback;

    [Header("Defaults (used if SkillDefinition fields are unset)")]
    [SerializeField] private LayerMask defaultEnemyHurtboxMask; // EnemyHurtbox
    [SerializeField] private float defaultMeleeRange = 0.9f;
    [SerializeField] private float defaultMeleeRadius = 0.4f;
    [SerializeField] private int defaultMeleeMaxTargets = 1;

    [Header("Timing")]
    [Tooltip("Leave false for normal gameplay timing. Enable only if impact delays should ignore time slow.")]
    [SerializeField] private bool useUnscaledImpactDelay = false;

    private bool isCasting;

    private readonly Collider2D[] hitCols = new Collider2D[16];
    private readonly EnemyHealth[] uniqueMeleeEnemies =
        new EnemyHealth[16];

    public class PendingCast
    {
        public SkillDefinition skill;
        public int ownerIndex;
        public int apCost;
        public Vector2 aimDirAtCommit;
    }

    private void Awake()
    {
        if (aim == null) aim = GetComponent<AimTracker>();
        if (shooter == null) shooter = GetComponent<ProjectileShooter>();
        if (vfxOrigin == null) vfxOrigin = transform;

        if (healingFeedback == null)
            healingFeedback = GetComponent<HealingFeedback2D>();

        if (healingFeedback == null)
            healingFeedback = gameObject.AddComponent<HealingFeedback2D>();
    }

    // ----------------------------
    // Public API
    // ----------------------------

    public int GetScaledCost(SkillDefinition skill)
    {
        var pm = PartyManager.Instance;
        if (pm == null || skill == null) return int.MaxValue;

        float mult = Mathf.Max(1f, pm.Active.skillCostMultiplier);
        return Mathf.Max(0, Mathf.CeilToInt(skill.baseApCost * mult));
    }

    public bool CanUse(SkillDefinition skill)
    {
        var pm = PartyManager.Instance;
        if (pm == null || skill == null) return false;

        return pm.Active.currentAP >= GetScaledCost(skill);
    }

    /// <summary>
    /// For skills that do NOT require a party target menu.
    /// If skill.requiresPartyTarget is true, this returns false.
    /// </summary>
    public bool TryUseSkill(SkillDefinition skill)
    {
        if (skill == null) return false;
        if (isCasting) return false;

        if (skill.requiresPartyTarget)
        {
            Debug.LogWarning($"TryUseSkill called for '{skill.name}', but it requires a party target. Use BeginCast + ResolveCast.");
            return false;
        }

        if (!BeginCast(skill, out PendingCast pending))
            return false;

        // No target needed, resolve immediately
        ResolveCast(pending, null);
        return true;
    }

    /// <summary>
    /// Starts a cast, spends AP immediately, spawns cast VFX immediately, then returns a PendingCast.
    /// Use ResolveCast(pending, partyTargetIndex) to finish it, or CancelCast(pending) to refund.
    /// </summary>
    public bool BeginCast(SkillDefinition skill, out PendingCast pending)
    {
        pending = null;

        if (skill == null) return false;
        if (isCasting) return false;

        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return false;

        int ownerIndex = pm.activeIndex;
        var owner = pm.party[ownerIndex];
        if (owner == null || owner.def == null) return false;

        int cost = GetScaledCost(skill);
        if (owner.currentAP < cost) return false;

        // Spend AP immediately
        owner.currentAP -= cost;

        // Cache aim direction at commit time (feels consistent)
        Vector2 dir = GetAimDir();

        pending = new PendingCast
        {
            skill = skill,
            ownerIndex = ownerIndex,
            apCost = cost,
            aimDirAtCommit = dir
        };

        // Cast VFX now
        SpawnVfx(
            skill.castVfxPrefab,
            vfxOrigin.position,
            dir,
            skill.castVfxAngleOffset,
            skill.castVfxForwardOffset
        );

        return true;
    }

    /// <summary>
    /// Refunds AP for a pending cast (for example if the player cancels the target menu).
    /// Does NOT increase skill cost multiplier.
    /// </summary>
    public void CancelCast(PendingCast pending)
    {
        if (pending == null) return;

        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null) return;

        if (pending.ownerIndex < 0 || pending.ownerIndex >= pm.party.Count) return;

        var owner = pm.party[pending.ownerIndex];
        if (owner == null || owner.def == null) return;

        int maxAP = Mathf.Max(0, owner.def.maxAP);
        owner.currentAP = Mathf.Clamp(owner.currentAP + pending.apCost, 0, maxAP);
    }

    /// <summary>
    /// Resolves the pending cast. For party-target skills, pass the chosen party index.
    /// For non-party-target casts, pass null.
    /// </summary>
    public void ResolveCast(PendingCast pending, int? partyTargetIndex)
    {
        if (pending == null || pending.skill == null) return;
        if (isCasting) return;

        StartCoroutine(ResolveRoutine(pending, partyTargetIndex));
    }

    // ----------------------------
    // Core execution
    // ----------------------------

    private IEnumerator ResolveRoutine(PendingCast pending, int? partyTargetIndex)
    {
        isCasting = true;

        SkillDefinition skill = pending.skill;

        // Delay to match skill timing.
        float delay = Mathf.Max(0f, skill.impactDelay);
        if (delay > 0f)
        {
            if (useUnscaledImpactDelay)
                yield return new WaitForSecondsRealtime(delay);
            else
                yield return new WaitForSeconds(delay);
        }

        Vector2 dir = pending.aimDirAtCommit.sqrMagnitude > 0.0001f ? pending.aimDirAtCommit : Vector2.up;
        dir = dir.normalized;

        // 0) Custom execution types
        if (skill.executionType == SkillExecutionType.PushBack)
        {
            PushBack pushBack = GetComponent<PushBack>();
            bool succeeded = false;

            if (pushBack != null)
                succeeded = pushBack.Execute();
            else
                Debug.LogWarning(
                    $"Skill '{skill.name}' is PushBack type but no PushBack component found on pawn."
                );

            if (succeeded)
                AwardSuccessfulSkillMomentum(skill);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        if (skill.executionType == SkillExecutionType.AoE)
        {
            bool succeeded = SpawnAoEZone(skill, dir);

            if (succeeded)
                AwardSuccessfulSkillMomentum(skill);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        if (skill.executionType == SkillExecutionType.SpeedBoost)
        {
            PlayerSpeedBoost speedBoost = GetComponent<PlayerSpeedBoost>();

            // Auto-add for a safe prototype setup. You can still add it manually
            // to PlayerRoot to tune its glow and ghost-trail fields in the Inspector.
            if (speedBoost == null)
                speedBoost = gameObject.AddComponent<PlayerSpeedBoost>();

            speedBoost.Activate(
                skill.speedBoostMultiplier,
                skill.speedBoostDuration
            );

            SpawnVfx(
                skill.impactVfxPrefab,
                vfxOrigin.position,
                dir,
                skill.impactVfxAngleOffset,
                skill.impactVfxForwardOffset
            );

            AwardSuccessfulSkillMomentum(skill);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 1) Projectile skill: spawn projectile at impact time, no direct damage
        if (skill.firesProjectile)
        {
            FireSkillProjectile(skill, dir, pending.ownerIndex);

            // Impact VFX at the spawn point (vfxOrigin + forward offset)
            Vector3 impactPos = vfxOrigin.position + (Vector3)(dir * skill.impactVfxForwardOffset);
            SpawnVfx(skill.impactVfxPrefab, impactPos, dir, skill.impactVfxAngleOffset, 0f);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 2) Party-target heal: heal selected index (or self if none)
        if (skill.requiresPartyTarget && skill.heal > 0)
        {
            bool healed = ApplyHealToPartyMember(
                skill,
                pending.ownerIndex,
                partyTargetIndex
            );

            if (healed)
            {
                AwardSuccessfulSkillMomentum(skill);
                healingFeedback?.PlayHealingFeedback();
            }

            // Impact VFX at player (or you can choose to spawn on target later)
            SpawnVfx(skill.impactVfxPrefab, vfxOrigin.position, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 3) Self heal (non-party target)
        if (!skill.requiresPartyTarget && skill.heal > 0)
        {
            bool healed = ApplyHealToPartyMember(
                skill,
                pending.ownerIndex,
                pending.ownerIndex
            );

            if (healed)
            {
                AwardSuccessfulSkillMomentum(skill);
                healingFeedback?.PlayHealingFeedback();
            }

            SpawnVfx(skill.impactVfxPrefab, vfxOrigin.position, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 4) Melee hitbox damage (only hits if overlap occurs)
        bool hitSomething = false;
        if (skill.damage > 0)
            hitSomething = ApplyMeleeHitboxDamage(skill, dir);

        if (skill.destroysEnemyProjectiles)
            DestroyEnemyProjectilesInMeleeHitbox(skill, dir);

        // Impact VFX at hitbox center (even if miss, still looks consistent)
        Vector3 meleeCenter = (Vector2)vfxOrigin.position + dir * GetMeleeRange(skill);
        SpawnVfx(skill.impactVfxPrefab, meleeCenter, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

        if (hitSomething)
        {
            HitstopManager.Request(skill.hitstop);

            CombatCameraShake.Request(
                skill.cameraShake,
                meleeCenter,
                dir
            );

            AwardSuccessfulSkillMomentum(skill);
        }

        // Cost multiplier still increases on resolve, even on a miss.
        ApplySkillCostMultiplier(pending.ownerIndex);

        isCasting = false;
    }

    // ----------------------------
    // Helpers
    // ----------------------------

    private void AwardSuccessfulSkillMomentum(
        SkillDefinition skill)
    {
        if (skill == null || skill.momentumGain <= 0f)
            return;

        AttackMomentumManager.Instance?.RegisterSuccessfulSkill(
            skill.momentumGain
        );
    }

    private Vector2 GetAimDir()
    {
        Vector2 dir = aim != null ? aim.AimDir : Vector2.up;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        return dir.normalized;
    }

    private void SpawnVfx(GameObject prefab, Vector3 origin, Vector2 dir, float angleOffset, float forwardOffset)
    {
        if (prefab == null) return;

        Vector3 pos = origin + (Vector3)(dir * forwardOffset);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;

        Instantiate(prefab, pos, Quaternion.Euler(0f, 0f, angle));
    }

    private void ApplySkillCostMultiplier(int ownerIndex)
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null) return;

        if (ownerIndex < 0 || ownerIndex >= pm.party.Count) return;

        var owner = pm.party[ownerIndex];
        if (owner == null || owner.def == null) return;

        float inc = Mathf.Max(1f, owner.def.skillCostIncreaseMultiplier);
        owner.skillCostMultiplier *= inc;
    }

    private bool ApplyHealToPartyMember(
        SkillDefinition skill,
        int ownerIndex,
        int? targetIndexNullable)
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.party == null)
            return false;

        int targetIndex =
            targetIndexNullable.HasValue
                ? targetIndexNullable.Value
                : ownerIndex;

        if (targetIndex < 0 || targetIndex >= pm.party.Count)
            return false;

        PartyManager.CharacterState target =
            pm.party[targetIndex];

        if (target == null || target.def == null)
            return false;

        int before = target.currentHP;

        target.currentHP = Mathf.Clamp(
            target.currentHP + skill.heal,
            0,
            target.def.maxHP
        );

        return target.currentHP > before;
    }

    private void FireSkillProjectile(SkillDefinition skill, Vector2 dir, int ownerIndex)
    {
        if (skill.projectilePrefab == null || shooter == null) return;

        var projComponent = skill.projectilePrefab.GetComponent<PlayerProjectile>();
        if (projComponent == null)
        {
            Debug.LogError($"Skill '{skill.name}': projectilePrefab has no PlayerProjectile component.");
            return;
        }

        LayerMask mask = skill.projectileHitMask.value != 0 ? skill.projectileHitMask : defaultEnemyHurtboxMask;

        shooter.Fire(
            projComponent,
            dir,
            ownerIndex,
            skill.projectileDamage,
            skill.projectileStunSeconds,
            skill.projectileSpeed,
            skill.projectileLifetime,
            mask,
            awardApOnHit: false,
            momentumGain: skill.momentumGain,
            startActiveScoringOnHit: true,
            hitstop: skill.hitstop,
            cameraShake: skill.cameraShake
        );
    }

    private bool SpawnAoEZone(SkillDefinition skill, Vector2 dir)
    {
        if (skill.aoeZonePrefab == null)
        {
            Debug.LogWarning($"Skill '{skill.name}' is AoE type but aoeZonePrefab is null.");
            return false;
        }

        Vector2 spawnPos = (Vector2)vfxOrigin.position;
        var zoneObj = Instantiate(skill.aoeZonePrefab, spawnPos, Quaternion.identity);
        var zone = zoneObj.GetComponent<AoEZone>();

        if (zone == null)
        {
            Debug.LogWarning($"Skill '{skill.name}': aoeZonePrefab missing AoEZone component.");
            Destroy(zoneObj);
            return false;
        }

        zone.Initialize(
            skill.aoeRadius,
            skill.aoeDuration,
            skill.aoeEffects,
            skill.aoeUsesProjectile ? dir : Vector2.zero,
            skill.aoeUsesProjectile ? skill.aoeProjectileSpeed : 0f,
            skill.aoeUsesProjectile ? skill.aoeProjectileTravelTime : 0f,
            skill.aoeObstacleMask
        );
        return true;
    }

    /// <summary>
    /// Resolves a pending cast at a specific world position.
    /// Called by CombatSkillMenuController after the player confirms placement.
    /// Routes to the correct handler based on execution type.
    /// AP was already spent by BeginCast; this applies the cost multiplier.
    /// </summary>
    public void ResolveCastAtPosition(PendingCast pending, Vector2 worldPos)
    {
        if (pending == null || pending.skill == null) return;

        bool succeeded = false;

        switch (pending.skill.executionType)
        {
            case SkillExecutionType.AoE:
                succeeded = SpawnAoEZoneAtPosition(
                    pending.skill,
                    worldPos
                );
                break;

            // Future placement-based types go here:
            // case SkillExecutionType.Teleport: ...
            // case SkillExecutionType.Summon: ...

            default:
                Debug.LogWarning($"Skill '{pending.skill.name}': placement not implemented for execution type {pending.skill.executionType}.");
                break;
        }

        if (succeeded)
            AwardSuccessfulSkillMomentum(pending.skill);

        ApplySkillCostMultiplier(pending.ownerIndex);
    }

    private bool SpawnAoEZoneAtPosition(
        SkillDefinition skill,
        Vector2 worldPos)
    {
        if (skill.aoeZonePrefab == null)
        {
            Debug.LogWarning($"Skill '{skill.name}': aoeZonePrefab is null.");
            return false;
        }

        var zoneObj = Instantiate(skill.aoeZonePrefab, worldPos, Quaternion.identity);
        var zone = zoneObj.GetComponent<AoEZone>();

        if (zone == null)
        {
            Debug.LogWarning($"Skill '{skill.name}': aoeZonePrefab missing AoEZone component.");
            Destroy(zoneObj);
            return false;
        }

        // Spawn directly at position — no travel
        zone.Initialize(
            skill.aoeRadius,
            skill.aoeDuration,
            skill.aoeEffects,
            Vector2.zero, 0f, 0f, default
        );
        return true;
    }

    private float GetMeleeRange(SkillDefinition skill)
    {
        // If SkillDefinition lacks these fields, set them in SkillDefinition first.
        // Defaults prevent accidental "global" hits.
        float r = skill.meleeRange;
        if (r <= 0f) r = defaultMeleeRange;
        return r;
    }

    private float GetMeleeRadius(SkillDefinition skill)
    {
        float r = skill.meleeRadius;
        if (r <= 0f) r = defaultMeleeRadius;
        return r;
    }

    private LayerMask GetMeleeMask(SkillDefinition skill)
    {
        return skill.meleeHitMask.value != 0 ? skill.meleeHitMask : defaultEnemyHurtboxMask;
    }

    private int GetMeleeMaxTargets(SkillDefinition skill)
    {
        int m = skill.meleeMaxTargets;
        if (m <= 0) m = defaultMeleeMaxTargets;
        return m;
    }

    private bool ApplyMeleeHitboxDamage(SkillDefinition skill, Vector2 dir)
    {
        LayerMask mask = GetMeleeMask(skill);
        if (mask.value == 0)
        {
            Debug.LogWarning($"Skill '{skill.name}': meleeHitMask/defaultEnemyHurtboxMask is empty. Set it to EnemyHurtbox.");
            return false;
        }

        float range = GetMeleeRange(skill);
        float radius = GetMeleeRadius(skill);
        int maxTargets = Mathf.Max(1, GetMeleeMaxTargets(skill));

        Vector2 center = (Vector2)vfxOrigin.position + dir * range;

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitCols, mask);
        if (count <= 0) return false;

        int applied = 0;

        for (int i = 0; i < count && applied < maxTargets; i++)
        {
            var col = hitCols[i];
            if (col == null) continue;

            var enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            bool alreadyHit = false;

            for (int j = 0; j < applied; j++)
            {
                if (uniqueMeleeEnemies[j] == enemy)
                {
                    alreadyHit = true;
                    break;
                }
            }

            if (alreadyHit)
                continue;

            if (applied >= uniqueMeleeEnemies.Length)
                break;

            uniqueMeleeEnemies[applied] = enemy;

            enemy.TakeDamage(skill.damage);

            if (skill.appliesMeleeKnockback)
                ApplyMeleeSkillKnockback(skill, enemy, dir);

            applied++;
        }

        return applied > 0;
    }

    private void ApplyMeleeSkillKnockback(
        SkillDefinition skill,
        EnemyHealth enemy,
        Vector2 fallbackDirection)
    {
        if (skill == null || enemy == null)
            return;

        KnockbackReceiver2D receiver =
            enemy.GetComponentInParent<KnockbackReceiver2D>();

        if (receiver == null)
            return;

        Vector2 origin = vfxOrigin != null
            ? (Vector2)vfxOrigin.position
            : (Vector2)transform.position;

        Vector2 direction =
            (Vector2)enemy.transform.position - origin;

        if (direction.sqrMagnitude < 0.0001f)
            direction = fallbackDirection;
        else
            direction.Normalize();

        receiver.ApplyKnockback(
            direction,
            skill.meleeKnockbackForce,
            skill.meleeKnockbackDuration
        );
    }

    private int DestroyEnemyProjectilesInMeleeHitbox(
        SkillDefinition skill,
        Vector2 dir)
    {
        float range = GetMeleeRange(skill);
        float radius = GetMeleeRadius(skill);
        Vector2 center =
            (Vector2)vfxOrigin.position + dir * range;

        Projectile[] projectiles =
            FindObjectsOfType<Projectile>(false);

        int destroyed = 0;

        for (int i = 0; i < projectiles.Length; i++)
        {
            Projectile projectile = projectiles[i];

            if (projectile == null ||
                projectile.Team != Projectile.ProjectileTeam.Enemy)
            {
                continue;
            }

            float distance = Vector2.Distance(
                center,
                projectile.transform.position
            );

            if (distance > radius)
                continue;

            Destroy(projectile.gameObject);
            destroyed++;
        }

        return destroyed;
    }
}
