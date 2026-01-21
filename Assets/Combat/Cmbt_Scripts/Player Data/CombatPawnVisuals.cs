using UnityEngine;

public class CombatPawnVisuals : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private int lastActiveIndex = -1;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

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

    public void Refresh()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.party == null || pm.party.Count == 0) return;

        lastActiveIndex = pm.activeIndex;

        var def = pm.Active.def;
        if (def != null && def.combatSprite != null && spriteRenderer != null)
            spriteRenderer.sprite = def.combatSprite;
    }
}
