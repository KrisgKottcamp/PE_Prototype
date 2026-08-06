using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Spells/Spell Definition")]
public class SpellDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public SpellBase spellBase = SpellBase.Casting;

    // -------------------------------------------------------
    // Phase Timing
    // -------------------------------------------------------

    [Header("Phase Timing")]
    [Tooltip("Duration of the build-up phase (animation/charge).")]
    public float buildUpDuration;
    [Tooltip("Duration of the firing phase.")]
    public float firingDuration = 0.1f;
    [Tooltip("Duration of the channeling phase.")]
    public float channelingDuration;
    [Tooltip("Duration of the antefire phase (recovery).")]
    public float antefireDuration = 0.2f;

    // -------------------------------------------------------
    // Casting Spell Fields
    // -------------------------------------------------------

    [Header("Casting — Projectile")]
    public CastProjectileType projectileType = CastProjectileType.Bullet;
    public ProjectilePattern pattern = ProjectilePattern.Line;
    public ProjectileHitBehavior hitBehavior = ProjectileHitBehavior.Direct;

    [Header("Casting — Numerics")]
    public int projectileCount = 1;
    [Tooltip("Angle spread for Cone pattern (degrees).")]
    [Range(0f, 360f)]
    public float coneDegree = 30f;
    [Tooltip("Arc for Swipe pattern (degrees).")]
    [Range(0f, 360f)]
    public float swipeDegree = 90f;
    public float projectileSpeed = 7f;
    [Tooltip("Max travel distance / lifetime.")]
    public float range = 10f;

    [Header("Casting — Multi-Fire")]
    [Tooltip("Number of times the attack fires per cast.")]
    public int fireCount = 1;
    [Tooltip("Delay between each fire event.")]
    public float fireDelay = 0.1f;
    [Tooltip("Lock aim to the original cast direction. When false, each shot re-targets the mouse.")]
    public bool lockAimDirection;

    [Header("Casting — Bomb")]
    [Tooltip("Delay before bomb detonation after placement.")]
    public float bombDelay;
    public float bombRadius = 1.5f;

    [Header("Casting — Beam")]
    [Tooltip("Beam width (for beam projectile type).")]
    public float beamWidth = 0.2f;

    [Header("Casting — Helix")]
    [Tooltip("How fast the helix radius grows per second.")]
    public float helixGrowthRate = 1f;
    public float helixInitialRadius = 0.3f;

    [Header("Casting — Mine")]
    [Tooltip("Radius at which the mine detects enemies and begins its fuse.")]
    public float mineDetectionRadius = 2f;
    [Tooltip("Fuse delay in seconds after an enemy enters the detection radius.")]
    public float mineFuseDelay = 0.5f;

    [Header("Casting — Zone")]
    [Tooltip("How long the zone persists.")]
    public float zoneDuration = 5f;
    [Tooltip("Seconds between each damage/effect tick.")]
    public float zoneTickRate = 0.5f;

    [Header("Casting — Damage")]
    public int damage = 10;
    [Tooltip("Damage multiplier at max range. 1 = no falloff.")]
    [Range(0f, 1f)]
    public float damageFalloff = 1f;
    public DamageType damageType = DamageType.Physical;

    [Header("Casting — Modifiers")]
    public SpellModifiers modifiers = new();

    [Header("Casting — Effects on Hit")]
    [Tooltip("Effects applied to the target on each hit.")]
    public List<EffectDefinition> onHitEffects = new();

    [Header("Casting — Projectile Visual")]
    [Tooltip("Projectile definition controlling visuals, trail, and impact. Uses a default if null.")]
    public ProjectileDefinition projectileVisual;
    [Tooltip("Override prefab. Takes priority over projectileVisual if set.")]
    public GameObject projectilePrefab;

    [Header("Casting — Collision")]
    public LayerMask hitMask;

    // -------------------------------------------------------
    // Movement Spell Fields
    // -------------------------------------------------------

    [Header("Movement")]
    public MovementSpellType movementType = MovementSpellType.Dash;
    public float movementDistance = 3f;

    [Header("Movement — Dash")]
    [Tooltip("Speed of the dash.")]
    public float dashSpeed = 15f;
    [Tooltip("Velocity retained after dash ends (0-1).")]
    [Range(0f, 1f)]
    public float dashMomentum = 0.2f;

    [Header("Movement — Boost")]
    [Tooltip("Movement speed multiplier during boost.")]
    public float boostMultiplier = 1.5f;
    [Tooltip("How long the speed boost lasts.")]
    public float boostDuration = 2f;

    [Header("Movement — Effects")]
    [Tooltip("Effects applied to the caster during/after movement.")]
    public List<EffectDefinition> movementEffects = new();

    // -------------------------------------------------------
    // Stat Spell Fields
    // -------------------------------------------------------

    [Header("Stat — Apply")]
    [Tooltip("Effects applied to the target (self or selected entity).")]
    public List<EffectDefinition> applyEffects = new();

    [Header("Stat — Remove")]
    [Tooltip("Effect IDs to remove from the target.")]
    public List<string> removeEffectIds = new();

    // -------------------------------------------------------
    // AI Fields
    // -------------------------------------------------------

    [Header("AI Targeting")]
    [Tooltip("Which targeting mode the enemy should use for this spell.")]
    public EnemyTargetingMode aiTargetingMode = EnemyTargetingMode.Single;
    [Tooltip("Ideal distance for the AI to use this spell.")]
    public float preferredRange = 5f;

    [Header("Cooldown")]
    public float cooldown = 1f;

    // -------------------------------------------------------
    // Visuals
    // -------------------------------------------------------

    [Header("Visuals")]
    public Sprite icon;
    public GameObject buildUpVfxPrefab;
    public GameObject firingVfxPrefab;
    public GameObject antefireVfxPrefab;

    [Header("Animation Keys")]
    public string buildUpAnimTrigger;
    public string firingAnimTrigger;
    public string antefireAnimTrigger;

    // -------------------------------------------------------
    // Spell Chaining
    // -------------------------------------------------------

    [Header("Multicast — On Hit")]
    [Tooltip("Spells that all fire at the point of impact when a projectile hits an enemy or surface.")]
    public List<SpellDefinition> multicastOnHit = new();

    [Header("Chain")]
    [Tooltip("Spells that fire in sequence after this one completes.")]
    public List<SpellDefinition> chain = new();
}
