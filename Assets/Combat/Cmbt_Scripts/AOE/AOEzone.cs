using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic AoE zone. Handles the full lifecycle:
///
///   Travel optional → Active → Fade during final lifetime → Destroy
///
/// Fixes:
/// - Existing occupants caught on spawn no longer get stuck slowed.
/// - Multi-collider entities are tracked by root object instead of fragile enter/exit counts.
/// - Zone periodically verifies who is still inside.
/// - Fade preserves prefab alpha and finishes exactly when duration ends.
/// </summary>
public class AoEZone : MonoBehaviour
{
    private enum Phase { Traveling, Active, Done }

    [Header("Zone Visual")]
    [SerializeField] private SpriteRenderer zoneVisual;
    [SerializeField] private float fadeOutTime = 0.3f;

    [Header("Travel Visual Optional")]
    [SerializeField] private SpriteRenderer travelVisual;

    [Header("Trigger")]
    [SerializeField] private CircleCollider2D zoneTrigger;

    [Header("Occupant Tracking")]
    [Tooltip("How often the zone verifies who is actually still inside. Fixes stuck slows from spawn-overlap edge cases.")]
    [SerializeField] private float occupantRecheckInterval = 0.05f;

    [Tooltip("Bigger buffer if you expect tons of colliders inside one zone.")]
    [SerializeField] private int overlapBufferSize = 96;

    // Set by Initialize
    private Phase phase = Phase.Done;
    private float radius;
    private float duration;
    private List<AoEEffect> effects = new();
    private int sourceId;

    // Travel
    private Vector2 travelDir;
    private float travelSpeed;
    private float burstTime;
    private LayerMask obstacleMask;

    // Active
    private float activeEndTime;
    private float fadeStartTime;
    private bool fadeStarted;
    private Coroutine fadeRoutine;

    // Occupants
    private readonly HashSet<GameObject> trackedRoots = new();
    private readonly HashSet<GameObject> scanRoots = new();
    private readonly List<GameObject> rootsToRemove = new();
    private Collider2D[] overlapBuffer;
    private float nextOccupantRecheckTime;

    public void Initialize(
        float radius,
        float duration,
        List<AoEEffect> effects,
        Vector2 travelDir,
        float travelSpeed,
        float travelTime,
        LayerMask obstacleMask)
    {
        this.radius = Mathf.Max(0.01f, radius);
        this.duration = Mathf.Max(0.01f, duration);
        this.effects = effects != null ? new List<AoEEffect>(effects) : new List<AoEEffect>();

        this.travelDir = travelDir.sqrMagnitude > 0.0001f ? travelDir.normalized : Vector2.zero;
        this.travelSpeed = Mathf.Max(0f, travelSpeed);
        this.obstacleMask = obstacleMask;

        sourceId = SpeedModifier.GenerateSourceId();

        if (overlapBuffer == null || overlapBuffer.Length != Mathf.Max(8, overlapBufferSize))
            overlapBuffer = new Collider2D[Mathf.Max(8, overlapBufferSize)];

        fadeStarted = false;
        fadeRoutine = null;

        bool hasTravel = this.travelSpeed > 0f && travelTime > 0f;

        if (hasTravel)
        {
            phase = Phase.Traveling;
            burstTime = Time.time + travelTime;

            if (travelVisual != null) travelVisual.enabled = true;
            if (zoneVisual != null) zoneVisual.enabled = false;
            if (zoneTrigger != null) zoneTrigger.enabled = false;
        }
        else
        {
            if (travelVisual != null) travelVisual.enabled = false;
            ActivateZone();
        }

        float maxLife = (hasTravel ? travelTime : 0f) + this.duration + Mathf.Max(0f, fadeOutTime) + 1f;
        Destroy(gameObject, maxLife);
    }

