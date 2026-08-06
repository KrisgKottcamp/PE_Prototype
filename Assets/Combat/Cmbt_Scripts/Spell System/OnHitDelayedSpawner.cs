using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnHitDelayedSpawner : MonoBehaviour
{
    private SpellDefinition spell;
    private Vector2 pos;
    private float baseAngle;
    private int remainingFires;
    private float delay;
    private LayerMask fallbackMask;

    public void Begin(SpellDefinition spellDef, Vector2 position, float angle,
        int fires, float fireDelay, LayerMask mask)
    {
        spell = spellDef;
        pos = position;
        baseAngle = angle;
        remainingFires = fires;
        delay = fireDelay;
        fallbackMask = mask;
        StartCoroutine(FireRoutine());
    }

    private IEnumerator FireRoutine()
    {
        int fireIndex = 1;
        for (int i = 0; i < remainingFires; i++)
        {
            yield return new WaitForSeconds(delay);
            float angle = baseAngle + spell.modifiers.continuousAngleDeg * fireIndex;
            SpawnOnce(angle);
            fireIndex++;
        }
        Destroy(gameObject);
    }

    private void SpawnOnce(float angle)
    {
        switch (spell.projectileType)
        {
            case CastProjectileType.Bomb:
                GameObject bombObj = new GameObject("SecondaryBomb");
                bombObj.transform.position = pos;
                SpellBomb bomb = bombObj.AddComponent<SpellBomb>();
                bomb.Initialize(spell, spell.bombDelay, spell.bombRadius, spell.damage,
                    spell.damageType, spell.onHitEffects,
                    spell.hitMask.value != 0 ? spell.hitMask : fallbackMask);
                break;

            case CastProjectileType.Circle:
                ApplyAoE(spell, pos);
                break;

            case CastProjectileType.Zone:
                GameObject zoneObj = new GameObject("SecondaryZone");
                zoneObj.transform.position = pos;
                SpellZone zone = zoneObj.AddComponent<SpellZone>();
                zone.Initialize(spell, spell.bombRadius, spell.zoneDuration, spell.zoneTickRate,
                    spell.damage, spell.damageType, spell.onHitEffects,
                    spell.hitMask.value != 0 ? spell.hitMask : fallbackMask,
                    spell.modifiers.vortex, spell.modifiers.vortexStrength);
                break;

            default:
                SpawnByPattern(spell, pos, angle);
                break;
        }
    }

    private void SpawnByPattern(SpellDefinition sp, Vector2 p, float normalAngle)
    {
        int count = Mathf.Max(1, sp.projectileCount);

        if (sp.projectileType == CastProjectileType.Cone ||
            (sp.projectileType == CastProjectileType.Bullet && sp.pattern == ProjectilePattern.Cone))
        {
            if (count == 1)
            {
                SpawnProjectile(sp, p, normalAngle);
                return;
            }
            float arc = sp.coneDegree;
            float start = normalAngle - arc * 0.5f;
            float step = arc / (count - 1);
            for (int i = 0; i < count; i++)
                SpawnProjectile(sp, p, start + step * i);
            return;
        }

        if (sp.projectileType == CastProjectileType.Bullet && sp.pattern == ProjectilePattern.Circle)
        {
            float step = 360f / count;
            for (int i = 0; i < count; i++)
                SpawnProjectile(sp, p, normalAngle + step * i);
            return;
        }

        SpawnProjectile(sp, p, normalAngle);
    }

    private void SpawnProjectile(SpellDefinition sp, Vector2 p, float angleDeg)
    {
        Vector2 dir = new Vector2(
            Mathf.Cos(angleDeg * Mathf.Deg2Rad),
            Mathf.Sin(angleDeg * Mathf.Deg2Rad));

        GameObject obj;
        if (sp.projectileVisual != null && sp.projectileVisual.sprite != null)
            obj = sp.projectileVisual.CreateInstance(p, angleDeg);
        else if (sp.projectilePrefab != null)
            obj = Object.Instantiate(sp.projectilePrefab, p, Quaternion.Euler(0, 0, angleDeg));
        else if (sp.projectileVisual != null)
            obj = sp.projectileVisual.CreateInstance(p, angleDeg);
        else
        {
            obj = new GameObject("SecondaryProjectile");
            obj.transform.position = p;
            obj.transform.rotation = Quaternion.Euler(0, 0, angleDeg);

            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = ProjectileDefinition.CreateFallbackSprite();
            sr.color = new Color(1f, 0.7f, 0.3f, 1f);
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 10;
            obj.transform.localScale = new Vector3(0.2f, 0.2f, 1f);

            CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;
            col.isTrigger = true;

            Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        SpellProjectile proj = obj.GetComponent<SpellProjectile>();
        if (proj == null)
            proj = obj.AddComponent<SpellProjectile>();

        LayerMask mask = sp.hitMask.value != 0 ? sp.hitMask : fallbackMask;

        proj.Initialize(dir, sp.projectileSpeed,
            sp.range / Mathf.Max(0.01f, sp.projectileSpeed),
            sp.damage, sp.damageType, sp.hitBehavior,
            sp.modifiers, sp.onHitEffects, mask,
            sp.modifiers.secondaryEffect, sp.projectileVisual, sp);
    }

    private static readonly Collider2D[] aoeBuf = new Collider2D[32];

    private void ApplyAoE(SpellDefinition sp, Vector2 p)
    {
        LayerMask mask = sp.hitMask.value != 0 ? sp.hitMask : fallbackMask;
        int count = Physics2D.OverlapCircleNonAlloc(p, sp.range, aoeBuf, mask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = aoeBuf[i];
            if (col == null) continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
                enemy.TakeDamage(sp.damage);

            EffectReceiver recv = col.GetComponentInParent<EffectReceiver>();
            if (recv != null && sp.onHitEffects != null)
            {
                for (int j = 0; j < sp.onHitEffects.Count; j++)
                {
                    if (sp.onHitEffects[j] != null)
                        recv.ApplyEffect(sp.onHitEffects[j]);
                }
            }
        }
    }
}
