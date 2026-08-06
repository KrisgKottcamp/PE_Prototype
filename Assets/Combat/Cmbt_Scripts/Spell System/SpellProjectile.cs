using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class SpellProjectile : MonoBehaviour
{
    private Vector2 direction;
    private float speed;
    private float lifetime;
    private int damage;
    private DamageType damageType;
    private ProjectileHitBehavior hitBehavior;
    private SpellModifiers modifiers;
    private List<EffectDefinition> onHitEffects;
    private LayerMask hitMask;
    private SpellDefinition secondarySpell;
    private SpellDefinition sourceSpell;

    private ProjectileDefinition visual;

    private Rigidbody2D rb;
    private int remainingBounces;
    private int remainingPassThrough;
    private Vector2 originPos;
    private bool returning;
    private Transform homingTarget;
    private float totalDistance;

    public void Initialize(
        Vector2 dir, float spd, float life,
        int dmg, DamageType dmgType, ProjectileHitBehavior hit,
        SpellModifiers mods, List<EffectDefinition> effects,
        LayerMask mask, SpellDefinition secondary,
        ProjectileDefinition projVisual = null,
        SpellDefinition source = null)
    {
        visual = projVisual;
        direction = dir.normalized;
        speed = spd;
        lifetime = life;
        damage = dmg;
        damageType = dmgType;
        hitBehavior = hit;
        modifiers = mods ?? new SpellModifiers();
        onHitEffects = effects;
        hitMask = mask;
        secondarySpell = secondary;
        sourceSpell = source;

        remainingBounces = modifiers.bounce;
        remainingPassThrough = modifiers.passThrough;
        originPos = transform.position;

        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        if (modifiers.homing || modifiers.tracking)
            FindHomingTarget();

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (modifiers.boomerang && totalDistance > 0f)
        {
            float halfLife = lifetime * 0.5f;
            float elapsed = lifetime - GetRemainingLifetime();
            if (elapsed > halfLife && !returning)
            {
                direction = -direction;
                returning = true;
            }
        }

        if (modifiers.returning && !returning)
        {
            float elapsed = lifetime - GetRemainingLifetime();
            if (elapsed > lifetime * 0.5f)
                returning = true;
        }

        if (returning && modifiers.returning)
        {
            Vector2 toOrigin = (originPos - rb.position);
            if (toOrigin.sqrMagnitude > 0.01f)
                direction = toOrigin.normalized;
        }

        if (modifiers.homing && homingTarget != null)
        {
            Vector2 toTarget = ((Vector2)homingTarget.position - rb.position).normalized;
            direction = Vector2.Lerp(direction, toTarget,
                modifiers.homingStrength * Time.fixedDeltaTime).normalized;
        }

        if (modifiers.tracking && homingTarget != null)
        {
            Vector2 toTarget = ((Vector2)homingTarget.position - rb.position).normalized;
            float maxAngle = modifiers.trackingDegreesPerSecond * Time.fixedDeltaTime;
            float currentAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, maxAngle);
            direction = new Vector2(
                Mathf.Cos(newAngle * Mathf.Deg2Rad),
                Mathf.Sin(newAngle * Mathf.Deg2Rad));
        }

        Vector2 movement = direction * speed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);
        totalDistance += movement.magnitude;

        if (visual != null && visual.spinSpeed != 0f)
        {
            transform.Rotate(0, 0, visual.spinSpeed * Time.fixedDeltaTime);
        }
        else
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null)
            return;

        if (((1 << other.gameObject.layer) & hitMask.value) == 0)
            return;

        if (hitBehavior == ProjectileHitBehavior.Phase)
        {
            ApplyHit(other);
            return;
        }

        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();
        if (enemy != null && remainingPassThrough > 0)
        {
            ApplyHit(other);
            remainingPassThrough--;
            return;
        }

        if (remainingBounces > 0)
        {
            ApplyHit(other);
            remainingBounces--;

            ContactPoint2D[] contacts = new ContactPoint2D[1];
            if (other.GetContacts(contacts) > 0)
                direction = Vector2.Reflect(direction, contacts[0].normal);
            else
                direction = -direction;

            return;
        }

        ApplyHit(other);
        Destroy(gameObject);
    }

    private void ApplyHit(Collider2D other)
    {
        EnemyHealth enemyHp = other.GetComponentInParent<EnemyHealth>();
        if (enemyHp != null)
        {
            float falloff = CalculateFalloff();
            int finalDamage = Mathf.RoundToInt(damage * falloff);
            enemyHp.TakeDamage(finalDamage);
        }

        EffectReceiver receiver = other.GetComponentInParent<EffectReceiver>();
        if (receiver != null && onHitEffects != null)
        {
            for (int i = 0; i < onHitEffects.Count; i++)
            {
                if (onHitEffects[i] != null)
                    receiver.ApplyEffect(onHitEffects[i]);
            }
        }

        Vector2 hitPos = transform.position;
        Vector2 hitNormal = (transform.position - other.transform.position).normalized;
        if (hitNormal.sqrMagnitude < 0.001f)
            hitNormal = direction;

        SpawnOnHitSpell(secondarySpell, hitPos, hitNormal);

        if (sourceSpell != null && sourceSpell.multicastOnHit != null)
        {
            for (int i = 0; i < sourceSpell.multicastOnHit.Count; i++)
                SpawnOnHitSpell(sourceSpell.multicastOnHit[i], hitPos, hitNormal);
        }
    }

    private void SpawnOnHitSpell(SpellDefinition spell, Vector2 pos, Vector2 normal)
    {
        if (spell == null) return;

        int fires = Mathf.Max(1, spell.fireCount);
        float baseAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;

        SpawnOnHitSpellOnce(spell, pos, baseAngle);

        if (fires > 1)
        {
            float delay = Mathf.Max(0.05f, spell.fireDelay);
            GameObject spawner = new GameObject("OnHitSpawner");
            spawner.transform.position = pos;
            OnHitDelayedSpawner comp = spawner.AddComponent<OnHitDelayedSpawner>();
            comp.Begin(spell, pos, baseAngle, fires - 1, delay, hitMask);
        }
    }

    private void SpawnOnHitSpellOnce(SpellDefinition spell, Vector2 pos, float angle)
    {
        switch (spell.projectileType)
        {
            case CastProjectileType.Bomb:
                GameObject bombObj = new GameObject("SecondaryBomb");
                bombObj.transform.position = pos;
                SpellBomb bomb = bombObj.AddComponent<SpellBomb>();
                bomb.Initialize(spell, spell.bombDelay, spell.bombRadius, spell.damage,
                    spell.damageType, spell.onHitEffects,
                    spell.hitMask.value != 0 ? spell.hitMask : hitMask);
                break;

            case CastProjectileType.Circle:
                ApplyAoEAtPoint(spell, pos);
                break;

            case CastProjectileType.Zone:
                GameObject zoneObj = new GameObject("SecondaryZone");
                zoneObj.transform.position = pos;
                SpellZone zone = zoneObj.AddComponent<SpellZone>();
                zone.Initialize(spell, spell.bombRadius, spell.zoneDuration, spell.zoneTickRate,
                    spell.damage, spell.damageType, spell.onHitEffects,
                    spell.hitMask.value != 0 ? spell.hitMask : hitMask,
                    spell.modifiers.vortex, spell.modifiers.vortexStrength);
                break;

            default:
                SpawnSecondaryByPattern(spell, pos, angle);
                break;
        }
    }

    private void SpawnSecondaryByPattern(SpellDefinition spell, Vector2 pos, float normalAngle)
    {
        int count = Mathf.Max(1, spell.projectileCount);

        if (spell.projectileType == CastProjectileType.Cone ||
            (spell.projectileType == CastProjectileType.Bullet && spell.pattern == ProjectilePattern.Cone))
        {
            if (count == 1)
            {
                SpawnSecondaryProjectile(spell, pos, normalAngle);
                return;
            }
            float arc = spell.coneDegree;
            float start = normalAngle - arc * 0.5f;
            float step = arc / (count - 1);
            for (int i = 0; i < count; i++)
                SpawnSecondaryProjectile(spell, pos, start + step * i);
            return;
        }

        if (spell.projectileType == CastProjectileType.Bullet && spell.pattern == ProjectilePattern.Circle)
        {
            float step = 360f / count;
            for (int i = 0; i < count; i++)
                SpawnSecondaryProjectile(spell, pos, normalAngle + step * i);
            return;
        }

        SpawnSecondaryProjectile(spell, pos, normalAngle);
    }

    private void SpawnSecondaryProjectile(SpellDefinition spell, Vector2 pos, float angleDeg)
    {
        Vector2 dir = new Vector2(
            Mathf.Cos(angleDeg * Mathf.Deg2Rad),
            Mathf.Sin(angleDeg * Mathf.Deg2Rad));

        GameObject obj;
        if (spell.projectileVisual != null && spell.projectileVisual.sprite != null)
            obj = spell.projectileVisual.CreateInstance(pos, angleDeg);
        else if (spell.projectilePrefab != null)
            obj = Instantiate(spell.projectilePrefab, pos, Quaternion.Euler(0, 0, angleDeg));
        else if (spell.projectileVisual != null)
            obj = spell.projectileVisual.CreateInstance(pos, angleDeg);
        else
        {
            obj = new GameObject("SecondaryProjectile");
            obj.transform.position = pos;
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

            Rigidbody2D secondaryRb = obj.AddComponent<Rigidbody2D>();
            secondaryRb.gravityScale = 0f;
            secondaryRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        SpellProjectile proj = obj.GetComponent<SpellProjectile>();
        if (proj == null)
            proj = obj.AddComponent<SpellProjectile>();

        LayerMask mask = spell.hitMask.value != 0 ? spell.hitMask : hitMask;

        proj.Initialize(dir, spell.projectileSpeed,
            spell.range / Mathf.Max(0.01f, spell.projectileSpeed),
            spell.damage, spell.damageType, spell.hitBehavior,
            spell.modifiers, spell.onHitEffects, mask,
            spell.modifiers.secondaryEffect, spell.projectileVisual, spell);
    }

    private static readonly Collider2D[] aoeBuf = new Collider2D[32];

    private void ApplyAoEAtPoint(SpellDefinition spell, Vector2 pos)
    {
        LayerMask mask = spell.hitMask.value != 0 ? spell.hitMask : hitMask;
        int count = Physics2D.OverlapCircleNonAlloc(pos, spell.range, aoeBuf, mask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = aoeBuf[i];
            if (col == null) continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
                enemy.TakeDamage(spell.damage);

            EffectReceiver recv = col.GetComponentInParent<EffectReceiver>();
            if (recv != null && spell.onHitEffects != null)
            {
                for (int j = 0; j < spell.onHitEffects.Count; j++)
                {
                    if (spell.onHitEffects[j] != null)
                        recv.ApplyEffect(spell.onHitEffects[j]);
                }
            }
        }
    }

    private float CalculateFalloff()
    {
        if (totalDistance <= 0.01f)
            return 1f;

        float maxDist = lifetime * speed;
        if (maxDist <= 0f)
            return 1f;

        float t = Mathf.Clamp01(totalDistance / maxDist);
        return Mathf.Lerp(1f, 0f, t * (1f - 1f));
    }

    private void FindHomingTarget()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        float bestDist = float.MaxValue;

        for (int i = 0; i < enemies.Length; i++)
        {
            float dist = Vector2.Distance(transform.position, enemies[i].transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                homingTarget = enemies[i].transform;
            }
        }
    }

    private void OnDestroy()
    {
        if (visual != null)
            visual.SpawnImpactVfx(transform.position);
    }

    private float GetRemainingLifetime()
    {
        return Mathf.Max(0f, lifetime - totalDistance / Mathf.Max(0.01f, speed));
    }

    public bool IsReflectable => modifiers != null && modifiers.reflectable;

    public void Reflect(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude < 0.0001f)
            newDirection = -direction;

        direction = newDirection.normalized;
        remainingBounces = modifiers.bounce;
        totalDistance = 0f;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    private static readonly List<Collider2D> reflectBuf = new(16);

    public static int ReflectInArea(Vector2 center, float radius, Vector2 hitDirection)
    {
        int mask = 0;
        int l1 = LayerMask.NameToLayer("Projectile");
        int l2 = LayerMask.NameToLayer("PlayerProjectile");
        if (l1 >= 0) mask |= 1 << l1;
        if (l2 >= 0) mask |= 1 << l2;
        if (mask == 0) return 0;

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(mask);
        filter.useTriggers = true;

        reflectBuf.Clear();
        int count = Physics2D.OverlapCircle(center, radius, filter, reflectBuf);
        int reflected = 0;

        Debug.Log($"ReflectInArea: center={center} radius={radius} mask={mask} found={count}");

        for (int i = 0; i < count; i++)
        {
            if (reflectBuf[i] == null) continue;
            SpellProjectile proj = reflectBuf[i].GetComponent<SpellProjectile>();
            if (proj == null) proj = reflectBuf[i].GetComponentInParent<SpellProjectile>();
            Debug.Log($"  [{i}] obj={reflectBuf[i].gameObject.name} layer={reflectBuf[i].gameObject.layer} hasProj={proj != null} reflectable={proj?.IsReflectable}");
            if (proj != null && proj.IsReflectable)
            {
                proj.Reflect(hitDirection);
                reflected++;
            }
        }
        return reflected;
    }
}
