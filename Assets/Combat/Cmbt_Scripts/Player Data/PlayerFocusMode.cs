using UnityEngine;

public class PlayerFocusMode : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode focusKey1 = KeyCode.LeftShift;
    [SerializeField] private KeyCode focusKey2 = KeyCode.RightShift;

    [Header("Movement")]
    [Tooltip("Multiply your normal move speed by this while focusing.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float focusMoveMultiplier = 0.6f;

    [Header("AP Cost")]
    [Tooltip("If true, Focus Mode requires AP and drains AP while held.")]
    [SerializeField] private bool requireApToFocus = true;

    [Tooltip("AP drained per real-time second while focusing. Try 4 to 8 for a light drain.")]
    [SerializeField] private float apDrainPerSecond = 6f;

    [Tooltip("If true, focus immediately turns off when AP hits zero.")]
    [SerializeField] private bool stopFocusAtZeroAp = true;

    [Header("Optional Global Time Slow Test")]
    [Tooltip("Experimental. If enabled, Focus Mode slightly slows the whole game.")]
    [SerializeField] private bool applyGlobalTimeSlow = true;

    [Tooltip("1 = normal time. Try 0.9 for subtle, 0.85 for noticeable, 0.75 for dramatic.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float focusTimeScale = 0.85f;

    [Tooltip("If true, Focus Mode will not override stronger time slows like the skill menu.")]
    [SerializeField] private bool avoidOverridingStrongerSlow = true;

    [Header("Character Sprite Opacity")]
    [Tooltip("Character/body renderers to fade while focusing. Leave empty to auto-find child SpriteRenderers.")]
    [SerializeField] private SpriteRenderer[] characterSpriteRenderers;

    [Tooltip("Character sprite alpha while Focus Mode is held. Hurtbox alpha is not affected.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float focusSpriteAlpha = 0.55f;

    [Tooltip("If true, opacity eases in/out. If false, opacity changes instantly.")]
    [SerializeField] private bool fadeSpriteAlpha = true;

    [Tooltip("Higher = faster opacity transition.")]
    [SerializeField] private float spriteFadeSpeed = 14f;

    [Header("Hurtbox Visual")]
    [SerializeField] private GameObject hurtboxVisualRoot;
    [SerializeField] private SpriteRenderer hurtboxRenderer;

    [Header("Optional Auto Scale")]
    [SerializeField] private CircleCollider2D hurtboxCollider;
    [SerializeField] private bool autoScaleVisualToColliderOnStart = true;

    [Header("Debug")]
    [SerializeField] private bool logTimeScaleChanges = false;
    [SerializeField] private bool logApDrain = false;

    public bool IsFocusing { get; private set; }

    public float MoveMultiplier => IsFocusing ? focusMoveMultiplier : 1f;

    private bool focusTimeSlowApplied;
    private float preFocusTimeScale = 1f;
    private float preFocusFixedDelta = 0.02f;

    private float[] normalSpriteAlphas;
    private float apDrainAccumulator;

    private const float TimeScaleEpsilon = 0.005f;

    private void Awake()
    {
        if (hurtboxVisualRoot == null && hurtboxRenderer != null)
            hurtboxVisualRoot = hurtboxRenderer.gameObject;

        if (characterSpriteRenderers == null || characterSpriteRenderers.Length == 0)
            characterSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        CacheNormalSpriteAlphas();

        SetHurtboxVisible(false);
        ApplyCharacterSpriteAlphaInstant(1f);
    }

    private void Start()
    {
        if (autoScaleVisualToColliderOnStart)
            MatchVisualToCollider();
    }

    private void Update()
    {
        bool focusHeld = Input.GetKey(focusKey1) || Input.GetKey(focusKey2);
        bool canFocus = CanFocusNow();

        bool shouldFocus = focusHeld && canFocus;

        if (shouldFocus != IsFocusing)
        {
            IsFocusing = shouldFocus;
            SetHurtboxVisible(IsFocusing);

            if (!IsFocusing)
                apDrainAccumulator = 0f;
        }

        if (IsFocusing)
            DrainFocusAp();

        TickFocusTimeSlow();
        TickCharacterSpriteOpacity();
    }

    private bool CanFocusNow()
    {
        if (!requireApToFocus)
            return true;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null)
            return false;

        return pm.Active.currentAP > 0;
    }

    private void DrainFocusAp()
    {
        if (!requireApToFocus)
            return;

        if (apDrainPerSecond <= 0f)
            return;

        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null)
        {
            ForceStopFocus();
            return;
        }

        var active = pm.Active;

        if (active.currentAP <= 0)
        {
            if (stopFocusAtZeroAp)
                ForceStopFocus();

            return;
        }

        // Use unscaled time so global Focus slow does not make Focus cheaper.
        apDrainAccumulator += apDrainPerSecond * Time.unscaledDeltaTime;

        int drainAmount = Mathf.FloorToInt(apDrainAccumulator);

        if (drainAmount <= 0)
            return;

        apDrainAccumulator -= drainAmount;

        int before = active.currentAP;
        active.currentAP = Mathf.Max(0, active.currentAP - drainAmount);

        if (logApDrain && before != active.currentAP)
            Debug.Log($"Focus AP drain: {before} -> {active.currentAP}");

        if (active.currentAP <= 0 && stopFocusAtZeroAp)
            ForceStopFocus();
    }

    private void ForceStopFocus()
    {
        if (!IsFocusing)
            return;

        IsFocusing = false;
        apDrainAccumulator = 0f;
        SetHurtboxVisible(false);
    }

    private void TickCharacterSpriteOpacity()
    {
        if (characterSpriteRenderers == null || characterSpriteRenderers.Length == 0)
            return;

        float targetMultiplier = IsFocusing ? focusSpriteAlpha : 1f;

        for (int i = 0; i < characterSpriteRenderers.Length; i++)
        {
            SpriteRenderer sr = characterSpriteRenderers[i];

            if (!ShouldFadeRenderer(sr))
                continue;

            float baseAlpha = GetBaseAlpha(i);
            float targetAlpha = baseAlpha * targetMultiplier;

            Color c = sr.color;

            if (fadeSpriteAlpha)
            {
                float k = 1f - Mathf.Exp(-spriteFadeSpeed * Time.unscaledDeltaTime);
                c.a = Mathf.Lerp(c.a, targetAlpha, k);
            }
            else
            {
                c.a = targetAlpha;
            }

            sr.color = c;
        }
    }

    private bool ShouldFadeRenderer(SpriteRenderer sr)
    {
        if (sr == null)
            return false;

        // Never fade the specific hurtbox renderer.
        if (hurtboxRenderer != null && sr == hurtboxRenderer)
            return false;

        // Never fade anything parented under the hurtbox visual root.
        if (hurtboxVisualRoot != null && sr.transform.IsChildOf(hurtboxVisualRoot.transform))
            return false;

        return true;
    }

    private void CacheNormalSpriteAlphas()
    {
        if (characterSpriteRenderers == null)
        {
            normalSpriteAlphas = new float[0];
            return;
        }

        normalSpriteAlphas = new float[characterSpriteRenderers.Length];

        for (int i = 0; i < characterSpriteRenderers.Length; i++)
        {
            SpriteRenderer sr = characterSpriteRenderers[i];

            if (sr != null)
                normalSpriteAlphas[i] = sr.color.a;
            else
                normalSpriteAlphas[i] = 1f;
        }
    }

    private float GetBaseAlpha(int index)
    {
        if (normalSpriteAlphas == null)
            return 1f;

        if (index < 0 || index >= normalSpriteAlphas.Length)
            return 1f;

        return normalSpriteAlphas[index];
    }

    private void ApplyCharacterSpriteAlphaInstant(float alphaMultiplier)
    {
        if (characterSpriteRenderers == null)
            return;

        for (int i = 0; i < characterSpriteRenderers.Length; i++)
        {
            SpriteRenderer sr = characterSpriteRenderers[i];

            if (!ShouldFadeRenderer(sr))
                continue;

            Color c = sr.color;
            c.a = GetBaseAlpha(i) * alphaMultiplier;
            sr.color = c;
        }
    }

    private void TickFocusTimeSlow()
    {
        if (!applyGlobalTimeSlow)
        {
            RestoreFocusTimeSlowIfSafe();
            return;
        }

        if (IsFocusing)
            TryApplyFocusTimeSlow();
        else
            RestoreFocusTimeSlowIfSafe();
    }

    private void TryApplyFocusTimeSlow()
    {
        if (focusTimeSlowApplied)
            return;

        float targetScale = Mathf.Clamp(focusTimeScale, 0.5f, 1f);

        // If another system has already slowed time more than Focus Mode would,
        // do not override it. Example: the skill menu at 0.12.
        if (avoidOverridingStrongerSlow && Time.timeScale < targetScale - TimeScaleEpsilon)
            return;

        preFocusTimeScale = Time.timeScale;
        preFocusFixedDelta = Time.fixedDeltaTime;

        Time.timeScale = targetScale;

        if (preFocusTimeScale > 0.001f)
            Time.fixedDeltaTime = preFocusFixedDelta * (targetScale / preFocusTimeScale);
        else
            Time.fixedDeltaTime = 0.02f * targetScale;

        focusTimeSlowApplied = true;

        if (logTimeScaleChanges)
        {
            Debug.Log(
                $"Focus Mode time slow applied. TimeScale={Time.timeScale}, FixedDelta={Time.fixedDeltaTime}"
            );
        }
    }

    private void RestoreFocusTimeSlowIfSafe()
    {
        if (!focusTimeSlowApplied)
            return;

        float targetScale = Mathf.Clamp(focusTimeScale, 0.5f, 1f);

        // If something else is currently controlling time scale, like the skill menu,
        // do not clobber it.
        if (avoidOverridingStrongerSlow && Mathf.Abs(Time.timeScale - targetScale) > TimeScaleEpsilon)
            return;

        Time.timeScale = preFocusTimeScale;
        Time.fixedDeltaTime = preFocusFixedDelta;

        focusTimeSlowApplied = false;

        if (logTimeScaleChanges)
        {
            Debug.Log(
                $"Focus Mode time slow restored. TimeScale={Time.timeScale}, FixedDelta={Time.fixedDeltaTime}"
            );
        }
    }

    private void SetHurtboxVisible(bool visible)
    {
        if (hurtboxRenderer != null)
            hurtboxRenderer.enabled = visible;

        if (hurtboxVisualRoot != null && hurtboxRenderer == null)
            hurtboxVisualRoot.SetActive(visible);
    }

    [ContextMenu("Match Visual To Collider")]
    public void MatchVisualToCollider()
    {
        if (hurtboxCollider == null)
            return;

        Transform t = null;

        if (hurtboxVisualRoot != null)
            t = hurtboxVisualRoot.transform;
        else if (hurtboxRenderer != null)
            t = hurtboxRenderer.transform;

        if (t == null)
            return;

        float diameter = hurtboxCollider.radius * 2f;
        t.localScale = new Vector3(diameter, diameter, 1f);
    }

    [ContextMenu("Debug Focus Renderers")]
    private void DebugFocusRenderers()
    {
        Debug.Log("=== Focus Mode Renderer Debug ===");

        if (characterSpriteRenderers == null || characterSpriteRenderers.Length == 0)
        {
            Debug.LogWarning("No characterSpriteRenderers assigned.");
            return;
        }

        for (int i = 0; i < characterSpriteRenderers.Length; i++)
        {
            SpriteRenderer sr = characterSpriteRenderers[i];

            if (sr == null)
            {
                Debug.Log($"Renderer {i}: null");
                continue;
            }

            bool isHurtboxRenderer = hurtboxRenderer != null && sr == hurtboxRenderer;
            bool isUnderHurtboxRoot = hurtboxVisualRoot != null && sr.transform.IsChildOf(hurtboxVisualRoot.transform);
            bool shouldFade = ShouldFadeRenderer(sr);

            Debug.Log(
                $"Renderer {i}: {sr.name} | " +
                $"ShouldFade={shouldFade} | " +
                $"IsHurtboxRenderer={isHurtboxRenderer} | " +
                $"IsUnderHurtboxRoot={isUnderHurtboxRoot} | " +
                $"Alpha={sr.color.a}"
            );
        }
    }

    private void OnDisable()
    {
        SetHurtboxVisible(false);
        ApplyCharacterSpriteAlphaInstant(1f);

        RestoreTimeScaleOnDisable();

        IsFocusing = false;
        apDrainAccumulator = 0f;
    }

    private void OnDestroy()
    {
        RestoreTimeScaleOnDisable();
    }

    private void RestoreTimeScaleOnDisable()
    {
        if (!focusTimeSlowApplied)
            return;

        float targetScale = Mathf.Clamp(focusTimeScale, 0.5f, 1f);

        // Only restore if Focus Mode still appears to be the thing controlling time.
        // This avoids breaking the skill menu's stronger time slow.
        if (Mathf.Abs(Time.timeScale - targetScale) <= TimeScaleEpsilon)
        {
            Time.timeScale = preFocusTimeScale;
            Time.fixedDeltaTime = preFocusFixedDelta;
        }

        focusTimeSlowApplied = false;
    }
}