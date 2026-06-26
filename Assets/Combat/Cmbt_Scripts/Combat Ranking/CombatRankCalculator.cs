using UnityEngine;

public static class CombatRankCalculator
{
    private const float DefaultTimeWeight = 0.45f;
    private const float DefaultMomentumWeight = 0.35f;
    private const float DefaultCleanWeight = 0.20f;

    private const float DefaultPerfectTimeRatio = 0.45f;
    private const float DefaultScoreAtTarget = 0.35f;
    private const float DefaultZeroTimeRatio = 1.25f;

    private const float DefaultActiveAverageWeight = 0.85f;
    private const float DefaultPeakWeight = 0.15f;
    private const float DefaultMomentumExponent = 1.10f;

    private const float DefaultZeroCleanAtDamageFraction = 0.80f;

    private const float DefaultTwoStarThreshold = 0.30f;
    private const float DefaultThreeStarThreshold = 0.50f;
    private const float DefaultFourStarThreshold = 0.72f;
    private const float DefaultFiveStarThreshold = 0.82f;

    private const int DefaultOneDeathCap = 3;
    private const int DefaultMultipleDeathCap = 1;

    private const float DefaultFourMinTimeScore = 0.55f;
    private const float DefaultFourMinMomentum = 0.35f;
    private const float DefaultFourMinClean = 0.60f;

    private const float DefaultFiveMinTimeScore = 0.75f;
    private const float DefaultFiveMinMomentum = 0.50f;
    private const float DefaultFiveMinClean = 0.75f;

    private const bool DefaultLegendaryEnabled = true;
    private const float DefaultLegendaryMaxTimeRatio = 0.45f;
    private const float DefaultLegendaryMinMomentum = 0.82f;
    private const float DefaultLegendaryMinClean = 0.95f;
    private const float DefaultLegendaryMinOverall = 0.92f;

    public static CombatRankResult Calculate(
        AttackMomentumResult encounter,
        float targetTimeSeconds,
        int totalDamageTaken,
        int combinedPartyMaxHP,
        int partyMemberDeaths,
        CombatRankSettings settings)
    {
        targetTimeSeconds = Mathf.Max(0.01f, targetTimeSeconds);
        totalDamageTaken = Mathf.Max(0, totalDamageTaken);
        combinedPartyMaxHP = Mathf.Max(1, combinedPartyMaxHP);
        partyMemberDeaths = Mathf.Max(0, partyMemberDeaths);

        float timeScore = CalculateTimeScore(
            encounter.encounterSeconds,
            targetTimeSeconds,
            settings
        );

        float momentumScore = CalculateMomentumScore(
            encounter,
            settings
        );

        float cleanScore = CalculateCleanPlayScore(
            totalDamageTaken,
            combinedPartyMaxHP,
            settings
        );

        float timeWeight =
            settings != null
                ? settings.TimeWeight
                : DefaultTimeWeight;

        float momentumWeight =
            settings != null
                ? settings.MomentumWeight
                : DefaultMomentumWeight;

        float cleanWeight =
            settings != null
                ? settings.CleanPlayWeight
                : DefaultCleanWeight;

        float totalWeight =
            timeWeight +
            momentumWeight +
            cleanWeight;

        if (totalWeight <= 0.0001f)
        {
            timeWeight = DefaultTimeWeight;
            momentumWeight = DefaultMomentumWeight;
            cleanWeight = DefaultCleanWeight;

            totalWeight =
                timeWeight +
                momentumWeight +
                cleanWeight;
        }

        float overallScore = Mathf.Clamp01(
            (
                timeScore * timeWeight +
                momentumScore * momentumWeight +
                cleanScore * cleanWeight
            ) /
            totalWeight
        );

        int baseStars = CalculateBaseStars(
            overallScore,
            settings
        );

        float timeRatio =
            encounter.encounterSeconds /
            targetTimeSeconds;

        bool passedFourGate =
            PassesFourStarGate(
                timeScore,
                momentumScore,
                cleanScore,
                partyMemberDeaths,
                settings
            );

        bool passedFiveGate =
            PassesFiveStarGate(
                timeScore,
                momentumScore,
                cleanScore,
                partyMemberDeaths,
                settings
            );

        bool passedLegendaryGate =
            PassesLegendaryGate(
                timeRatio,
                momentumScore,
                cleanScore,
                overallScore,
                partyMemberDeaths,
                settings
            );

        int masteryGatedStars = baseStars;

        if (masteryGatedStars >= 5 &&
            !passedFiveGate)
        {
            masteryGatedStars = 4;
        }

        if (masteryGatedStars >= 4 &&
            !passedFourGate)
        {
            masteryGatedStars = 3;
        }

        int deathCap = GetDeathStarCap(
            partyMemberDeaths,
            settings
        );

        int finalStars = Mathf.Min(
            masteryGatedStars,
            deathCap
        );

        bool isLegendary =
            finalStars == 5 &&
            deathCap == 5 &&
            passedLegendaryGate;

        return new CombatRankResult(
            finalStars,
            baseStars,
            deathCap,
            isLegendary,
            overallScore,
            timeScore,
            momentumScore,
            cleanScore,
            encounter.encounterSeconds,
            targetTimeSeconds,
            totalDamageTaken,
            combinedPartyMaxHP,
            partyMemberDeaths,
            passedFourGate,
            passedFiveGate,
            passedLegendaryGate
        );
    }

