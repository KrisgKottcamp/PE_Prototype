using System;
using System.Collections.Generic;
using UnityEngine;

public class ProceduralArenaGenerator : MonoBehaviour
{
    private struct PlacedCircle
    {
        public Vector2 position;
        public float radius;

        public PlacedCircle(
            Vector2 position,
            float radius)
        {
            this.position = position;
            this.radius = radius;
        }
    }

    private class EnemyChoice
    {
        public ProceduralEncounterProfile.EnemyRule rule;
    }

    private class ObstacleChoice
    {
        public ProceduralEncounterProfile.ObstacleRule rule;
    }

    [Header("Arena Bounds")]
    [Tooltip(
        "Enemy centers and their footprints must fit inside this polygon."
    )]
    [SerializeField]
    private PolygonCollider2D enemySpawnBounds;

    [Tooltip(
        "Obstacle centers and their footprints must fit inside this polygon."
    )]
    [SerializeField]
    private PolygonCollider2D obstacleSpawnBounds;

    [Tooltip(
        "Extra scene regions that enemies and obstacles must avoid. " +
        "Use these for entrances, scripted areas, UI staging, or special props."
    )]
    [SerializeField]
    private Collider2D[] reservedZones;

    [Header("Navigation Bounds")]
    [Tooltip(
        "The full region that players and enemies may travel through. " +
        "This must include the player spawn, enemy spawn region, and all routes " +
        "around generated obstacles. It is separate from Enemy Spawn Bounds."
    )]
    [SerializeField]
    private Collider2D navigationBounds;

    [Header("Scene References")]
    [SerializeField]
    private Transform playerSpawn;

    [SerializeField]
    private SpriteRenderer backgroundRenderer;

    [SerializeField]
    private Transform generatedObstacleRoot;

    [SerializeField]
    private Transform generatedEnemyRoot;

    [Header("Navigation")]
    [SerializeField]
    private ArenaNavigationGrid navigationGrid;

    [Tooltip(
        "Fail generation when a navigation grid cannot be built."
    )]
    [SerializeField]
    private bool requireNavigationGrid = true;

    [Header("Collision")]
    [Tooltip(
        "Layers containing walls, fixed arena geometry, and generated obstacles."
    )]
    [SerializeField]
    private LayerMask blockingMask;

    [Header("Validation")]
    [SerializeField]
    private bool disableTacticalPointsInsideBlockingGeometry = true;

    [SerializeField]
    private bool logGeneration = true;

    [Header("Debug Gizmos")]
    [SerializeField]
    private bool drawPlacementBounds = true;

    [SerializeField]
    private Color enemyBoundsColor =
        new Color(1f, 0.25f, 0.25f, 0.55f);

    [SerializeField]
    private Color obstacleBoundsColor =
        new Color(0.25f, 0.75f, 1f, 0.55f);

    private readonly List<GameObject> generatedObjects = new();

