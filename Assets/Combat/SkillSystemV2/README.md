# Skill System V2 Core

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

## Deliberately deferred

- Player targeting previews and time-slow confirmation.
- Projectile, melee, area, placement, beam, and movement deliveries.
- Damage, healing, pushback, status, and AP effect definitions.
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
