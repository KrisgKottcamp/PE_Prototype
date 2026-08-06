using System.Collections.Generic;
using UnityEngine;

public class SpellBomb : MonoBehaviour
{
    private float delay;
    private float radius;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;
    private SpellDefinition sourceSpell;
    private float timer;
    private bool detonated;

    private GameObject reticleObj;
    private GameObject damageCircleObj;
    private const float DAMAGE_CIRCLE_DURATION = 0.3f;
    private float damageCircleTimer;

    private static readonly Collider2D[] hitBuffer = new Collider2D[32];

    public void Initialize(SpellDefinition spell, float bombDelay, float bombRadius,
        int dmg, DamageType dmgType, List<EffectDefinition> onHitEffects, LayerMask mask)
    {
        sourceSpell = spell;
        delay = bombDelay;
        radius = bombRadius;
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = mask;
        timer = delay;

        CreateReticle();
    }

    private void Update()
    {
        if (detonated)
        {
            damageCircleTimer -= Time.deltaTime;
            if (damageCircleTimer <= 0f)
                Destroy(gameObject);
            else if (damageCircleObj != null)
            {
                float t = 1f - damageCircleTimer / DAMAGE_CIRCLE_DURATION;
                float alpha = Mathf.Lerp(0.5f, 0f, t);
                SpriteRenderer sr = damageCircleObj.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.color = new Color(1f, 0.2f, 0f, alpha);
            }
            return;
        }

        timer -= Time.deltaTime;

        if (reticleObj != null)
        {
            float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 8f);
            float reticleScale = radius * 2f * pulse;
            reticleObj.transform.localScale = new Vector3(reticleScale, reticleScale, 1f);
        }

        if (timer <= 0f)
            Detonate();
    }

    private void CreateReticle()
    {
        reticleObj = new GameObject("BombReticle");
        reticleObj.transform.position = transform.position;
        reticleObj.transform.SetParent(transform);

        SpriteRenderer sr = reticleObj.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite();
        sr.color = new Color(1f, 0.8f, 0f, 0.35f);
        sr.sortingLayerName = ResolveSortingLayer();
        sr.sortingOrder = 5;

        float scale = radius * 2f;
        reticleObj.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void CreateDamageCircle()
    {
        damageCircleObj = new GameObject("BombDamageCircle");
        damageCircleObj.transform.position = transform.position;
        damageCircleObj.transform.SetParent(transform);

        SpriteRenderer sr = damageCircleObj.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite();
        sr.color = new Color(1f, 0.2f, 0f, 0.5f);
        sr.sortingLayerName = ResolveSortingLayer();
        sr.sortingOrder = 6;

        float scale = radius * 2f;
        damageCircleObj.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void Detonate()
    {
        detonated = true;

        if (reticleObj != null)
            Destroy(reticleObj);

        CreateDamageCircle();
        damageCircleTimer = DAMAGE_CIRCLE_DURATION;

        int count = Physics2D.OverlapCircleNonAlloc(transform.position, radius, hitBuffer, hitMask);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null)
                continue;

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

    private static Sprite circleCache;

    public static Sprite CreateCircleSprite()
    {
        if (circleCache != null)
            return circleCache;

        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float center = (res - 1) * 0.5f;
        float outerR = center;
        float ringWidth = 3f;
        float innerFill = outerR * 0.3f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                if (dist > outerR)
                {
                    tex.SetPixel(x, y, Color.clear);
                }
                else if (dist > outerR - ringWidth)
                {
                    float edge = 1f - (outerR - dist) / ringWidth;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, 1f - edge * 0.5f));
                }
                else if (dist <= innerFill)
                {
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.3f));
                }
                else
                {
                    float t = (dist - innerFill) / (outerR - ringWidth - innerFill);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Lerp(0.3f, 0.15f, t)));
                }
            }
        }

        tex.Apply();
        circleCache = Sprite.Create(tex, new Rect(0, 0, res, res),
            new Vector2(0.5f, 0.5f), res);
        return circleCache;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