    public ProceduralGenerationResult GenerateAndSpawn(
        ProceduralEncounterProfile profile,
        int requestedSeed)
    {
        ProceduralGenerationResult result =
            new ProceduralGenerationResult();

        if (!ValidateRequiredSetup(out string reason))
        {
            result.failureReason = reason;
            return result;
        }

        if (profile == null)
        {
            result.failureReason =
                "No Procedural Encounter Profile was provided.";

            return result;
        }

        int actualSeed =
            requestedSeed != 0
                ? requestedSeed
                : CreateRandomSeed();

        result.seed = actualSeed;

        System.Random random =
            new System.Random(actualSeed);

        EnsureGeneratedRoots();
        ClearGeneratedObjects();
        SelectBackground(profile, random);

        List<ObstacleChoice> obstacleChoices =
            BuildObstacleChoices(
                profile,
                random,
                out int requestedObstacleCount
            );

        result.requestedObstacleCount =
            requestedObstacleCount;

        List<PlacedCircle> obstacleCircles =
            new List<PlacedCircle>();

        for (int i = 0; i < obstacleChoices.Count; i++)
        {
            TryPlaceObstacle(
                obstacleChoices[i].rule,
                profile,
                random,
                obstacleCircles,
                result
            );
        }

        Physics2D.SyncTransforms();

        if (!BuildNavigationGrid(
                out string navigationFailure))
        {
            if (requireNavigationGrid)
            {
                result.failureReason =
                    navigationFailure;

                ClearGeneratedObjects();
                return result;
            }

            Debug.LogWarning(
                navigationFailure,
                this
            );
        }

        result.activeTacticalPointCount =
            ConfigureGeneratedTacticalPoints();

        if (result.spawnedObstacleCount <
            profile.minimumObstacleCount)
        {
            result.failureReason =
                $"Only placed {result.spawnedObstacleCount} obstacles, " +
                $"but the profile requires at least " +
                $"{profile.minimumObstacleCount}.";

            ClearGeneratedObjects();
            return result;
        }

        List<EnemyChoice> enemyChoices =
            BuildEnemyChoices(
                profile,
                random,
                result
            );

        if (enemyChoices.Count <
            profile.minimumEnemyCount)
        {
            result.failureReason =
                $"Only selected {enemyChoices.Count} enemies, " +
                $"but the profile requires at least " +
                $"{profile.minimumEnemyCount}.";

            ClearGeneratedObjects();
            return result;
        }

        List<PlacedCircle> enemyCircles =
            new List<PlacedCircle>();

        for (int i = 0; i < enemyChoices.Count; i++)
        {
            TryPlaceEnemy(
                enemyChoices[i].rule,
                profile,
                random,
                obstacleCircles,
                enemyCircles,
                result
            );
        }

        result.requestedEnemyCount =
            enemyChoices.Count;

        result.spawnedEnemyCount =
            result.spawnedEnemies.Count;

        if (result.spawnedEnemyCount <
            profile.minimumEnemyCount)
        {
            result.failureReason =
                $"Only spawned {result.spawnedEnemyCount} enemies, " +
                $"but the profile requires at least " +
                $"{profile.minimumEnemyCount}.";

            ClearGeneratedObjects();
            result.spawnedEnemies.Clear();
            return result;
        }

        result.success = true;

        if (logGeneration)
        {
            Debug.Log(
                $"Procedural encounter generated. " +
                $"Profile='{profile.displayName}', " +
                $"Seed={result.seed}, " +
                $"Enemies={result.spawnedEnemyCount}/" +
                $"{result.requestedEnemyCount}, " +
                $"Budget={result.usedDifficultyBudget}/" +
                $"{result.selectedDifficultyBudget}, " +
                $"Obstacles={result.spawnedObstacleCount}/" +
                $"{result.requestedObstacleCount}, " +
                $"TacticalPoints={result.activeTacticalPointCount}.",
                this
            );
        }

        return result;
    }

    [ContextMenu("Validate Arena Setup")]
    public void ValidateArenaSetup()
    {
        if (ValidateRequiredSetup(out string reason))
        {
            Debug.Log(
                "ProceduralArenaGenerator: Required arena references are valid.",
                this
            );
        }
        else
        {
            Debug.LogError(
                $"ProceduralArenaGenerator: {reason}",
                this
            );
        }
    }

    [ContextMenu("Clear Generated Objects")]
    public void ClearGeneratedObjects()
    {
        for (int i = generatedObjects.Count - 1;
             i >= 0;
             i--)
        {
            GameObject generated =
                generatedObjects[i];

            if (generated == null)
                continue;

            generated.SetActive(false);

            if (Application.isPlaying)
                Destroy(generated);
            else
                DestroyImmediate(generated);
        }

        generatedObjects.Clear();

        ClearRootChildren(generatedEnemyRoot);
        ClearRootChildren(generatedObstacleRoot);
    }

