using System;
using UnityEngine;

[Serializable]
public struct CombatRankResult
{
    [Range(1, 5)]
    public int stars;

    [Range(1, 5)]
    public int uncappedStars;

    [Range(1, 5)]
    public int deathStarCap;

    public bool isLegendary;

    [Range(0f, 1f)]
    public float overallScore01;

    [Range(0f, 1f)]
    public float timeScore01;

    [Range(0f, 1f)]
    public float momentumScore01;

    [Range(0f, 1f)]
    public float cleanPlayScore01;

    public float actualTimeSeconds;
    public float targetTimeSeconds;
    public float timeRatio;

    public int totalDamageTaken;
    public int combinedPartyMaxHP;
    public int partyMemberDeaths;

    public bool passedFourStarGate;
    public bool passedFiveStarGate;
    public bool passedLegendaryGate;

    public CombatRankResult(
        int stars,
        int uncappedStars,
        int deathStarCap,
        bool isLegendary,
        float overallScore01,
        float timeScore01,
        float momentumScore01,
        float cleanPlayScore01,
        float actualTimeSeconds,
        float targetTimeSeconds,
        int totalDamageTaken,
        int combinedPartyMaxHP,
        int partyMemberDeaths,
        bool passedFourStarGate,
        bool passedFiveStarGate,
        bool passedLegendaryGate)
    {
        this.stars = Mathf.Clamp(stars, 1, 5);
        this.uncappedStars = Mathf.Clamp(uncappedStars, 1, 5);
        this.deathStarCap = Mathf.Clamp(deathStarCap, 1, 5);
        this.isLegendary = isLegendary;

        this.overallScore01 = Mathf.Clamp01(overallScore01);
        this.timeScore01 = Mathf.Clamp01(timeScore01);
        this.momentumScore01 = Mathf.Clamp01(momentumScore01);
        this.cleanPlayScore01 = Mathf.Clamp01(cleanPlayScore01);

        this.actualTimeSeconds = Mathf.Max(0f, actualTimeSeconds);
        this.targetTimeSeconds = Mathf.Max(0.01f, targetTimeSeconds);
        this.timeRatio =
            this.actualTimeSeconds / this.targetTimeSeconds;

        this.totalDamageTaken = Mathf.Max(0, totalDamageTaken);
        this.combinedPartyMaxHP =
            Mathf.Max(1, combinedPartyMaxHP);
        this.partyMemberDeaths =
            Mathf.Max(0, partyMemberDeaths);

        this.passedFourStarGate = passedFourStarGate;
        this.passedFiveStarGate = passedFiveStarGate;
        this.passedLegendaryGate = passedLegendaryGate;
    }
}
