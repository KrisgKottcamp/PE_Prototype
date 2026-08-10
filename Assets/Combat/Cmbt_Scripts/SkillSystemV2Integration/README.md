# SkillSystemV2 Player Integration

This folder deliberately sits outside `Assets/Combat/SkillSystemV2`.
It may reference both the V2 assembly and Project Eri's legacy
`Assembly-CSharp` types (`PartyManager`, `AimTracker`, and the combat menu).

## Scene setup

The quickest setup is **Tools > Project Eri > Skill System V2 > Player
Integration Setup**:

1. Drag the combat pawn prefab (or a spawned pawn in an open combat scene)
   into **Combat Pawn**.
2. Optionally assign a projectile prefab for its visuals. V2 sanitizes the
   spawned copy, disabling legacy gameplay scripts, Rigidbody simulation, and
   colliders while keeping its renderer and animation.
3. Click **Create Content + Configure Pawn**.
4. Save the prefab/scene when Unity prompts you.
5. Ensure the combat HUD's `CombatSkillMenuController` has **Prefer V2
   Loadout When Available** enabled (it is enabled by default).

The tool generates ordinary editable assets under
`Assets/Combat/SkillSystemV2/PlayerIntegrationContent`. It is safe to run
again: it refreshes the starter values without duplicating assets.

Authored content is organized by responsibility:

- `Spells` contains complete castable Spell Definitions.
- `Deliveries` contains reusable delivery behavior assets.
- `Effects` contains reusable effect behavior modules.
- `Targeting` contains player targeting definitions created for this content.

Use **Organize Existing Skill Assets** in the setup window, or **Tools >
Project Eri > Skill System V2 > Organize Player Integration Content**, to move
loose assets from the content root into these folders. Unity moves them through
the Asset Database, preserving GUIDs and existing references. The organizer
never overwrites a same-named asset already present at the destination.

## Reusable effects and inline settings

Deliveries and Effects use the same module-plus-settings pattern. A Spell
Definition equips one reusable delivery behavior and edits its targeting,
speed, range, shape, collision, duration, and visuals inline. Multiple spells
can therefore share `Delivery_Projectile`, `Delivery_MeleeArc`, or another
delivery module without sharing their authored values or duplicating assets.

The generated Quick Shot uses the Projectile module. Slash and Pushback both
use the same Melee Arc module with different range, angle, hit mask, and player
targeting settings. Slow Orb uses the Lingering Area module. Existing Spell
Definitions automatically copy their current delivery asset values into an
inline delivery slot once.

Spell Definitions now equip reusable effect modules through compact effect
slots. Expanding a slot shows only that effect's per-spell settings. Assigning
an effect automatically copies its module defaults into the spell; changing
those inline values does not affect any other spell using the same module.

The generated starter content demonstrates this directly:

- Quick Shot and Slash share `Effect_Damage`, with different damage amounts.
- Slash and Pushback share `Effect_Knockback`, with different magnitude and
  duration values.
- **Reset to Defaults** restores one slot from the shared module without
  changing other spell slots.

Existing Spell Definitions using the old effect list migrate automatically the
first time they are loaded. Their current effect-asset values are copied into
the new inline slots so gameplay remains unchanged.

For manual setup, add `PlayerSpellV2Bridge` to the pawn and assign spells to
the automatically added `SpellLoadout`. The bridge now guarantees a runtime
targeting outline and a short cast-confirmation pulse even when no authored
VFX are assigned. A projectile with no visual prefab receives a small cyan
fallback sprite, so a prototype cast is never completely invisible.

No AP is spent when targeting starts. AP is validated and spent only when
the player confirms a valid cast. Cancelling therefore needs no refund.
Legacy skills remain the fallback whenever the V2 loadout is empty.

## First play-mode smoke test

1. Start from Bootstrap and enter combat normally.
2. Give the active character enough AP and open the existing skill menu.
3. Confirm that the four V2 starter rows appear.
4. Start each spell, move the cursor, cancel once, then confirm once.
5. Verify cancellation spends no AP and confirmation spends AP exactly once.
6. Verify Quick Shot and Slash reduce `EnemyHealth`; Pushback moves either a
   Dynamic or Kinematic enemy body and reverses enemy projectiles into
   player-owned projectiles that damage enemies; and Slow Orb slows enemies,
   the player, and projectiles while they are inside the zone.
7. Confirm all 32 SkillSystemV2 EditMode tests pass.
8. Empty the `SpellLoadout` and verify the old PartyManager skill list returns.

The generated Slow Orb creates a visible four-second lingering zone and
applies its movement slow only while targets remain inside. Crossing the
boundary adds or removes the slow on the next frame, with no timed linger.
It recognizes the
`EnemyHurtbox`, `PlayerHurtbox`, `Projectile`, and `PlayerProjectile` layers.
The slow changes movement rate only; projectile lifetime, range, damage, and
effect potency remain unchanged. A later delivery variant can add projectile
travel before the zone appears without changing the movement-slow effect.
