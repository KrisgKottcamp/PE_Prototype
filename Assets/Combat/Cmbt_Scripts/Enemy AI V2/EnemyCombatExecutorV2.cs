using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemyCombatExecutorV2 : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnemyShooterDebug shooter;

        [Header("Compatibility")]
        [Tooltip("If the requested V2 pattern is unavailable in the installed shooter, try a legacy pattern instead.")]
        [SerializeField] private bool fallbackWhenPatternIsUnavailable = true;

        [SerializeField] private string fallbackPattern = "AimedFan";
        [SerializeField] private string finalFallbackPattern = "AimedSingle";
        [SerializeField] private string playerTag = "PlayerCombatPawn";

        [Tooltip("EnemyShooterDebug is a shared weapon component, not legacy decision AI. V2 keeps the component enabled but keeps its shooting gate closed until an attack order starts.")]
        [SerializeField] private bool keepShooterComponentEnabled = true;

        [SerializeField] private bool logAttackFailures = false;

        [Header("Runtime Debug")]
        [SerializeField] private bool attackRunning;
        [SerializeField] private string debugPattern = "None";
        [SerializeField] private string debugRequestedPattern = "None";
        [SerializeField] private string debugPatternShape = "Default";
        [SerializeField] private string debugResult = "Idle";
        [SerializeField] private Transform debugTarget;
        [SerializeField] private Vector2 debugAimOrigin;
        [SerializeField] private Vector2 debugAimTargetPosition;
        [SerializeField] private Vector2 debugAimDirection;
        [SerializeField] private float debugAimDistance;
        [SerializeField] private bool debugShooterEnabled;

        private float attackStartedAt;
        private float lastShotTimeAtStart;
        private bool observedShot;

        public bool IsRunning => attackRunning;
        public bool HasShooter => shooter != null;
        public string DebugResult => debugResult;

        private void Reset()
        {
            ResolveShooter();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                ResolveShooter();
        }
