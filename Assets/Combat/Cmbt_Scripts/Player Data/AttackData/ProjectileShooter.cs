using UnityEngine;

public class ProjectileShooter : MonoBehaviour
{
    [SerializeField] private Transform muzzle;
    [SerializeField] private float muzzleForwardOffset = 0.15f;

    [Header("Spawn Safety")]
    [Tooltip("If true, projectile skills try to spawn at the first clear point in the aim direction instead of blindly spawning inside nearby wall/cover when the player is hugging geometry.")]
    [SerializeField] private bool useSafeProjectileSpawn = true;

    [Tooltip("Layers that count as solid spawn blockers for projectile skills. Set this to walls, cover, and generated obstacles only. Do not include Enemy, EnemyHurtbox, Player, PlayerHurtbox, Projectile, PlayerProjectile, or trigger-only utility layers.")]
    [SerializeField] private LayerMask spawnObstacleMask;

    [Tooltip("Small clearance circle used when testing whether the projectile spawn point is inside wall/cover. v4 also compares this with the prefab collider size and uses whichever is larger.")]
    [SerializeField, Min(0.01f)] private float spawnClearanceRadius = 0.14f;

    [Tooltip("How far past the normal muzzle offset the shooter may search for a clear spawn point. If no clear point is found, the projectile uses the original spawn and will correctly hit the wall.")]
    [SerializeField, Min(0f)] private float safeSpawnSearchDistance = 0.85f;

    [Tooltip("Step size used when searching forward for a clear projectile spawn point.")]
    [SerializeField, Min(0.01f)] private float safeSpawnStep = 0.04f;

    [Tooltip("If true, the spawn search also checks whether there is a real wall/cover face directly in the aim direction. v4 uses a narrow forward check and ignores side-wall contact, so wall-hugging beside a wall no longer blocks open shots.")]
    [SerializeField] private bool requireClearPathToSpawn = true;

    [Header("Spawn Safety v4 - Wall Hug Tolerance")]
    [Tooltip("The path check uses this fraction of the projectile clearance radius. Lower values ignore harmless side-wall brushing while still catching a wall directly in the aim direction.")]
    [SerializeField, Range(0.05f, 1f)] private float forwardPathRadiusMultiplier = 0.22f;

    [Tooltip("Minimum radius for the narrow forward path check.")]
    [SerializeField, Min(0.001f)] private float minimumForwardPathRadius = 0.015f;

    [Tooltip("Small distance to skip at the muzzle so a wall beside the player's body does not count as a direct forward blocker.")]
    [SerializeField, Min(0f)] private float forwardPathStartNudge = 0.035f;

    [Tooltip("A path hit only blocks the spawn if the surface normal faces opposite the shot direction by at least this much. 0.45-0.65 is usually good. This lets open shots slide out from beside a wall while still blocking shots aimed into the wall.")]
    [SerializeField, Range(0f, 1f)] private float forwardBlockNormalDot = 0.50f;

    [Header("Projectile Clip Protection v5")]
    [Tooltip("Arms the spawned PlayerProjectile to ignore only first-frame side/back wall clips. This is what fixes wall-hug shots after spawn placement has moved the projectile forward.")]
    [SerializeField] private bool armProjectileSpawnClipProtection = true;

    [Tooltip("How far the projectile can travel while ignoring side/back wall clips caused by spawning out of hug geometry.")]
    [SerializeField, Min(0f)] private float projectileClipProtectionTravelDistance = 0.60f;

    [Tooltip("If an obstacle is this aligned with the shot direction, it is a real forward wall hit and will not be ignored.")]
    [SerializeField, Range(-1f, 1f)] private float projectileClipForwardDot = 0.55f;

    [Tooltip("Obstacle contacts closer than this forward distance from the muzzle are considered side/back spawn clips.")]
    [SerializeField, Min(0f)] private float projectileClipMinimumForwardDistance = 0.06f;

    [SerializeField] private bool logSafeSpawn = false;

    private readonly RaycastHit2D[] forwardPathHits = new RaycastHit2D[12];

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
        float effectiveRadius = GetEffectiveSpawnClearanceRadius(prefab);

        Vector3 originalSpawnPos =
            muzzle.position +
            (Vector3)(dir * muzzleForwardOffset);

        Vector3 spawnPos = GetSafeSpawnPosition(
            trueMuzzlePosition,
            dir,
            originalSpawnPos,
            effectiveRadius
        );

        float angle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg;

        PlayerProjectile proj = Instantiate(
            prefab,
            spawnPos,
            Quaternion.Euler(0f, 0f, angle)
        );

        // Preserve the true muzzle location for Power Shot close-range aim assist.
        proj.SetLaunchOrigin(trueMuzzlePosition);

        // This is opt-in and local to projectiles spawned by ProjectileShooter.
        // Normal basics such as Phil's direct PlayerProjectile usage stay unaffected.
        if (armProjectileSpawnClipProtection &&
            useSafeProjectileSpawn &&
            spawnObstacleMask.value != 0)
        {
            float travelWindow = Mathf.Max(
                projectileClipProtectionTravelDistance,
                Vector2.Distance(trueMuzzlePosition, spawnPos) + effectiveRadius
            );

            proj.ArmSpawnObstacleClipProtection(
                spawnObstacleMask,
                trueMuzzlePosition,
                dir,
                travelWindow,
                projectileClipForwardDot,
                projectileClipMinimumForwardDistance
            );
        }

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

