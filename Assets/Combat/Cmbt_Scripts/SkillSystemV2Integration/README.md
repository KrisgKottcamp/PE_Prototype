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
`Assets/Combat/SkillSystemV2/Content`. It is safe to run
again: it refreshes the starter values without duplicating assets.

The same refresh also creates reusable `Delivery_TripWire`,
`Delivery_ProximityMine`, `Delivery_Grenade`, and
`Delivery_RicochetProjectile` modules plus Two Point and all three Menu Select
targeters. It does not add example spells for them to every character; equip a
module on a new Spell Definition and tune its inline values there.

Authored content is organized by responsibility:

- `Spells` contains complete castable Spell Definitions.
- `Deliveries` contains reusable delivery behavior assets.
- `Effects` contains reusable effect behavior modules.
- `Targeting` contains player targeting definitions.
- `Definitions` contains supporting damage-type, resource, status, and signal
  assets that are not themselves selectable effects.

Use **Organize Existing Skill Assets** in the setup window, or **Tools >
Project Eri > Skill System V2 > Organize Skill Content Library**, to consolidate
assets previously stored under `PlayerIntegrationContent` and `Presets`. Unity
moves them through the Asset Database, preserving GUIDs and existing
references. The organizer never overwrites a same-named destination asset.

## Reusable effects and inline settings

Deliveries and Effects use the same module-plus-settings pattern. A Spell
Definition equips one reusable delivery behavior and edits its targeting,
speed, range, shape, collision, duration, and visuals inline. Multiple spells
can therefore share `Delivery_Projectile`, `Delivery_MeleeArc`, or another
delivery module without sharing their authored values or duplicating assets.

The generated Quick Shot uses the Projectile module. Slash and Pushback both
use the same Melee Arc module with different range, angle, hit mask, and player
targeting settings. Slow Orb uses the Lingering Area module. Dash uses the
Point Click module, which applies effects to the caster while retaining the
clicked destination in its cast context. Existing Spell
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
- Dash equips `Effect_CasterMovement`; its inline values make it a four-unit,
  line-of-sight-checked movement over time.
- Impact Teleport shares the Projectile delivery and Caster Movement effect.
  Its Event Effect Recipe teleports the caster to the enemy or wall where the
  projectile stops.
- **Reset to Defaults** restores one slot from the shared module without
  changing other spell slots.

## Delivery interactions and reactions

Every existing delivery emits normalized contacts through the shared delivery
interaction service. Projectiles report their traveled segment, melee arcs
report their sweep, instant areas report their circle, target/self deliveries
report a point, and lingering areas register persistent volumes.

Spell Definitions can add compact Reaction slots. Each reaction has an ordered
response list, allowing one contact to activate a delivery, pulse its effects,
and consume it in a single designer-authored sequence. An empty Activation Filter
matches every delivery. Optional conditions can filter by source relationship,
team, exact spell, spell category, delivery module, contact phase, effect
module, or Damage Type. Only added conditions appear in the Inspector.

The Oil Spill starter spell demonstrates the system. It creates a dormant
Lingering Area. When any delivery contacts it, Oil Spill activates, applies the
shared Damage Over Time effect to valid occupants, and consumes the spill while
the burn runtimes continue ticking. Add filter conditions when the Oil
Spill should respond only to a specific source. The same Damage Over Time
module can be equipped directly by projectile, melee, or area spells and has
per-spell tick damage, interval, duration, Damage Type, immediate-tick, and
stacking settings.

The Spell Definition Inspector presents each reaction as a guided workflow:

1. **WHEN** — add readable Trigger Rules for what can activate it.
2. **HOW OFTEN** — choose every contact, once per source delivery, or only once.
3. **THEN** — arrange Actions in their execution order.

Each card ends with a live plain-language summary. Empty actions, unassigned
rule values, unsupported delivery modules, zero-delay repeated contacts,
duplicate effect pulses, and a Destroy action placed before later actions are
called out directly in the Inspector. These are presentation and authoring
improvements only; existing reaction assets remain compatible.

## Reactive Effect Groups

A Reactive Effect Group is a named, initially active or inactive effect set
stored directly on a Spell Definition. Add one in **Composition > Reactive
Effect Groups**, equip reusable effects in it, and edit those effects' settings
inline. The group may use the spell's Target Rules or expose independent rules
when **Use Spell Target Rules** is disabled. In a Reaction's **THEN** section,
add **Enable or Disable Reactive Effect Group** and select the group by name.

