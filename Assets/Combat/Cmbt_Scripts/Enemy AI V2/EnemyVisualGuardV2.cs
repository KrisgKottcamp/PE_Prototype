using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    /// <summary>
    /// Compatibility helper for prefabs whose visible art lives on a child object.
    ///
    /// AI V2 may disable the legacy decision brain while continuing to use shared
    /// combat/visual components such as EnemyShooterDebug and EnemyAttackTelegraph.
    /// Some of those components initialize enemy visuals. This guard restores only
    /// the intended child art and deliberately ignores the root SpriteRenderer when
    /// that renderer is used as a disabled placeholder.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyVisualGuardV2 : MonoBehaviour
    {
        [System.Serializable]
        private struct RendererState
        {
            public SpriteRenderer renderer;
            public GameObject gameObject;
            public Transform visualTransform;
            public bool activeSelf;
            public bool enabled;
            public Color color;
            public Vector3 localScale;
            public bool usedFallbackScale;
        }

        [Header("Protection")]
        [SerializeField] private bool protectVisuals = true;
        [SerializeField] private bool ignoreRootSpriteRenderer = true;
        [SerializeField] private bool restoreEveryLateUpdate = true;

        [Tooltip("When enabled, descendant SpriteRenderers with a sprite are treated as intended visible art, even if the root SpriteRenderer is intentionally disabled.")]
        [SerializeField] private bool protectDescendantRenderersWithSprites = true;

        [Tooltip("Prevents legacy scripts from making visible enemy art fully transparent while AI V2 controls the enemy.")]
        [SerializeField, Range(0f, 1f)] private float minimumVisibleAlpha = 0.95f;

        [Header("Scale Protection v5")]
        [Tooltip("Restores protected child visual transforms when another component sets their X/Y scale to 0 or nearly zero.")]
        [SerializeField] private bool protectChildVisualScale = true;

        [Tooltip("If Capture sees a child sprite already at 0 scale, use this as the intended visible scale instead of capturing the bad value.")]
        [SerializeField] private bool useFallbackScaleWhenCapturedScaleIsZero = true;

        [Tooltip("The fallback visible scale to use when the child sprite was already 0,0,0 at capture time. Most enemy art should use 1,1,1.")]
        [SerializeField] private Vector3 fallbackVisibleScale = Vector3.one;

        [Tooltip("X/Y scale at or below this value counts as invisible. Z scale is ignored because 2D SpriteRenderers are normally affected by X/Y.")]
        [SerializeField, Min(0f)] private float zeroScaleThreshold = 0.01f;

        [Tooltip("If true, the guard restores the captured visual scale every LateUpdate, not only when the X/Y scale is near zero. Leave off unless another component constantly writes an incorrect non-zero scale.")]
        [SerializeField] private bool forceCapturedScaleEveryFrame = false;

        [Header("Debug")]
        [SerializeField] private bool logVisualRestores = false;
        [SerializeField] private int debugProtectedRendererCount;
        [SerializeField] private string debugLastRestore = "None";
        [SerializeField] private string debugLastScaleRestore = "None";
        [SerializeField] private Vector3 debugFirstProtectedScale;

        private RendererState[] states;
        private bool hasCaptured;

        private void Awake()
        {
            CaptureNow();
        }

        private void OnEnable()
        {
            if (!hasCaptured)
                CaptureNow();

            RestoreNow();
        }

        private void LateUpdate()
        {
            if (restoreEveryLateUpdate)
                RestoreNow();
        }

        [ContextMenu("Capture Visual State Now")]
        public void CaptureNow()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            System.Collections.Generic.List<RendererState> list = new System.Collections.Generic.List<RendererState>();
            int fallbackScaleCount = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer sr = renderers[i];
                if (sr == null)
                    continue;

                bool isRoot = sr.transform == transform;
                if (ignoreRootSpriteRenderer && isRoot)
                    continue;

                if (protectDescendantRenderersWithSprites && sr.sprite == null)
                    continue;

                bool shouldProtect = sr.gameObject.activeSelf || sr.enabled;
                if (!shouldProtect)
                    continue;

                Vector3 capturedScale = sr.transform.localScale;
                bool usedFallback = false;

                if (protectChildVisualScale &&
                    useFallbackScaleWhenCapturedScaleIsZero &&
                    !IsVisibleScaleUsable(capturedScale) &&
                    IsVisibleScaleUsable(fallbackVisibleScale))
                {
                    capturedScale = fallbackVisibleScale;
                    usedFallback = true;
                    fallbackScaleCount++;
                }

                list.Add(new RendererState
                {
                    renderer = sr,
                    gameObject = sr.gameObject,
                    visualTransform = sr.transform,
                    activeSelf = sr.gameObject.activeSelf,
                    enabled = sr.enabled,
                    color = sr.color,
                    localScale = capturedScale,
                    usedFallbackScale = usedFallback
                });
            }

            states = list.ToArray();
            debugProtectedRendererCount = states.Length;
            debugFirstProtectedScale = states.Length > 0 ? states[0].localScale : Vector3.zero;
            hasCaptured = true;
            debugLastRestore = $"Captured {debugProtectedRendererCount}";
            debugLastScaleRestore = fallbackScaleCount > 0
                ? $"Captured fallback visible scale for {fallbackScaleCount} zero-scale visual(s)"
                : "Captured scale state";

            if (logVisualRestores && fallbackScaleCount > 0)
                Debug.Log($"[Enemy AI V2] {name}: {debugLastScaleRestore}", this);
        }

        [ContextMenu("Restore Visual State Now")]
        public void RestoreNow()
        {
            if (!protectVisuals || states == null)
                return;

            int restored = 0;
            int scaleRestored = 0;

            for (int i = 0; i < states.Length; i++)
            {
                RendererState state = states[i];
                SpriteRenderer sr = state.renderer;

                if (sr == null)
                    continue;

                if (state.gameObject != null && state.activeSelf && !state.gameObject.activeSelf)
                {
                    state.gameObject.SetActive(true);
                    restored++;
                }

                if (state.enabled && !sr.enabled)
                {
                    sr.enabled = true;
                    restored++;
                }

                if (protectChildVisualScale && state.visualTransform != null)
                {
                    Vector3 current = state.visualTransform.localScale;
                    bool capturedScaleIsUsable = IsVisibleScaleUsable(state.localScale);
                    bool currentIsInvisible = IsScaleInvisible(current);

                    bool shouldRestoreScale =
                        capturedScaleIsUsable &&
                        (forceCapturedScaleEveryFrame || currentIsInvisible);

                    if (shouldRestoreScale)
                    {
                        state.visualTransform.localScale = state.localScale;
                        scaleRestored++;
                        restored++;
                    }
                }

                Color c = sr.color;
                float targetAlpha = Mathf.Max(c.a, Mathf.Max(state.color.a, minimumVisibleAlpha));
                if (c.a < targetAlpha - 0.001f)
                {
                    c.a = targetAlpha;
                    sr.color = c;
                    restored++;
                }
            }

            if (scaleRestored > 0)
                debugLastScaleRestore = $"Restored child visual scale x{scaleRestored}";

            if (restored > 0)
            {
                debugLastRestore = scaleRestored > 0
                    ? $"Restored {restored} visual fields; {debugLastScaleRestore}"
                    : $"Restored {restored} visual fields";

                if (logVisualRestores)
                    Debug.Log($"[Enemy AI V2] {name}: {debugLastRestore}", this);
            }
        }

        private bool IsVisibleScaleUsable(Vector3 scale)
        {
            return Mathf.Abs(scale.x) > zeroScaleThreshold &&
                   Mathf.Abs(scale.y) > zeroScaleThreshold;
        }

        private bool IsScaleInvisible(Vector3 scale)
        {
            return Mathf.Abs(scale.x) <= zeroScaleThreshold ||
                   Mathf.Abs(scale.y) <= zeroScaleThreshold;
        }
    }
}
