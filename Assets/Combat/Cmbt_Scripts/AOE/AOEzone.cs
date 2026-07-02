using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic AoE zone. Handles the full lifecycle:
///
/// Travel optional -> Active -> Fade during final lifetime -> Destroy
///
/// Fixes:
/// - Existing occupants caught on spawn no longer get stuck slowed.
/// - Multi-collider entities are tracked by their actual actor object.
/// - Procedural enemies are resolved through EnemyHealth instead of the scene root.
/// - Zone periodically verifies who is still inside.
/// - Fade preserves prefab alpha and finishes exactly when duration ends.
/// </summary>
public class AoEZone : MonoBehaviour
{
    private enum Phase
    {
        Traveling,
        Active,
        Done
    }

    [Header("Zone Visual")]
    [SerializeField] private SpriteRenderer zoneVisual;
    [SerializeField] private float fadeOutTime = 0.3f;

    [Header("Travel Visual Optional")]
    [SerializeField] private SpriteRenderer travelVisual;

    [Header("Trigger")]
    [SerializeField] private CircleCollider2D zoneTrigger;

    [Header("Occupant Tracking")]
    [Tooltip(
        "How often the zone verifies who is actually still inside. " +
        "This fixes stuck slows from spawn-overlap edge cases."
    )]
    [SerializeField] private float occupantRecheckInterval = 0.05f;

    [Tooltip("Increase this if many colliders may be inside one zone.")]
    [SerializeField] private int overlapBufferSize = 96;

    private Phase phase = Phase.Done;
    private float radius;
    private float duration;
    private List<AoEEffect> effects = new List<AoEEffect>();
    private int sourceId;

    private Vector2 travelDir;
    private float travelSpeed;
    private float burstTime;
    private LayerMask obstacleMask;

    private float activeEndTime;
    private float fadeStartTime;
    private bool fadeStarted;
    private Coroutine fadeRoutine;

    private readonly HashSet<GameObject> trackedTargets =
        new HashSet<GameObject>();

    private readonly HashSet<GameObject> scannedTargets =
        new HashSet<GameObject>();

    private readonly List<GameObject> targetsToRemove =
        new List<GameObject>();

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

        this.effects =
            effects != null
                ? new List<AoEEffect>(effects)
                : new List<AoEEffect>();

        this.travelDir =
            travelDir.sqrMagnitude > 0.0001f
                ? travelDir.normalized
                : Vector2.zero;

        this.travelSpeed = Mathf.Max(0f, travelSpeed);
        this.obstacleMask = obstacleMask;

        sourceId = SpeedModifier.GenerateSourceId();

        EnsureOverlapBuffer();

        fadeStarted = false;
        fadeRoutine = null;

        bool hasTravel =
            this.travelSpeed > 0f &&
            travelTime > 0f;

        if (hasTravel)
        {
            phase = Phase.Traveling;
            burstTime = Time.time + travelTime;

            if (travelVisual != null)
                travelVisual.enabled = true;

            if (zoneVisual != null)
                zoneVisual.enabled = false;

            if (zoneTrigger != null)
                zoneTrigger.enabled = false;
        }
        else
        {
            if (travelVisual != null)
                travelVisual.enabled = false;

            ActivateZone();
        }

        float maxLife =
            (hasTravel ? travelTime : 0f) +
            this.duration +
            Mathf.Max(0f, fadeOutTime) +
            1f;

