using System;

/// <summary>Pure prototype rules; shared by runtime and deterministic checks.</summary>
public static class EriTurnRules
{
    public const int Segments = 4;
    public const int MaxMP = 100;
    public const float RecoverySeconds = 4f;
    public static int Capacity(int maxAP, int exhausted) =>
        Math.Max(0, maxAP) * (Segments - Math.Clamp(exhausted, 0, Segments)) / Segments;
    public static int Cost(int maxAP, int segments, int exhausted = 0) =>
        Capacity(maxAP, exhausted) - Capacity(maxAP, exhausted + Math.Clamp(segments, 0, Segments));
    public static string Unavailable(int hp, int ap, int mp, int exhausted,
        int maxAP, int segments, int mpCost, float recovery)
    {
        if (hp <= 0) return "Character defeated";
        if (recovery > 0.001f) return "Command recovering";
        if (segments > Segments - exhausted) return "Capacity exhausted — switch or Recover";
        if (ap < Cost(maxAP, segments, exhausted)) return "Build AP with basic attacks";
        if (mp < mpCost) return "Not enough MP — use a potion or rest";
        return string.Empty;
    }
    public static int RestoreSegment(int exhausted) => Math.Max(0, exhausted - 1);
}
