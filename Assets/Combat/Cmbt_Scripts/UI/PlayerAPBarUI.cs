using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the active character's AP and presents the existing stacking
/// skill-cost multiplier. Once the first skill use has raised the multiplier
/// past the configured threshold, white curved sweat streaks appear around the
/// active character's head. This never changes costs or switching rules.
/// </summary>
public class PlayerAPBarUI : MonoBehaviour
{
    [Header("AP Bar")]
    [SerializeField] private Image apFill;
    [SerializeField] private bool smooth = true;
    [SerializeField, Min(0.1f)] private float smoothSpeed = 12f;

    [Header("Optional Cost Readout")]
    [Tooltip("Created by Build Editable Cost Readout UI in the component menu.")]
    [SerializeField] private TMP_Text costMultiplierText;
    [SerializeField] private string costLabel = "COST";
    [SerializeField] private Color normalCostColor =
        new Color(0.84f, 0.93f, 0.95f, 1f);
    [SerializeField] private Color pressuredCostColor =
        new Color(1f, 0.68f, 0.24f, 1f);

    [Header("AP Bar Pressure")]
    [Tooltip("The AP bar begins warming after the first typical skill use.")]
    [SerializeField, Min(1f)] private float pressureBeginsAt = 1.20f;
    [SerializeField, Min(1f)] private float fullPressureAt = 2.20f;
    [SerializeField] private Color pressureColor =
        new Color(1f, 0.63f, 0.20f, 1f);
    [SerializeField, Range(0f, 1f)] private float maximumAPTint = 0.72f;

    [Header("Affordable Skill Glow")]
    [SerializeField] private bool enableAffordableSkillGlow = true;
    [SerializeField] private Color affordableSkillGlowColor =
        new Color(0.56f, 0.92f, 1f, 0.72f);
    [Tooltip("How far the soft aura expands beyond the player sprite.")]
    [SerializeField, Range(0.02f, 0.5f)] private float affordableGlowRadius = 0.18f;
    [Tooltip("Blur radius sampled by the additive glow shader.")]
    [SerializeField, Range(0.5f, 8f)] private float affordableGlowSoftness = 2.4f;
    [SerializeField, Range(0f, 1f)] private float affordableGlowIntensity = 0.26f;
    [SerializeField, Range(0f, 1f)] private float affordableGlowMinimumPulse = 0.76f;
    [SerializeField, Range(0f, 1f)] private float affordableGlowMaximumPulse = 1f;
    [SerializeField, Min(0.5f)] private float affordableGlowPulseDuration = 2.8f;
    [SerializeField, Min(0.1f)] private float affordableGlowFadeSpeed = 4f;
    [SerializeField] private int affordableGlowSortingOrderOffset = -1;

    [Header("World-Space AP Prototype")]
    [Tooltip("Master toggle for the reversible diegetic-adjacent AP experiment.")]
    [SerializeField] private bool enableWorldAPPrototype = true;
    [SerializeField] private Vector2 worldAPLocalOffset =
        new Vector2(0f, -0.30f);
    [SerializeField, Range(0.15f, 0.75f)] private float worldAPArcRadius = 0.34f;
    [SerializeField, Range(8, 32)] private int worldAPArcSegments = 18;
    [SerializeField, Range(0.008f, 0.08f)] private float worldAPArcThickness = 0.022f;
    [SerializeField, Range(0.04f, 0.30f)] private float worldAPSkillNodeSize = 0.11f;

    [Header("World-Space Inner Health Arc")]
    [SerializeField] private bool showWorldHealthArc = true;
    [SerializeField, Range(0.03f, 0.25f)] private float worldHealthArcRadiusOffset = 0.085f;
    [SerializeField, Range(8, 32)] private int worldHealthArcSegments = 18;
    [SerializeField, Range(0.008f, 0.08f)] private float worldHealthArcThickness = 0.018f;
    [SerializeField] private Color worldHealthFillColor =
        new Color(0.94f, 0.18f, 0.16f, 0.92f);
    [SerializeField] private Color worldHealthEmptyColor =
        new Color(0.22f, 0.035f, 0.03f, 0.42f);

    [Header("World-Space AP Colors")]
    [SerializeField] private Color worldAPFillColor =
        new Color(0.24f, 0.92f, 1f, 0.68f);
    [SerializeField] private Color worldAPEmptyColor =
        new Color(0.08f, 0.14f, 0.16f, 0.30f);
    [SerializeField] private Color worldAPUnavailableNodeColor =
        new Color(0.56f, 0.64f, 0.67f, 0.58f);
    [SerializeField] private Color worldAPAffordableNodeColor =
        new Color(0.72f, 0.98f, 1f, 0.92f);
    [SerializeField] private Color worldAPOverMaximumNodeColor =
        new Color(1f, 0.34f, 0.26f, 0.90f);
    [SerializeField] private Color worldAPLockedArcColor =
        new Color(0.92f, 0.16f, 0.12f, 0.88f);
    [SerializeField] private bool showBriefWorldAPNumber = true;
    [SerializeField, Min(0.1f)] private float worldAPNumberDuration = 0.80f;
    [SerializeField] private Color worldAPNumberColor =
        new Color(0.78f, 0.98f, 1f, 0.90f);
    [SerializeField] private int worldAPSortingOrderOffset = -8;

