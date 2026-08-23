using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    public enum EnemyThreatReactionPresetV2
    {
        Reckless,
        Humanoid,
        Elite,
        Boss
    }

    public readonly struct EnemyThreatResponseBehaviorV2
    {
        public float HazardAwareness { get; }
        public float ReactionDelay { get; }
        public float RiskTolerance { get; }
        public float AvoidanceStrength { get; }
        public float WillingnessToInterruptAttack { get; }
        public bool RecognizesHiddenTraps { get; }
        public bool CanChargeThroughDanger { get; }
        public float MistakeChance { get; }
        public float AvoidanceCooldown { get; }
        public int MaximumReactionsPerThreat { get; }
        public float MinimumReadableWarning { get; }

        public EnemyThreatResponseBehaviorV2(
            float hazardAwareness,
            float reactionDelay,
            float riskTolerance,
            float avoidanceStrength,
            float willingnessToInterruptAttack,
            bool recognizesHiddenTraps,
            bool canChargeThroughDanger,
            float mistakeChance,
            float avoidanceCooldown,
            int maximumReactionsPerThreat,
            float minimumReadableWarning)
        {
            HazardAwareness = Mathf.Clamp01(hazardAwareness);
            ReactionDelay = Mathf.Max(0f, reactionDelay);
            RiskTolerance = Mathf.Clamp01(riskTolerance);
            AvoidanceStrength = Mathf.Clamp01(avoidanceStrength);
            WillingnessToInterruptAttack = Mathf.Clamp01(
                willingnessToInterruptAttack);
            RecognizesHiddenTraps = recognizesHiddenTraps;
            CanChargeThroughDanger = canChargeThroughDanger;
            MistakeChance = Mathf.Clamp01(mistakeChance);
            AvoidanceCooldown = Mathf.Max(0f, avoidanceCooldown);
            MaximumReactionsPerThreat = Mathf.Max(
                0,
                maximumReactionsPerThreat);
            MinimumReadableWarning = Mathf.Max(
                0f,
                minimumReadableWarning);
        }
    }

    [CreateAssetMenu(
        fileName = "EnemyThreatResponseProfile",
        menuName = "Project Eri/Enemy AI V2/Threat Response Profile")]
    public sealed class EnemyThreatResponseProfileV2 : ScriptableObject
    {
        [Tooltip("How far ahead and how consistently this enemy notices hazardous delivery geometry.")]
        [SerializeField, Range(0f, 1f)] private float hazardAwareness = 0.7f;

        [Tooltip("Readable delay after noticing a threat before the enemy may react.")]
        [SerializeField, Min(0f)] private float reactionDelay = 0.18f;

        [Tooltip("How much danger the enemy accepts before changing plans. Higher values ignore more low-urgency hazards.")]
        [SerializeField, Range(0f, 1f)] private float riskTolerance = 0.35f;

        [Tooltip("How strongly avoidance competes with progress toward the enemy's existing tactical destination.")]
        [SerializeField, Range(0f, 1f)] private float avoidanceStrength = 0.75f;

        [Tooltip("Likelihood that a serious threat is worth cancelling an attack or skill already in progress.")]
        [SerializeField, Range(0f, 1f)]
        private float willingnessToInterruptAttack = 0.5f;

        [Tooltip("Allows this enemy to account for mines and tripwires before physically entering their footprint.")]
        [SerializeField] private bool recognizesHiddenTraps = true;

        [Tooltip("Allows this enemy to deliberately keep its current plan through tolerable danger instead of always avoiding it.")]
        [SerializeField] private bool canChargeThroughDanger;

        [Tooltip("Chance that this enemy fails an otherwise valid reaction opportunity. Emergency exposure still forces a response.")]
        [SerializeField, Range(0f, 1f)] private float mistakeChance = 0.12f;

        [Tooltip("Minimum time after reacting before another non-emergency avoidance order may replace it.")]
        [SerializeField, Min(0f)] private float avoidanceCooldown = 0.55f;

        [Tooltip("Maximum times this enemy may react to one continuous threat. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maximumReactionsPerThreat = 2;

        [Tooltip("Telegraphs shorter than this are considered unreadable unless the enemy is already exposed.")]
        [SerializeField, Min(0f)] private float minimumReadableWarning = 0.12f;

        public EnemyThreatResponseBehaviorV2 Behavior =>
            new EnemyThreatResponseBehaviorV2(
                hazardAwareness,
                reactionDelay,
                riskTolerance,
                avoidanceStrength,
                willingnessToInterruptAttack,
                recognizesHiddenTraps,
                canChargeThroughDanger,
                mistakeChance,
                avoidanceCooldown,
                maximumReactionsPerThreat,
                minimumReadableWarning);

        public static EnemyThreatResponseBehaviorV2 BuiltIn(
            EnemyThreatReactionPresetV2 preset)
        {
            switch (preset)
            {
                case EnemyThreatReactionPresetV2.Reckless:
                    return new EnemyThreatResponseBehaviorV2(
                        0.32f, 0.34f, 0.72f, 0.4f, 0.18f,
                        false, true, 0.32f, 0.7f, 1, 0.2f);
                case EnemyThreatReactionPresetV2.Elite:
                    return new EnemyThreatResponseBehaviorV2(
                        0.92f, 0.07f, 0.2f, 0.92f, 0.78f,
                        true, false, 0.04f, 0.38f, 3, 0.08f);
                case EnemyThreatReactionPresetV2.Boss:
                    return new EnemyThreatResponseBehaviorV2(
                        0.82f, 0.14f, 0.58f, 0.7f, 0.35f,
                        true, true, 0.08f, 0.6f, 2, 0.12f);
                default:
                    return new EnemyThreatResponseBehaviorV2(
                        0.7f, 0.18f, 0.35f, 0.75f, 0.5f,
                        true, false, 0.12f, 0.55f, 2, 0.12f);
            }
        }
    }
}