    private void Awake()
    {
        if (overlapBuffer == null)
            overlapBuffer = new Collider2D[Mathf.Max(8, overlapBufferSize)];
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.Traveling:
                TickTravel();
                break;

            case Phase.Active:
                if (Time.time >= nextOccupantRecheckTime)
                {
                    nextOccupantRecheckTime = Time.time + Mathf.Max(0.01f, occupantRecheckInterval);
                    ReconcileOccupants();
                }

                if (!fadeStarted && fadeOutTime > 0f && Time.time >= fadeStartTime)
                    BeginVisualFade();

                if (Time.time >= activeEndTime)
                    EndZone();

                break;
        }
    }

    private void TickTravel()
    {
        float dist = travelSpeed * Time.deltaTime;
        Vector2 pos = transform.position;

        if (obstacleMask.value != 0)
        {
            RaycastHit2D hit = Physics2D.Raycast(pos, travelDir, dist + 0.05f, obstacleMask);
            if (hit.collider != null)
            {
                transform.position = hit.point;
                ActivateZone();
                return;
            }
        }

        transform.position += (Vector3)(travelDir * dist);

        if (Time.time >= burstTime)
            ActivateZone();
    }

    private void ActivateZone()
    {
        phase = Phase.Active;
        activeEndTime = Time.time + duration;

        float actualFadeTime = Mathf.Min(Mathf.Max(0f, fadeOutTime), duration);
        fadeStartTime = activeEndTime - actualFadeTime;
        fadeStarted = false;

        nextOccupantRecheckTime = Time.time;

        if (travelVisual != null)
            travelVisual.enabled = false;

        if (zoneVisual != null)
        {
            zoneVisual.enabled = true;

            float diameter = radius * 2f;
            zoneVisual.transform.localScale = new Vector3(diameter, diameter, 1f);
        }

        if (zoneTrigger != null)
        {
            zoneTrigger.isTrigger = true;

            float worldScale = Mathf.Max(0.001f, zoneTrigger.transform.lossyScale.x);
            zoneTrigger.radius = radius / worldScale;
            zoneTrigger.enabled = true;
        }

        // Immediately catch anything already inside, including the player if the orb spawns on them.
        ReconcileOccupants();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (phase != Phase.Active) return;
        if (other == null || other == zoneTrigger) return;

        GameObject root = other.transform.root.gameObject;
        ApplyRoot(root);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (phase != Phase.Active) return;

        // Do not remove immediately.
        // Multi-collider objects and spawn-overlap cases can produce mismatched enter/exit events.
        // The periodic ReconcileOccupants pass removes effects only when the root is truly outside.
        nextOccupantRecheckTime = Time.time;
    }

    private void ReconcileOccupants()
    {
        if (phase != Phase.Active)
            return;

        scanRoots.Clear();

        Vector2 center = transform.position;
        int count = Physics2D.OverlapCircleNonAlloc(center, radius, overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == zoneTrigger) continue;

            GameObject root = col.transform.root.gameObject;
            if (root == null) continue;

            scanRoots.Add(root);
            ApplyRoot(root);
        }

        rootsToRemove.Clear();

        foreach (GameObject tracked in trackedRoots)
        {
            if (tracked == null || !scanRoots.Contains(tracked))
                rootsToRemove.Add(tracked);
        }

        for (int i = 0; i < rootsToRemove.Count; i++)
            RemoveRoot(rootsToRemove[i]);

        rootsToRemove.Clear();
    }

    private void ApplyRoot(GameObject root)
    {
        if (root == null) return;

        if (trackedRoots.Contains(root))
            return;

        trackedRoots.Add(root);

        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null)
                effects[i].OnApply(root, sourceId);
        }
    }

    private void RemoveRoot(GameObject root)
    {
        if (root == null)
        {
            trackedRoots.Remove(root);
            return;
        }

        if (!trackedRoots.Remove(root))
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null)
                effects[i].OnRemove(root, sourceId);
        }
    }

    private void BeginVisualFade()
    {
        if (fadeStarted) return;

        fadeStarted = true;

        if (zoneVisual == null || fadeOutTime <= 0f)
            return;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeVisualOnly());
    }

    private IEnumerator FadeVisualOnly()
    {
        if (zoneVisual == null)
            yield break;

        Color c = zoneVisual.color;
        float startAlpha = c.a;

        float fadeDuration = Mathf.Max(0.001f, activeEndTime - Time.time);
        float elapsed = 0f;

        while (elapsed < fadeDuration && phase == Phase.Active)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fadeDuration);

            c.a = Mathf.Lerp(startAlpha, 0f, k);
            zoneVisual.color = c;

            yield return null;
        }

        if (zoneVisual != null)
        {
            c = zoneVisual.color;
            c.a = 0f;
            zoneVisual.color = c;
        }

        fadeRoutine = null;
    }

    private void EndZone()
    {
        if (phase == Phase.Done) return;

        CleanUpEffects();
        phase = Phase.Done;

        Destroy(gameObject);
    }

    private void CleanUpEffects()
    {
        rootsToRemove.Clear();

        foreach (GameObject root in trackedRoots)
            rootsToRemove.Add(root);

        for (int i = 0; i < rootsToRemove.Count; i++)
        {
            GameObject root = rootsToRemove[i];
            if (root == null) continue;

            for (int e = 0; e < effects.Count; e++)
            {
                if (effects[e] != null)
                    effects[e].OnRemove(root, sourceId);
            }
        }

        trackedRoots.Clear();
        scanRoots.Clear();
        rootsToRemove.Clear();

        if (zoneTrigger != null)
            zoneTrigger.enabled = false;
    }

    private void OnDestroy()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        CleanUpEffects();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);

        float drawRadius = radius > 0f ? radius : 1f;
        Gizmos.DrawWireSphere(transform.position, drawRadius);
    }
#endif
}