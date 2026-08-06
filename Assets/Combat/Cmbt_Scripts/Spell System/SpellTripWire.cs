using System.Collections.Generic;
using UnityEngine;

public class SpellTripWire : MonoBehaviour
{
    private Vector2 origin;
    private Vector2 aimDir;
    private float range;
    private int damage;
    private DamageType damageType;
    private List<EffectDefinition> effects;
    private LayerMask hitMask;
    private LayerMask wallMask;
    private SpellDefinition sourceSpell;

    private Vector2 endpointA;
    private Vector2 endpointB;
    private bool wireFormed;
    private float wireWidth = 0.08f;
    private float lifetime = 10f;
    private float timer;

    private LineRenderer lineRenderer;
    private static readonly Collider2D[] hitBuffer = new Collider2D[32];
    private readonly HashSet<int> alreadyHit = new();

    public void Initialize(SpellDefinition spell, Vector2 castOrigin, Vector2 castDir,
        float castRange, int dmg, DamageType dmgType,
        List<EffectDefinition> onHitEffects, LayerMask hitLayer, LayerMask blockLayer)
    {
        sourceSpell = spell;
        origin = castOrigin;
        aimDir = castDir.normalized;
        range = castRange;
        damage = dmg;
        damageType = dmgType;
        effects = onHitEffects;
        hitMask = hitLayer;
        wallMask = blockLayer;

        FormWire();
    }

    private void FormWire()
    {
        Vector2 perpRight = new Vector2(aimDir.y, -aimDir.x);
        Vector2 perpLeft = -perpRight;

        endpointA = CastToEndpoint(origin, perpRight);
        endpointB = CastToEndpoint(origin, perpLeft);

        wireFormed = true;
        timer = lifetime;

        CreateLineVisual();
    }

    private Vector2 CastToEndpoint(Vector2 start, Vector2 dir)
    {
        RaycastHit2D hit = Physics2D.Raycast(start, dir, range, wallMask);
        if (hit.collider != null)
            return hit.point;
        return start + dir * range;
    }

    private void CreateLineVisual()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, endpointA);
        lineRenderer.SetPosition(1, endpointB);
        lineRenderer.startWidth = wireWidth;
        lineRenderer.endWidth = wireWidth;
        lineRenderer.startColor = new Color(0.3f, 0.8f, 1f, 0.7f);
        lineRenderer.endColor = new Color(0.3f, 0.8f, 1f, 0.7f);
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        string sortLayer = ResolveSortingLayer();
        lineRenderer.sortingLayerName = sortLayer;
        lineRenderer.sortingOrder = 8;
    }

    private void Update()
    {
        if (!wireFormed)
            return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (timer < 2f && lineRenderer != null)
        {
            float alpha = timer / 2f;
            lineRenderer.startColor = new Color(0.3f, 0.8f, 1f, 0.7f * alpha);
            lineRenderer.endColor = new Color(0.3f, 0.8f, 1f, 0.7f * alpha);
        }

        CheckLineIntersections();
    }

    private void CheckLineIntersections()
    {
        Vector2 wireDir = (endpointB - endpointA);
        float wireLen = wireDir.magnitude;
        if (wireLen < 0.01f) return;
        Vector2 wireNorm = wireDir / wireLen;

        float checkRadius = wireLen * 0.5f + 1f;
        Vector2 center = (endpointA + endpointB) * 0.5f;

        int count = Physics2D.OverlapCircleNonAlloc(center, checkRadius, hitBuffer, hitMask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i];
            if (col == null) continue;

            int id = col.GetInstanceID();
            if (alreadyHit.Contains(id)) continue;

            Vector2 closestPoint = col.ClosestPoint(center);
            float distToLine = DistanceToSegment(closestPoint, endpointA, endpointB);

            if (distToLine < 0.3f)
            {
                alreadyHit.Add(id);
                ApplyHit(col);
            }
        }
    }

    private void ApplyHit(Collider2D col)
    {
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

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sqLen = ab.sqrMagnitude;
        if (sqLen < 0.0001f) return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqLen);
        Vector2 projection = a + ab * t;
        return Vector2.Distance(point, projection);
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
        if (wireFormed)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(endpointA, endpointB);
        }
    }
}
