using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEri.SkillSystemV2
{
    [Flags]
    public enum SpellAIIntent
    {
        None = 0,
        Damage = 1 << 0,
        Control = 1 << 1,
        Mobility = 1 << 2,
        Defense = 1 << 3,
        Support = 1 << 4,
        Setup = 1 << 5,
        Execute = 1 << 6,
        Escape = 1 << 7
    }

    public enum SpellAITargetPreference
    {
        CurrentTarget,
        NearestEnemy,
        LowestHealthEnemy,
        EnemyCluster,
        Self,
        LowestHealthAlly,
        AllyCluster,
        SafeGround,
        EscapeGround
    }

    public enum SpellAIPlacementIntent
    {
        Auto,
        DirectHit,
        LeadMovingTarget,
        ControlEscapeRoute,
        ProtectSelf,
        ProtectAlly,
        AffectCluster,
        ComboLocation
    }

    public enum SpellAIComboRequirementMode
    {
        AnyTag,
        AllTags
    }

    [Flags]
    public enum SpellAIReaction
    {
        None = 0,
        DodgeSideways = 1 << 0,
        LeaveArea = 1 << 1,
        SeekCover = 1 << 2,
        InterruptCaster = 1 << 3,
        Deflect = 1 << 4,
        SpreadOut = 1 << 5,
        CloseDistance = 1 << 6
    }

    [Serializable]
    public sealed class SpellAIAffordance : ISerializationCallbackReceiver
    {
        [Tooltip("Allow enemy AI to consider equipping and casting this spell. Player use is unaffected.")]
        [SerializeField] private bool usableByAI;

        [Tooltip("Plain categories describing why an AI would cast this spell. A spell may have several intents, such as Damage and Control.")]
        [SerializeField] private SpellAIIntent intents = SpellAIIntent.Damage;

        [Tooltip("The kind of target or ground point the AI should search for first.")]
        [SerializeField] private SpellAITargetPreference targetPreference =
            SpellAITargetPreference.CurrentTarget;

        [Tooltip("How a point-placement solver should use the chosen target. Auto infers a sensible approach from the spell's intent and delivery.")]
        [SerializeField] private SpellAIPlacementIntent placementIntent =
            SpellAIPlacementIntent.Auto;

        [Tooltip("Extra seconds of target movement projected for AI placement. Zero preserves travel-time prediction and lets the enemy solver supply its conservative instant-area fallback.")]
        [SerializeField, Min(0f)] private float placementLookaheadSeconds;

        [Tooltip("Closest distance at which this spell is normally useful. This is a preference, not a replacement for cast validation.")]
        [SerializeField, Min(0f)] private float preferredMinimumRange;

        [Tooltip("Farthest distance at which this spell is normally useful. Zero means no additional AI preference limit.")]
        [SerializeField, Min(0f)] private float preferredMaximumRange = 8f;

        [Tooltip("Minimum number of useful targets the AI should affect before choosing the spell. Use 2 or more for area spells that should be saved for groups.")]
        [SerializeField, Min(1)] private int minimumUsefulTargets = 1;

        [Tooltip("How strongly the AI should prefer this spell before situational scoring. One is normal; zero effectively disables it without changing Usable By AI.")]
        [SerializeField, Min(0f)] private float baseUtility = 1f;

        [Tooltip("How risky it is to commit to this cast. Long stationary casts should be near one; instant safe casts should be near zero.")]
        [SerializeField, Range(0f, 1f)] private float commitmentRisk;

        [Header("AI cadence and active placement")]
        [Tooltip("Minimum seconds before the same caster may choose this spell again, independent of the gameplay cooldown.")]
        [SerializeField, Min(0f)] private float minimumAIRecastInterval =
            1.25f;

        [Tooltip("Maximum remembered persistent instances of this spell from one caster. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maximumActiveInstancesPerCaster =
            1;

        [Tooltip("Maximum remembered persistent instances of this spell across the enemy squad. Zero means unlimited.")]
        [SerializeField, Min(0)] private int maximumActiveInstancesPerSquad =
            2;

        [Tooltip("Permit a new persistent placement to substantially overlap an equivalent active placement.")]
        [SerializeField] private bool allowEquivalentOverlap;

        [Tooltip("Utility multiplier used when equivalent overlap is allowed. Values below one discourage redundant placement without forbidding it.")]
        [SerializeField, Min(0f)]
        private float equivalentOverlapUtilityMultiplier = 0.25f;

        [Tooltip("Health gate for Execute targets, Escape casters, and ally-targeted Support spells.")]
        [SerializeField, Range(0f, 1f)] private float healthThreshold = 0.3f;

        [Tooltip("Allow the AI to approach its selected actor before casting instead of rejecting or wasting a short-range spell from too far away.")]
        [SerializeField] private bool moveIntoRangeBeforeCasting;

        [Tooltip("Center-to-center distance at which the AI stops approaching and starts the cast. Keep this slightly inside the delivery's real reach.")]
        [SerializeField, Min(0.1f)] private float requiredAICastRange = 1.4f;

        [Tooltip("Farthest distance the AI is willing to travel to begin this cast. Zero means unlimited.")]
        [SerializeField, Min(0f)] private float maximumAIApproachDistance = 8f;

        [Tooltip("For actor-targeted Support casts, ask the allied target to hold position during the actual buildup so squad movement does not break the cast.")]
        [SerializeField] private bool requestTargetHoldDuringSupportCast = true;

        [Tooltip("Optionally interrupt this AI Support action when the caster actually takes damage. Leave disabled when nearby delivery danger should be the readable counterplay.")]
        [SerializeField] private bool interruptSupportCastWhenDamaged;

        [FormerlySerializedAs("requireDamageToInterruptSupportCast")]
        [Tooltip("Allow a live hostile delivery with sufficient score and imminent impact timing to interrupt this Support action.")]
        [SerializeField] private bool interruptSupportCastForImminentThreat =
            true;

        [Tooltip("Minimum normalized threat score required to interrupt this Support action.")]
        [SerializeField, Range(0f, 1f)]
        private float supportCastThreatInterruptScore = 0.72f;

        [Tooltip("A qualifying threat must already contain the caster or be predicted to hit within this many seconds.")]
        [SerializeField, Min(0f)]
        private float supportCastThreatInterruptWindow = 0.9f;

        [SerializeField, HideInInspector]
        private int supportCoordinationVersion = 2;

        [Tooltip("Tags this spell creates for later combo decisions, such as Wet, Marked, Grouped, or Oil.")]
        [SerializeField] private List<string> producesComboTags =
            new List<string>();

        [Tooltip("Tags that make this spell more valuable, such as Burning consuming Oil. These are AI planning hints, not gameplay requirements.")]
        [SerializeField] private List<string> consumesComboTags =
            new List<string>();

        [Tooltip("Delivery event that makes Produced Combo Tags available to allied AI. Delivery Started is appropriate for persistent fields; Target Hit is useful for marks applied to actors.")]
        [SerializeField] private SpellEventType comboTagActivationEvent =
            SpellEventType.DeliveryStarted;

        [Tooltip("How long produced combo tags remain available when the delivery has no persistent authored lifetime. Zero uses the delivery lifetime when available, then a conservative four-second fallback.")]
        [SerializeField, Min(0f)] private float comboOpportunityLifetime;

        [Tooltip("Planning footprint for produced combo tags. Zero uses the delivery's reported geometry or inferred radius.")]
        [SerializeField, Min(0f)] private float comboOpportunityRadius;

        [Tooltip("When enabled, this spell is rejected unless the squad currently owns a matching combo opportunity. When disabled, matching tags are a utility bonus only.")]
        [SerializeField] private bool requireActiveComboToCast;

        [Tooltip("Whether one matching consumed tag is enough or every authored consumed tag must be present on the same opportunity.")]
        [SerializeField] private SpellAIComboRequirementMode
            comboRequirementMode = SpellAIComboRequirementMode.AnyTag;

        [Tooltip("Utility multiplier when this spell can consume a matching active setup.")]
        [SerializeField, Min(0f)] private float comboUtilityMultiplier = 1.65f;

        [Tooltip("Reserve a matched setup until this cast completes so another squad member cannot consume it simultaneously.")]
        [SerializeField] private bool consumeComboOpportunityOnCast = true;

        [Tooltip("Minimum reservation window for setup/consumer coordination. Long casts automatically extend this window through their authored duration.")]
        [SerializeField, Min(0.05f)] private float comboReservationSeconds =
            1.5f;

        [Tooltip("Allow this caster to consume a combo setup it produced itself. Disable for combos that must demonstrate squad cooperation.")]
        [SerializeField] private bool allowSelfCombo = true;

        [Tooltip("Prevent this squad from starting another nearby setup that produces equivalent tags while an active or reserved setup already covers the location.")]
        [SerializeField] private bool suppressRedundantComboSetup = true;

        [Tooltip("Reject this setup spell unless another living squad member has an AI-enabled spell that consumes one of its produced tags.")]
        [SerializeField] private bool requireSquadConsumerForSetup;

        [Tooltip("Utility multiplier for a setup when another living squad member has a compatible consumer equipped.")]
        [SerializeField, Min(0f)] private float
            setupUtilityMultiplierWithConsumer = 1.35f;

        [Header("How opponents should read it")]
        [Tooltip("Reasonable reactions available to an AI threatened by this spell. The reaction system chooses among actions the enemy can actually perform.")]
        [SerializeField] private SpellAIReaction suggestedReactions =
            SpellAIReaction.DodgeSideways;

        [Tooltip("Approximate danger radius used for reaction planning. Zero asks the delivery runtime to infer its own footprint.")]
        [SerializeField, Min(0f)] private float dangerRadius;

        [Tooltip("How urgently an affected AI should react. One is an immediate danger; lower values allow it to finish more valuable actions first.")]
        [SerializeField, Range(0f, 1f)] private float reactionUrgency = 0.7f;

        [Tooltip("Seconds of warning an AI may use before the spell becomes dangerous. Keep this aligned with the visible telegraph.")]
        [SerializeField, Min(0f)] private float telegraphDuration;

        public bool UsableByAI => usableByAI;
        public SpellAIIntent Intents => intents;
        public SpellAITargetPreference TargetPreference => targetPreference;
        public SpellAIPlacementIntent PlacementIntent => placementIntent;
        public float PlacementLookaheadSeconds =>
            Mathf.Max(0f, placementLookaheadSeconds);
        public float PreferredMinimumRange => Mathf.Max(0f, preferredMinimumRange);
        public float PreferredMaximumRange => Mathf.Max(0f, preferredMaximumRange);
        public int MinimumUsefulTargets => Mathf.Max(1, minimumUsefulTargets);
        public float BaseUtility => Mathf.Max(0f, baseUtility);
        public float CommitmentRisk => Mathf.Clamp01(commitmentRisk);
        public float MinimumAIRecastInterval =>
            Mathf.Max(0f, minimumAIRecastInterval);
        public int MaximumActiveInstancesPerCaster =>
            Mathf.Max(0, maximumActiveInstancesPerCaster);
        public int MaximumActiveInstancesPerSquad =>
            Mathf.Max(0, maximumActiveInstancesPerSquad);
        public bool AllowEquivalentOverlap => allowEquivalentOverlap;
        public float EquivalentOverlapUtilityMultiplier =>
            Mathf.Max(0f, equivalentOverlapUtilityMultiplier);
        public float HealthThreshold => Mathf.Clamp01(healthThreshold);
        public bool MoveIntoRangeBeforeCasting =>
            moveIntoRangeBeforeCasting;
        public float RequiredAICastRange =>
            Mathf.Max(0.1f, requiredAICastRange);
        public float MaximumAIApproachDistance =>
            Mathf.Max(0f, maximumAIApproachDistance);
        public bool RequestTargetHoldDuringSupportCast =>
            requestTargetHoldDuringSupportCast;
        public bool InterruptSupportCastWhenDamaged =>
            interruptSupportCastWhenDamaged;
        public bool InterruptSupportCastForImminentThreat =>
            interruptSupportCastForImminentThreat;
        public float SupportCastThreatInterruptScore =>
            Mathf.Clamp01(supportCastThreatInterruptScore);
        public float SupportCastThreatInterruptWindow =>
            Mathf.Max(0f, supportCastThreatInterruptWindow);
        public IReadOnlyList<string> ProducesComboTags =>
            producesComboTags ??= new List<string>();
        public IReadOnlyList<string> ConsumesComboTags =>
            consumesComboTags ??= new List<string>();
        public SpellEventType ComboTagActivationEvent =>
            comboTagActivationEvent;
        public float ComboOpportunityLifetime =>
            Mathf.Max(0f, comboOpportunityLifetime);
        public float ComboOpportunityRadius =>
            Mathf.Max(0f, comboOpportunityRadius);
        public bool RequireActiveComboToCast => requireActiveComboToCast;
        public SpellAIComboRequirementMode ComboRequirementMode =>
            comboRequirementMode;
        public float ComboUtilityMultiplier =>
            Mathf.Max(0f, comboUtilityMultiplier);
        public bool ConsumeComboOpportunityOnCast =>
            consumeComboOpportunityOnCast;
        public float ComboReservationSeconds =>
            Mathf.Max(0.05f, comboReservationSeconds);
        public bool AllowSelfCombo => allowSelfCombo;
        public bool SuppressRedundantComboSetup =>
            suppressRedundantComboSetup;
        public bool RequireSquadConsumerForSetup =>
            requireSquadConsumerForSetup;
        public float SetupUtilityMultiplierWithConsumer =>
            Mathf.Max(0f, setupUtilityMultiplierWithConsumer);
        public SpellAIReaction SuggestedReactions => suggestedReactions;
        public float DangerRadius => Mathf.Max(0f, dangerRadius);
        public float ReactionUrgency => Mathf.Clamp01(reactionUrgency);
        public float TelegraphDuration => Mathf.Max(0f, telegraphDuration);

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (supportCoordinationVersion < 1)
                requestTargetHoldDuringSupportCast = true;

            if (supportCoordinationVersion < 2)
            {
                // Replace the earlier damage-only interruption default with
                // verified imminent delivery danger. Damage remains an
                // optional independent authoring choice.
                interruptSupportCastWhenDamaged = false;
                interruptSupportCastForImminentThreat = true;
                supportCastThreatInterruptScore = 0.72f;
                supportCastThreatInterruptWindow = 0.9f;
            }

            supportCoordinationVersion = 2;
        }
    }

    public readonly struct SpellAIDecisionContext
    {
        public float DistanceToTarget { get; }
        public int UsefulTargetCount { get; }
        public float CasterHealthFraction { get; }
        public float TargetHealthFraction { get; }
        public float IncomingDanger { get; }
        public float CurrentCommitmentCost { get; }
        public ISet<string> ActiveComboTags { get; }
        public bool WillApproachTarget { get; }

        public SpellAIDecisionContext(
            float distanceToTarget,
            int usefulTargetCount,
            float casterHealthFraction,
            float targetHealthFraction,
            float incomingDanger,
            float currentCommitmentCost,
            ISet<string> activeComboTags = null,
            bool willApproachTarget = false)
        {
            DistanceToTarget = Mathf.Max(0f, distanceToTarget);
            UsefulTargetCount = Mathf.Max(0, usefulTargetCount);
            CasterHealthFraction = Mathf.Clamp01(casterHealthFraction);
            TargetHealthFraction = Mathf.Clamp01(targetHealthFraction);
            IncomingDanger = Mathf.Clamp01(incomingDanger);
            CurrentCommitmentCost = Mathf.Clamp01(currentCommitmentCost);
            ActiveComboTags = activeComboTags;
            WillApproachTarget = willApproachTarget;
        }
    }

    public static class SpellAIDecisionUtility
    {
        public static float Score(
            SpellDefinition spell,
            in SpellAIDecisionContext context)
        {
            if (spell == null || spell.AIAffordance == null ||
                !spell.AIAffordance.UsableByAI)
            {
                return float.NegativeInfinity;
            }

            SpellAIAffordance data = spell.AIAffordance;
            if (context.UsefulTargetCount < data.MinimumUsefulTargets ||
                context.DistanceToTarget < data.PreferredMinimumRange ||
                (!context.WillApproachTarget &&
                 data.PreferredMaximumRange > 0f &&
                 context.DistanceToTarget > data.PreferredMaximumRange))
            {
                return float.NegativeInfinity;
            }

            float score = data.BaseUtility;
            score *= 1f - data.CommitmentRisk *
                     Mathf.Max(context.IncomingDanger,
                         context.CurrentCommitmentCost);
            score *= 1f + Mathf.Max(
                0,
                context.UsefulTargetCount - data.MinimumUsefulTargets) * 0.2f;

            if ((data.Intents & SpellAIIntent.Escape) != 0 &&
                context.CasterHealthFraction <= data.HealthThreshold)
            {
                score *= 1.75f;
            }
            if ((data.Intents & SpellAIIntent.Execute) != 0 &&
                context.TargetHealthFraction <= data.HealthThreshold)
            {
                score *= 1.75f;
            }
            if ((data.Intents & SpellAIIntent.Support) != 0 &&
                (data.TargetPreference ==
                    SpellAITargetPreference.LowestHealthAlly ||
                 data.TargetPreference ==
                    SpellAITargetPreference.AllyCluster))
            {
                if (context.TargetHealthFraction >= 0.999f ||
                    context.TargetHealthFraction > data.HealthThreshold)
                {
                    return float.NegativeInfinity;
                }

                float missingHealth = 1f - context.TargetHealthFraction;
                score *= Mathf.Lerp(1.15f, 2f, missingHealth);
            }

            IReadOnlyList<string> consumed = data.ConsumesComboTags;
            if (context.ActiveComboTags != null)
            {
                int matched = 0;
                for (int i = 0; i < consumed.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(consumed[i]) &&
                        context.ActiveComboTags.Contains(consumed[i]))
                    {
                        matched++;
                    }
                }

                if (matched > 0)
                {
                    float coverage = matched /
                        (float)Mathf.Max(1, consumed.Count);
                    score *= Mathf.Lerp(
                        1f,
                        data.ComboUtilityMultiplier,
                        coverage);
                }
            }

            return Mathf.Max(0f, score);
        }
    }
}
