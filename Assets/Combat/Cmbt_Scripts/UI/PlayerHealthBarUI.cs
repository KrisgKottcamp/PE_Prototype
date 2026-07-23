using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

public class PlayerHealthBarUI : MonoBehaviour
{
    [SerializeField] private Image hpFill;

    // Optional, only if you use TMP.
    // If you do not, you can remove this field and the related code.
    [SerializeField] private TMPro.TextMeshProUGUI hpText;

    [Header("Smoothing")]
    [SerializeField] private bool smooth = true;
    [SerializeField] private float smoothSpeed = 12f;

    [Header("Healing Flash")]
    [SerializeField] private bool flashHealthBarOnHeal = true;
    [SerializeField] private Color healingFlashColor =
        new Color(0.12f, 1f, 0.30f, 1f);
    [SerializeField, Min(0.05f)] private float healingFlashDuration = 1f;
    [SerializeField, Min(0f)] private float healingFlashPulseCount = 3f;

    private float displayed01 = 1f;
    private Color baseHPFillColor = Color.white;
    private bool hasCapturedBaseHPColor;
    private Coroutine healingFlashRoutine;

    private void Awake()
    {
        if (hpFill == null)
            Debug.LogError("PlayerHealthBarUI: hpFill is not assigned.");

        CaptureBaseHPFillColor();
    }

    private void OnEnable()
    {
        PartyManager.PartyMemberHealed += HandlePartyMemberHealed;
    }

    private void OnDisable()
    {
        PartyManager.PartyMemberHealed -= HandlePartyMemberHealed;
        StopHealingFlash(restoreColor: true);
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return;

        var active = pm.Active;
        if (active == null || active.def == null) return;

        int maxHp = Mathf.Max(1, active.def.maxHP);
        int curHp = Mathf.Clamp(active.currentHP, 0, maxHp);

        float target01 = (float)curHp / maxHp;

        if (!smooth)
        {
            displayed01 = target01;
        }
        else
        {
            // Use unscaledDeltaTime so it still animates when time is slowed
            displayed01 = Mathf.Lerp(displayed01, target01, 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime));
        }

        if (hpFill != null)
            hpFill.fillAmount = displayed01;

        if (hpText != null)
            hpText.text = $"{curHp} / {maxHp}";
    }

    private void HandlePartyMemberHealed(
        int healedPartyIndex,
        int restoredAmount)
    {
        PartyManager manager = PartyManager.Instance;

        if (!flashHealthBarOnHeal || restoredAmount <= 0 ||
            manager == null || healedPartyIndex != manager.activeIndex ||
            hpFill == null)
        {
            return;
        }

        StartHealingFlash();
    }

    private void StartHealingFlash()
    {
        CaptureBaseHPFillColor();
        StopHealingFlash(restoreColor: true);
        healingFlashRoutine = StartCoroutine(HealingFlashRoutine());
    }

    private IEnumerator HealingFlashRoutine()
    {
        float duration = Mathf.Max(0.05f, healingFlashDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pulse = healingFlashPulseCount > 0f
                ? Mathf.Lerp(
                    0.68f,
                    1f,
                    Mathf.Abs(Mathf.Cos(
                        normalized * Mathf.PI * healingFlashPulseCount
                    ))
                )
                : 1f;
            float strength = (1f - normalized) * pulse;

            hpFill.color = Color.Lerp(
                baseHPFillColor,
                healingFlashColor,
                strength
            );

            yield return null;
        }

        hpFill.color = baseHPFillColor;
        healingFlashRoutine = null;
    }

    private void CaptureBaseHPFillColor()
    {
        if (hasCapturedBaseHPColor || hpFill == null)
            return;

        baseHPFillColor = hpFill.color;
        hasCapturedBaseHPColor = true;
    }

    private void StopHealingFlash(bool restoreColor)
    {
        if (healingFlashRoutine != null)
        {
            StopCoroutine(healingFlashRoutine);
            healingFlashRoutine = null;
        }

        if (restoreColor && hpFill != null && hasCapturedBaseHPColor)
            hpFill.color = baseHPFillColor;
    }
}
