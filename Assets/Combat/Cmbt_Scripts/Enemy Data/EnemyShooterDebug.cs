using System.Collections.Generic;
using UnityEngine;

public class EnemyShooterDebug : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float projectileSpeed = 7f;
    [SerializeField] private bool applyVelocityToRigidbody = true;

    [Header("Legacy Continuous Fire")]
    [SerializeField] private float fireInterval = 0.30f; // used only when burst mode is OFF
    [SerializeField] private float initialFireDelay = 0f;
    [SerializeField] private float enableWakeDelay = 0.03f;

    [Header("Burst Fire")]
    [SerializeField] private bool useBurstFire = true;
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float intraBurstInterval = 0.10f;
    [SerializeField] private float burstCooldown = 0.90f;
    [SerializeField] private float losRetryDelay = 0.06f;

    [Header("Burst Quota Per Enable")]
    [SerializeField] private bool limitBurstsPerEnable = true;
    [SerializeField] private int burstsPerEnable = 1; // 1 = one burst each time brain enables shooting

    [Header("Aim")]
    [SerializeField] private float aimLagSeconds = 0.07f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float maxRange = 30f;

    [Header("Target")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";
    [SerializeField] private Transform target;

    [Header("Runtime")]
    [SerializeField] private bool shootingEnabled = true;
    [SerializeField] private string lastBlockReason = "None";
    [SerializeField] private string cooldownSetBy = "None";
    [SerializeField] private float nextFireIn = 0f;
    [SerializeField] private int burstShotsRemaining = 0;
    [SerializeField] private int burstsFiredThisEnable = 0;

    private struct TargetSample
    {
        public float time;
        public Vector2 pos;
    }

    private readonly List<TargetSample> samples = new List<TargetSample>(64);

    private float nextFireTime = 0f;
    private float nextTargetSearchTime = 0f;

    private const float TargetSearchInterval = 0.20f;
    private const float MinFireInterval = 0.03f;

    public float FireInterval => fireInterval;
    public float AimLagSeconds => aimLagSeconds;
    public bool ShootingEnabled => shootingEnabled;
    public Transform CurrentTarget => target;
    public string LastBlockReason => lastBlockReason;
    public string CooldownSetBy => cooldownSetBy;

    private void OnEnable()
    {
        samples.Clear();
        ResetBurstStateForEnable();
        ScheduleNextFire(initialFireDelay, "OnEnable");
        lastBlockReason = "Enabled";
    }

    private void Update()
    {
        ResolveTarget();
        RecordTargetSample();

        nextFireIn = Mathf.Max(0f, nextFireTime - Time.time);

        if (!shootingEnabled)
        {
            lastBlockReason = "ShootingDisabled";
            return;
        }

        if (projectilePrefab == null)
        {
            lastBlockReason = "NoProjectilePrefab";
            return;
        }

        if (Time.time < nextFireTime)
        {
            lastBlockReason = $"Cooldown({cooldownSetBy})";
            return;
        }

        if (limitBurstsPerEnable && useBurstFire && burstsPerEnable > 0 && burstsFiredThisEnable >= burstsPerEnable)
        {
            lastBlockReason = "BurstQuotaReached";
            return;
        }

        if (!TryGetDelayedAimPoint(out Vector2 aimPoint))
        {
            lastBlockReason = "NoTargetOrAimPoint";
            ScheduleNextFire(0.04f, "RetryNoTarget");
            return;
        }

        Vector2 origin = muzzle != null ? (Vector2)muzzle.position : (Vector2)transform.position;
        Vector2 toAim = aimPoint - origin;
        float dist = toAim.magnitude;

        if (dist < 0.001f)
        {
            lastBlockReason = "AimTooClose";
            ScheduleNextFire(0.04f, "RetryAimTooClose");
            return;
        }

        if (dist > maxRange)
        {
            lastBlockReason = "OutOfRange";
            ScheduleNextFire(0.05f, "RetryOutOfRange");
            return;
        }

        if (requireLineOfSight && IsLineBlocked(origin, aimPoint))
        {
            lastBlockReason = "LOSBlocked";
            ScheduleNextFire(losRetryDelay, "LOSRetry");
            return;
        }

        Vector2 dir = toAim / dist;

        if (useBurstFire)
            FireBurstStep(origin, dir);
        else
            FireContinuousStep(origin, dir);
    }

    private void FireContinuousStep(Vector2 origin, Vector2 dir)
    {
        Fire(origin, dir);
        lastBlockReason = "FiredContinuous";
        ScheduleNextFire(Mathf.Max(MinFireInterval, fireInterval), "ShotFiredContinuous");
    }

    private void FireBurstStep(Vector2 origin, Vector2 dir)
    {
        if (burstShotsRemaining <= 0)
            burstShotsRemaining = Mathf.Max(1, shotsPerBurst);

        Fire(origin, dir);
        burstShotsRemaining--;

        int total = Mathf.Max(1, shotsPerBurst);
        int firedInCurrentBurst = total - burstShotsRemaining;
        lastBlockReason = $"FiredBurstShot {firedInCurrentBurst}/{total}";

        if (burstShotsRemaining > 0)
        {
            ScheduleNextFire(Mathf.Max(MinFireInterval, intraBurstInterval), "IntraBurst");
        }
        else
        {
            burstsFiredThisEnable++;
            ScheduleNextFire(Mathf.Max(MinFireInterval, burstCooldown), "BurstCooldown");
        }
    }

    public void SetFireInterval(float value)
    {
        fireInterval = Mathf.Max(MinFireInterval, value);
    }

    public void SetAimLag(float value)
    {
        aimLagSeconds = Mathf.Max(0f, value);
    }

    public void SetShootingEnabled(bool enabled)
    {
        if (enabled == shootingEnabled) return;

        shootingEnabled = enabled;

        if (shootingEnabled)
        {
            ResetBurstStateForEnable();
            ScheduleNextFire(enableWakeDelay, "EnableRisingEdge");
        }
        else
        {
            lastBlockReason = "ShootingDisabled";
        }
    }

    public void ForceReadyToFire(float delay = 0f)
    {
        ScheduleNextFire(delay, "ForceReadyToFire");
    }

    public void SetTarget(Transform newTarget)
    {
        if (target == newTarget) return;
        target = newTarget;
        samples.Clear();
    }

    public void SetBurstConfig(int newShotsPerBurst, float newIntraBurstInterval, float newBurstCooldown)
    {
        shotsPerBurst = Mathf.Max(1, newShotsPerBurst);
        intraBurstInterval = Mathf.Max(MinFireInterval, newIntraBurstInterval);
        burstCooldown = Mathf.Max(MinFireInterval, newBurstCooldown);
    }

    public void SetBurstQuotaPerEnable(bool enabled, int quota)
    {
        limitBurstsPerEnable = enabled;
        burstsPerEnable = Mathf.Max(1, quota);
    }

    private void ResetBurstStateForEnable()
    {
        burstShotsRemaining = 0;
        burstsFiredThisEnable = 0;
    }

    private void ScheduleNextFire(float delay, string reason)
    {
        nextFireTime = Time.time + Mathf.Max(0f, delay);
        cooldownSetBy = reason;
    }

    private void ResolveTarget()
    {
        if (target != null) return;
        if (!autoFindPlayer) return;
        if (Time.time < nextTargetSearchTime) return;

        nextTargetSearchTime = Time.time + TargetSearchInterval;

        GameObject playerObj = null;
        try
        {
            playerObj = GameObject.FindWithTag(playerTag);
        }
        catch (UnityException)
        {
            lastBlockReason = $"MissingTag:{playerTag}";
            return;
        }

        if (playerObj != null)
        {
            target = playerObj.transform;
            lastBlockReason = "TargetAcquired";
        }
    }

    private void RecordTargetSample()
    {
        if (target == null) return;

        samples.Add(new TargetSample
        {
            time = Time.time,
            pos = target.position
        });

        float keepFrom = Time.time - Mathf.Max(aimLagSeconds + 0.75f, 1.0f);
        int removeCount = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].time < keepFrom) removeCount++;
            else break;
        }

        if (removeCount > 0)
            samples.RemoveRange(0, removeCount);
    }

    private bool TryGetDelayedAimPoint(out Vector2 aimPoint)
    {
        aimPoint = Vector2.zero;
        if (target == null) return false;

        if (samples.Count == 0)
        {
            aimPoint = target.position;
            return true;
        }

        float t = Time.time - aimLagSeconds;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            if (samples[i].time <= t)
            {
                aimPoint = samples[i].pos;
                return true;
            }
        }

        aimPoint = samples[0].pos;
        return true;
    }

    private bool IsLineBlocked(Vector2 origin, Vector2 aimPoint)
    {
        RaycastHit2D[] hits = Physics2D.LinecastAll(origin, aimPoint, obstacleMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null) continue;

            // Ignore own body colliders
            if (c.transform.root == transform.root) continue;

            return true;
        }
        return false;
    }

    private void Fire(Vector2 origin, Vector2 dir)
    {
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

        Vector2 spawnPos = origin + dir * 0.20f;
        Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir);

        GameObject spawned = Instantiate(projectilePrefab, spawnPos, rot);

        // Ignore owner collision
        Collider2D[] ownerCols = GetComponentsInChildren<Collider2D>(true);
        Collider2D[] projCols = spawned.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerCols.Length; i++)
        {
            for (int j = 0; j < projCols.Length; j++)
            {
                if (ownerCols[i] != null && projCols[j] != null)
                    Physics2D.IgnoreCollision(ownerCols[i], projCols[j], true);
            }
        }

        // IMPORTANT: initialize projectile script first (if present)
        if (spawned.TryGetComponent<Projectile>(out Projectile p))
        {
            p.Initialize(dir, projectileSpeed);
        }

        // Fallback: also set Rigidbody velocity
        if (spawned.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = dir * projectileSpeed;
#else
        rb.velocity = dir * projectileSpeed;
#endif
        }
    }

}
