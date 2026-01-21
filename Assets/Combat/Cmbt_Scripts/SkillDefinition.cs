using UnityEngine;

public enum SkillTargetType { Self, Enemy, Area }

[CreateAssetMenu(menuName = "Game/Skills/Skill Definition")]
public class SkillDefinition : ScriptableObject
{
    [Header("Info")]
    public string displayName;
    public SkillTargetType targetType = SkillTargetType.Enemy;

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
}
