using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CombatResultsPanel : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI headingText;
    [SerializeField] private TextMeshProUGUI rankText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI targetTimeText;
    [SerializeField] private TextMeshProUGUI averageMomentumText;
    [SerializeField] private TextMeshProUGUI breakdownText;
    [SerializeField] private TextMeshProUGUI continuePromptText;

    [Header("Stars")]
    [SerializeField] private Image[] starImages = new Image[5];
    [SerializeField] private Sprite filledStarSprite;
    [SerializeField] private Sprite emptyStarSprite;

    [SerializeField]
    private Color filledStarColor =
        new Color(1f, 0.84f, 0.25f, 1f);

    [SerializeField]
    private Color emptyStarColor =
        new Color(1f, 1f, 1f, 0.22f);

    [Header("Continue Input")]
    [SerializeField]
    private KeyCode primaryContinueKey =
        KeyCode.Return;

    [SerializeField]
    private KeyCode alternateContinueKey =
        KeyCode.Space;

    [SerializeField, Min(0f)]
    private float inputLockSeconds = 0.45f;

    private Action onContinue;
    private bool isShowing;
    private float inputUnlockTime;

    public bool IsShowing => isShowing;

    private void Awake()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (!isShowing ||
            Time.unscaledTime < inputUnlockTime)
        {
            return;
        }

        bool pressed =
            Input.GetKeyDown(primaryContinueKey) ||
            Input.GetKeyDown(alternateContinueKey) ||
            Input.GetMouseButtonDown(0);

        if (pressed)
            Continue();
    }

    public void Show(
        AttackMomentumResult encounter,
        CombatRankResult rank,
        Action continueCallback)
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        onContinue = continueCallback;
        isShowing = true;

        inputUnlockTime =
            Time.unscaledTime +
            Mathf.Max(0f, inputLockSeconds);

        panelRoot.SetActive(true);

        if (headingText != null)
            headingText.text = "BATTLE COMPLETE";

        if (rankText != null)
            rankText.text = $"{rank.stars} STAR RANK";

        if (timeText != null)
        {
            timeText.text =
                $"TIME  {FormatTime(rank.actualTimeSeconds)}";
        }

        if (targetTimeText != null)
        {
            targetTimeText.text =
                $"TARGET  {FormatTime(rank.targetTimeSeconds)}";
        }

        if (averageMomentumText != null)
        {
            averageMomentumText.text =
                $"ACTIVE AVG  " +
                $"{encounter.activeAverageMomentumNormalized * 100f:0}%\n" +
                $"PEAK  " +
                $"{encounter.peakMomentumNormalized * 100f:0}%";
        }

        if (breakdownText != null)
        {
            string deathLine =
                rank.partyMemberDeaths <= 0
                    ? "PARTY KOS  0"
                    : $"PARTY KOS  {rank.partyMemberDeaths}  " +
                      $"MAX RANK {rank.deathStarCap}";

            breakdownText.text =
                $"TIME SCORE  {rank.timeScore01 * 100f:0}\n" +
                $"MOMENTUM SCORE  {rank.momentumScore01 * 100f:0}\n" +
                $"CLEAN PLAY  {rank.cleanPlayScore01 * 100f:0}\n" +
                $"DAMAGE  {rank.totalDamageTaken}/" +
                $"{rank.combinedPartyMaxHP}\n" +
                $"{deathLine}\n" +
                $"TOTAL SCORE  {rank.overallScore01 * 100f:0}";
        }

        if (continuePromptText != null)
        {
            continuePromptText.text =
                "PRESS ENTER TO CONTINUE";
        }

        RefreshStars(rank.stars);
    }

    public void Continue()
    {
        if (!isShowing)
            return;

        isShowing = false;

        Action callback = onContinue;
        onContinue = null;

        if (panelRoot != null)
            panelRoot.SetActive(false);

        callback?.Invoke();
    }

    public void HideImmediately()
    {
        isShowing = false;
        onContinue = null;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void RefreshStars(int earnedStars)
    {
        earnedStars = Mathf.Clamp(
            earnedStars,
            1,
            5
        );

        if (starImages == null)
            return;

        for (int i = 0; i < starImages.Length; i++)
        {
            Image image = starImages[i];

            if (image == null)
                continue;

            bool filled = i < earnedStars;

            if (filled && filledStarSprite != null)
                image.sprite = filledStarSprite;
            else if (!filled && emptyStarSprite != null)
                image.sprite = emptyStarSprite;

            image.color =
                filled
                    ? filledStarColor
                    : emptyStarColor;
        }
    }

    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes =
            Mathf.FloorToInt(seconds / 60f);

        int wholeSeconds =
            Mathf.FloorToInt(seconds) % 60;

        int hundredths =
            Mathf.FloorToInt(
                (seconds - Mathf.Floor(seconds)) *
                100f
            );

        return
            $"{minutes:00}:{wholeSeconds:00}." +
            $"{hundredths:00}";
    }
}