    private bool ValidateRequiredSetup(
        out string reason)
    {
        if (enemySpawnBounds == null)
        {
            reason = "Enemy Spawn Bounds is not assigned.";
            return false;
        }

        if (obstacleSpawnBounds == null)
        {
            reason = "Obstacle Spawn Bounds is not assigned.";
            return false;
        }

        if (navigationBounds == null)
        {
            reason =
                "Navigation Bounds is not assigned. " +
                "Assign a collider covering the full playable combat arena.";

            return false;
        }

        if (playerSpawn == null)
        {
            reason = "Player Spawn is not assigned.";
            return false;
        }

        reason = "";
        return true;
    }

    private void EnsureGeneratedRoots()
    {
        if (generatedObstacleRoot == null)
        {
            GameObject root =
                new GameObject("Generated Obstacles");

            root.transform.SetParent(
                transform,
                false
            );

            generatedObstacleRoot =
                root.transform;
        }

        if (generatedEnemyRoot == null)
        {
            GameObject root =
                new GameObject("Generated Enemies");

            root.transform.SetParent(
                transform,
                false
            );

            generatedEnemyRoot =
                root.transform;
        }
    }

    private void SelectBackground(
        ProceduralEncounterProfile profile,
        System.Random random)
    {
        if (backgroundRenderer == null ||
            profile.backgroundVariants == null ||
            profile.backgroundVariants.Length == 0)
        {
            return;
        }

        List<Sprite> validSprites =
            new List<Sprite>();

        for (int i = 0;
             i < profile.backgroundVariants.Length;
             i++)
        {
            if (profile.backgroundVariants[i] != null)
                validSprites.Add(
                    profile.backgroundVariants[i]
                );
        }

        if (validSprites.Count == 0)
            return;

        backgroundRenderer.sprite =
            validSprites[
                random.Next(0, validSprites.Count)
            ];
    }

    private List<ObstacleChoice> BuildObstacleChoices(
        ProceduralEncounterProfile profile,
        System.Random random,
        out int requestedCount)
    {
        requestedCount = NextInclusive(
            random,
            profile.minimumObstacleCount,
            profile.maximumObstacleCount
        );

        List<ObstacleChoice> choices =
            new List<ObstacleChoice>();

        Dictionary<
            ProceduralEncounterProfile.ObstacleRule,
            int
        > counts = new();

        if (profile.obstacleRules == null)
            return choices;

        for (int i = 0;
             i < profile.obstacleRules.Count;
             i++)
        {
            ProceduralEncounterProfile.ObstacleRule rule =
                profile.obstacleRules[i];

            if (!IsValidObstacleRule(rule))
                continue;

            counts[rule] = 0;

            for (int copy = 0;
                 copy < rule.minimumCount;
                 copy++)
            {
                choices.Add(
                    new ObstacleChoice
                    {
                        rule = rule
                    }
                );

                counts[rule]++;
            }
        }

        requestedCount =
            Mathf.Max(
                requestedCount,
                choices.Count
            );

        while (choices.Count < requestedCount)
        {
            List<
                ProceduralEncounterProfile.ObstacleRule
            > eligible =
                new List<
                    ProceduralEncounterProfile.ObstacleRule
                >();

            for (int i = 0;
                 i < profile.obstacleRules.Count;
                 i++)
            {
                ProceduralEncounterProfile.ObstacleRule rule =
                    profile.obstacleRules[i];

                if (!IsValidObstacleRule(rule))
                    continue;

                counts.TryGetValue(
                    rule,
                    out int currentCount
                );

                if (currentCount >= rule.maximumCount)
                    continue;

                eligible.Add(rule);
            }

            ProceduralEncounterProfile.ObstacleRule selected =
                ChooseWeighted(
                    eligible,
                    rule => rule.weight,
                    random
                );

            if (selected == null)
                break;

            choices.Add(
                new ObstacleChoice
                {
                    rule = selected
                }
            );

            counts.TryGetValue(
                selected,
                out int oldCount
            );

            counts[selected] =
                oldCount + 1;
        }

        Shuffle(choices, random);
        return choices;
    }

