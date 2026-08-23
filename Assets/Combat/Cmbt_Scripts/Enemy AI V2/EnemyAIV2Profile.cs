using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [CreateAssetMenu(
        fileName = "EnemyAI_V2_Profile_Default",
        menuName = "Combat/Enemy AI V2/Profile"
    )]
    public sealed class EnemyAIV2Profile : ScriptableObject
    {
        [Header("Director Tick")]
        [Min(0.02f)] public float directorTickSeconds = 0.10f;
        [Min(0.2f)] public float planMaximumSeconds = 4.5f;
        [Min(0.1f)] public float formationMaximumSeconds = 1.8f;
        [Min(0.1f)] public float reformDelaySeconds = 0.45f;

        [Header("Formation")]
        [Min(0.5f)] public float controllerRadius = 3.2f;
        [Min(0.5f)] public float flankerRadius = 3.5f;
        [Range(45f, 180f)] public float minimumFlankAngleDegrees = 105f;
        [Range(90f, 180f)] public float idealFlankAngleDegrees = 145f;
        [Min(0.25f)] public float hardDestinationSeparation = 1.5f;
        [Range(8, 32)] public int formationCandidateCount = 16;
        [Min(0.1f)] public float tacticalSlotArrivalRadius = 0.42f;
        [Min(0f)] public float candidatePathCostWeight = 0.18f;
        [Min(0f)] public float candidateAngleWeight = 2.2f;
        [Min(0f)] public float candidateLosBonus = 2.0f;
        [Min(0f)] public float candidateSeparationWeight = 1.4f;

        [Header("Stage 2 - Destination Leases / Anti-Clump")]
        [Tooltip("When enabled, tactical slots behave like leases. Other enemies avoid choosing or steering into those reserved destinations.")]
        public bool useDestinationLeases = true;

        [Tooltip("Candidate slots closer than this to another enemy's reserved slot are rejected.")]
        [Min(0.1f)] public float destinationLeaseRadius = 1.45f;

        [Tooltip("Agents steer away from other agents' assigned slots while moving, reducing clumps at arrival points.")]
        [Min(0.1f)] public float assignedSlotRepulsionRadius = 1.55f;

        [Tooltip("How strongly an agent avoids another agent's assigned slot while moving.")]
        [Range(0f, 1f)] public float assignedSlotRepulsionWeight = 0.32f;

        [Tooltip("If two or more living enemies remain this close for long enough, the director forces a re-form.")]
        [Min(0.1f)] public float clumpWatchRadius = 1.05f;

        [Tooltip("How long a clump has to persist before the director re-forms the squad.")]
        [Min(0.1f)] public float clumpWatchSeconds = 0.75f;

        [Tooltip("Cooldown before another automatic re-form can be triggered by the clump watchdog.")]
        [Min(0.1f)] public float clumpReformCooldown = 1.0f;

        [Header("Stage 2 - Contribution / Fail-Open")]
        [Tooltip("If a move fails during a tactic, the director quickly assigns an alternate useful action instead of letting the enemy idle.")]
        public bool failOpenWhenMovementFails = true;

        [Tooltip("Minimum time between director responses to the same agent's failure.")]
        [Min(0.05f)] public float failedActionResponseCooldown = 0.22f;

        [Tooltip("If a flanker has not reached the flank after this long during the controller phrase, it takes a useful shot instead of doing nothing.")]
        [Min(0.2f)] public float flankerFailOpenAttackSeconds = 1.65f;

        [Tooltip("Enemies can use a fail-open attack when they have line of sight or are within this range of the player.")]
        [Min(0.5f)] public float failOpenAttackDistance = 7.0f;

        [Tooltip("When a movement action fails, retry with an alternate destination before falling back to an attack.")]
        public bool retryAlternateSlotBeforeFailOpenAttack = true;

        [Tooltip("How far around the player the alternate-slot search rotates on each retry.")]
        [Range(10f, 90f)] public float alternateSlotAngleStepDegrees = 35f;

        [Header("Stage 4.1 - Attack Concurrency Toggle")]
        [Tooltip("Experiment toggle. When enabled, the director only allows one V2 enemy to begin an attack action at a time. Movement, repositioning, recovery, and holding lanes can still happen in parallel.")]
        public bool oneEnemyAttacksAtATime = false;

        [Tooltip("When enabled, FluidPressure counts as the active attack until the whole fluid action finishes. Turn this off if you want enemies to move-and-coast after firing while the next enemy may begin its own attack.")]
        public bool oneAtATimeCountsFluidPressureAsAttack = true;

        [Tooltip("Small safety gap after one attack action releases before the next enemy may begin attacking. Use 0 for immediate handoff, or 0.08-0.18 for a readable rhythm.")]
        [Min(0f)] public float oneAtATimeGapAfterAttack = 0.08f;

        [Tooltip("When one-at-a-time mode is enabled in 3-enemy squads, the Sentinel waits until after the Flanker attack instead of firing during the Controller phrase.")]
        public bool oneAtATimeSentinelAttacksAfterFlanker = true;



        [Header("Stage 4.2 - Pressure Pacing / Breathing Room")]
        [Tooltip("Keeps the fluid movement feel, but prevents the squad from beginning attacks back-to-back with no breathing room.")]
        public bool usePressurePacing = true;

        [Tooltip("Minimum time between V2 attack starts across the whole squad. Movement can still continue during this gap.")]
        [Min(0f)] public float minimumSecondsBetweenAttackStarts = 0.18f;

        [Tooltip("Adds a visible pause after an attack phrase has fully resolved before the squad immediately starts another phrase. Default OFF for tempo tuning; prefer slower movement/cadence over hard pauses.")]
        public bool usePhraseBreathingRoom = false;

        [Tooltip("Breathing room after a two-or-more enemy phrase. This should feel like a quick tactical reset, not dead air.")]
        [Min(0f)] public float phraseBreatherSeconds = 0f;

        [Tooltip("Breathing room after a solo enemy's attack phrase. Keep this shorter so solo duels remain active.")]
        [Min(0f)] public float soloPhraseBreatherSeconds = 0f;

        [Tooltip("Extra rest after a three-enemy Controller/Flanker/Sentinel phrase, because overlapping bullet-hell pressure is mentally heavier.")]
        [Min(0f)] public float extraBreatherAfterSentinelPhrase = 0f;

        [Tooltip("Multiplies recovery durations after attacks. Values above 1 add punish windows without removing the fluid movement layer.")]
        [Range(0.5f, 2.5f)] public float pressurePacingRecoveryMultiplier = 1.05f;

        [Tooltip("When enabled, the Director writes debug text explaining why an attack start or phrase restart is being held.")]
        public bool logPressurePacing = false;

        [Header("Stage 4.3 - Combat Tempo Scaling / No Hard Pauses")]
        [Tooltip("Slows the overall feel by scaling movement and attack cadence instead of pausing the squad between phrases. This keeps the flow, but makes it less frantic.")]
        public bool useCombatTempoScaling = true;

        [Tooltip("Multiplies V2 enemy movement speed. 0.85 keeps the fluid acceleration/deceleration feel but slightly slows repositioning and flank pressure.")]
        [Range(0.45f, 1.25f)] public float enemyMovementTempoMultiplier = 0.85f;

        [Tooltip("Multiplies intra-burst interval and burst cooldown. Values above 1 make bullet phrases slower and more readable without adding hard pauses.")]
        [Range(0.75f, 2.25f)] public float attackCadenceTempoMultiplier = 1.18f;

        [Tooltip("Multiplies normal recovery after attacks. This creates softer punish windows while enemies can still coast/reposition.")]
        [Range(0.75f, 2.25f)] public float recoveryTempoMultiplier = 1.10f;

        [Tooltip("Multiplies minimum attack-start spacing. This is a soft rhythm control; keep much lower than the old phrase breather values.")]
        [Range(0.5f, 2.5f)] public float attackStartGapTempoMultiplier = 1.0f;

        [Header("Stage 4.4 - Slow Orb / Slow Zone Respect")]
        [Tooltip("When enabled, EnemyLocomotionV2 applies movement-speed changes from spells and zones. Values below 1 slow the enemy; values above 1 speed it up.")]
        public bool respectSlowZones = true;

        [Tooltip("When an enemy first enters a slow zone, clamp high current velocity down toward the slowed speed so it cannot simply coast/run out at full speed.")]
        public bool slowZoneClampVelocity = true;

        [Tooltip("How quickly current velocity is braked down when entering or moving inside a slow zone. Higher values make the Slow Orb feel stickier without hard-rooting enemies.")]
        [Min(0.1f)] public float slowZoneEntryBrake = 70f;

        [Tooltip("Allows a little overshoot above the slowed target speed so movement still feels organic. Use 1.0 for strict clamping, 1.15 for a small natural carry.")]
        [Range(1f, 2.5f)] public float slowZoneVelocityOvershootAllowance = 1.10f;

        [Tooltip("Multiplies deceleration while slowed and coasting/recovering. This prevents V2's nice arcade momentum from letting enemies slide out of the Slow Orb too easily.")]
        [Range(1f, 6f)] public float slowZoneCoastBrakeMultiplier = 2.75f;

        [Tooltip("Writes slow-zone debug state into EnemyLocomotionV2. Keep off unless diagnosing Slow Orb behavior.")]
        public bool logSlowZoneEffects = false;

        [Header("Action Timing")]
        [Min(0.1f)] public float controllerAttackTimeout = 2.5f;
        [Min(0.1f)] public float flankerAttackTimeout = 2.5f;
        [Min(0f)] public float flankerAttackDelayAfterController = 0.22f;
        [Min(0.05f)] public float standardRecoverySeconds = 0.65f;
        [Min(0.05f)] public float soloRecoverySeconds = 0.55f;
        [Min(0.1f)] public float holdLaneMaximumSeconds = 0.75f;

        [Header("SkillSystemV2 Vertical Slice")]
        [Tooltip("Opt-in safety switch. When enabled, AI V2 may replace a scheduled legacy attack with an equipped SkillSystemV2 spell whose AI utility clears the threshold below.")]
        public bool enableSkillActions = false;

        [Tooltip("Minimum final utility required before a skill may replace the reliable legacy basic attack. Keep above one until each enemy spell's AI Guidance is tuned.")]
        [Min(0f)] public float minimumSkillUtility = 1.15f;

        [Tooltip("Extra time added after the spell's authored phase durations before the action watchdog treats the cast as stuck.")]
        [Min(0.1f)] public float skillCastTimeoutPadding = 1f;

        [Tooltip("When enabled, skills can replace a moving FluidPressure shot. The first vertical slice stops locomotion for the skill cast so authored build-up and recovery remain readable.")]
        public bool skillsMayReplaceFluidPressure = true;

        [Tooltip("Minimum real combat time between SkillSystemV2 cast starts across the squad. Ordinary attacks and movement remain available during this gap.")]
        [Min(0f)] public float minimumSecondsBetweenSkillStarts = 1.25f;

        [Tooltip("After a skill, require this many successful legacy attack actions before the same squad may select another skill. Zero permits consecutive skills.")]
        [Min(0)] public int minimumLegacyAttacksBetweenSkills = 1;

        [Tooltip("Hard safety cap on consecutive skill actions. One creates a readable skill/basic cadence even when a skill has very high utility.")]
        [Min(1)] public int maximumConsecutiveSkillActions = 1;

        [Header("SkillSystemV2 Threat Reactions")]
        [Tooltip("Allow configured EnemySpellThreatPerceptionV2 components to read hostile delivery geometry and issue bounded avoidance orders.")]
        public bool enableSpellThreatReactions = true;

        [Tooltip("Maximum enemies allowed to run a non-emergency threat reaction simultaneously. Zero means unlimited.")]
        [Min(0)] public int maximumConcurrentThreatReactions = 2;

        [Tooltip("Threat scores at or above this value may bypass squad reaction capacity and ordinary avoidance cooldowns.")]
        [Range(0f, 1f)] public float emergencyThreatScore = 0.9f;

        [Header("Controller Attack")]
        public string controllerPattern = "PetalFan";
        [Min(1)] public int controllerShotsPerBurst = 1;
        [Min(0.03f)] public float controllerIntraBurstInterval = 0.13f;
        [Min(0.05f)] public float controllerBurstCooldown = 0.80f;

        [Header("Flanker Attack")]
        public string flankerPattern = "ButterflySpread";
        [Min(1)] public int flankerShotsPerBurst = 1;
        [Min(0.03f)] public float flankerIntraBurstInterval = 0.11f;
        [Min(0.05f)] public float flankerBurstCooldown = 0.75f;

        [Header("Solo Duel Attack")]
        [Tooltip("Stage 2 safety fallback. Stage 3 role pools can override this when Role Pools Override Solo Aimed Safety is on.")]
        public bool forceSoloAimedPattern = true;

        [Tooltip("Used when Force Solo Aimed Pattern is on and Stage 3 role pools are not overriding it. AimedSingle is the clearest test pattern; AimedFan is also safe.")]
        public string soloAimedPattern = "AimedSingle";

        [Tooltip("Legacy/default solo pattern used when role attack pools are disabled.")]
        public string soloPattern = "AimedSingle";
        [Min(1)] public int soloShotsPerBurst = 1;
        [Min(0.03f)] public float soloIntraBurstInterval = 0.13f;
        [Min(0.05f)] public float soloBurstCooldown = 0.85f;
        [Min(0.5f)] public float soloPreferredRange = 3.1f;

        [Header("Stage 3 v3 - Role Attack Profiles Adapter")]
        [Tooltip("When enabled, V2 asks the existing EnemyRoleAttackProfiles asset for the attack identity before falling back to the built-in V2 attack pools. This makes V2 a decision engine while the role profile owns the exact bullet pattern.")]
        public bool useRoleAttackProfilesAsset = true;

        [Tooltip("Existing Project Eri RoleAttackProfiles_Default asset. Assign the same asset you used for the legacy EnemyBrain role attack profile pass.")]
        public EnemyRoleAttackProfiles roleAttackProfiles;

        [Tooltip("When on, the Role Attack Profiles asset is the first source of truth. If no asset is assigned or it cannot resolve a slot, V2 falls back to its built-in Stage 3 pools.")]
        public bool roleAttackProfilesTakePriority = true;

        [Tooltip("Role Attack Profiles can provide minimum shots and cadence multipliers. Keep this on so your profile asset controls feel without duplicating attack pools in V2.")]
        public bool useRoleAttackProfileCadence = true;

        [Tooltip("Role Attack Profiles can provide fan/ring/angular shape values. Keep this on so Controller, Flanker, and future Sentinel identity comes from the asset.")]
        public bool useRoleAttackProfileShape = true;

        [Tooltip("Solo duelists do not have an old legacy role. When this is on they use the Suppressor/Controller profile, which keeps solo shots readable but lets your profile tune the identity.")]
        public bool soloUsesSuppressorRoleProfile = true;

        [Tooltip("Default intensity used when a Controller asks the Role Attack Profile for an attack. 0=low, 1=medium, 2=high. Close range still uses the close slot.")]
        [Range(0, 2)] public int controllerProfileIntensity = 1;

        [Tooltip("Default intensity used when a Flanker asks the Role Attack Profile for an attack. Good side/rear angles may be promoted to High.")]
        [Range(0, 2)] public int flankerProfileIntensity = 1;

        [Tooltip("Default intensity used by solo duelists. Keep this lower until solo enemies have deeper behavior.")]
        [Range(0, 2)] public int soloProfileIntensity = 0;

        [Tooltip("When a Flanker has this much angle separation from the Controller, it asks for the High flanker slot instead of the default flanker slot.")]
        [Range(25f, 150f)] public float flankerHighProfileAngleDegrees = 88f;

        [Header("Stage 3 v4 - Touhou Pattern Usage")]
        [Tooltip("When enabled, V2 sends full pretty/Touhou pattern names such as PetalFan, ClosingBlossom, StaggeredRosette, RotatingFlowerRing, and ButterflySpread to EnemyShooterDebug first. If the installed shooter does not support that exact pattern, the executor falls back to the closest legacy shape.")]
        public bool preferTouhouPatternNamesWhenAvailable = true;

        [Tooltip("When enabled, V2 asks the Role Attack Profiles asset for medium/high slots more often so decorative bullet-hell patterns appear during normal play instead of only the safest aimed shots.")]
        public bool biasRoleAttackProfilesTowardTouhouSlots = true;

        [Tooltip("Minimum RoleAttackProfiles intensity for Controllers when not in close range. 0=low, 1=medium, 2=high. Use 1 or 2 for more Touhou-style pressure patterns.")]
        [Range(0, 2)] public int minimumControllerTouhouIntensity = 1;

        [Tooltip("Minimum RoleAttackProfiles intensity for Flankers when not in close range. Use 1 for ButterflySpread-style side pressure; use 2 if you want more high-pressure flank punishes.")]
        [Range(0, 2)] public int minimumFlankerTouhouIntensity = 1;

        [Tooltip("Minimum RoleAttackProfiles intensity for solo duelists when not in close range. Keep at 0 for very readable solos; raise to 1 for more bullet-hell flavor.")]
        [Range(0, 2)] public int minimumSoloTouhouIntensity = 1;

        [Tooltip("When enabled, V2 tries a different RoleAttackProfiles intensity if the chosen pattern would immediately repeat. This makes profile-driven enemies use more of the Touhou pattern set without random spaghetti in the brain.")]
        public bool rotateRoleProfileIntensityToAvoidRepeats = true;

        [Tooltip("Close range still prefers the RoleAttackProfiles close/safety slot instead of forcing high-intensity Touhou patterns at point blank.")]
        public bool keepCloseRangeSafetyPatterns = true;

        [Header("Stage 4 - Sentinel / Environment Lane Control")]
        [Tooltip("When three or more ranged enemies are alive, the director assigns one extra enemy as a Sentinel instead of leaving it unassigned. The Sentinel claims a useful lane/open area and fires Anchor-style denial patterns from RoleAttackProfiles_Default.")]
        public bool useSentinelRole = true;

        [Tooltip("Minimum living V2 enemies needed before a Sentinel is assigned. Keep at 3 for the first Stage 4 test.")]
        [Min(3)] public int minimumAgentsForSentinel = 3;

        [Tooltip("Distance from the player where the Sentinel tries to claim a controlling lane or open region.")]
        [Min(0.5f)] public float sentinelRadius = 4.15f;

        [Tooltip("How long after the Controller begins pressure before the Sentinel may begin its area-denial phrase.")]
        [Min(0f)] public float sentinelAttackDelayAfterController = 0.36f;

        [Tooltip("If enabled, the Sentinel may fire while still claiming its lane, preserving the fast fluid combat feel.")]
        public bool sentinelUsesFluidPressure = true;

        [Tooltip("If enabled, Sentinel slots score how many escape/approach lanes around the player they can see. This makes anchors appear to use the generated level more strategically.")]
        public bool sentinelScoresLaneCoverage = true;

        [Tooltip("Radius around the player used for lane-coverage probes. Larger values make the Sentinel prefer positions that see across more of the local arena.")]
        [Min(0.3f)] public float sentinelLaneProbeRadius = 2.25f;

        [Tooltip("How many points around the player are sampled to estimate whether a Sentinel position controls useful lanes.")]
        [Range(4, 16)] public int sentinelLaneProbeCount = 8;

        [Tooltip("Score bonus for each visible lane probe. Higher values make Sentinel slots more environment-aware, but can make them prefer strange long routes if too high.")]
        [Min(0f)] public float sentinelLaneCoverageBonus = 0.34f;

        [Tooltip("How strongly the Sentinel avoids duplicating the Controller or Flanker angle.")]
        [Min(0f)] public float sentinelAngleSeparationWeight = 1.6f;

        [Tooltip("Default RoleAttackProfiles intensity for the Sentinel/Anchor role. 1 = RotatingFlowerRing by default, 2 = HaloSpear high pressure.")]
        [Range(0, 2)] public int sentinelProfileIntensity = 1;

        [Tooltip("Minimum RoleAttackProfiles intensity for Sentinel when not in close range. Use 1 for more Touhou-like rings, 2 for stronger area denial.")]
        [Range(0, 2)] public int minimumSentinelTouhouIntensity = 1;

        [Tooltip("If enabled, a Sentinel that cannot keep line of sight to the player for its denial phrase forces a quick re-form instead of sitting in the back doing nothing.")]
        public bool sentinelReformsWhenLaneInvalid = true;

        [Header("Stage 3 - Role Attack Identity")]
        [Tooltip("Enables role-specific attack pools instead of one fixed pattern per role.")]
        public bool useRoleAttackPools = true;

        [Tooltip("When on, Solo Duelists use the Solo Attack Pool even if Force Solo Aimed Pattern is still enabled from Stage 2 testing.")]
        public bool rolePoolsOverrideSoloAimedSafety = true;

        [Tooltip("Stage 3 v2 safety: use the newest built-in Controller/Flanker/Solo attack pools even if an older profile asset still contains the old Stage 3 v1 arrays. Turn this off once you want to hand-tune the arrays directly.")]
        public bool useStage3V2DefaultRoleAttackPools = true;

        [Tooltip("Avoid choosing the exact same pattern twice in a row for the same enemy and same role when alternatives are valid.")]
        public bool avoidImmediatePatternRepeats = true;

        [Range(0f, 1f)] public float repeatedPatternWeightMultiplier = 0.22f;
        [Range(0f, 1.5f)] public float attackChoiceRandomness = 0.35f;

        [Tooltip("Distance at which emergency/close patterns become more attractive.")]
        [Min(0.2f)] public float closeRangeAttackDistance = 2.15f;

        [Header("Stage 3 v2 - Attack Phrase Tuning")]
        [Tooltip("When enabled, individual attack-pool options can override burst count, intra-burst timing, and cooldown. This makes roles feel distinct without needing brand-new projectile systems yet.")]
        public bool useAttackOptionBurstOverrides = true;

        [Tooltip("When enabled, individual attack-pool options configure EnemyShooterDebug shape values such as fan arc, fan bullet count, ring bullet count, and spiral/sweep angular speed.")]
        public bool useAttackOptionShapeOverrides = true;

        [Tooltip("When enabled, Flankers with a strong side/rear angle strongly prefer precise ambush shots instead of wide Controller-style pressure.")]
        public bool preferPrecisionFlankerAtGoodAngle = true;

        [Tooltip("Angle separation where a Flanker is considered to have earned a real side/rear attack identity.")]
        [Range(25f, 150f)] public float precisionFlankerAngleDegrees = 80f;

        [Tooltip("When enabled, Controllers avoid using the same broad pressure shape twice while a Flanker is active, making the squad phrase read more clearly.")]
        public bool separateControllerAndFlankerShapes = true;

        [Tooltip("Controller chooses attacks that shape movement and keep visible pressure.")]
        public EnemyAttackPatternOptionV2[] controllerAttackPool = new EnemyAttackPatternOptionV2[]
        {
            new EnemyAttackPatternOptionV2("Wide herding fan", "AimedFan", 3.2f, 0.7f, 8.5f, "wide pressure that shapes player movement")
            {
                overridePatternShape = true,
                fanBullets = 5,
                fanArcDegrees = 62f,
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.13f,
                burstCooldown = 0.66f
            },
            new EnemyAttackPatternOptionV2("Sweeping lane", "SweepFan", 2.7f, 1.7f, 8.5f, "moving lane pressure while allies rotate")
            {
                overridePatternShape = true,
                fanBullets = 4,
                fanArcDegrees = 46f,
                angularSpeedDegPerTick = 16f,
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.12f,
                burstCooldown = 0.78f
            },
            new EnemyAttackPatternOptionV2("Close ring check", "Ring", 1.55f, 0.0f, 3.0f, "deny close space around the Controller")
            {
                overridePatternShape = true,
                ringBullets = 8,
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.13f,
                burstCooldown = 0.86f
            }
        };

        [Tooltip("Flanker chooses attacks that exploit side/rear angles without duplicating Controller pressure.")]
        public EnemyAttackPatternOptionV2[] flankerAttackPool = new EnemyAttackPatternOptionV2[]
        {
            new EnemyAttackPatternOptionV2("Precise ambush shot", "AimedSingle", 3.4f, 0.0f, 9.0f, "precise side/rear punish")
            {
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.10f,
                burstCooldown = 0.58f
            },
            new EnemyAttackPatternOptionV2("Narrow side fan", "AimedFan", 2.0f, 1.2f, 7.5f, "compact side-angle spread")
            {
                overridePatternShape = true,
                fanBullets = 3,
                fanArcDegrees = 28f,
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.10f,
                burstCooldown = 0.64f
            },
            new EnemyAttackPatternOptionV2("Point-blank cross", "BoI_4Way", 1.6f, 0.0f, 2.7f, "close crossfire escape check")
            {
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.10f,
                burstCooldown = 0.78f
            }
        };

        [Tooltip("Solo Duelist uses a smaller, reliable duel kit. Keep mostly aimed patterns until more solo behavior is built.")]
        public EnemyAttackPatternOptionV2[] soloAttackPool = new EnemyAttackPatternOptionV2[]
        {
            new EnemyAttackPatternOptionV2("Clean aimed shot", "AimedSingle", 3.1f, 0.0f, 9.0f, "reliable aimed duel shot")
            {
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.12f,
                burstCooldown = 0.60f
            },
            new EnemyAttackPatternOptionV2("Short duel fan", "AimedFan", 1.9f, 1.0f, 6.5f, "small pressure spread")
            {
                overridePatternShape = true,
                fanBullets = 3,
                fanArcDegrees = 34f,
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.12f,
                burstCooldown = 0.68f
            },
            new EnemyAttackPatternOptionV2("Close escape check", "BoI_4Way", 1.25f, 0.0f, 2.5f, "close-range safety check")
            {
                overrideBurst = true,
                shotsPerBurst = 1,
                intraBurstInterval = 0.10f,
                burstCooldown = 0.76f
            }
        };

        [Header("Stage 3.5 - Fluid Combat Movement")]
        [Tooltip("When enabled, V2 does not wait for perfect tactical slots before creating pressure. Enemies can shoot while moving toward their tactical goal.")]
        public bool useFluidCombatMovement = true;

        [Tooltip("Solo enemies use a moving skirmish attack instead of move -> stop -> shoot. This is the main setting that makes one enemy feel less robotic.")]
        public bool soloUsesSkirmishPressure = true;

        [Tooltip("Controllers begin firing while still claiming their lane, keeping the fight from going dead during formation setup.")]
        public bool controllerPressuresWhileForming = true;

        [Tooltip("Flankers may take a useful side shot before reaching the exact perfect flank slot if their current angle is already good enough.")]
        public bool flankerCanFireBeforePerfectSlot = true;

        [Tooltip("How long the Controller is allowed to move before it starts pressuring even if it has not reached the exact slot.")]
        [Min(0f)] public float controllerStartPressureAfterSeconds = 0.22f;

        [Tooltip("How long the Flanker may route silently before it takes an opportunistic moving shot if the route is taking too long.")]
        [Min(0.1f)] public float flankerOpportunisticShotSeconds = 0.85f;

        [Tooltip("The angle separation needed before a Flanker may decide the current side angle is good enough to attack from while still moving.")]
        [Range(25f, 150f)] public float goodEnoughFlankAngleDegrees = 72f;

        [Tooltip("If an enemy is this close to its desired slot, it may treat the position as good enough for fluid pressure.")]
        [Min(0.1f)] public float goodEnoughSlotDistance = 1.15f;

        [Tooltip("Fluid pressure phrases have shorter recovery than the older stop-start phrases. Keep some recovery so the player still gets punish windows.")]
        [Range(0.25f, 1f)] public float fluidRecoveryMultiplier = 0.68f;

        [Tooltip("When enabled, the Director writes extra reason strings for Stage 3.5 fluid-pressure decisions.")]
        public bool logFluidCombatMovement = false;

        [Header("Stage 3.5.2 - Arcade Motion Feel")]
        [Tooltip("Acceleration used by EnemyLocomotionV2. Higher values feel snappier; lower values feel floatier. This is the main setting that restores the fast mixed-control feel without re-enabling mixed brains.")]
        [Min(0.1f)] public float motionAcceleration = 34f;

        [Tooltip("Deceleration used when an enemy clears its destination or enters recovery. Lower than acceleration gives a visible ease-out instead of a hard stop.")]
        [Min(0.1f)] public float motionDeceleration = 24f;

        [Tooltip("How strongly the velocity bends toward a new target. Lower values create smoother arcs; higher values snap harder toward the target.")]
        [Range(0.15f, 1f)] public float motionTurnSharpness = 0.62f;

        [Tooltip("Distance from the final destination where the enemy begins easing down so it does not overshoot tiny tactical slots.")]
        [Min(0.02f)] public float motionArrivalBrakeDistance = 0.55f;

        [Tooltip("Minimum time a FluidPressure action keeps movement alive after a quick shot starts/finishes. This prevents one-shot attacks from instantly snapping into recovery.")]
        [Min(0f)] public float minimumFluidMovementSeconds = 0.42f;

        [Tooltip("When enabled, FluidPressure can finish only after both the attack has resolved and the movement has had a short time to breathe.")]
        public bool fluidPressureRequiresMinimumMovementTime = true;

        [Tooltip("When enabled, the agent keeps its current velocity during recovery instead of instantly stopping. This creates the accelerate/decelerate feel seen in the mixed-control reference clip.")]
        public bool recoveryAllowsCoasting = true;


        [Header("Stage 2 v3 - Aim Reliability")]
        [Tooltip("Clears the shooter's target-sample buffer every time V2 starts an attack. This prevents stale aim samples after backend switches, telegraph cancels, or target refreshes.")]
        public bool resetShooterAimSamplesOnV2Attack = true;

        [Tooltip("For early V2 testing, use zero aim lag so a bad pattern or wrong target is obvious. Later, raise this to restore predictive/lagged aiming.")]
        [Min(0f)] public float v2AttackAimLagSeconds = 0f;

        [Header("Locomotion")]
        [Min(0.1f)] public float moveSpeed = 4.8f;
        [Min(0.05f)] public float pathRefreshSeconds = 0.28f;
        [Min(0.02f)] public float waypointArrivalRadius = 0.16f;
        [Min(0.1f)] public float progressTimeoutSeconds = 0.58f;
        [Min(0.01f)] public float progressRequiredDistance = 0.08f;
        [Min(0.05f)] public float separationRadius = 1.05f;
        [Range(0f, 1f)] public float separationWeight = 0.35f;
        [Min(0.05f)] public float recoverySidestepDistance = 0.55f;
        [Range(1, 5)] public int maximumMovementFailures = 3;
        [Min(0.1f)] public float failedDestinationMemorySeconds = 1.5f;

        [Header("Stage 2 - Locomotion Recovery")]
        [Tooltip("If true, the second recovery failure tries a wider opposite sidestep before declaring the move failed.")]
        public bool useWiderSecondSidestep = true;

        [Tooltip("Multiplier applied to Recovery Sidestep Distance on the second sidestep attempt.")]
        [Min(1f)] public float secondSidestepMultiplier = 1.65f;

        [Tooltip("When a path fails, try snapping the target to the nearest walkable cell before marking the destination failed.")]
        public bool retrySnappedDestinationBeforeFailing = true;

        [Header("Debug")]
        public bool drawFormationGizmos = true;
        public bool drawLeasesAndClumps = true;
        public bool logPlanChanges = false;
        public bool logActionFailures = true;
        public bool logStage2Watchdogs = false;
    }
}
