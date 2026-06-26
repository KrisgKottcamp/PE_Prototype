using UnityEngine;

[CreateAssetMenu(
    menuName = "Combat/Combat Rank Settings",
    fileName = "CombatRankSettings"
)]
public class CombatRankSettings : ScriptableObject
{
    [Header("Automatic Target Time")]
    [Tooltip(
        "Fixed setup time granted to every encounter before enemy count is added."
    )]
    [SerializeField, Min(0f)]
    private float baseTargetSeconds = 20f;

    [Tooltip(
        "Additional target time granted for every successfully spawned enemy."
    )]
    [SerializeField, Min(0f)]
    private float targetSecondsPerEnemy = 30f;

    [Tooltip(
        "The target time can never fall below this value."
    )]
    [SerializeField, Min(1f)]
    private float minimumTargetSeconds = 35f;

    [Header("Final Score Weights")]
    [SerializeField, Range(0f, 1f)]
    private float timeWeight = 0.45f;

    [SerializeField, Range(0f, 1f)]
    private float momentumWeight = 0.35f;

    [SerializeField, Range(0f, 1f)]
    private float cleanPlayWeight = 0.20f;

    [Header("Time Score Curve")]
    [Tooltip(
        "Finishing at or below this fraction of target time gives a perfect time score."
    )]
    [SerializeField, Range(0.10f, 1f)]
    private float perfectTimeRatio = 0.45f;

    [Tooltip(
        "Time score earned when the player finishes exactly at target time."
    )]
    [SerializeField, Range(0f, 1f)]
    private float scoreAtTargetTime = 0.35f;

    [Tooltip(
        "At or above this fraction of target time, time score becomes zero."
    )]
    [SerializeField, Min(1.01f)]
    private float zeroTimeScoreRatio = 1.25f;

    [Header("Momentum Score")]
    [Tooltip(
        "Weight of Active Average Momentum after the first successful skill."
    )]
    [SerializeField, Range(0f, 1f)]
    private float activeAverageMomentumWeight = 0.85f;

    [Tooltip(
        "Weight of the highest Momentum reached."
    )]
    [SerializeField, Range(0f, 1f)]
    private float peakMomentumWeight = 0.15f;

    [Tooltip(
        "Above 1 makes mediocre Momentum performance score more harshly."
    )]
    [SerializeField, Range(0.25f, 2f)]
    private float momentumExponent = 1.10f;

    [Header("Clean Play Score")]
    [Tooltip(
        "Clean Play reaches zero after taking damage equal to this fraction " +
        "of the party's combined maximum HP."
    )]
    [SerializeField, Range(0.10f, 2f)]
    private float zeroCleanScoreAtPartyHealthLost = 0.80f;

    [Header("Base Star Thresholds")]
    [SerializeField, Range(0f, 1f)]
    private float twoStarThreshold = 0.30f;

    [SerializeField, Range(0f, 1f)]
    private float threeStarThreshold = 0.50f;

    [SerializeField, Range(0f, 1f)]
    private float fourStarThreshold = 0.72f;

    [SerializeField, Range(0f, 1f)]
    private float fiveStarThreshold = 0.82f;

    [Header("Party Death Caps")]
    [Tooltip(
        "The highest possible rank after exactly one party member is defeated."
    )]
    [SerializeField, Range(1, 5)]
    private int oneDeathMaximumStars = 3;

    [Tooltip(
        "The highest possible rank after two or more party members are defeated."
    )]
    [SerializeField, Range(1, 5)]
    private int multipleDeathsMaximumStars = 1;

    [Header("Four-Star Eligibility")]
    [Tooltip(
        "Four stars reward strong overall play. These are minimum category floors, " +
        "not mastery requirements."
    )]
    [SerializeField, Range(0f, 1f)]
    private float fourStarMinimumTimeScore = 0.55f;

    [SerializeField, Range(0f, 1f)]
    private float fourStarMinimumMomentumScore = 0.35f;

    [SerializeField, Range(0f, 1f)]
    private float fourStarMinimumCleanPlayScore = 0.60f;

    [Header("Five-Star Eligibility")]
    [Tooltip(
        "Five stars reward an excellent run, but Legendary remains the only " +
        "true mastery checklist."
    )]
    [SerializeField, Range(0f, 1f)]
    private float fiveStarMinimumTimeScore = 0.75f;

    [SerializeField, Range(0f, 1f)]
    private float fiveStarMinimumMomentumScore = 0.50f;

    [SerializeField, Range(0f, 1f)]
    private float fiveStarMinimumCleanPlayScore = 0.75f;

    [Header("Legendary Rank Gate")]
    [Tooltip(
        "Legendary is the true mastery tier above five stars."
    )]
    [SerializeField]
    private bool enableLegendaryRank = true;

    [Tooltip(
        "Legendary requires completion at or below this fraction of target time."
    )]
    [SerializeField, Range(0.10f, 1.5f)]
    private float legendaryMaximumTimeRatio = 0.45f;

    [SerializeField, Range(0f, 1f)]
    private float legendaryMinimumMomentumScore = 0.82f;

    [SerializeField, Range(0f, 1f)]
    private float legendaryMinimumCleanPlayScore = 0.95f;

    [SerializeField, Range(0f, 1f)]
    private float legendaryMinimumOverallScore = 0.92f;

    public float TimeWeight => Mathf.Max(0f, timeWeight);
    public float MomentumWeight => Mathf.Max(0f, momentumWeight);
    public float CleanPlayWeight => Mathf.Max(0f, cleanPlayWeight);

    // Compatibility with the earlier calculator/property name.
    public float AverageMomentumWeight => MomentumWeight;

    public float PerfectTimeRatio =>
        Mathf.Clamp(perfectTimeRatio, 0.10f, 1f);