The generated Slow Orb demonstrates the complete flow. Its base Movement Speed
Change effect is always active and is configured below `1` to slow. Its
**Projectile Burn** group starts inactive, targets
enemies only, and contains a configured Damage Over Time effect. A Reaction
requires the shared Projectile delivery and enables Projectile Burn only once,
applying it immediately to enemies already inside. The group then remains
active for the rest of that orb's lifetime, so later enemy entrants also begin
taking repeat damage. The player and projectiles remain valid for the base slow
but are excluded from the burn group's independent Target Rules.

## Event Effect Recipes

Use Event Effect Recipes when the delivery's normal effect moment is not enough.
The Inspector presents each recipe in four short sections:

1. **WHEN** — choose the delivery moment.
2. **ONLY IF** — optionally filter the object involved in that moment.
3. **APPLY TO** — choose the involved object, spell caster, selected target, or
   world point.
4. **EFFECTS** — add reusable effects and tune their values inline.

The generated sentence at the bottom describes the result in plain language.
The Inspector warns when the equipped delivery cannot report the selected
event, when an event has no object but the recipe requires one, when a selected
target will not exist, or when Caster Movement is configured in a place that
cannot supply its event destination.

The generated **Impact Teleport** is the reference setup:

- Delivery: Projectile, non-piercing, collision mask `Obstacles` and
  `EnemyHurtbox`.
- Default Effects: none.
- WHEN: Delivery Stops.
- ONLY IF: Require an Involved Object. This excludes a maximum-range miss.
- APPLY TO: The Spell Caster.
- EFFECTS: Caster Movement with Destination set to Delivery Event Point,
  Instantaneous enabled, and Keep Outside Hit Surface enabled.

To make it teleport at maximum range too, change ONLY IF to **No
Restrictions**. To make a projectile create a lingering area, use a
Target-Hit or Delivery-Stops recipe with `Effect_TriggerSpell`, then reference a
small secondary spell whose delivery is Lingering Area. Chain-budget safety
still applies.

Delivery Reactions can also run recipes. Create a recipe whose WHEN value is
**Started by a Delivery Reaction**, then add **Run Event Effect Recipe** under
the reaction's THEN actions. The recipe receives the contact point and source
caster, while the reaction retains its existing spell, delivery, team, effect,
Damage Type, and contact-phase filters.

## Point-click movement spells

`Delivery_PointClick` records a world destination and applies its effects to
the caster. `Effect_CasterMovement` consumes that destination. Its compact
inline settings show Maximum Distance and Instantaneous first; Speed appears
only for non-instant movement, and Obstruction Mask appears only when Require
Line Of Sight is enabled.

- Dash: Instantaneous off, a high Speed, and line of sight on.
- Blink: Instantaneous on and line of sight on.
- Teleport: Instantaneous on and line of sight off.

Maximum distance clamps the actual targeting point. A blocked line-of-sight
cast remains invalid during aiming and consumes no AP. Both player targeting
and future enemy AI use the same spell-level cast validation.

Existing Spell Definitions using the old effect list migrate automatically the
first time they are loaded. Their current effect-asset values are copied into
the new inline slots so gameplay remains unchanged.

## Trip wires, mines, grenades, and ricochets

- **Trip Wire** uses Two Point Targeting. The first click stores endpoint one;
  the second click confirms endpoint two only when the Obstruction Mask is
  clear. Crossing applies Default Effects and reports **Object Crosses the Trip
  Wire** for optional recipes. It is single-use by default.
- **Proximity Mine** uses Point Targeting. Arming Delay controls when it begins
  watching, Trigger Radius detects a valid object, Detonation Delay adds an
  optional warning window, and Effect Radius applies Default Effects. It is
  single-use by default but can rearm.
- **Grenade** uses Point Targeting. Its fuse starts when thrown. Regular stops
  at the first collision, Sticky follows the struck object, and Bouncy reflects
  up to Maximum Bounces while retaining the configured speed fraction.
- **Ricocheting Projectile** uses Direction Targeting. It reflects from
  blocking surfaces, optionally reflects from valid targets, and can transfer
  caster/team ownership when hit by any current player basic-attack shape.

