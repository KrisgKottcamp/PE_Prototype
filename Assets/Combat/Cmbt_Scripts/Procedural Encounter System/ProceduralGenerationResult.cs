using System.Collections.Generic;

public class ProceduralGenerationResult
{
    public bool success;
    public int seed;
    public string failureReason;

    public int selectedDifficultyBudget;
    public int usedDifficultyBudget;

    public int requestedEnemyCount;
    public int spawnedEnemyCount;

    public int requestedObstacleCount;
    public int spawnedObstacleCount;

    public int activeTacticalPointCount;

    public readonly List<EnemyHealth> spawnedEnemies = new();
}
