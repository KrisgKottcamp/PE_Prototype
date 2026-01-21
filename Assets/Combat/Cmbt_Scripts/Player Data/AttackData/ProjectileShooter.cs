using UnityEngine;

public class ProjectileShooter : MonoBehaviour
{
    [SerializeField] private Transform muzzle; // defaults to transform
    [SerializeField] private float muzzleForwardOffset = 0.15f;

    private void Awake()
    {
        if (muzzle == null) muzzle = transform;
    }

    public PlayerProjectile Fire(
        PlayerProjectile prefab,
        Vector2 dir,
        int ownerIndex,
        int damage,
        float stunSeconds,
        float speed,
        float lifetime,
        LayerMask hitMask,
        bool awardApOnHit)
    {
        if (prefab == null) return null;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir = dir.normalized;

        Vector3 spawnPos = muzzle.position + (Vector3)(dir * muzzleForwardOffset);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        var proj = Instantiate(prefab, spawnPos, Quaternion.Euler(0f, 0f, angle));
        proj.Fire(dir, ownerIndex, damage, stunSeconds, speed, lifetime, hitMask, awardApOnHit);
        return proj;
    }
}
