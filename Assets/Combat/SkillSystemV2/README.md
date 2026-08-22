# Skill System V2

This folder contains the caster-neutral foundation for Project Eri's replacement
skill system. It does not replace the current combat skill scripts yet.

## Included

- `CastContext` carries caster, team, aim, point, selected target, and chain data.
- `CombatTeamMember`, `CombatTarget`, and `TargetFilter` provide shared targeting
  rules for players, enemies, allies, and environmental objects.
- `SpellDefinition` composes timing, cooldown, resource cost, target rules, one
  delivery, and any number of effects.
- `SpellRunner` owns phase timing, cooldowns, costs, interruption, delivery
  lifecycle, and cast events.
- `SpellLoadout` gives any combatant a basic-attack slot and equipped skill list.
- `CastChainBudget` prevents recursive triggers from creating unlimited casts.
- Editor validation and EditMode tests cover the initial contracts.
- Player targeting assets build `CastContext` through immediate, direction,
  point/area, selected-target, two-point, or menu-selection aim flows.
- `PlayerSpellTargetingController` owns preview, confirmation, cancellation,
  and time slowdown without spending resources before confirmation.
- Deliveries cover self, instant target, area-at-point, lingering area,
  point-click-for-caster, melee arc, collision-safe 2D projectiles, trip wires,
  proximity mines, grenades, and ricocheting projectiles.
- Universal effect receiver contracts decouple spell assets from player,
  enemy, prop, and summon implementations.
- Delivery Effect Anchors let any effect opt into independent point, circle,
  arc, segment, or moving-delivery geometry without delivery/effect-specific
  synergy code.
- Effects now cover damage, damage over time, healing, impulse/pushback,
  caster movement, resources/AP, statuses, spawning, gameplay signals, and
  safely queued secondary spells.
- Reference `SpellVitality`, `SpellResourcePool`, `StatusController`, and
  `Rigidbody2DImpulseReceiver` components make V2 independently testable.

## Still deliberately deferred

- Beam, summon, and dedicated chain delivery archetypes.
- Adapters for `PartyManager`, `EnemyHealth`, movement, and existing prefabs.
- Enemy skill scoring and squad coordination.

These features should depend on the core assembly rather than adding
player- or enemy-specific behavior to `SpellRunner`.

## Unity setup

1. Create a spell from **Assets > Create > Project Eri > Skill System V2 >
   Spell Definition**.
2. Generate its stable ID in the custom inspector.
3. Assign a delivery asset and effect assets once their branches are merged.
4. Add `CombatTeamMember`, `CombatTarget`, `SpellLoadout`, and `SpellRunner` to a
   test combatant.
5. Use a player targeter or AI targeter to produce a `CastContext`, then call
   `SpellRunner.TryCast`.

Paid spells require a component implementing `ISpellResourceProvider`. Basic
attacks can use a zero-cost `SpellDefinition` in the loadout's Basic Attack slot.

## Targeting and delivery setup

1. Create a targeting asset from **Assets > Create > Project Eri > Skill System
   V2 > Targeting**. The asset controls range, aim slowdown, and preview shape.
   target, and immediate casts are stored under `Content/Targeting`.
2. Equip a reusable delivery module on the Spell Definition, then edit Player
   Targeting and all delivery values in its inline settings.
3. The spell inspector reports an
   error when the targeting asset cannot provide the delivery's required data.
4. Add `SpellRunner`, `PlayerSpellTargetingController`,
   `PlayerMouseTargetingInput2D`, and `TargetingTimeScaleController` to the
   player. `RequireComponent` adds the runner and time controller automatically.
5. Optionally add `TargetingPreviewRenderer2D` and assign a `LineRenderer` plus
   target marker. The renderer draws line, circle, cone, or selected-target
   previews from the targeting asset.
6. When the skill menu selects a spell, call
   `PlayerSpellTargetingController.BeginTargeting(spell, out failure)`. The
   mouse input component updates the aim and uses left click to confirm, right
   click or Escape to cancel.

Enemy AI does not use `PlayerSpellTargetingController`. It evaluates targets
and supplies a valid `CastContext` directly to `SpellRunner.TryCast`, so player
confirmation rules never slow or block enemy casts.

### Enemy skill vertical slice

