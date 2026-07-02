using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight runtime A* navigation for Project Eri combat arenas.
///
/// The grid is rebuilt after procedural obstacles are placed and before
/// enemies are spawned. EnemyBrain can still fall back to its original
/// steering when no built grid exists.
/// </summary>
[DisallowMultipleComponent]
public class ArenaNavigationGrid : MonoBehaviour
{
    private sealed class Node
    {
        public int x;
        public int y;
        public int index;
        public Vector2 world;
        public bool walkable;
    }

    [Header("Grid Source")]
    [SerializeField]
    private Collider2D walkableBounds;

    [SerializeField]
    private LayerMask blockingMask;

    [Header("Agent Clearance")]
    [SerializeField, Min(0.1f)]
    private float cellSize = 0.4f;

    [Tooltip(
        "Obstacle clearance used while building and smoothing paths. " +
        "Set this close to the normal enemy collider radius."
    )]
    [SerializeField, Min(0.05f)]
    private float agentRadius = 0.42f;

    [Header("Movement")]
    [SerializeField]
    private bool allowDiagonalMovement = true;

    [SerializeField]
    private bool preventDiagonalCornerCutting = true;

    [Header("Path Search")]
    [SerializeField, Min(100)]
    private int maximumExpandedNodes = 12000;

    [SerializeField, Min(1)]
    private int nearestWalkableSearchRadius = 6;

    [Header("Debug")]
    [SerializeField]
    private bool drawGrid = false;

    [SerializeField]
    private bool drawBlockedCells = false;

    [SerializeField]
    private bool drawLastPath = true;

    [SerializeField]
    private string debugBuildStatus = "Not Built";

    [SerializeField]
    private int debugWalkableCells;

    [SerializeField]
    private int debugBlockedCells;

    private Node[] nodes;
    private int width;
    private int height;
    private Vector2 origin;
    private Bounds gridBounds;
    private bool isBuilt;

    private readonly List<Vector2> lastDebugPath =
        new List<Vector2>();

    public bool IsBuilt => isBuilt;
    public float AgentRadius => agentRadius;
    public float CellSize => cellSize;
    public Collider2D WalkableBounds => walkableBounds;

    public void Configure(
        Collider2D bounds,
        LayerMask obstacleMask)
    {
        walkableBounds = bounds;
        blockingMask = obstacleMask;
    }

    [ContextMenu("Build Navigation Grid")]
    public bool BuildGrid()
    {
        isBuilt = false;
        nodes = null;
        lastDebugPath.Clear();

        if (walkableBounds == null)
        {
            debugBuildStatus =
                "Failed: Walkable Bounds is missing.";

            Debug.LogError(
                $"ArenaNavigationGrid: {debugBuildStatus}",
                this
            );

            return false;
        }

        Physics2D.SyncTransforms();

        gridBounds = walkableBounds.bounds;

        cellSize = Mathf.Max(0.1f, cellSize);
        agentRadius = Mathf.Max(0.05f, agentRadius);

        width = Mathf.Max(
            1,
            Mathf.CeilToInt(
                gridBounds.size.x / cellSize
            )
        );

        height = Mathf.Max(
            1,
            Mathf.CeilToInt(
                gridBounds.size.y / cellSize
            )
        );

        origin = new Vector2(
            gridBounds.min.x,
            gridBounds.min.y
        );

        nodes = new Node[width * height];

        debugWalkableCells = 0;
        debugBlockedCells = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y);

                Vector2 world =
                    origin +
                    new Vector2(
                        (x + 0.5f) * cellSize,
                        (y + 0.5f) * cellSize
                    );

                bool inside =
                    CircleFitsInsideBounds(
                        world,
                        agentRadius
                    );

                bool blocked =
                    inside &&
                    blockingMask.value != 0 &&
                    Physics2D.OverlapCircle(
                        world,
                        agentRadius,
                        blockingMask
                    ) != null;

                bool walkable =
                    inside && !blocked;

                nodes[index] =
                    new Node
                    {
                        x = x,
                        y = y,
                        index = index,
                        world = world,
                        walkable = walkable
                    };

