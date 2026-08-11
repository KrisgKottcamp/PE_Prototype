using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks active movement-speed effects on this entity.
///
/// Slow sources use the strongest slow, meaning the lowest multiplier.
/// Boost sources use the strongest boost, meaning the highest multiplier.
/// The final movement multiplier is:
///
///     strongest slow * strongest boost
///
/// Examples:
/// - No effects: 1.0
/// - 0.5 slow: 0.5
/// - 1.35 boost: 1.35
/// - 0.5 slow and 1.35 boost: 0.675
///
/// Movement scripts such as CombatPawnMover and EnemyBrain read Multiplier.
/// </summary>
public class SpeedModifier : MonoBehaviour
{
    private readonly Dictionary<int, float> activeSlows = new();
    private readonly Dictionary<int, float> activeBoosts = new();

    private static int nextSourceId;

    /// <summary>The final combined movement-speed multiplier.</summary>
    public float Multiplier { get; private set; } = 1f;

    /// <summary>The strongest currently active slow.</summary>
    public float SlowMultiplier { get; private set; } = 1f;

    /// <summary>The strongest currently active boost.</summary>
    public float BoostMultiplier { get; private set; } = 1f;

    /// <summary>Returns a unique source ID for a slow, boost, or other timed effect.</summary>
    public static int GenerateSourceId()
    {
        nextSourceId++;

        // Avoid returning zero after an integer overflow.
        if (nextSourceId == 0)
            nextSourceId++;

        return nextSourceId;
    }

    public void ApplySlow(int sourceId, float multiplier)
    {
        activeSlows[sourceId] = Mathf.Clamp(multiplier, 0f, 1f);
        Recalculate();
    }

    public void RemoveSlow(int sourceId)
    {
        activeSlows.Remove(sourceId);
        Recalculate();
        DestroyIfUnused();
    }

    public void ApplyBoost(int sourceId, float multiplier)
    {
        activeBoosts[sourceId] = Mathf.Max(1f, multiplier);
        Recalculate();
    }

    /// <summary>
    /// Atomically assigns one signed movement-speed change to a source.
    /// Values below one slow, values above one boost, and one clears it.
    /// </summary>
    public void ApplyMovementSpeedChange(int sourceId, float multiplier)
    {
        activeSlows.Remove(sourceId);
        activeBoosts.Remove(sourceId);

        float safeMultiplier = Mathf.Clamp(multiplier, 0.05f, 5f);
        if (safeMultiplier < 1f)
            activeSlows[sourceId] = safeMultiplier;
        else if (safeMultiplier > 1f)
            activeBoosts[sourceId] = safeMultiplier;

        Recalculate();
    }

    public void RemoveBoost(int sourceId)
    {
        activeBoosts.Remove(sourceId);
        Recalculate();
        DestroyIfUnused();
    }

    public void RemoveSource(int sourceId)
    {
        activeSlows.Remove(sourceId);
        activeBoosts.Remove(sourceId);
        Recalculate();
        DestroyIfUnused();
    }

    private void Recalculate()
    {
        float strongestSlow = 1f;

        foreach (KeyValuePair<int, float> source in activeSlows)
        {
            if (source.Value < strongestSlow)
                strongestSlow = source.Value;
        }

        float strongestBoost = 1f;

        foreach (KeyValuePair<int, float> source in activeBoosts)
        {
            if (source.Value > strongestBoost)
                strongestBoost = source.Value;
        }

        SlowMultiplier = strongestSlow;
        BoostMultiplier = strongestBoost;
        Multiplier = strongestSlow * strongestBoost;
    }

    private void DestroyIfUnused()
    {
        if (activeSlows.Count == 0 && activeBoosts.Count == 0)
            Destroy(this);
    }
}