    [Header("White Sweat Rotation Cue")]
    [SerializeField] private bool enableSweatCue = true;
    [Tooltip("The sweat begins after this many skills have resolved for the active character.")]
    [SerializeField, Min(1)] private int sweatActivationSkillUses = 1;
    [SerializeField] private Vector2 sweatHeadLocalOffset =
        new Vector2(0f, 0.68f);
    [SerializeField] private Color sweatColor =
        new Color(1f, 1f, 1f, 0.88f);
    [Tooltip("Optional custom sprite. Leave empty to use the generated curved white droplet.")]
    [SerializeField] private Sprite sweatSpriteOverride;
    [SerializeField] private Vector2 sweatSizeRange =
        new Vector2(0.18f, 0.25f);
    [SerializeField, Min(0.05f)] private float minimumSweatDropsPerSecond = 0.75f;
    [SerializeField, Min(0.05f)] private float maximumSweatDropsPerSecond = 1.75f;
    [SerializeField] private string sweatSortingLayer = "Foreground";
    [SerializeField] private int sweatSortingOrder = 165;

    [Header("Runtime Debug")]
    [SerializeField] private float currentCostMultiplier = 1f;
    [SerializeField] private int currentResolvedSkillUses;
    [SerializeField, Range(0f, 1f)] private float currentPressure;
    [SerializeField, Range(0f, 1f)] private float currentSweatPressure;
    [SerializeField] private bool hasAffordableAPSkill;
    [SerializeField] private int activePartyIndex = -1;

    private float displayedAP01;
    private Color baseAPFillColor = Color.white;
    private bool capturedBaseAPColor;
    private float nextContextRefreshTime;
    private CombatPawn combatPawn;
    private CombatSkillSystem combatSkillSystem;
    private CombatSkillMenuController skillMenuController;
    private APReadyGlow2D affordableSkillGlow;
    private WorldAPStatusDisplay2D worldAPStatusDisplay;
    private RotationPressureSweat2D rotationSweat;

    private void Awake()
    {
        CaptureBaseAPColor();
        RefreshCombatContext();
    }

    private void Update()
    {
        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null ||
            partyManager.party == null ||
            partyManager.party.Count == 0)
        {
            ClearRotationCues();
            return;
        }

        PartyManager.CharacterState active = partyManager.Active;

        if (active == null || active.def == null)
        {
            ClearRotationCues();
            return;
        }

        int maximumAP = Mathf.Max(1, active.def.maxAP);
        int currentAP = Mathf.Clamp(active.currentAP, 0, maximumAP);
        float targetAP01 = currentAP / (float)maximumAP;

