using System.Collections.Generic;
using ProjectEri.EnemyAI.V2;
using UnityEngine;

/// <summary>
/// Chooses quiet backline positions for Eri and grants occasional finishing
/// shots when the enemy squad's pressure is concentrated on the player.
/// Healing delivery always takes priority and is owned by EriCombatCompanion.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriTacticalBrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EriTacticalLocomotion locomotion;
    [SerializeField] private ArenaNavigationGrid navigationGrid;
    [SerializeField] private Collider2D bodyCollider;

    [Header("Backline Positioning")]
    [SerializeField, Min(0.5f)] private float minimumPlayerDistance = 4.6f;
    [SerializeField, Min(0.5f)] private float preferredPlayerDistance = 5.8f;
    [SerializeField, Min(0.5f)] private float maximumPlayerDistance = 7.2f;
    [SerializeField, Range(8, 24)] private int positionCandidateCount = 16;
    [SerializeField, Min(0.05f)] private float decisionInterval = 0.38f;
    [SerializeField, Min(0.1f)] private float minimumSlotCommitSeconds = 0.9f;
    [SerializeField, Min(0.1f)] private float maximumSlotCommitSeconds = 1.7f;
    [SerializeField, Min(0.1f)] private float playerRepositionThreshold = 1.2f;

    [Header("Safety")]
    [SerializeField, Min(0.1f)] private float enemyPersonalSpace = 3.0f;
    [SerializeField, Min(0.1f)] private float emergencyEnemyDistance = 1.8f;
    [SerializeField, Min(0.1f)] private float emergencyEvadeDistance = 4.2f;
    [SerializeField, Min(0.1f)] private float hardLeashDistance = 10f;
    [SerializeField, Min(0.1f)] private float emergencySpeedMultiplier = 1.22f;

    [Header("Self-Defense Burst")]
    [Tooltip(
        "When several enemies crowd Eri, she rapidly tags each nearby threat " +
        "once before committing to an escape route.")]
    [SerializeField] private bool enableSelfDefenseBurst = true;
    [SerializeField, Min(0.5f)] private float defenseTriggerRadius = 3.2f;
    [SerializeField, Min(2)] private int minimumDefenseTargets = 3;
    [SerializeField, Min(0.05f)] private float defensiveShotInterval = 0.12f;
    [SerializeField, Min(0.1f)] private float defensiveResponseCooldown = 7.5f;
    [SerializeField, Min(1)] private int defensiveShotDamage = 2;
    [SerializeField, Min(0.1f)] private float defensiveShotSpeed = 14f;
    [SerializeField, Min(0.1f)] private float defensiveShotLifetime = 1.6f;
    [SerializeField, Min(0.08f)] private float defensiveShotVisualSize = 0.32f;
    [SerializeField] private Color defensiveShotColor =
        new Color(0.28f, 1f, 0.90f, 1f);
    [SerializeField, Min(0.1f)] private float defensiveEscapeCommitSeconds = 1.25f;
    [SerializeField, Range(0.5f, 1.5f)] private float defensiveEscapeSpeedMultiplier = 1.35f;
    [SerializeField, Min(0f)] private float supportFireDelayAfterDefense = 1.75f;

    [Header("Safest / Quickest Route Balance")]
    [Tooltip(
        "0 makes Eri use the quickest available route. 1 strongly favors " +
        "routes with less enemy exposure. Start around 0.67."
    )]
    [SerializeField, Range(0f, 1f)] private float routeSafetyPriority = 0.67f;

    [Tooltip("Enemy influence radius used when comparing possible routes.")]
    [SerializeField, Min(0.2f)] private float routeThreatRadius = 3.8f;

    [Tooltip(
        "A route inside this distance receives a severe danger penalty."
    )]
    [SerializeField, Min(0.05f)] private float routeCriticalDistance = 1.55f;

    [Header("Position Scoring")]
    [SerializeField, Min(0f)] private float safetyWeight = 4.5f;
    [SerializeField, Min(0f)] private float backlineWeight = 3.2f;
    [SerializeField, Min(0f)] private float ringWeight = 2.2f;
    [SerializeField, Min(0f)] private float pathCostWeight = 0.16f;
    [SerializeField, Min(0f)] private float lineOfSightBonus = 0.65f;
    [SerializeField, Min(0f)] private float currentSlotBias = 1.25f;

    [Header("Sensing")]
    [SerializeField, Min(0.05f)] private float enemyRefreshInterval = 0.30f;
    [SerializeField, Min(0.1f)] private float playerPressureRadius = 6.5f;
    [SerializeField, Range(0f, 1f)] private float concentratedPressureRatio = 0.60f;
    [SerializeField, Min(1)] private int concentratedPressureMinimumEnemies = 2;

    [Header("Restrained Support Fire")]
    [SerializeField] private bool enableSupportFire = true;
    [SerializeField, Range(0.05f, 1f)] private float weakenedEnemyHealth01 = 0.35f;
    [Tooltip(
        "Chance to take a covering shot when enemies are concentrating on " +
        "the player but none are weak enough to finish.")]
    [SerializeField, Range(0f, 1f)] private float coveringShotChancePerOpportunity = 0.18f;
    [SerializeField, Range(0f, 1f)] private float shotChancePerOpportunity = 0.40f;
    [SerializeField, Min(0.1f)] private float minimumShotCooldown = 4.5f;
    [SerializeField, Min(0.1f)] private float maximumShotCooldown = 7f;
    [SerializeField, Min(1)] private int supportShotDamage = 3;
    [SerializeField, Min(0.1f)] private float supportShotSpeed = 11f;
    [SerializeField, Min(0.1f)] private float supportShotLifetime = 2f;
    [SerializeField, Min(0.08f)] private float supportShotVisualSize = 0.34f;
    [SerializeField] private Color supportShotColor =
        new Color(0.45f, 1f, 0.82f, 1f);
    [SerializeField] private LayerMask supportShotCollisionMask;

    [Header("Runtime Debug")]
    [SerializeField] private Vector2 currentTacticalSlot;
    [SerializeField] private bool hasTacticalSlot;
    [SerializeField] private string currentIntent = "Waiting";
    [SerializeField] private int livingEnemyCount;
    [SerializeField] private int playerPressureCount;
    [SerializeField] private float closestEnemyDistance;
    [SerializeField] private float currentSlotScore;
    [SerializeField] private float currentRouteExposure;
    [SerializeField] private string supportFireStatus = "Waiting";
    [SerializeField] private string defensiveResponseStatus = "Ready";
    [SerializeField] private int nearbyDefenseTargetCount;

    private readonly List<EnemyHealth> enemies =
        new List<EnemyHealth>();
    private readonly List<Vector2> routeThreatPositions =
        new List<Vector2>();
    private readonly List<Vector2> routeEvaluationPath =
        new List<Vector2>();
    private readonly List<EnemyHealth> defensiveTargets =
        new List<EnemyHealth>();

    private Transform player;
    private Rigidbody2D playerBody;
    private Vector2 previousPlayerPosition;
    private Vector2 observedPlayerVelocity;
    private Vector2 playerPositionAtLastDecision;

    private float nextEnemyRefreshTime;
    private float nextDecisionTime;
    private float slotCommitUntil;
    private float nextSupportShotTime;
    private float nextDefensiveShotTime;
    private float nextDefensiveResponseTime;
    private float defensiveEscapeUntil;
    private int defensiveTargetIndex;
    private bool defensiveBurstActive;

    public string CurrentIntent => currentIntent;
    public bool IsEvading =>
        currentIntent == "Emergency evade" ||
        currentIntent == "Defensive escape" ||
        currentIntent == "Self-defense burst";

    private void Awake()
    {
        if (locomotion == null)
            locomotion = GetComponent<EriTacticalLocomotion>();

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<ArenaNavigationGrid>(true);
        }

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        ConfigureLocomotionRouting();
        ConfigureDefaultMasks();
    }

    public void Configure(
        EriTacticalLocomotion newLocomotion,
        ArenaNavigationGrid newGrid,
        Collider2D newBodyCollider)
    {
        if (newLocomotion != null)
            locomotion = newLocomotion;

        if (newGrid != null)
            navigationGrid = newGrid;

        if (newBodyCollider != null)
            bodyCollider = newBodyCollider;

        ConfigureLocomotionRouting();
        ConfigureDefaultMasks();
    }

    public void ConfigureRouteSafety(
        float safetyPriority,
        float threatRadius,
        float criticalDistance)
    {
        routeSafetyPriority =
            Mathf.Clamp01(safetyPriority);
        routeThreatRadius =
            Mathf.Max(0.2f, threatRadius);
        routeCriticalDistance =
            Mathf.Clamp(
                criticalDistance,
                0.05f,
                routeThreatRadius
            );

        ConfigureLocomotionRouting();
    }

    public bool TickTactics(
        Transform playerTransform,
        bool externalSevereThreat,
        Vector2 externalEvadeDirection)
    {
        if (locomotion == null ||
            playerTransform == null)
        {
            currentIntent = "Missing movement or player";
            return false;
        }

        SetPlayer(playerTransform);
        UpdateObservedPlayerVelocity();
        RefreshEnemiesIfNeeded();

        Vector2 myPosition = transform.position;
        Vector2 playerPosition = player.position;

        Vector2 enemyEvadeDirection =
            CalculateEnemyEvadeDirection(
                myPosition,
                out closestEnemyDistance
            );

        Vector2 combinedEvadeDirection =
            enemyEvadeDirection +
            externalEvadeDirection * 1.35f;

        if (TickSelfDefense(
                myPosition,
                playerPosition,
                combinedEvadeDirection))
        {
            return true;
        }

        bool enemyEmergency =
            closestEnemyDistance <=
            emergencyEnemyDistance;

        if (externalSevereThreat ||
            enemyEmergency)
        {
            Vector2 evadeDirection =
                combinedEvadeDirection;

            if (evadeDirection.sqrMagnitude <= 0.0001f)
                evadeDirection = Vector2.left;

            bool chooseNewEmergencySlot =
                currentIntent != "Emergency evade" ||
                Time.time >= nextDecisionTime ||
                locomotion.NeedsNewDestination;

            if (chooseNewEmergencySlot)
            {
                currentTacticalSlot =
                    FindEmergencySlot(
                        myPosition,
                        playerPosition,
                        evadeDirection.normalized
                    );

                locomotion.SetDestination(
                    currentTacticalSlot,
                    emergencySpeedMultiplier,
                    true
                );

                nextDecisionTime =
                    Time.time + 0.24f;
            }
            else
            {
                locomotion.SetDestination(
                    currentTacticalSlot,
                    emergencySpeedMultiplier
                );
            }

            currentIntent = "Emergency evade";
            hasTacticalSlot = true;
            supportFireStatus = "Held: danger";
            return true;
        }

        bool justLeftEmergency =
            currentIntent == "Emergency evade";

        float distanceFromPlayer =
            Vector2.Distance(
                myPosition,
                playerPosition
            );

        bool hardLeashBroken =
            distanceFromPlayer > hardLeashDistance;

        bool playerShifted =
            Vector2.Distance(
                playerPosition,
                playerPositionAtLastDecision
            ) >= playerRepositionThreshold;

        bool hardLeashNeedsDecision =
            hardLeashBroken &&
            (!hasTacticalSlot ||
             Time.time >= nextDecisionTime);

        bool needsDecision =
            justLeftEmergency ||
            hardLeashNeedsDecision ||
            locomotion.NeedsNewDestination ||
            !hasTacticalSlot ||
            (Time.time >= nextDecisionTime &&
             (Time.time >= slotCommitUntil ||
              playerShifted ||
              IsCurrentSlotUnsafe()));

        if (needsDecision)
            ChooseTacticalSlot(hardLeashBroken);

        if (hasTacticalSlot)
        {
            locomotion.SetDestination(
                currentTacticalSlot,
                hardLeashBroken ? 1.12f : 1f
            );
        }

        TrySupportFire();
        return false;
    }

    public void Halt(string reason, bool hardStop)
    {
        CancelSelfDefenseResponse();

        locomotion?.ClearDestination(
            reason,
            hardStop
        );

        currentIntent = reason;
        supportFireStatus = "Held";
    }

    private void SetPlayer(Transform newPlayer)
    {
        if (player == newPlayer)
            return;

        player = newPlayer;
        playerBody =
            player.GetComponent<Rigidbody2D>();
        previousPlayerPosition =
            player.position;
        playerPositionAtLastDecision =
            player.position;

        IgnoreBodyCollisionsWith(player);
    }

    private void UpdateObservedPlayerVelocity()
    {
        if (player == null)
            return;

        Vector2 current = player.position;
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

#if UNITY_6000_0_OR_NEWER
        Vector2 rigidbodyVelocity =
            playerBody != null
                ? playerBody.linearVelocity
                : Vector2.zero;
#else
        Vector2 rigidbodyVelocity =
            playerBody != null
                ? playerBody.velocity
                : Vector2.zero;
#endif

        Vector2 measured =
            (current - previousPlayerPosition) /
            deltaTime;

        observedPlayerVelocity =
            Vector2.Lerp(
                observedPlayerVelocity,
                rigidbodyVelocity.sqrMagnitude > 0.01f
                    ? rigidbodyVelocity
                    : measured,
                0.2f
            );

        previousPlayerPosition = current;
    }

    private void RefreshEnemiesIfNeeded()
    {
        if (Time.time < nextEnemyRefreshTime)
            return;

        nextEnemyRefreshTime =
            Time.time + enemyRefreshInterval;

        enemies.Clear();

        EnemyHealth[] found =
            FindObjectsOfType<EnemyHealth>(false);

        for (int i = 0; i < found.Length; i++)
        {
            EnemyHealth enemy = found[i];

            if (enemy == null ||
                enemy.CurrentHP <= 0)
            {
                continue;
            }

            enemies.Add(enemy);
            IgnoreBodyCollisionsWith(
                enemy.transform
            );
        }

        livingEnemyCount = enemies.Count;
        SyncRouteThreats();
    }

    private void ConfigureLocomotionRouting()
    {
        locomotion?.ConfigureDangerRouting(
            routeSafetyPriority,
            routeThreatRadius,
            routeCriticalDistance
        );
    }

    private void SyncRouteThreats()
    {
        routeThreatPositions.Clear();

        for (int i = 0;
             i < enemies.Count;
             i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null ||
                enemy.CurrentHP <= 0)
            {
                continue;
            }

            routeThreatPositions.Add(
                enemy.transform.position
            );
        }

        ConfigureLocomotionRouting();
        locomotion?.SetRouteThreats(
            routeThreatPositions
        );
    }

    private void ChooseTacticalSlot(
        bool hardLeashBroken)
    {
        Vector2 myPosition = transform.position;
        Vector2 playerPosition = player.position;
        Vector2 enemyCenter =
            CalculateEnemyCenter(playerPosition);

        Vector2 preferredBacklineDirection =
            playerPosition - enemyCenter;

        if (enemies.Count == 0)
        {
            preferredBacklineDirection =
                observedPlayerVelocity.sqrMagnitude > 0.05f
                    ? -observedPlayerVelocity.normalized
                    : Vector2.left;
        }

        if (preferredBacklineDirection.sqrMagnitude <=
            0.0001f)
        {
            preferredBacklineDirection = Vector2.left;
        }

        preferredBacklineDirection.Normalize();

        EnemyHealth sightTarget =
            FindWeakestEnemy(false);

        bool foundCandidate = false;
        Vector2 best = playerPosition +
            preferredBacklineDirection *
            preferredPlayerDistance;
        float bestScore = float.NegativeInfinity;
        float bestRouteExposure = 0f;

        int count =
            Mathf.Clamp(
                positionCandidateCount,
                8,
                24
            );

        float baseAngle =
            Mathf.Atan2(
                preferredBacklineDirection.y,
                preferredBacklineDirection.x
            );

        for (int i = 0; i < count; i++)
        {
            float fraction =
                i / (float)count;

            float angle =
                baseAngle +
                fraction * Mathf.PI * 2f;

            float radius;

            switch (i % 3)
            {
                case 0:
                    radius = preferredPlayerDistance;
                    break;

                case 1:
                    radius =
                        Mathf.Lerp(
                            minimumPlayerDistance,
                            preferredPlayerDistance,
                            0.55f
                        );
                    break;

                default:
                    radius =
                        Mathf.Lerp(
                            preferredPlayerDistance,
                            maximumPlayerDistance,
                            0.65f
                        );
                    break;
            }

            Vector2 candidate =
                playerPosition +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) * radius;

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                candidate =
                    navigationGrid.
                        FindNearestWalkablePosition(
                            candidate
                        );
            }

            float score =
                ScoreCandidate(
                    myPosition,
                    playerPosition,
                    candidate,
                    preferredBacklineDirection,
                    sightTarget,
                    out float routeExposure
                );

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = candidate;
            bestRouteExposure = routeExposure;
            foundCandidate = true;
        }

        if (!foundCandidate)
        {
            best =
                playerPosition +
                preferredBacklineDirection *
                preferredPlayerDistance;

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                best =
                    navigationGrid.
                        FindNearestWalkablePosition(best);
            }
        }

        currentTacticalSlot = best;
        currentSlotScore = bestScore;
        currentRouteExposure =
            bestRouteExposure;
        hasTacticalSlot = true;
        playerPositionAtLastDecision = playerPosition;

        float commitment =
            Random.Range(
                minimumSlotCommitSeconds,
                Mathf.Max(
                    minimumSlotCommitSeconds,
                    maximumSlotCommitSeconds
                )
            );

        slotCommitUntil =
            Time.time + commitment;
        nextDecisionTime =
            Time.time + decisionInterval;

        locomotion.SetDestination(
            best,
            hardLeashBroken ? 1.12f : 1f,
            true
        );

        currentIntent =
            hardLeashBroken
                ? "Rejoining at safe range"
                : enemies.Count > 0
                    ? "Holding tactical backline"
                    : "Quiet trailing position";
    }

    private float ScoreCandidate(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 candidate,
        Vector2 preferredBacklineDirection,
        EnemyHealth sightTarget,
        out float routeExposure)
    {
        routeExposure = 0f;

        float playerDistance =
            Vector2.Distance(
                candidate,
                playerPosition
            );

        if (playerDistance <
                minimumPlayerDistance * 0.75f ||
            playerDistance >
                maximumPlayerDistance * 1.35f)
        {
            return float.NegativeInfinity;
        }

        if (locomotion != null &&
            locomotion.IsDestinationRecentlyFailed(
                candidate,
                0.55f))
        {
            return float.NegativeInfinity;
        }

        float pathCost =
            Vector2.Distance(
                myPosition,
                candidate
            );

        if (navigationGrid != null &&
            navigationGrid.IsBuilt)
        {
            routeEvaluationPath.Clear();

            // Candidate comparison samples the ordinary route for performance.
            // Once a destination wins, locomotion performs the full dynamic
            // danger-aware search for the path Eri actually follows.
            bool routeFound =
                navigationGrid.TryFindPath(
                    myPosition,
                    candidate,
                    routeEvaluationPath
                );

            if (!routeFound)
                return float.NegativeInfinity;

            pathCost =
                CalculatePathLength(
                    myPosition,
                    routeEvaluationPath
                );

            routeExposure =
                CalculateRouteExposure(
                    myPosition,
                    routeEvaluationPath
                );
        }

        float nearestEnemy =
            GetClosestEnemyDistance(candidate);

        if (nearestEnemy <
            enemyPersonalSpace * 0.65f)
        {
            return float.NegativeInfinity;
        }

        float safety =
            enemies.Count == 0
                ? 1f
                : Mathf.Clamp01(
                    (nearestEnemy -
                     enemyPersonalSpace * 0.65f) /
                    Mathf.Max(
                        0.1f,
                        preferredPlayerDistance
                    )
                );

        Vector2 fromPlayer =
            candidate - playerPosition;

        float backline =
            fromPlayer.sqrMagnitude > 0.0001f
                ? Mathf.InverseLerp(
                    -1f,
                    1f,
                    Vector2.Dot(
                        fromPlayer.normalized,
                        preferredBacklineDirection
                    )
                )
                : 0f;

        float ring =
            1f -
            Mathf.Clamp01(
                Mathf.Abs(
                    playerDistance -
                    preferredPlayerDistance
                ) /
                Mathf.Max(
                    0.1f,
                    maximumPlayerDistance -
                    minimumPlayerDistance
                )
            );

        float score =
            safety * safetyWeight +
            backline * backlineWeight +
            ring * ringWeight -
            pathCost * pathCostWeight -
            routeExposure *
            Mathf.Lerp(
                0f,
                6f,
                routeSafetyPriority
            );

        if (hasTacticalSlot)
        {
            float retained =
                1f -
                Mathf.Clamp01(
                    Vector2.Distance(
                        candidate,
                        currentTacticalSlot
                    ) / 2f
                );

            score +=
                retained * currentSlotBias;
        }

        if (sightTarget != null &&
            HasLineOfSight(
                candidate,
                sightTarget.transform.position))
        {
            score += lineOfSightBonus;
        }

        return score;
    }

    private static float CalculatePathLength(
        Vector2 start,
        IReadOnlyList<Vector2> route)
    {
        float total = 0f;
        Vector2 previous = start;

        for (int i = 0;
             i < route.Count;
             i++)
        {
            total +=
                Vector2.Distance(
                    previous,
                    route[i]
                );

            previous = route[i];
        }

        return total;
    }

    private float CalculateRouteExposure(
        Vector2 start,
        IReadOnlyList<Vector2> route)
    {
        if (route.Count == 0 ||
            routeThreatPositions.Count == 0)
        {
            return 0f;
        }

        float weightedExposure = 0f;
        float routeLength = 0f;
        Vector2 previous = start;

        for (int routeIndex = 0;
             routeIndex < route.Count;
             routeIndex++)
        {
            Vector2 next = route[routeIndex];
            float segmentLength =
                Vector2.Distance(previous, next);

            int samples =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        segmentLength / 0.35f
                    )
                );

            float sampleLength =
                segmentLength / samples;

            for (int sample = 0;
                 sample < samples;
                 sample++)
            {
                Vector2 position =
                    Vector2.Lerp(
                        previous,
                        next,
                        (sample + 0.5f) /
                        samples
                    );

                float nearestEnemy =
                    GetClosestEnemyDistance(
                        position
                    );

                if (nearestEnemy >=
                    routeThreatRadius)
                {
                    continue;
                }

                float danger =
                    1f -
                    nearestEnemy /
                    Mathf.Max(
                        0.1f,
                        routeThreatRadius
                    );

                danger *= danger;

                if (nearestEnemy <
                    routeCriticalDistance)
                {
                    danger += 1f;
                }

                weightedExposure +=
                    danger * sampleLength;
            }

            routeLength += segmentLength;
            previous = next;
        }

        return routeLength > 0.001f
            ? weightedExposure / routeLength
            : 0f;
    }

    private Vector2 FindEmergencySlot(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 evadeDirection)
    {
        Vector2 best =
            myPosition +
            evadeDirection *
            emergencyEvadeDistance;

        float bestSafety =
            float.NegativeInfinity;

        for (int i = -2; i <= 2; i++)
        {
            float angle = i * 28f;
            Vector2 direction =
                (Vector2)(
                    Quaternion.Euler(
                        0f,
                        0f,
                        angle
                    ) *
                    (Vector3)evadeDirection
                );

            Vector2 candidate =
                myPosition +
                direction *
                emergencyEvadeDistance;

            float playerDistance =
                Vector2.Distance(
                    candidate,
                    playerPosition
                );

            if (playerDistance >
                hardLeashDistance)
            {
                Vector2 towardPlayer =
                    (playerPosition - candidate).
                        normalized;

                candidate +=
                    towardPlayer *
                    (playerDistance -
                     hardLeashDistance);
            }

            if (navigationGrid != null &&
                navigationGrid.IsBuilt)
            {
                candidate =
                    navigationGrid.
                        FindNearestWalkablePosition(
                            candidate
                        );

                float pathCost =
                    navigationGrid.
                        EstimatePathCost(
                            myPosition,
                            candidate
                        );

                if (float.IsInfinity(pathCost))
                    continue;
            }

            if (locomotion != null &&
                locomotion.IsDestinationRecentlyFailed(
                    candidate,
                    0.55f))
            {
                continue;
            }

            float safety =
                GetClosestEnemyDistance(candidate);

            if (safety > bestSafety)
            {
                bestSafety = safety;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsCurrentSlotUnsafe()
    {
        if (!hasTacticalSlot ||
            player == null)
        {
            return true;
        }

        float playerDistance =
            Vector2.Distance(
                currentTacticalSlot,
                player.position
            );

        if (playerDistance <
                minimumPlayerDistance * 0.8f ||
            playerDistance >
                maximumPlayerDistance * 1.25f)
        {
            return true;
        }

        return GetClosestEnemyDistance(
            currentTacticalSlot
        ) < enemyPersonalSpace * 0.85f;
    }

    private Vector2 CalculateEnemyCenter(
        Vector2 fallback)
    {
        if (enemies.Count == 0)
            return fallback;

        Vector2 center = Vector2.zero;
        int count = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null)
                continue;

            center +=
                (Vector2)enemy.transform.position;
            count++;
        }

        return count > 0
            ? center / count
            : fallback;
    }

    private Vector2 CalculateEnemyEvadeDirection(
        Vector2 position,
        out float closestDistance)
    {
        Vector2 result = Vector2.zero;
        closestDistance = float.PositiveInfinity;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null)
                continue;

            Vector2 away =
                position -
                (Vector2)enemy.transform.position;

            float distance = away.magnitude;
            closestDistance =
                Mathf.Min(
                    closestDistance,
                    distance
                );

            if (distance >
                enemyPersonalSpace * 1.5f)
            {
                continue;
            }

            result +=
                away.normalized /
                Mathf.Max(0.2f, distance);
        }

        if (result.sqrMagnitude > 0.0001f)
            result.Normalize();

        return result;
    }

    private float GetClosestEnemyDistance(
        Vector2 position)
    {
        float closest = float.PositiveInfinity;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null)
                continue;

            closest =
                Mathf.Min(
                    closest,
                    Vector2.Distance(
                        position,
                        enemy.transform.position
                    )
                );
        }

        return closest;
    }

    private bool TickSelfDefense(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 preferredEscapeDirection)
    {
        nearbyDefenseTargetCount =
            CountEnemiesWithin(
                myPosition,
                defenseTriggerRadius);

        if (defensiveBurstActive)
        {
            TickDefensiveBurst(
                myPosition,
                playerPosition,
                preferredEscapeDirection);
            return true;
        }

        if (Time.time < defensiveEscapeUntil)
        {
            MaintainDefensiveEscape(
                myPosition,
                playerPosition,
                preferredEscapeDirection);
            return true;
        }

        if (defensiveEscapeUntil > 0f)
        {
            defensiveEscapeUntil = 0f;
            defensiveResponseStatus =
                "Escape complete; cooling down";
        }

        if (!enableSelfDefenseBurst)
        {
            defensiveResponseStatus = "Disabled";
            return false;
        }

        if (Time.time < nextDefensiveResponseTime)
        {
            defensiveResponseStatus =
                $"Cooldown: " +
                $"{nextDefensiveResponseTime - Time.time:0.0}s";
            return false;
        }

        if (nearbyDefenseTargetCount <
            Mathf.Max(2, minimumDefenseTargets))
        {
            defensiveResponseStatus =
                $"Ready: {nearbyDefenseTargetCount} nearby";
            return false;
        }

        BeginDefensiveBurst(myPosition);
        TickDefensiveBurst(
            myPosition,
            playerPosition,
            preferredEscapeDirection);
        return true;
    }

    private void BeginDefensiveBurst(
        Vector2 myPosition)
    {
        defensiveTargets.Clear();

        float radiusSquared =
            defenseTriggerRadius * defenseTriggerRadius;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null ||
                enemy.CurrentHP <= 0)
            {
                continue;
            }

            Vector2 delta =
                (Vector2)enemy.transform.position -
                myPosition;

            if (delta.sqrMagnitude <= radiusSquared)
                defensiveTargets.Add(enemy);
        }

        SortDefensiveTargetsByDistance(myPosition);
        defensiveTargetIndex = 0;
        defensiveBurstActive = true;
        nextDefensiveShotTime = Time.time;
        currentIntent = "Self-defense burst";
        supportFireStatus = "Held: self-defense";
        defensiveResponseStatus =
            $"Engaging {defensiveTargets.Count} threats";

        // A brief planted response reads as deliberate instead of panicked.
        // The interval is short, then Eri immediately commits to escape.
        locomotion?.ClearDestination(
            "Self-defense burst",
            true);
    }

    private void TickDefensiveBurst(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 preferredEscapeDirection)
    {
        currentIntent = "Self-defense burst";
        supportFireStatus = "Held: self-defense";

        if (Time.time < nextDefensiveShotTime)
            return;

        while (defensiveTargetIndex <
            defensiveTargets.Count)
        {
            EnemyHealth target =
                defensiveTargets[defensiveTargetIndex];
            defensiveTargetIndex++;

            if (!IsValidDefensiveTarget(
                    target,
                    myPosition))
            {
                continue;
            }

            FireDefensiveShot(target);
            nextDefensiveShotTime =
                Time.time + defensiveShotInterval;
            defensiveResponseStatus =
                $"Countering {defensiveTargetIndex}/" +
                $"{defensiveTargets.Count}";
            return;
        }

        CompleteDefensiveBurst(
            myPosition,
            playerPosition,
            preferredEscapeDirection);
    }

    private bool IsValidDefensiveTarget(
        EnemyHealth target,
        Vector2 myPosition)
    {
        if (target == null ||
            target.CurrentHP <= 0)
        {
            return false;
        }

        float maximumFollowThroughRange =
            defenseTriggerRadius * 1.6f;

        if (Vector2.Distance(
                myPosition,
                target.transform.position) >
            maximumFollowThroughRange)
        {
            return false;
        }

        return HasLineOfSight(
            myPosition,
            target.transform.position);
    }

    private void FireDefensiveShot(
        EnemyHealth target)
    {
        Vector2 origin = transform.position;
        Vector2 direction =
            (Vector2)target.transform.position - origin;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.up;

        origin += direction.normalized * 0.28f;

        EriSupportBolt.Spawn(
            origin,
            direction,
            defensiveShotDamage,
            defensiveShotSpeed,
            defensiveShotLifetime,
            supportShotCollisionMask,
            defensiveShotColor,
            defensiveShotVisualSize);
    }

    private void CompleteDefensiveBurst(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 preferredEscapeDirection)
    {
        defensiveBurstActive = false;
        defensiveTargets.Clear();
        nextDefensiveResponseTime =
            Time.time + defensiveResponseCooldown;
        nextSupportShotTime =
            Mathf.Max(
                nextSupportShotTime,
                Time.time + supportFireDelayAfterDefense);

        BeginDefensiveEscape(
            myPosition,
            playerPosition,
            preferredEscapeDirection);
    }

    private void BeginDefensiveEscape(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 preferredEscapeDirection)
    {
        if (preferredEscapeDirection.sqrMagnitude <= 0.0001f)
            preferredEscapeDirection = Vector2.left;

        currentTacticalSlot =
            FindEmergencySlot(
                myPosition,
                playerPosition,
                preferredEscapeDirection.normalized);

        locomotion?.SetDestination(
            currentTacticalSlot,
            defensiveEscapeSpeedMultiplier,
            true);

        hasTacticalSlot = true;
        defensiveEscapeUntil =
            Time.time + defensiveEscapeCommitSeconds;
        nextDecisionTime = Time.time + 0.24f;
        currentIntent = "Defensive escape";
        supportFireStatus = "Held: escaping";
        defensiveResponseStatus = "Escaping after counterattack";
    }

    private void MaintainDefensiveEscape(
        Vector2 myPosition,
        Vector2 playerPosition,
        Vector2 preferredEscapeDirection)
    {
        bool chooseNewSlot =
            locomotion == null ||
            locomotion.NeedsNewDestination ||
            Time.time >= nextDecisionTime;

        if (chooseNewSlot)
        {
            if (preferredEscapeDirection.sqrMagnitude <= 0.0001f)
                preferredEscapeDirection = Vector2.left;

            currentTacticalSlot =
                FindEmergencySlot(
                    myPosition,
                    playerPosition,
                    preferredEscapeDirection.normalized);

            locomotion?.SetDestination(
                currentTacticalSlot,
                defensiveEscapeSpeedMultiplier,
                true);
            nextDecisionTime = Time.time + 0.24f;
        }
        else
        {
            locomotion.SetDestination(
                currentTacticalSlot,
                defensiveEscapeSpeedMultiplier);
        }

        currentIntent = "Defensive escape";
        supportFireStatus = "Held: escaping";
        defensiveResponseStatus = "Escaping after counterattack";
        hasTacticalSlot = true;
    }

    private int CountEnemiesWithin(
        Vector2 position,
        float radius)
    {
        int count = 0;
        float radiusSquared = radius * radius;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null ||
                enemy.CurrentHP <= 0)
            {
                continue;
            }

            Vector2 delta =
                (Vector2)enemy.transform.position - position;

            if (delta.sqrMagnitude <= radiusSquared)
                count++;
        }

        return count;
    }

    private void SortDefensiveTargetsByDistance(
        Vector2 origin)
    {
        for (int i = 0; i < defensiveTargets.Count - 1; i++)
        {
            int closestIndex = i;
            float closestDistance =
                ((Vector2)defensiveTargets[i].transform.position - origin).
                    sqrMagnitude;

            for (int candidate = i + 1;
                 candidate < defensiveTargets.Count;
                 candidate++)
            {
                EnemyHealth target = defensiveTargets[candidate];

                if (target == null)
                    continue;

                float distance =
                    ((Vector2)target.transform.position - origin).
                        sqrMagnitude;

                if (distance < closestDistance)
                {
                    closestIndex = candidate;
                    closestDistance = distance;
                }
            }

            if (closestIndex == i)
                continue;

            EnemyHealth swap = defensiveTargets[i];
            defensiveTargets[i] = defensiveTargets[closestIndex];
            defensiveTargets[closestIndex] = swap;
        }
    }

    private void CancelSelfDefenseResponse()
    {
        defensiveBurstActive = false;
        defensiveTargets.Clear();
        defensiveTargetIndex = 0;
        defensiveEscapeUntil = 0f;
        defensiveResponseStatus = "Cancelled by higher priority action";
    }

    private void TrySupportFire()
    {
        if (!enableSupportFire ||
            player == null ||
            Time.time < nextSupportShotTime)
        {
            return;
        }

        if (livingEnemyCount <= 0)
        {
            supportFireStatus =
                "Held: no enemies";
            return;
        }

        if (closestEnemyDistance <
            enemyPersonalSpace)
        {
            supportFireStatus =
                "Held: Eri is threatened";
            return;
        }

        playerPressureCount = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null)
                continue;

            if (IsPressuringPlayer(enemy))
                playerPressureCount++;
        }

        float pressureRatio =
            livingEnemyCount > 0
                ? playerPressureCount /
                  (float)livingEnemyCount
                : 0f;

        EnemyHealth target =
            FindWeakestEnemy(true);

        bool finishingShot = target != null;

        if (target == null)
        {
            bool concentratedOnPlayer =
                livingEnemyCount >=
                    concentratedPressureMinimumEnemies &&
                playerPressureCount >=
                    concentratedPressureMinimumEnemies &&
                pressureRatio >=
                    concentratedPressureRatio;

            if (!concentratedOnPlayer)
            {
                supportFireStatus =
                    "Held: no finisher or covering opening";
                return;
            }

            target = FindWeakestEnemy(false);

            if (target == null)
            {
                supportFireStatus = "Held: no target";
                return;
            }
        }

        if (!HasLineOfSight(
                transform.position,
                target.transform.position))
        {
            supportFireStatus =
                "Held: no clear shot";
            nextSupportShotTime =
                Time.time + 0.6f;
            return;
        }

        float opportunityChance =
            finishingShot
                ? shotChancePerOpportunity
                : coveringShotChancePerOpportunity;

        if (Random.value > opportunityChance)
        {
            supportFireStatus =
                finishingShot
                    ? "Finishing opportunity passed"
                    : "Covering opportunity passed";
            nextSupportShotTime =
                Time.time +
                Random.Range(1.0f, 1.8f);
            return;
        }

        Vector2 origin = transform.position;
        Vector2 targetPosition =
            target.transform.position;
        Vector2 direction =
            targetPosition - origin;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        origin += direction.normalized * 0.28f;

        EriSupportBolt.Spawn(
            origin,
            direction,
            supportShotDamage,
            supportShotSpeed,
            supportShotLifetime,
            supportShotCollisionMask,
            supportShotColor,
            supportShotVisualSize
        );

        supportFireStatus =
            finishingShot
                ? $"Finishing shot: {target.name}"
                : $"Covering shot: {target.name}";

        nextSupportShotTime =
            Time.time +
            Random.Range(
                minimumShotCooldown,
                Mathf.Max(
                    minimumShotCooldown,
                    maximumShotCooldown
                )
            );
    }

    private EnemyHealth FindWeakestEnemy(
        bool requireWeakened)
    {
        EnemyHealth best = null;
        float bestHealth01 = float.PositiveInfinity;
        float bestPlayerDistance =
            float.PositiveInfinity;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyHealth enemy = enemies[i];

            if (enemy == null ||
                enemy.MaxHP <= 0)
            {
                continue;
            }

            float health01 =
                enemy.CurrentHP /
                (float)enemy.MaxHP;

            if (requireWeakened &&
                health01 >
                weakenedEnemyHealth01)
            {
                continue;
            }

            float playerDistance =
                player != null
                    ? Vector2.Distance(
                        enemy.transform.position,
                        player.position
                    )
                    : 0f;

            if (health01 < bestHealth01 ||
                (Mathf.Approximately(
                     health01,
                     bestHealth01) &&
                 playerDistance <
                 bestPlayerDistance))
            {
                best = enemy;
                bestHealth01 = health01;
                bestPlayerDistance =
                    playerDistance;
            }
        }

        return best;
    }

    private bool IsPressuringPlayer(
        EnemyHealth enemy)
    {
        if (enemy == null ||
            player == null)
        {
            return false;
        }

        EnemyAgentV2 agent =
            enemy.GetComponent<EnemyAgentV2>();

        if (agent != null &&
            agent.ActionRunner != null &&
            agent.ActionRunner.IsBusy)
        {
            EnemyActionKindV2 action =
                agent.ActionRunner.CurrentKind;

            if (action ==
                    EnemyActionKindV2.AttackPattern ||
                action ==
                    EnemyActionKindV2.FluidPressure)
            {
                return true;
            }
        }

        return Vector2.Distance(
            enemy.transform.position,
            player.position
        ) <= playerPressureRadius;
    }

    private bool HasLineOfSight(
        Vector2 start,
        Vector2 end)
    {
        if (navigationGrid == null ||
            !navigationGrid.IsBuilt)
        {
            return true;
        }

        return navigationGrid.HasClearPath(
            start,
            end
        );
    }

    private void IgnoreBodyCollisionsWith(
        Transform otherRoot)
    {
        if (bodyCollider == null ||
            otherRoot == null)
        {
            return;
        }

        Collider2D[] otherColliders =
            otherRoot.GetComponentsInChildren<
                Collider2D>(true);

        for (int i = 0;
            i < otherColliders.Length;
            i++)
        {
            Collider2D other =
                otherColliders[i];

            if (other == null ||
                other == bodyCollider)
            {
                continue;
            }

            Physics2D.IgnoreCollision(
                bodyCollider,
                other,
                true
            );
        }
    }

    private void ConfigureDefaultMasks()
    {
        if (supportShotCollisionMask.value != 0)
            return;

        supportShotCollisionMask =
            LayerMask.GetMask(
                "EnemyHurtbox",
                "Obstacles"
            );
    }
}
