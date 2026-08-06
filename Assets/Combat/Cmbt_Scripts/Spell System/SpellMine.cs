using System.Collections.Generic;
using UnityEngine;

public class SpellMine : MonoBehaviour
{
    private float detectionRadius;
    private float fuseDelay;
    private float detonationRadius;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;
    private SpellDefinition sourceSpell;

    private bool armed;
    private bool fuseTriggered;
    private float fuseTimer;
    private bool detonated;

    private GameObject reticleObj;
    private GameObject detectionRingObj;

    private static readonly Collider2D[] hitBuffer = new Collider2D[32];

    public void Initialize(SpellDefinition spell, float detectRadius, float fuse,
        float blastRadius, int dmg, DamageType dmgType,
        List<EffectDefinition> onHitEffects, LayerMask mask)
    {
        sourceSpell = spell;
        detectionRadius = detectRadius;
        fuseDelay = fuse;
        detonationRadius = blastRadius;
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = mask;

        armed = true;
        CreateVisuals();
    }

    private void Update()
    {
        if (detonated || !armed)
            return;

        if (fuseTriggered)
        {
            fuseTimer -= Time.deltaTime;
            if (reticleObj != null)
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 16f);
                SpriteRenderer sr = reticleObj.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.color = new Color(1f, 0.2f, 0f, 0.5f * pulse);
            }
            if (fuseTimer <= 0f)
                Detonate();
            return;
        }

        int count = Physics2D.OverlapCircleNonAlloc(transform.position, detectionRadius, hitBuffer, hitMask);
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i] == null) continue;
            EnemyHealth enemy = hitBuffer[i].GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                fuseTriggered = true;
                fuseTimer = fuseDelay;
                break;
            }
        }
    }

    private void CreateVisuals()
    {
        detectionRingObj = new GameObject("MineDetectionRing");
        detectionRingObj.transform.position = transform.position;
        detectionRingObj.transform.SetParent(transform);

        SpriteRenderer detSr = detectionRingObj.AddComponent<SpriteRenderer>();
        detSr.sprite = SpellBomb.CreateCircleSprite();
        detSr.color = new Color(0.5f, 0.5f, 1f, 0.15f);
        detSr.sortingLayerName = ResolveSortingLayer();
        detSr.sortingOrder = 4;
        float detScale = detectionRadius * 2f;
        detectionRingObj.transform.localScale = new Vector3(detScale, detScale, 1f);

        reticleObj = new GameObject("MineReticle");
        reticleObj.transform.position = transform.position;
        reticleObj.transform.SetParent(transform);

        SpriteRenderer sr = reticleObj.AddComponent<SpriteRenderer>();
        sr.sprite = SpellBomb.CreateCircleSprite();
        sr.color = new Color(1f, 0.6f, 0f, 0.3f);
        sr.sortingLayerName = ResolveSortingLayer();
        sr.sortingOrder = 5;
        float scale = detonationRadius * 2f;
        reticleObj.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void Detonate()
    {
        detonated = true;

        if (detectionRingObj != null) Destroy(detectionRingObj);
        if (reticleObj != null)
        {
            SpriteRenderer sr = reticleObj.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = new Color(1f, 0.2f, 0f, 0.5f);
        }

        int count = Physics2D.OverlapCircleNonAlloc(transform.position, detonationRadius, hitBuffer, hitMask);
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

        if (sourceSpell != null && sourceSpell.firingVfxPrefab != null)
            Instantiate(sourceSpell.firingVfxPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject, 0.3f);
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
        Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detonationRadius);
    }
}
