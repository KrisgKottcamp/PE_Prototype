using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Enemy Personality Weights")]
public class EnemyPersonalityWeights : ScriptableObject
{
    [Serializable]
    public struct Weight
    {
        public EnemyPersonality personality;
        [Min(0f)] public float weight;
    }

    [Header("Default weights (used now for all enemies)")]
    public List<Weight> defaultWeights = new List<Weight>
    {
        new Weight { personality = EnemyPersonality.Aggressive,  weight = 1f },
        new Weight { personality = EnemyPersonality.ScaredCat,   weight = 1f },
        new Weight { personality = EnemyPersonality.Backstabber, weight = 1f },
        new Weight { personality = EnemyPersonality.Avenger,     weight = 1f },
    };

    [Header("Later: optional per-type overrides")]
    public List<PerTypeOverride> perTypeOverrides = new List<PerTypeOverride>();

    [Serializable]
    public class PerTypeOverride
    {
        public UnityEngine.Object enemyTypeKey;
        public List<Weight> weights = new List<Weight>();
    }

    public EnemyPersonality Roll(System.Random rng, UnityEngine.Object enemyTypeKey = null)
    {
        var list = GetList(enemyTypeKey);
        float total = 0f;

        for (int i = 0; i < list.Count; i++)
            total += Mathf.Max(0f, list[i].weight);

        if (total <= 0.0001f)
            return EnemyPersonality.Aggressive;

        double r = rng.NextDouble() * total;
        float acc = 0f;

        for (int i = 0; i < list.Count; i++)
        {
            acc += Mathf.Max(0f, list[i].weight);
            if (r <= acc)
                return list[i].personality;
        }

        return list[list.Count - 1].personality;
    }

    private List<Weight> GetList(UnityEngine.Object enemyTypeKey)
    {
        if (enemyTypeKey != null)
        {
            for (int i = 0; i < perTypeOverrides.Count; i++)
            {
                if (perTypeOverrides[i] != null && perTypeOverrides[i].enemyTypeKey == enemyTypeKey)
                    return perTypeOverrides[i].weights;
            }
        }
        return defaultWeights;
    }
}