The first opt-in integration keeps the existing Enemy AI V2 squad phrases and
lets a scheduled attack become a validated SkillSystemV2 cast:

1. Select an existing `EnemyAgentV2` object and run **Tools > Project Eri >
   Skill System V2 > Enemy AI > Configure Selected Enemy**.
2. Assign the enemy's skill assets under `SpellLoadout > Equipped Skills`.
3. On each intended spell, enable **Enemy AI Guidance > Usable By AI**, set its
   intent/range/base utility, and choose an optional Placement Intent.
4. Run **Validate Selected Enemy** from the same menu.
5. On the scene's `EnemyAIV2Profile`, enable **Skill Actions**. Existing profile
   assets default this off, so importing the feature cannot change combat by
   itself.

`EnemySpellTargetingSolverV2` currently handles self, selected-target,
direction, and one-point deliveries. Moving targets are led using authored
build-up plus projectile/grenade travel time, then every candidate still passes
through normal placement, line-of-sight, target, cooldown, and resource
validation. Two-point placement, navigation choke scoring, shared combo state,
and threat reactions are intentionally reserved for later milestones.

### Recommended starter combinations

- Quick Shot: Direction Targeting + Projectile Delivery.
- Slash: Direction Targeting with a cone preview + Melee Arc Delivery.
- Pushback: Direction Targeting with a cone preview + Melee Arc Delivery; the
  eventual pushback behavior belongs in an Effect Definition.
- Slow Orb: Point/Area Targeting + Projectile or Area Delivery.
- Dash: Point Targeting + Point Click Delivery + Caster Movement Effect.
- Self buff: Immediate Targeting + Self Delivery.
- Trip wire: Two Point Targeting + Trip Wire Delivery.
- Proximity mine: Point Targeting + Proximity Mine Delivery.
- Regular, sticky, or bouncy grenade: Point Targeting + Grenade Delivery.
- Deflectable bank shot: Direction Targeting + Ricocheting Projectile Delivery.
- Character buff/debuff: one of the Menu Select targeters + Instant Target
  Delivery. Recovery Duration controls how long the caster remains committed.

### Advanced player targeters

Two Point Targeting confirms one endpoint, keeps aim time slowed, then confirms
the second endpoint. It rejects a connection blocked by its Obstruction Mask
and writes both points into `SpellTargetingPayload`. Enemy AI creates the same
two-point payload directly, without using the player confirmation controller.

Menu Select Targeting uses a player-only roster window while still producing a
normal selected-target `CastContext`. The setup tool creates three reusable
variants: every party member (including defeated members for revival), living
party members, and living spawned enemies. The delivery and effects remain
caster-neutral, so enemies can use those same spells with AI-selected targets.

## Effects setup

1. Store one reusable module for each effect behavior under `Content/Effects`.
2. Add any number of effect modules to a `SpellDefinition` and edit their
   values inline. Deliveries decide
   which objects receive them; effects only ask those objects for capabilities
   such as `ISpellDamageReceiver` or `ISpellResourceReceiver`.
3. For standalone V2 prototypes, add `SpellVitality`, `SpellResourcePool`,
   `StatusController`, or `Rigidbody2DImpulseReceiver` to targets. The later
   integration branch will adapt the existing `EnemyHealth` and `PartyManager`
   systems instead of duplicating their state.
4. All authored SkillSystemV2 assets live under `Content`; supporting damage
   types and resource definitions live under `Content/Definitions`.

Statuses can compose effects when applied, at a periodic interval, and when
removed. This supports data-authored poison, regeneration, delayed bursts, and
similar behaviors without adding code to `SpellRunner`.

`DamageOverTimeEffectDefinition` applies reusable timed damage with inline
tick damage, interval, duration, Damage Type, initial-tick, and stacking
settings. It can be delivered by any delivery archetype.

`CasterMovementEffectDefinition` turns the Point Click delivery into a dash,
blink, or teleport. Each spell independently configures maximum distance,
travel speed, instant movement, line-of-sight requirements, and obstruction
layers. Its cast-context constraint clamps the targeting preview and rejects a
blocked destination before resources are spent.

Reaction slots let persistent deliveries respond to contacts from every
delivery through one normalized interaction service. An empty filter accepts
everything; optional conditions narrow by relationship, team, spell, category,
delivery, contact phase, effect, or Damage Type. Ordered response modules can
activate or deactivate a delivery, pulse its effects on occupants, and destroy
the receiving delivery after the sequence completes.

