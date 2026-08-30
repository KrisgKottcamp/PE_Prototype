using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyLocomotionV2))]
    [RequireComponent(typeof(EnemyCombatExecutorV2))]
    [RequireComponent(typeof(EnemyActionRunnerV2))]
    [RequireComponent(typeof(EnemySlowReceiverV2))]
    public sealed class EnemyAgentV2 : MonoBehaviour
    {
        [Header("Capabilities")]
        [SerializeField] private bool isRanged = true;
        [SerializeField] private bool eligibleForController = true;
        [SerializeField] private bool eligibleForFlanker = true;
        [SerializeField] private bool eligibleForSentinel = true;
        [SerializeField] private bool eligibleForSoloDuel = true;

        [Header("References")]
        [SerializeField] private EnemyHealth enemyHealth;
        [SerializeField] private EnemyLocomotionV2 locomotion;
        [SerializeField] private EnemyActionRunnerV2 actionRunner;
        [SerializeField] private EnemyVisualGuardV2 visualGuard;
        [SerializeField] private EnemySlowReceiverV2 slowReceiver;
        [SerializeField] private Behaviour[] legacyAIBehaviours;

        [Header("Legacy Compatibility")]
        [Tooltip("Keeps child visuals in the same enabled/active state they had before legacy AI is disabled.")]
        [SerializeField] private bool preserveChildVisualsWhenSwitchingBackend = true;

        [Tooltip("Leave empty to cache all child SpriteRenderers automatically. Disabled renderers stay disabled.")]
        [SerializeField] private SpriteRenderer[] protectedSpriteRenderers;

        [Tooltip("Optional visual child roots that must remain active when V2 takes control.")]
        [SerializeField] private GameObject[] protectedVisualObjects;

        [SerializeField] private bool logBackendCompatibility = false;

        [Header("Shared Runtime Components v5 / Stage 2 Safe Runtime")]
        [Tooltip("V2 replaces the old decision brain, but shared weapon/telegraph components must stay enabled. Keep this on.")]
        [SerializeField] private bool forceSharedRuntimeComponentsEnabled = true;

        [Tooltip("Component type names that should stay enabled even when the old EnemyBrain is disabled. This intentionally includes EnemyShooterDebug.")]
        [SerializeField] private string[] forcedEnabledComponentTypeNames = new string[]
        {
            "EnemyShooterDebug",
            "EnemyAttackTelegraph"
        };

        [SerializeField] private bool logForcedRuntimeComponents = false;
        [SerializeField] private string debugForcedRuntimeComponents = "None";

        [Header("Runtime Assignment")]
        [SerializeField] private EnemyRoleV2 currentRole = EnemyRoleV2.Unassigned;
        [SerializeField] private EnemySectorV2 currentSector = EnemySectorV2.None;
        [SerializeField] private Vector2 assignedSlot;
        [SerializeField] private string debugAssignmentReason = "Unassigned";
        [SerializeField] private bool legacyDisabledByV2;
        [SerializeField] private bool debugRuntimeReady;
        [SerializeField] private string debugRuntimeReadiness = "Not initialized";
        [SerializeField] private bool debugHasPlayerTarget;
        [SerializeField] private bool debugHasProfile;

        private SquadDirectorV2 director;
        private Transform playerTarget;
        private EnemyAIV2Profile runtimeProfile;
        private ArenaNavigationGrid runtimeGrid;

        private bool[] cachedRendererEnabled;
        private Color[] cachedRendererColors;
        private bool[] cachedVisualObjectActive;

        public SquadDirectorV2 Director => director;
        public Transform PlayerTarget => playerTarget;
        public EnemyAIV2Profile Profile => runtimeProfile;
        public EnemyActionRunnerV2 ActionRunner => actionRunner;
        public EnemyLocomotionV2 Locomotion => locomotion;
        public ArenaNavigationGrid NavigationGrid => runtimeGrid;
        public Vector2 CurrentPosition => transform.position;
        public string DebugAssignmentReason => debugAssignmentReason;
        public EnemyRoleV2 CurrentRole => currentRole;
        public EnemySectorV2 CurrentSector => currentSector;
        public Vector2 AssignedSlot => assignedSlot;
        public bool IsRanged => isRanged;
        public bool IsAlive => enemyHealth == null || enemyHealth.CurrentHP > 0;
        public bool EligibleForController => eligibleForController;
        public bool EligibleForFlanker => eligibleForFlanker;
        public bool EligibleForSentinel => eligibleForSentinel;
        public bool EligibleForSoloDuel => eligibleForSoloDuel;
        public bool RuntimeReady => director != null && playerTarget != null && runtimeProfile != null;

        private void Reset()
        {
            RefreshLocalReferences();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                RefreshLocalReferences();
        }
#endif

        private void Awake()
        {
            if (enemyHealth == null)
                enemyHealth = GetComponent<EnemyHealth>();

            if (locomotion == null)
                locomotion = GetComponent<EnemyLocomotionV2>();

            if (actionRunner == null)
                actionRunner = GetComponent<EnemyActionRunnerV2>();

            if (visualGuard == null)
                visualGuard = GetComponent<EnemyVisualGuardV2>();

            if (visualGuard == null)
                visualGuard = gameObject.AddComponent<EnemyVisualGuardV2>();

            if (slowReceiver == null)
                slowReceiver = GetComponent<EnemySlowReceiverV2>();

            if (slowReceiver == null)
                slowReceiver = gameObject.AddComponent<EnemySlowReceiverV2>();

            ForceSharedRuntimeComponentsEnabled("Awake");
            visualGuard.CaptureNow();

            AutoPopulateLegacyBehaviours();
            CacheProtectedVisualState();
            RefreshReadinessDebug();
        }

        private void OnEnable()
        {
            ForceSharedRuntimeComponentsEnabled("OnEnable");
            RestoreProtectedVisualState();
            FindAndRegisterDirector();
        }

        private void Start()
        {
            ForceSharedRuntimeComponentsEnabled("Start");
            visualGuard?.CaptureNow();
            RestoreProtectedVisualState();
            FindAndRegisterDirector();
        }

        private void OnDisable()
        {
            if (director != null)
                director.Unregister(this);

            actionRunner?.ForceCancelCurrent("Agent disabled");
        }

        public void Initialize(
            SquadDirectorV2 newDirector,
            Transform player,
            EnemyAIV2Profile profile,
            ArenaNavigationGrid grid)
        {
            director = newDirector;
            playerTarget = player;
            runtimeProfile = profile;
            runtimeGrid = grid;

            locomotion?.Configure(runtimeProfile, runtimeGrid, this);
            RefreshReadinessDebug();

            ApplyBackendMode(newDirector != null
                ? newDirector.BackendMode
                : EnemyAIV2BackendMode.ObserveOnly);
        }

        public void AssignRoleAndSlot(
            EnemyRoleV2 role,
            EnemySectorV2 sector,
            Vector2 slot,
            string reason)
        {
            currentRole = role;
            currentSector = sector;
            assignedSlot = slot;
            debugAssignmentReason = reason;
        }

        public void ApplyBackendMode(EnemyAIV2BackendMode mode)
        {
            bool requestedActive = mode == EnemyAIV2BackendMode.Active;
            bool shouldDisableLegacy = requestedActive && RuntimeReady;

            if (legacyAIBehaviours != null)
            {
                for (int i = 0; i < legacyAIBehaviours.Length; i++)
                {
                    Behaviour behaviour = legacyAIBehaviours[i];

                    if (behaviour == null || behaviour == this ||
                        behaviour == actionRunner || behaviour == locomotion ||
                        behaviour is EnemyCombatExecutorV2 || behaviour is EnemyShooterDebug)
                    {
                        continue;
                    }

                    behaviour.enabled = !shouldDisableLegacy;
                }
            }

            ForceSharedRuntimeComponentsEnabled("ApplyBackendMode");

            legacyDisabledByV2 = shouldDisableLegacy;

            // The legacy brain may perform cleanup in OnDisable. Restore only the
            // renderers/objects that were already visible before V2 took control.
            RestoreProtectedVisualState();

            if (!shouldDisableLegacy)
                actionRunner?.ForceCancelCurrent(requestedActive
                    ? "V2 waiting for player/profile"
                    : "ObserveOnly mode");

            RefreshReadinessDebug();

            if (logBackendCompatibility)
            {
                Debug.Log(
                    $"[Enemy AI V2] {name}: requested={mode}, " +
                    $"runtimeReady={RuntimeReady}, legacyDisabled={legacyDisabledByV2}, " +
                    $"player={(playerTarget != null ? playerTarget.name : "None")}, " +
                    $"profile={(runtimeProfile != null ? runtimeProfile.name : "None")}",
                    this
                );
            }
        }

        [ContextMenu("Refresh V2 References")]
        private void RefreshLocalReferences()
        {
            if (enemyHealth == null)
                enemyHealth = GetComponent<EnemyHealth>();

            if (locomotion == null)
                locomotion = GetComponent<EnemyLocomotionV2>();

            if (actionRunner == null)
                actionRunner = GetComponent<EnemyActionRunnerV2>();

            if (visualGuard == null)
                visualGuard = GetComponent<EnemyVisualGuardV2>();

            if (visualGuard == null)
                visualGuard = gameObject.AddComponent<EnemyVisualGuardV2>();

            if (slowReceiver == null)
                slowReceiver = GetComponent<EnemySlowReceiverV2>();

            if (slowReceiver == null)
                slowReceiver = gameObject.AddComponent<EnemySlowReceiverV2>();

            ForceSharedRuntimeComponentsEnabled("RefreshReferences");
            visualGuard.CaptureNow();

            AutoPopulateLegacyBehaviours();
            CacheProtectedVisualState();
            RefreshReadinessDebug();
        }

        private void FindAndRegisterDirector()
        {
            if (director == null)
                director = FindObjectOfType<SquadDirectorV2>(true);

            if (director != null)
                director.Register(this);
        }

        private void ForceSharedRuntimeComponentsEnabled(string source)
        {
            if (!forceSharedRuntimeComponentsEnabled || forcedEnabledComponentTypeNames == null)
                return;

            int enabledCount = 0;
            System.Text.StringBuilder sb = null;

            Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;

                bool shouldForce = false;
                for (int j = 0; j < forcedEnabledComponentTypeNames.Length; j++)
                {
                    if (typeName == forcedEnabledComponentTypeNames[j])
                    {
                        shouldForce = true;
                        break;
                    }
                }

                if (!shouldForce)
                    continue;

                if (!behaviour.enabled)
                {
                    behaviour.enabled = true;
                    enabledCount++;

                    if (sb == null)
                        sb = new System.Text.StringBuilder();

                    if (sb.Length > 0)
                        sb.Append(", ");

                    sb.Append(typeName);
                }
            }

            if (enabledCount > 0)
            {
                debugForcedRuntimeComponents = $"{source}: enabled {sb}";

                if (logForcedRuntimeComponents)
                    Debug.Log($"[Enemy AI V2] {name}: {debugForcedRuntimeComponents}", this);
            }
        }

        private void AutoPopulateLegacyBehaviours()
        {
            if (legacyAIBehaviours != null && legacyAIBehaviours.Length > 0)
                return;

            EnemyBrain legacyBrain = GetComponent<EnemyBrain>();

            if (legacyBrain != null)
                legacyAIBehaviours = new Behaviour[] { legacyBrain };
        }

        private void CacheProtectedVisualState()
        {
            if (!preserveChildVisualsWhenSwitchingBackend)
                return;

            if (protectedSpriteRenderers == null || protectedSpriteRenderers.Length == 0)
                protectedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

            cachedRendererEnabled = new bool[protectedSpriteRenderers.Length];
            cachedRendererColors = new Color[protectedSpriteRenderers.Length];

            for (int i = 0; i < protectedSpriteRenderers.Length; i++)
            {
                SpriteRenderer renderer = protectedSpriteRenderers[i];
                if (renderer == null)
                    continue;

                cachedRendererEnabled[i] = renderer.enabled;
                cachedRendererColors[i] = renderer.color;
            }

            if (protectedVisualObjects == null)
                protectedVisualObjects = new GameObject[0];

            cachedVisualObjectActive = new bool[protectedVisualObjects.Length];

            for (int i = 0; i < protectedVisualObjects.Length; i++)
            {
                GameObject visual = protectedVisualObjects[i];
                cachedVisualObjectActive[i] = visual != null && visual.activeSelf;
            }
        }

        private void RestoreProtectedVisualState()
        {
            visualGuard?.RestoreNow();

            if (!preserveChildVisualsWhenSwitchingBackend)
                return;

            if (protectedVisualObjects != null && cachedVisualObjectActive != null)
            {
                int count = Mathf.Min(protectedVisualObjects.Length, cachedVisualObjectActive.Length);
                for (int i = 0; i < count; i++)
                {
                    GameObject visual = protectedVisualObjects[i];
                    if (visual != null && visual.activeSelf != cachedVisualObjectActive[i])
                        visual.SetActive(cachedVisualObjectActive[i]);
                }
            }

            if (protectedSpriteRenderers == null || cachedRendererEnabled == null)
                return;

            int rendererCount = Mathf.Min(protectedSpriteRenderers.Length, cachedRendererEnabled.Length);

            for (int i = 0; i < rendererCount; i++)
            {
                SpriteRenderer renderer = protectedSpriteRenderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = cachedRendererEnabled[i];

                if (cachedRendererColors != null && i < cachedRendererColors.Length)
                    renderer.color = cachedRendererColors[i];
            }
        }

        private void RefreshReadinessDebug()
        {
            debugHasPlayerTarget = playerTarget != null;
            debugHasProfile = runtimeProfile != null;
            debugRuntimeReady = RuntimeReady;

            if (RuntimeReady)
                debugRuntimeReadiness = "Ready";
            else if (director == null)
                debugRuntimeReadiness = "Waiting for SquadDirectorV2";
            else if (playerTarget == null)
                debugRuntimeReadiness = "Waiting for player target";
            else if (runtimeProfile == null)
                debugRuntimeReadiness = "Waiting for EnemyAIV2Profile";
            else
                debugRuntimeReadiness = "Not ready";
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = currentRole == EnemyRoleV2.Controller
                ? Color.yellow
                : currentRole == EnemyRoleV2.Flanker
                    ? Color.magenta
                    : currentRole == EnemyRoleV2.Sentinel
                        ? Color.cyan
                        : Color.white;

            Gizmos.DrawWireSphere(assignedSlot, 0.24f);
            Gizmos.DrawLine(transform.position, assignedSlot);
        }
#endif
    }
}
