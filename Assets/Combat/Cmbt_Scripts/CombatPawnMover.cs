using System.Reflection;
using ProjectEri.SkillSystemV2;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CombatPawnMover : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Optional Modifiers")]
    [SerializeField] private PlayerFocusMode focusMode;

    [Tooltip("Optional V2 movement modifier stack. Used by Slow Orb V2 so player slow stacks with Speed Boost instead of overwriting it.")]
    [SerializeField] private PlayerMoveSpeedModifierReceiverV2 movementModifierReceiver;

    [Tooltip("Runtime owner for forced movement and relocation applied by SkillSystemV2.")]
    [SerializeField] private SpellActorMotionController2D forcedMotion;

    [Tooltip("Automatically add PlayerMoveSpeedModifierReceiverV2 if it is missing. Recommended for the combat player.")]
    [SerializeField] private bool autoAddMovementModifierReceiver = true;

    [Header("Stage 4 v8 - Speed Boost / Slow Orb Stack Fix")]
    [Tooltip("Prevents the old SpeedModifier slow and the new V2 slow receiver from multiplying into an extreme double-slow. When both are below 1, the stronger slow wins instead of both being multiplied together.")]
    [SerializeField] private bool combineLegacyAndV2SlowsAsStrongestOnly = true;

    [Tooltip("When Speed Boost is detected while a V2 slow is active, raise the total movement modifier to at least this value. This makes Speed Boost feel useful inside Slow Orb instead of still feeling super slow.")]
    [SerializeField] private bool useBoostedSlowMinimumTotalSpeed = true;

    [Tooltip("Minimum total movement multiplier while Speed Boost is active inside a V2 slow zone. 0.78 means boosted-in-slow-orb movement is at least 78% of normal base speed before Focus Mode.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float boostedSlowMinimumTotalMultiplier = 0.78f;

    [Tooltip("If the legacy SpeedModifier multiplier is above this value, V2 treats Speed Boost as active.")]
    [SerializeField, Min(1f)] private float legacyBoostDetectionThreshold = 1.05f;

    [Tooltip("Detect an active PlayerSpeedBoost-like component by reflection. It looks for common bool fields/properties such as IsActive, Active, IsBoosting, BoostActive, or IsRunning on components with SpeedBoost in their type name.")]
    [SerializeField] private bool detectPlayerSpeedBoostByReflection = true;

    [Tooltip("Manual debug override. Leave off normally. Turn on only if your speed boost script cannot be detected but you want to verify the boosted slow floor behavior.")]
    [SerializeField] private bool forceSpeedBoostDetectedForTesting = false;

    [Tooltip("If true, logs speed calculation while moving. Keep off unless debugging.")]
    [SerializeField] private bool logEffectiveSpeed = false;

    [Header("Debug")]
    [SerializeField] private float debugBaseSpeed;
    [SerializeField] private float debugLegacySpeedModifier = 1f;
    [SerializeField] private float debugFocusMultiplier = 1f;
    [SerializeField] private float debugV2MovementModifier = 1f;
    [SerializeField] private float debugCombinedMovementMultiplier = 1f;
    [SerializeField] private bool debugV8DoubleSlowProtected;
    [SerializeField] private bool debugV8SpeedBoostDetected;
    [SerializeField] private string debugV8SpeedBoostDetectionReason = "None";
    [SerializeField] private bool debugV8BoostedSlowFloorApplied;
    [SerializeField] private float debugFinalSpeed;

    private Rigidbody2D rb;
    private Vector2 input;
    private MonoBehaviour[] cachedBehaviours;
    private float nextReflectionRefreshAt;

    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    public float EffectiveSpeed => debugFinalSpeed;
    public float V2MovementModifier => debugV2MovementModifier;
    public bool DebugV8SpeedBoostDetected => debugV8SpeedBoostDetected;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (focusMode == null)
            focusMode = GetComponent<PlayerFocusMode>();

        if (movementModifierReceiver == null)
            movementModifierReceiver = GetComponent<PlayerMoveSpeedModifierReceiverV2>();

        if (movementModifierReceiver == null && autoAddMovementModifierReceiver)
            movementModifierReceiver = gameObject.AddComponent<PlayerMoveSpeedModifierReceiverV2>();

        if (forcedMotion == null)
            forcedMotion = GetComponent<SpellActorMotionController2D>();

        RefreshBehaviourCache();
    }

    private void Update()
    {
        input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;
    }

    private void FixedUpdate()
    {
        if (forcedMotion == null)
            forcedMotion = GetComponent<SpellActorMotionController2D>();

        if (forcedMotion != null && forcedMotion.IsControllingMotion)
            return;

        if (SpellBuildUpControl2D.IsMovementBlocked(gameObject))
        {
            rb.MovePosition(rb.position);
            return;
        }

        float legacySpeedModifierMultiplier = 1f;

        SpeedModifier mod = GetComponent<SpeedModifier>();
        if (mod != null)
            legacySpeedModifierMultiplier = Mathf.Max(0f, mod.Multiplier);

        float focusMultiplier = 1f;

        if (focusMode != null)
            focusMultiplier = focusMode.MoveMultiplier;

        float v2MovementMultiplier = 1f;

        if (movementModifierReceiver != null)
            v2MovementMultiplier = Mathf.Max(0.01f, movementModifierReceiver.FinalMultiplier);

        bool v2SlowActive = v2MovementMultiplier < 0.999f;
        bool legacySlowActive = legacySpeedModifierMultiplier < 0.999f;
        bool speedBoostDetected = DetectSpeedBoostActive(legacySpeedModifierMultiplier, out string speedBoostReason);

        float combinedMultiplier;
        bool doubleSlowProtected = false;

        if (combineLegacyAndV2SlowsAsStrongestOnly && legacySlowActive && v2SlowActive)
        {
            // If the old AoE slow and the new V2 slow both affect the player, do not multiply them.
            // Multiplying them makes Slow Orb feel like a near-root, especially after adding CombatSlowZoneV2.
            combinedMultiplier = Mathf.Min(legacySpeedModifierMultiplier, v2MovementMultiplier);
            doubleSlowProtected = true;
        }
        else
        {
            combinedMultiplier = legacySpeedModifierMultiplier * v2MovementMultiplier;
        }

        bool boostedSlowFloorApplied = false;

        if (useBoostedSlowMinimumTotalSpeed && v2SlowActive && speedBoostDetected)
        {
            float floor = Mathf.Max(0.01f, boostedSlowMinimumTotalMultiplier);
            if (combinedMultiplier < floor)
            {
                combinedMultiplier = floor;
                boostedSlowFloorApplied = true;
            }
        }

        float statMultiplier = SpellStatModifierUtility.Evaluate(
            gameObject,
            SpellActorStat.MovementSpeed,
            1f);
        float finalSpeed = moveSpeed * combinedMultiplier * focusMultiplier *
                           statMultiplier;

        debugBaseSpeed = moveSpeed;
        debugLegacySpeedModifier = legacySpeedModifierMultiplier;
        debugFocusMultiplier = focusMultiplier;
        debugV2MovementModifier = v2MovementMultiplier;
        debugCombinedMovementMultiplier = combinedMultiplier;
        debugV8DoubleSlowProtected = doubleSlowProtected;
        debugV8SpeedBoostDetected = speedBoostDetected;
        debugV8SpeedBoostDetectionReason = speedBoostReason;
        debugV8BoostedSlowFloorApplied = boostedSlowFloorApplied;
        debugFinalSpeed = finalSpeed;

        if (logEffectiveSpeed && input.sqrMagnitude > 0.001f)
        {
            Debug.Log(
                $"CombatPawnMover v8 speed: base={moveSpeed:0.00}, legacySpeedMod={legacySpeedModifierMultiplier:0.00}, v2MoveMod={v2MovementMultiplier:0.00}, combined={combinedMultiplier:0.00}, focus={focusMultiplier:0.00}, boostDetected={speedBoostDetected}({speedBoostReason}), doubleSlowProtected={doubleSlowProtected}, boostedFloor={boostedSlowFloorApplied}, final={finalSpeed:0.00}",
                this
            );
        }

        rb.MovePosition(rb.position + input * finalSpeed * Time.fixedDeltaTime);
    }

    private bool DetectSpeedBoostActive(float legacySpeedModifierMultiplier, out string reason)
    {
        if (forceSpeedBoostDetectedForTesting)
        {
            reason = "Manual testing override";
            return true;
        }

        if (movementModifierReceiver != null && movementModifierReceiver.HasReceiverBoost)
        {
            reason = $"PlayerMoveSpeedModifierReceiverV2 boost source: {movementModifierReceiver.DebugStrongestBoostSource}";
            return true;
        }

        if (legacySpeedModifierMultiplier > legacyBoostDetectionThreshold)
        {
            reason = $"SpeedModifier multiplier {legacySpeedModifierMultiplier:0.00} > {legacyBoostDetectionThreshold:0.00}";
            return true;
        }

        if (detectPlayerSpeedBoostByReflection && DetectSpeedBoostByReflection(out reason))
            return true;

        reason = "None";
        return false;
    }

    private bool DetectSpeedBoostByReflection(out string reason)
    {
        reason = "None";

        if (Time.time >= nextReflectionRefreshAt || cachedBehaviours == null)
            RefreshBehaviourCache();

        if (cachedBehaviours == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < cachedBehaviours.Length; i++)
        {
            MonoBehaviour behaviour = cachedBehaviours[i];
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (!typeName.ToLowerInvariant().Contains("speedboost"))
                continue;

            if (!behaviour.enabled)
                continue;

            if (TryReadBoolMember(behaviour, "IsActive", flags, out bool isActive) && isActive)
            {
                reason = $"{typeName}.IsActive";
                return true;
            }

            if (TryReadBoolMember(behaviour, "Active", flags, out bool active) && active)
            {
                reason = $"{typeName}.Active";
                return true;
            }

            if (TryReadBoolMember(behaviour, "isActive", flags, out bool privateIsActive) && privateIsActive)
            {
                reason = $"{typeName}.isActive";
                return true;
            }

            if (TryReadBoolMember(behaviour, "IsBoosting", flags, out bool isBoosting) && isBoosting)
            {
                reason = $"{typeName}.IsBoosting";
                return true;
            }

            if (TryReadBoolMember(behaviour, "BoostActive", flags, out bool boostActive) && boostActive)
            {
                reason = $"{typeName}.BoostActive";
                return true;
            }

            if (TryReadBoolMember(behaviour, "boostActive", flags, out bool privateBoostActive) && privateBoostActive)
            {
                reason = $"{typeName}.boostActive";
                return true;
            }

            if (TryReadBoolMember(behaviour, "IsRunning", flags, out bool isRunning) && isRunning)
            {
                reason = $"{typeName}.IsRunning";
                return true;
            }
        }

        return false;
    }

    private static bool TryReadBoolMember(object target, string memberName, BindingFlags flags, out bool value)
    {
        value = false;

        System.Type type = target.GetType();

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
        {
            object raw = property.GetValue(target, null);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            object raw = field.GetValue(target);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        return false;
    }

    private void RefreshBehaviourCache()
    {
        cachedBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
        nextReflectionRefreshAt = Time.time + 0.5f;
    }

    private void OnDisable()
    {
        input = Vector2.zero;
    }
}