        Destroy(gameObject, maxLife);
    }

    private void Awake()
    {
        EnsureOverlapBuffer();
    }

    private void EnsureOverlapBuffer()
    {
        int requiredSize =
            Mathf.Max(8, overlapBufferSize);

        if (overlapBuffer == null ||
            overlapBuffer.Length != requiredSize)
        {
            overlapBuffer =
                new Collider2D[requiredSize];
        }
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
                    nextOccupantRecheckTime =
                        Time.time +
                        Mathf.Max(
                            0.01f,
                            occupantRecheckInterval
                        );

                    ReconcileOccupants();
                }

                if (!fadeStarted &&
                    fadeOutTime > 0f &&
                    Time.time >= fadeStartTime)
                {
                    BeginVisualFade();
                }

                if (Time.time >= activeEndTime)
                    EndZone();

                break;
        }
    }

    private void TickTravel()
    {
        float distance =
            travelSpeed * Time.deltaTime;

        Vector2 position =
            transform.position;

        if (obstacleMask.value != 0)
        {
            RaycastHit2D hit =
                Physics2D.Raycast(
                    position,
                    travelDir,
                    distance + 0.05f,
                    obstacleMask
                );

            if (hit.collider != null)
            {
                transform.position = hit.point;
                ActivateZone();
                return;
            }
        }

        transform.position +=
            (Vector3)(travelDir * distance);

        if (Time.time >= burstTime)
            ActivateZone();
    }

    private void ActivateZone()
    {
        phase = Phase.Active;
        activeEndTime = Time.time + duration;

        float actualFadeTime =
            Mathf.Min(
                Mathf.Max(0f, fadeOutTime),
                duration
            );

        fadeStartTime =
            activeEndTime - actualFadeTime;

        fadeStarted = false;
        nextOccupantRecheckTime = Time.time;

        if (travelVisual != null)
            travelVisual.enabled = false;

        if (zoneVisual != null)
        {
            zoneVisual.enabled = true;

            float diameter = radius * 2f;

            zoneVisual.transform.localScale =
                new Vector3(
                    diameter,
                    diameter,
                    1f
                );
        }

        if (zoneTrigger != null)
        {
            zoneTrigger.isTrigger = true;

            float worldScale =
                Mathf.Max(
                    0.001f,
                    zoneTrigger.transform.lossyScale.x
                );

            zoneTrigger.radius =
                radius / worldScale;

            zoneTrigger.enabled = true;
        }

        ReconcileOccupants();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (phase != Phase.Active)
            return;

        if (other == null ||
            other == zoneTrigger)
        {
            return;
        }

        ApplyTarget(
            ResolveEffectTarget(other)
        );
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (phase != Phase.Active)
            return;

        nextOccupantRecheckTime = Time.time;
    }

    private void ReconcileOccupants()
    {
        if (phase != Phase.Active)
            return;

        EnsureOverlapBuffer();
        scannedTargets.Clear();

        int count =
            Physics2D.OverlapCircleNonAlloc(
                transform.position,
                radius,
                overlapBuffer
            );

        for (int i = 0; i < count; i++)
        {
            Collider2D collider =
                overlapBuffer[i];

            if (collider == null ||
                collider == zoneTrigger)
            {
                continue;
            }

            GameObject target =
                ResolveEffectTarget(collider);

            if (target == null)
                continue;

            scannedTargets.Add(target);
            ApplyTarget(target);
        }

        targetsToRemove.Clear();

        foreach (GameObject tracked in trackedTargets)
        {
            if (tracked == null ||
                !scannedTargets.Contains(tracked))
            {
                targetsToRemove.Add(tracked);
            }
        }

        for (int i = 0;
             i < targetsToRemove.Count;
             i++)
        {
            RemoveTarget(
                targetsToRemove[i]
            );
        }

        targetsToRemove.Clear();
    }

    private GameObject ResolveEffectTarget(
        Collider2D collider)
    {
        if (collider == null)
            return null;

        EnemyHealth enemy =
            collider.GetComponentInParent<EnemyHealth>();

        if (enemy != null)
            return enemy.gameObject;

        CombatPawn combatPawn =
            collider.GetComponentInParent<CombatPawn>();

        if (combatPawn != null)
            return combatPawn.gameObject;

        if (collider.attachedRigidbody != null)
        {
            return collider
                .attachedRigidbody
                .gameObject;
        }

        return collider.gameObject;
    }

    private void ApplyTarget(GameObject target)
    {
        if (target == null ||
            trackedTargets.Contains(target))
        {
            return;
        }

        trackedTargets.Add(target);

        for (int i = 0; i < effects.Count; i++)
        {
            AoEEffect effect = effects[i];

            if (effect != null)
            {
                effect.OnApply(
                    target,
                    sourceId
                );
            }
        }
    }

    private void RemoveTarget(GameObject target)
    {
        if (target == null)
        {
            trackedTargets.Remove(target);
            return;
        }

        if (!trackedTargets.Remove(target))
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            AoEEffect effect = effects[i];

            if (effect != null)
            {
                effect.OnRemove(
                    target,
                    sourceId
                );
            }
        }
    }

    private void BeginVisualFade()
    {
        if (fadeStarted)
            return;

        fadeStarted = true;

        if (zoneVisual == null ||
            fadeOutTime <= 0f)
        {
            return;
        }

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine =
            StartCoroutine(FadeVisualOnly());
    }

    private IEnumerator FadeVisualOnly()
    {
        if (zoneVisual == null)
            yield break;

        Color color = zoneVisual.color;
        float startAlpha = color.a;

        float fadeDuration =
            Mathf.Max(
                0.001f,
                activeEndTime - Time.time
            );

        float elapsed = 0f;

        while (elapsed < fadeDuration &&
               phase == Phase.Active)
        {
            elapsed += Time.deltaTime;

            float progress =
                Mathf.Clamp01(
                    elapsed / fadeDuration
                );

            color.a =
                Mathf.Lerp(
                    startAlpha,
                    0f,
                    progress
                );

            zoneVisual.color = color;

            yield return null;
        }

        if (zoneVisual != null)
        {
            color = zoneVisual.color;
            color.a = 0f;
            zoneVisual.color = color;
        }

        fadeRoutine = null;
    }

    private void EndZone()
    {
        if (phase == Phase.Done)
            return;

        CleanUpEffects();
        phase = Phase.Done;

        Destroy(gameObject);
    }

    private void CleanUpEffects()
    {
        targetsToRemove.Clear();

        foreach (GameObject target in trackedTargets)
        {
            targetsToRemove.Add(target);
        }

        for (int i = 0;
             i < targetsToRemove.Count;
             i++)
        {
            GameObject target =
                targetsToRemove[i];

            if (target == null)
                continue;

            for (int e = 0;
                 e < effects.Count;
                 e++)
            {
                AoEEffect effect = effects[e];

                if (effect != null)
                {
                    effect.OnRemove(
                        target,
                        sourceId
                    );
                }
            }
        }

        trackedTargets.Clear();
        scannedTargets.Clear();
        targetsToRemove.Clear();

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
        Gizmos.color =
            new Color(
                0.2f,
                0.8f,
                1f,
                0.2f
            );

        float drawRadius =
            radius > 0f
                ? radius
                : 1f;

        Gizmos.DrawWireSphere(
            transform.position,
            drawRadius
        );
    }
#endif
}
