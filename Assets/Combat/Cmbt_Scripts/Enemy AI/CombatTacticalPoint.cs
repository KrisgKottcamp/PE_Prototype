using UnityEngine;
using System.Collections.Generic;

public enum TacticalPointType
{
    Cover,
    Fire,
    FlankLeft,
    FlankRight,
    Retreat
}

[ExecuteAlways]
public class CombatTacticalPoint : MonoBehaviour
{
    [Header("Point Setup")]
    public TacticalPointType pointType = TacticalPointType.Cover;

    [Min(0.05f)] public float arrivalRadius = 0.35f;

    [Tooltip("Optional lane tag: -1 = left, 0 = center, 1 = right")]
    [Range(-1, 1)] public int laneTag = 0;

    [Tooltip("Base bias used later by scoring.")]
    public float baseScoreBias = 0f;

    [Header("Zone Wander")]
    [Tooltip("Radius around this point that enemies can reposition within while shooting. " +
             "Larger = more strafing range. Set to 0 to disable zone wander for this point.")]
    public float wanderRadius = 1.2f;

    [Header("Reuse and Reservation")]
    [Min(0f)] public float reuseCooldown = 1.2f;

    [Header("Debug LOS")]
    public LayerMask losBlockMask;
    public Transform debugTarget;
    public bool drawLabel = true;

    private Component reservedBy;
    private float cooldownUntil;

    public Vector2 Position => transform.position;
    public bool IsReserved
    {
        get
        {
            CleanupStaleReservation();
            return reservedBy != null;
        }
    }

    public Component ReservedBy
    {
        get
        {
            CleanupStaleReservation();
            return reservedBy;
        }
    }

    public float CooldownRemaining =>
        Mathf.Max(0f, cooldownUntil - Time.time);

    public bool CanBeUsedBy(Component requester, float now)
    {
        CleanupStaleReservation();

        if (now < cooldownUntil)
            return false;

        if (reservedBy != null &&
            reservedBy != requester)
        {
            return false;
        }

        return true;
    }

    public bool TryReserve(Component requester, float now)
    {
        if (!CanBeUsedBy(requester, now)) return false;
        reservedBy = requester;
        return true;
    }

    public void Release(Component requester, float now)
    {
        if (reservedBy == requester) reservedBy = null;
        cooldownUntil = Mathf.Max(cooldownUntil, now + reuseCooldown);
    }

    public void ForceReleaseNow()
    {
        reservedBy = null;
        cooldownUntil = 0f;
    }

    private void CleanupStaleReservation()
    {
        if (reservedBy == null)
            return;

        Behaviour behaviour =
            reservedBy as Behaviour;

        if (behaviour != null &&
            (!behaviour.isActiveAndEnabled ||
             !behaviour.gameObject.activeInHierarchy))
        {
            reservedBy = null;
            return;
        }

        GameObject ownerObject =
            reservedBy.gameObject;

        if (ownerObject == null ||
            !ownerObject.activeInHierarchy)
        {
            reservedBy = null;
        }
    }

    public bool HasLineOfSightTo(Vector2 target)
    {
        return !Physics2D.Linecast(Position, target, losBlockMask);
    }

    public bool HasCoverFrom(Vector2 source)
    {
        return Physics2D.Linecast(source, Position, losBlockMask);
    }

    /// <summary>
    /// Returns a random position within the wander zone.
    /// Validates that the path from this point's center to the candidate is not blocked.
    /// Falls back to the point's exact position if no valid spot is found after all attempts.
    /// </summary>
    public Vector2 GetRandomWanderPosition(int attempts = 8)
    {
        if (wanderRadius <= 0f) return Position;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 candidate = Position + UnityEngine.Random.insideUnitCircle * wanderRadius;

            if (losBlockMask.value != 0 && Physics2D.Linecast(Position, candidate, losBlockMask))
                continue;

            return candidate;
        }

        return Position;
    }

    private void OnDrawGizmos()
    {
        Color c = pointType switch
        {
            TacticalPointType.Cover => new Color(0.2f, 0.9f, 1f),
            TacticalPointType.Fire => new Color(1f, 0.7f, 0.1f),
            TacticalPointType.FlankLeft => new Color(0.8f, 0.2f, 1f),
            TacticalPointType.FlankRight => new Color(0.6f, 0.2f, 1f),
            TacticalPointType.Retreat => new Color(0.3f, 1f, 0.3f),
            _ => Color.white
        };

        if (IsReserved) c *= 0.6f;

        Gizmos.color = c;
        Gizmos.DrawSphere(transform.position, 0.1f);
        Gizmos.DrawWireSphere(transform.position, arrivalRadius);

        // Wander zone shown as a faint outer ring
        if (wanderRadius > arrivalRadius)
        {
            Color wc = c;
            wc.a = 0.25f;
            Gizmos.color = wc;
            Gizmos.DrawWireSphere(transform.position, wanderRadius);
        }

        if (debugTarget != null)
        {
            bool los = HasLineOfSightTo(debugTarget.position);
            Gizmos.color = los ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, debugTarget.position);
        }
    }

    private static readonly List<CombatTacticalPoint> allPoints = new();
    public static IReadOnlyList<CombatTacticalPoint> AllPoints => allPoints;

    private void OnEnable()
    {
        if (!allPoints.Contains(this)) allPoints.Add(this);
    }

    private void OnDisable()
    {
        ForceReleaseNow();
        allPoints.Remove(this);
    }

    private void OnDestroy()
    {
        ForceReleaseNow();
        allPoints.Remove(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawLabel) return;

        string txt = $"{name}\n{pointType}\nLane {laneTag}";
        if (wanderRadius > 0f) txt += $"\nWander r={wanderRadius:F1}";
        if (IsReserved) txt += "\n[RESERVED]";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.2f, txt);
    }
#endif
}