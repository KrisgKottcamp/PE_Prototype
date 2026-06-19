using System.Collections.Generic;
using UnityEngine;

public enum SkillTargetType
{
    Self,
    Enemy,
    Area
}

public enum SkillExecutionType
{
    Default,    // auto-detects from firesProjectile / heal / damage
    PushBack,   // AoE knockback + projectile reflect
    AoE,        // generic AoE zone
    SpeedBoost  // temporary party-pawn movement boost with visual trail
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
    public bool includeDownedTargets = false;
    public string partyTargetMenuTitle = "Choose ally";

    [Header("Melee Hitbox (used when firesProjectile is false and damage > 0)")]
    public float meleeRange = 0.9f;
    public float meleeRadius = 0.4f;
    public LayerMask meleeHitMask;
    public int meleeMaxTargets = 1;

    [Header("Projectile Skill (optional)")]
    public bool firesProjectile = false;
    public GameObject projectilePrefab;
    public float projectileSpeed = 14f;
    public float projectileLifetime = 2.5f;
    public int projectileDamage = 8;
    public float projectileStunSeconds = 0.25f;
    public LayerMask projectileHitMask;

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

    [Header("Speed Boost (executionType = SpeedBoost)")]
    [Tooltip("1.35 means 35% faster movement.")]
    [Min(1f)]
    public float speedBoostMultiplier = 1.35f;

    [Tooltip("How long the movement boost remains active.")]
    [Min(0.05f)]
    public float speedBoostDuration = 5f;

    [Header("AoE Zone (executionType = AoE)")]
    [Tooltip("Prefab with AoEZone, CircleCollider2D trigger, and SpriteRenderer.")]
    public GameObject aoeZonePrefab;
    public List<AoEEffect> aoeEffects = new();
    public float aoeRadius = 2.5f;
    public float aoeDuration = 4f;

    [Header("AoE Delivery")]
    [Tooltip("If true, the zone flies before activating. Ignored when usesPlacement is true.")]
    public bool aoeUsesProjectile = true;
    public float aoeProjectileSpeed = 12f;
    public float aoeProjectileTravelTime = 0.4f;

    [Tooltip("Obstacles that make the projectile burst early.")]
    public LayerMask aoeObstacleMask;

    [Header("Placement (click-to-place, works with any execution type)")]
    [Tooltip("If true, the player clicks to place within range.")]
    public bool usesPlacement = false;

    [Tooltip("Maximum placement distance from the player.")]
    public float placementRange = 8f;

    [Tooltip("Layers that block placement.")]
    public LayerMask placementBlockMask;

    [Tooltip("Preview radius for non-AoE placement skills.")]
    public float placementPreviewRadius = 0.5f;
}
