using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SortByY : MonoBehaviour
{
    [SerializeField] private Transform sortPoint; // feet or base point
    [SerializeField] private int precision = 100;
    [SerializeField] private int orderOffset = 0;

    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        float y = sortPoint ? sortPoint.position.y : transform.position.y;

        // Lower Y should draw in front, so lower Y => higher sortingOrder.
        int order = Mathf.RoundToInt(-y * precision) + orderOffset;

        sr.sortingOrder = order;
    }
}