Reactive Effect Groups are optional named effect sets inside a Spell
Definition. Each group has its own inline effect settings, initial active state,
and either inherits the spell's target rules or supplies narrower rules of its
own. Reactions can enable or disable a group without changing the delivery.
Once enabled on a Lingering Area, the group affects both current occupants and
future valid entrants at the normal application interval. This separates an
area's persistent behavior from new behavior unlocked by an interaction—for
example, a Slow Orb can always slow everything inside but enable enemy-only
Damage Over Time after a projectile hits it.

## Delivery Effect Anchors

Every Default Effect and Event Effect Recipe slot has an **Apply Through**
choice. **Delivered Targets** preserves the delivery's normal behavior.
**Delivery Anchor** creates a separate geometry-aware lifetime when the chosen
delivery event occurs. Existing spell assets remain Delivered Targets unless a
designer explicitly opts in.

Anchors use the delivery's real shape and size: Self/Direct/Point Click expose
points, Area and persistent objects expose circles, Melee and cone shots expose
arcs, beams and trip wires expose segments, and projectile/grenade/ricochet
objects expose moving circles. An optional size override changes the radius,
arc range, or segment half-width without changing the delivery itself.

Application modes are generic rather than effect-specific:

- **On Enter** applies once as each valid object enters, with an optional
  leave-and-reenter reset.
- **Periodic** reapplies to current occupants at an independent interval.
- **While Present** applies/removes exact source-owned state for effects that
  implement `IAreaPresenceEffectDefinition`, including Spatial Force, Stat
  Modifier, and Movement Speed Change. Other effects safely fall back to On
  Enter instead of deleting state they do not own.
- **Once At Anchor** runs one effect that supports a world point, such as Spawn,
  Gameplay Signal, Trigger Spell, or Caster Movement.

Multiplicity may be once per root cast, once per delivery runtime, or once for
every event occurrence. The anchor owns its duration even after an
instantaneous delivery completes or a projectile/grenade object is destroyed;
a moving anchor follows its source and freezes at the last valid position if
that source disappears. Target Rules and layer masks still decide what it may
acquire.

This is the compatibility bridge for combinations such as a persistent gravity
grenade, vacuum melee arc, moving projectile aura, mine healing field,
trip-wire slow corridor, point reflect zone, and caster-centered force or
parry field. They all use the same acquisition, geometry, lifetime, and
presence contracts; no pairing is hard coded.

## Relocate Actor destination targeting

Relocate Actor's **Aimed Point** destination uses a secondary destination
delivery. The normal delivery first selects the actor that receives the effect;
the destination delivery then runs independently to determine where that actor
moves. `Delivery_PointClick` resolves immediately, while Projectile, Grenade,
and Ricochet deliveries move normally and relocate the actor where they stop.
Areas resolve when created, and mines or trip wires resolve when armed.

The destination delivery keeps its targeting, preview, visuals, motion,
collision, and timing. Its normal effects, Event Effect Recipes, and delivery
reactions are suppressed so the spell cannot apply twice. The final
`CastContext` retains both the original selected actor and the complete
supplemental targeting context. Enemy AI can provide the same data through
`CastContext.WithSupplementalTargeting`.

Projectile deliveries using the **Cone** hit shape render a short-lived
world-space outline matching the configured Range and Cone Angle. A 360-degree
cone renders as a circle. Cone hits remain instantaneous and do not instantiate
the moving Projectile Prefab.

Grenade throw distance comes from the selected **Player Targeting** asset's
Maximum Range, with the spell's Placement Rules able to make that range stricter.
Grenade Speed is only travel rate, and the fuse determines whether it has enough
time to reach the chosen point. The spell Inspector displays all three values
together and warns when a short-range targeting asset, such as a melee aim,
limits an otherwise fast grenade to a very short throw.

Spatial Force can move actor rigidbodies and projectile motion owners. Legacy
player/enemy projectiles, moving Skill V2 projectiles, ricochet projectiles,
and grenades keep their normal forward motion while the force adds a separate
inward or outward displacement. Area Hit Masks and Target Rules must include
the projectile's detected layer; the authored Black Hole includes both
`Projectile` and `PlayerProjectile` and uses the `Any` relationship.