    public float ScoreAtTargetTime =>
        Mathf.Clamp01(scoreAtTargetTime);

    public float ZeroTimeScoreRatio =>
        Mathf.Max(1.01f, zeroTimeScoreRatio);

    public float ActiveAverageMomentumWeight =>
        Mathf.Max(0f, activeAverageMomentumWeight);

    public float PeakMomentumWeight =>
        Mathf.Max(0f, peakMomentumWeight);

    public float AverageMomentumExponent =>
        Mathf.Max(0.01f, momentumExponent);

    public float ZeroCleanScoreAtPartyHealthLost =>
        Mathf.Max(0.01f, zeroCleanScoreAtPartyHealthLost);

    public float TwoStarThreshold => Mathf.Clamp01(twoStarThreshold);
    public float ThreeStarThreshold => Mathf.Clamp01(threeStarThreshold);
    public float FourStarThreshold => Mathf.Clamp01(fourStarThreshold);
    public float FiveStarThreshold => Mathf.Clamp01(fiveStarThreshold);

    public int OneDeathMaximumStars =>
        Mathf.Clamp(oneDeathMaximumStars, 1, 5);

    public int MultipleDeathsMaximumStars =>
        Mathf.Clamp(multipleDeathsMaximumStars, 1, 5);

    public float FourStarMinimumTimeScore =>
        Mathf.Clamp01(fourStarMinimumTimeScore);

    public float FourStarMinimumMomentumScore =>
        Mathf.Clamp01(fourStarMinimumMomentumScore);

    public float FourStarMinimumCleanPlayScore =>
        Mathf.Clamp01(fourStarMinimumCleanPlayScore);

    public float FiveStarMinimumTimeScore =>
        Mathf.Clamp01(fiveStarMinimumTimeScore);

    public float FiveStarMinimumMomentumScore =>
        Mathf.Clamp01(fiveStarMinimumMomentumScore);

    public float FiveStarMinimumCleanPlayScore =>
        Mathf.Clamp01(fiveStarMinimumCleanPlayScore);

    public bool EnableLegendaryRank =>
        enableLegendaryRank;

    public float LegendaryMaximumTimeRatio =>
        Mathf.Max(0.01f, legendaryMaximumTimeRatio);

    public float LegendaryMinimumMomentumScore =>
        Mathf.Clamp01(legendaryMinimumMomentumScore);

    public float LegendaryMinimumCleanPlayScore =>
        Mathf.Clamp01(legendaryMinimumCleanPlayScore);

    public float LegendaryMinimumOverallScore =>
        Mathf.Clamp01(legendaryMinimumOverallScore);

    public float CalculateTargetTime(int enemyCount)
    {
        float calculated =
            Mathf.Max(0f, baseTargetSeconds) +
            Mathf.Max(0, enemyCount) *
            Mathf.Max(0f, targetSecondsPerEnemy);

        return Mathf.Max(
            Mathf.Max(1f, minimumTargetSeconds),
            calculated
        );
    }

    [ContextMenu("Apply Mastery Rank Defaults")]
    public void ApplyMasteryRankDefaults()
    {
        baseTargetSeconds = 20f;
        targetSecondsPerEnemy = 30f;
        minimumTargetSeconds = 35f;

        timeWeight = 0.45f;
        momentumWeight = 0.35f;
        cleanPlayWeight = 0.20f;

        perfectTimeRatio = 0.45f;
        scoreAtTargetTime = 0.35f;
        zeroTimeScoreRatio = 1.25f;

        activeAverageMomentumWeight = 0.85f;
        peakMomentumWeight = 0.15f;
        momentumExponent = 1.10f;

        zeroCleanScoreAtPartyHealthLost = 0.80f;

        twoStarThreshold = 0.30f;
        threeStarThreshold = 0.50f;
        fourStarThreshold = 0.72f;
        fiveStarThreshold = 0.82f;

        oneDeathMaximumStars = 3;
        multipleDeathsMaximumStars = 1;

        fourStarMinimumTimeScore = 0.55f;
        fourStarMinimumMomentumScore = 0.35f;
        fourStarMinimumCleanPlayScore = 0.60f;

        fiveStarMinimumTimeScore = 0.75f;
        fiveStarMinimumMomentumScore = 0.50f;
        fiveStarMinimumCleanPlayScore = 0.75f;

        enableLegendaryRank = true;
        legendaryMaximumTimeRatio = 0.45f;
        legendaryMinimumMomentumScore = 0.82f;
        legendaryMinimumCleanPlayScore = 0.95f;
        legendaryMinimumOverallScore = 0.92f;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Apply Recommended Project Eri Defaults")]
    private void ApplyLegacyRecommendedDefaultsMenu()
    {
        ApplyMasteryRankDefaults();
    }

    private void OnValidate()
    {
        perfectTimeRatio =
            Mathf.Clamp(perfectTimeRatio, 0.10f, 1f);

        zeroTimeScoreRatio =
            Mathf.Max(1.01f, zeroTimeScoreRatio);

        twoStarThreshold = Mathf.Clamp01(twoStarThreshold);

        threeStarThreshold = Mathf.Clamp(
            threeStarThreshold,
            twoStarThreshold,
            1f
        );

        fourStarThreshold = Mathf.Clamp(
            fourStarThreshold,
            threeStarThreshold,
            1f
        );

        fiveStarThreshold = Mathf.Clamp(
            fiveStarThreshold,
            fourStarThreshold,
            1f
        );

        oneDeathMaximumStars =
            Mathf.Clamp(oneDeathMaximumStars, 1, 5);

        multipleDeathsMaximumStars =
            Mathf.Clamp(multipleDeathsMaximumStars, 1, 5);

    }
}
