using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the party-wide Attack Momentum Gauge and encounter clock.
///
/// Gameplay values update immediately in AttackMomentumManager.
/// This script animates only upward HUD movement so gains feel fluid.
/// The manager's existing smooth drain remains responsive and readable.
/// </summary>
public class AttackMomentumHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AttackMomentumManager momentumManager;
    [SerializeField] private Image currentMomentumFill;
    [SerializeField] private Image bankedMomentumFill;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI momentumText;

    [Header("Gain Animation")]
    [SerializeField] private bool animateGains = true;

    [Tooltip(
        "How long the red Current Momentum fill takes to catch up after a gain."
    )]
    [SerializeField, Min(0.01f)]
    private float currentGainSmoothTime = 0.16f;

    [Tooltip(
        "How quickly the mauve Banked fill reaches the new banked value. " +
        "Keep this lower than Current Gain Smooth Time."
    )]
    [SerializeField, Min(0.01f)]
    private float bankedGainSmoothTime = 0.06f;

    [Tooltip(
        "Prevents SmoothDamp from moving so fast that a small gain appears instant."
    )]
    [SerializeField, Min(0.01f)]
    private float minimumVisibleGainSpeed = 0.35f;

    [Header("Display")]
    [SerializeField] private bool showHundredths = true;
    [SerializeField] private bool showMomentumPercent = false;

    private float displayedCurrent;
    private float displayedBanked;

    private float currentGainVelocity;
    private float bankedGainVelocity;

    private void Awake()
    {
        displayedCurrent = 0f;
        displayedBanked = 0f;

        ApplyFillAmounts();
    }

    private void OnEnable()
    {
        currentGainVelocity = 0f;
        bankedGainVelocity = 0f;
    }

    private void Update()
    {
        FindMomentumManagerIfNeeded();

        if (momentumManager == null)
            return;

        UpdateMomentumFills();
        UpdateText();
    }

    private void FindMomentumManagerIfNeeded()
    {
        if (momentumManager != null)
            return;

        momentumManager = AttackMomentumManager.Instance;

        if (momentumManager == null)
        {
            momentumManager =
                FindObjectOfType<AttackMomentumManager>(true);
        }
    }

    private void UpdateMomentumFills()
    {
        float targetCurrent =
            Mathf.Clamp01(momentumManager.CurrentNormalized);

        float targetBanked =
            Mathf.Clamp01(momentumManager.BankedNormalized);

        if (!animateGains)
        {
            SnapToTargets(targetCurrent, targetBanked);
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // The mauve banked fill gets ahead first, creating a visible preview
        // of the amount the red fill is about to reach.
        if (targetBanked > displayedBanked)
        {
            displayedBanked = SmoothGain(
                displayedBanked,
                targetBanked,
                ref bankedGainVelocity,
                bankedGainSmoothTime,
                dt
            );
        }
        else
        {
            // Banked value stays fixed during drain and clears immediately
            // when the chain breaks.
            displayedBanked = targetBanked;
            bankedGainVelocity = 0f;
        }

        if (targetCurrent > displayedCurrent)
        {
            displayedCurrent = SmoothGain(
                displayedCurrent,
                targetCurrent,
                ref currentGainVelocity,
                currentGainSmoothTime,
                dt
            );
        }
        else
        {
            // Gameplay already drains smoothly every frame. Following the
            // target directly avoids adding sluggishness to the countdown.
            displayedCurrent = targetCurrent;
            currentGainVelocity = 0f;
        }

        displayedCurrent = Mathf.Clamp01(displayedCurrent);
        displayedBanked = Mathf.Clamp01(displayedBanked);

        ApplyFillAmounts();
    }

    private float SmoothGain(
        float displayed,
        float target,
        ref float velocity,
        float smoothTime,
        float deltaTime)
    {
        float distance = Mathf.Max(0f, target - displayed);

        // Small gains can otherwise look almost instantaneous. This minimum
        // duration keeps them readable while allowing repeated hits to stack.
        float distanceBasedSmoothTime =
            distance / Mathf.Max(0.01f, minimumVisibleGainSpeed);

        float finalSmoothTime = Mathf.Max(
            Mathf.Max(0.01f, smoothTime),
            Mathf.Min(distanceBasedSmoothTime, 0.24f)
        );

        float result = Mathf.SmoothDamp(
            displayed,
            target,
            ref velocity,
            finalSmoothTime,
            Mathf.Infinity,
            deltaTime
        );

        if (Mathf.Abs(target - result) < 0.0005f)
        {
            velocity = 0f;
            return target;
        }

        return result;
    }

    private void SnapToTargets(
        float targetCurrent,
        float targetBanked)
    {
        displayedCurrent = targetCurrent;
        displayedBanked = targetBanked;

        currentGainVelocity = 0f;
        bankedGainVelocity = 0f;

        ApplyFillAmounts();
    }

    private void ApplyFillAmounts()
    {
        if (currentMomentumFill != null)
            currentMomentumFill.fillAmount = displayedCurrent;

        if (bankedMomentumFill != null)
            bankedMomentumFill.fillAmount = displayedBanked;
    }

    private void UpdateText()
    {
        if (timerText != null)
        {
            timerText.text =
                FormatTime(momentumManager.EncounterSeconds);
        }

        if (momentumText != null)
        {
            momentumText.text = showMomentumPercent
                ? $"{momentumManager.CurrentNormalized * 100f:0}%"
                : "MOMENTUM";
        }
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds) % 60;

        if (!showHundredths)
            return $"{minutes:00}:{wholeSeconds:00}";

        int hundredths = Mathf.FloorToInt(
            (seconds - Mathf.Floor(seconds)) * 100f
        );

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
    }
}
