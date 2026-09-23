using System;
using System.Collections.Generic;

/// <summary>Pure prototype rules; shared by runtime and deterministic checks.</summary>
public static class EriTurnRules
{
    public const int Segments = 4;
    public const int MaxMP = 100;
    public const float RecoverySeconds = 1.5f;
    public static bool CanTakeTurn(int index, int lastActor, int livingMembers) =>
        livingMembers <= 1 || index != lastActor;
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

/// <summary>Completed turns advance only that character's skill cooldowns.</summary>
public sealed class EriSkillCooldowns
{
    private readonly Dictionary<int, Dictionary<EriCommandKind, int>> byCharacter =
        new Dictionary<int, Dictionary<EriCommandKind, int>>();

    public int Remaining(int character, EriCommandKind skill) =>
        byCharacter.TryGetValue(character, out var cooldowns) && cooldowns.TryGetValue(skill, out int turns)
            ? turns : 0;

    public void CompleteTurn(int character, EriCommandKind? usedSkill, int cooldownTurns)
    {
        if (!byCharacter.TryGetValue(character, out var cooldowns))
            byCharacter[character] = cooldowns = new Dictionary<EriCommandKind, int>();
        var kinds = new List<EriCommandKind>(cooldowns.Keys);
        foreach (var kind in kinds)
        {
            int remaining = cooldowns[kind] - 1;
            if (remaining > 0) cooldowns[kind] = remaining;
            else cooldowns.Remove(kind);
        }
        if (usedSkill.HasValue && usedSkill.Value != EriCommandKind.Recover)
            cooldowns[usedSkill.Value] = Math.Max(1, cooldownTurns);
    }
}
