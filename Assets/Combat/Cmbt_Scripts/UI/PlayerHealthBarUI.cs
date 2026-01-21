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

    private float displayed01 = 1f;

    private void Awake()
    {
        if (hpFill == null)
            Debug.LogError("PlayerHealthBarUI: hpFill is not assigned.");
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
}
