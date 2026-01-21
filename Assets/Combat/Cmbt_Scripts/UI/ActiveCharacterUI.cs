using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ActiveCharacterUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image portraitImage; // optional

    private int lastActiveIndex = -1;

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        if (pm.activeIndex != lastActiveIndex)
            Refresh();
    }

    private void Refresh()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return;

        lastActiveIndex = pm.activeIndex;

        var def = pm.Active.def;
        if (def == null) return;

        if (nameText != null)
            nameText.text = def.displayName;

        if (portraitImage != null)
        {
            // Prefer portrait sprite if provided, otherwise use combat sprite
            Sprite s = def.portraitSprite != null ? def.portraitSprite : def.combatSprite;
            portraitImage.sprite = s;
            portraitImage.enabled = s != null;
        }
    }
}
