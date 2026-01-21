using UnityEngine;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private GameObject barPrefab;

    [Header("Layout (world units)")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.7f, 0f);
    [SerializeField] private float width = 0.8f;
    [SerializeField] private float height = 0.12f;

    [Header("Render")]
    [SerializeField] private string sortingLayerName = "UI";
    [SerializeField] private int bgOrder = 500;

    [Header("Behavior")]
    [SerializeField] private bool hideWhenFull = true;

    private EnemyHealth health;

    private Transform barRoot;
    private Transform bg;
    private Transform fill;

    private SpriteRenderer bgSR;
    private SpriteRenderer fillSR;

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        if (health == null) { enabled = false; return; }

        if (barPrefab == null)
        {
            Debug.LogWarning($"EnemyHealthBar on '{name}': barPrefab not assigned.");
            enabled = false;
            return;
        }

        var inst = Instantiate(barPrefab, transform);
        inst.name = "EnemyHPBar";
        barRoot = inst.transform;

        bg = barRoot.Find("BG");
        fill = barRoot.Find("Fill");

        if (bg == null || fill == null)
        {
            Debug.LogWarning("EnemyHPBar prefab must have children named BG and Fill.");
            enabled = false;
            return;
        }

        bgSR = bg.GetComponent<SpriteRenderer>();
        fillSR = fill.GetComponent<SpriteRenderer>();

        if (bgSR == null || fillSR == null || bgSR.sprite == null || fillSR.sprite == null)
        {
            Debug.LogWarning("BG and Fill must have SpriteRenderer components with sprites assigned.");
            enabled = false;
            return;
        }

        // Force render order
        bgSR.sortingLayerName = sortingLayerName;
        bgSR.sortingOrder = bgOrder;

        fillSR.sortingLayerName = sortingLayerName;
        fillSR.sortingOrder = bgOrder + 1;

        // Initial draw
        Redraw();
    }

    private void LateUpdate()
    {
        if (barRoot == null || health == null) return;

        barRoot.localPosition = localOffset;
        Redraw();
    }

    private void Redraw()
    {
        int maxHp = Mathf.Max(1, health.MaxHP);
        int curHp = Mathf.Clamp(health.CurrentHP, 0, maxHp);

        if (hideWhenFull && curHp >= maxHp)
        {
            if (barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(false);
            return;
        }

        if (!barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);

        float pct = (float)curHp / maxHp;

        // Size BG to (width, height) in world units, regardless of sprite PPU
        SetSpriteWorldSize(bg, bgSR, width, height);

        // Size Fill to (width*pct, height)
        float fillW = width * pct;
        SetSpriteWorldSize(fill, fillSR, fillW, height);

        // Left-anchor fill inside bg
        float leftEdge = -width * 0.5f;
        fill.localPosition = new Vector3(leftEdge + fillW * 0.5f, 0f, 0f);
        bg.localPosition = Vector3.zero;
    }

    private void SetSpriteWorldSize(Transform t, SpriteRenderer sr, float desiredW, float desiredH)
    {
        // sprite.bounds.size is in local units at scale 1
        Vector2 spriteSize = sr.sprite.bounds.size;

        float sx = spriteSize.x > 0f ? desiredW / spriteSize.x : 1f;
        float sy = spriteSize.y > 0f ? desiredH / spriteSize.y : 1f;

        t.localScale = new Vector3(sx, sy, 1f);
    }
}
