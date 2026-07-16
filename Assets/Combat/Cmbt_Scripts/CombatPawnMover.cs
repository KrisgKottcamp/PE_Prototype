using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CombatPawnMover : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Optional Modifiers")]
    [SerializeField] private PlayerFocusMode focusMode;
    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("Debug")]
    [SerializeField] private bool logEffectiveSpeed = false;
    [SerializeField] private float debugAttackCommitmentMultiplier = 1f;
    [SerializeField] private float debugFinalSpeed;

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

        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();
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

        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();

        float attackCommitmentMultiplier =
            attackCommitment != null
                ? attackCommitment.MovementMultiplier
                : 1f;

        debugAttackCommitmentMultiplier = attackCommitmentMultiplier;

        float finalSpeed =
            moveSpeed *
            speedModifierMultiplier *
            focusMultiplier *
            attackCommitmentMultiplier;

        debugFinalSpeed = finalSpeed;

        if (logEffectiveSpeed && input.sqrMagnitude > 0.001f)
        {
            Debug.Log(
                $"CombatPawnMover speed: base={moveSpeed}, speedMod={speedModifierMultiplier}, " +
                $"focus={focusMultiplier}, attackCommitment={attackCommitmentMultiplier}, final={finalSpeed}",
                this
            );
        }

        rb.MovePosition(
            rb.position + input * finalSpeed * Time.fixedDeltaTime
        );
    }

    private void OnDisable()
    {
        input = Vector2.zero;
    }
}
