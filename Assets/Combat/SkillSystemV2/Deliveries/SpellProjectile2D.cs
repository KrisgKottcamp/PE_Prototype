using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellProjectile2D : MonoBehaviour
    {
        private SpellExecutionContext context;
        private Vector2 direction;
        private float speed;
        private float maximumDistance;
        private float collisionRadius;
        private LayerMask collisionMask;
        private bool pierceTargets;
        private int maximumTargetHits;
        private bool stopOnBlockedCollider;
        private SpellTimeMode timeMode;
        private RaycastHit2D[] castBuffer;
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private float distanceTravelled;
        private int targetHitCount;
        private bool launched;

        public bool IsComplete { get; private set; }

        public void Launch(
            in SpellExecutionContext executionContext,
            Vector2 aimDirection,
            float projectileSpeed,
            float range,
            float radius,
            LayerMask mask,
            bool shouldPierceTargets,
            int maximumHits,
            bool shouldStopOnBlockedCollider,
            int bufferSize,
            SpellTimeMode projectileTimeMode)
        {
            context = executionContext;
            direction = aimDirection.sqrMagnitude > 0.000001f
                ? aimDirection.normalized
                : Vector2.right;
            speed = Mathf.Max(0.01f, projectileSpeed);
            maximumDistance = Mathf.Max(0.01f, range);
            collisionRadius = Mathf.Max(0f, radius);
            collisionMask = mask;
            pierceTargets = shouldPierceTargets;
            maximumTargetHits = Mathf.Max(1, maximumHits);
            stopOnBlockedCollider = shouldStopOnBlockedCollider;
            timeMode = projectileTimeMode;
            castBuffer = new RaycastHit2D[Mathf.Max(1, bufferSize)];
            distanceTravelled = 0f;
            targetHitCount = 0;
            hitTargets.Clear();
            IsComplete = false;
            launched = true;

            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        public void Cancel()
        {
            Complete();
        }

        private void Update()
        {
            if (!launched || IsComplete)
                return;

            float deltaTime = timeMode == SpellTimeMode.Unscaled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            Step(Mathf.Max(0f, deltaTime));
        }

        internal void Step(float deltaTime)
        {
            if (!launched || IsComplete || deltaTime <= 0f)
                return;

            float remainingRange = maximumDistance - distanceTravelled;
            float stepDistance = Mathf.Min(speed * deltaTime, remainingRange);

            if (stepDistance <= 0f)
            {
                Complete();
                return;
            }

            Vector2 origin = transform.position;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(collisionMask);
            filter.useTriggers = Physics2D.queriesHitTriggers;
            int count = Physics2D.CircleCast(
                origin,
                collisionRadius,
                direction,
                filter,
                castBuffer,
                stepDistance);
            SortHitsByDistance(count);

            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = castBuffer[i];
                if (hit.collider == null)
                    continue;

                GameObject resolved = SpellTargetResolver.Resolve(
                    hit.collider.gameObject);
                if (resolved == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        resolved))
                {
                    continue;
                }

                bool validTarget = context.Spell != null &&
                                   context.Spell.TargetFilter.IsValid(
                                       context.Cast,
                                       resolved);

                if (!validTarget)
                {
                    if (stopOnBlockedCollider)
                    {
                        transform.position = hit.point;
                        Complete();
                        return;
                    }

                    continue;
                }

                int targetId = resolved.GetInstanceID();
                if (!hitTargets.Add(targetId))
                    continue;

                context.ApplyEffects(
                    resolved,
                    hit.point,
                    hit.normal);
                targetHitCount++;

                if (!pierceTargets || targetHitCount >= maximumTargetHits)
                {
                    transform.position = hit.point;
                    Complete();
                    return;
                }
            }

            transform.position = origin + direction * stepDistance;
            distanceTravelled += stepDistance;

            if (distanceTravelled >= maximumDistance - 0.0001f)
                Complete();
        }

        private void SortHitsByDistance(int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                int nearest = i;
                for (int j = i + 1; j < count; j++)
                {
                    if (castBuffer[j].distance < castBuffer[nearest].distance)
                        nearest = j;
                }

                if (nearest == i)
                    continue;

                RaycastHit2D swap = castBuffer[i];
                castBuffer[i] = castBuffer[nearest];
                castBuffer[nearest] = swap;
            }
        }

        private void Complete()
        {
            if (IsComplete)
                return;

            IsComplete = true;
            launched = false;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            IsComplete = true;
            launched = false;
        }
    }
}
