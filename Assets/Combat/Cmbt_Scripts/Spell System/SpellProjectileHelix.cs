using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class SpellProjectileHelix : MonoBehaviour
{
    private Vector2 forwardDir;
    private float speed;
    private float lifetime;
    private float currentRadius;
    private float growthRate;
    private Vector2 center;

    private int damage;
    private DamageType damageType;
    private ProjectileHitBehavior hitBehavior;
    private List<EffectDefinition> onHitEffects;
    private LayerMask hitMask;

    private ProjectileDefinition visual;

    private Rigidbody2D rb;
    private float elapsed;
    private float angularSpeed;

    public void Initialize(
        Vector2 origin, Vector2 dir, float spd, float life,
        float initialRadius, float growth,
        int dmg, DamageType dmgType, ProjectileHitBehavior hit,
        List<EffectDefinition> effects, LayerMask mask,
        ProjectileDefinition projVisual = null)
    {
        visual = projVisual;
        center = origin;
        forwardDir = dir.normalized;
        speed = spd;
        lifetime = life;
        currentRadius = initialRadius;
        growthRate = growth;
        damage = dmg;
        damageType = dmgType;
        hitBehavior = hit;
        onHitEffects = effects;
        hitMask = mask;

        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        angularSpeed = speed / Mathf.Max(0.01f, currentRadius);

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        currentRadius += growthRate * Time.fixedDeltaTime;
        angularSpeed = speed / Mathf.Max(0.01f, currentRadius);

        center += forwardDir * speed * Time.fixedDeltaTime;

        float angle = angularSpeed * elapsed;
        Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * currentRadius;

        rb.MovePosition(center + offset);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        if (((1 << other.gameObject.layer) & hitMask.value) == 0)
            return;

        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            enemy.TakeDamage(damage);

        EffectReceiver receiver = other.GetComponentInParent<EffectReceiver>();
        if (receiver != null && onHitEffects != null)
        {
            for (int i = 0; i < onHitEffects.Count; i++)
            {
                if (onHitEffects[i] != null)
                    receiver.ApplyEffect(onHitEffects[i]);
            }
        }

        if (hitBehavior == ProjectileHitBehavior.Direct)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (visual != null)
            visual.SpawnImpactVfx(transform.position);
    }
}
