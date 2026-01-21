using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private float lifetime = 6f;
    [SerializeField] private int damage = 5;

    [SerializeField] private LayerMask hitMask; // PlayerHurtbox + Obstacles

    private Rigidbody2D rb;
    private Vector2 dir;

    public void Fire(Vector2 direction)
    {
        dir = direction.normalized;
        Destroy(gameObject, lifetime);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + dir * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Blocked by cover or walls
        if (((1 << other.gameObject.layer) & hitMask.value) == 0) return;

        // Hit player hurtbox
        var pawn = other.GetComponentInParent<CombatPawn>();
        if (pawn != null)
        {
            pawn.ApplyDamage(damage);
            Destroy(gameObject);
            return;
        }

        // Hit cover/wall
        Destroy(gameObject);
    }
}
