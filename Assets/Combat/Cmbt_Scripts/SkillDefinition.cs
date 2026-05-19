using UnityEngine;

public enum SkillTargetType { Self, Enemy, Area }

public enum SkillExecutionType
{
    Default,   // existing logic: auto-detects from firesProjectile / heal / damage
    PushBack,  // AoE knockback + projectile reflect (delegated to PushBack component)
    AoE        // generic AoE zone: projectile delivery or instant, applies AoEEffect list
}

[CreateAssetMenu(menuName = "Game/Skills/Skill Definition")]
public class SkillDefinition : ScriptableObject
{
    [Header("Info")]
    public string displayName;
    public SkillTargetType targetType = SkillTargetType.Enemy;

    [Header("Execution")]
    public SkillExecutionType executionType = SkillExecutionType.Default;

    [Header("AP")]
    public int baseApCost = 10;

    [Header("Targeting")]
    public bool requiresPartyTarget = false;
    public bool includeDownedTargets = false; // for future revive skills
    public string partyTargetMenuTitle = "Choose ally";


    [Header("Melee Hitbox (used when firesProjectile is false and damage > 0)")]
    public float meleeRange = 0.9f;
    public float meleeRadius = 0.4f;
    public LayerMask meleeHitMask; // set to EnemyHurtbox
    public int meleeMaxTargets = 1; // 1 for slash, higher for AoE if you want


    [Header("Projectile Skill (optional)")]
    public bool firesProjectile = false;
    public GameObject projectilePrefab;
    public float projectileSpeed = 14f;
    public float projectileLifetime = 2.5f;
    public int projectileDamage = 8;
    public float projectileStunSeconds = 0.25f;
    public LayerMask projectileHitMask; // EnemyHurtbox + Obstacles


    [Header("Effects (prototype)")]
    public int damage = 10;
    public int heal = 0;

    [Header("Timing")]
    public float impactDelay = 0.1f;

    [Header("VFX")]
    public GameObject castVfxPrefab;
    public float castVfxAngleOffset = 0f;
    public float castVfxForwardOffset = 0f;

    public GameObject impactVfxPrefab;
    public float impactVfxAngleOffset = 0f;
    public float impactVfxForwardOffset = 0f;

    [Header("AoE Zone (executionType = AoE)")]
    [Tooltip("Prefab with AoEZone component, CircleCollider2D (trigger), and SpriteRenderer.")]
    public GameObject aoeZonePrefab;
    public System.Collections.Generic.List<AoEEffect> aoeEffects = new();
    public float aoeRadius = 2.5f;
    public float aoeDuration = 4f;

    [Header("AoE Delivery")]
    [Tooltip("If true, zone flies as a projectile before activating. If false, spawns instantly at the player.")]
    public bool aoeUsesProjectile = true;
    public float aoeProjectileSpeed = 12f;
    public float aoeProjectileTravelTime = 0.4f;
    [Tooltip("Obstacles that make the projectile burst early.")]
    public LayerMask aoeObstacleMask;
}