Enable **Use Spatial Curve** for gravity-style motion. In this mode Strength is
acceleration rather than an imposed velocity, so an object's entry direction
and speed remain part of its trajectory. Gravity increases toward the center
using `Gravity Exponent` (2 is inverse-square-like) and is stabilized by
`Gravity Softening Distance`. `Maximum Speed` caps only the momentum contributed
by the field. With **Preserve Curve Momentum After Exit** enabled, objects retain
their bent trajectory after escaping the field. Projectile motion retains the
added curve velocity alongside its normal flight, while actor momentum is
returned to `Rigidbody2D` physics when the field releases control. The authored
Black Hole enables this mode while the reusable Spatial Force effect keeps it
off by default for backward compatibility.

Reflect Projectile exposes a per-spell **Motion Mode**. **Reverse Direction**
keeps the traditional parry behavior and is the backward-compatible default.
**Preserve Velocity (Allegiance Only)** changes an enemy projectile to the
player team, updates its owner/layer/collision rules and reflected-damage
behavior, but does not rotate it, negate its stored direction, or overwrite its
live Rigidbody velocity. The projectile therefore continues along the path it
already had while becoming friendly.

Delivery Anchor filters govern admission, not the continued lifetime of an
exact **While Present** effect. Once Spatial Force, Stat Modifier, or Movement
Speed owns presence on a target, changing that target's team or layer does not
remove the effect. It remains until the target physically leaves the anchor or
the anchor expires. This lets allegiance-only reflection coexist with a curved
gravity trajectory even though reflection changes `Projectile` to the
`PlayerProjectile` layer.

## Event Effect Recipes

Default Effects remain the simple path: the delivery applies them at its normal
targeting moment. Event Effect Recipes are the optional composition layer for
spells that need more than that. Every recipe reads as:

**WHEN a delivery moment happens → ONLY IF the involved object matches → APPLY
these effects TO this recipient.**

Supported moments currently include cast start, delivery start, chosen-point
arrival, target hit, blocking hit, delivery stop, area creation, area pulse,
area entry, area exit, expiration, arming, trip-wire crossing, mine proximity,
timer expiration, bouncing, sticking, deflection, detonation, and a manually
invoked delivery reaction. A recipe can apply its effects to the involved object, original
caster, originally selected target, or a world point with no object recipient.
Each recipe carries its own inline effect settings and optional subject rules.

This creates combinations without adding spell-specific scripts:

- Projectile stops → move the caster to the impact point.
- Projectile hits → spawn an object at the hit point.
- Projectile stops → trigger a secondary spell whose delivery creates an area.
- Object enters an area → apply a status or grant a resource.
- Area expires → spawn a final burst at its world position.
- Filtered delivery reaction → run a Manual Reaction recipe on the receiving
  caster, source caster, or contact point.

`SpellEffectContext` now includes the event type, event subject, event point,
normal, and runtime delivery component. New effect modules can therefore use
the same recipes without requiring changes to every delivery. Deliveries only
report standardized moments; effects decide what those moments mean.

`CasterMovementEffectDefinition` supports both Aimed Point and Delivery Event
Point destinations. Event-point movement can automatically offset the caster
by its collider footprint and an extra clearance value, making projectile
teleports land outside enemies and walls rather than inside them.

Event recipes are caster-neutral. Player targeting still builds a CastContext
through the confirmation UI; enemy AI supplies the same CastContext directly.
Everything after that—including impact movement, spawned areas, reactions, and
secondary spells—runs identically for both.

`SpawnEffectDefinition` initializes every `ISpellSpawnReceiver` on its prefab.
For example, an AP-collecting orb can use a trigger collider plus
`ResourceCollectorZone2D`; loose pickups use `SpellResourcePickup`. The later
legacy adapter only needs to expose existing AP particles through the pickup
contract.

`GameplaySignalEffectDefinition` is the escape hatch for game-wide or unusual
interactions. A system subscribes to a `GameplaySignalDefinition` asset and
receives the complete spell, caster, target, hit, label, and numeric payload.
This avoids adding one-off dependencies to the core spell executor.

`TriggerSpellEffectDefinition` creates a child `CastContext` and queues the
secondary spell on its chosen runner. The original root cast budget and depth
limits remain active, preventing recursive spell combinations from running
forever.