                if (walkable)
                    debugWalkableCells++;
                else
                    debugBlockedCells++;
            }
        }

        isBuilt =
            debugWalkableCells > 0;

        debugBuildStatus =
            isBuilt
                ? $"Built {width}x{height}, " +
                  $"{debugWalkableCells} walkable"
                : "Failed: no walkable cells.";

        if (isBuilt)
        {
            Debug.Log(
                $"ArenaNavigationGrid: {debugBuildStatus}",
                this
            );
        }
        else
        {
            Debug.LogError(
                $"ArenaNavigationGrid: {debugBuildStatus}",
                this
            );
        }

        return isBuilt;
    }

    public bool IsPositionWalkable(
        Vector2 worldPosition)
    {
        if (!isBuilt)
            return false;

        if (!TryGetNearestWalkableIndex(
                worldPosition,
                out int index))
        {
            return false;
        }

        float tolerance =
            cellSize *
            Mathf.Max(
                1f,
                nearestWalkableSearchRadius
            );

        return Vector2.Distance(
            nodes[index].world,
            worldPosition
        ) <= tolerance;
    }

    public Vector2 FindNearestWalkablePosition(
        Vector2 worldPosition)
    {
        if (!isBuilt ||
            !TryGetNearestWalkableIndex(
                worldPosition,
                out int index))
        {
            return worldPosition;
        }

        return nodes[index].world;
    }

    public bool AreConnected(
        Vector2 start,
        Vector2 end)
    {
        List<Vector2> scratch =
            new List<Vector2>();

        return TryFindPath(
            start,
            end,
            scratch,
            false
        );
    }

    public float EstimatePathCost(
        Vector2 start,
        Vector2 end)
    {
        List<Vector2> scratch =
            new List<Vector2>();

        if (!TryFindPath(
                start,
                end,
                scratch,
                false))
        {
            return float.PositiveInfinity;
        }

        if (scratch.Count == 0)
            return Vector2.Distance(start, end);

        float total = 0f;
        Vector2 previous = start;

        for (int i = 0; i < scratch.Count; i++)
        {
            total += Vector2.Distance(
                previous,
                scratch[i]
            );

            previous = scratch[i];
        }

        return total;
    }

    public bool TryFindPath(
        Vector2 start,
        Vector2 end,
        List<Vector2> output)
    {
        return TryFindPath(
            start,
            end,
            output,
            true
        );
    }

    public bool HasClearPath(
        Vector2 start,
        Vector2 end)
    {
        Vector2 delta = end - start;
        float distance = delta.magnitude;

        if (distance <= 0.001f)
            return true;

        if (blockingMask.value == 0)
            return true;

        RaycastHit2D hit =
            Physics2D.CircleCast(
                start,
                agentRadius * 0.9f,
                delta / distance,
                distance,
                blockingMask
            );

        return hit.collider == null;
    }

    private bool TryFindPath(
        Vector2 start,
        Vector2 end,
        List<Vector2> output,
        bool storeDebugPath)
    {
        output.Clear();

        if (!isBuilt ||
            nodes == null ||
            nodes.Length == 0)
        {
            return false;
        }

        if (!TryGetNearestWalkableIndex(
                start,
                out int startIndex) ||
            !TryGetNearestWalkableIndex(
                end,
                out int endIndex))
        {
            return false;
        }

        if (startIndex == endIndex)
        {
            output.Add(end);

            if (storeDebugPath)
                StoreDebugPath(output);

            return true;
        }

        int count = nodes.Length;

        float[] gScore =
            new float[count];

        float[] fScore =
            new float[count];

        int[] cameFrom =
            new int[count];

        bool[] closed =
            new bool[count];

        bool[] openFlags =
            new bool[count];

        for (int i = 0; i < count; i++)
        {
            gScore[i] =
                float.PositiveInfinity;

            fScore[i] =
                float.PositiveInfinity;

            cameFrom[i] = -1;
        }

        List<int> open =
            new List<int>();

        gScore[startIndex] = 0f;

        fScore[startIndex] =
            Heuristic(
                nodes[startIndex],
                nodes[endIndex]
            );

        open.Add(startIndex);
        openFlags[startIndex] = true;

        int expanded = 0;
        bool found = false;

        while (open.Count > 0 &&
               expanded <
               maximumExpandedNodes)
        {
            int bestOpenListIndex = 0;
            int current = open[0];
            float bestF = fScore[current];

            for (int i = 1;
                 i < open.Count;
                 i++)
            {
                int candidate = open[i];

                if (fScore[candidate] <
                    bestF)
                {
                    bestF =
                        fScore[candidate];

                    current = candidate;
                    bestOpenListIndex = i;
                }
            }

            open.RemoveAt(
                bestOpenListIndex
            );

            openFlags[current] = false;

            if (current == endIndex)
            {
                found = true;
                break;
            }

            closed[current] = true;
            expanded++;

            Node currentNode =
                nodes[current];

            for (int yOffset = -1;
                 yOffset <= 1;
                 yOffset++)
            {
                for (int xOffset = -1;
                     xOffset <= 1;
                     xOffset++)
                {
                    if (xOffset == 0 &&
                        yOffset == 0)
                    {
                        continue;
                    }

                    bool diagonal =
                        xOffset != 0 &&
                        yOffset != 0;

                    if (diagonal &&
                        !allowDiagonalMovement)
                    {
                        continue;
                    }

                    int nx =
                        currentNode.x +
                        xOffset;

                    int ny =
                        currentNode.y +
                        yOffset;

                    if (!IsInGrid(nx, ny))
                        continue;

                    int neighborIndex =
                        ToIndex(nx, ny);

                    Node neighbor =
                        nodes[neighborIndex];

                    if (!neighbor.walkable ||
                        closed[neighborIndex])
                    {
                        continue;
                    }

                    if (diagonal &&
                        preventDiagonalCornerCutting)
                    {
                        int sideA =
                            ToIndex(
                                currentNode.x +
                                xOffset,
                                currentNode.y
                            );

                        int sideB =
                            ToIndex(
                                currentNode.x,
                                currentNode.y +
                                yOffset
                            );

                        if (!nodes[sideA].walkable ||
                            !nodes[sideB].walkable)
                        {
                            continue;
                        }
                    }

                    float stepCost =
                        diagonal
                            ? 1.41421356f
                            : 1f;

                    float tentative =
                        gScore[current] +
                        stepCost;

                    if (tentative >=
                        gScore[neighborIndex])
                    {
                        continue;
                    }

                    cameFrom[neighborIndex] =
                        current;

                    gScore[neighborIndex] =
                        tentative;

                    fScore[neighborIndex] =
                        tentative +
                        Heuristic(
                            neighbor,
                            nodes[endIndex]
                        );

                    if (!openFlags[neighborIndex])
                    {
                        open.Add(neighborIndex);
                        openFlags[neighborIndex] = true;
                    }
                }
            }
        }

        if (!found)
            return false;

        List<Vector2> raw =
            new List<Vector2>();

        int pathIndex = endIndex;

        while (pathIndex >= 0)
        {
            raw.Add(
                nodes[pathIndex].world
            );

            if (pathIndex == startIndex)
                break;

            pathIndex =
                cameFrom[pathIndex];
        }

        raw.Reverse();

        SmoothPath(
            start,
            end,
            raw,
            output
        );

        if (storeDebugPath)
            StoreDebugPath(output);

        return true;
    }

    private void SmoothPath(
        Vector2 actualStart,
        Vector2 actualEnd,
        List<Vector2> raw,
        List<Vector2> output)
    {
        output.Clear();

        if (raw.Count == 0)
        {
            output.Add(actualEnd);
            return;
        }

        List<Vector2> candidates =
            new List<Vector2>(raw);

        candidates[0] = actualStart;
        candidates[
            candidates.Count - 1
        ] = actualEnd;

        int anchor = 0;

        while (anchor <
               candidates.Count - 1)
        {
            int furthest =
                candidates.Count - 1;

            while (furthest >
                   anchor + 1)
            {
                if (HasClearPath(
                        candidates[anchor],
                        candidates[furthest]))
                {
                    break;
                }

                furthest--;
            }

            output.Add(
                candidates[furthest]
            );

            anchor = furthest;
        }
    }

    private bool TryGetNearestWalkableIndex(
        Vector2 world,
        out int result)
    {
        result = -1;

        WorldToCell(
            world,
            out int centerX,
            out int centerY
        );

        if (IsInGrid(
                centerX,
                centerY))
        {
            int centerIndex =
                ToIndex(
                    centerX,
                    centerY
                );

            if (nodes[centerIndex].walkable)
            {
                result = centerIndex;
                return true;
            }
        }

        for (int radius = 1;
             radius <=
             nearestWalkableSearchRadius;
             radius++)
        {
            float bestDistance =
                float.PositiveInfinity;

            int bestIndex = -1;

            for (int y = centerY - radius;
                 y <= centerY + radius;
                 y++)
            {
                for (int x = centerX - radius;
                     x <= centerX + radius;
                     x++)
                {
                    if (!IsInGrid(x, y))
                        continue;

                    bool onEdge =
                        x == centerX - radius ||
                        x == centerX + radius ||
                        y == centerY - radius ||
                        y == centerY + radius;

                    if (!onEdge)
                        continue;

                    int index =
                        ToIndex(x, y);

                    if (!nodes[index].walkable)
                        continue;

                    float distance =
                        (
                            nodes[index].world -
                            world
                        ).sqrMagnitude;

                    if (distance <
                        bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = index;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                result = bestIndex;
                return true;
            }
        }

        return false;
    }

    private bool CircleFitsInsideBounds(
        Vector2 center,
        float radius)
    {
        if (!walkableBounds.OverlapPoint(
                center))
        {
            return false;
        }

        const int samples = 12;

        for (int i = 0;
             i < samples;
             i++)
        {
            float angle =
                Mathf.PI *
                2f *
                i /
                samples;

            Vector2 edge =
                center +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) *
                radius;

            if (!walkableBounds.OverlapPoint(
                    edge))
            {
                return false;
            }
        }

        return true;
    }

    private float Heuristic(
        Node a,
        Node b)
    {
        int dx =
            Mathf.Abs(a.x - b.x);

        int dy =
            Mathf.Abs(a.y - b.y);

        if (!allowDiagonalMovement)
            return dx + dy;

        int diagonal =
            Mathf.Min(dx, dy);

        int straight =
            Mathf.Max(dx, dy) -
            diagonal;

        return
            diagonal *
            1.41421356f +
            straight;
    }

    private void WorldToCell(
        Vector2 world,
        out int x,
        out int y)
    {
        x = Mathf.FloorToInt(
            (world.x - origin.x) /
            cellSize
        );

        y = Mathf.FloorToInt(
            (world.y - origin.y) /
            cellSize
        );

        x = Mathf.Clamp(
            x,
            0,
            Mathf.Max(0, width - 1)
        );

        y = Mathf.Clamp(
            y,
            0,
            Mathf.Max(0, height - 1)
        );
    }

    private int ToIndex(
        int x,
        int y)
    {
        return y * width + x;
    }

    private bool IsInGrid(
        int x,
        int y)
    {
        return
            x >= 0 &&
            y >= 0 &&
            x < width &&
            y < height;
    }

    private void StoreDebugPath(
        List<Vector2> path)
    {
        lastDebugPath.Clear();
        lastDebugPath.AddRange(path);
    }

    private void OnDrawGizmosSelected()
    {
        if (drawGrid &&
            nodes != null)
        {
            float size =
                cellSize * 0.8f;

            for (int i = 0;
                 i < nodes.Length;
                 i++)
            {
                Node node = nodes[i];

                if (node.walkable)
                {
                    Gizmos.color =
                        new Color(
                            0.2f,
                            1f,
                            0.3f,
                            0.13f
                        );
                }
                else
                {
                    if (!drawBlockedCells)
                        continue;

                    Gizmos.color =
                        new Color(
                            1f,
                            0.2f,
                            0.2f,
                            0.18f
                        );
                }

                Gizmos.DrawCube(
                    node.world,
                    new Vector3(
                        size,
                        size,
                        0.02f
                    )
                );
            }
        }

        if (drawLastPath &&
            lastDebugPath.Count > 0)
        {
            Gizmos.color =
                new Color(
                    0.1f,
                    0.8f,
                    1f,
                    0.9f
                );

            for (int i = 0;
                 i < lastDebugPath.Count;
                 i++)
            {
                Gizmos.DrawSphere(
                    lastDebugPath[i],
                    0.08f
                );

                if (i > 0)
                {
                    Gizmos.DrawLine(
                        lastDebugPath[i - 1],
                        lastDebugPath[i]
                    );
                }
            }
        }
    }
}