    private Vector3 GetSafeSpawnPosition(
        Vector2 launchOrigin,
        Vector2 direction,
        Vector3 originalSpawnPos,
        float radius)
    {
        if (!useSafeProjectileSpawn || spawnObstacleMask.value == 0)
            return originalSpawnPos;

        radius = Mathf.Max(0.01f, radius);

        if (IsSpawnClear(launchOrigin, originalSpawnPos, direction, radius))
            return originalSpawnPos;

        float startDistance = Mathf.Max(0f, muzzleForwardOffset);
        float maxDistance = startDistance + Mathf.Max(0f, safeSpawnSearchDistance);
        float step = Mathf.Max(0.01f, safeSpawnStep);

        for (float d = startDistance + step;
             d <= maxDistance + 0.0001f;
             d += step)
        {
            Vector3 candidate =
                (Vector3)launchOrigin +
                (Vector3)(direction * d);

            if (!IsSpawnClear(launchOrigin, candidate, direction, radius))
                continue;

            if (logSafeSpawn)
            {
                Debug.Log(
                    $"ProjectileShooter: shifted projectile spawn from " +
                    $"{originalSpawnPos} to {candidate}. " +
                    $"Radius={radius:0.00}",
                    this
                );
            }

            return candidate;
        }

        if (logSafeSpawn)
        {
            Debug.Log(
                "ProjectileShooter: spawn was blocked and no safe forward " +
                "spawn point was found. Using original spawn so the shot can " +
                "correctly collide with the wall/cover.",
                this
            );
        }

        return originalSpawnPos;
    }

    private bool IsSpawnClear(
        Vector2 launchOrigin,
        Vector2 candidate,
        Vector2 direction,
        float radius)
    {
        if (IsSpawnBlocked(candidate, radius))
            return false;

        if (!requireClearPathToSpawn)
            return true;

        return !HasDirectForwardBlocker(
            launchOrigin,
            candidate,
            direction,
            radius
        );
    }

    private bool IsSpawnBlocked(Vector2 position, float radius)
    {
        return Physics2D.OverlapCircle(
            position,
            radius,
            spawnObstacleMask
        ) != null;
    }

    private bool HasDirectForwardBlocker(
        Vector2 launchOrigin,
        Vector2 candidate,
        Vector2 direction,
        float projectileRadius)
    {
        Vector2 delta = candidate - launchOrigin;
        float fullDistance = delta.magnitude;

        if (fullDistance <= 0.001f)
            return false;

        direction = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : delta / fullDistance;

        float nudge = Mathf.Clamp(
            forwardPathStartNudge,
            0f,
            Mathf.Max(0f, fullDistance - 0.001f)
        );

        Vector2 start = launchOrigin + direction * nudge;
        float distance = Mathf.Max(0f, fullDistance - nudge);

        if (distance <= 0.001f)
            return false;

        float pathRadius = Mathf.Max(
            minimumForwardPathRadius,
            projectileRadius * Mathf.Clamp01(forwardPathRadiusMultiplier)
        );

        int hitCount = Physics2D.CircleCastNonAlloc(
            start,
            pathRadius,
            direction,
            forwardPathHits,
            distance,
            spawnObstacleMask
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = forwardPathHits[i];

            if (hit.collider == null)
                continue;

            // A wall beside the player often appears as a zero-distance side hit
            // while hugging geometry. Only treat it as a blocker if its surface
            // is actually facing opposite the shot direction.
            float facingDot = Vector2.Dot(hit.normal, -direction);

            if (facingDot >= forwardBlockNormalDot)
            {
                if (logSafeSpawn)
                {
                    Debug.Log(
                        $"ProjectileShooter: direct forward blocker " +
                        $"'{hit.collider.name}' at {hit.distance:0.00}. " +
                        $"NormalDot={facingDot:0.00}",
                        this
                    );
                }

                return true;
            }

            if (logSafeSpawn)
            {
                Debug.Log(
                    $"ProjectileShooter: ignored side spawn path contact " +
                    $"'{hit.collider.name}'. NormalDot={facingDot:0.00}",
                    this
                );
            }
        }

        return false;
    }

    private float GetEffectiveSpawnClearanceRadius(PlayerProjectile prefab)
    {
        float radius = Mathf.Max(0.01f, spawnClearanceRadius);

        if (prefab == null)
            return radius;

        CircleCollider2D circle = prefab.GetComponent<CircleCollider2D>();

        if (circle != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(prefab.transform.localScale.x),
                Mathf.Abs(prefab.transform.localScale.y),
                0.01f
            );

            radius = Mathf.Max(
                radius,
                Mathf.Max(0.01f, circle.radius * scale)
            );
        }

        CapsuleCollider2D capsule = prefab.GetComponent<CapsuleCollider2D>();

        if (capsule != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(prefab.transform.localScale.x),
                Mathf.Abs(prefab.transform.localScale.y),
                0.01f
            );

            radius = Mathf.Max(
                radius,
                Mathf.Max(capsule.size.x, capsule.size.y) * 0.5f * scale
            );
        }

        BoxCollider2D box = prefab.GetComponent<BoxCollider2D>();

        if (box != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(prefab.transform.localScale.x),
                Mathf.Abs(prefab.transform.localScale.y),
                0.01f
            );

            radius = Mathf.Max(
                radius,
                box.size.magnitude * 0.5f * scale
            );
        }

        return radius;
    }
}
