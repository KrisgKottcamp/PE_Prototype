using UnityEngine;

public class CombatAPController : MonoBehaviour
{
    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return;

        var active = pm.Active;
        if (active == null || active.def == null) return;

        int maxAP = Mathf.Max(0, active.def.maxAP);
        if (maxAP <= 0) return;

        float regen = active.def.apRegenPerSecond;
        if (regen <= 0f) return;

        // Regen should slow down with slowed time, so use Time.deltaTime
        float apFloat = active.currentAP + regen * Time.deltaTime;
        active.currentAP = Mathf.Clamp(Mathf.FloorToInt(apFloat), 0, maxAP);
    }
}
