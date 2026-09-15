# Project Eri — turn combat concept test

Prototype branch: `prototype/eri-turn-combat`. Original baseline: `06f014e` on
`SkillV2/CharacterBuildTesting`. No authored scenes, prefabs, character definitions,
or spell assets are replaced. The existing player spell bridge creates this runtime
test kit; the original authored loadouts remain in the project.

## Play

Start the existing Bootstrap scene and enter a normal encounter. Use the existing
movement and basic-attack controls. **C** switches character; **Tab** opens the
paused command menu; **W/S or arrows** selects; **Space** confirms; **Esc** backs out.
For offensive skills, press Space in the timing window, then aim and confirm with
Space or left click. Cancellation before the final cast costs nothing.

The potion button restores 30 MP and uses the shared command opportunity. The
party has three potions per rest. Outside combat, **F8** explicitly performs a
prototype rest (HP, MP, and potion stock). This is a testing convenience, not a
final checkpoint or expedition-economy design.

## Rules

- Four segments divide each character's actual maximum AP, including 150 AP.
  Basic attacks retain the game's existing AP gain. No passive AP regeneration.
- A skill consumes charge and exhausts its segments. Exhausted capacity cannot
  refill during that active stint. Each successful command by another living
  character restores one reserve segment, empty. Switching starts with zero AP.
- Every ordinary skill costs MP. MP persists across encounter scene transitions
  within the play session. It does not have save-file persistence in this prototype.
- One shared, fixed **4-second** recovery follows a command. Switching cannot
  bypass it. Paused menus and timing games stop the recovery clock.
- **Recover** is the explicit exception: no AP/MP cost, one shared command,
  restores one exhausted segment without charging it. Works for a lone survivor.
- A good timing input awards 5 AP after acceptance; perfect awards 10. Bonuses
  respect remaining capacity. Cancelling cannot farm the bonus.
- Call Eri preserves the original healing request, ally eligibility, refusal,
  healing points, and companion behavior. Accepted requests cost 1 segment and
  5 MP. Rejected requests cost nothing. It uses the existing ally selector.
- New encounters reset AP/exhaustion, not MP. Player callbacks bypass individual
  skill cooldowns; enemy runners retain their original behavior.

## Temporary test kit

| Character | Commands besides Slash and Recover |
|---|---|
| Dominic | Dread Field (1 segment/6 MP), Dread Pulse (2/10) |
| Imogen | Gather (2/8), Black Hole (4/18) |
| Audrey, Phil | Piercing Dread (2/10), Fear Shot (1/5) |

Slash costs 1 segment/3 MP. Dread Field marks entrants once each; the field lasts
10 seconds and marks last 16 seconds. Gather/Black Hole pull enemies together.
Fear damage consumes a mark for a 1.75x multiplier, combined with the labeled test
enemy's natural Fear modifier (weak 1.25x, resistant 0.75x, neutral 1x). Character
affinities remain neutral pending authored decisions. Test enemy affinities are
assigned by runtime instance order, not saved enemy identity.

## Implementation and limits

`EriTurnRules` owns numeric economy constants and integer segment boundaries.
`EriTurnCombat` owns shared readiness and runtime kits; `EriPrototypeDelivery`
contains the small Fear test effects. `EriTurnHUD` creates the resource display and
hides legacy combat resource graphics during play. The existing menu receives a
new runtime panel. No files or saved hierarchy objects are deleted for cleanup.

This is a combat-flow experiment, not production content: one emotional affinity,
one timing minigame, instantaneous test attacks, and simple local pulls. The test
deliveries do not yet enforce obstacle line-of-sight and do not implement the full
authored V2 reaction/AI-affordance ecosystem. Existing enemy AI, movement, attacks,
scene flow, and Eri companion are retained. Costs, durations, and damage are
provisional. Balance across long expeditions remains a manual playtest question.

## Verification

Tools > Project Eri > Turn Prototype > Run Rule Checks checks segment arithmetic
and resource gates. Play Mode Smoke Test (Ctrl+Alt+F9), from a saved Bootstrap
scene outside Play Mode, verifies the real pawn/menu/resource integration and
returns to Edit Mode without saving scenes. It temporarily changes runtime state
and disables enemies during the test; those changes end with Play Mode.

The smoke check includes paused selection, timed-game handoff, free cancellation,
single spending, fixed recovery, switching, reserve recovery, potions, zero-MP
lone-survivor recovery, mark consumption, and MP across a new encounter.