    public static float CalculateTimeScore(
        float actualSeconds,
        float targetSeconds,
        CombatRankSettings settings)
    {
        targetSeconds = Mathf.Max(0.01f, targetSeconds);
        actualSeconds = Mathf.Max(0f, actualSeconds);

        float perfectRatio =
            settings != null
                ? settings.PerfectTimeRatio
                : DefaultPerfectTimeRatio;

        float scoreAtTarget =
            settings != null
                ? settings.ScoreAtTargetTime
                : DefaultScoreAtTarget;

        float zeroRatio =
            settings != null
                ? settings.ZeroTimeScoreRatio
                : DefaultZeroTimeRatio;

        float ratio = actualSeconds / targetSeconds;

        if (ratio <= perfectRatio)
            return 1f;

        if (ratio <= 1f)
        {
            float t = Mathf.InverseLerp(
                perfectRatio,
                1f,
                ratio
            );

            return Mathf.Lerp(
                1f,
                scoreAtTarget,
                t
            );
        }

        if (ratio >= zeroRatio)
            return 0f;

        float slowT = Mathf.InverseLerp(
            1f,
            zeroRatio,
            ratio
        );

        return Mathf.Lerp(
            scoreAtTarget,
            0f,
            slowT
        );
    }

    public static float CalculateMomentumScore(
        AttackMomentumResult encounter,
        CombatRankSettings settings)
    {
        float activeWeight =
            settings != null
                ? settings.ActiveAverageMomentumWeight
                : DefaultActiveAverageWeight;

        float peakWeight =
            settings != null
                ? settings.PeakMomentumWeight
                : DefaultPeakWeight;

        float totalWeight =
            activeWeight + peakWeight;

        if (totalWeight <= 0.0001f)
        {
            activeWeight = DefaultActiveAverageWeight;
            peakWeight = DefaultPeakWeight;
            totalWeight = activeWeight + peakWeight;
        }

        float blended = Mathf.Clamp01(
            (
                encounter.activeAverageMomentumNormalized *
                activeWeight +
                encounter.peakMomentumNormalized *
                peakWeight
            ) /
            totalWeight
        );

        float exponent =
            settings != null
                ? settings.AverageMomentumExponent
                : DefaultMomentumExponent;

        return Mathf.Clamp01(
            Mathf.Pow(
                blended,
                Mathf.Max(0.01f, exponent)
            )
        );
    }

