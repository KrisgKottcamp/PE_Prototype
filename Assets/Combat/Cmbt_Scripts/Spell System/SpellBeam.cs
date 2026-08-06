using System.Collections.Generic;
using UnityEngine;

public class SpellBeam : MonoBehaviour
{
    private Vector2 origin;
    private Vector2 direction;
    private float length;
    private float width;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;
    private bool phasing;
    private SpellDefinition sourceSpell;

    private Color beamColor = Color.white;

    private bool fired;
    private LineRenderer lineRenderer;
    private float displayTimer;
    private const float DISPLAY_DURATION = 0.3f;

    private static readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[32];

    public void Initialize(SpellDefinition spell, Vector2 orig, Vector2 dir, float len,
        float beamWidth, int dmg, DamageType dmgType,
        List<EffectDefinition> onHitEffects, LayerMask mask, bool isPhasing,
        Color color = default)
    {
        sourceSpell = spell;
        origin = orig;
        direction = dir.normalized;
        length = len;
        width = beamWidth;
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = mask;
        phasing = isPhasing;
        beamColor = color.a > 0f ? color : Color.white;
    }

    private void Start()
    {
        Fire();
        SetupVisual();
    }

    private void Update()
    {
        displayTimer -= Time.deltaTime;
        if (displayTimer <= 0f)
            Destroy(gameObject);
    }

    private void Fire()
    {
        if (fired)
            return;
        fired = true;

        float castLength = phasing ? length : length;

        int hitCount = Physics2D.CircleCastNonAlloc(origin, width * 0.5f, direction,
            hitBuffer, castLength, hitMask);

        HashSet<EnemyHealth> alreadyHit = new();

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = hitBuffer[i].collider;
            if (col == null)
                continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();

            if (enemy != null && !alreadyHit.Contains(enemy))
            {
                enemy.TakeDamage(damage);
                alreadyHit.Add(enemy);
            }

            EffectReceiver receiver = col.GetComponentInParent<EffectReceiver>();
            if (receiver != null && effects != null)
            {
                for (int j = 0; j < effects.Count; j++)
                {
                    if (effects[j] != null)
                        receiver.ApplyEffect(effects[j]);
                }
            }

            if (!phasing && enemy != null)
                break;
        }
    }

    private void SetupVisual()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width * 0.5f;
        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, origin + direction * length);
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = beamColor;
        lineRenderer.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.3f);

        displayTimer = DISPLAY_DURATION;
    }
}
