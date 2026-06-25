using UnityEngine;

public class ProjectileShooter : MonoBehaviour
{
    [SerializeField] private Transform muzzle;
    [SerializeField] private float muzzleForwardOffset = 0.15f;

    private void Awake()
    {
        if (muzzle == null)
            muzzle = transform;
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
        bool awardApOnHit,
        float momentumGain = 0f,
        bool startActiveScoringOnHit = false)
    {
        if (prefab == null)
            return null;

        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.up;

        dir = dir.normalized;

        Vector2 trueMuzzlePosition = muzzle.position;
        Vector3 spawnPos = muzzle.position + (Vector3)(dir * muzzleForwardOffset);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        PlayerProjectile proj = Instantiate(
            prefab,
            spawnPos,
            Quaternion.Euler(0f, 0f, angle)
        );

        // Record the true muzzle location before Fire() checks close-range intent.
        proj.SetLaunchOrigin(trueMuzzlePosition);

        proj.Fire(
            dir,
            ownerIndex,
            damage,
            stunSeconds,
            speed,
            lifetime,
            hitMask,
            awardApOnHit,
            momentumGain,
            startActiveScoringOnHit
        );

        return proj;
    }
}
