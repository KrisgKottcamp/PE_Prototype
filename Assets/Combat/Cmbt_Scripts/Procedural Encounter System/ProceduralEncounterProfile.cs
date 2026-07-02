using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    menuName = "Game/Combat/Procedural Encounter Profile",
    fileName = "ProceduralEncounterProfile"
)]
public class ProceduralEncounterProfile : ScriptableObject
{
    [Serializable]
    public class EnemyCountWeight
    {
        [Min(1)]
        public int enemyCount = 1;

        [Tooltip(
            "Relative chance of selecting this enemy count. " +
            "A weight of 4 is four times as likely as a weight of 1."
        )]
        [Min(0f)]
        public float weight = 1f;
    }

    [Serializable]
    public class EnemyRule
    {
        public EnemyDefinition enemy;

        [Tooltip(
            "Relative chance that this enemy is selected."
        )]
        [Min(0f)]
        public float weight = 1f;

        [Tooltip(
            "Approximate encounter difficulty contributed by one copy."
        )]
        [Min(1)]
        public int difficultyCost = 1;

        [Min(0)]
        public int minimumCount = 0;

        [Min(0)]
        public int maximumCount = 3;

        [Tooltip(
            "Circular footprint used to prevent enemy spawn overlap."
        )]
        [Min(0.05f)]
        public float footprintRadius = 0.55f;

        [Tooltip(
            "This enemy cannot initially spawn closer than this distance " +
            "to the player's combat spawn."
        )]
        [Min(0f)]
        public float minimumPlayerDistance = 3f;
    }

    [Serializable]
    public class ObstacleRule
    {
        public ProceduralObstacleDefinition obstacle;

        [Tooltip(
            "Relative chance that this obstacle is selected."
        )]
        [Min(0f)]
        public float weight = 1f;

        [Min(0)]
        public int minimumCount = 0;

        [Min(0)]
        public int maximumCount = 3;
    }

    [Header("Profile Identity")]
    public string displayName = "New Encounter Profile";

    [TextArea]
    public string designerNotes;

    [Header("Background Variants")]
    [Tooltip(
        "One compatible flat prerendered arena background is selected."
    )]
    public Sprite[] backgroundVariants;

    [Header("Enemy Count")]
    [Min(1)]
    public int minimumEnemyCount = 3;

    [Min(1)]
    public int maximumEnemyCount = 5;

    [Header("Enemy Count Distribution")]
    [Tooltip(
        "When enabled, enemy count is selected from the weighted list below " +
        "instead of giving every count between Minimum and Maximum equal odds."
    )]
    public bool useWeightedEnemyCount = true;

    [Tooltip(
        "Only entries inside the Minimum and Maximum Enemy Count range are used."
    )]
    public List<EnemyCountWeight> enemyCountWeights =
        new List<EnemyCountWeight>
        {
            new EnemyCountWeight
            {
                enemyCount = 1,
                weight = 1f
            },
            new EnemyCountWeight
            {
                enemyCount = 2,
                weight = 4f
            },
            new EnemyCountWeight
            {
                enemyCount = 3,
                weight = 4f
            },
            new EnemyCountWeight
            {
                enemyCount = 4,
                weight = 1f
            }
        };

    [Header("Difficulty Budget")]
    [Min(1)]
    public int minimumDifficultyBudget = 4;

    [Min(1)]
    public int maximumDifficultyBudget = 8;

    [Header("Enemy Pool")]
    public List<EnemyRule> enemyRules = new();

    [Header("Obstacle Count")]
    [Min(0)]
    public int minimumObstacleCount = 2;

    [Min(0)]
    public int maximumObstacleCount = 5;

    [Header("Obstacle Pool")]
    public List<ObstacleRule> obstacleRules = new();

    [Header("Placement Safety")]
    [Tooltip(
        "Universal empty radius around the player's combat spawn."
    )]
    [Min(0f)]
    public float playerSafeRadius = 3f;

    [Tooltip(
        "Additional spacing placed between enemy footprints."
    )]
    [Min(0f)]
    public float enemySpacing = 0.3f;

    [Tooltip(
        "Maximum random placement attempts for each object."
    )]
    [Min(10)]
    public int placementAttemptsPerObject = 100;

    [Header("Failure Handling")]
    [Tooltip(
        "When procedural generation fails, CombatManager may use the " +
        "manual enemies stored in CombatContext."
    )]
    public bool fallbackToManualEncounter = true;

    private void OnValidate()
    {
        minimumEnemyCount =
            Mathf.Max(1, minimumEnemyCount);

        maximumEnemyCount =
            Mathf.Max(
                minimumEnemyCount,
                maximumEnemyCount
            );

        if (enemyCountWeights != null)
        {
            for (int i = 0;
                 i < enemyCountWeights.Count;
                 i++)
            {
                EnemyCountWeight entry =
                    enemyCountWeights[i];

                if (entry == null)
                    continue;

                entry.enemyCount =
                    Mathf.Max(
                        1,
                        entry.enemyCount
                    );

                entry.weight =
                    Mathf.Max(
                        0f,
                        entry.weight
                    );
            }
        }

        minimumDifficultyBudget =
            Mathf.Max(1, minimumDifficultyBudget);

        maximumDifficultyBudget =
            Mathf.Max(
                minimumDifficultyBudget,
                maximumDifficultyBudget
            );

        minimumObstacleCount =
            Mathf.Max(0, minimumObstacleCount);

        maximumObstacleCount =
            Mathf.Max(
                minimumObstacleCount,
                maximumObstacleCount
            );

        playerSafeRadius =
            Mathf.Max(0f, playerSafeRadius);

        enemySpacing =
            Mathf.Max(0f, enemySpacing);

        placementAttemptsPerObject =
            Mathf.Max(10, placementAttemptsPerObject);

        if (enemyRules != null)
        {
            for (int i = 0; i < enemyRules.Count; i++)
            {
                EnemyRule rule = enemyRules[i];

                if (rule == null)
                    continue;

                rule.weight = Mathf.Max(0f, rule.weight);
                rule.difficultyCost =
                    Mathf.Max(1, rule.difficultyCost);

                rule.minimumCount =
                    Mathf.Max(0, rule.minimumCount);

                rule.maximumCount =
                    Mathf.Max(
                        rule.minimumCount,
                        rule.maximumCount
                    );

                rule.footprintRadius =
                    Mathf.Max(0.05f, rule.footprintRadius);

                rule.minimumPlayerDistance =
                    Mathf.Max(
                        0f,
                        rule.minimumPlayerDistance
                    );
            }
        }

        if (obstacleRules != null)
        {
            for (int i = 0; i < obstacleRules.Count; i++)
            {
                ObstacleRule rule = obstacleRules[i];

                if (rule == null)
                    continue;

                rule.weight = Mathf.Max(0f, rule.weight);

                rule.minimumCount =
                    Mathf.Max(0, rule.minimumCount);

                rule.maximumCount =
                    Mathf.Max(
                        rule.minimumCount,
                        rule.maximumCount
                    );
            }
        }
    }
}
