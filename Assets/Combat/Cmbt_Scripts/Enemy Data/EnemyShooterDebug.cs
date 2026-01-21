using System.Collections.Generic;
using UnityEngine;

public class EnemyShooterDebug : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab; // must have Projectile component
    [SerializeField] private float fireInterval = 0.6f;

    [Header("Target (leave empty to auto-find)")]
    [SerializeField] private CombatPawn targetPawn;

    [Header("Aim Lag")]
    [Tooltip("Aim at where the target was this many seconds ago.")]
    [SerializeField] private float aimLagSeconds = 0.12f;

    [Tooltip("How many seconds of position history to keep. Must be >= aimLagSeconds.")]
    [SerializeField] private float historySeconds = 1.0f;

    [Tooltip("How often to record target position samples (seconds). 0.02 is a good default.")]
    [SerializeField] private float recordInterval = 0.02f;

    [Tooltip("If true, history uses real time (ignores Time.timeScale). Usually keep false.")]
    [SerializeField] private bool useUnscaledTime = false;

    private float fireTimer;
    private float nextRecordTime;

    private Rigidbody2D targetRb;

    private struct Sample
    {
        public float t;
        public Vector2 pos;
        public Sample(float t, Vector2 pos) { this.t = t; this.pos = pos; }
    }

    private readonly List<Sample> samples = new();

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void OnEnable()
    {
        samples.Clear();
        nextRecordTime = Now;
    }

    private void Update()
    {
        if (projectilePrefab == null) return;

        AcquireTarget();
        if (targetPawn == null) return;

        RecordTargetHistory();

        fireTimer += Time.deltaTime;
        if (fireTimer < fireInterval) return;
        fireTimer = 0f;

        FireAtLaggedPosition();
    }

    private void AcquireTarget()
    {
        if (targetPawn == null)
        {
            targetPawn = FindObjectOfType<CombatPawn>(true);
            targetRb = null;
            if (targetPawn != null) targetRb = targetPawn.GetComponent<Rigidbody2D>();
        }
        else if (targetRb == null)
        {
            targetRb = targetPawn.GetComponent<Rigidbody2D>();
        }
    }

    private Vector2 ReadTargetPosition()
    {
        if (targetRb != null) return targetRb.position;
        return targetPawn.transform.position;
    }

    private void RecordTargetHistory()
    {
        // Sanitize values from Inspector
        float interval = Mathf.Max(0.005f, recordInterval);
        float keep = Mathf.Max(0.1f, historySeconds);

        float t = Now;
        if (t < nextRecordTime) return;

        nextRecordTime = t + interval;

        samples.Add(new Sample(t, ReadTargetPosition()));
        PruneOldSamples(t, keep);
    }

    private void PruneOldSamples(float now, float keepSeconds)
    {
        float cutoff = now - keepSeconds;

        int removeCount = 0;
        while (removeCount < samples.Count && samples[removeCount].t < cutoff)
            removeCount++;

        if (removeCount > 0)
            samples.RemoveRange(0, removeCount);
    }

    private void FireAtLaggedPosition()
    {
        Vector2 targetPos = GetTargetPositionSecondsAgo(aimLagSeconds);

        Vector2 dir = (targetPos - (Vector2)transform.position);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir = dir.normalized;

        GameObject go = Instantiate(projectilePrefab, transform.position, Quaternion.identity);

        Projectile proj = go.GetComponent<Projectile>();
        if (proj == null)
        {
            Debug.LogError("EnemyShooterDebug: projectilePrefab has no Projectile component.");
            Destroy(go);
            return;
        }

        proj.Fire(dir);
    }

    private Vector2 GetTargetPositionSecondsAgo(float secondsAgo)
    {
        if (targetPawn == null) return transform.position;

        float keep = Mathf.Max(0.1f, historySeconds);
        secondsAgo = Mathf.Clamp(secondsAgo, 0f, keep);

        // If no samples yet, use current
        if (samples.Count == 0) return ReadTargetPosition();

        float targetTime = Now - secondsAgo;

        // Clamp to recorded range
        if (targetTime <= samples[0].t) return samples[0].pos;
        if (targetTime >= samples[samples.Count - 1].t) return samples[samples.Count - 1].pos;

        // Interpolate between surrounding samples
        for (int i = samples.Count - 1; i > 0; i--)
        {
            Sample a = samples[i - 1];
            Sample b = samples[i];

            if (a.t <= targetTime && targetTime <= b.t)
            {
                float u = Mathf.InverseLerp(a.t, b.t, targetTime);
                return Vector2.Lerp(a.pos, b.pos, u);
            }
        }

        return samples[samples.Count - 1].pos;
    }
}