These delivery modules report standardized events, use the spell's ordinary
Target Rules and Default Effects, and accept an AI-authored `CastContext`.
There is no player-input code inside the delivery runtime.

## Menu Select targeting

Choose one of the generated targeters in the delivery's **Player Targeting**
field:

- `Targeting_Menu_AllParty` shows the full roster, including defeated members.
- `Targeting_Menu_ActiveParty` shows living party members.
- `Targeting_Menu_ActiveEnemies` shows living enemies currently spawned.

During aim slowdown, W/S or Up/Down changes the highlighted row, Space or left
click confirms, and Escape or right click cancels. The menu shows current HP.
Use an Instant Target delivery for a direct buff or debuff and tune the Spell
Definition's **Recovery Duration** for the post-selection action lockout.

For manual setup, add `PlayerSpellV2Bridge` to the pawn and assign spells to
the automatically added `SpellLoadout`. The bridge now guarantees a runtime
targeting outline and a short cast-confirmation pulse even when no authored
VFX are assigned. A projectile with no visual prefab receives a small cyan
fallback sprite, so a prototype cast is never completely invisible.

## Character Definition loadouts

Each `CharacterDefinition` has a **Skill System V2 Loadout** section. Enable
**Use Skill System V2 Loadout**, optionally assign a V2 Basic Attack, and add
the character's equipped Spell Definitions in menu order. The shared combat
pawn's `CharacterDefinitionSpellLoadoutBinder` copies that authored loadout into
its runtime `SpellLoadout` whenever the active party member changes.

Characters that have not enabled the V2 loadout keep the pawn's original
manual loadout by default, preserving the existing migration fallback. The
Character Definition is the source of truth once its V2 toggle is enabled;
there is no need to maintain a separate loadout on each character prefab.

## Inspector hover help

Hover over Spell Definition fields, inline delivery and effect settings,
Recipe and Reaction controls, targeting assets, statuses, Damage Types, and
compact action buttons to see a short plain-English explanation. Effect picker
entries explain what each reusable effect does, while each effect card explains
that its inline values belong only to the current spell. Tooltip coverage is
checked by an EditMode test so newly added authoring fields cannot silently ship
without hover help.

No AP is spent when targeting starts. AP is validated and spent only when
the player confirms a valid cast. Cancelling therefore needs no refund.
Legacy skills remain the fallback whenever the V2 loadout is empty.

## First play-mode smoke test

1. Start from Bootstrap and enter combat normally.
2. Give the active character enough AP and open the existing skill menu.
3. Confirm that the seven V2 starter rows appear.
4. Start each spell, move the cursor, cancel once, then confirm once.
5. Verify cancellation spends no AP and confirmation spends AP exactly once.
6. Verify Quick Shot and Slash reduce `EnemyHealth`; Pushback moves either a
   Dynamic or Kinematic enemy body and reverses enemy projectiles into
   player-owned projectiles that damage enemies; and Slow Orb slows enemies,
   the player, and projectiles while they are inside the zone. Verify Dash
   moves toward its clamped point, stops at four units, and rejects paths
   blocked by the Obstacles layer.
7. Cast Oil Spill and confirm that it remains visually dormant until a
   projectile, slash, area, or another delivery contacts it; then verify its
   spill disappears and Damage Over Time continues ticking on occupants.
8. Hit Slow Orb with Quick Shot. Verify enemies currently inside and enemies
   entering later receive repeating damage, while the player does not receive
   that damage.
9. Fire Impact Teleport into an enemy and a wall. Verify the caster moves just
   outside the impacted surface. Verify a maximum-range miss does not teleport.
10. Enable V2 loadouts on two Character Definitions, equip different spells,
    swap characters, and verify the combat menu follows the active character.
11. Confirm all SkillSystemV2 EditMode tests pass.
12. Disable the Character Definition V2 toggle and verify the pawn's fallback
    loadout returns.

The generated Slow Orb creates a visible four-second lingering zone and
applies its movement-speed change only while targets remain inside. Crossing
the boundary adds or removes the modifier on the next frame, with no timed linger.
It recognizes the
`EnemyHurtbox`, `PlayerHurtbox`, `Projectile`, and `PlayerProjectile` layers.
The effect changes movement rate only; projectile lifetime, range, damage, and
effect potency remain unchanged. Values below `1` slow and values above `1`
speed up. A later delivery variant can add projectile travel before the zone
appears without changing the movement-speed effect.
