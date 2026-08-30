using System;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    public enum EnemyAIV2BackendMode
    {
        ObserveOnly = 0,
        Active = 1
    }

    public enum EnemyRoleV2
    {
        Unassigned = 0,
        SoloDuelist = 1,
        Controller = 2,
        Flanker = 3,
        Sentinel = 4,
        Harrier = 5,
        Opportunist = 6
    }

    public enum EnemySectorV2
    {
        None = 0,
        Front = 1,
        FrontLeft = 2,
        Left = 3,
        RearLeft = 4,
        Rear = 5,
        RearRight = 6,
        Right = 7,
        FrontRight = 8
    }

    public enum SquadTacticV2
    {
        None = 0,
        SoloDuel = 1,
        PinAndPincer = 2,
        ReForm = 3,

        // Stage 4: three-enemy formation. A Controller pins, a Flanker
        // rotates, and a Sentinel/Anchor claims a lane or open region.
        PinPincerSentinel = 4
    }

    public enum EnemyActionKindV2
    {
        None = 0,
        MoveToSlot = 1,
        HoldLane = 2,
        AttackPattern = 3,
        Recover = 4,
        Guard = 5,

        // Stage 3.5: one action that keeps locomotion and attack pressure
        // active at the same time. This is the intentional version of the
        // fast V1/V2 mixed-control feeling, without two brains fighting.
        FluidPressure = 6,

        // Generic SkillSystemV2 cast. The order carries a fully validated
        // CastContext, so execution never needs the player's targeting UI.
        CastSkill = 7,

        // Short, obstacle-aware movement order issued by generic spell threat
        // perception. Keeping it distinct makes reactions visible in runtime
        // diagnostics without introducing a second movement controller.
        EvadeThreat = 8,

        // Protected move-then-cast sequence for short-range actor-targeted
        // skills such as an ally heal, melee buff, or close-range debuff.
        ApproachAndCastSkill = 9,

        // Protected pause requested by an allied support caster. This is a
        // squad-coordination action, not spell-name-specific healing logic.
        HoldForSupport = 10
    }

    public enum EnemyActionStatusV2
    {
        Idle = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4
    }



    [Serializable]
    public sealed class EnemyAttackPatternOptionV2
    {
        [Tooltip("Friendly inspector label. This is only for designers/debugging.")]
        public string label = "Attack";

        [Tooltip("EnemyShooterDebug pattern name. Current compatible names include AimedSingle, AimedFan, Ring, Spiral, BoI_4Way, BoI_8Way, and SweepFan. Common pretty-pattern aliases are translated by the director.")]
        public string patternName = "AimedSingle";

        [Min(0f)] public float weight = 1f;
        [Min(0f)] public float minDistance = 0f;
        [Min(0.1f)] public float maxDistance = 99f;

        [Tooltip("Additional text appended to attack-selection debug when this option is chosen.")]
        public string intent = "General pressure";

        [Header("Stage 3 v2 - Phrase Overrides")]
        [Tooltip("When enabled, this option can override the role's default burst timing. Use this to make Controller, Flanker, and Solo attacks feel meaningfully different even when they share a supported shooter pattern.")]
        public bool overrideBurst = false;

        [Min(1)] public int shotsPerBurst = 1;
        [Min(0.03f)] public float intraBurstInterval = 0.12f;
        [Min(0.05f)] public float burstCooldown = 0.75f;

        [Tooltip("When enabled, this option configures the underlying EnemyShooterDebug shape parameters before firing. This is what lets one AimedFan be a wide Controller herd and another AimedFan be a narrow Flanker punish.")]
        public bool overridePatternShape = false;

        [Min(1)] public int fanBullets = 5;
        [Range(0f, 180f)] public float fanArcDegrees = 40f;
        [Min(3)] public int ringBullets = 8;
        [Min(0f)] public float angularSpeedDegPerTick = 12f;

        public EnemyAttackPatternOptionV2() { }

        public EnemyAttackPatternOptionV2(
            string label,
            string patternName,
            float weight,
            float minDistance,
            float maxDistance,
            string intent)
        {
            this.label = label;
            this.patternName = patternName;
            this.weight = weight;
            this.minDistance = minDistance;
            this.maxDistance = maxDistance;
            this.intent = intent;
        }
    }

    [Serializable]
    public sealed class EnemyActionOrderV2
    {
        public int orderId;
        public EnemyActionKindV2 kind;
        public Vector2 targetPosition;
        public float arrivalRadius = 0.35f;
        public float timeoutSeconds = 2f;
        public float durationSeconds = 0.5f;
        public string patternName = "AimedSingle";
        public int shotsPerBurst = 1;
        public float intraBurstInterval = 0.12f;
        public float burstCooldown = 0.8f;
        public bool resetShooterAimSamples = true;
        public float aimLagSeconds = 0f;

        // Stage 3 v2: optional shape configuration passed through to EnemyShooterDebug.
        public bool overridePatternShape = false;
        public int fanBullets = 5;
        public float fanArcDegrees = 40f;
        public int ringBullets = 8;
        public float angularSpeedDegPerTick = 12f;

        [NonSerialized] public SpellDefinition skillSpell;
        [NonSerialized] public CastContext skillCast;
        [NonSerialized] public SpellAIComboReservation comboReservation;
        [NonSerialized] public GameObject skillApproachTarget;
        [NonSerialized] public GameObject supportHoldOwner;
        [NonSerialized] public int threatId;
        [NonSerialized] public float threatScore;
        [NonSerialized] public float threatTimeToImpact;
        [NonSerialized] public bool threatIsInside;
        public float skillApproachRange = 1.4f;
        public float skillCastTimeoutSeconds = 2f;

        public string reason = "None";

        public EnemyActionOrderV2 Clone()
        {
            return (EnemyActionOrderV2)MemberwiseClone();
        }
    }
}
