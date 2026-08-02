using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-local combat body for Eri.
/// Handles tactical backline cooperation, requested healing delivery,
/// interruption, self-healing, self-revival, and emergency Audrey revival.
/// Persistent HP and healing points live in EriSupportManager.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EriCombatCompanion : MonoBehaviour
{
    public enum CompanionState
    {
        Following,
        Evading,
        Responding,
        Delivering,
        Defeated
    }

    public static EriCombatCompanion ActiveInstance
    {
        get;
        private set;
    }

    [Header("Core References")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private SpriteRenderer[] visualRenderers;
    [SerializeField] private ArenaNavigationGrid navigationGrid;

    [Header("Tactical AI")]
    [SerializeField] private EriTacticalLocomotion tacticalLocomotion;
    [SerializeField] private EriTacticalBrain tacticalBrain;

    [Header("Safest / Quickest Route Balance")]
    [Tooltip(
        "0 makes Eri use the quickest route. 1 strongly favors safety. " +
        "0.67 is the recommended safety-first baseline."
    )]
    [SerializeField, Range(0f, 1f)] private float routeSafetyPriority = 0.67f;

    [Tooltip("Enemies inside this radius make a route less desirable.")]
    [SerializeField, Min(0.2f)] private float routeThreatRadius = 3.8f;

    [Tooltip(
        "Routes passing this close to an enemy receive a severe penalty."
    )]
    [SerializeField, Min(0.05f)] private float routeCriticalDistance = 1.55f;

    [Header("Healing Response Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 4.8f;
    [SerializeField, Min(0.1f)] private float followDistance = 2.1f;
    [SerializeField, Min(0.1f)] private float followTolerance = 0.55f;
    [SerializeField, Min(0.1f)] private float orbitRetargetSeconds = 1.4f;

    [Header("Threat Evaluation")]
    [SerializeField] private LayerMask threatMask;
    [SerializeField, Min(0.1f)] private float threatScanRadius = 4.2f;
    [SerializeField, Min(0.1f)] private float severeThreatRadius = 1.6f;
    [SerializeField, Min(0.1f)] private float evadeDistance = 3.2f;
    [SerializeField, Min(0.02f)] private float threatScanInterval = 0.10f;

    [Header("Healing Delivery")]
    [SerializeField, Min(0.1f)] private float deliveryRange = 1.15f;
    [SerializeField, Min(0f)] private float callerStationarySeconds = 0.30f;
    [SerializeField, Min(0.05f)] private float callerMoveThreshold = 0.05f;
    [SerializeField, Min(0.05f)] private float deliverySeconds = 1f;
    [SerializeField, Min(0f)] private float emergencyRetryDelay = 0.75f;

    [Header("Dangerous Heal Refusal")]
    [SerializeField] private bool refuseDangerousHealingCalls = true;
    [Tooltip("Threats around the caller are considered inside this radius.")]
    [SerializeField, Min(0.2f)] private float refusalDangerScanRadius = 3.25f;
    [Tooltip("One hostile this close to the caller causes an immediate refusal.")]
    [SerializeField, Min(0.05f)] private float refusalImmediateThreatRadius = 1.50f;
    [Tooltip("Several nearby threats can exceed this combined danger score.")]
    [SerializeField, Min(0.05f)] private float refusalDangerScoreThreshold = 1.35f;
    [SerializeField, Min(0f)] private float refusalEnemyProjectileWeight = 1.35f;
    [SerializeField] private Vector2 refusalIconLocalOffset =
        new Vector2(0f, 0.62f);
    [SerializeField, Min(0.15f)] private float refusalIconDuration = 1f;
    [SerializeField, Range(0.08f, 0.80f)] private float refusalIconSize = 0.32f;
    [SerializeField] private Color refusalIconColor =
        new Color(1f, 0.78f, 0.12f, 1f);
    [SerializeField] private int refusalIconSortingOrderOffset = 20;
    [Tooltip("Position of the crossed-out healing icon above the caller.")]
    [SerializeField] private Vector2 refusalCallerIconLocalOffset =
        new Vector2(0f, 0.72f);
    [SerializeField, Range(0.08f, 0.80f)]
    private float refusalCallerIconSize = 0.34f;
    [SerializeField] private int refusalCallerIconSortingOrderOffset = 20;
    [SerializeField] private bool logDangerousHealingRefusals;

    [Header("Healing Momentum")]
    [Tooltip(
        "Raw Momentum granted only after Eri successfully heals a living " +
        "party member. The Momentum manager applies global and tier scaling.")]
    [SerializeField, Min(0f)] private float successfulHealMomentum = 12f;
    [Tooltip(
        "Raw Momentum granted after a successful player-requested revival.")]
    [SerializeField, Min(0f)] private float successfulReviveMomentum = 18f;
    [Tooltip(
        "If disabled, Eri's automatic full-party recovery does not reward " +
        "Momentum. Manual revival requests still do.")]
    [SerializeField] private bool emergencyReviveGrantsMomentum;
    [SerializeField] private bool logHealingMomentum;

    [Header("Eri Survival")]
    [SerializeField, Range(0.01f, 1f)] private float automaticSelfHealThreshold = 0.25f;
    [SerializeField, Min(0.1f)] private float selfReviveDelaySeconds = 15f;

    [Header("Pathing")]
    [SerializeField, Min(0.05f)] private float pathRefreshSeconds = 0.25f;
    [SerializeField, Min(0.02f)] private float waypointArrivalDistance = 0.14f;

    [Header("Feedback")]
    [SerializeField] private DamageFlash2D damageFlash;
    [SerializeField] private HealingFeedback2D eriHealingFeedback;
    [SerializeField] private bool logStateChanges;

    [Header("World Status Arcs")]
    [SerializeField] private bool enableWorldStatusArcs = true;
    [SerializeField] private Vector2 worldStatusLocalOffset =
        new Vector2(0f, -0.28f);
    [SerializeField, Range(0.08f, 0.80f)] private float worldHealthArcRadius = 0.25f;
    [SerializeField, Range(8, 32)] private int worldHealthArcSegments = 18;
    [SerializeField, Range(0.008f, 0.08f)] private float worldHealthArcThickness = 0.018f;
    [SerializeField] private Color worldHealthFillColor =
        new Color(0.94f, 0.18f, 0.16f, 0.92f);
    [SerializeField] private Color worldHealthEmptyColor =
        new Color(0.22f, 0.035f, 0.03f, 0.42f);
    [SerializeField, Range(0.10f, 0.90f)] private float worldHealingArcRadius = 0.34f;
    [SerializeField, Range(0.008f, 0.10f)] private float worldHealingArcThickness = 0.026f;
    [SerializeField] private Color worldHealingFillColor =
        new Color(0.18f, 1f, 0.54f, 0.94f);
    [SerializeField] private Color worldHealingEmptyColor =
        new Color(0.025f, 0.20f, 0.10f, 0.42f);
    [SerializeField] private int worldStatusSortingOrderOffset = 4;

    [Header("Runtime Debug")]
    [SerializeField] private CompanionState state;
    [SerializeField] private int pendingTargetIndex = -1;
    [SerializeField] private bool pendingTargetWasDowned;
    [SerializeField] private bool emergencyRequest;
    [SerializeField] private float selfReviveRemaining;
    [SerializeField] private string lastCancelReason = "None";
    [SerializeField] private int lastRefusalThreatCount;
    [SerializeField] private float lastRefusalDangerScore;

    private readonly Collider2D[] threatHits =
        new Collider2D[32];

    private readonly List<Vector2> path =
        new List<Vector2>();

    private readonly HashSet<int> refusalThreatIds =
        new HashSet<int>();

    private EriSupportManager support;
    private EriWorldStatusDisplay2D worldStatusDisplay;
    private EriRefusalFeedback2D refusalFeedback;
    private float nextWorldStatusConfigureTime;
    private CombatPawn callerPawn;
    private Transform caller;
    private Rigidbody2D callerBody;
    private CombatLockout callerLockout;
    private HealingFeedback2D callerHealingFeedback;

    private Coroutine deliveryRoutine;
    private bool deliveryInterrupted;

    private Vector2 followOffset;
    private Vector2 moveDestination;
    private Vector2 lastPathDestination;
    private Vector3 previousCallerPosition;

    private int pathIndex;
    private float nextOrbitRetargetTime;
    private float nextThreatScanTime;
    private float nextPathRefreshTime;
    private float callerStationaryTimer;
    private float emergencyRetryAvailableAt;

    private bool severeThreatNearby;
    private Vector2 evadeDirection;

    public CompanionState State => state;
    public bool HasPendingRequest =>
        pendingTargetIndex != -1;
    public bool IsDelivering =>
        state == CompanionState.Delivering;

    private void Awake()
    {
        if (ActiveInstance != null &&
            ActiveInstance != this)
        {
            Destroy(gameObject);
            return;
        }

        ActiveInstance = this;

        if (body == null)
            body = GetComponent<Rigidbody2D>();

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (visualRenderers == null ||
            visualRenderers.Length == 0)
        {
            visualRenderers =
                GetComponentsInChildren<SpriteRenderer>(true);
        }

        if (navigationGrid == null)
        {
            navigationGrid =
                FindObjectOfType<ArenaNavigationGrid>(true);
        }

        if (tacticalLocomotion == null)
        {
            tacticalLocomotion =
                GetComponent<EriTacticalLocomotion>();
        }

        if (tacticalLocomotion == null)
        {
            tacticalLocomotion =
                gameObject.
                    AddComponent<EriTacticalLocomotion>();
        }

        tacticalLocomotion.Configure(
            body,
            navigationGrid
        );

        if (tacticalBrain == null)
            tacticalBrain = GetComponent<EriTacticalBrain>();

        if (tacticalBrain == null)
        {
            tacticalBrain =
                gameObject.
                    AddComponent<EriTacticalBrain>();
        }

        tacticalBrain.Configure(
            tacticalLocomotion,
            navigationGrid,
            bodyCollider
        );

        tacticalBrain.ConfigureRouteSafety(
            routeSafetyPriority,
            routeThreatRadius,
            routeCriticalDistance
        );

        if (damageFlash == null)
            damageFlash = GetComponent<DamageFlash2D>();

        if (damageFlash == null)
            damageFlash = gameObject.AddComponent<DamageFlash2D>();

        if (!damageFlash.HasConfiguredTargets)
            damageFlash.ConfigureTargets(visualRenderers);

        if (eriHealingFeedback == null)
            eriHealingFeedback = GetComponent<HealingFeedback2D>();

        if (eriHealingFeedback == null)
            eriHealingFeedback = gameObject.AddComponent<HealingFeedback2D>();

        body.gravityScale = 0f;
        body.freezeRotation = true;

        if (threatMask.value == 0)
        {
            threatMask =
                LayerMask.GetMask(
                    "Projectile",
                    "EnemyHurtbox"
                );
        }

        PickNewFollowOffset();
        ResolveCaller();
        ResolveSupport();
        ConfigureWorldStatusDisplay();
    }

    private void OnEnable()
    {
        CombatPawn.AcceptedDamage +=
            OnCallerAcceptedDamage;
    }

    private void OnDisable()
    {
        CombatPawn.AcceptedDamage -=
            OnCallerAcceptedDamage;

        ReleaseCallerLock();
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    private void Start()
    {
        ResolveSupport();
        ResolveCaller();

        bool defeated =
            support != null &&
            support.IsEriDefeated;

        if (defeated)
        {
            selfReviveRemaining =
                selfReviveDelaySeconds;
        }

        SetDefeatedVisual(defeated);
        SetState(
            defeated
                ? CompanionState.Defeated
                : CompanionState.Following
        );
    }

    private void Update()
    {
        ResolveSupport();
        ResolveCaller();

        if (Time.unscaledTime >= nextWorldStatusConfigureTime)
            ConfigureWorldStatusDisplay();

        if (support == null)
            return;

        if (support.IsEriDefeated)
        {
            TickDefeated();
            return;
        }

        if (state == CompanionState.Defeated)
        {
            SetDefeatedVisual(false);
            SetState(
                HasPendingRequest
                    ? CompanionState.Responding
                    : CompanionState.Following
            );
        }

        TryAutomaticSelfHeal();
        UpdateCallerStationaryTimer();
        ScanThreatsIfNeeded();

        if (state == CompanionState.Delivering)
            return;

        if (HasPendingRequest)
        {
            TickPendingRequest();
            return;
        }

        TickLooseFollow();
    }

    private void FixedUpdate()
    {
        // New tactical locomotion owns Rigidbody2D movement. Keep the original
        // mover below as a safe fallback for older prefabs while scripts reload.
        if (tacticalLocomotion != null)
            return;

        if (body == null ||
            support == null ||
            support.IsEriDefeated ||
            state == CompanionState.Delivering)
        {
            return;
        }

        Vector2 steeringTarget =
            ResolveSteeringTarget(
                body.position,
                moveDestination
            );

        Vector2 delta =
            steeringTarget - body.position;

        if (delta.sqrMagnitude <=
            waypointArrivalDistance *
            waypointArrivalDistance)
        {
            return;
        }

        Vector2 step =
            delta.normalized *
            Mathf.Max(0f, moveSpeed) *
            Time.fixedDeltaTime;

        if (step.sqrMagnitude >
            delta.sqrMagnitude)
        {
            step = delta;
        }

        body.MovePosition(
            body.position + step
        );
    }

    public bool TryRequestHealing(
        Transform requestingCaller,
        int partyTargetIndex)
    {
        ResolveSupport();

        bool targetsEri =
            partyTargetIndex ==
            EriSupportManager.SelfTargetIndex;

        if (support == null ||
            support.IsEriDefeated ||
            HasPendingRequest ||
            !support.CanRequestPartyTarget(
                partyTargetIndex))
        {
            return false;
        }

        PartyManager pm =
            PartyManager.Instance;

        if (!targetsEri &&
            (pm == null ||
             partyTargetIndex < 0 ||
             partyTargetIndex >= pm.party.Count))
        {
            return false;
        }

        Transform resolvedCaller =
            requestingCaller != null
                ? requestingCaller
                : ResolveCallerTransform();

        if (!targetsEri &&
            ShouldRefuseDangerousHealingCall(resolvedCaller))
        {
            lastCancelReason =
                $"Refused dangerous healing call " +
                $"({lastRefusalDangerScore:0.00})";
            refusalFeedback?.Play();
            PlayCallerRefusalFeedback(resolvedCaller);

            if (logDangerousHealingRefusals)
            {
                Debug.Log(
                    $"Eri refused Call Eri: " +
                    $"{lastRefusalThreatCount} threat(s), " +
                    $"danger {lastRefusalDangerScore:0.00}/" +
                    $"{refusalDangerScoreThreshold:0.00}.",
                    this);
            }

            return false;
        }

        caller =
            resolvedCaller;

        CacheCallerComponents();

        pendingTargetIndex =
            partyTargetIndex;

        pendingTargetWasDowned =
            !targetsEri &&
            pm.party[partyTargetIndex].currentHP <= 0;

        emergencyRequest = false;
        deliveryInterrupted = false;
        callerStationaryTimer = 0f;

        if (caller != null)
            previousCallerPosition = caller.position;

        tacticalBrain?.Halt(
            "Healing response",
            false);
        SetState(CompanionState.Responding);
        return true;
    }

    public bool TryBeginEmergencyAudreyRevival()
    {
        ResolveSupport();
        ResolveCaller();

        if (support == null)
        {
            return false;
        }

        bool canBeginNow =
            support.CanEmergencyRevive(
                out int audreyIndex);

        bool canBeginAfterSelfRevive =
            !canBeginNow &&
            support.CanEventuallyEmergencyRevive(
                out audreyIndex);

        if (!canBeginNow &&
            !canBeginAfterSelfRevive)
        {
            return false;
        }

        pendingTargetIndex =
            audreyIndex;

        pendingTargetWasDowned = true;
        emergencyRequest = true;
        deliveryInterrupted = false;
        callerStationaryTimer =
            callerStationarySeconds;

        caller =
            ResolveCallerTransform();

        CacheCallerComponents();
        AcquireCallerLock();

        if (caller != null)
            previousCallerPosition = caller.position;

        tacticalBrain?.Halt(
            "Emergency healing response",
            false);
        SetState(CompanionState.Responding);
        return true;
    }

    public void CancelPendingRequest(
        string reason = "Cancelled")
    {
        if (!HasPendingRequest)
            return;

        lastCancelReason = reason;

        if (deliveryRoutine != null)
        {
            deliveryInterrupted = true;
            return;
        }

        ClearRequest();
    }

    /// <summary>
    /// Enemy projectile scripts use SendMessage("ApplyDamage").
    /// </summary>
    public void ApplyDamage(int amount)
    {
        ResolveSupport();

        if (support == null ||
            amount <= 0 ||
            support.IsEriDefeated)
        {
            return;
        }

        int actualDamage =
            support.ApplyDamageToEri(amount);

        if (actualDamage <= 0)
            return;

        damageFlash?.PlayFlash();

        if (state == CompanionState.Delivering)
            deliveryInterrupted = true;

        if (support.IsEriDefeated)
            EnterDefeatedState();
    }

    private void TickPendingRequest()
    {
        if (!ValidatePendingTarget())
            return;

        bool targetsEri =
            pendingTargetIndex ==
            EriSupportManager.SelfTargetIndex;

        if (!targetsEri && caller == null)
        {
            CancelPendingRequest(
                "Caller missing"
            );
            return;
        }

        if (severeThreatNearby)
        {
            SetState(CompanionState.Evading);
            moveDestination =
                (Vector2)transform.position +
                evadeDirection *
                evadeDistance;

            SetMovementDestination(
                moveDestination,
                1.22f,
                true
            );
            return;
        }

        if (targetsEri)
        {
            SetState(CompanionState.Responding);
            moveDestination = transform.position;
            tacticalLocomotion?.ClearDestination(
                "Manual Eri self-heal");

            if (Time.time >= emergencyRetryAvailableAt)
            {
                deliveryRoutine =
                    StartCoroutine(DeliveryRoutine());
            }

            return;
        }

        SetState(CompanionState.Responding);

        bool callerStopped =
            emergencyRequest ||
            callerStationaryTimer >=
            callerStationarySeconds;

        float distanceToCaller =
            Vector2.Distance(
                transform.position,
                caller.position
            );

        if (callerStopped)
        {
            moveDestination =
                caller.position;
        }
        else
        {
            moveDestination =
                (Vector2)caller.position +
                followOffset;
        }

        SetMovementDestination(
            moveDestination,
            1.08f
        );

        if (callerStopped &&
            distanceToCaller <=
            deliveryRange &&
            Time.time >=
            emergencyRetryAvailableAt)
        {
            deliveryRoutine =
                StartCoroutine(
                    DeliveryRoutine()
                );
        }
    }

    private IEnumerator DeliveryRoutine()
    {
        SetState(CompanionState.Delivering);
        deliveryInterrupted = false;
        tacticalBrain?.Halt(
            "Healing delivery",
            true
        );

        bool targetsEri =
            pendingTargetIndex ==
            EriSupportManager.SelfTargetIndex;

        if (!targetsEri)
            AcquireCallerLock();

        float elapsed = 0f;

        while (elapsed <
            Mathf.Max(0.05f, deliverySeconds))
        {
            if (deliveryInterrupted ||
                support == null ||
                support.IsEriDefeated ||
                !ValidatePendingTarget(
                    cancelOnFailure: false))
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        bool completed =
            !deliveryInterrupted &&
            elapsed >=
            Mathf.Max(0.05f, deliverySeconds) &&
            support != null &&
            !support.IsEriDefeated;

        EriHealingDeliveryResult result =
            EriHealingDeliveryResult.EriUnavailable;

        if (completed)
        {
            result =
                support.TryDeliverToPartyMember(
                    pendingTargetIndex,
                    pendingTargetWasDowned
                );

            completed =
                result ==
                EriHealingDeliveryResult.Success;
        }

        ReleaseCallerLock();
        deliveryRoutine = null;

        if (completed)
        {
            bool completedRevival = pendingTargetWasDowned;
            bool completedEmergencyRevival = emergencyRequest;

            eriHealingFeedback?.
                PlayHealingFeedback();

            if (!targetsEri)
            {
                callerHealingFeedback?.
                    PlayHealingFeedback();
            }

            AwardHealingMomentum(
                completedRevival,
                completedEmergencyRevival);

            if (emergencyRequest)
                CompleteEmergencyRevival();

            ClearRequest();
            yield break;
        }

        if (emergencyRequest &&
            support != null &&
            support.CanEmergencyRevive(
                out _))
        {
            emergencyRetryAvailableAt =
                Time.time +
                emergencyRetryDelay;

            deliveryInterrupted = false;
            SetState(
                CompanionState.Responding
            );
            yield break;
        }

        lastCancelReason =
            deliveryInterrupted
                ? "Delivery interrupted by damage"
                : result.ToString();

        ClearRequest();

        CombatManager.Instance?.
            ReevaluateDeferredPartyDefeat();
    }

    private void AwardHealingMomentum(
        bool wasRevival,
        bool wasEmergencyRevival)
    {
        if (wasEmergencyRevival &&
            !emergencyReviveGrantsMomentum)
        {
            return;
        }

        float rawMomentum = wasRevival
            ? successfulReviveMomentum
            : successfulHealMomentum;

        if (rawMomentum <= 0f)
            return;

        AttackMomentumManager manager =
            AttackMomentumManager.Instance;

        if (manager == null)
            return;

        manager.RegisterSuccessfulSkill(rawMomentum);

        if (logHealingMomentum)
        {
            Debug.Log(
                $"Eri {(wasRevival ? "revive" : "heal")} " +
                $"Momentum +{rawMomentum:0.##} raw.",
                this);
        }
    }

    private void CompleteEmergencyRevival()
    {
        PartyManager pm =
            PartyManager.Instance;

        if (pm == null ||
            pendingTargetIndex < 0 ||
            pendingTargetIndex >= pm.party.Count)
        {
            return;
        }

        pm.activeIndex =
            pendingTargetIndex;

        callerPawn?.
            ReviveAfterEri();

        callerLockout?.
            TriggerLockout();

        CombatManager.Instance?.
            ReevaluateDeferredPartyDefeat();
    }

    private bool ValidatePendingTarget(
        bool cancelOnFailure = true)
    {
        if (!HasPendingRequest ||
            support == null)
        {
            return false;
        }

        bool targetsEri =
            pendingTargetIndex ==
            EriSupportManager.SelfTargetIndex;

        PartyManager pm = PartyManager.Instance;

        bool valid = targetsEri
            ? support.CanRequestPartyTarget(
                EriSupportManager.SelfTargetIndex)
            : pm != null &&
              pm.party != null &&
              pendingTargetIndex >= 0 &&
              pendingTargetIndex < pm.party.Count;

        if (valid && !targetsEri)
        {
            PartyManager.CharacterState target =
                pm.party[pendingTargetIndex];

            valid =
                target != null &&
                target.def != null &&
                (target.currentHP <= 0) ==
                pendingTargetWasDowned &&
                support.CurrentHealingPoints >=
                support.GetPartyTargetPointCost(
                    pendingTargetIndex
                );
        }

        if (!valid && cancelOnFailure)
        {
            lastCancelReason =
                "Target state or healing-point availability changed";

            ClearRequest();

            CombatManager.Instance?.
                ReevaluateDeferredPartyDefeat();
        }

        return valid;
    }

    private void TickLooseFollow()
    {
        if (caller == null)
            return;

        if (tacticalBrain != null)
        {
            bool evading =
                tacticalBrain.TickTactics(
                    caller,
                    severeThreatNearby,
                    evadeDirection
                );

            SetState(
                evading
                    ? CompanionState.Evading
                    : CompanionState.Following
            );
            return;
        }

        if (severeThreatNearby)
        {
            SetState(CompanionState.Evading);
            moveDestination =
                (Vector2)transform.position +
                evadeDirection *
                evadeDistance;
            return;
        }

        SetState(CompanionState.Following);

        if (Time.time >=
            nextOrbitRetargetTime)
        {
            PickNewFollowOffset();
        }

        Vector2 desired =
            (Vector2)caller.position +
            followOffset;

        float distance =
            Vector2.Distance(
                transform.position,
                desired
            );

        if (distance >
            followTolerance)
        {
            moveDestination = desired;
        }
        else
        {
            moveDestination =
                transform.position;
        }
    }

    private void ScanThreatsIfNeeded()
    {
        if (Time.time <
            nextThreatScanTime)
        {
            return;
        }

        nextThreatScanTime =
            Time.time +
            threatScanInterval;

        severeThreatNearby = false;
        evadeDirection = Vector2.zero;

        if (threatMask.value == 0)
            return;

        int count =
            Physics2D.OverlapCircleNonAlloc(
                transform.position,
                threatScanRadius,
                threatHits,
                threatMask
            );

        float closest =
            float.PositiveInfinity;

        Vector2 myPosition =
            transform.position;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit =
                threatHits[i];

            if (hit == null ||
                hit == bodyCollider ||
                hit.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector2 away =
                myPosition -
                (Vector2)hit.bounds.center;

            float distance =
                away.magnitude;

            if (distance <
                closest)
            {
                closest = distance;
            }

            float weight =
                1f /
                Mathf.Max(
                    0.15f,
                    distance
                );

            evadeDirection +=
                away.normalized *
                weight;
        }

        severeThreatNearby =
            closest <=
            severeThreatRadius;

        if (evadeDirection.sqrMagnitude <=
            0.0001f)
        {
            evadeDirection =
                Random.insideUnitCircle.normalized;
        }
        else
        {
            evadeDirection.Normalize();
        }
    }

    private void UpdateCallerStationaryTimer()
    {
        if (caller == null)
        {
            callerStationaryTimer = 0f;
            return;
        }

        float movement =
            Vector2.Distance(
                previousCallerPosition,
                caller.position
            );

        previousCallerPosition =
            caller.position;

        float threshold =
            callerMoveThreshold *
            Mathf.Max(
                Time.deltaTime,
                0.0001f
            );

        if (movement <= threshold)
        {
            callerStationaryTimer +=
                Time.deltaTime;
        }
        else
        {
            callerStationaryTimer = 0f;
        }
    }

    private void TryAutomaticSelfHeal()
    {
        if (support == null ||
            support.IsEriDefeated ||
            support.CurrentHealingPoints <= 1)
        {
            return;
        }

        float health01 =
            support.EriCurrentHP /
            (float)support.EriMaximumHP;

        if (health01 >
            automaticSelfHealThreshold)
        {
            return;
        }

        if (support.TryHealEriFromPool())
        {
            eriHealingFeedback?.
                PlayHealingFeedback();
        }
    }

    private void EnterDefeatedState()
    {
        deliveryInterrupted = true;
        ReleaseCallerLock();
        tacticalBrain?.Halt(
            "Eri defeated",
            true
        );

        selfReviveRemaining =
            selfReviveDelaySeconds;

        SetDefeatedVisual(true);
        SetState(CompanionState.Defeated);
    }

    private void TickDefeated()
    {
        SetState(CompanionState.Defeated);

        selfReviveRemaining -=
            Time.deltaTime;

        if (selfReviveRemaining > 0f)
            return;

        if (support != null &&
            support.TryReviveEriFromPool())
        {
            SetDefeatedVisual(false);
            eriHealingFeedback?.
                PlayHealingFeedback();

            SetState(
                HasPendingRequest
                    ? CompanionState.Responding
                    : CompanionState.Following
            );

            return;
        }

        selfReviveRemaining = 0f;

        CombatManager.Instance?.
            ReevaluateDeferredPartyDefeat();
    }

    private Vector2 ResolveSteeringTarget(
        Vector2 origin,
        Vector2 destination)
    {
        if (navigationGrid == null ||
            !navigationGrid.IsBuilt)
        {
            return destination;
        }

        bool refresh =
            Time.time >=
            nextPathRefreshTime ||
            path.Count == 0 ||
            Vector2.Distance(
                destination,
                lastPathDestination
            ) > 0.35f;

        if (refresh)
        {
            nextPathRefreshTime =
                Time.time +
                pathRefreshSeconds;

            path.Clear();
            pathIndex = 0;

            Vector2 validatedDestination =
                navigationGrid.
                FindNearestWalkablePosition(
                    destination
                );

            navigationGrid.TryFindPath(
                origin,
                validatedDestination,
                path
            );

            lastPathDestination =
                destination;
        }

        while (pathIndex <
            path.Count - 1 &&
            Vector2.Distance(
                origin,
                path[pathIndex]
            ) <= waypointArrivalDistance)
        {
            pathIndex++;
        }

        if (path.Count > 0 &&
            pathIndex < path.Count)
        {
            return path[pathIndex];
        }

        return destination;
    }

    private void SetMovementDestination(
        Vector2 destination,
        float speedMultiplier = 1f,
        bool forceRefresh = false)
    {
        if (tacticalLocomotion == null)
            return;

        bool accepted =
            tacticalLocomotion.SetDestination(
                destination,
                speedMultiplier,
                forceRefresh
            );

        // A healing call is an explicit player request and outranks failed-slot
        // memory. Recovery can try the caller again after clearing that memory.
        if (!accepted)
        {
            tacticalLocomotion.
                ForgetFailedDestination();

            tacticalLocomotion.SetDestination(
                destination,
                speedMultiplier,
                true
            );
        }
    }

    private void ResolveSupport()
    {
        if (support == null)
            support = EriSupportManager.Instance;
    }

    private void ConfigureWorldStatusDisplay()
    {
        nextWorldStatusConfigureTime = Time.unscaledTime + 0.5f;

        if (worldStatusDisplay == null)
        {
            worldStatusDisplay =
                GetComponent<EriWorldStatusDisplay2D>();
        }

        if (worldStatusDisplay == null)
        {
            worldStatusDisplay =
                gameObject.AddComponent<EriWorldStatusDisplay2D>();
        }

        worldStatusDisplay.Configure(
            visualRenderers,
            worldStatusLocalOffset,
            worldHealthArcRadius,
            worldHealthArcSegments,
            worldHealthArcThickness,
            worldHealthFillColor,
            worldHealthEmptyColor,
            worldHealingArcRadius,
            worldHealingArcThickness,
            worldHealingFillColor,
            worldHealingEmptyColor,
            worldStatusSortingOrderOffset);
        worldStatusDisplay.SetPresentationEnabled(
            enableWorldStatusArcs);

        ConfigureRefusalFeedback();
    }

    private void ConfigureRefusalFeedback()
    {
        if (refusalFeedback == null)
        {
            refusalFeedback =
                GetComponent<EriRefusalFeedback2D>();
        }

        if (refusalFeedback == null)
        {
            refusalFeedback =
                gameObject.AddComponent<EriRefusalFeedback2D>();
        }

        refusalFeedback.Configure(
            visualRenderers,
            refusalIconLocalOffset,
            refusalIconDuration,
            refusalIconSize,
            refusalIconColor,
            refusalIconSortingOrderOffset);
    }

    private void PlayCallerRefusalFeedback(
        Transform callerTransform)
    {
        if (callerTransform == null)
            return;

        EriHealRefusalTargetFeedback2D callerFeedback =
            callerTransform.GetComponent<
                EriHealRefusalTargetFeedback2D>();

        if (callerFeedback == null)
        {
            callerFeedback =
                callerTransform.gameObject.AddComponent<
                    EriHealRefusalTargetFeedback2D>();
        }

        callerFeedback.Configure(
            refusalCallerIconLocalOffset,
            refusalIconDuration,
            refusalCallerIconSize,
            refusalCallerIconSortingOrderOffset);
        callerFeedback.Play();
    }

    private bool ShouldRefuseDangerousHealingCall(
        Transform callerTransform)
    {
        lastRefusalThreatCount = 0;
        lastRefusalDangerScore = 0f;
        refusalThreatIds.Clear();

        if (!refuseDangerousHealingCalls ||
            callerTransform == null ||
            threatMask.value == 0)
        {
            return false;
        }

        Vector2 callerPosition =
            callerTransform.position;

        float scanRadius =
            Mathf.Max(0.2f, refusalDangerScanRadius);

        float immediateRadius =
            Mathf.Min(
                scanRadius,
                Mathf.Max(0.05f, refusalImmediateThreatRadius));

        int count =
            Physics2D.OverlapCircleNonAlloc(
                callerPosition,
                scanRadius,
                threatHits,
                threatMask);

        bool immediateThreat = false;
        float dangerScore = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = threatHits[i];

            if (hit == null ||
                hit == bodyCollider ||
                hit.transform.IsChildOf(transform) ||
                hit.transform.root == callerTransform.root)
            {
                continue;
            }

            int threatId;
            float threatWeight;

            Projectile projectile =
                hit.GetComponentInParent<Projectile>();

            if (projectile != null)
            {
                // Reflected and player-owned shots are allies, not danger.
                if (projectile.Team !=
                    Projectile.ProjectileTeam.Enemy)
                {
                    continue;
                }

                threatId = projectile.GetInstanceID();
                threatWeight = refusalEnemyProjectileWeight;
            }
            else
            {
                EnemyHealth enemy =
                    hit.GetComponentInParent<EnemyHealth>();

                if (enemy != null)
                {
                    if (enemy.CurrentHP <= 0)
                        continue;

                    threatId = enemy.GetInstanceID();
                }
                else
                {
                    threatId =
                        hit.transform.root.GetInstanceID();
                }

                threatWeight = 1f;
            }

            // An enemy can own several hurtbox colliders. It still counts once.
            if (!refusalThreatIds.Add(threatId))
                continue;

            Vector2 closestPoint =
                hit.ClosestPoint(callerPosition);

            float distance =
                Vector2.Distance(callerPosition, closestPoint);

            float proximity =
                1f - Mathf.Clamp01(distance / scanRadius);

            dangerScore +=
                proximity * Mathf.Max(0f, threatWeight);

            lastRefusalThreatCount++;

            if (distance <= immediateRadius)
                immediateThreat = true;
        }

        lastRefusalDangerScore = dangerScore;

        if (immediateThreat)
        {
            lastRefusalDangerScore =
                Mathf.Max(
                    lastRefusalDangerScore,
                    refusalDangerScoreThreshold);
            return true;
        }

        return dangerScore >=
            Mathf.Max(0.05f, refusalDangerScoreThreshold);
    }

    private void ResolveCaller()
    {
        if (caller != null &&
            callerPawn != null)
        {
            return;
        }

        callerPawn =
            FindObjectOfType<CombatPawn>(true);

        caller =
            callerPawn != null
                ? callerPawn.transform
                : null;

        CacheCallerComponents();

        if (caller != null)
            previousCallerPosition = caller.position;
    }

    private Transform ResolveCallerTransform()
    {
        ResolveCaller();
        return caller;
    }

    private void CacheCallerComponents()
    {
        if (caller == null)
            return;

        callerPawn =
            caller.GetComponent<CombatPawn>();

        if (callerPawn == null)
        {
            callerPawn =
                caller.GetComponentInParent<CombatPawn>();
        }

        callerBody =
            caller.GetComponent<Rigidbody2D>();

        callerLockout =
            caller.GetComponent<CombatLockout>();

        callerHealingFeedback =
            caller.GetComponent<HealingFeedback2D>();
    }

    private void AcquireCallerLock()
    {
        callerLockout?.
            AcquireExternalLock(this);
    }

    private void ReleaseCallerLock()
    {
        callerLockout?.
            ReleaseExternalLock(this);
    }

    private void OnCallerAcceptedDamage(
        int partyIndex,
        int actualDamage)
    {
        if (actualDamage <= 0 ||
            state != CompanionState.Delivering ||
            pendingTargetIndex ==
            EriSupportManager.SelfTargetIndex)
        {
            return;
        }

        deliveryInterrupted = true;
    }

    private void ClearRequest()
    {
        pendingTargetIndex = -1;
        pendingTargetWasDowned = false;
        emergencyRequest = false;
        deliveryInterrupted = false;
        callerStationaryTimer = 0f;

        ReleaseCallerLock();

        tacticalLocomotion?.ClearDestination(
            "Healing request complete"
        );

        SetState(
            support != null &&
            support.IsEriDefeated
                ? CompanionState.Defeated
                : CompanionState.Following
        );
    }

    private void PickNewFollowOffset()
    {
        Vector2 direction =
            Random.insideUnitCircle;

        if (direction.sqrMagnitude <=
            0.0001f)
        {
            direction = Vector2.left;
        }

        followOffset =
            direction.normalized *
            followDistance;

        nextOrbitRetargetTime =
            Time.time +
            orbitRetargetSeconds;
    }

    private void SetDefeatedVisual(bool defeated)
    {
        if (visualRenderers != null)
        {
            for (int i = 0;
                i < visualRenderers.Length;
                i++)
            {
                if (visualRenderers[i] != null)
                {
                    visualRenderers[i].enabled =
                        !defeated;
                }
            }
        }

        if (bodyCollider != null)
            bodyCollider.enabled = !defeated;

        if (body != null)
            body.simulated = !defeated;
    }

    private void SetState(
        CompanionState newState)
    {
        if (state == newState)
            return;

        if (logStateChanges)
        {
            Debug.Log(
                $"Eri: {state} -> {newState}",
                this
            );
        }

        state = newState;
    }
}