        displayedAP01 = smooth
            ? Mathf.Lerp(
                displayedAP01,
                targetAP01,
                1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime))
            : targetAP01;

        if (apFill != null)
            apFill.fillAmount = displayedAP01;

        TickRotationCues(partyManager, active);
    }

    private void TickRotationCues(
        PartyManager partyManager,
        PartyManager.CharacterState active)
    {
        if (Time.unscaledTime >= nextContextRefreshTime)
        {
            nextContextRefreshTime = Time.unscaledTime + 0.5f;
            RefreshCombatContext();
        }

        int newActiveIndex = partyManager.activeIndex;

        if (newActiveIndex != activePartyIndex)
        {
            // Switching must not leave the previous character's droplets behind.
            rotationSweat?.SetPressure(0f, true);
            affordableSkillGlow?.SetReady(false, true);
            worldAPStatusDisplay?.ClearState(true);
            activePartyIndex = newActiveIndex;
        }

        currentCostMultiplier = Mathf.Max(1f, active.skillCostMultiplier);
        currentResolvedSkillUses = ResolveSkillUseCount(active);
        currentPressure = Mathf.InverseLerp(
            pressureBeginsAt,
            Mathf.Max(pressureBeginsAt + 0.01f, fullPressureAt),
            currentCostMultiplier);

        ApplyAPPressure(currentPressure);
        ApplyCostReadout(currentCostMultiplier, currentPressure);
        ApplyAffordableSkillGlow(active);
        ApplyWorldAPStatus(active);
        ApplySweatCue(
            HasLivingAlternative(partyManager, activePartyIndex));
    }

    private void ApplyAPPressure(float pressure)
    {
        if (apFill == null)
            return;

        CaptureBaseAPColor();
        apFill.color = Color.Lerp(
            baseAPFillColor,
            pressureColor,
            pressure * maximumAPTint);
    }

    private void ApplyCostReadout(float multiplier, float pressure)
    {
        if (costMultiplierText == null)
            return;

        bool visible = multiplier > 1.01f;
        costMultiplierText.gameObject.SetActive(visible);

        if (!visible)
            return;

        costMultiplierText.text = $"{costLabel}  x{multiplier:0.00}";
        costMultiplierText.color = Color.Lerp(
            normalCostColor,
            pressuredCostColor,
            pressure);
    }

    private void ApplySweatCue(bool hasLivingAlternative)
    {
        if (rotationSweat == null)
        {
            currentSweatPressure = 0f;
            return;
        }

        bool shouldEmit =
            enableSweatCue &&
            hasLivingAlternative &&
            currentResolvedSkillUses >= sweatActivationSkillUses;

        if (!shouldEmit)
        {
            currentSweatPressure = 0f;
            rotationSweat.SetPressure(0f, false, 1);
            return;
        }

        float additionalPressure = Mathf.InverseLerp(
            sweatActivationSkillUses,
            sweatActivationSkillUses + 2f,
            currentResolvedSkillUses);

        currentSweatPressure = Mathf.Lerp(0.30f, 1f, additionalPressure);
        rotationSweat.SetPressure(
            currentSweatPressure,
            false,
            Mathf.Max(1, currentResolvedSkillUses));
    }

    private void ApplyAffordableSkillGlow(
        PartyManager.CharacterState active)
    {
        hasAffordableAPSkill =
            enableAffordableSkillGlow &&
            HasAffordableAPSkill(active);

        affordableSkillGlow?.SetReady(
            hasAffordableAPSkill);
    }

    private bool HasAffordableAPSkill(
        PartyManager.CharacterState active)
    {
        if (active == null ||
            active.def == null ||
            active.currentHP <= 0 ||
            active.unlockedSkills == null ||
            combatSkillSystem == null)
        {
            return false;
        }

        for (int i = 0; i < active.unlockedSkills.Count; i++)
        {
            SkillDefinition skill = active.unlockedSkills[i];

            if (skill == null ||
                skill.executionType == SkillExecutionType.EriHealingCall)
            {
                continue;
            }

            int scaledCost = combatSkillSystem.GetScaledCost(skill);

            if (scaledCost > 0 &&
                scaledCost < int.MaxValue &&
                active.currentAP >= scaledCost)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyWorldAPStatus(
        PartyManager.CharacterState active)
    {
        if (worldAPStatusDisplay == null)
            return;

        worldAPStatusDisplay.SetPresentationEnabled(
            enableWorldAPPrototype);

        if (!enableWorldAPPrototype)
        {
            worldAPStatusDisplay.ClearState(true);
            return;
        }

        worldAPStatusDisplay.SetState(
            active,
            combatSkillSystem,
            skillMenuController != null &&
            skillMenuController.IsOpen);
    }

    private static int ResolveSkillUseCount(
        PartyManager.CharacterState active)
    {
        if (active == null || active.def == null)
            return 0;

        float multiplier = Mathf.Max(1f, active.skillCostMultiplier);
        float perUseIncrease = active.def.skillCostIncreaseMultiplier;

        if (multiplier <= 1.0001f || perUseIncrease <= 1.0001f)
            return 0;

        float rawUseCount =
            Mathf.Log(multiplier) /
            Mathf.Log(perUseIncrease);

        return Mathf.Max(0, Mathf.RoundToInt(rawUseCount));
    }

    private static bool HasLivingAlternative(
        PartyManager partyManager,
        int currentIndex)
    {
        for (int i = 0; i < partyManager.party.Count; i++)
        {
            if (i == currentIndex)
                continue;

            PartyManager.CharacterState state = partyManager.party[i];

            if (state != null &&
                state.def != null &&
                state.currentHP > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshCombatContext()
    {
        CombatPawn foundPawn = FindObjectOfType<CombatPawn>(true);

        if (foundPawn != combatPawn)
        {
            rotationSweat?.SetPressure(0f, true);
            affordableSkillGlow?.SetReady(false, true);
            worldAPStatusDisplay?.SetPresentationEnabled(false);
            combatPawn = foundPawn;
            combatSkillSystem = null;
            skillMenuController = null;
            affordableSkillGlow = null;
            worldAPStatusDisplay = null;
            rotationSweat = null;
        }

        if (combatPawn == null)
            return;

        combatSkillSystem =
            combatPawn.GetComponent<CombatSkillSystem>();

        if (combatSkillSystem == null)
        {
            combatSkillSystem =
                FindObjectOfType<CombatSkillSystem>(true);
        }

        skillMenuController =
            FindObjectOfType<CombatSkillMenuController>(true);

        affordableSkillGlow =
            combatPawn.GetComponent<APReadyGlow2D>();

        if (affordableSkillGlow == null)
        {
            affordableSkillGlow = combatPawn.gameObject.
                AddComponent<APReadyGlow2D>();
        }

        affordableSkillGlow.Configure(
            DamageFlash2D.FindLikelyCharacterSprites(combatPawn.transform),
            affordableSkillGlowColor,
            affordableGlowRadius,
            affordableGlowSoftness,
            affordableGlowIntensity,
            affordableGlowMinimumPulse,
            affordableGlowMaximumPulse,
            affordableGlowPulseDuration,
            affordableGlowFadeSpeed,
            affordableGlowSortingOrderOffset);

        worldAPStatusDisplay =
            combatPawn.GetComponent<WorldAPStatusDisplay2D>();

        if (worldAPStatusDisplay == null)
        {
            worldAPStatusDisplay = combatPawn.gameObject.
                AddComponent<WorldAPStatusDisplay2D>();
        }

        worldAPStatusDisplay.Configure(
            DamageFlash2D.FindLikelyCharacterSprites(combatPawn.transform),
            worldAPLocalOffset,
            worldAPArcRadius,
            worldAPArcSegments,
            worldAPArcThickness,
            worldAPSkillNodeSize,
            showWorldHealthArc,
            worldHealthArcRadiusOffset,
            worldHealthArcSegments,
            worldHealthArcThickness,
            worldHealthFillColor,
            worldHealthEmptyColor,
            worldAPFillColor,
            worldAPEmptyColor,
            worldAPUnavailableNodeColor,
            worldAPAffordableNodeColor,
            worldAPOverMaximumNodeColor,
            worldAPLockedArcColor,
            showBriefWorldAPNumber,
            worldAPNumberDuration,
            worldAPNumberColor,
            worldAPSortingOrderOffset);
        worldAPStatusDisplay.SetPresentationEnabled(
            enableWorldAPPrototype);

        rotationSweat =
            combatPawn.GetComponent<RotationPressureSweat2D>();

        if (rotationSweat == null)
        {
            rotationSweat = combatPawn.gameObject.
                AddComponent<RotationPressureSweat2D>();
        }

        rotationSweat.Configure(
            sweatSpriteOverride,
            sweatHeadLocalOffset,
            sweatColor,
            sweatSizeRange,
            minimumSweatDropsPerSecond,
            maximumSweatDropsPerSecond,
            sweatSortingLayer,
            sweatSortingOrder);
    }

    private void CaptureBaseAPColor()
    {
        if (capturedBaseAPColor || apFill == null)
            return;

        baseAPFillColor = apFill.color;
        capturedBaseAPColor = true;
    }

    private void ClearRotationCues()
    {
        currentCostMultiplier = 1f;
        currentResolvedSkillUses = 0;
        currentPressure = 0f;
        currentSweatPressure = 0f;
        hasAffordableAPSkill = false;

        if (capturedBaseAPColor && apFill != null)
            apFill.color = baseAPFillColor;

        if (costMultiplierText != null)
            costMultiplierText.gameObject.SetActive(false);

        rotationSweat?.SetPressure(0f, true);
        affordableSkillGlow?.SetReady(false, true);
        worldAPStatusDisplay?.ClearState(true);
    }

    private void OnDisable()
    {
        ClearRotationCues();
    }

    private void OnValidate()
    {
        pressureBeginsAt = Mathf.Max(1f, pressureBeginsAt);
        sweatActivationSkillUses = Mathf.Max(1, sweatActivationSkillUses);
        sweatSizeRange = new Vector2(
            Mathf.Max(0.04f, sweatSizeRange.x),
            Mathf.Max(Mathf.Max(0.04f, sweatSizeRange.x), sweatSizeRange.y));
        maximumSweatDropsPerSecond = Mathf.Max(
            minimumSweatDropsPerSecond,
            maximumSweatDropsPerSecond);
        affordableGlowMaximumPulse = Mathf.Max(
            affordableGlowMinimumPulse,
            affordableGlowMaximumPulse);
        worldAPArcSegments = Mathf.Clamp(worldAPArcSegments, 8, 32);
        worldHealthArcSegments = Mathf.Clamp(worldHealthArcSegments, 8, 32);
        worldAPNumberDuration = Mathf.Max(0.1f, worldAPNumberDuration);
        fullPressureAt = Mathf.Max(
            pressureBeginsAt + 0.01f,
            fullPressureAt);
    }
}
