using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CombatPawnMover : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;

    private Rigidbody2D rb;
    private Vector2 input;

    private void Awake() => rb = GetComponent<Rigidbody2D>();

    private void Update()
    {
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
    }

    private void FixedUpdate()
    {
        var mod = GetComponent<SpeedModifier>();
        float speedMult = mod != null ? mod.Multiplier : 1f;
        rb.MovePosition(rb.position + input * moveSpeed * speedMult * Time.fixedDeltaTime);
    }
}