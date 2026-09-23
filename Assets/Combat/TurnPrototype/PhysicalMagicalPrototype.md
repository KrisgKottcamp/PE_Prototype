# Physical / Magical Combat Prototype

This note describes the current, opt-in prototype contract. Values marked provisional are tuning defaults, not final balance. The implementation is a bridge around the existing turn/AP loop; it does not claim that gameplay has been tested.

## Defense rules

- Physical basic attacks deal **zero Armor/Shield damage**. They award AP on contact and damage Health only when both defenses are absent or the enemy is knocked down. A defense-blocked basic does not consume the stored Armor reward.

- Enemies have independent Armor and Shield bars. When the attack's matching defense exists, a skill damages that bar at 100% and the other at the shared `OffTypeMultiplier` (currently 35%). Defense damage never overflows into Health on the breaking hit.
- While either defense remains, physical and magical skills damage the available defense bars at their matching or off-type rates; Health remains protected. Fear applies to Shield and exposed Health, not Armor. A health-only enemy can knock down when crossing the configured threshold.
- Breaking either bar knocks the enemy down without a recovery timer. Two distinct player attacks are required to wake it: the first deals normal Health damage, the final adds 10% max Health and heavy hitstop, then restores full defenses if it survives. One cast cannot count twice. All-out attacks neither wake enemies nor advance this hit count.
- Armor and Shield breaks are separate events. Armor break arms the next attack's Armor reward multiplier; Shield break grants one free shield action. These rewards must not be conflated.
- Fear marks/temporary resistance affect the Shield-side elemental value and later Health damage, never Armor. Off-balance increases physical Armor damage. Oil Spill is neutral magical damage, not physical; Mark Shot applies a mark and deals no damage.
- A single skill execution uses one attack ID, so fan/projectile sub-hits cannot repeatedly cash in a break reward.

## Turn controls and rewards

- A normal command spends its authored segments and MP, ends the active character's turn, and follows the existing recovery/character rotation. Timing can save 1–2 MP and grades defense damage (normal/1.25x/1.5x).
- After a Shield break, the next regular skill is a free second action: it waives AP, MP, and recovery/cooldown consumption. It may target any valid target and still ends the active character's turn. Recover, an MP potion, and Call Eri are not free-shield skill commands and do not consume this bonus.
- One-turn party/self buffs decrement after a completed skill command, excluding the cast that granted the buff. `Motivate` grants two turns to the actor; `Inspire` grants one turn to living party members.
- All-Out becomes available only when every living enemy is knocked down. It applies the current rules asset's `100` Health and `40` defense damage (provisional tuning). Every survivor stands up with full defenses afterward, even if the sweep breaks its remaining bar. Fresh breaks still award their normal rewards. A repeat requires knocking every survivor down again. It never consumes Armor's attack reward.

## Editable setup

Create assets from the Unity asset menu and place them under a `Resources` folder with these exact names for automatic loading:

| Asset | Menu | Resources name | Main controls |
| --- | --- | --- | --- |
| Defense rules | `Project Eri/Defense Rules` | `EriDefenseRules` | off-type multiplier, knockdown damage bonus, health threshold, off-balance Armor multiplier, default Armor/Shield, Armor reward, test profiles, All-Out, and party-buff values |
| Character kit settings | `Project Eri/Prototype/Character Kit Settings` | `EriCharacterKitSettings` | per-skill damage, segments, MP, range/radius/duration, projectile/fan/grenade/dash/mark/oil timings |

The editor authoring hook creates `Assets/Combat/TurnPrototype/Resources/EriDefenseRules.asset` and `EriCharacterKitSettings.asset` when missing, and adds the defense UI to the scene. It preserves existing assets; UI additions are permanent scene changes. The rules asset is authoritative for mechanics defaults, test profiles, All-Out, and buffs. `EriTurnCombat` may reference `KitSettings` directly; otherwise it loads the Resources asset (or an in-memory fallback). All tuning defaults remain provisional.

## Prototype skill coverage

Enemies without an authored defense component receive repeating test profiles: Armor only, Shield only, both, neither. Add `EriEnemyDefenses` to an enemy prefab outside Play Mode to author its permanent profile instead. The Shield-only fallback can periodically raise a six-second Fear resistance during its normal attack preparation; Dispel removes it. This behavior and its strength/duration are editable on the enemy defense component.

Eri's short sweep pauses combat while its animation runs, then restores the previous game speed. This prevents projectiles or timers from changing the knockdown setup midway through resolution. The offer uses a separate mouse click, not the skill-selection confirm key.

The generated kits expose the following mechanics: physical Slash, Dash Slash, Whip Slash, and Grenade; fear/magical Shot, Snipe, Fan, Reflect, and Oil Spill (neutral magical); Mark Shot applies a mark without damage; support Motivate, Inspire, Heal Self, Cover, Silence, and Dispel; plus Recover. Dominic gets Whip Slash/Mark Shot/Dispel/Oil Spill, Imogen gets Reflect/Cover/Inspire/Silence, Phil gets Snipe/Fan/Grenade/Heal Self, and the fallback kit gets Slash/Dash Slash/Shot/Motivate.

Basic controls remain WASD movement/selection, Mouse 0 attack/confirm, C character switch, and Tab skill menu (Space confirms timing; Escape cancels).

## Verification checklist

Implementation verification: runtime and Editor assemblies compile with the project's Unity compiler. All 18 formula checks pass, including the corrected exact-20% boundary and basic-attack defense restrictions. Unity imported the initial implementation and saved the settings/UI. The isolated component checks are available under Tools > Project Eri > Check Defense State Transitions but their latest execution is not verified. No hands-on gameplay or visual QA is claimed; Windows UI activation timed out.

- [ ] Assign authored Armor/Shield profiles and confirm each bar initializes and refills only on recovery.
- [ ] Confirm physical/magical routing damages remaining defense bars (100% matching / 35% off-type) without reaching Health until both bars are gone. Check Fear does not multiply Armor damage, basics cannot hurt standing defended enemies, and no defense-to-Health overflow.
- [ ] Break Armor alone: next attack receives the Armor reward once; verify a multi-projectile/fan attack cannot multiply it.
- [ ] Break Shield alone: exactly one regular skill gets a free action; verify AP, MP, and recovery are waived and it is consumed once. Verify Recover, potion, and Call Eri are excluded.
- [ ] Break both bars in one skill: verify both reward channels remain distinct and both events report correctly.
- [ ] Confirm buffs decrement after a later completed skill, not on the granting cast; test both self and party buffs.
- [ ] Put every living enemy into knockdown, invoke All-Out, and verify survivors stand up with restored bars even when the sweep breaks the other bar. Verify a repeat requires fresh player knockdowns on every survivor.
- [ ] Exercise Recover, potion, defeated-character rotation, timing grades, and menu/cooldown guards.
- [ ] Run editor/unit smoke checks and then perform an explicit in-game playtest before treating any provisional number as approved; main-scene integration remains unverified.
