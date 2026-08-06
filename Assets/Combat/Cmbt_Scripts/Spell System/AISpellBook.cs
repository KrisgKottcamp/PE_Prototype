using System.Collections.Generic;
using UnityEngine;

public class AISpellBook : MonoBehaviour
{
    [System.Serializable]
    public class SpellEntry
    {
        public SpellDefinition spell;
        [Range(0f, 1f)]
        public float baseProbability = 0.5f;
        [Tooltip("If true, the AI prefers this spell at its preferred range.")]
        public bool rangePreferred = true;
    }

    [Header("Spell List")]
    [SerializeField] private List<SpellEntry> spells = new();

    [Header("Selection Tuning")]
    [Tooltip("How much range matching boosts probability (multiplier).")]
    [SerializeField] private float rangeBonus = 1.5f;
    [Tooltip("How much recent use reduces probability (multiplier per use).")]
    [SerializeField] private float recentUsePenalty = 0.5f;
    [Tooltip("Usage history decays after this many seconds.")]
    [SerializeField] private float historyDecaySeconds = 10f;

    [Header("References")]
    [SerializeField] private SpellCaster caster;
    [SerializeField] private EnemyTargetingSystem targeting;

    private readonly Dictionary<SpellDefinition, List<float>> usageHistory = new();

    public IReadOnlyList<SpellEntry> Spells => spells;

    private void Awake()
    {
        if (caster == null)
            caster = GetComponent<SpellCaster>();
        if (targeting == null)
            targeting = GetComponent<EnemyTargetingSystem>();
    }

    public SpellDefinition SelectSpell(float distanceToTarget)
    {
        PruneHistory();

        float totalWeight = 0f;
        SpellDefinition best = null;
        float bestWeight = -1f;

        for (int i = 0; i < spells.Count; i++)
        {
            SpellEntry entry = spells[i];
            if (entry.spell == null)
                continue;

            if (!caster.CanCast(entry.spell))
                continue;

            float weight = entry.baseProbability;

            // Range bonus
            if (entry.rangePreferred)
            {
                float rangeDiff = Mathf.Abs(distanceToTarget - entry.spell.preferredRange);
                float rangeFactor = Mathf.Clamp01(1f - rangeDiff / Mathf.Max(1f, entry.spell.preferredRange));
                weight *= Mathf.Lerp(1f, rangeBonus, rangeFactor);
            }

            // Recent usage penalty
            int recentUses = GetRecentUseCount(entry.spell);
            for (int u = 0; u < recentUses; u++)
                weight *= recentUsePenalty;

            totalWeight += weight;

            if (weight > bestWeight)
            {
                bestWeight = weight;
                best = entry.spell;
            }
        }

        if (totalWeight <= 0f)
            return null;

        // Weighted random selection
        float roll = Random.Range(0f, totalWeight);
        float running = 0f;

        for (int i = 0; i < spells.Count; i++)
        {
            SpellEntry entry = spells[i];
            if (entry.spell == null || !caster.CanCast(entry.spell))
                continue;

            float weight = CalculateWeight(entry, distanceToTarget);
            running += weight;

            if (roll <= running)
            {
                RecordUsage(entry.spell);
                return entry.spell;
            }
        }

        if (best != null)
            RecordUsage(best);

        return best;
    }

    public bool TryCastSpell(SpellDefinition spell)
    {
        if (caster == null || targeting == null || spell == null)
            return false;

        Vector2 aimDir = targeting.GetAimDirection(spell.aiTargetingMode);
        return caster.Cast(spell, aimDir);
    }

    public bool TryCastBestSpell(float distanceToTarget)
    {
        SpellDefinition spell = SelectSpell(distanceToTarget);
        if (spell == null)
            return false;

        return TryCastSpell(spell);
    }

    public bool ShouldDefend()
    {
        if (caster == null || !caster.IsCasting)
            return false;

        if (caster.ActiveSpell == null)
            return false;

        return caster.CurrentPhase == SpellPhase.BuildUp ||
               caster.CurrentPhase == SpellPhase.Channeling;
    }

    private float CalculateWeight(SpellEntry entry, float distanceToTarget)
    {
        float weight = entry.baseProbability;

        if (entry.rangePreferred)
        {
            float rangeDiff = Mathf.Abs(distanceToTarget - entry.spell.preferredRange);
            float rangeFactor = Mathf.Clamp01(1f - rangeDiff / Mathf.Max(1f, entry.spell.preferredRange));
            weight *= Mathf.Lerp(1f, rangeBonus, rangeFactor);
        }

        int recentUses = GetRecentUseCount(entry.spell);
        for (int u = 0; u < recentUses; u++)
            weight *= recentUsePenalty;

        return Mathf.Max(0f, weight);
    }

    private void RecordUsage(SpellDefinition spell)
    {
        if (!usageHistory.TryGetValue(spell, out List<float> timestamps))
        {
            timestamps = new List<float>();
            usageHistory[spell] = timestamps;
        }

        timestamps.Add(Time.time);
    }

    private int GetRecentUseCount(SpellDefinition spell)
    {
        if (!usageHistory.TryGetValue(spell, out List<float> timestamps))
            return 0;

        return timestamps.Count;
    }

    private void PruneHistory()
    {
        float cutoff = Time.time - historyDecaySeconds;
        List<SpellDefinition> emptyKeys = null;

        foreach (var kvp in usageHistory)
        {
            kvp.Value.RemoveAll(t => t < cutoff);

            if (kvp.Value.Count == 0)
            {
                emptyKeys ??= new List<SpellDefinition>();
                emptyKeys.Add(kvp.Key);
            }
        }

        if (emptyKeys != null)
        {
            for (int i = 0; i < emptyKeys.Count; i++)
                usageHistory.Remove(emptyKeys[i]);
        }
    }
}
