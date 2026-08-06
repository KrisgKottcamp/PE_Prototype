using System.Collections.Generic;
using UnityEngine;

public class SpellZone : MonoBehaviour
{
    private float radius;
    private float duration;
    private float tickRate;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;
    private SpellDefinition sourceSpell;

    private bool vortex;
    private float vortexStrength;

    private float remainingDuration;
    private float tickTimer;

    private GameObject zoneVisualObj;
    private SpriteRenderer zoneSr;

    private static readonly Collider2D[] hitBuffer = new Collider2D[32];

    public void Initialize(SpellDefinition spell, float zoneRadius, float dur,
        float tick, int dmg, DamageType dmgType,
        List<EffectDefinition> onHitEffects, LayerMask mask,
        bool hasVortex, float vortexForce)
    {
        sourceSpell = spell;
        radius = zoneRadius;
        duration = dur;
        tickRate = Mathf.Max(0.05f, tick);
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = mask;
        vortex = hasVortex;
        vortexStrength = vortexForce;

        remainingDuration = duration;
        tickTimer = 0f;

        CreateVisual();
    }

    private void Update()
    {
        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0f)
        {
            if (sourceSpell != null && sourceSpell.antefireVfxPrefab != null)
                Instantiate(sourceSpell.antefireVfxPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
            return;
        }

        if (zoneSr != null)
        {
            float fadeT = Mathf.Clamp01(remainingDuration / Mathf.Min(duration, 1f));
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
            zoneSr.color = new Color(zoneSr.color.r, zoneSr.color.g, zoneSr.color.b,
                0.3f * fadeT * pulse);
        }

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            ApplyTick();
            tickTimer = tickRate;
        }

        if (vortex)
            ApplyVortex();
    }

    private void ApplyTick()
    {
        int count = Physics2D.OverlapCircleNonAlloc(transform.position, radius, hitBuffer, hitMask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null) continue;

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

    private void ApplyVortex()
    {
        Vector2 center = transform.position;
        float dt = Time.deltaTime;

        int count = Physics2D.OverlapCircleNonAlloc(center, radius, hitBuffer, hitMask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null) continue;

            Rigidbody2D rb = col.attachedRigidbody;
            if (rb == null) continue;

            Vector2 toCenter = center - rb.position;
            float dist = toCenter.magnitude;
            if (dist < 0.1f) continue;

            float pullFactor = 1f - Mathf.Clamp01(dist / radius);
            Vector2 force = toCenter.normalized * vortexStrength * pullFactor * dt;
            rb.position += force;
        }

        Rigidbody2D[] allBodies = FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None);
        for (int i = 0; i < allBodies.Length; i++)
        {
            Rigidbody2D rb = allBodies[i];
            SpellProjectile proj = rb.GetComponent<SpellProjectile>();
            SpellProjectileHelix helix = rb.GetComponent<SpellProjectileHelix>();
            if (proj == null && helix == null) continue;

            Vector2 toCenter = center - rb.position;
            float dist = toCenter.magnitude;
            if (dist > radius || dist < 0.1f) continue;

            float pullFactor = 1f - Mathf.Clamp01(dist / radius);
            rb.position += toCenter.normalized * vortexStrength * pullFactor * dt;
        }
    }

    private void CreateVisual()
    {
        zoneVisualObj = new GameObject("ZoneVisual");
        zoneVisualObj.transform.position = transform.position;
        zoneVisualObj.transform.SetParent(transform);

        zoneSr = zoneVisualObj.AddComponent<SpriteRenderer>();
        zoneSr.sprite = SpellBomb.CreateCircleSprite();

        if (vortex)
            zoneSr.color = new Color(0.6f, 0.2f, 1f, 0.3f);
        else
            zoneSr.color = new Color(0.2f, 1f, 0.4f, 0.3f);

        zoneSr.sortingLayerName = ResolveSortingLayer();
        zoneSr.sortingOrder = 4;

        float scale = radius * 2f;
        zoneVisualObj.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private static string ResolveSortingLayer()
    {
        string[] candidates = { "Foreground", "VFX", "Characters" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (SortingLayer.NameToID(candidates[i]) != 0)
                return candidates[i];
        }
        return "Default";
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = vortex
            ? new Color(0.6f, 0.2f, 1f, 0.4f)
            : new Color(0.2f, 1f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
