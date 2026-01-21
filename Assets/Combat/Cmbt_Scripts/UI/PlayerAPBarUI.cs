using UnityEngine;
using UnityEngine.UI;

public class PlayerAPBarUI : MonoBehaviour
{
    [SerializeField] private Image apFill;
    [SerializeField] private bool smooth = true;
    [SerializeField] private float smoothSpeed = 12f;

    private float displayed01;

    private void Start()
    {
        displayed01 = 0f;
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return;

        var active = pm.Active;
        if (active == null || active.def == null) return;

        int maxAP = Mathf.Max(1, active.def.maxAP);
        int curAP = Mathf.Clamp(active.currentAP, 0, maxAP);

        float target01 = (float)curAP / maxAP;

        if (!smooth) displayed01 = target01;
        else displayed01 = Mathf.Lerp(displayed01, target01, 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime));

        if (apFill != null) apFill.fillAmount = displayed01;
    }
}
