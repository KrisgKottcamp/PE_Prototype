using UnityEngine;

public class PlayerFocusMode : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode focusKey1 = KeyCode.LeftShift;
    [SerializeField] private KeyCode focusKey2 = KeyCode.RightShift;

    [Header("Movement")]
    [Tooltip("Multiply your normal move speed by this while focusing.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float focusMoveMultiplier = 0.6f;

    [Header("Hurtbox Visual")]
    [SerializeField] private GameObject hurtboxVisualRoot; // SpriteRenderer object, or the Hurtbox GO
    [SerializeField] private SpriteRenderer hurtboxRenderer; // optional, if you want to toggle renderer instead of GO

    [Header("Optional Auto Scale")]
    [SerializeField] private CircleCollider2D hurtboxCollider; // if assigned, can auto-scale visual to match collider
    [SerializeField] private bool autoScaleVisualToColliderOnStart = true;

    public bool IsFocusing { get; private set; }
    public float MoveMultiplier => IsFocusing ? focusMoveMultiplier : 1f;

    private void Awake()
    {
        if (hurtboxVisualRoot == null && hurtboxRenderer != null)
            hurtboxVisualRoot = hurtboxRenderer.gameObject;

        SetHurtboxVisible(false);
    }

    private void Start()
    {
        if (autoScaleVisualToColliderOnStart)
            MatchVisualToCollider();
    }

    private void Update()
    {
        bool focusHeld = Input.GetKey(focusKey1) || Input.GetKey(focusKey2);

        if (focusHeld != IsFocusing)
        {
            IsFocusing = focusHeld;
            SetHurtboxVisible(IsFocusing);
        }
    }

    private void SetHurtboxVisible(bool visible)
    {
        if (hurtboxRenderer != null)
            hurtboxRenderer.enabled = visible;

        if (hurtboxVisualRoot != null && hurtboxRenderer == null)
            hurtboxVisualRoot.SetActive(visible);
    }

    [ContextMenu("Match Visual To Collider")]
    public void MatchVisualToCollider()
    {
        if (hurtboxCollider == null) return;

        Transform t = (hurtboxVisualRoot != null) ? hurtboxVisualRoot.transform : (hurtboxRenderer != null ? hurtboxRenderer.transform : null);
        if (t == null) return;

        // Assumes your circle sprite is 1 unit wide at scale 1.
        // If your sprite is not 1 unit, you can tune by eye after this sets a baseline.
        float diameter = hurtboxCollider.radius * 2f;
        t.localScale = new Vector3(diameter, diameter, 1f);
    }
}
