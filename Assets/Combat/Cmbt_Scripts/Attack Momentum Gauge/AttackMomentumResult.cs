using System;
using UnityEngine;

[Serializable]
public struct AttackMomentumResult
{
    public bool succeeded;
    public float encounterSeconds;

    [Range(0f, 1f)]
    public float averageMomentumNormalized;

    [Range(0f, 1f)]
    public float activeAverageMomentumNormalized;

    [Range(0f, 1f)]
    public float peakMomentumNormalized;

    public float activeMomentumScoringSeconds;
    public int chainBreakCount;
    public int qualifyingActionCount;

    public AttackMomentumResult(
        bool succeeded,
        float encounterSeconds,
        float averageMomentumNormalized,
        float activeAverageMomentumNormalized,
        float peakMomentumNormalized,
        float activeMomentumScoringSeconds,
        int chainBreakCount,
        int qualifyingActionCount)
    {
        this.succeeded = succeeded;
        this.encounterSeconds = Mathf.Max(0f, encounterSeconds);
        this.averageMomentumNormalized =
            Mathf.Clamp01(averageMomentumNormalized);
        this.activeAverageMomentumNormalized =
            Mathf.Clamp01(activeAverageMomentumNormalized);
        this.peakMomentumNormalized =
            Mathf.Clamp01(peakMomentumNormalized);
        this.activeMomentumScoringSeconds =
            Mathf.Max(0f, activeMomentumScoringSeconds);
        this.chainBreakCount = Mathf.Max(0, chainBreakCount);
        this.qualifyingActionCount =
            Mathf.Max(0, qualifyingActionCount);
    }
}
