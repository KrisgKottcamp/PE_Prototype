using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Characters/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    public string displayName;
    public enum BasicAttackType
    {
        Melee,
        Projectile,
        HeavyCombo,
        Whip
    }

    [Header("Base Stats")]
    public int maxHP = 100;
    public float combatMoveSpeed = 6f;

    [Header("AP")]
    public int maxAP = 100;
    public float apRegenPerSecond = 0f;
    public int apGainOnBasicHit = 5;

    [Tooltip("How close this character must be before loose AP particles begin flying toward the combat pawn.")]
    [Min(0f)] public float apMagnetizationRange = 3f;

    [Header("Basic Attack")]
    public BasicAttackType basicAttackType = BasicAttackType.Melee;

    [Header("Projectile Basic Attack")]
    public GameObject basicAttackProjectilePrefab;


    [Header("Skills")]
    public List<SkillDefinition> startingSkills = new();
    [Tooltip("After using any skill, multiply all skill AP costs by this value (stacking) until you swap.")]
    public float skillCostIncreaseMultiplier = 1.25f;

    [Header("Skill System V2 Loadout")]
    [Tooltip("Use the V2 spells assigned below whenever this character is active. Leave disabled while this character still uses the legacy or pawn-default skill loadout.")]
    public bool useSkillSystemV2Loadout;

    [Tooltip("Optional V2 basic attack for this character. Enemy and player basic-attack integration can consume this slot without changing the equipped spell list.")]
    public SpellDefinition skillSystemV2BasicAttack;

    [Tooltip("V2 spells equipped by this character, in the order shown in the combat skill menu.")]
    public List<SpellDefinition> equippedSpellsV2 = new();

    [Header("Progression")]
    public List<SkillUnlock> unlocks = new();

    [Header("Visuals")]
    public Sprite combatSprite;
    public Sprite portraitSprite; // optional for UI


    [Serializable]
    public class SkillUnlock
    {
        public int levelRequired = 2;
        public SkillDefinition skill;
    }
}