#endif

        private void Awake()
        {
            ResolveShooter();
            EnsureShooterComponentEnabled("Awake");
        }

        private void OnEnable()
        {
            ResolveShooter();
            EnsureShooterComponentEnabled("OnEnable");
        }

        private void Start()
        {
            ResolveShooter();
            EnsureShooterComponentEnabled("Start");
        }

        private void OnDisable()
        {
            CancelAttack("Executor disabled");
        }

        public bool BeginAttack(
            Transform target,
            string patternName,
            int shotsPerBurst,
            float intraBurstInterval,
            float burstCooldown,
            bool resetAimSamples = true,
            float aimLagSeconds = 0f,
            bool overridePatternShape = false,
            int fanBullets = 5,
            float fanArcDegrees = 40f,
            int ringBullets = 8,
            float angularSpeedDegPerTick = 12f)
        {
            ResolveShooter();

            if (shooter == null)
            {
                return Fail("Failed: no EnemyShooterDebug");
            }

            EnsureShooterComponentEnabled("BeginAttack");

            if (target == null)
                target = FindPlayerTarget();

            if (target == null)
            {
                return Fail("Failed: no player target");
            }

            CancelAttack("Replaced");

            // The old brain may have been disabled immediately before this call.
            // Ensure the shared shooter remains alive for the V2 executor.
            shooter.enabled = true;

            // Stage 2 v3: clear the target sample buffer at the start of each V2 attack.
            // This prevents stale aim samples from previous backend modes, cancelled telegraphs,
            // or target refreshes from producing shots that appear to fire away from the player.
            if (resetAimSamples)
                shooter.SetTarget(null);

            shooter.SetTarget(target);
            shooter.SetAimLag(Mathf.Max(0f, aimLagSeconds));

            Vector2 origin = transform.position;
            Transform muzzle = FindLikelyMuzzleTransform();
            if (muzzle != null)
                origin = muzzle.position;

            debugAimOrigin = origin;
            debugAimTargetPosition = target.position;
            debugAimDirection = debugAimTargetPosition - debugAimOrigin;
            debugAimDistance = debugAimDirection.magnitude;
            if (debugAimDirection.sqrMagnitude > 0.0001f)
                debugAimDirection.Normalize();

            debugRequestedPattern = patternName;
            string resolvedPattern = ResolvePattern(patternName);

            if (string.IsNullOrWhiteSpace(resolvedPattern))
                return Fail($"Failed: unknown pattern '{patternName}' and fallbacks unavailable");

            ApplyPatternShapeConfig(
                overridePatternShape,
                fanBullets,
                fanArcDegrees,
                ringBullets,
                angularSpeedDegPerTick
            );

            shooter.SetBurstConfig(
                Mathf.Max(1, shotsPerBurst),
                Mathf.Max(0.03f, intraBurstInterval),
                Mathf.Max(0.05f, burstCooldown)
            );

            shooter.SetBurstQuotaPerEnable(true, 1);
            shooter.SetShootingEnabled(true);
            shooter.ForceReadyToFire(0f);

            attackRunning = true;
            observedShot = false;
            attackStartedAt = Time.time;
            lastShotTimeAtStart = shooter.LastSuccessfulShotTime;
            debugPattern = resolvedPattern;
            debugTarget = target;
            debugShooterEnabled = shooter.enabled;
            debugResult = resolvedPattern == patternName
                ? $"Running: {resolvedPattern}"
                : $"Running fallback: {patternName} -> {resolvedPattern}";

            return true;
        }

        public EnemyActionStatusV2 TickAttack(float timeoutSeconds)
        {
            if (!attackRunning)
                return EnemyActionStatusV2.Failed;

            ResolveShooter();

            if (shooter == null)
            {
                CancelAttack("Shooter missing");
                return EnemyActionStatusV2.Failed;
            }

            debugShooterEnabled = shooter.enabled;

            if (shooter.LastSuccessfulShotTime > lastShotTimeAtStart + 0.0001f)
                observedShot = true;

            if (observedShot &&
                shooter.BurstQuotaReached &&
                !shooter.IsTelegraphing)
            {
                shooter.SetShootingEnabled(false);
                attackRunning = false;
                debugResult = $"Succeeded: {debugPattern}";
                return EnemyActionStatusV2.Succeeded;
            }

            if (Time.time - attackStartedAt >= Mathf.Max(0.15f, timeoutSeconds))
            {
                string reason = shooter.LastBlockReason;
                CancelAttack($"Timeout: {reason}");
                return EnemyActionStatusV2.Failed;
            }

            return EnemyActionStatusV2.Running;
        }

        public void CancelAttack(string reason = "Cancelled")
        {
            if (shooter != null && (attackRunning || shooter.IsShootingEnabled))
                shooter.SetShootingEnabled(false);

            attackRunning = false;
            observedShot = false;
            debugResult = reason;
        }

        private void ResolveShooter()
        {
            if (shooter == null)
                shooter = GetComponent<EnemyShooterDebug>();

            if (shooter == null)
                shooter = GetComponentInChildren<EnemyShooterDebug>(true);
        }

        private void EnsureShooterComponentEnabled(string source)
        {
            if (!keepShooterComponentEnabled || shooter == null)
                return;

            if (!shooter.enabled)
            {
                shooter.enabled = true;
                debugResult = $"Enabled shared shooter component during {source}";
            }

            debugShooterEnabled = shooter.enabled;
        }

        private string ResolvePattern(string requestedPattern)
        {
            if (!string.IsNullOrWhiteSpace(requestedPattern) && shooter.SetPattern(requestedPattern))
                return requestedPattern;

            if (!fallbackWhenPatternIsUnavailable)
                return null;

            string compatibilityFallback = ResolvePrettyPatternCompatibilityFallback(requestedPattern);
            if (!string.IsNullOrWhiteSpace(compatibilityFallback) && shooter.SetPattern(compatibilityFallback))
                return compatibilityFallback;

            if (!string.IsNullOrWhiteSpace(fallbackPattern) && shooter.SetPattern(fallbackPattern))
                return fallbackPattern;

            if (!string.IsNullOrWhiteSpace(finalFallbackPattern) && shooter.SetPattern(finalFallbackPattern))
                return finalFallbackPattern;

            return null;
        }

        private string ResolvePrettyPatternCompatibilityFallback(string requestedPattern)
        {
            if (string.IsNullOrWhiteSpace(requestedPattern))
                return null;

            string key = requestedPattern.Trim().Replace(" ", string.Empty).ToLowerInvariant();
            switch (key)
            {
                case "petalfan": return "AimedFan";
                case "butterflyspread": return "AimedFan";
                case "closingblossom": return "SweepFan";
                case "staggeredrosette": return "Spiral";
                case "crescentsweep": return "SweepFan";
                case "rotatingflowerring": return "Ring";
                case "halospear": return "Ring";
                case "closecross": return "BoI_4Way";
                case "escapecutoff": return "AimedSingle";
                case "braidedstream": return "Spiral";
                default: return null;
            }
        }

        private void ApplyPatternShapeConfig(
            bool overridePatternShape,
            int fanBullets,
            float fanArcDegrees,
            int ringBullets,
            float angularSpeedDegPerTick)
        {
            if (shooter == null)
            {
                debugPatternShape = "No shooter";
                return;
            }

            if (!overridePatternShape)
            {
                debugPatternShape = "Default shooter shape";
                return;
            }

            int safeFanBullets = Mathf.Max(1, fanBullets);
            float safeFanArc = Mathf.Max(0f, fanArcDegrees);
            int safeRingBullets = Mathf.Max(3, ringBullets);
            float safeAngularSpeed = Mathf.Max(0f, angularSpeedDegPerTick);

            shooter.SetFanConfig(safeFanBullets, safeFanArc);
            shooter.SetRingBullets(safeRingBullets);
            shooter.SetAngularSpeed(safeAngularSpeed);

            debugPatternShape =
                $"fan {safeFanBullets}/{safeFanArc:0}°, ring {safeRingBullets}, angular {safeAngularSpeed:0}";
        }

        private Transform FindPlayerTarget()
        {
            if (string.IsNullOrWhiteSpace(playerTag))
                return null;

            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            return player != null ? player.transform : null;
        }

        private Transform FindLikelyMuzzleTransform()
        {
            // EnemyShooterDebug keeps its muzzle private. This is debug-only and
            // intentionally non-authoritative; the shooter still computes its own origin.
            Transform direct = transform.Find("Muzzle");
            if (direct != null)
                return direct;

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child != null && child.name.ToLowerInvariant().Contains("muzzle"))
                    return child;
            }

            return null;
        }

        private bool Fail(string reason)
        {
            debugResult = reason;

            if (logAttackFailures)
                Debug.LogWarning($"[Enemy AI V2] {name}: {reason}", this);

            return false;
        }
    }
}