    private List<EnemyChoice> BuildEnemyChoices(
        ProceduralEncounterProfile profile,
        System.Random random,
        ProceduralGenerationResult result)
    {
        int targetCount =
            ChooseTargetEnemyCount(
                profile,
                random
            );

        int targetBudget = NextInclusive(
            random,
            profile.minimumDifficultyBudget,
            profile.maximumDifficultyBudget
        );

        result.selectedDifficultyBudget =
            targetBudget;

        List<EnemyChoice> choices =
            new List<EnemyChoice>();

        Dictionary<
            ProceduralEncounterProfile.EnemyRule,
            int
        > counts = new();

        if (profile.enemyRules == null)
            return choices;

        int usedBudget = 0;

        for (int i = 0;
             i < profile.enemyRules.Count;
             i++)
        {
            ProceduralEncounterProfile.EnemyRule rule =
                profile.enemyRules[i];

            if (!IsValidEnemyRule(rule))
                continue;

            counts[rule] = 0;

            for (int copy = 0;
                 copy < rule.minimumCount;
                 copy++)
            {
                choices.Add(
                    new EnemyChoice
                    {
                        rule = rule
                    }
                );

                counts[rule]++;
                usedBudget += rule.difficultyCost;
            }
        }

        targetCount =
            Mathf.Max(
                targetCount,
                choices.Count
            );

        int safety = 0;

        while (choices.Count < targetCount &&
               safety < 500)
        {
            safety++;

            List<
                ProceduralEncounterProfile.EnemyRule
            > eligible =
                new List<
                    ProceduralEncounterProfile.EnemyRule
                >();

            for (int i = 0;
                 i < profile.enemyRules.Count;
                 i++)
            {
                ProceduralEncounterProfile.EnemyRule rule =
                    profile.enemyRules[i];

                if (!IsValidEnemyRule(rule))
                    continue;

                counts.TryGetValue(
                    rule,
                    out int currentCount
                );

                if (currentCount >= rule.maximumCount)
                    continue;

                if (usedBudget + rule.difficultyCost >
                    targetBudget)
                {
                    continue;
                }

                eligible.Add(rule);
            }

            ProceduralEncounterProfile.EnemyRule selected =
                ChooseWeighted(
                    eligible,
                    rule => rule.weight,
                    random
                );

            if (selected == null)
                break;

            choices.Add(
                new EnemyChoice
                {
                    rule = selected
                }
            );

            counts.TryGetValue(
                selected,
                out int oldCount
            );

            counts[selected] =
                oldCount + 1;

            usedBudget +=
                selected.difficultyCost;
        }

        while (choices.Count <
               profile.minimumEnemyCount)
        {
            ProceduralEncounterProfile.EnemyRule cheapest =
                FindCheapestAvailableEnemyRule(
                    profile,
                    counts
                );

            if (cheapest == null)
                break;

            choices.Add(
                new EnemyChoice
                {
                    rule = cheapest
                }
            );

            counts.TryGetValue(
                cheapest,
                out int oldCount
            );

            counts[cheapest] =
                oldCount + 1;

            usedBudget +=
                cheapest.difficultyCost;
        }

        result.usedDifficultyBudget =
            usedBudget;

        Shuffle(choices, random);
        return choices;
    }

