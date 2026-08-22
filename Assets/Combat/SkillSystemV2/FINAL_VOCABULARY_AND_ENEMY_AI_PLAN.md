# SkillSystemV2 Final Vocabulary and Enemy AI Plan

This pass completes broad building blocks rather than adding one script for
every imagined spell.

## Designer vocabulary now available

- **Delivery** decides how effects arrive: projectile, fan/shotgun, rapid fire,
  ring, beam, cone, homing shot, boomerang, area, mine, grenade, tripwire, or
  direct selection.
- **Effect** decides what happens: damage, healing, resource change, status,
  stat modifier, spatial pull/push, actor relocation, spawn, reflection, or a
  gameplay signal.
- **Placement Rules** add a per-spell maximum placement distance and optional
  obstacle line-of-sight check without duplicating targeting assets.
- **Recipes and Reactions** decide when effects happen and how deliveries
  interact.

Create a new code module only when a design needs a genuinely new verb. A new
shotgun, black hole, buff, blink, or combo normally needs only a new Spell
Definition assembled from the vocabulary above.

## Enemy use

Every Spell Definition now contains **Enemy AI Guidance**. It explicitly says:

- why the spell is useful (damage, control, mobility, defense, support, setup,
  execute, or escape);
- its useful range and minimum target count;
- its preferred target type;
- combo tags it creates and consumes;
- commitment risk; and
- reasonable opponent reactions and reaction urgency.

AI use starts disabled on existing and newly created spells so an unfinished
definition cannot enter an enemy loadout accidentally. Enable **Usable By AI**
after filling in the guidance for that spell.

`EnemySpellAIDecisionSupportV2` is the compatibility seam for the current AI.
It reads an enemy's `SpellLoadout`, builds the same `CastContext` used by the
player, rejects casts that fail placement, line-of-sight, cooldown, resource,
or target rules, then returns the highest-utility skill. The loadout's Basic
Attack remains separate, so the current attack-pattern system is never removed
or treated as a spell cooldown filler.

The first vertical slice now adds an opt-in `CastSkill` action,
`EnemySkillExecutorV2`, and `EnemySpellTargetingSolverV2`. Existing profiles
leave Skill Actions disabled. Once enabled, the current director may replace a
scheduled attack with an equipped AI-enabled spell whose validated utility
clears the profile threshold. Initial targeting supports self, actor,
directional, and single-point casts with bounded movement prediction.

The EnemySkillAI branch should connect it in this order:

1. The `SquadDirectorV2` continues assigning tactical roles and phrase slots.
2. A new Skill action asks `EnemySpellAIDecisionSupportV2` for candidates that
   fit that role and the current world snapshot.
3. Basic attack and skill candidates compete on utility, while the director
   reserves combo tags so allies can intentionally set up and consume them.
4. `EnemyActionRunnerV2` owns the selected action and calls `SpellRunner` once.
5. Failure returns control to the current recovery behavior instead of creating
   a second AI brain.

## Enemy reactions

The next AI pass should add one shared threat registry. Active V2 delivery
runtimes publish their spell, owner, shape, time-to-danger, and AI reaction
guidance. Each enemy converts nearby threats into a small set of choices it can
actually perform: continue, dodge, leave area, seek cover, interrupt, deflect,
spread out, or close distance. Reaction utility should include urgency,
telegraph time, current commitment, role, and whether an ally already reserved
the same escape lane.

This keeps reaction logic out of individual spell scripts: a new cone, mine,
black hole, or projectile automatically becomes readable once its delivery
publishes a threat shape and its Spell Definition describes sensible responses.

## Examples

- **Shotgun:** Projectile delivery; Fan emission; count 6; chosen spread; Damage.
- **Rapid fire:** Projectile delivery; count above 1; positive Shot Interval.
- **Black hole:** Lingering Area; Spatial Force toward Delivery Center; optional
  Damage Over Time or Movement Speed modifier.
- **Teleport an enemy on impact:** Projectile; Relocate Actor effect with Event
  Point destination on the Target Hit recipe.
- **Haste:** Direct Target delivery; Stat Modifier; Movement Speed; Multiply 1.25.
- **Damage resistance field:** Lingering Area; Stat Modifier; Damage Received;
  Multiply below 1. Presence removal is immediate on exit.
