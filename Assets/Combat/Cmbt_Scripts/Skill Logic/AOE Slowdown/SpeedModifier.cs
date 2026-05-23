using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks active speed-modifying effects on this entity.
/// Movement scripts (CombatPawnMover, EnemyBrain) read Multiplier to scale their speed.
/// Added/removed automatically by SlowZone. Supports multiple simultaneous sources;
/// uses the strongest slow (lowest multiplier).
/// Self-destructs when all sources are removed.
/// </summary>
public class SpeedModifier : MonoBehaviour
{
    private readonly Dictionary<int, float> activeSources = new();
    private static int nextSourceId;

    /// <summary>Current speed multiplier (0..1). 1 = full speed, 0.4 = 40% speed.</summary>
    public float Multiplier { get; private set; } = 1f;

    /// <summary>Returns a unique source ID. Call once per SlowZone instance.</summary>
    public static int GenerateSourceId() => ++nextSourceId;

    public void ApplySlow(int sourceId, float multiplier)
    {
        activeSources[sourceId] = Mathf.Clamp01(multiplier);
        Recalculate();
    }

    public void RemoveSlow(int sourceId)
    {
        activeSources.Remove(sourceId);
        Recalculate();

        if (activeSources.Count == 0)
            Destroy(this);
    }

    private void Recalculate()
    {
        float min = 1f;
        foreach (var kv in activeSources)
            if (kv.Value < min) min = kv.Value;
        Multiplier = min;
    }
}