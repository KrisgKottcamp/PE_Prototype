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
  point/area, or selected-target aim flows.
- `PlayerSpellTargetingController` owns preview, confirmation, cancellation,
  and time slowdown without spending resources before confirmation.
- Initial deliveries cover self, instant target, area-at-point, melee arc, and
  collision-safe 2D projectiles.
- Universal effect receiver contracts decouple spell assets from player,
  enemy, prop, and summon implementations.
- Effects now cover damage, healing, impulse/pushback, resources/AP, statuses,
  spawning, gameplay signals, and safely queued secondary spells.
- Reference `SpellVitality`, `SpellResourcePool`, `StatusController`, and
  `Rigidbody2DImpulseReceiver` components make V2 independently testable.

## Still deliberately deferred

- Beam, persistent-zone, summon, movement, and chain deliveries.
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
   Ready-to-tune presets for Quick Shot, Slash, Pushback, point/area, selected
   target, and immediate casts are included under `Presets/Targeting`.
2. Create a delivery asset from **Assets > Create > Project Eri > Skill System
   V2 > Delivery**, then assign the targeting asset under **Player Targeting**.
3. Assign the delivery to a `SpellDefinition`. The spell inspector reports an
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

### Recommended starter combinations

- Quick Shot: Direction Targeting + Projectile Delivery.
- Slash: Direction Targeting with a cone preview + Melee Arc Delivery.
- Pushback: Direction Targeting with a cone preview + Melee Arc Delivery; the
  eventual pushback behavior belongs in an Effect Definition.
- Slow Orb: Point/Area Targeting + Projectile or Area Delivery.
- Self buff: Immediate Targeting + Self Delivery.

## Effects setup

1. Create or duplicate an effect under **Assets > Create > Project Eri > Skill
   System V2 > Effects**.
2. Add any number of effect assets to a `SpellDefinition`. Deliveries decide
   which objects receive them; effects only ask those objects for capabilities
   such as `ISpellDamageReceiver` or `ISpellResourceReceiver`.
3. For standalone V2 prototypes, add `SpellVitality`, `SpellResourcePool`,
   `StatusController`, or `Rigidbody2DImpulseReceiver` to targets. The later
   integration branch will adapt the existing `EnemyHealth` and `PartyManager`
   systems instead of duplicating their state.
4. Starter Physical Damage, Healing, Pushback, and Gain AP effect assets are in
   `Presets/Effects`.

Statuses can compose effects when applied, at a periodic interval, and when
removed. This supports data-authored poison, regeneration, delayed bursts, and
similar behaviors without adding code to `SpellRunner`.

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