    private void TryPlaceObstacle(
        ProceduralEncounterProfile.ObstacleRule rule,
        ProceduralEncounterProfile profile,
        System.Random random,
        List<PlacedCircle> placedObstacles,
        ProceduralGenerationResult result)
    {
        if (!IsValidObstacleRule(rule))
            return;

        ProceduralObstacleDefinition definition =
            rule.obstacle;

        float radius =
            definition.footprintRadius +
            definition.extraSpacing;

        if (!TryFindPosition(
                obstacleSpawnBounds,
                radius,
                profile.playerSafeRadius,
                profile.placementAttemptsPerObject,
                random,
                placedObstacles,
                null,
                false,
                out Vector2 position))
        {
            return;
        }

        float zRotation =
            definition.GetRandomRotation(random);

        GameObject instance = Instantiate(
            definition.prefab,
            position,
            Quaternion.Euler(
                0f,
                0f,
                zRotation
            ),
            generatedObstacleRoot
        );

        generatedObjects.Add(instance);

        placedObstacles.Add(
            new PlacedCircle(
                position,
                radius
            )
        );

        result.spawnedObstacleCount++;

        if (definition.expectsTacticalPoints)
        {
            CombatTacticalPoint[] points =
                instance.GetComponentsInChildren<
                    CombatTacticalPoint
                >(true);

            if (points.Length <
                definition.minimumTacticalPointCount)
            {
                Debug.LogWarning(
                    $"Generated obstacle '{definition.name}' expected at least " +
                    $"{definition.minimumTacticalPointCount} tactical points, " +
                    $"but prefab '{definition.prefab.name}' contains {points.Length}.",
                    instance
                );
            }
        }
    }

    private void TryPlaceEnemy(
        ProceduralEncounterProfile.EnemyRule rule,
        ProceduralEncounterProfile profile,
        System.Random random,
        List<PlacedCircle> placedObstacles,
        List<PlacedCircle> placedEnemies,
        ProceduralGenerationResult result)
    {
        if (!IsValidEnemyRule(rule))
            return;

        float radius =
            rule.footprintRadius +
            profile.enemySpacing;

        float playerDistance =
            Mathf.Max(
                profile.playerSafeRadius,
                rule.minimumPlayerDistance
            );

        if (!TryFindPosition(
                enemySpawnBounds,
                radius,
                playerDistance,
                profile.placementAttemptsPerObject,
                random,
                placedObstacles,
                placedEnemies,
                true,
                out Vector2 position))
        {
            return;
        }

        GameObject enemyObject = Instantiate(
            rule.enemy.prefab,
            position,
            Quaternion.identity,
            generatedEnemyRoot
        );

        EnemyHealth health =
            enemyObject.GetComponent<EnemyHealth>();

        if (health == null)
        {
            Debug.LogWarning(
                $"Procedural enemy prefab '{rule.enemy.prefab.name}' " +
                $"does not contain EnemyHealth.",
                enemyObject
            );

            if (Application.isPlaying)
                Destroy(enemyObject);
            else
                DestroyImmediate(enemyObject);

            return;
        }

        health.Init(rule.enemy.maxHP);

        generatedObjects.Add(enemyObject);

        placedEnemies.Add(
            new PlacedCircle(
                position,
                radius
            )
        );

        result.spawnedEnemies.Add(health);
    }

    private int ConfigureGeneratedTacticalPoints()
    {
        if (generatedObstacleRoot == null)
            return 0;

        Physics2D.SyncTransforms();

        CombatTacticalPoint[] points =
            generatedObstacleRoot.GetComponentsInChildren<
                CombatTacticalPoint
            >(true);

        int activeCount = 0;

        for (int i = 0; i < points.Length; i++)
        {
            CombatTacticalPoint point =
                points[i];

            if (point == null)
                continue;

            point.losBlockMask =
                blockingMask;

            if (disableTacticalPointsInsideBlockingGeometry)
            {
                Collider2D hit =
                    Physics2D.OverlapPoint(
                        point.Position,
                        blockingMask
                    );

                if (hit != null)
                {
                    Debug.LogWarning(
                        $"Disabling tactical point '{point.name}' because its " +
                        $"center overlaps blocking collider '{hit.name}'.",
                        point
                    );

                    point.gameObject.SetActive(false);
                    continue;
                }
            }

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                bool walkable =
                    navigationGrid.IsPositionWalkable(
                        point.Position
                    );

                bool connected =
                    walkable &&
                    navigationGrid.AreConnected(
                        point.Position,
                        playerSpawn.position
                    );

                if (!walkable ||
                    !connected)
                {
                    Debug.LogWarning(
                        $"Disabling tactical point '{point.name}' because it is " +
                        $"not reachable on the generated navigation grid.",
                        point
                    );

                    point.gameObject.SetActive(false);
                    continue;
                }
            }

            if (point.gameObject.activeInHierarchy)
                activeCount++;
        }