    public static float CalculateCleanPlayScore(
        int totalDamageTaken,
        int combinedPartyMaxHP,
        CombatRankSettings settings)
    {
        totalDamageTaken = Mathf.Max(0, totalDamageTaken);
        combinedPartyMaxHP = Mathf.Max(1, combinedPartyMaxHP);

        float zeroAt =
            settings != null
                ? settings.ZeroCleanScoreAtPartyHealthLost
                : DefaultZeroCleanAtDamageFraction;

        zeroAt = Mathf.Max(0.01f, zeroAt);

        float damageFraction =
            totalDamageTaken /
            (float)combinedPartyMaxHP;

        return Mathf.Clamp01(
            1f - damageFraction / zeroAt
        );
    }

    private static int CalculateBaseStars(
        float overallScore,
        CombatRankSettings settings)
    {
        float two =
            settings != null
                ? settings.TwoStarThreshold
                : DefaultTwoStarThreshold;

        float three =
            settings != null
                ? settings.ThreeStarThreshold
                : DefaultThreeStarThreshold;

        float four =
            settings != null
                ? settings.FourStarThreshold
                : DefaultFourStarThreshold;

        float five =
            settings != null
                ? settings.FiveStarThreshold
                : DefaultFiveStarThreshold;

        if (overallScore >= five)
            return 5;

        if (overallScore >= four)
            return 4;

        if (overallScore >= three)
            return 3;

        if (overallScore >= two)
            return 2;

        return 1;
    }

    private static bool PassesFourStarGate(
        float timeScore,
        float momentumScore,
        float cleanScore,
        int deaths,
        CombatRankSettings settings)
    {
        if (deaths > 0)
            return false;

        float minTime =
            settings != null
                ? settings.FourStarMinimumTimeScore
                : DefaultFourMinTimeScore;

        float minMomentum =
            settings != null
                ? settings.FourStarMinimumMomentumScore
                : DefaultFourMinMomentum;

        float minClean =
            settings != null
                ? settings.FourStarMinimumCleanPlayScore
                : DefaultFourMinClean;

        return
            timeScore >= minTime &&
            momentumScore >= minMomentum &&
            cleanScore >= minClean;
    }

    private static bool PassesFiveStarGate(
        float timeScore,
        float momentumScore,
        float cleanScore,
        int deaths,
        CombatRankSettings settings)
    {
        if (deaths > 0)
            return false;

        float minTime =
            settings != null
                ? settings.FiveStarMinimumTimeScore
                : DefaultFiveMinTimeScore;

        float minMomentum =
            settings != null
                ? settings.FiveStarMinimumMomentumScore
                : DefaultFiveMinMomentum;

        float minClean =
            settings != null
                ? settings.FiveStarMinimumCleanPlayScore
                : DefaultFiveMinClean;

        return
            timeScore >= minTime &&
            momentumScore >= minMomentum &&
            cleanScore >= minClean;
    }

    private static bool PassesLegendaryGate(
        float timeRatio,
        float momentumScore,
        float cleanScore,
        float overallScore,
        int deaths,
        CombatRankSettings settings)
    {
        if (deaths > 0)
            return false;

        bool enabled =
            settings != null
                ? settings.EnableLegendaryRank
                : DefaultLegendaryEnabled;

        if (!enabled)
            return false;

        float maxTime =
            settings != null
                ? settings.LegendaryMaximumTimeRatio
                : DefaultLegendaryMaxTimeRatio;

        float minMomentum =
            settings != null
                ? settings.LegendaryMinimumMomentumScore
                : DefaultLegendaryMinMomentum;

        float minClean =
            settings != null
                ? settings.LegendaryMinimumCleanPlayScore
                : DefaultLegendaryMinClean;

        float minOverall =
            settings != null
                ? settings.LegendaryMinimumOverallScore
                : DefaultLegendaryMinOverall;

        return
            timeRatio <= maxTime &&
            momentumScore >= minMomentum &&
            cleanScore >= minClean &&
            overallScore >= minOverall;
    }

    private static int GetDeathStarCap(
        int deaths,
        CombatRankSettings settings)
    {
        if (deaths <= 0)
            return 5;

        if (deaths == 1)
        {
            return settings != null
                ? settings.OneDeathMaximumStars
                : DefaultOneDeathCap;
        }

        return settings != null
            ? settings.MultipleDeathsMaximumStars
            : DefaultMultipleDeathCap;
    }
}
