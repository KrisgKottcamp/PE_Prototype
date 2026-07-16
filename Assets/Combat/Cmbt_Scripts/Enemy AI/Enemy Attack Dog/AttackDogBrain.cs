using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AttackDogBrain v22
///
/// Purpose:
/// A melee harasser enemy for Project Eri.
/// The dog pressures the player through presence, prowling, threat pauses,
/// readable lunges, and punishable recovery windows.
///
/// v8 design goals:
/// - Integrate with the squad system through IEnemySquadAgent without becoming a shooter EnemyBrain.
/// - Use intent-based behavior instead of constant vectoring/orbiting.
/// - Preserve direct, snappy movement with no acceleration/deceleration smoothing.
/// - Reduce jitter by committing to prowl intents and retargeting only after arrival/failure.
/// - Make the lunge readable, tunable, and satisfying to punish.
/// - Let player hits interrupt/shape the dog's rhythm through state-based hit reactions.
/// - Show a visible red lunge hitbox object during telegraph/lunge. v13 aligns the visual to the exact damage hitbox center.
/// - v11 changes prowling into arc-based circling: small back-and-forth side steps around the player with variance, closer to the original orbit feel without becoming a perfect constant circle.
/// - v14 adds timed re-engage behavior so flee/back-off mode eventually returns to aggression.
/// - v15 integrates shared SpeedModifier effects, honest cover cancellation, wall-crash punish windows,
///   damage-threshold retreat reactions, role-aware replanning, and opposite-side follow-ups after misses.
/// - v16 flips the assigned dog SpriteRenderer horizontally so the dog faces its actual movement direction.
///   The red lunge hitbox visual is not transformed or mirrored by this feature.
/// - v17 separates the dog's damageable hurtbox from its lunge attack hitbox. A larger child
///   EnemyHurtbox trigger can make the dog easier to hit while a smaller lunge radius keeps the attack fair.
/// - v18 allows the damageable hurtbox to be either a CircleCollider2D or CapsuleCollider2D.
/// - v19 adds telegraph anti-stall checks so the dog does not warn a lunge while wedged into cover.
/// - v20 adds blocked-move rescue and HoldNearPlayer cooldowns so the dog can unpin itself from cover/walls
///   instead of repeatedly choosing invalid hold/prowl targets until the player drags it away.
/// - v22 makes the visible red lunge object the authoritative damage hitbox. Its own Collider2D is
///   disabled during telegraph/fade-out, armed only during the active lunge, and swept to prevent tunneling.
///   This is a corrected rebuild based on the stable v20 script so no movement/utility methods are lost.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class AttackDogBrain : MonoBehaviour, IEnemySquadAgent
{
    private enum DogState
    {
        Acquire,
        ChooseIntent,
        Prowl,
        HoldThreat,
        Telegraph,
        Lunge,
        Recovery,
        HitReact,
        Retreat
    }

    private enum HarassIntent
    {
        None,
        ProwlLeft,
        ProwlRight,
        CutOffEscape,
        HoldNearPlayer,
        DirectPressure,
        BackOff
    }

    private enum HitboxVisualFadeState
    {
        Hidden,
        FadingIn,
        Visible,
        FadingOut
    }

    [Header("Core References")]
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private ArenaNavigationGrid navigationGrid;

    [Header("Player Tracking")]
    [SerializeField] private bool autoFindPlayerByTag = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";
    [SerializeField, Min(0.02f)] private float playerSearchInterval = 0.20f;
    [SerializeField, Min(0f)] private float playerVelocitySampleSharpness = 12f;

    [Header("Squad Integration")]
    [SerializeField] private bool registerWithSquad = true;
    [SerializeField] private bool countAsMeleeAgent = true;

    [Tooltip("How strongly squad pressure shortens intent pauses and attack cooldowns.")]
    [SerializeField, Range(0f, 1f)] private float squadPressureInfluence = 0.45f;

    [Header("Movement")]
    [Tooltip("Base movement speed while prowling or routing around cover.")]
    [SerializeField, Min(0f)] private float moveSpeed = 4.1f;

    [Tooltip("Speed multiplier while committing to DirectPressure or high-pressure CutOffEscape intents.")]
    [SerializeField, Min(0f)] private float pressureSpeedMultiplier = 1.12f;

    [Tooltip("Speed multiplier while retreating after being hit.")]
    [SerializeField, Min(0f)] private float retreatSpeedMultiplier = 1.22f;

    [Header("Shared Speed Effects")]
    [Tooltip("Apply the existing SpeedModifier multiplier to prowling, retreating, telegraph backsteps, and lunges.")]
    [SerializeField] private bool useSpeedModifier = true;

    [Tooltip("Lowest SpeedModifier multiplier allowed during the active lunge. Keeps strong slows from making the attack stop functioning entirely.")]
    [SerializeField, Range(0f, 1f)] private float minimumLungeSpeedMultiplier = 0.50f;

    [Header("Sprite Facing")]
    [Tooltip("The dog's main body SpriteRenderer. Assign the character sprite here, not the red lunge hitbox visual.")]
    [SerializeField] private SpriteRenderer movementFacingSprite;

    [Tooltip("If the Sprite Facing field is empty, first try the telegraph renderers, then search child SpriteRenderers while excluding the lunge hitbox visual.")]
    [SerializeField] private bool autoFindMovementFacingSprite = true;

    [Tooltip("Turn this on when the unflipped source sprite naturally looks to the right. Turn it off when the source sprite naturally looks to the left.")]
    [SerializeField] private bool spriteFacesRightByDefault = true;

    [Tooltip("Horizontal movement smaller than this keeps the previous facing direction. This prevents flip jitter during mostly vertical movement or tiny physics corrections.")]
    [SerializeField, Min(0f)] private float minimumHorizontalMovementForFlip = 0.05f;

    [Tooltip("Dog considers its prowl target reached within this radius. Larger values reduce jitter.")]
    [SerializeField, Min(0.05f)] private float prowlArrivalRadius = 0.48f;

    [Tooltip("Dog considers retreat targets reached within this radius.")]
    [SerializeField, Min(0.05f)] private float retreatArrivalRadius = 0.32f;

    [Tooltip("After reaching a prowl point, the dog waits briefly before choosing its next intent.")]
    [SerializeField] private Vector2 arrivalPauseRange = new Vector2(0.25f, 0.65f);

    [Header("Intent Rhythm")]
    [Tooltip("Minimum time the dog keeps a chosen prowl/harass intent before it can voluntarily change.")]
    [SerializeField] private Vector2 intentDurationRange = new Vector2(1.1f, 2.3f);

    [Tooltip("Brief passive time after spawn before the dog begins serious pressure.")]
    [SerializeField, Min(0f)] private float initialPassiveSeconds = 0.85f;

    [Tooltip("Chance, per intent selection, that the dog holds still near the player instead of moving immediately.")]
    [SerializeField, Range(0f, 1f)] private float holdThreatChance = 0.18f;

    [Tooltip("Chance, per intent selection, that the dog crosses to the other prowl side.")]
    [SerializeField, Range(0f, 1f)] private float sideSwitchChance = 0.22f;

    [Tooltip("Chance that the dog uses a player-movement intercept when the player is running.")]
    [SerializeField, Range(0f, 1f)] private float cutOffEscapeChance = 0.38f;

    [Tooltip("Chance that the dog presses straight toward the player when squad pressure is high.")]
    [SerializeField, Range(0f, 1f)] private float directPressureChanceAtMaxPressure = 0.30f;

    [Header("Prowl Shape")]
    [Tooltip("Closest desired prowl range. This is not a hard repulsion wall.")]
    [SerializeField, Min(0f)] private float comfortMinDistance = 2.2f;

    [Tooltip("Farthest desired prowl range.")]
    [SerializeField, Min(0f)] private float comfortMaxDistance = 3.8f;

    [Tooltip("When enabled, prowl targets are chosen as small arc steps around the player instead of big reposition jumps. This feels closer to circling without becoming a perfect orbit.")]
    [SerializeField] private bool useArcProwlMovement = true;

    [Tooltip("Normal angular step around the player for each prowl target. Smaller values feel smoother and more orbit-like; larger values feel more jumpy and evasive.")]
    [SerializeField] private Vector2 prowlArcStepDegreesRange = new Vector2(24f, 48f);

    [Tooltip("Occasionally lets the dog take a larger arc step so it does not look like a fixed math orbit.")]
    [SerializeField, Range(0f, 1f)] private float occasionalWideArcChance = 0.14f;

    [Tooltip("Angular step used when the dog decides to make a wider prowl move.")]
    [SerializeField] private Vector2 wideArcStepDegreesRange = new Vector2(52f, 82f);

    [Tooltip("Chance that a prowl target briefly reverses direction, creating the back-and-forth circling feel.")]
    [SerializeField, Range(0f, 1f)] private float prowlBacktrackChance = 0.22f;

    [Tooltip("Small random radius variation added per arc target. This keeps circling from being a perfect ring.")]
    [SerializeField] private Vector2 prowlRadiusVarianceRange = new Vector2(-0.35f, 0.35f);

    [Tooltip("Extra randomness added to prowl point angles so circling is not a literal orbit. For v11, lower values usually look better than older versions.")]
    [SerializeField, Range(0f, 90f)] private float prowlAngleJitterDegrees = 16f;

    [Tooltip("How far ahead of a moving player the dog may try to cut off escape routes.")]
    [SerializeField, Min(0f)] private float interceptAheadDistance = 1.9f;

    [Tooltip("Side offset from the player's movement direction during CutOffEscape.")]
    [SerializeField, Min(0f)] private float interceptSideOffset = 0.85f;

    [Tooltip("Minimum sampled player speed before CutOffEscape is considered.")]
    [SerializeField, Min(0f)] private float playerMovingSpeedThreshold = 0.35f;

    [Header("Target Validation")]
    [SerializeField] private bool useNavigationGrid = true;
    [SerializeField, Min(0.05f)] private float pathRefreshInterval = 0.28f;
    [SerializeField, Min(0.05f)] private float waypointArrivalRadius = 0.20f;
    [SerializeField, Min(1)] private int prowlTargetAttempts = 14;
    [SerializeField, Min(0.1f)] private float blockedTargetMemorySeconds = 1.5f;
    [SerializeField, Min(0.05f)] private float invalidTargetRetryDelay = 0.28f;
    [SerializeField, Min(0.05f)] private float blockedMoveRetryDelay = 0.38f;

    [Header("Attack Timing")]
    [SerializeField] private Vector2 initialAttackDelayRange = new Vector2(1.6f, 2.6f);
    [SerializeField] private Vector2 attackCooldownRange = new Vector2(2.8f, 4.8f);

    [Tooltip("Dog may start a lunge within this range.")]
    [SerializeField, Min(0f)] private float attackRange = 2.35f;

    [Tooltip("Dog avoids starting a lunge when already too close, unless Force Close Threat is enabled.")]
    [SerializeField, Min(0f)] private float preferredMinimumLungeRange = 0.85f;

    [Tooltip("If the player is this close and attack is ready, the dog can lunge even inside preferred minimum range.")]
    [SerializeField, Min(0f)] private float closeThreatRange = 1.15f;

    [SerializeField] private bool forceCloseThreatAttacks = true;

    [Header("Threat Hold")]
    [Tooltip("How long the dog may pause near the player before either lunging or changing intent.")]
    [SerializeField] private Vector2 holdThreatSecondsRange = new Vector2(0.30f, 0.75f);

    [Tooltip("While holding threat, dog may start a lunge immediately if ready and in range.")]
    [SerializeField] private bool allowHoldThreatToAttack = true;

    [Header("Squad Attack Slots / Rhythm v1")]
    [Tooltip("If true, the dog must claim a squad attack slot before entering its lunge telegraph.")]
    [SerializeField] private bool useSquadAttackSlots = true;

    [Tooltip("How often the dog retries an attack-slot request while in range.")]
    [SerializeField, Min(0.03f)] private float attackSlotRetryDelay = 0.16f;

    [Tooltip("If true, squad threat-gap urgency can bypass some of the dog's random attack cooldown.")]
    [SerializeField] private bool threatGapCanHurryLunge = true;

    [SerializeField] private string debugAttackSlot = "None";

    [Header("Pincer Positioning / Escape Denial v1")]
    [Tooltip("If true, the dog asks the squad coordinator for escape-cutoff and side-pressure targets so it helps box the player in.")]
    [SerializeField] private bool useSquadPincerTargets = true;

    [Tooltip("Scoring weight for sampled dog prowl targets that create better side pressure / avoid clumping.")]
    [SerializeField, Min(0f)] private float pincerTargetScoreWeight = 1.25f;

    [Tooltip("When true, CutOffEscape targets use the squad's smoothed player velocity instead of only the dog's local velocity sample.")]
    [SerializeField] private bool useSquadEscapeCutoffPrediction = true;

    [SerializeField] private string debugPincerTarget = "None";

    [Header("Telegraph")]
    [Tooltip("Total visible tell before the dog lunges.")]
    [SerializeField, Min(0.05f)] private float telegraphSeconds = 0.68f;

    [Tooltip("How far through the telegraph the dog waits before locking the lunge target. 0 = start, 1 = end.")]
    [SerializeField, Range(0f, 1f)] private float aimLockFraction = 0.52f;

    [Tooltip("Optional backstep during the first part of the telegraph.")]
    [SerializeField, Min(0f)] private float backstepSeconds = 0.20f;

    [SerializeField, Min(0f)] private float backstepSpeed = 3.0f;
    [SerializeField] private SpriteRenderer[] telegraphFlashRenderers;
    [SerializeField] private Color telegraphColor = Color.red;

    [Header("Lunge")]
    [SerializeField, Min(0)] private int lungeDamage = 8;
    [SerializeField, Min(0f)] private float lungeSpeed = 10.0f;
    [SerializeField, Min(0.05f)] private float lungeSeconds = 0.34f;
    [SerializeField, Min(0f)] private float lungeOvershootDistance = 1.15f;

    [Tooltip("Radius of the dog's damaging lunge hitbox. This is independent from the larger damageable hurtbox used by player attacks.")]
    [SerializeField, Min(0.05f)] private float lungeHitRadius = 0.46f;

    [SerializeField, Min(0f)] private float lungeHitForwardOffset = 0.30f;
    [SerializeField] private bool endLungeOnHit = true;

    [Tooltip("Use a transform-distance fallback if Player Hit Mask does not find the combat pawn. Keep the extra radius small so the visible lunge hitbox remains honest.")]
    [SerializeField] private bool usePlayerTransformFallbackHitCheck = true;

    [Tooltip("Extra forgiveness added only to the transform fallback hit check. Set to 0 for the strictest match to the visible lunge radius.")]
    [SerializeField, Min(0f)] private float playerTransformFallbackExtraRadius = 0.05f;

    [Header("Cover Counterplay")]
    [Tooltip("Recheck the committed lunge path after the telegraph. If the player reached cover, cancel the lunge and give the dog a punishable recovery.")]
    [SerializeField] private bool cancelLungeWhenCoverBlocksCommittedPath = true;

    [Tooltip("Extra recovery when cover cancels the attack before the dog begins moving.")]
    [SerializeField, Min(0f)] private float coverCancelRecoveryBonusSeconds = 0.35f;

    [Tooltip("Extra recovery when an active lunge physically crashes into a wall or cover object.")]
    [SerializeField, Min(0f)] private float wallCrashRecoveryBonusSeconds = 0.50f;

    [Tooltip("After a missed, cancelled, or wall-crashed lunge, make the next prowl move continue from the opposite side of the player.")]
    [SerializeField] private bool forceOppositeSideProwlAfterFailedLunge = true;

    [Header("Telegraph Anti-Stall")]
    [Tooltip("Before the red telegraph appears, do a radius-based clearance check. This prevents the dog from warning an attack while wedged against cover that blocks the lunge body.")]
    [SerializeField] private bool requireClearLungeStartBeforeTelegraph = true;

    [Tooltip("How far forward from the dog to check with Body Radius before allowing the telegraph to begin. Raise this if the dog still warns attacks while pressed into cover.")]
    [SerializeField, Min(0.05f)] private float lungeStartClearanceDistance = 0.85f;

    [Tooltip("When the dog wants to attack but cover/body clearance blocks the start, delay the next attack attempt by this many seconds.")]
    [SerializeField, Min(0.05f)] private float blockedTelegraphRetryDelay = 0.70f;

    [Tooltip("After a blocked telegraph attempt, wait this long before choosing a new prowl target.")]
    [SerializeField, Min(0.05f)] private float blockedTelegraphRetargetDelay = 0.25f;

    [Tooltip("If an attack warning is blocked by cover, queue an opposite-side prowl so the dog routes around the obstacle instead of repeatedly warning into the wall.")]
    [SerializeField] private bool queueSideProwlWhenTelegraphBlocked = true;

    [Header("Blocked Move Rescue")]
    [Tooltip("When normal movement is blocked by cover/walls, temporarily abandon the current hold/prowl target and force a safer side prowl.")]
    [SerializeField] private bool enableBlockedMoveRescue = true;

    [Tooltip("How long the dog may keep hitting a wall before it performs a small emergency nudge away from the blocker.")]
    [SerializeField, Min(0.02f)] private float blockedMoveEscapeAfterSeconds = 0.22f;

    [Tooltip("Small physical correction used to pull the dog away from cover when it is wedged. Keep this low so it does not look like teleporting.")]
    [SerializeField, Min(0.01f)] private float blockedMoveNudgeDistance = 0.16f;

    [Tooltip("After HoldNearPlayer gets blocked, temporarily forbid HoldNearPlayer so the dog chooses a side prowl instead of repeating the same bad wall-side hold.")]
    [SerializeField, Min(0.05f)] private float holdNearPlayerBlockedCooldown = 1.25f;

    [Tooltip("How many consecutive target-pick failures are allowed before the dog performs an emergency nudge and forces a side prowl.")]
    [SerializeField, Min(1)] private int targetFailureEscapeThreshold = 2;

    [Tooltip("Minimum delay between no-path/target-failure emergency nudges.")]
    [SerializeField, Min(0.05f)] private float targetFailureEscapeCooldown = 0.45f;

    [Header("Lunge Hitbox Visual")]
    [Tooltip("Shows a visible object at the dog's active lunge hitbox. Use a red enemy-projectile-style SpriteRenderer child or prefab instance.")]
    [SerializeField] private bool showLungeHitboxVisual = true;

    [Tooltip("Assign a disabled child GameObject with a SpriteRenderer, Collider2D, and AttackDogLungeHitbox. This object is both the warning visual and the real lunge damage hitbox.")]
    [SerializeField] private GameObject lungeHitboxVisualObject;

    [Tooltip("AttackDogLungeHitbox component on the same visible child. Leave empty to auto-find it.")]
    [SerializeField] private AttackDogLungeHitbox lungeDamageHitbox;

    [Tooltip("Automatically find AttackDogLungeHitbox on the visible lunge object when the reference is empty.")]
    [SerializeField] private bool autoFindLungeDamageHitbox = true;

    [Tooltip("Fade/show the danger marker during the telegraph instead of waiting until the active lunge starts.")]
    [SerializeField] private bool showLungeHitboxVisualDuringTelegraph = true;

    [Tooltip("Before Aim Lock, the telegraph marker follows the current player direction. After Aim Lock, it freezes to the committed lunge direction.")]
    [SerializeField] private bool telegraphVisualTracksUntilAimLock = true;

    [Tooltip("Automatically scales the visible object and its Collider2D together from Lunge Hit Radius. Turn this off when sizing the sprite and collider manually.")]
    [SerializeField] private bool autoScaleLungeHitboxVisual = true;

    [SerializeField, Min(0.01f)] private float lungeHitboxVisualScaleMultiplier = 1.0f;

    [Tooltip("Rotates the visual to face the lunge direction. Turn this off for circular sprites.")]
    [SerializeField] private bool rotateLungeHitboxVisualToDirection = false;

    [Tooltip("Optional offset for the entire visible damage object. Since its Collider2D is on the same object, the sprite and damaging shape stay aligned. Leave at 0,0 for normal setup.")]
    [SerializeField] private Vector2 lungeHitboxVisualOffset = Vector2.zero;

    [Tooltip("When enabled, the visual is placed using the exact same circle center used by the lunge damage check. This should stay ON for a true gameplay hitbox marker.")]
    [SerializeField] private bool lungeHitboxVisualUseExactDamageCenter = true;

    [Tooltip("When enabled, the script shifts the visual object so its SpriteRenderer bounds center sits exactly on the damage hitbox center. This fixes sprites/children with off-center pivots.")]
    [SerializeField] private bool centerVisualRendererBoundsOnHitbox = true;

    [Tooltip("Draws the real lunge damage circle as a gizmo while the dog is selected. Useful for confirming the red visual matches the actual hitbox.")]
    [SerializeField] private bool drawActualLungeHitboxGizmo = true;

    [Tooltip("Fade the hitbox visual in when the lunge begins and fade it out after the lunge ends.")]
    [SerializeField] private bool fadeLungeHitboxVisual = true;

    [SerializeField, Min(0f)] private float lungeHitboxVisualFadeInSeconds = 0.07f;
    [SerializeField, Min(0f)] private float lungeHitboxVisualFadeOutSeconds = 0.12f;

    [Tooltip("Final opacity of the visible danger marker. 1 = fully opaque, 0.6 = mostly transparent.")]
    [SerializeField, Range(0f, 1f)] private float lungeHitboxVisualMaxAlpha = 0.85f;

    [Header("Punish Window")]
    [Tooltip("How long the dog is stuck in recovery after a lunge.")]
    [SerializeField, Min(0.05f)] private float recoverySeconds = 0.70f;

    [Tooltip("Extra pause after missing. Helps the player punish a dodged lunge.")]
    [SerializeField, Min(0f)] private float missRecoveryBonusSeconds = 0.10f;

    [Tooltip("Extra pause after hitting. Set lower than miss bonus if a successful hit should feel snappier.")]
    [SerializeField, Min(0f)] private float hitRecoveryBonusSeconds = 0.00f;

    [Header("Hit Reactions")]
    [Tooltip("A hit during telegraph interrupts the lunge and forces a retreat.")]
    [SerializeField] private bool interruptTelegraphOnHit = true;

    [Tooltip("A hit during lunge can interrupt the lunge. Usually false unless you want very strong melee counterplay.")]
    [SerializeField] private bool interruptLungeOnHit = false;

    [SerializeField, Min(0f)] private float hitReactSeconds = 0.18f;

    [Tooltip("Normal prowling hits below this damage only cause a brief flinch instead of a full retreat. Telegraph and recovery interrupts still force a retreat.")]
    [SerializeField, Min(1)] private int minimumDamageForFullRetreat = 3;

    [Tooltip("Minimum time between normal full-retreat reactions. Prevents rapid low-damage attacks from repeatedly sending the dog away.")]
    [SerializeField, Min(0f)] private float fullRetreatReactionCooldown = 1.0f;

    [Tooltip("Length of the brief non-retreat flinch used when a normal hit does not qualify for a full retreat.")]
    [SerializeField, Min(0f)] private float lightHitReactSeconds = 0.10f;

    [SerializeField, Min(0f)] private float normalHitRetreatSeconds = 0.75f;
    [SerializeField, Min(0f)] private float interruptHitRetreatSeconds = 1.05f;
    [SerializeField, Min(0f)] private float lowHealthRetreatSeconds = 1.25f;
    [SerializeField, Min(0f)] private float hitRetreatDistance = 3.0f;
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.35f;
    [SerializeField, Min(0f)] private float attackDelayAfterHit = 1.0f;

    [Header("Flee / Re-Engage")]
    [Tooltip("After the dog finishes a retreat or back-off, it temporarily ignores flee logic so it returns to pressure instead of staying passive forever.")]
    [SerializeField] private bool forceReengageAfterRetreat = true;

    [Tooltip("How long the dog is allowed to act aggressive after a retreat/back-off before low-health or Retreater role can make it flee again.")]
    [SerializeField, Min(0f)] private float reengageAggressionSeconds = 2.25f;

    [Tooltip("During re-engage, ignore the squad Retreater role. This prevents the dog from immediately selecting BackOff again.")]
    [SerializeField] private bool reengageIgnoresRetreaterRole = true;

    [Tooltip("During re-engage, ignore low-health flee checks. Keep this on if the dog feels like it runs away forever at low HP.")]
    [SerializeField] private bool reengageIgnoresLowHealthFlee = true;

    [Tooltip("Failsafe: if BackOff is repeatedly selected for this long, force a re-engage even without reaching the retreat target. 0 disables this failsafe.")]
    [SerializeField, Min(0f)] private float maximumContinuousBackOffSeconds = 2.0f;

    [Header("Damageable Hurtbox")]
    [Tooltip("Assign a CHILD Collider2D used by player attacks to damage the dog. CircleCollider2D and CapsuleCollider2D are auto-configured. Make it a trigger and larger than the physical body/lunge attack radius.")]
    [SerializeField] private Collider2D damageableHurtbox;

    [Tooltip("Automatically applies the configured shape size, offset, trigger state, and optional layer to the assigned hurtbox.")]
    [SerializeField] private bool autoConfigureDamageableHurtbox = true;

    [Tooltip("Used when the assigned damageable hurtbox is a CircleCollider2D. This does not change movement collision or lunge damage.")]
    [SerializeField, Min(0.05f)] private float damageableHurtboxRadius = 0.60f;

    [Tooltip("Used when the assigned damageable hurtbox is a CapsuleCollider2D. X = width, Y = height. This does not change movement collision or lunge damage.")]
    [SerializeField] private Vector2 damageableHurtboxCapsuleSize = new Vector2(0.75f, 1.05f);

    [Tooltip("Used when the assigned damageable hurtbox is a CapsuleCollider2D.")]
    [SerializeField] private CapsuleDirection2D damageableHurtboxCapsuleDirection = CapsuleDirection2D.Vertical;

    [SerializeField] private Vector2 damageableHurtboxOffset = Vector2.zero;

    [Tooltip("The damageable hurtbox should normally be a trigger so it does not create an invisible physical wall around the dog.")]
    [SerializeField] private bool forceDamageableHurtboxTrigger = true;

    [Tooltip("Automatically place the hurtbox child on the named layer when that layer exists.")]
    [SerializeField] private bool assignEnemyHurtboxLayerByName = true;

    [SerializeField] private string enemyHurtboxLayerName = "EnemyHurtbox";
    [SerializeField] private bool drawDamageableHurtboxGizmo = true;

    [Header("Collision")]
    [Tooltip("Walls, cover, fixed arena geometry, and generated obstacles only. Do not include Enemy, Player, hurtbox, trigger bounds, or reserved zones.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("Optional player body/hurtbox layer. Assigning the real player hurtbox layer gives the most exact lunge collision. Leave empty only if using transform fallback.")]
    [SerializeField] private LayerMask playerHitMask;

    [Tooltip("Physical movement/navigation radius only. Match the dog's SMALL solid body collider, not its larger damageable hurtbox.")]
    [SerializeField, Min(0.05f)] private float bodyRadius = 0.32f;
    [SerializeField, Min(0f)] private float obstacleSkinWidth = 0.03f;

    [Header("Debug")]
    [SerializeField] private string debugFacing = "Right";
    [SerializeField] private bool drawDebug = true;
    [SerializeField] private DogState debugState;
    [SerializeField] private HarassIntent debugIntent;
    [SerializeField] private EnemySquadRole debugRole;
    [SerializeField] private float debugDistanceToPlayer;
    [SerializeField] private float debugSquadPressure;
    [SerializeField] private string debugPathStatus = "No Path";
    [SerializeField] private string debugLastTargetReason = "None";
    [SerializeField] private string debugBlockedBy = "None";
    [SerializeField] private bool debugPunishWindowActive;
    [SerializeField] private float debugReengageRemaining;
    [SerializeField] private float debugContinuousBackOffSeconds;
    [SerializeField] private float debugSpeedModifierMultiplier = 1f;
    [SerializeField] private string debugQueuedIntent = "None";
    [SerializeField] private float debugFullRetreatCooldownRemaining;
    [SerializeField] private float debugHoldNearPlayerBlockedRemaining;
    [SerializeField] private float debugBlockedMoveSeconds;
    [SerializeField] private int debugConsecutiveTargetFailures;

    private readonly struct BlockedTargetMemory
    {
        public readonly Vector2 position;
        public readonly float expiresAt;

        public BlockedTargetMemory(Vector2 position, float expiresAt)
        {
            this.position = position;
            this.expiresAt = expiresAt;
        }
    }

    private EnemySquadCoordinator squad;
    private Transform playerTransform;

    private DogState state = DogState.Acquire;
    private HarassIntent intent = HarassIntent.None;
    private EnemySquadRole currentRole = EnemySquadRole.None;

    private float nextPlayerSearchTime;
    private float activeStartTime;
    private float nextAttackTime;
    private float stateEndTime;
    private float intentEndTime;
    private float aimLockTime;
    private float backstepEndTime;
    private float nextAllowedRetargetTime;
    private float nextBlockedTelegraphHandleTime;

    private Vector2 desiredVelocity;
    private int movementFacingSign = 1;
    private string debugMovementFacing = "Right";
    private Vector2 currentTarget;
    private bool hasCurrentTarget;

    private Vector2 smoothedPlayerVelocity;
    private Vector2 lastPlayerPosition;
    private bool hasLastPlayerPosition;

    private Vector2 sharedPlayerPosition;
    private bool hasSharedPlayerPosition;
    private float squadPressure01;
    private bool ownsSquadAttackSlot;
    private float nextSquadAttackSlotRequestTime;
    private float lastMeaningfulActionTime;

    private Vector2 lockedLungeTarget;
    private bool hasLockedLungeTarget;
    private Vector2 lungeDirection;
    private Vector2 lungeEndTarget;
    private Vector2 previousLungePosition;
    private bool lungeHasHit;
    private Transform lungeHitboxVisualTransform;
    private SpriteRenderer[] lungeHitboxVisualRenderers;
    private Color[] lungeHitboxVisualBaseColors;
    private HitboxVisualFadeState lungeHitboxVisualFadeState = HitboxVisualFadeState.Hidden;
    private float lungeHitboxVisualFadeStartTime;
    private float lungeHitboxVisualFadeStartAlpha;
    private float lungeHitboxVisualCurrentAlpha;

    private Vector2 retreatTarget;
    private int prowlSideSign = 1;
    private int lastKnownHP;
    private bool lastLungeHitPlayer;
    private float forcedAggressionUntil;
    private float continuousBackOffStartedAt = -1f;
    private float nextFullRetreatAllowedTime;
    private bool hitReactWillRetreat = true;

    private bool hasQueuedIntent;
    private HarassIntent queuedIntent = HarassIntent.None;
    private string queuedIntentReason = "";

    private readonly List<Vector2> path = new List<Vector2>();
    private int pathIndex;
    private Vector2 pathDestination;
    private bool hasPathDestination;
    private float nextPathRefreshTime;

    private readonly List<BlockedTargetMemory> blockedTargets = new List<BlockedTargetMemory>();
    private readonly RaycastHit2D[] castResults = new RaycastHit2D[8];

    private float blockedMoveStartedAt = -1f;
    private Collider2D lastMoveBlocker;
    private float holdNearPlayerForbiddenUntil;
    private int consecutiveTargetPickFailures;
    private float nextTargetFailureEscapeTime;

    public Transform Transform => transform;

    public bool IsAlive =>
        enemyHealth == null || enemyHealth.CurrentHP > 0;

    public float Health01
    {
        get
        {
            if (enemyHealth == null || enemyHealth.MaxHP <= 0)
                return 1f;

            return Mathf.Clamp01(
                enemyHealth.CurrentHP / (float)enemyHealth.MaxHP
            );
        }
    }

    public bool IsRanged => false;
    public bool IsMelee => countAsMeleeAgent;
    public EnemySquadRole CurrentRole => currentRole;

    public float LastMeaningfulActionTime => lastMeaningfulActionTime;

    private void Awake()
    {
        if (enemyHealth == null)
            enemyHealth = GetComponent<EnemyHealth>();

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (navigationGrid == null)
            navigationGrid = FindObjectOfType<ArenaNavigationGrid>(true);

        ResolveMovementFacingSprite();
        ResetMovementFacingToDefault();
        ConfigureDamageableHurtbox();
        CacheLungeHitboxVisual();
        ResolveLungeDamageHitbox();
        ConfigureLungeDamageHitbox();

        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }
    }

    private void OnValidate()
    {
        damageableHurtboxRadius = Mathf.Max(0.05f, damageableHurtboxRadius);
        damageableHurtboxCapsuleSize = new Vector2(
            Mathf.Max(0.05f, damageableHurtboxCapsuleSize.x),
            Mathf.Max(0.05f, damageableHurtboxCapsuleSize.y)
        );
        lungeHitRadius = Mathf.Max(0.05f, lungeHitRadius);
        bodyRadius = Mathf.Max(0.05f, bodyRadius);
        playerTransformFallbackExtraRadius = Mathf.Max(0f, playerTransformFallbackExtraRadius);

        ConfigureDamageableHurtbox();
        CacheLungeHitboxVisual();
        ResolveLungeDamageHitbox();
        ConfigureLungeDamageHitbox();
    }

    [ContextMenu("Configure Damageable Hurtbox")]
    private void ConfigureDamageableHurtbox()
    {
        if (!autoConfigureDamageableHurtbox || damageableHurtbox == null)
            return;

        if (damageableHurtbox is CircleCollider2D circle)
        {
            circle.radius = Mathf.Max(0.05f, damageableHurtboxRadius);
            circle.offset = damageableHurtboxOffset;
        }
        else if (damageableHurtbox is CapsuleCollider2D capsule)
        {
            capsule.size = new Vector2(
                Mathf.Max(0.05f, damageableHurtboxCapsuleSize.x),
                Mathf.Max(0.05f, damageableHurtboxCapsuleSize.y)
            );
            capsule.direction = damageableHurtboxCapsuleDirection;
            capsule.offset = damageableHurtboxOffset;
        }
        else if (damageableHurtbox is BoxCollider2D box)
        {
            box.size = damageableHurtboxCapsuleSize;
            box.offset = damageableHurtboxOffset;
        }

        if (forceDamageableHurtboxTrigger)
            damageableHurtbox.isTrigger = true;

        if (assignEnemyHurtboxLayerByName &&
            !string.IsNullOrWhiteSpace(enemyHurtboxLayerName))
        {
            int layer = LayerMask.NameToLayer(enemyHurtboxLayerName);

            if (layer >= 0)
                damageableHurtbox.gameObject.layer = layer;
        }
    }

    private void OnEnable()
    {
        if (enemyHealth != null)
        {
            enemyHealth.OnHealthChanged += HandleHealthChanged;
            enemyHealth.OnDied += HandleDied;
            lastKnownHP = enemyHealth.CurrentHP;
        }

        if (registerWithSquad)
        {
            squad = FindObjectOfType<EnemySquadCoordinator>(true);
            if (squad != null)
                squad.Register(this);
        }

        if (navigationGrid == null)
            navigationGrid = FindObjectOfType<ArenaNavigationGrid>(true);

        activeStartTime = Time.time + initialPassiveSeconds;
        prowlSideSign = Random.value < 0.5f ? -1 : 1;

        ResolveMovementFacingSprite();
        ResetMovementFacingToDefault();
        ClearPath();
        ClearMovement();
        RestoreTelegraphColor();
        CacheLungeHitboxVisual();
        ResolveLungeDamageHitbox();
        ConfigureLungeDamageHitbox();
        ValidateLungeDamageHitboxSetup();
        DisarmLungeDamageHitbox();
        ForceHideLungeHitboxVisual();
        blockedTargets.Clear();
        forcedAggressionUntil = 0f;
        continuousBackOffStartedAt = -1f;
        nextFullRetreatAllowedTime = 0f;
        hitReactWillRetreat = true;
        hasQueuedIntent = false;
        queuedIntent = HarassIntent.None;
        queuedIntentReason = "";
        blockedMoveStartedAt = -1f;
        lastMoveBlocker = null;
        holdNearPlayerForbiddenUntil = 0f;
        consecutiveTargetPickFailures = 0;
        nextTargetFailureEscapeTime = 0f;
        ownsSquadAttackSlot = false;
        nextSquadAttackSlotRequestTime = 0f;
        lastMeaningfulActionTime = Time.time;

        ResolvePlayerTransform(force: true);
        ScheduleNextAttack(initialAttackDelayRange);
        EnterState(DogState.Acquire);
    }

    private void OnDisable()
    {
        if (enemyHealth != null)
        {
            enemyHealth.OnHealthChanged -= HandleHealthChanged;
            enemyHealth.OnDied -= HandleDied;
        }

        ReleaseSquadAttackSlot("Disabled");

        if (squad != null)
            squad.Unregister(this);

        ClearPath();
        ClearMovement();
        RestoreTelegraphColor();
        DisarmLungeDamageHitbox();
        ForceHideLungeHitboxVisual();
    }

    private void Update()
    {
        debugState = state;
        debugFacing = debugMovementFacing;
        debugIntent = intent;
        debugRole = currentRole;
        debugSquadPressure = squadPressure01;
        debugPunishWindowActive = state == DogState.Recovery;
        debugReengageRemaining = Mathf.Max(0f, forcedAggressionUntil - Time.time);
        debugContinuousBackOffSeconds = continuousBackOffStartedAt >= 0f
            ? Mathf.Max(0f, Time.time - continuousBackOffStartedAt)
            : 0f;
        debugSpeedModifierMultiplier = GetCurrentSpeedModifierMultiplier();
        debugQueuedIntent = hasQueuedIntent
            ? $"{queuedIntent}: {queuedIntentReason}"
            : "None";
        debugFullRetreatCooldownRemaining = Mathf.Max(
            0f,
            nextFullRetreatAllowedTime - Time.time
        );
        debugHoldNearPlayerBlockedRemaining = Mathf.Max(
            0f,
            holdNearPlayerForbiddenUntil - Time.time
        );
        debugBlockedMoveSeconds = blockedMoveStartedAt >= 0f
            ? Mathf.Max(0f, Time.time - blockedMoveStartedAt)
            : 0f;
        debugConsecutiveTargetFailures = consecutiveTargetPickFailures;

        if (!IsAlive)
        {
            ClearMovement();
            return;
        }

        ResolvePlayerTransform(force: false);
        CleanupBlockedMemory();

        if (playerTransform == null && !hasSharedPlayerPosition)
        {
            EnterState(DogState.Acquire);
            ClearMovement();
            return;
        }

        UpdatePlayerVelocity();

        debugDistanceToPlayer = Vector2.Distance(
            GetPosition(),
            GetPlayerPosition()
        );

        switch (state)
        {
            case DogState.Acquire:
                TickAcquire();
                break;

            case DogState.ChooseIntent:
                TickChooseIntent();
                break;

            case DogState.Prowl:
                TickProwl();
                break;

            case DogState.HoldThreat:
                TickHoldThreat();
                break;

            case DogState.Telegraph:
                TickTelegraph();
                break;

            case DogState.Lunge:
                TickLunge();
                break;

            case DogState.Recovery:
                TickRecovery();
                break;

            case DogState.HitReact:
                TickHitReact();
                break;

            case DogState.Retreat:
                TickRetreat();
                break;
        }

        TickLungeHitboxVisualFade();
    }

    private void FixedUpdate()
    {
        if (!IsAlive || desiredVelocity.sqrMagnitude <= 0.0001f)
        {
            SetRigidbodyVelocity(Vector2.zero);
            return;
        }

        Vector2 startPosition = GetPosition();
        Vector2 direction = desiredVelocity.normalized;

        float movementMultiplier = GetCurrentSpeedModifierMultiplier();
        if (state == DogState.Lunge)
        {
            movementMultiplier = Mathf.Max(
                Mathf.Clamp01(minimumLungeSpeedMultiplier),
                movementMultiplier
            );
        }

        float distance =
            desiredVelocity.magnitude *
            movementMultiplier *
            Time.fixedDeltaTime;

        if (distance <= 0.0001f)
        {
            SetRigidbodyVelocity(Vector2.zero);
            return;
        }

        float allowedDistance = distance;
        Collider2D blocker = null;
        Vector2 blockerNormal = Vector2.zero;

        if (obstacleMask.value != 0)
        {
            int hitCount = Physics2D.CircleCastNonAlloc(
                startPosition,
                bodyRadius,
                direction,
                castResults,
                distance + obstacleSkinWidth,
                obstacleMask
            );

            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hitCollider = castResults[i].collider;
                if (hitCollider == null)
                    continue;

                if (hitCollider.transform == transform ||
                    hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (castResults[i].distance < bestDistance)
                {
                    bestDistance = castResults[i].distance;
                    blocker = hitCollider;
                    blockerNormal = castResults[i].normal;
                }
            }

            if (blocker != null)
            {
                allowedDistance = Mathf.Max(
                    0f,
                    bestDistance - obstacleSkinWidth
                );
            }
        }

        Vector2 nextPosition = startPosition + direction * allowedDistance;
        Vector2 appliedMovement = nextPosition - startPosition;

        if (appliedMovement.sqrMagnitude > 0.000001f)
        {
            UpdateMovementFacing(appliedMovement);
            ResetBlockedMoveTracker();
        }

        if (rb != null)
            rb.MovePosition(nextPosition);
        else
            transform.position = nextPosition;

        if (state == DogState.Lunge)
        {
            // Keep the red visual glued to the exact active hitbox center for
            // the position the dog is moving to this physics step. This avoids
            // a visible one-frame offset from Rigidbody2D.MovePosition timing.
            // Sweep the exact visible Collider2D through this movement step before
            // the Rigidbody applies MovePosition. This keeps fast lunges from tunneling.
            SweepVisibleLungeHitboxTo(nextPosition, lungeDirection);

            // A successful hit may already have moved the dog into Recovery.
            if (state != DogState.Lunge)
                return;

            // Keep the visible damage object at the same local offset from the dog.
            UpdateLungeHitboxVisualAt(nextPosition, lungeDirection);
            CheckVisibleLungeHitboxOverlap();

            if (state != DogState.Lunge)
                return;

            if (blocker != null)
            {
                HandleLungeWallCrash(blocker);
            }
        }
        else if (blocker != null)
        {
            HandleBlockedMove(blocker, blockerNormal, direction);
        }
    }

    public void SetRole(EnemySquadRole role)
    {
        if (currentRole == role)
            return;

        currentRole = role;

        HarassIntent roleIntent = HarassIntent.None;
        string roleReason = $"Role:{role}";

        switch (role)
        {
            case EnemySquadRole.FlankerLeft:
                prowlSideSign = -1;
                roleIntent = HarassIntent.ProwlLeft;
                break;

            case EnemySquadRole.FlankerRight:
                prowlSideSign = 1;
                roleIntent = HarassIntent.ProwlRight;
                break;

            case EnemySquadRole.Anchor:
                roleIntent = Time.time < holdNearPlayerForbiddenUntil
                    ? (prowlSideSign < 0 ? HarassIntent.ProwlLeft : HarassIntent.ProwlRight)
                    : HarassIntent.HoldNearPlayer;
                break;

            case EnemySquadRole.Retreater:
                if (!(IsForcedReengageActive() &&
                      reengageIgnoresRetreaterRole))
                {
                    roleIntent = HarassIntent.BackOff;
                }
                break;

            case EnemySquadRole.Suppressor:
                roleIntent =
                    smoothedPlayerVelocity.magnitude >= playerMovingSpeedThreshold
                        ? HarassIntent.CutOffEscape
                        : HarassIntent.DirectPressure;
                break;
        }

        if (roleIntent != HarassIntent.None)
        {
            QueueNextIntent(
                roleIntent,
                roleReason,
                overwrite: true
            );
        }
        else if (role == EnemySquadRole.None &&
                 hasQueuedIntent &&
                 queuedIntentReason.StartsWith("Role:"))
        {
            hasQueuedIntent = false;
            queuedIntent = HarassIntent.None;
            queuedIntentReason = "";
        }

        // Telegraphs, lunges, hit reactions, retreats, and punish windows
        // are committed readable actions. Let them finish, then use the queued role intent.
        if (state == DogState.Telegraph ||
            state == DogState.Lunge ||
            state == DogState.Recovery ||
            state == DogState.HitReact ||
            state == DogState.Retreat)
        {
            return;
        }

        if (roleIntent != HarassIntent.None)
        {
            ClearPath();
            ClearMovement();
            hasCurrentTarget = false;
            nextAllowedRetargetTime = Time.time;
            EnterState(DogState.ChooseIntent);
        }
    }

    public void SetSharedPlayerPosition(Vector2 pos)
    {
        sharedPlayerPosition = pos;
        hasSharedPlayerPosition = true;
    }

    public void NotifySquadPressure(float pressure01)
    {
        squadPressure01 = Mathf.Clamp01(pressure01);
    }

    private void TickAcquire()
    {
        ResolvePlayerTransform(force: true);

        if (playerTransform != null || hasSharedPlayerPosition)
            EnterState(DogState.ChooseIntent);
    }

    private void TickChooseIntent()
    {
        ClearMovement();

        if (Time.time < activeStartTime)
            return;

        if (Time.time < nextAllowedRetargetTime)
            return;

        ChooseNewIntent();
    }

    private void TickProwl()
    {
        if (ShouldStartAttack())
        {
            EnterTelegraph();
            return;
        }

        if (!hasCurrentTarget || Time.time >= intentEndTime)
        {
            EnterState(DogState.ChooseIntent);
            return;
        }

        MoveTowardPoint(currentTarget, GetIntentMoveSpeed(), prowlArrivalRadius);

        if (Vector2.Distance(GetPosition(), currentTarget) <= prowlArrivalRadius)
        {
            ClearMovement();

            if (intent == HarassIntent.BackOff)
            {
                BeginForcedReengage("BackOff reached");
                stateEndTime = Time.time + RandomRange(arrivalPauseRange);
                EnterState(DogState.HoldThreat);
                return;
            }

            stateEndTime = Time.time + RandomRange(arrivalPauseRange);
            EnterState(DogState.HoldThreat);
        }
    }

    private void TickHoldThreat()
    {
        ClearMovement();

        if (allowHoldThreatToAttack && ShouldStartAttack())
        {
            EnterTelegraph();
            return;
        }

        if (Time.time >= stateEndTime)
            EnterState(DogState.ChooseIntent);
    }

    private void TickTelegraph()
    {
        Vector2 playerPosition = GetPlayerPosition();
        Vector2 myPosition = GetPosition();

        if (!hasLockedLungeTarget && Time.time >= aimLockTime)
            LockLungeTarget();

        if (showLungeHitboxVisualDuringTelegraph)
        {
            UpdateTelegraphLungeHitboxPreview();

            if (lungeHitboxVisualObject == null ||
                !lungeHitboxVisualObject.activeSelf)
            {
                ShowLungeHitboxVisual();
            }

            UpdateLungeHitboxVisual();
        }

        if (Time.time < backstepEndTime)
        {
            Vector2 away = myPosition - playerPosition;
            if (away.sqrMagnitude <= 0.0001f)
                away = Random.insideUnitCircle;

            SetDesiredVelocity(away.normalized * backstepSpeed);
        }
        else
        {
            ClearMovement();
        }

        if (Time.time >= stateEndTime)
            EnterLunge();
    }

    private void TickLunge()
    {
        SetDesiredVelocity(lungeDirection * lungeSpeed);
        UpdateLungeHitboxVisual();

        if (Time.time >= stateEndTime)
            EnterRecovery();
    }

    private void TickRecovery()
    {
        ClearMovement();

        if (Time.time >= stateEndTime)
        {
            ScheduleNextAttack(attackCooldownRange);
            EnterState(DogState.ChooseIntent);
        }
    }

    private void TickHitReact()
    {
        ClearMovement();

        if (Time.time >= stateEndTime)
        {
            if (hitReactWillRetreat && retreatTarget != Vector2.zero)
            {
                EnterRetreat(currentHitRetreatSeconds);
            }
            else
            {
                hitReactWillRetreat = true;
                EnterState(DogState.ChooseIntent);
            }
        }
    }

    private float currentHitRetreatSeconds;

    private void TickRetreat()
    {
        MoveTowardPoint(
            retreatTarget,
            moveSpeed * retreatSpeedMultiplier,
            retreatArrivalRadius
        );

        if (Vector2.Distance(GetPosition(), retreatTarget) <= retreatArrivalRadius ||
            Time.time >= stateEndTime)
        {
            ClearMovement();
            BeginForcedReengage("Retreat complete");
            ScheduleNextAttack(new Vector2(attackDelayAfterHit, attackDelayAfterHit + 0.35f));
            EnterState(DogState.ChooseIntent);
        }
    }

    private void ChooseNewIntent()
    {
        HarassIntent chosen;

        if (hasQueuedIntent)
        {
            chosen = queuedIntent;
            debugLastTargetReason = queuedIntentReason;
            hasQueuedIntent = false;
            queuedIntent = HarassIntent.None;
            queuedIntentReason = "";
        }
        else
        {
            chosen = ChooseIntentByRoleAndPressure();
        }

        intent = chosen;

        if (chosen != HarassIntent.BackOff)
            continuousBackOffStartedAt = -1f;

        float duration = RandomRange(intentDurationRange);
        float pressureScale = Mathf.Lerp(1f, 0.82f, squadPressure01 * squadPressureInfluence);
        intentEndTime = Time.time + duration * pressureScale;

        if (Random.value < holdThreatChance && CanHoldThreatNow())
        {
            stateEndTime = Time.time + RandomRange(holdThreatSecondsRange);
            debugLastTargetReason = "Hold Threat";
            EnterState(DogState.HoldThreat);
            return;
        }

        if (TryPickTargetForIntent(intent, out Vector2 target, out string reason))
        {
            consecutiveTargetPickFailures = 0;
            currentTarget = target;
            hasCurrentTarget = true;
            debugLastTargetReason = reason;
            ClearPath();
            EnterState(DogState.Prowl);
        }
        else
        {
            consecutiveTargetPickFailures++;
            debugLastTargetReason = reason;

            if (enableBlockedMoveRescue &&
                consecutiveTargetPickFailures >= targetFailureEscapeThreshold &&
                Time.time >= nextTargetFailureEscapeTime)
            {
                nextTargetFailureEscapeTime = Time.time + targetFailureEscapeCooldown;
                TryEmergencyNudge(GetPosition() - GetPlayerPosition());
                ForbidHoldNearPlayer("Target failure rescue");
                QueueSideProwlAroundBlocker("Target failure rescue", overwrite: true);
            }

            nextAllowedRetargetTime = Time.time + invalidTargetRetryDelay;
            EnterState(DogState.ChooseIntent);
        }
    }

    private HarassIntent ChooseIntentByRoleAndPressure()
    {
        if (Random.value < sideSwitchChance)
            prowlSideSign *= -1;

        bool isForcedReengage = IsForcedReengageActive();

        bool retreaterRoleWantsBackOff =
            currentRole == EnemySquadRole.Retreater &&
            !(isForcedReengage && reengageIgnoresRetreaterRole);

        bool lowHealthWantsBackOff =
            Health01 <= lowHealthThreshold * 0.75f &&
            !(isForcedReengage && reengageIgnoresLowHealthFlee);

        if (retreaterRoleWantsBackOff || lowHealthWantsBackOff)
        {
            if (continuousBackOffStartedAt < 0f)
                continuousBackOffStartedAt = Time.time;

            if (maximumContinuousBackOffSeconds <= 0f ||
                Time.time - continuousBackOffStartedAt < maximumContinuousBackOffSeconds)
            {
                return HarassIntent.BackOff;
            }

            BeginForcedReengage("BackOff timeout");
        }
        else
        {
            continuousBackOffStartedAt = -1f;
        }

        if (currentRole == EnemySquadRole.FlankerLeft)
            return HarassIntent.ProwlLeft;

        if (currentRole == EnemySquadRole.FlankerRight)
            return HarassIntent.ProwlRight;

        if (currentRole == EnemySquadRole.Anchor)
        {
            if (Time.time >= holdNearPlayerForbiddenUntil)
                return HarassIntent.HoldNearPlayer;

            return prowlSideSign < 0
                ? HarassIntent.ProwlLeft
                : HarassIntent.ProwlRight;
        }

        bool playerMoving = smoothedPlayerVelocity.magnitude >= playerMovingSpeedThreshold;

        if (playerMoving && Random.value < cutOffEscapeChance)
            return HarassIntent.CutOffEscape;

        float directPressureChance = directPressureChanceAtMaxPressure * squadPressure01;
        if (currentRole == EnemySquadRole.Suppressor)
            directPressureChance += 0.12f;

        if (Random.value < directPressureChance)
            return HarassIntent.DirectPressure;

        return prowlSideSign < 0
            ? HarassIntent.ProwlLeft
            : HarassIntent.ProwlRight;
    }

    private bool CanHoldThreatNow()
    {
        if (Time.time < holdNearPlayerForbiddenUntil)
            return false;

        float distance = Vector2.Distance(GetPosition(), GetPlayerPosition());
        return distance >= preferredMinimumLungeRange && distance <= attackRange + 0.6f;
    }

    private bool TryPickTargetForIntent(
        HarassIntent selectedIntent,
        out Vector2 target,
        out string reason)
    {
        Vector2 playerPosition = GetPlayerPosition();
        Vector2 myPosition = GetPosition();

        if (selectedIntent == HarassIntent.BackOff)
        {
            target = BuildRetreatTarget();
            reason = "Back Off";
            return true;
        }

        Vector2 directCandidate = BuildCandidateForIntent(selectedIntent);

        if (ValidateTarget(directCandidate, out Vector2 validTarget))
        {
            target = validTarget;
            reason = selectedIntent.ToString();
            return true;
        }

        float bestScore = float.NegativeInfinity;
        Vector2 bestTarget = Vector2.zero;
        bool found = false;

        for (int i = 0; i < prowlTargetAttempts; i++)
        {
            float radius = Random.Range(comfortMinDistance, comfortMaxDistance);
            float side = prowlSideSign;

            if (selectedIntent == HarassIntent.ProwlLeft)
                side = -1f;
            else if (selectedIntent == HarassIntent.ProwlRight)
                side = 1f;
            else if (Random.value < 0.35f)
                side *= -1f;

            Vector2 fromPlayerToDog = myPosition - playerPosition;
            if (fromPlayerToDog.sqrMagnitude <= 0.0001f)
                fromPlayerToDog = Random.insideUnitCircle;

            float baseAngle = Mathf.Atan2(fromPlayerToDog.y, fromPlayerToDog.x) * Mathf.Rad2Deg;

            Vector2 stepRange = useArcProwlMovement
                ? (Random.value < occasionalWideArcChance ? wideArcStepDegreesRange : prowlArcStepDegreesRange)
                : new Vector2(35f, 105f);

            float angleStep = RandomRange(stepRange) * side;

            if (useArcProwlMovement && Random.value < prowlBacktrackChance)
                angleStep *= -1f;

            float jitter = Random.Range(-prowlAngleJitterDegrees, prowlAngleJitterDegrees);
            float angle = baseAngle + angleStep + jitter;

            if (useArcProwlMovement)
            {
                float currentRadius = Mathf.Clamp(
                    fromPlayerToDog.magnitude,
                    comfortMinDistance,
                    comfortMaxDistance
                );

                radius = Mathf.Clamp(
                    currentRadius + RandomRange(prowlRadiusVarianceRange),
                    comfortMinDistance,
                    comfortMaxDistance
                );
            }

            Vector2 candidate = playerPosition + AngleToVector(angle) * radius;

            if (!ValidateTarget(candidate, out Vector2 validated))
                continue;

            float distanceFromCurrent = Vector2.Distance(myPosition, validated);
            float distanceToPlayer = Vector2.Distance(playerPosition, validated);
            float rangeScore = -Mathf.Abs(distanceToPlayer - ((comfortMinDistance + comfortMaxDistance) * 0.5f));
            float movementScore = Mathf.Clamp(distanceFromCurrent, 0f, 4f) * 0.15f;
            float blockedPenalty = IsTargetRememberedAsBlocked(validated) ? -4f : 0f;

            float score = rangeScore + movementScore + blockedPenalty + Random.Range(-0.2f, 0.2f);

            if (useSquadPincerTargets && squad != null && pincerTargetScoreWeight > 0f)
            {
                score += squad.ScorePincerCandidate(
                    this,
                    validated,
                    currentRole
                ) * pincerTargetScoreWeight;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = validated;
                found = true;
            }
        }

        if (found)
        {
            target = bestTarget;
            reason = selectedIntent + " Sampled";
            return true;
        }

        target = myPosition;
        reason = "No Valid Prowl Target";
        return false;
    }

    private Vector2 BuildCandidateForIntent(HarassIntent selectedIntent)
    {
        Vector2 playerPosition = GetPlayerPosition();
        Vector2 myPosition = GetPosition();
        float radius = Random.Range(comfortMinDistance, comfortMaxDistance);

        if (useSquadPincerTargets && squad != null)
        {
            if (selectedIntent == HarassIntent.CutOffEscape &&
                useSquadEscapeCutoffPrediction)
            {
                Vector2 cutoff = squad.GetEscapeCutoffPosition(
                    this,
                    interceptAheadDistance,
                    interceptSideOffset,
                    radius
                );

                debugPincerTarget = "Squad Cutoff";
                return cutoff;
            }

            if (selectedIntent == HarassIntent.DirectPressure)
            {
                Vector2 pressure = squad.GetPressurePositionForAgent(
                    this,
                    playerPosition,
                    Mathf.Max(comfortMinDistance, preferredMinimumLungeRange + 0.35f)
                );

                debugPincerTarget = "Squad Direct Pressure";
                return pressure;
            }

            if (selectedIntent == HarassIntent.ProwlLeft ||
                selectedIntent == HarassIntent.ProwlRight)
            {
                float sideBias = selectedIntent == HarassIntent.ProwlLeft ? -1f : 1f;
                Vector2 pressureSide = squad.GetPressurePositionForAgent(
                    this,
                    playerPosition,
                    radius,
                    sideBias
                );

                debugPincerTarget = selectedIntent == HarassIntent.ProwlLeft
                    ? "Squad Left"
                    : "Squad Right";
                return pressureSide;
            }
        }

        if (useArcProwlMovement &&
            (selectedIntent == HarassIntent.ProwlLeft ||
             selectedIntent == HarassIntent.ProwlRight))
        {
            return BuildArcProwlCandidate(selectedIntent);
        }

        if (selectedIntent == HarassIntent.CutOffEscape &&
            smoothedPlayerVelocity.magnitude >= playerMovingSpeedThreshold)
        {
            Vector2 forward = smoothedPlayerVelocity.normalized;
            Vector2 side = Perpendicular(forward) * prowlSideSign;
            return playerPosition + forward * interceptAheadDistance + side * interceptSideOffset;
        }

        if (selectedIntent == HarassIntent.DirectPressure)
        {
            Vector2 away = myPosition - playerPosition;
            if (away.sqrMagnitude <= 0.0001f)
                away = Random.insideUnitCircle;

            return playerPosition + away.normalized * Mathf.Max(comfortMinDistance, preferredMinimumLungeRange + 0.25f);
        }

        if (selectedIntent == HarassIntent.HoldNearPlayer)
        {
            Vector2 away = myPosition - playerPosition;
            if (away.sqrMagnitude <= 0.0001f)
                away = Random.insideUnitCircle;

            float angle = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg;
            angle += Random.Range(-35f, 35f);
            return playerPosition + AngleToVector(angle) * radius;
        }

        int sideSign = prowlSideSign;
        if (selectedIntent == HarassIntent.ProwlLeft)
            sideSign = -1;
        else if (selectedIntent == HarassIntent.ProwlRight)
            sideSign = 1;

        Vector2 fromPlayerToDog = myPosition - playerPosition;
        if (fromPlayerToDog.sqrMagnitude <= 0.0001f)
            fromPlayerToDog = Random.insideUnitCircle;

        float baseAngleDegrees = Mathf.Atan2(fromPlayerToDog.y, fromPlayerToDog.x) * Mathf.Rad2Deg;
        float angleDegrees = baseAngleDegrees + Random.Range(45f, 100f) * sideSign;
        angleDegrees += Random.Range(-prowlAngleJitterDegrees, prowlAngleJitterDegrees);

        return playerPosition + AngleToVector(angleDegrees) * radius;
    }

    private Vector2 BuildArcProwlCandidate(HarassIntent selectedIntent)
    {
        Vector2 playerPosition = GetPlayerPosition();
        Vector2 myPosition = GetPosition();

        Vector2 fromPlayerToDog = myPosition - playerPosition;
        if (fromPlayerToDog.sqrMagnitude <= 0.0001f)
            fromPlayerToDog = Random.insideUnitCircle;

        float currentAngleDegrees = Mathf.Atan2(fromPlayerToDog.y, fromPlayerToDog.x) * Mathf.Rad2Deg;
        float currentRadius = Mathf.Clamp(
            fromPlayerToDog.magnitude,
            comfortMinDistance,
            comfortMaxDistance
        );

        int sideSign = prowlSideSign;
        if (selectedIntent == HarassIntent.ProwlLeft)
            sideSign = -1;
        else if (selectedIntent == HarassIntent.ProwlRight)
            sideSign = 1;

        // A small reversal chance creates the desired back-and-forth circling.
        // It should not fully randomize movement every frame, just occasionally make
        // the dog check back the other direction like an animal pacing around you.
        if (Random.value < prowlBacktrackChance)
            sideSign *= -1;

        Vector2 stepRange = Random.value < occasionalWideArcChance
            ? wideArcStepDegreesRange
            : prowlArcStepDegreesRange;

        float stepDegrees = RandomRange(stepRange) * sideSign;
        float jitter = Random.Range(-prowlAngleJitterDegrees, prowlAngleJitterDegrees);
        float nextAngle = currentAngleDegrees + stepDegrees + jitter;

        float radius = Mathf.Clamp(
            currentRadius + RandomRange(prowlRadiusVarianceRange),
            comfortMinDistance,
            comfortMaxDistance
        );

        return playerPosition + AngleToVector(nextAngle) * radius;
    }

    private bool ValidateTarget(Vector2 candidate, out Vector2 validTarget)
    {
        validTarget = candidate;

        if (IsTargetRememberedAsBlocked(candidate))
            return false;

        if (useNavigationGrid && navigationGrid != null && navigationGrid.IsBuilt)
        {
            validTarget = navigationGrid.FindNearestWalkablePosition(candidate);

            if (!navigationGrid.TryFindPath(GetPosition(), validTarget, path))
            {
                path.Clear();
                return false;
            }

            path.Clear();
        }

        if (obstacleMask.value != 0)
        {
            Collider2D overlap = Physics2D.OverlapCircle(validTarget, bodyRadius, obstacleMask);
            if (overlap != null && !overlap.transform.IsChildOf(transform))
                return false;
        }

        return true;
    }

    private bool ShouldStartAttack()
    {
        if (intent == HarassIntent.BackOff || state == DogState.Retreat || state == DogState.HitReact)
            return false;

        bool forceByThreatGap = threatGapCanHurryLunge && squad != null && squad.ShouldForceAttack(this);

        if (Time.time < nextAttackTime && !forceByThreatGap)
            return false;

        float distance = Vector2.Distance(GetPosition(), GetPlayerPosition());

        bool inNormalRange =
            distance <= attackRange &&
            distance >= preferredMinimumLungeRange;

        bool inCloseThreat =
            forceCloseThreatAttacks &&
            distance <= closeThreatRange;

        if (!inNormalRange && !inCloseThreat)
            return false;

        if (!HasClearLungeLine())
            return false;

        if (requireClearLungeStartBeforeTelegraph &&
            !HasClearLungeStart(out Collider2D startBlocker))
        {
            HandleBlockedTelegraphAttempt(startBlocker);
            return false;
        }

        if (!TryClaimSquadAttackSlotForLunge())
            return false;

        return true;
    }

    private void EnterTelegraph()
    {
        MarkMeaningfulAction("DogTelegraph");
        DisarmLungeDamageHitbox();
        EnterState(DogState.Telegraph);

        ClearPath();
        ClearMovement();

        float clampedTelegraph = Mathf.Max(0.05f, telegraphSeconds);
        stateEndTime = Time.time + clampedTelegraph;
        aimLockTime = Time.time + clampedTelegraph * aimLockFraction;
        backstepEndTime = Time.time + Mathf.Clamp(backstepSeconds, 0f, clampedTelegraph);
        hasLockedLungeTarget = false;

        if (aimLockFraction <= 0.001f)
            LockLungeTarget();

        if (showLungeHitboxVisualDuringTelegraph)
        {
            UpdateTelegraphLungeHitboxPreview();
            ShowLungeHitboxVisual();
            UpdateLungeHitboxVisual();
        }
        else
        {
            ForceHideLungeHitboxVisual();
        }

        ApplyTelegraphColor();
    }

    private void LockLungeTarget()
    {
        lockedLungeTarget = GetPlayerPosition();
        hasLockedLungeTarget = true;
    }

    private void EnterLunge()
    {
        MarkMeaningfulAction("DogLunge");
        RestoreTelegraphColor();

        if (!hasLockedLungeTarget)
            LockLungeTarget();

        Vector2 myPosition = GetPosition();
        Vector2 toLockedTarget = lockedLungeTarget - myPosition;

        if (toLockedTarget.sqrMagnitude <= 0.0001f)
            toLockedTarget = Random.insideUnitCircle;

        lungeDirection = toLockedTarget.normalized;
        lungeEndTarget = lockedLungeTarget + lungeDirection * lungeOvershootDistance;
        previousLungePosition = myPosition;
        lungeHasHit = false;
        lastLungeHitPlayer = false;

        if (cancelLungeWhenCoverBlocksCommittedPath &&
            TryGetLungePathBlocker(
                myPosition,
                lockedLungeTarget,
                out Collider2D committedPathBlocker))
        {
            debugBlockedBy =
                committedPathBlocker != null
                    ? committedPathBlocker.name
                    : "Cover";

            debugLastTargetReason = "Lunge Cancelled By Cover";
            EnterRecovery(
                coverCancelRecoveryBonusSeconds,
                "Cover Cancel"
            );
            return;
        }

        stateEndTime = Time.time + Mathf.Max(0.05f, lungeSeconds);

        EnterState(DogState.Lunge);

        if (lungeHitboxVisualObject == null ||
            !lungeHitboxVisualObject.activeSelf)
        {
            ShowLungeHitboxVisual();
        }

        UpdateLungeHitboxVisual();
        ArmLungeDamageHitbox();
        CheckVisibleLungeHitboxOverlap();
    }

    private void EnterRecovery(
        float extraBonusSeconds = 0f,
        string reason = null)
    {
        ReleaseSquadAttackSlot("Recovery");
        RestoreTelegraphColor();
        HideLungeHitboxVisual();

        ClearPath();
        ClearMovement();

        if (!lastLungeHitPlayer)
        {
            string followUpReason = !string.IsNullOrEmpty(reason)
                ? $"{reason} follow-up"
                : "Miss follow-up";

            QueueOppositeSideProwl(followUpReason);
        }

        float bonus = lastLungeHitPlayer
            ? hitRecoveryBonusSeconds
            : missRecoveryBonusSeconds;

        stateEndTime = Time.time + Mathf.Max(
            0.05f,
            recoverySeconds +
            bonus +
            Mathf.Max(0f, extraBonusSeconds)
        );

        if (!string.IsNullOrEmpty(reason))
            debugLastTargetReason = reason;

        EnterState(DogState.Recovery);
    }

    private void EnterHitReact(
        float retreatSeconds,
        bool retreatAfterReact = true)
    {
        ReleaseSquadAttackSlot("HitReact");
        RestoreTelegraphColor();
        HideLungeHitboxVisual();

        ClearPath();
        ClearMovement();

        hitReactWillRetreat = retreatAfterReact;
        currentHitRetreatSeconds = retreatAfterReact
            ? retreatSeconds
            : 0f;

        retreatTarget = retreatAfterReact
            ? BuildRetreatTarget()
            : Vector2.zero;

        if (retreatAfterReact)
        {
            nextAttackTime = Mathf.Max(
                nextAttackTime,
                Time.time + attackDelayAfterHit
            );

            nextFullRetreatAllowedTime =
                Time.time + fullRetreatReactionCooldown;
        }

        float reactionSeconds = retreatAfterReact
            ? hitReactSeconds
            : lightHitReactSeconds;

        stateEndTime = Time.time + Mathf.Max(0.01f, reactionSeconds);
        EnterState(DogState.HitReact);
    }

    private void EnterRetreat(float seconds)
    {
        ReleaseSquadAttackSlot("Retreat");
        ClearPath();

        if (retreatTarget == Vector2.zero)
            retreatTarget = BuildRetreatTarget();

        stateEndTime = Time.time + Mathf.Max(0.05f, seconds);
        EnterState(DogState.Retreat);
    }

    private Vector2 BuildRetreatTarget()
    {
        Vector2 myPosition = GetPosition();
        Vector2 playerPosition = GetPlayerPosition();
        Vector2 away = myPosition - playerPosition;

        if (away.sqrMagnitude <= 0.0001f)
            away = Random.insideUnitCircle;

        away = RotateVector(away.normalized, Random.Range(-30f, 30f));
        Vector2 candidate = myPosition + away.normalized * hitRetreatDistance;

        if (useNavigationGrid && navigationGrid != null && navigationGrid.IsBuilt)
            candidate = navigationGrid.FindNearestWalkablePosition(candidate);

        return candidate;
    }

    private bool IsForcedReengageActive()
    {
        return forceReengageAfterRetreat && Time.time < forcedAggressionUntil;
    }

    private void BeginForcedReengage(string reason)
    {
        continuousBackOffStartedAt = -1f;

        if (!forceReengageAfterRetreat || reengageAggressionSeconds <= 0f)
            return;

        forcedAggressionUntil = Mathf.Max(
            forcedAggressionUntil,
            Time.time + reengageAggressionSeconds
        );

        debugLastTargetReason = reason;
    }


    private bool TryClaimSquadAttackSlotForLunge()
    {
        if (!useSquadAttackSlots || squad == null)
        {
            debugAttackSlot = "Slots disabled";
            return true;
        }

        if (ownsSquadAttackSlot || squad.IsAttackSlotOwner(this))
        {
            ownsSquadAttackSlot = true;
            debugAttackSlot = "Owned";
            return true;
        }

        if (Time.time < nextSquadAttackSlotRequestTime)
        {
            debugAttackSlot = "Waiting retry";
            return false;
        }

        bool urgent = squad.ShouldForceAttack(this);
        string reason = urgent ? "ThreatGap Dog Lunge" : "Dog Lunge";

        if (squad.TryRequestAttackSlot(this, reason))
        {
            ownsSquadAttackSlot = true;
            debugAttackSlot = reason;
            return true;
        }

        nextSquadAttackSlotRequestTime = Time.time + Mathf.Max(0.03f, attackSlotRetryDelay);
        debugAttackSlot = "Waiting for slot";
        return false;
    }

    private void ReleaseSquadAttackSlot(string reason)
    {
        if (squad != null && (ownsSquadAttackSlot || squad.IsAttackSlotOwner(this)))
            squad.ReleaseAttackSlot(this, reason);

        ownsSquadAttackSlot = false;
    }

    private void MarkMeaningfulAction(string reason)
    {
        lastMeaningfulActionTime = Time.time;
        debugLastTargetReason = reason;
    }

    private void ScheduleNextAttack(Vector2 range)
    {
        float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        float cooldown = Random.Range(min, max);

        float pressureScale = Mathf.Lerp(
            1f,
            0.78f,
            squadPressure01 * squadPressureInfluence
        );

        nextAttackTime = Mathf.Max(
            nextAttackTime,
            Time.time + cooldown * pressureScale
        );
    }

    private float GetIntentMoveSpeed()
    {
        float speed = moveSpeed;

        if (intent == HarassIntent.DirectPressure || intent == HarassIntent.CutOffEscape)
        {
            speed *= Mathf.Lerp(
                1f,
                pressureSpeedMultiplier,
                Mathf.Max(0.35f, squadPressure01)
            );
        }

        return speed;
    }

    private float GetCurrentSpeedModifierMultiplier()
    {
        if (!useSpeedModifier)
            return 1f;

        SpeedModifier modifier = GetComponent<SpeedModifier>();

        if (modifier == null && enemyHealth != null)
            modifier = enemyHealth.GetComponent<SpeedModifier>();

        if (modifier == null)
            modifier = GetComponentInParent<SpeedModifier>();

        return modifier != null
            ? Mathf.Max(0f, modifier.Multiplier)
            : 1f;
    }

    private void QueueNextIntent(
        HarassIntent nextIntent,
        string reason,
        bool overwrite)
    {
        if (nextIntent == HarassIntent.None)
            return;

        if (hasQueuedIntent && !overwrite)
            return;

        queuedIntent = nextIntent;
        queuedIntentReason = reason;
        hasQueuedIntent = true;
    }

    private void QueueOppositeSideProwl(string reason)
    {
        if (!forceOppositeSideProwlAfterFailedLunge ||
            hasQueuedIntent)
        {
            return;
        }

        prowlSideSign *= -1;

        QueueNextIntent(
            prowlSideSign < 0
                ? HarassIntent.ProwlLeft
                : HarassIntent.ProwlRight,
            reason,
            overwrite: false
        );
    }

    private bool TryGetLungePathBlocker(
        Vector2 start,
        Vector2 end,
        out Collider2D blocker)
    {
        blocker = null;

        if (obstacleMask.value == 0)
            return false;

        Vector2 delta = end - start;
        float distance = delta.magnitude;

        if (distance <= 0.001f)
            return false;

        int hitCount = Physics2D.CircleCastNonAlloc(
            start,
            bodyRadius,
            delta / distance,
            castResults,
            distance,
            obstacleMask
        );

        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D candidate = castResults[i].collider;

            if (candidate == null)
                continue;

            if (candidate.transform == transform ||
                candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            if (castResults[i].distance < nearestDistance)
            {
                nearestDistance = castResults[i].distance;
                blocker = candidate;
            }
        }

        return blocker != null;
    }

    private void HandleLungeWallCrash(Collider2D blocker)
    {
        debugBlockedBy = blocker != null
            ? blocker.name
            : "Cover";

        debugLastTargetReason = "Lunge Wall Crash";

        EnterRecovery(
            wallCrashRecoveryBonusSeconds,
            "Wall Crash"
        );
    }

    private void HandleBlockedMove(Collider2D blocker, Vector2 blockerNormal, Vector2 attemptedDirection)
    {
        debugBlockedBy = blocker != null ? blocker.name : "Collider";
        debugLastTargetReason = "Blocked Move";

        if (hasCurrentTarget)
            RememberBlockedTarget(currentTarget);

        if (lastMoveBlocker != blocker || blockedMoveStartedAt < 0f)
        {
            lastMoveBlocker = blocker;
            blockedMoveStartedAt = Time.time;
        }

        if (enableBlockedMoveRescue)
        {
            if (intent == HarassIntent.HoldNearPlayer || state == DogState.HoldThreat)
                ForbidHoldNearPlayer("Blocked hold");

            if (Time.time - blockedMoveStartedAt >= blockedMoveEscapeAfterSeconds)
            {
                Vector2 preferredEscape = blockerNormal.sqrMagnitude > 0.0001f
                    ? blockerNormal
                    : -attemptedDirection;

                TryEmergencyNudge(preferredEscape);
                ResetBlockedMoveTracker();
            }

            QueueSideProwlAroundBlocker("Blocked move side prowl", overwrite: true);
        }

        nextAllowedRetargetTime = Time.time + blockedMoveRetryDelay;
        ClearPath();
        ClearMovement();
        hasCurrentTarget = false;
        EnterState(DogState.ChooseIntent);
    }

    private void ResetBlockedMoveTracker()
    {
        blockedMoveStartedAt = -1f;
        lastMoveBlocker = null;
    }

    private void ForbidHoldNearPlayer(string reason)
    {
        holdNearPlayerForbiddenUntil = Mathf.Max(
            holdNearPlayerForbiddenUntil,
            Time.time + holdNearPlayerBlockedCooldown
        );

        if (!string.IsNullOrEmpty(reason))
            debugLastTargetReason = reason;
    }

    private void QueueSideProwlAroundBlocker(string reason, bool overwrite)
    {
        if (prowlSideSign == 0)
            prowlSideSign = Random.value < 0.5f ? -1 : 1;

        prowlSideSign *= -1;

        QueueNextIntent(
            prowlSideSign < 0 ? HarassIntent.ProwlLeft : HarassIntent.ProwlRight,
            reason,
            overwrite
        );
    }

    private bool TryEmergencyNudge(Vector2 preferredDirection)
    {
        if (!enableBlockedMoveRescue || blockedMoveNudgeDistance <= 0f)
            return false;

        Vector2 position = GetPosition();
        Vector2 preferred = preferredDirection.sqrMagnitude > 0.0001f
            ? preferredDirection.normalized
            : Random.insideUnitCircle.normalized;

        Collider2D overlap = FindBlockingOverlap(position);
        if (overlap != null)
        {
            Vector2 closest = overlap.ClosestPoint(position);
            Vector2 away = position - closest;
            if (away.sqrMagnitude > 0.0001f)
                preferred = away.normalized;
        }

        Vector2[] directions = new Vector2[]
        {
            preferred,
            RotateVector(preferred, 35f).normalized,
            RotateVector(preferred, -35f).normalized,
            Perpendicular(preferred).normalized,
            -Perpendicular(preferred).normalized,
            -preferred,
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right
        };

        for (int i = 0; i < directions.Length; i++)
        {
            Vector2 dir = directions[i];
            if (dir.sqrMagnitude <= 0.0001f)
                continue;

            if (!WouldNudgeHitObstacle(position, dir.normalized))
            {
                Vector2 newPosition = position + dir.normalized * blockedMoveNudgeDistance;

                if (rb != null)
                    rb.MovePosition(newPosition);
                else
                    transform.position = newPosition;

                UpdateMovementFacing(dir);
                debugLastTargetReason = "Emergency Wall Nudge";
                return true;
            }
        }

        return false;
    }

    private Collider2D FindBlockingOverlap(Vector2 position)
    {
        if (obstacleMask.value == 0)
            return null;

        Collider2D[] overlaps = Physics2D.OverlapCircleAll(position, bodyRadius, obstacleMask);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider2D overlap = overlaps[i];
            if (overlap == null)
                continue;

            if (overlap.transform == transform || overlap.transform.IsChildOf(transform))
                continue;

            return overlap;
        }

        return null;
    }

    private bool WouldNudgeHitObstacle(Vector2 position, Vector2 direction)
    {
        if (obstacleMask.value == 0)
            return false;

        int hitCount = Physics2D.CircleCastNonAlloc(
            position,
            bodyRadius,
            direction,
            castResults,
            blockedMoveNudgeDistance + obstacleSkinWidth,
            obstacleMask
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D candidate = castResults[i].collider;
            if (candidate == null)
                continue;

            if (candidate.transform == transform || candidate.transform.IsChildOf(transform))
                continue;

            return true;
        }

        return false;
    }

    private void MoveTowardPoint(Vector2 target, float speed, float arrivalRadius)
    {
        Vector2 myPosition = GetPosition();

        if (Vector2.Distance(myPosition, target) <= arrivalRadius)
        {
            ClearMovement();
            return;
        }

        Vector2 steeringTarget = target;

        if (useNavigationGrid && navigationGrid != null && navigationGrid.IsBuilt)
        {
            bool needsPath =
                !hasPathDestination ||
                Vector2.Distance(pathDestination, target) > 0.25f ||
                path.Count == 0 ||
                pathIndex >= path.Count ||
                Time.time >= nextPathRefreshTime;

            if (needsPath)
            {
                path.Clear();
                pathIndex = 0;

                bool foundPath = navigationGrid.TryFindPath(myPosition, target, path);
                hasPathDestination = true;
                pathDestination = target;
                nextPathRefreshTime = Time.time + pathRefreshInterval;

                if (!foundPath || path.Count == 0)
                {
                    debugPathStatus = "No Path";
                    RememberBlockedTarget(target);
                    nextAllowedRetargetTime = Time.time + invalidTargetRetryDelay;
                    ClearMovement();
                    EnterState(DogState.ChooseIntent);
                    return;
                }

                debugPathStatus = $"Path {path.Count}";
            }

            while (pathIndex < path.Count &&
                   Vector2.Distance(myPosition, path[pathIndex]) <= waypointArrivalRadius)
            {
                pathIndex++;
            }

            if (pathIndex < path.Count)
                steeringTarget = path[pathIndex];
        }
        else
        {
            debugPathStatus = "Direct";
        }

        Vector2 toTarget = steeringTarget - myPosition;

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            ClearMovement();
            return;
        }

        SetDesiredVelocity(toTarget.normalized * Mathf.Max(0f, speed));
    }

    private bool HasClearLungeLine()
    {
        if (obstacleMask.value == 0)
            return true;

        return CombatLineOfSight2D.HasLineOfSight(
            this,
            GetPosition(),
            GetPlayerPosition(),
            obstacleMask,
            out _
        );
    }

    private bool HasClearLungeStart(out Collider2D blocker)
    {
        blocker = null;

        if (obstacleMask.value == 0)
            return true;

        Vector2 start = GetPosition();
        Vector2 target = GetPlayerPosition();
        Vector2 delta = target - start;
        float distanceToTarget = delta.magnitude;

        if (distanceToTarget <= 0.001f)
            return true;

        Vector2 direction = delta / distanceToTarget;
        float checkDistance = Mathf.Min(
            distanceToTarget,
            Mathf.Max(0.05f, lungeStartClearanceDistance)
        );

        int hitCount = Physics2D.CircleCastNonAlloc(
            start,
            bodyRadius,
            direction,
            castResults,
            checkDistance + obstacleSkinWidth,
            obstacleMask
        );

        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D candidate = castResults[i].collider;

            if (candidate == null)
                continue;

            if (candidate.transform == transform ||
                candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            if (castResults[i].distance < nearestDistance)
            {
                nearestDistance = castResults[i].distance;
                blocker = candidate;
            }
        }

        return blocker == null;
    }

    private void HandleBlockedTelegraphAttempt(Collider2D blocker)
    {
        if (Time.time < nextBlockedTelegraphHandleTime)
            return;

        nextBlockedTelegraphHandleTime =
            Time.time + Mathf.Max(0.05f, blockedTelegraphRetargetDelay);

        debugBlockedBy = blocker != null
            ? blocker.name
            : "Cover";

        debugLastTargetReason = "Telegraph Blocked By Cover";

        nextAttackTime = Time.time +
            Mathf.Max(0.05f, blockedTelegraphRetryDelay);

        nextAllowedRetargetTime = Time.time +
            Mathf.Max(0.05f, blockedTelegraphRetargetDelay);

        if (hasCurrentTarget)
            RememberBlockedTarget(currentTarget);
        else
            RememberBlockedTarget(GetPlayerPosition());

        ClearPath();
        ClearMovement();
        RestoreTelegraphColor();
        ForceHideLungeHitboxVisual();

        if (queueSideProwlWhenTelegraphBlocked)
            QueueOppositeSideProwl("Telegraph blocked by cover");
    }

    public bool TryApplyLungeDamageFromVisibleHitbox(CombatPawn pawn)
    {
        if (pawn == null ||
            state != DogState.Lunge ||
            lungeHasHit)
        {
            return false;
        }

        pawn.ApplyDamage(lungeDamage);
        lungeHasHit = true;
        lastLungeHitPlayer = true;
        debugLastTargetReason = "Visible Lunge Hitbox Hit Player";

        if (endLungeOnHit)
            EnterRecovery();

        return true;
    }

    private void HandleHealthChanged(int current, int maximum)
    {
        int damageTaken = Mathf.Max(0, lastKnownHP - current);

        if (damageTaken > 0 && current > 0)
        {
            float retreatSeconds = Health01 <= lowHealthThreshold
                ? lowHealthRetreatSeconds
                : normalHitRetreatSeconds;

            bool ignoreReactionForCommittedAttack =
                (state == DogState.Telegraph &&
                 !interruptTelegraphOnHit) ||
                (state == DogState.Lunge &&
                 !interruptLungeOnHit);

            if (!ignoreReactionForCommittedAttack)
            {
                bool forcedRetreatReaction = false;

                if (state == DogState.Telegraph &&
                    interruptTelegraphOnHit)
                {
                    retreatSeconds = Mathf.Max(
                        retreatSeconds,
                        interruptHitRetreatSeconds
                    );

                    forcedRetreatReaction = true;
                }
                else if (state == DogState.Lunge &&
                         interruptLungeOnHit)
                {
                    retreatSeconds = Mathf.Max(
                        retreatSeconds,
                        interruptHitRetreatSeconds
                    );

                    forcedRetreatReaction = true;
                }
                else if (state == DogState.Recovery)
                {
                    forcedRetreatReaction = true;
                }

                if (forcedRetreatReaction)
                {
                    EnterHitReact(
                        retreatSeconds,
                        retreatAfterReact: true
                    );
                }
                else if (state != DogState.HitReact &&
                         state != DogState.Retreat)
                {
                    bool lowHealthQualifies =
                        Health01 <= lowHealthThreshold;

                    bool normalFullRetreatAllowed =
                        (damageTaken >= minimumDamageForFullRetreat ||
                         lowHealthQualifies) &&
                        Time.time >= nextFullRetreatAllowedTime;

                    EnterHitReact(
                        retreatSeconds,
                        retreatAfterReact: normalFullRetreatAllowed
                    );

                    debugLastTargetReason = normalFullRetreatAllowed
                        ? $"Full Retreat Hit ({damageTaken})"
                        : $"Light Hit ({damageTaken})";
                }
            }
        }

        lastKnownHP = current;
    }

    private void HandleDied(EnemyHealth dead)
    {
        ClearMovement();
        RestoreTelegraphColor();
        HideLungeHitboxVisual();
        ReleaseSquadAttackSlot("Died");

        if (squad != null)
            squad.Unregister(this);
    }

    private void ResolvePlayerTransform(bool force)
    {
        if (!autoFindPlayerByTag)
            return;

        if (playerTransform != null)
            return;

        if (!force && Time.time < nextPlayerSearchTime)
            return;

        nextPlayerSearchTime = Time.time + playerSearchInterval;

        GameObject playerObject = null;

        try
        {
            playerObject = GameObject.FindWithTag(playerTag);
        }
        catch
        {
            return;
        }

        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
            lastPlayerPosition = playerTransform.position;
            hasLastPlayerPosition = true;
        }
    }

    private void UpdatePlayerVelocity()
    {
        if (playerTransform == null)
            return;

        Vector2 current = playerTransform.position;

        if (!hasLastPlayerPosition)
        {
            lastPlayerPosition = current;
            hasLastPlayerPosition = true;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 rawVelocity = (current - lastPlayerPosition) / dt;

        float blend = 1f - Mathf.Exp(-playerVelocitySampleSharpness * dt);
        smoothedPlayerVelocity = Vector2.Lerp(smoothedPlayerVelocity, rawVelocity, blend);

        lastPlayerPosition = current;
    }

    private void ApplyTelegraphColor()
    {
        if (telegraphFlashRenderers == null)
            return;

        for (int i = 0; i < telegraphFlashRenderers.Length; i++)
        {
            if (telegraphFlashRenderers[i] != null)
                telegraphFlashRenderers[i].color = telegraphColor;
        }
    }

    private void RestoreTelegraphColor()
    {
        if (telegraphFlashRenderers == null)
            return;

        for (int i = 0; i < telegraphFlashRenderers.Length; i++)
        {
            if (telegraphFlashRenderers[i] != null)
                telegraphFlashRenderers[i].color = Color.white;
        }
    }

    private void UpdateTelegraphLungeHitboxPreview()
    {
        Vector2 myPosition = GetPosition();
        Vector2 targetPosition;

        if (hasLockedLungeTarget)
        {
            targetPosition = lockedLungeTarget;
        }
        else if (telegraphVisualTracksUntilAimLock)
        {
            targetPosition = GetPlayerPosition();
        }
        else
        {
            targetPosition = GetPlayerPosition();
        }

        Vector2 toTarget = targetPosition - myPosition;

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            toTarget = lungeDirection.sqrMagnitude > 0.0001f
                ? lungeDirection
                : Vector2.right;
        }

        lungeDirection = toTarget.normalized;
    }

    private void ResolveLungeDamageHitbox()
    {
        if (lungeDamageHitbox != null)
            return;

        if (!autoFindLungeDamageHitbox ||
            lungeHitboxVisualObject == null)
        {
            return;
        }

        lungeDamageHitbox =
            lungeHitboxVisualObject.GetComponent<AttackDogLungeHitbox>();

        if (lungeDamageHitbox == null)
        {
            lungeDamageHitbox =
                lungeHitboxVisualObject.GetComponentInChildren<AttackDogLungeHitbox>(true);
        }
    }

    private void ConfigureLungeDamageHitbox()
    {
        ResolveLungeDamageHitbox();

        if (lungeDamageHitbox == null)
            return;

        lungeDamageHitbox.Configure(this, playerHitMask);
    }

    private void ValidateLungeDamageHitboxSetup()
    {
        if (!showLungeHitboxVisual)
            return;

        if (lungeHitboxVisualObject == null)
        {
            Debug.LogError(
                "AttackDogBrain: Lunge Hitbox Visual Object is missing.",
                this
            );
            return;
        }

        if (lungeDamageHitbox == null)
        {
            Debug.LogError(
                "AttackDogBrain: Add AttackDogLungeHitbox and a Collider2D to the visible red lunge object.",
                this
            );
            return;
        }

        if (playerHitMask.value == 0)
        {
            Debug.LogWarning(
                "AttackDogBrain: Player Hit Mask is empty, so the visible lunge collider cannot damage the player.",
                this
            );
        }
    }

    private void ArmLungeDamageHitbox()
    {
        ConfigureLungeDamageHitbox();

        if (lungeDamageHitbox != null)
            lungeDamageHitbox.SetDamageActive(true);
    }

    private void DisarmLungeDamageHitbox()
    {
        if (lungeDamageHitbox != null)
            lungeDamageHitbox.SetDamageActive(false);
    }

    private void SweepVisibleLungeHitboxTo(
        Vector2 nextDogPosition,
        Vector2 direction)
    {
        if (lungeDamageHitbox == null ||
            lungeHitboxVisualTransform == null)
        {
            return;
        }

        Vector2 targetPosition =
            GetLungeHitboxWorldPosition(nextDogPosition, direction);

        Vector2 delta =
            targetPosition -
            (Vector2)lungeHitboxVisualTransform.position;

        lungeDamageHitbox.SweepForDamage(delta);
    }

    private void CheckVisibleLungeHitboxOverlap()
    {
        if (lungeDamageHitbox != null)
            lungeDamageHitbox.CheckCurrentOverlap();
    }

    private void CacheLungeHitboxVisual()
    {
        if (lungeHitboxVisualObject == null)
        {
            lungeHitboxVisualTransform = null;
            lungeHitboxVisualRenderers = null;
            lungeHitboxVisualBaseColors = null;
            return;
        }

        lungeHitboxVisualTransform = lungeHitboxVisualObject.transform;
        ResolveLungeDamageHitbox();

        SpriteRenderer[] renderers =
            lungeHitboxVisualObject.GetComponentsInChildren<SpriteRenderer>(true);

        bool needsColorCache =
            lungeHitboxVisualRenderers == null ||
            lungeHitboxVisualBaseColors == null ||
            lungeHitboxVisualRenderers.Length != renderers.Length;

        lungeHitboxVisualRenderers = renderers;

        if (needsColorCache)
        {
            lungeHitboxVisualBaseColors = new Color[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                lungeHitboxVisualBaseColors[i] =
                    renderers[i] != null ? renderers[i].color : Color.white;
            }
        }
    }

    private void ShowLungeHitboxVisual()
    {
        if (!showLungeHitboxVisual || lungeHitboxVisualObject == null)
            return;

        CacheLungeHitboxVisual();
        lungeHitboxVisualObject.SetActive(true);

        if (!fadeLungeHitboxVisual || lungeHitboxVisualFadeInSeconds <= 0f)
        {
            SetLungeHitboxVisualAlpha(1f);
            lungeHitboxVisualFadeState = HitboxVisualFadeState.Visible;
            return;
        }

        SetLungeHitboxVisualAlpha(0f);
        lungeHitboxVisualFadeStartAlpha = 0f;
        lungeHitboxVisualFadeStartTime = Time.time;
        lungeHitboxVisualFadeState = HitboxVisualFadeState.FadingIn;
    }

    private void HideLungeHitboxVisual()
    {
        DisarmLungeDamageHitbox();

        if (lungeHitboxVisualObject == null)
            return;

        CacheLungeHitboxVisual();

        if (!fadeLungeHitboxVisual ||
            lungeHitboxVisualFadeOutSeconds <= 0f ||
            !lungeHitboxVisualObject.activeSelf)
        {
            ForceHideLungeHitboxVisual();
            return;
        }

        lungeHitboxVisualFadeStartAlpha = lungeHitboxVisualCurrentAlpha;
        lungeHitboxVisualFadeStartTime = Time.time;
        lungeHitboxVisualFadeState = HitboxVisualFadeState.FadingOut;
    }

    private void ForceHideLungeHitboxVisual()
    {
        DisarmLungeDamageHitbox();

        if (lungeHitboxVisualObject == null)
            return;

        CacheLungeHitboxVisual();
        SetLungeHitboxVisualAlpha(0f);
        lungeHitboxVisualObject.SetActive(false);
        lungeHitboxVisualFadeState = HitboxVisualFadeState.Hidden;
    }

    private void TickLungeHitboxVisualFade()
    {
        if (lungeHitboxVisualObject == null)
            return;

        if (lungeHitboxVisualFadeState == HitboxVisualFadeState.FadingIn)
        {
            float duration = Mathf.Max(0.0001f, lungeHitboxVisualFadeInSeconds);
            float t = Mathf.Clamp01((Time.time - lungeHitboxVisualFadeStartTime) / duration);
            float smoothed = Mathf.SmoothStep(0f, 1f, t);

            SetLungeHitboxVisualAlpha(smoothed);

            if (t >= 1f)
                lungeHitboxVisualFadeState = HitboxVisualFadeState.Visible;
        }
        else if (lungeHitboxVisualFadeState == HitboxVisualFadeState.FadingOut)
        {
            float duration = Mathf.Max(0.0001f, lungeHitboxVisualFadeOutSeconds);
            float t = Mathf.Clamp01((Time.time - lungeHitboxVisualFadeStartTime) / duration);
            float smoothed = Mathf.SmoothStep(0f, 1f, t);
            float alpha = Mathf.Lerp(lungeHitboxVisualFadeStartAlpha, 0f, smoothed);

            SetLungeHitboxVisualAlpha(alpha);

            if (t >= 1f)
                ForceHideLungeHitboxVisual();
        }
    }

    private void SetLungeHitboxVisualAlpha(float normalizedAlpha)
    {
        lungeHitboxVisualCurrentAlpha = Mathf.Clamp01(normalizedAlpha);

        if (lungeHitboxVisualRenderers == null ||
            lungeHitboxVisualBaseColors == null)
        {
            return;
        }

        float maxAlpha = Mathf.Clamp01(lungeHitboxVisualMaxAlpha);

        for (int i = 0; i < lungeHitboxVisualRenderers.Length; i++)
        {
            SpriteRenderer renderer = lungeHitboxVisualRenderers[i];
            if (renderer == null)
                continue;

            Color baseColor =
                i < lungeHitboxVisualBaseColors.Length
                    ? lungeHitboxVisualBaseColors[i]
                    : renderer.color;

            Color color = baseColor;
            color.a = baseColor.a * maxAlpha * lungeHitboxVisualCurrentAlpha;
            renderer.color = color;
        }
    }

    private void UpdateLungeHitboxVisual()
    {
        UpdateLungeHitboxVisualAt(GetPosition(), lungeDirection);
    }

    private void UpdateLungeHitboxVisualAt(Vector2 bodyPosition, Vector2 direction)
    {
        if (!showLungeHitboxVisual ||
            lungeHitboxVisualObject == null ||
            lungeHitboxVisualTransform == null)
        {
            return;
        }

        Vector2 forward = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;

        Vector2 center =
            GetLungeHitboxWorldPosition(bodyPosition, forward);

        if (autoScaleLungeHitboxVisual)
        {
            float diameter = Mathf.Max(
                0.01f,
                lungeHitRadius * 2f * lungeHitboxVisualScaleMultiplier
            );

            lungeHitboxVisualTransform.localScale =
                new Vector3(diameter, diameter, 1f);
        }

        if (rotateLungeHitboxVisualToDirection)
        {
            float angle =
                Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;

            lungeHitboxVisualTransform.rotation =
                Quaternion.Euler(0f, 0f, angle);
        }

        if (lungeHitboxVisualTransform.parent == transform)
        {
            Vector2 worldOffset = center - bodyPosition;
            Vector3 localOffset =
                transform.InverseTransformVector(worldOffset);

            Vector3 oldLocal =
                lungeHitboxVisualTransform.localPosition;

            lungeHitboxVisualTransform.localPosition =
                new Vector3(
                    localOffset.x,
                    localOffset.y,
                    oldLocal.z
                );
        }
        else
        {
            Vector3 oldWorld =
                lungeHitboxVisualTransform.position;

            lungeHitboxVisualTransform.position =
                new Vector3(center.x, center.y, oldWorld.z);
        }
    }

    private Vector2 GetLungeHitboxWorldPosition(
        Vector2 bodyPosition,
        Vector2 direction)
    {
        Vector2 forward = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;

        Vector2 center =
            bodyPosition + forward * lungeHitForwardOffset;

        center += LocalOffsetToWorld(
            lungeHitboxVisualOffset,
            forward
        );

        return center;
    }

    private Vector2 GetLungeHitboxCenter(
        Vector2 bodyPosition,
        Vector2 direction)
    {
        return GetLungeHitboxWorldPosition(bodyPosition, direction);
    }

    private void EnterState(DogState newState)
    {
        state = newState;
    }

    private void ClearPath()
    {
        path.Clear();
        pathIndex = 0;
        hasPathDestination = false;
        debugPathStatus = "No Path";
    }

    private void ResolveMovementFacingSprite()
    {
        if (movementFacingSprite != null || !autoFindMovementFacingSprite)
            return;

        if (telegraphFlashRenderers != null)
        {
            for (int i = 0; i < telegraphFlashRenderers.Length; i++)
            {
                SpriteRenderer candidate = telegraphFlashRenderers[i];
                if (candidate == null || IsPartOfLungeHitboxVisual(candidate.transform))
                    continue;

                movementFacingSprite = candidate;
                return;
            }
        }

        SpriteRenderer[] candidates = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            SpriteRenderer candidate = candidates[i];
            if (candidate == null || IsPartOfLungeHitboxVisual(candidate.transform))
                continue;

            movementFacingSprite = candidate;
            return;
        }
    }

    private bool IsPartOfLungeHitboxVisual(Transform candidate)
    {
        if (candidate == null || lungeHitboxVisualObject == null)
            return false;

        Transform hitboxRoot = lungeHitboxVisualObject.transform;
        return candidate == hitboxRoot || candidate.IsChildOf(hitboxRoot);
    }

    private void ResetMovementFacingToDefault()
    {
        movementFacingSign = spriteFacesRightByDefault ? 1 : -1;
        ApplyMovementFacing();
    }

    private void UpdateMovementFacing(Vector2 actualMovement)
    {
        float horizontal = actualMovement.x;
        if (Mathf.Abs(horizontal) < Mathf.Max(0f, minimumHorizontalMovementForFlip))
            return;

        movementFacingSign = horizontal > 0f ? 1 : -1;
        ApplyMovementFacing();
    }

    private void ApplyMovementFacing()
    {
        if (movementFacingSprite == null)
            return;

        bool movingRight = movementFacingSign > 0;
        movementFacingSprite.flipX = spriteFacesRightByDefault
            ? !movingRight
            : movingRight;

        debugMovementFacing = movingRight ? "Right" : "Left";
    }

    private void SetDesiredVelocity(Vector2 velocity)
    {
        desiredVelocity = velocity;
    }

    private void ClearMovement()
    {
        desiredVelocity = Vector2.zero;
        SetRigidbodyVelocity(Vector2.zero);
    }

    private void SetRigidbodyVelocity(Vector2 velocity)
    {
        if (rb == null)
            return;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif
    }

    private Vector2 GetPosition()
    {
        if (rb != null)
            return rb.position;

        return transform.position;
    }

    private Vector2 GetPlayerPosition()
    {
        if (playerTransform != null)
            return playerTransform.position;

        if (hasSharedPlayerPosition)
            return sharedPlayerPosition;

        return GetPosition();
    }

    private void RememberBlockedTarget(Vector2 position)
    {
        blockedTargets.Add(
            new BlockedTargetMemory(
                position,
                Time.time + blockedTargetMemorySeconds
            )
        );
    }

    private bool IsTargetRememberedAsBlocked(Vector2 position)
    {
        for (int i = blockedTargets.Count - 1; i >= 0; i--)
        {
            if (Time.time >= blockedTargets[i].expiresAt)
            {
                blockedTargets.RemoveAt(i);
                continue;
            }

            if (Vector2.Distance(position, blockedTargets[i].position) <= bodyRadius * 2.5f)
                return true;
        }

        return false;
    }

    private void CleanupBlockedMemory()
    {
        for (int i = blockedTargets.Count - 1; i >= 0; i--)
        {
            if (Time.time >= blockedTargets[i].expiresAt)
                blockedTargets.RemoveAt(i);
        }
    }

    private static Vector2 AngleToVector(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private static Vector2 Perpendicular(Vector2 v)
    {
        return new Vector2(-v.y, v.x);
    }

    private static Vector2 RotateVector(Vector2 v, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);

        return new Vector2(
            v.x * cos - v.y * sin,
            v.x * sin + v.y * cos
        );
    }

    private static float RandomRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max);
    }

    private static Vector2 LocalOffsetToWorld(Vector2 localOffset, Vector2 forward)
    {
        if (localOffset.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector2.right;

        forward.Normalize();
        Vector2 side = Perpendicular(forward);
        return side * localOffset.x + forward * localOffset.y;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sqrMagnitude = ab.sqrMagnitude;

        if (sqrMagnitude <= 0.0001f)
            return Vector2.Distance(point, a);

        float t = Vector2.Dot(point - a, ab) / sqrMagnitude;
        t = Mathf.Clamp01(t);

        Vector2 closest = a + ab * t;
        return Vector2.Distance(point, closest);
    }

    private void DrawDamageableHurtboxGizmo(Vector2 fallbackPosition)
    {
        if (damageableHurtbox == null)
        {
            Gizmos.DrawWireSphere(
                fallbackPosition + damageableHurtboxOffset,
                damageableHurtboxRadius
            );
            return;
        }

        if (damageableHurtbox is CircleCollider2D circle)
        {
            Vector2 center = circle.transform.TransformPoint(circle.offset);
            float radius = circle.radius * Mathf.Max(
                Mathf.Abs(circle.transform.lossyScale.x),
                Mathf.Abs(circle.transform.lossyScale.y)
            );
            Gizmos.DrawWireSphere(center, radius);
            return;
        }

        if (damageableHurtbox is CapsuleCollider2D capsule)
        {
            DrawCapsuleCollider2DGizmo(capsule);
            return;
        }

        Gizmos.DrawWireCube(
            damageableHurtbox.bounds.center,
            damageableHurtbox.bounds.size
        );
    }

    private static void DrawCapsuleCollider2DGizmo(CapsuleCollider2D capsule)
    {
        Vector2 center = capsule.transform.TransformPoint(capsule.offset);

        Vector2 scale = capsule.transform.lossyScale;
        float worldWidth = Mathf.Abs(capsule.size.x * scale.x);
        float worldHeight = Mathf.Abs(capsule.size.y * scale.y);

        if (capsule.direction == CapsuleDirection2D.Vertical)
        {
            float radius = worldWidth * 0.5f;
            float straight = Mathf.Max(0f, worldHeight - worldWidth);
            Vector2 top = center + Vector2.up * (straight * 0.5f);
            Vector2 bottom = center + Vector2.down * (straight * 0.5f);

            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);
            Gizmos.DrawLine(top + Vector2.left * radius, bottom + Vector2.left * radius);
            Gizmos.DrawLine(top + Vector2.right * radius, bottom + Vector2.right * radius);
        }
        else
        {
            float radius = worldHeight * 0.5f;
            float straight = Mathf.Max(0f, worldWidth - worldHeight);
            Vector2 left = center + Vector2.left * (straight * 0.5f);
            Vector2 right = center + Vector2.right * (straight * 0.5f);

            Gizmos.DrawWireSphere(left, radius);
            Gizmos.DrawWireSphere(right, radius);
            Gizmos.DrawLine(left + Vector2.up * radius, right + Vector2.up * radius);
            Gizmos.DrawLine(left + Vector2.down * radius, right + Vector2.down * radius);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        Vector2 pos = Application.isPlaying
            ? GetPosition()
            : (Vector2)transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pos, comfortMinDistance);
        Gizmos.DrawWireSphere(pos, comfortMaxDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pos, attackRange);

        if (drawDamageableHurtboxGizmo)
        {
            Gizmos.color = Color.green;
            DrawDamageableHurtboxGizmo(pos);
        }

        if (Application.isPlaying && hasCurrentTarget)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pos, currentTarget);
            Gizmos.DrawWireSphere(currentTarget, 0.18f);
        }

        if (Application.isPlaying && state == DogState.Lunge)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(pos, lungeEndTarget);
            Gizmos.DrawWireSphere(lungeEndTarget, 0.18f);

            if (drawActualLungeHitboxGizmo)
            {
                if (lungeDamageHitbox != null)
                    lungeDamageHitbox.DrawColliderGizmo();
                else
                {
                    Vector2 hitCenter = GetLungeHitboxCenter(pos, lungeDirection);
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(hitCenter, lungeHitRadius);
                }
            }
        }
        else if (Application.isPlaying && state == DogState.Telegraph && drawActualLungeHitboxGizmo)
        {
            Vector2 previewDirection = lungeDirection.sqrMagnitude > 0.0001f
                ? lungeDirection.normalized
                : Vector2.right;

            if (lungeDamageHitbox != null)
                lungeDamageHitbox.DrawColliderGizmo();
            else
            {
                Vector2 hitCenter = GetLungeHitboxCenter(pos, previewDirection);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(hitCenter, lungeHitRadius);
            }
        }

        if (Application.isPlaying && state == DogState.Retreat)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(pos, retreatTarget);
            Gizmos.DrawWireSphere(retreatTarget, 0.18f);
        }
    }
}