        return activeCount;
    }

    private bool BuildNavigationGrid(
        out string failureReason)
    {
        failureReason = "";

        if (navigationGrid == null)
        {
            navigationGrid =
                GetComponent<
                    ArenaNavigationGrid
                >();
        }

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<
                    ArenaNavigationGrid
                >(true);
        }

        if (navigationGrid == null)
        {
            failureReason =
                "ProceduralArenaGenerator: No ArenaNavigationGrid exists in the arena.";

            return false;
        }

        navigationGrid.Configure(
            navigationBounds,
            blockingMask
        );

        if (!navigationGrid.BuildGrid())
        {
            failureReason =
                "ProceduralArenaGenerator: The navigation grid failed to build.";

            return false;
        }

        if (!navigationGrid.IsPositionWalkable(
                playerSpawn.position))
        {
            Vector2 nearest =
                navigationGrid.FindNearestWalkablePosition(
                    playerSpawn.position
                );

            float distance =
                Vector2.Distance(
                    playerSpawn.position,
                    nearest
                );

            failureReason =
                "ProceduralArenaGenerator: Player Spawn is not on a walkable " +
                "navigation cell. Make sure Navigation Bounds includes the player " +
                "spawn and that the spawn is not inside a collider on Blocking Mask. " +
                $"Nearest walkable position is {distance:0.00} units away.";

            return false;
        }

        return true;
    }

    private bool TryFindPosition(
        PolygonCollider2D area,
        float radius,
        float minimumPlayerDistance,
        int attempts,
        System.Random random,
        List<PlacedCircle> firstCircleSet,
        List<PlacedCircle> secondCircleSet,
        bool requireNavigationConnection,
        out Vector2 position)
    {
        Bounds bounds = area.bounds;

        for (int attempt = 0;
             attempt < attempts;
             attempt++)
        {
            Vector2 candidate =
                new Vector2(
                    Mathf.Lerp(
                        bounds.min.x,
                        bounds.max.x,
                        NextFloat(random)
                    ),
                    Mathf.Lerp(
                        bounds.min.y,
                        bounds.max.y,
                        NextFloat(random)
                    )
                );

            if (!CircleFitsInsideArea(
                    area,
                    candidate,
                    radius))
            {
                continue;
            }

            if (Vector2.Distance(
                    candidate,
                    playerSpawn.position) <
                minimumPlayerDistance + radius)
            {
                continue;
            }

            if (IntersectsReservedZone(
                    candidate,
                    radius))
            {
                continue;
            }

            if (blockingMask.value != 0 &&
                Physics2D.OverlapCircle(
                    candidate,
                    radius,
                    blockingMask) != null)
            {
                continue;
            }

            if (OverlapsPlacedCircles(
                    candidate,
                    radius,
                    firstCircleSet))
            {
                continue;
            }

            if (OverlapsPlacedCircles(
                    candidate,
                    radius,
                    secondCircleSet))
            {
                continue;
            }

            if (requireNavigationConnection &&
                navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                if (!navigationGrid.IsPositionWalkable(
                        candidate))
                {
                    continue;
                }

                if (!navigationGrid.AreConnected(
                        candidate,
                        playerSpawn.position))
                {
                    continue;
                }
            }

            position = candidate;
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    private bool CircleFitsInsideArea(
        PolygonCollider2D area,
        Vector2 center,
        float radius)
    {
        if (!area.OverlapPoint(center))
            return false;

        const int samples = 12;

        for (int i = 0; i < samples; i++)
        {
            float angle =
                Mathf.PI * 2f *
                (i / (float)samples);

            Vector2 edge =
                center +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) * radius;

            if (!area.OverlapPoint(edge))
                return false;
        }

        return true;
    }

    private bool IntersectsReservedZone(
        Vector2 center,
        float radius)
    {
        if (reservedZones == null)
            return false;

        for (int i = 0;
             i < reservedZones.Length;
             i++)
        {
            Collider2D zone =
                reservedZones[i];

            if (zone == null ||
                !zone.enabled)
            {
                continue;
            }

            if (zone.OverlapPoint(center))
                return true;

            Vector2 closest =
                zone.ClosestPoint(center);

            if ((closest - center).sqrMagnitude <
                radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    private static bool OverlapsPlacedCircles(
        Vector2 candidate,
        float candidateRadius,
        List<PlacedCircle> circles)
    {
        if (circles == null)
            return false;

        for (int i = 0; i < circles.Count; i++)
        {
            PlacedCircle other =
                circles[i];

            float combined =
                candidateRadius +
                other.radius;

            if ((candidate - other.position).sqrMagnitude <
                combined * combined)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidEnemyRule(
        ProceduralEncounterProfile.EnemyRule rule)
    {
        return
            rule != null &&
            rule.enemy != null &&
            rule.enemy.prefab != null &&
            rule.maximumCount > 0 &&
            rule.weight > 0f;
    }

    private static bool IsValidObstacleRule(
        ProceduralEncounterProfile.ObstacleRule rule)
    {
        return
            rule != null &&
            rule.obstacle != null &&
            rule.obstacle.prefab != null &&
            rule.maximumCount > 0 &&
            rule.weight > 0f;
    }

    private static T ChooseWeighted<T>(
        List<T> choices,
        Func<T, float> getWeight,
        System.Random random)
        where T : class
    {
        if (choices == null ||
            choices.Count == 0)
        {
            return null;
        }

        float totalWeight = 0f;

        for (int i = 0;
             i < choices.Count;
             i++)
        {
            totalWeight +=
                Mathf.Max(
                    0f,
                    getWeight(choices[i])
                );
        }

        if (totalWeight <= 0f)
            return null;

        float roll =
            NextFloat(random) *
            totalWeight;

        float running = 0f;

        for (int i = 0;
             i < choices.Count;
             i++)
        {
            running +=
                Mathf.Max(
                    0f,
                    getWeight(choices[i])
                );

            if (roll <= running)
                return choices[i];
        }

        return choices[
            choices.Count - 1
        ];
    }

    private static ProceduralEncounterProfile.EnemyRule
        FindCheapestAvailableEnemyRule(
            ProceduralEncounterProfile profile,
            Dictionary<
                ProceduralEncounterProfile.EnemyRule,
                int
            > counts)
    {
        ProceduralEncounterProfile.EnemyRule best =
            null;

        int bestCost = int.MaxValue;

        if (profile.enemyRules == null)
            return null;

        for (int i = 0;
             i < profile.enemyRules.Count;
             i++)
        {
            ProceduralEncounterProfile.EnemyRule rule =
                profile.enemyRules[i];

            if (!IsValidEnemyRule(rule))
                continue;

            counts.TryGetValue(
                rule,
                out int currentCount
            );

            if (currentCount >= rule.maximumCount)
                continue;

            if (rule.difficultyCost < bestCost)
            {
                best = rule;
                bestCost = rule.difficultyCost;
            }
        }

        return best;
    }

    private static void Shuffle<T>(
        List<T> list,
        System.Random random)
    {
        for (int i = list.Count - 1;
             i > 0;
             i--)
        {
            int swapIndex =
                random.Next(0, i + 1);

            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private static int ChooseTargetEnemyCount(
        ProceduralEncounterProfile profile,
        System.Random random)
    {
        if (!profile.useWeightedEnemyCount ||
            profile.enemyCountWeights == null ||
            profile.enemyCountWeights.Count == 0)
        {
            return NextInclusive(
                random,
                profile.minimumEnemyCount,
                profile.maximumEnemyCount
            );
        }

        float totalWeight = 0f;

        for (int i = 0;
             i < profile.enemyCountWeights.Count;
             i++)
        {
            ProceduralEncounterProfile.EnemyCountWeight entry =
                profile.enemyCountWeights[i];

            if (entry == null)
                continue;

            if (entry.enemyCount <
                    profile.minimumEnemyCount ||
                entry.enemyCount >
                    profile.maximumEnemyCount)
            {
                continue;
            }

            totalWeight +=
                Mathf.Max(
                    0f,
                    entry.weight
                );
        }

        if (totalWeight <= 0f)
        {
            return NextInclusive(
                random,
                profile.minimumEnemyCount,
                profile.maximumEnemyCount
            );
        }

        float roll =
            NextFloat(random) *
            totalWeight;

        float runningWeight = 0f;

        for (int i = 0;
             i < profile.enemyCountWeights.Count;
             i++)
        {
            ProceduralEncounterProfile.EnemyCountWeight entry =
                profile.enemyCountWeights[i];

            if (entry == null)
                continue;

            if (entry.enemyCount <
                    profile.minimumEnemyCount ||
                entry.enemyCount >
                    profile.maximumEnemyCount)
            {
                continue;
            }

            runningWeight +=
                Mathf.Max(
                    0f,
                    entry.weight
                );

            if (roll <= runningWeight)
                return entry.enemyCount;
        }

        return profile.maximumEnemyCount;
    }

    private static int NextInclusive(
        System.Random random,
        int minimum,
        int maximum)
    {
        if (maximum <= minimum)
            return minimum;

        return random.Next(
            minimum,
            maximum + 1
        );
    }

    private static float NextFloat(
        System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static int CreateRandomSeed()
    {
        unchecked
        {
            return
                Environment.TickCount *
                397 ^
                Guid.NewGuid().GetHashCode();
        }
    }

    private static void ClearRootChildren(
        Transform root)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1;
             i >= 0;
             i--)
        {
            GameObject child =
                root.GetChild(i).gameObject;

            child.SetActive(false);

            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPlacementBounds)
            return;

        if (enemySpawnBounds != null)
        {
            Gizmos.color = enemyBoundsColor;
            DrawPolygon(enemySpawnBounds);
        }

        if (obstacleSpawnBounds != null)
        {
            Gizmos.color = obstacleBoundsColor;
            DrawPolygon(obstacleSpawnBounds);
        }

        if (playerSpawn != null)
        {
            Gizmos.color =
                new Color(0.25f, 1f, 0.25f, 0.75f);

            Gizmos.DrawWireSphere(
                playerSpawn.position,
                0.5f
            );
        }
    }

    private static void DrawPolygon(
        PolygonCollider2D polygon)
    {
        if (polygon.pathCount <= 0)
            return;

        for (int path = 0;
             path < polygon.pathCount;
             path++)
        {
            Vector2[] points =
                polygon.GetPath(path);

            if (points.Length < 2)
                continue;

            for (int i = 0;
                 i < points.Length;
                 i++)
            {
                Vector3 a =
                    polygon.transform.TransformPoint(
                        points[i]
                    );

                Vector3 b =
                    polygon.transform.TransformPoint(
                        points[
                            (i + 1) %
                            points.Length
                        ]
                    );

                Gizmos.DrawLine(a, b);
            }
        }
    }
}
