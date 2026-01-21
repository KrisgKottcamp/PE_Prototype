using System.Collections;
using UnityEngine;

/// <summary>
/// CombatSkillSystem
/// - Supports skills that:
///   1) Spawn VFX (cast + impact)
///   2) Do melee hitbox damage (only if hitbox overlaps enemies)
///   3) Fire a projectile (damage happens only on projectile collision)
///   4) Heal a chosen party member (generic party target support)
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

    [Header("Defaults (used if SkillDefinition fields are unset)")]
    [SerializeField] private LayerMask defaultEnemyHurtboxMask; // EnemyHurtbox
    [SerializeField] private float defaultMeleeRange = 0.9f;
    [SerializeField] private float defaultMeleeRadius = 0.4f;
    [SerializeField] private int defaultMeleeMaxTargets = 1;

    private bool isCasting;

    private readonly Collider2D[] hitCols = new Collider2D[16];

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

        // Delay to match skill timing
        float delay = Mathf.Max(0f, skill.impactDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Vector2 dir = pending.aimDirAtCommit.sqrMagnitude > 0.0001f ? pending.aimDirAtCommit : Vector2.up;
        dir = dir.normalized;

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
            ApplyHealToPartyMember(skill, pending.ownerIndex, partyTargetIndex);

            // Impact VFX at player (or you can choose to spawn on target later)
            SpawnVfx(skill.impactVfxPrefab, vfxOrigin.position, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 3) Self heal (non-party target)
        if (!skill.requiresPartyTarget && skill.heal > 0)
        {
            ApplyHealToPartyMember(skill, pending.ownerIndex, pending.ownerIndex);

            SpawnVfx(skill.impactVfxPrefab, vfxOrigin.position, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

            ApplySkillCostMultiplier(pending.ownerIndex);
            isCasting = false;
            yield break;
        }

        // 4) Melee hitbox damage (only hits if overlap occurs)
        bool hitSomething = false;
        if (skill.damage > 0)
            hitSomething = ApplyMeleeHitboxDamage(skill, dir);

        // Impact VFX at hitbox center (even if miss, still looks consistent)
        Vector3 meleeCenter = (Vector2)vfxOrigin.position + dir * GetMeleeRange(skill);
        SpawnVfx(skill.impactVfxPrefab, meleeCenter, dir, skill.impactVfxAngleOffset, skill.impactVfxForwardOffset);

        // You can decide if multiplier should increase even on miss.
        // This implementation increases on resolve regardless of hit.
        ApplySkillCostMultiplier(pending.ownerIndex);

        isCasting = false;
    }

    // ----------------------------
    // Helpers
    // ----------------------------

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

    private void ApplyHealToPartyMember(SkillDefinition skill, int ownerIndex, int? targetIndexNullable)
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null) return;

        int targetIndex = targetIndexNullable.HasValue ? targetIndexNullable.Value : ownerIndex;
        if (targetIndex < 0 || targetIndex >= pm.party.Count) return;

        var tgt = pm.party[targetIndex];
        if (tgt == null || tgt.def == null) return;

        tgt.currentHP = Mathf.Clamp(tgt.currentHP + skill.heal, 0, tgt.def.maxHP);
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
            awardApOnHit: false
        );
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

            enemy.TakeDamage(skill.damage);
            applied++;
        }

        return applied > 0;
    }
}
