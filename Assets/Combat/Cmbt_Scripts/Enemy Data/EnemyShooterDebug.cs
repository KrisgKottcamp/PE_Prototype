using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyShooterDebug : MonoBehaviour
{
    [System.Serializable]
    private class PatternTelegraph
    {
        public PatternType pattern;
        public EnemyTelegraphProfile profile;
    }

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
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float intraBurstInterval = 0.10f;
    [SerializeField] private float burstCooldown = 0.90f;
    [SerializeField] private float losRetryDelay = 0.06f;

    [Header("Burst Quota Per Enable")]
    [SerializeField] private bool limitBurstsPerEnable = true;
    [SerializeField] private int burstsPerEnable = 1;

    [Header("Aim")]
    [SerializeField] private float aimLagSeconds = 0.07f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float maxRange = 30f;
    [Tooltip("When firing a fan pattern, always include one extra bullet aimed directly at the " +
             "target's current real position (bypasses aim lag). Eliminates gaps in fan dead zones.")]
    [SerializeField] private bool guaranteeCenterBullet = true;

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

    [Header("Attack Telegraph")]
    [SerializeField] private EnemyAttackTelegraph attackTelegraph;
    [SerializeField] private PatternTelegraph[] patternTelegraphs;
    [SerializeField] private EnemyTelegraphProfile fallbackTelegraphProfile;
    [SerializeField] private bool lockAimDuringTelegraph = true;

    [Header("Runtime")]
    [SerializeField] private bool shootingEnabled = false;
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
    private bool isTelegraphing = false;

    private const float TargetSearchInterval = 0.20f;
    private const float MinInterval = 0.03f;

    public Transform CurrentTarget => target;
    public string LastBlockReason => lastBlockReason;

    private void OnEnable()
    {
        samples.Clear();
        ResetBurstStateForEnable();

        // Enemies should not fire just because the prefab spawned.
        // EnemyBrain opens this gate only after the enemy reaches / settles into
        // its tactical behavior and enters an attack window.
        shootingEnabled = false;
        lastBlockReason = "SpawnLocked";
        ScheduleNextFire(999f, "SpawnLocked");
    }

    private void Update()
    {
        ResolveTarget();
        RecordTargetSample();

        if (!shootingEnabled) { lastBlockReason = "ShootingDisabled"; return; }
        if (isTelegraphing) { lastBlockReason = "Telegraphing"; return; }
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

        StartCoroutine(
            TelegraphAndFireRoutine(origin, baseDir, useBurstFire)
        );
    }

    public void SetShootingEnabled(bool enabled)
    {
        if (enabled == shootingEnabled) return;

        shootingEnabled = enabled;

        if (shootingEnabled)
        {
            ResetBurstStateForEnable();
            ScheduleNextFire(0.03f, "EnableRisingEdge");
            lastBlockReason = "ShooterEnabled";
        }
        else
        {
            ResetBurstStateForEnable();
            isTelegraphing = false;
            if (attackTelegraph != null) attackTelegraph.CancelTelegraph();
            ScheduleNextFire(999f, "ShootingDisabled");
            lastBlockReason = "ShootingDisabled";
        }
    }

    public void SetFireInterval(float value)
    {
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

    public void SetPattern(PatternType newPattern)
    {
        pattern = newPattern;
    }

    private IEnumerator TelegraphAndFireRoutine(Vector2 origin, Vector2 initialDirection, bool fireAsBurstStep)
    {
        if (isTelegraphing) yield break;
        isTelegraphing = true;

        EnemyTelegraphProfile profile = GetTelegraphProfile(pattern);
        Vector2 lockedDirection = initialDirection.sqrMagnitude > 0.0001f ? initialDirection.normalized : Vector2.right;

        if (attackTelegraph != null && profile != null)
            yield return attackTelegraph.PlayTelegraphRoutine(profile, lockedDirection);
        else if (profile != null)
            yield return new WaitForSeconds(Mathf.Max(0.01f, profile.duration));

        if (!shootingEnabled) { isTelegraphing = false; yield break; }

        Vector2 fireDirection = lockedDirection;
        if (!lockAimDuringTelegraph && TryGetDelayedAimPoint(out Vector2 updatedAimPoint))
        {
            Vector2 currentOrigin = muzzle != null ? (Vector2)muzzle.position : (Vector2)transform.position;
            Vector2 toUpdatedAim = updatedAimPoint - currentOrigin;
            if (toUpdatedAim.sqrMagnitude > 0.0001f) fireDirection = toUpdatedAim.normalized;
            origin = currentOrigin;
        }

        if (fireAsBurstStep) FireBurstStep(origin, fireDirection);
        else FireSingleTick(origin, fireDirection);

        isTelegraphing = false;
    }

    private EnemyTelegraphProfile GetTelegraphProfile(PatternType requestedPattern)
    {
        if (patternTelegraphs != null)
        {
            for (int i = 0; i < patternTelegraphs.Length; i++)
            {
                PatternTelegraph entry = patternTelegraphs[i];
                if (entry != null && entry.pattern == requestedPattern && entry.profile != null)
                    return entry.profile;
            }
        }
        return fallbackTelegraphProfile;
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
            ScheduleNextFire(Mathf.Max(MinInterval, intraBurstInterval), "IntraBurst");
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
            float t = (float)i / (count - 1);
            float ang = Mathf.Lerp(-half, half, t);
            SpawnProjectile(origin, Rotate(baseDir, ang));
        }

        // Guarantee one bullet aimed at the target's CURRENT real position,
        // bypassing aim lag. This closes the dead zone that opens up when fan
        // gaps align with a stationary player, regardless of spread angle.
        if (guaranteeCenterBullet && target != null)
        {
            Vector2 toTarget = (Vector2)target.position - origin;
            if (toTarget.sqrMagnitude > 0.0001f)
                SpawnProjectile(origin, toTarget.normalized);
        }
    }

    private void EmitRing(Vector2 origin, int count, Vector2 baseDir)
    {
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < count; i++)
        {
            float ang = baseAngle + (360f * i / count);
            SpawnProjectile(origin, AngleToDir(ang));
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

        Collider2D[] ownerCols = GetComponentsInChildren<Collider2D>(true);
        Collider2D[] projCols = spawned.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerCols.Length; i++)
            for (int j = 0; j < projCols.Length; j++)
                if (ownerCols[i] != null && projCols[j] != null)
                    Physics2D.IgnoreCollision(ownerCols[i], projCols[j], true);

        if (spawned.TryGetComponent<Projectile>(out Projectile p))
            p.Initialize(dir, projectileSpeed);

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