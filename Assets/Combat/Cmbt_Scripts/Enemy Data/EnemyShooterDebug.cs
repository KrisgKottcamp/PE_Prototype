using System.Collections.Generic;
using UnityEngine;

public class EnemyShooterDebug : MonoBehaviour
{
    public enum PatternType
    {
        AimedSingle,
        AimedFan,
        Ring,
        Spiral,
        BoI_4Way,
        BoI_8Way,
        SweepFan
    }

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float projectileSpeed = 7f;
    [SerializeField] private bool applyVelocityToRigidbody = true;

    [Header("Burst Fire")]
    [SerializeField] private bool useBurstFire = true;
    [SerializeField] private int shotsPerBurst = 3;              // number of pattern "ticks" per burst
    [SerializeField] private float intraBurstInterval = 0.10f;   // time between pattern ticks
    [SerializeField] private float burstCooldown = 0.90f;        // time between bursts
    [SerializeField] private float losRetryDelay = 0.06f;

    [Header("Burst Quota Per Enable")]
    [SerializeField] private bool limitBurstsPerEnable = true;
    [SerializeField] private int burstsPerEnable = 1;

    [Header("Aim")]
    [SerializeField] private float aimLagSeconds = 0.07f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float maxRange = 30f;

    [Header("Target")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private string playerTag = "PlayerCombatPawn";
    [SerializeField] private Transform target;

    [Header("Pattern")]
    [SerializeField] private PatternType pattern = PatternType.AimedFan;

    [Tooltip("Used by AimedFan and SweepFan")]
    [SerializeField] private int fanBullets = 5;

    [Tooltip("Degrees total cone width for AimedFan")]
    [SerializeField] private float fanArcDegrees = 40f;

    [Tooltip("Used by Ring")]
    [SerializeField] private int ringBullets = 12;

    [Tooltip("Degrees per tick for Spiral and SweepFan")]
    [SerializeField] private float angularSpeedDegPerTick = 12f;

    [Tooltip("For Spiral: bullets per tick (usually 1-3)")]
    [SerializeField] private int spiralBulletsPerTick = 2;

    [Tooltip("If true, Spiral is centered on aim direction. If false, it rotates globally.")]
    [SerializeField] private bool spiralCenteredOnAim = true;

    [Header("Runtime")]
    [SerializeField] private bool shootingEnabled = true;
    [SerializeField] private string lastBlockReason = "None";
    [SerializeField] private string cooldownSetBy = "None";

    private struct TargetSample { public float time; public Vector2 pos; }
    private readonly List<TargetSample> samples = new List<TargetSample>(64);

    private float nextFireTime = 0f;
    private float nextTargetSearchTime = 0f;

    private int burstShotsRemaining = 0;
    private int burstsFiredThisEnable = 0;

    private float spiralAngleDeg = 0f;
    private float sweepAngleDeg = 0f;
    private int sweepDir = 1;

    private const float TargetSearchInterval = 0.20f;
    private const float MinInterval = 0.03f;

    public Transform CurrentTarget => target;
    public string LastBlockReason => lastBlockReason;

    private void OnEnable()
    {
        samples.Clear();
        ResetBurstStateForEnable();
        ScheduleNextFire(0f, "OnEnable");
    }

    private void Update()
    {
        ResolveTarget();
        RecordTargetSample();

        if (!shootingEnabled) { lastBlockReason = "ShootingDisabled"; return; }
        if (projectilePrefab == null) { lastBlockReason = "NoProjectilePrefab"; return; }

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

        if (dist < 0.001f) { lastBlockReason = "AimTooClose"; ScheduleNextFire(0.04f, "RetryAimTooClose"); return; }
        if (dist > maxRange) { lastBlockReason = "OutOfRange"; ScheduleNextFire(0.06f, "RetryOutOfRange"); return; }

        // LOS check should be against current target position, not lagged aim
        if (requireLineOfSight && target != null)
        {
            if (IsLineBlocked(origin, target.position))
            {
                lastBlockReason = "LOSBlocked";
                ScheduleNextFire(losRetryDelay, "LOSRetry");
                return;
            }
        }

        Vector2 baseDir = toAim / dist;

        if (useBurstFire) FireBurstStep(origin, baseDir);
        else FireSingleTick(origin, baseDir);
    }

    // Called by EnemyBrain
    public void SetShootingEnabled(bool enabled)
    {
        if (enabled == shootingEnabled) return;
        shootingEnabled = enabled;

        if (shootingEnabled)
        {
            ResetBurstStateForEnable();
            ScheduleNextFire(0.03f, "EnableRisingEdge");
        }
        else
        {
            lastBlockReason = "ShootingDisabled";
        }
    }

    // Kept for compatibility with your brain tuning
    public void SetFireInterval(float value)
    {
        // We map this to intraBurstInterval for burst mode, otherwise itÅfs unused here.
        intraBurstInterval = Mathf.Max(MinInterval, value);
    }

    public void SetAimLag(float value)
    {
        aimLagSeconds = Mathf.Max(0f, value);
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

    // Optional: allow AI or EnemyDefinition to set pattern
    public void SetPattern(PatternType newPattern)
    {
        pattern = newPattern;
    }

    private void FireSingleTick(Vector2 origin, Vector2 baseDir)
    {
        EmitPattern(origin, baseDir);
        ScheduleNextFire(Mathf.Max(MinInterval, intraBurstInterval), "SingleTick");
    }

    private void FireBurstStep(Vector2 origin, Vector2 baseDir)
    {
        if (burstShotsRemaining <= 0)
            burstShotsRemaining = Mathf.Max(1, shotsPerBurst);

        EmitPattern(origin, baseDir);
        burstShotsRemaining--;

        if (burstShotsRemaining > 0)
        {
            ScheduleNextFire(Mathf.Max(MinInterval, intraBurstInterval), "IntraBurst");
        }
        else
        {
            burstsFiredThisEnable++;
            ScheduleNextFire(Mathf.Max(MinInterval, burstCooldown), "BurstCooldown");
        }
    }

    private void EmitPattern(Vector2 origin, Vector2 baseDir)
    {
        lastBlockReason = $"Pattern:{pattern}";

        switch (pattern)
        {
            case PatternType.AimedSingle:
                SpawnProjectile(origin, baseDir);
                break;

            case PatternType.AimedFan:
                EmitFan(origin, baseDir, Mathf.Max(1, fanBullets), fanArcDegrees);
                break;

            case PatternType.Ring:
                EmitRing(origin, Mathf.Max(3, ringBullets), baseDir);
                break;

            case PatternType.Spiral:
                EmitSpiral(origin, baseDir);
                break;

            case PatternType.BoI_4Way:
                EmitFixedWays(origin, 4);
                break;

            case PatternType.BoI_8Way:
                EmitFixedWays(origin, 8);
                break;

            case PatternType.SweepFan:
                EmitSweepFan(origin, baseDir);
                break;
        }
    }

    private void EmitFan(Vector2 origin, Vector2 baseDir, int count, float arcDeg)
    {
        if (count == 1)
        {
            SpawnProjectile(origin, baseDir);
            return;
        }

        float half = arcDeg * 0.5f;
        for (int i = 0; i < count; i++)
        {
            float t = (count == 1) ? 0.5f : (float)i / (count - 1);
            float ang = Mathf.Lerp(-half, half, t);
            Vector2 dir = Rotate(baseDir, ang);
            SpawnProjectile(origin, dir);
        }
    }

    private void EmitRing(Vector2 origin, int count, Vector2 baseDir)
    {
        // baseDir used to offset ring so patterns can ÅgfaceÅh the player
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < count; i++)
        {
            float ang = baseAngle + (360f * i / count);
            Vector2 dir = AngleToDir(ang);
            SpawnProjectile(origin, dir);
        }
    }

    private void EmitSpiral(Vector2 origin, Vector2 baseDir)
    {
        float baseAngle = spiralCenteredOnAim ? Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg : 0f;
        spiralAngleDeg += angularSpeedDegPerTick;

        int n = Mathf.Max(1, spiralBulletsPerTick);
        for (int i = 0; i < n; i++)
        {
            float step = (n == 1) ? 0f : (360f / n) * i;
            float ang = baseAngle + spiralAngleDeg + step;
            SpawnProjectile(origin, AngleToDir(ang));
        }
    }

    private void EmitFixedWays(Vector2 origin, int ways)
    {
        int n = Mathf.Max(2, ways);
        for (int i = 0; i < n; i++)
        {
            float ang = 360f * i / n;
            SpawnProjectile(origin, AngleToDir(ang));
        }
    }

    private void EmitSweepFan(Vector2 origin, Vector2 baseDir)
    {
        // Sweep angle bounces back and forth
        sweepAngleDeg += angularSpeedDegPerTick * sweepDir;

        float maxSweep = Mathf.Max(5f, fanArcDegrees);
        if (sweepAngleDeg > maxSweep * 0.5f) { sweepAngleDeg = maxSweep * 0.5f; sweepDir = -1; }
        if (sweepAngleDeg < -maxSweep * 0.5f) { sweepAngleDeg = -maxSweep * 0.5f; sweepDir = 1; }

        Vector2 centerDir = Rotate(baseDir, sweepAngleDeg);
        EmitFan(origin, centerDir, Mathf.Max(1, fanBullets), fanArcDegrees);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir)
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

        // Initialize projectile movement if your Projectile supports it
        if (spawned.TryGetComponent<Projectile>(out Projectile p))
            p.Initialize(dir, projectileSpeed);

        // Always also set RB velocity as fallback
        if (applyVelocityToRigidbody && spawned.TryGetComponent<Rigidbody2D>(out Rigidbody2D prb))
        {
#if UNITY_6000_0_OR_NEWER
            prb.linearVelocity = dir * projectileSpeed;
#else
            prb.velocity = dir * projectileSpeed;
#endif
        }
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
        try { playerObj = GameObject.FindWithTag(playerTag); }
        catch (UnityException) { lastBlockReason = $"MissingTag:{playerTag}"; return; }

        if (playerObj != null) { target = playerObj.transform; lastBlockReason = "TargetAcquired"; }
    }

    private void RecordTargetSample()
    {
        if (target == null) return;

        samples.Add(new TargetSample { time = Time.time, pos = target.position });

        float keepFrom = Time.time - Mathf.Max(aimLagSeconds + 0.75f, 1.0f);
        int removeCount = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].time < keepFrom) removeCount++;
            else break;
        }
        if (removeCount > 0) samples.RemoveRange(0, removeCount);
    }

    private bool TryGetDelayedAimPoint(out Vector2 aimPoint)
    {
        aimPoint = Vector2.zero;
        if (target == null) return false;

        if (samples.Count == 0) { aimPoint = target.position; return true; }

        float t = Time.time - aimLagSeconds;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            if (samples[i].time <= t) { aimPoint = samples[i].pos; return true; }
        }

        aimPoint = samples[0].pos;
        return true;
    }

    private bool IsLineBlocked(Vector2 origin, Vector2 point)
    {
        RaycastHit2D[] hits = Physics2D.LinecastAll(origin, point, obstacleMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == null) continue;
            if (c.transform.root == transform.root) continue;
            return true;
        }
        return false;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cs = Mathf.Cos(rad);
        float sn = Mathf.Sin(rad);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs).normalized;
    }

    private static Vector2 AngleToDir(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
    }
}
