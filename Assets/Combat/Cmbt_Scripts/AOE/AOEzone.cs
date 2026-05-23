using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic AoE zone. Handles the full lifecycle:
///
///   Travel (optional) → Active → Fade Out → Destroy
///
/// Travel phase:  Moves in a straight line. Bursts on obstacle (raycast) or timer.
/// Active phase:  Stationary. Applies AoEEffects to occupants via trigger.
/// Fade phase:    Removes all effects, fades visual, self-destructs.
///
/// Replaces the need for separate projectile and zone scripts.
/// Spawned and configured by CombatSkillSystem.
///
/// Prefab setup:
///   - CircleCollider2D (trigger, starts disabled — enabled on activate)
///   - SpriteRenderer for the zone visual
///   - Optional: a child SpriteRenderer for the travel/orb visual
/// </summary>
public class AoEZone : MonoBehaviour
{
    private enum Phase { Traveling, Active, Done }

    [Header("Zone Visual")]
    [SerializeField] private SpriteRenderer zoneVisual;
    [SerializeField] private float fadeOutTime = 0.3f;

    [Header("Travel Visual (optional)")]
    [SerializeField] private SpriteRenderer travelVisual;

    [Header("Trigger")]
    [SerializeField] private CircleCollider2D zoneTrigger;

    // -- Set by Initialize --
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

    // Occupant tracking (handles multi-collider entities)
    private readonly Dictionary<GameObject, int> occupantCounts = new();
    private readonly HashSet<GameObject> trackedRoots = new();
    private readonly Collider2D[] overlapBuffer = new Collider2D[32];

    // --------------------------------------------------
    // Initialization
    // --------------------------------------------------

    /// <summary>
    /// Full initialization. If travelSpeed and travelTime are > 0, the zone
    /// starts in travel mode. Otherwise it activates immediately.
    /// </summary>
    public void Initialize(
        float radius,
        float duration,
        List<AoEEffect> effects,
        Vector2 travelDir,
        float travelSpeed,
        float travelTime,
        LayerMask obstacleMask)
    {
        this.radius = radius;
        this.duration = duration;
        this.effects = effects != null ? new List<AoEEffect>(effects) : new();
        this.travelDir = travelDir.sqrMagnitude > 0.0001f ? travelDir.normalized : Vector2.zero;
        this.travelSpeed = travelSpeed;
        this.obstacleMask = obstacleMask;

        sourceId = SpeedModifier.GenerateSourceId();

        bool hasTravel = travelSpeed > 0f && travelTime > 0f;

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

        // Safety net
        float maxLife = (hasTravel ? travelTime : 0f) + duration + fadeOutTime + 1f;
        Destroy(gameObject, maxLife);
    }

    // --------------------------------------------------
    // Update
    // --------------------------------------------------

    private void Update()
    {
        switch (phase)
        {
            case Phase.Traveling:
                TickTravel();
                break;

            case Phase.Active:
                if (Time.time >= activeEndTime)
                    BeginFadeOut();
                break;
        }
    }

    private void TickTravel()
    {
        float dist = travelSpeed * Time.deltaTime;
        Vector2 pos = (Vector2)transform.position;

        // Raycast ahead for obstacles
        if (obstacleMask.value != 0)
        {
            var hit = Physics2D.Raycast(pos, travelDir, dist + 0.05f, obstacleMask);
            if (hit.collider != null)
            {
                transform.position = (Vector3)hit.point;
                ActivateZone();
                return;
            }
        }

        transform.position += (Vector3)(travelDir * dist);

        if (Time.time >= burstTime)
            ActivateZone();
    }

    // --------------------------------------------------
    // Zone activation
    // --------------------------------------------------

    private void ActivateZone()
    {
        phase = Phase.Active;
        activeEndTime = Time.time + duration;

        if (travelVisual != null) travelVisual.enabled = false;

        if (zoneVisual != null)
        {
            zoneVisual.enabled = true;
            // Scale visual to match radius (assumes 1-unit sprite at scale 1)
            float diameter = radius * 2f;
            zoneVisual.transform.localScale = new Vector3(diameter, diameter, 1f);
        }

        if (zoneTrigger != null)
        {
            zoneTrigger.isTrigger = true;
            // Compensate for any transform scale (visual scaling on same GO inflates the collider)
            float worldScale = Mathf.Max(0.001f, zoneTrigger.transform.lossyScale.x);
            zoneTrigger.radius = radius / worldScale;
            zoneTrigger.enabled = true;
        }

        // Catch anything already overlapping the zone at activation
        CatchExistingOccupants();
    }

    private void CatchExistingOccupants()
    {
        Vector2 center = (Vector2)transform.position;
        int count = Physics2D.OverlapCircleNonAlloc(center, radius, overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            var col = overlapBuffer[i];
            if (col == null || col == zoneTrigger) continue;
            HandleOccupantEnter(col);
        }
    }

    // --------------------------------------------------
    // Occupant tracking
    // --------------------------------------------------

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (phase != Phase.Active) return;
        HandleOccupantEnter(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (phase != Phase.Active) return;
        HandleOccupantExit(other);
    }

    private void HandleOccupantEnter(Collider2D other)
    {
        GameObject root = other.transform.root.gameObject;

        if (!occupantCounts.ContainsKey(root))
            occupantCounts[root] = 0;
        occupantCounts[root]++;

        // Apply effects only on the first collider entering
        if (occupantCounts[root] == 1)
        {
            trackedRoots.Add(root);
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] != null) effects[i].OnApply(root, sourceId);
        }
    }

    private void HandleOccupantExit(Collider2D other)
    {
        GameObject root = other.transform.root.gameObject;

        if (!occupantCounts.ContainsKey(root)) return;
        occupantCounts[root]--;

        // Remove effects only when ALL colliders have left
        if (occupantCounts[root] <= 0)
        {
            occupantCounts.Remove(root);
            trackedRoots.Remove(root);
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] != null) effects[i].OnRemove(root, sourceId);
        }
    }

    // --------------------------------------------------
    // Fade out and cleanup
    // --------------------------------------------------

    private void BeginFadeOut()
    {
        CleanUpEffects();
        phase = Phase.Done;
        StartCoroutine(FadeAndDestroy());
    }

    private IEnumerator FadeAndDestroy()
    {
        if (zoneVisual != null && fadeOutTime > 0f)
        {
            Color c = zoneVisual.color;
            float elapsed = 0f;
            while (elapsed < fadeOutTime)
            {
                elapsed += Time.deltaTime;
                c.a = 1f - Mathf.Clamp01(elapsed / fadeOutTime);
                zoneVisual.color = c;
                yield return null;
            }
        }

        Destroy(gameObject);
    }

    private void CleanUpEffects()
    {
        foreach (var root in trackedRoots)
        {
            if (root == null) continue;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i] != null) effects[i].OnRemove(root, sourceId);
        }
        trackedRoots.Clear();
        occupantCounts.Clear();

        if (zoneTrigger != null) zoneTrigger.enabled = false;
    }

    private void OnDestroy()
    {
        CleanUpEffects();
    }

    // --------------------------------------------------
    // Gizmos
    // --------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}