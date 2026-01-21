using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private float lifetime = 2.5f;

    [SerializeField] private LayerMask hitMask;
    [SerializeField] private int damage = 3;
    [SerializeField] private float stunSeconds = 0.15f;

    private Rigidbody2D rb;
    private Vector2 dir;

    private int ownerCharacterIndex = -1;
    private bool awardApOnHit = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    public void Fire(
        Vector2 direction,
        int ownerIndex,
        int dmg,
        float stun,
        float projectileSpeed,
        float life,
        LayerMask mask,
        bool awardAp)
    {
        dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;

        ownerCharacterIndex = ownerIndex;
        damage = Mathf.Max(0, dmg);
        stunSeconds = Mathf.Max(0f, stun);
        speed = Mathf.Max(0.01f, projectileSpeed);
        lifetime = Mathf.Max(0.05f, life);
        hitMask = mask;
        awardApOnHit = awardAp;

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + dir * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & hitMask.value) == 0) return;

        var enemyHealth = other.GetComponentInParent<EnemyHealth>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(damage);

            var stunnable = other.GetComponentInParent<EnemyStunnable>();
            if (stunnable != null) stunnable.Stun(stunSeconds);

            if (awardApOnHit && PartyManager.Instance != null && PartyManager.Instance.activeIndex == ownerCharacterIndex)
            {
                var pm = PartyManager.Instance;
                if (ownerCharacterIndex >= 0 && ownerCharacterIndex < pm.party.Count)
                {
                    var owner = pm.party[ownerCharacterIndex];
                    if (owner.def != null)
                    {
                        int maxAP = Mathf.Max(0, owner.def.maxAP);
                        int gain = Mathf.Max(0, owner.def.apGainOnBasicHit);
                        owner.currentAP = Mathf.Clamp(owner.currentAP + gain, 0, maxAP);
                    }
                }
            }

            Destroy(gameObject);
            return;
        }

        // cover/walls
        Destroy(gameObject);
    }
}
