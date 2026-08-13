using System;
using System.Collections.Generic;
using UnityEngine;

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
    public sealed class SpellAIAffordance
    {
        [Tooltip("Allow enemy AI to consider equipping and casting this spell. Player use is unaffected.")]
        [SerializeField] private bool usableByAI;

        [Tooltip("Plain categories describing why an AI would cast this spell. A spell may have several intents, such as Damage and Control.")]
        [SerializeField] private SpellAIIntent intents = SpellAIIntent.Damage;

        [Tooltip("The kind of target or ground point the AI should search for first.")]
        [SerializeField] private SpellAITargetPreference targetPreference =
            SpellAITargetPreference.CurrentTarget;

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

        [Tooltip("For Execute or Escape spells, only prefer the special behavior below this caster or target health fraction.")]
        [SerializeField, Range(0f, 1f)] private float healthThreshold = 0.3f;

        [Tooltip("Tags this spell creates for later combo decisions, such as Wet, Marked, Grouped, or Oil.")]
        [SerializeField] private List<string> producesComboTags =
            new List<string>();

        [Tooltip("Tags that make this spell more valuable, such as Burning consuming Oil. These are AI planning hints, not gameplay requirements.")]
        [SerializeField] private List<string> consumesComboTags =
            new List<string>();

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
        public float PreferredMinimumRange => Mathf.Max(0f, preferredMinimumRange);
        public float PreferredMaximumRange => Mathf.Max(0f, preferredMaximumRange);
        public int MinimumUsefulTargets => Mathf.Max(1, minimumUsefulTargets);
        public float BaseUtility => Mathf.Max(0f, baseUtility);
        public float CommitmentRisk => Mathf.Clamp01(commitmentRisk);
        public float HealthThreshold => Mathf.Clamp01(healthThreshold);
        public IReadOnlyList<string> ProducesComboTags =>
            producesComboTags ??= new List<string>();
        public IReadOnlyList<string> ConsumesComboTags =>
            consumesComboTags ??= new List<string>();
        public SpellAIReaction SuggestedReactions => suggestedReactions;
        public float DangerRadius => Mathf.Max(0f, dangerRadius);
        public float ReactionUrgency => Mathf.Clamp01(reactionUrgency);
        public float TelegraphDuration => Mathf.Max(0f, telegraphDuration);
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

        public SpellAIDecisionContext(
            float distanceToTarget,
            int usefulTargetCount,
            float casterHealthFraction,
            float targetHealthFraction,
            float incomingDanger,
            float currentCommitmentCost,
            ISet<string> activeComboTags = null)
        {
            DistanceToTarget = Mathf.Max(0f, distanceToTarget);
            UsefulTargetCount = Mathf.Max(0, usefulTargetCount);
            CasterHealthFraction = Mathf.Clamp01(casterHealthFraction);
            TargetHealthFraction = Mathf.Clamp01(targetHealthFraction);
            IncomingDanger = Mathf.Clamp01(incomingDanger);
            CurrentCommitmentCost = Mathf.Clamp01(currentCommitmentCost);
            ActiveComboTags = activeComboTags;
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
                (data.PreferredMaximumRange > 0f &&
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

            IReadOnlyList<string> consumed = data.ConsumesComboTags;
            if (context.ActiveComboTags != null)
            {
                for (int i = 0; i < consumed.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(consumed[i]) &&
                        context.ActiveComboTags.Contains(consumed[i]))
                    {
                        score *= 1.5f;
                    }
                }
            }

            return Mathf.Max(0f, score);
        }
    }
}
