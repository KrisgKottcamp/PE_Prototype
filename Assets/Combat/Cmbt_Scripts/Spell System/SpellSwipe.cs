using System.Collections.Generic;
using UnityEngine;

public class SpellSwipe : MonoBehaviour
{
    private Vector2 origin;
    private float centerAngle;
    private float arcDegrees;
    private float range;
    private float duration;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;

    private float elapsed;
    private readonly HashSet<int> alreadyHit = new();
    private static readonly Collider2D[] hitBuffer = new Collider2D[16];

    public void Initialize(SpellDefinition spell, Vector2 orig, float center,
        float arc, float rng, float dur, int dmg, DamageType dmgType,
        List<EffectDefinition> onHitEffects, LayerMask mask)
    {
        origin = orig;
        centerAngle = center;
        arcDegrees = arc;
        range = rng;
        duration = Mathf.Max(0.01f, dur);
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = mask;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= duration)
        {
            Destroy(gameObject);
            return;
        }

        float t = elapsed / duration;
        float currentAngle = centerAngle - arcDegrees * 0.5f + arcDegrees * t;
        float rad = currentAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Vector2 hitPos = origin + dir * range * 0.5f;

        int count = Physics2D.OverlapCircleNonAlloc(hitPos, range * 0.3f, hitBuffer, hitMask);

        SpellProjectile.ReflectInArea(hitPos, range * 0.3f, dir);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null)
                continue;

            int id = col.GetInstanceID();
            if (alreadyHit.Contains(id))
                continue;

            alreadyHit.Add(id);

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
                enemy.TakeDamage(damage);

            EffectReceiver receiver = col.GetComponentInParent<EffectReceiver>();
            if (receiver != null && effects != null)
            {
                for (int j = 0; j < effects.Count; j++)
                {
                    if (effects[j] != null)
                        receiver.ApplyEffect(effects[j]);
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        float t = duration > 0 ? elapsed / duration : 0f;
        float currentAngle = centerAngle - arcDegrees * 0.5f + arcDegrees * t;
        float rad = currentAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        Gizmos.DrawWireSphere(origin + dir * range * 0.5f, range * 0.3f);
    }
}
