using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CombatPawnMover : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Optional Modifiers")]
    [SerializeField] private PlayerFocusMode focusMode;

    [Header("Debug")]
    [SerializeField] private bool logEffectiveSpeed = false;

    private Rigidbody2D rb;
    private Vector2 input;

    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (focusMode == null)
            focusMode = GetComponent<PlayerFocusMode>();
    }

    private void Update()
    {
        input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;
    }

    private void FixedUpdate()
    {
        float speedModifierMultiplier = 1f;

        SpeedModifier mod = GetComponent<SpeedModifier>();
        if (mod != null)
            speedModifierMultiplier = mod.Multiplier;

        float focusMultiplier = 1f;

        if (focusMode != null)
            focusMultiplier = focusMode.MoveMultiplier;

        float finalSpeed = moveSpeed * speedModifierMultiplier * focusMultiplier;

        if (logEffectiveSpeed && input.sqrMagnitude > 0.001f)
        {
            Debug.Log(
                $"CombatPawnMover speed: base={moveSpeed}, speedMod={speedModifierMultiplier}, focus={focusMultiplier}, final={finalSpeed}"
            );
        }

        rb.MovePosition(rb.position + input * finalSpeed * Time.fixedDeltaTime);
    }

    private void OnDisable()
    {
        input = Vector2.zero;
    }
}