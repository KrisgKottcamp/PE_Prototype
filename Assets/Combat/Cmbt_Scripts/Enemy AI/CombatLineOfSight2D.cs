using UnityEngine;

/// <summary>
/// Shared 2D line-of-sight helper.
///
/// Important:
/// It ignores only colliders belonging to the observing enemy.
/// It never compares transform.root, because procedural enemies and
/// obstacles may share the same arena root.
/// </summary>
public static class CombatLineOfSight2D
{
    public static bool HasLineOfSight(
        Component observer,
        Vector2 origin,
        Vector2 target,
        LayerMask blockingMask,
        out Collider2D blocker)
    {
        blocker = null;

        EnemyHealth observerEnemy =
            observer != null
                ? observer.GetComponentInParent<EnemyHealth>()
                : null;

        RaycastHit2D[] hits =
            Physics2D.LinecastAll(
                origin,
                target,
                blockingMask
            );

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCollider =
                hits[i].collider;

            if (hitCollider == null)
                continue;

            EnemyHealth hitEnemy =
                hitCollider.GetComponentInParent<EnemyHealth>();

            if (observerEnemy != null &&
                hitEnemy == observerEnemy)
            {
                continue;
            }

            blocker = hitCollider;
            return false;
        }

        return true;
    }

    public static bool HasLineOfSight(
        Component observer,
        Vector2 origin,
        Transform target,
        LayerMask blockingMask,
        out Collider2D blocker)
    {
        if (target == null)
        {
            blocker = null;
            return false;
        }

        return HasLineOfSight(
            observer,
            origin,
            target.position,
            blockingMask,
            out blocker
        );
    }

    public static bool IsBlocked(
        Component observer,
        Vector2 origin,
        Vector2 target,
        LayerMask blockingMask,
        out Collider2D blocker)
    {
        return !HasLineOfSight(
            observer,
            origin,
            target,
            blockingMask,
            out blocker
        );
    }
}
