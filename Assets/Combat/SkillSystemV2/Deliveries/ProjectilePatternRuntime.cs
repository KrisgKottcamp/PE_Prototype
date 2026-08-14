using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellProjectileEmitter2D : MonoBehaviour
    {
        private readonly List<SpellProjectile2D> projectiles =
            new List<SpellProjectile2D>();
        private SpellExecutionContext context;
        private ProjectileDeliverySettings settings;
        private int randomSeed;
        private int nextShot;
        private float remainingInterval;
        private bool initialized;
        private bool cancelled;

        public bool IsComplete
        {
            get
            {
                if (!initialized || cancelled)
                    return true;
                if (nextShot < settings.Emission.ProjectileCount)
                    return false;
                for (int i = 0; i < projectiles.Count; i++)
                {
                    if (projectiles[i] != null &&
                        !projectiles[i].IsComplete)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public void Initialize(
            in SpellExecutionContext executionContext,
            ProjectileDeliverySettings deliverySettings,
            int seed)
        {
            context = executionContext;
            settings = deliverySettings;
            randomSeed = seed;
            initialized = settings != null;
            cancelled = false;
            nextShot = 0;
            remainingInterval = 0f;
            EmitNext();
            remainingInterval = settings != null
                ? settings.Emission.ShotInterval
                : 0f;
        }

        public void Cancel()
        {
            cancelled = true;
            for (int i = 0; i < projectiles.Count; i++)
            {
                if (projectiles[i] != null)
                    projectiles[i].Cancel();
            }
            if (Application.isPlaying)
                Destroy(gameObject);
        }

        private void Update()
        {
            if (!initialized || cancelled)
                return;

            if (nextShot < settings.Emission.ProjectileCount)
            {
                float delta = context.Spell.Timing.TimeMode ==
                              SpellTimeMode.Unscaled
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;
                remainingInterval -= Mathf.Max(0f, delta);
                while (remainingInterval <= 0f &&
                       nextShot < settings.Emission.ProjectileCount)
                {
                    EmitNext();
                    remainingInterval += settings.Emission.ShotInterval;
                    if (settings.Emission.ShotInterval <= 0f)
                        break;
                }
            }

            if (IsComplete && Application.isPlaying)
                Destroy(gameObject);
        }

        private void EmitNext()
        {
            if (!initialized ||
                nextShot >= settings.Emission.ProjectileCount)
            {
                return;
            }

            Vector2 baseDirection = context.Cast.AimDirection;
            if (settings.Emission.ReAimSequentialShots &&
                context.Cast.SelectedTarget != null)
            {
                baseDirection =
                    (Vector2)context.Cast.SelectedTarget.transform.position -
                    (Vector2)transform.position;
            }

            Vector2 shotDirection =
                ProjectileDeliveryDefinition.ResolveShotDirection(
                    baseDirection,
                    settings.Emission,
                    nextShot,
                    randomSeed);
            SpellProjectile2D projectile =
                ProjectileDeliveryDefinition.EmitShot(
                    context,
                    settings,
                    shotDirection,
                    nextShot);
            if (projectile != null)
                projectiles.Add(projectile);
            nextShot++;
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpellInstantRangedShape2D : MonoBehaviour
    {
        private const float VisualLifetime = 0.15f;
        private static readonly Color ShapeColor =
            new Color(0.25f, 0.9f, 1f, 0.9f);

        private SpellExecutionContext context;
        private readonly HashSet<int> contactedVolumes = new HashSet<int>();

        public static void Fire(
            in SpellExecutionContext executionContext,
            ProjectileDeliverySettings settings,
            Vector2 origin,
            Vector2 direction,
            int shotIndex)
        {
            var runtimeObject = new GameObject(
                $"{executionContext.Spell.DisplayName} Instant Shape");
            runtimeObject.transform.position = origin;
            SpellInstantRangedShape2D runtime =
                runtimeObject.AddComponent<SpellInstantRangedShape2D>();
            runtime.context = executionContext;
            runtime.FireInternal(settings, origin, direction);

            if (Application.isPlaying)
            {
                TimedSpellObject lifetime =
                    runtimeObject.AddComponent<TimedSpellObject>();
                lifetime.Initialize(
                    VisualLifetime,
                    executionContext.Spell.Timing.TimeMode);
            }
        }

        private void FireInternal(
            ProjectileDeliverySettings settings,
            Vector2 origin,
            Vector2 direction)
        {
            Vector2 aim = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector2.right;
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                origin,
                aim,
                this));

            if (settings.ShotShape.HitShape == ProjectileHitShape.Cone)
                FireCone(settings, origin, aim);
            else
                FireBeam(settings, origin, aim);

            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStopped,
                null,
                origin + aim * settings.Range,
                -aim,
                this));
        }

        private void FireBeam(
            ProjectileDeliverySettings settings,
            Vector2 origin,
            Vector2 aim)
        {
            RaycastHit2D[] hits = settings.ShotShape.BeamWidth > 0.0001f
                ? Physics2D.CircleCastAll(
                    origin,
                    settings.ShotShape.BeamWidth * 0.5f,
                    aim,
                    settings.Range,
                    settings.CollisionMask)
                : Physics2D.RaycastAll(
                    origin,
                    aim,
                    settings.Range,
                    settings.CollisionMask);
            System.Array.Sort(
                hits,
                (left, right) => left.distance.CompareTo(right.distance));

            var hitTargets = new HashSet<int>();
            int targetHits = 0;
            Vector2 end = origin + aim * settings.Range;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit2D hit = hits[i];
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

                bool valid = context.Spell.TargetFilter.IsValid(
                    context.Cast,
                    resolved,
                    hit.collider.gameObject);
                if (!valid)
                {
                    if (settings.StopOnBlockedCollider)
                    {
                        end = hit.point;
                        context.DispatchEvent(new SpellEventOccurrence(
                            SpellEventType.BlockingHit,
                            resolved,
                            hit.point,
                            hit.normal,
                            this));
                        break;
                    }
                    continue;
                }

                int targetId = SpellTargetResolver.GetTargetId(resolved);
                if (targetId == 0 || !hitTargets.Add(targetId))
                    continue;
                float potency = settings.Falloff.Evaluate(
                    hit.distance / settings.Range);
                context.ApplyEffects(
                    resolved,
                    hit.collider.gameObject,
                    hit.point,
                    hit.normal,
                    this,
                    potency);
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    resolved,
                    hit.point,
                    hit.normal,
                    this));
                targetHits++;
                if (!settings.PierceTargets ||
                    targetHits >= settings.MaximumTargetHits)
                {
                    end = hit.point;
                    break;
                }
            }

            SpellDeliveryInteractionService.EmitSegment(
                context,
                origin,
                end,
                settings.ShotShape.BeamWidth * 0.5f,
                DeliveryContactPhase.Impact,
                GetInstanceID(),
                contactedVolumes);
            AddBeamVisual(origin, end, settings);
        }

        private void FireCone(
            ProjectileDeliverySettings settings,
            Vector2 origin,
            Vector2 aim)
        {
            Collider2D[] overlaps = Physics2D.OverlapCircleAll(
                origin,
                settings.Range,
                settings.CollisionMask);
            var hitTargets = new HashSet<int>();
            int targetHits = 0;
            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider2D detected = overlaps[i];
                if (detected == null)
                    continue;
                GameObject resolved = SpellTargetResolver.Resolve(
                    detected.gameObject);
                if (resolved == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        resolved))
                {
                    continue;
                }

                Vector2 offset = (Vector2)detected.bounds.center - origin;
                if (offset.sqrMagnitude > 0.000001f &&
                    settings.ShotShape.ConeAngle < 359.9f &&
                    Vector2.Angle(aim, offset) >
                    settings.ShotShape.ConeAngle * 0.5f)
                {
                    continue;
                }
                if (!context.Spell.TargetFilter.IsValid(
                        context.Cast,
                        resolved,
                        detected.gameObject))
                {
                    continue;
                }
                if (settings.StopOnBlockedCollider &&
                    !HasClearPath(
                        origin,
                        detected,
                        settings.CollisionMask))
                {
                    continue;
                }

                int targetId = SpellTargetResolver.GetTargetId(resolved);
                if (targetId == 0 || !hitTargets.Add(targetId))
                    continue;
                float distance = offset.magnitude;
                context.ApplyEffects(
                    resolved,
                    detected.gameObject,
                    detected.ClosestPoint(origin),
                    -offset.normalized,
                    this,
                    settings.Falloff.Evaluate(
                        distance / settings.Range));
                context.DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    resolved,
                    detected.ClosestPoint(origin),
                    -offset.normalized,
                    this));
                targetHits++;
                if (targetHits >= settings.MaximumTargetHits)
                    break;
            }

            SpellDeliveryInteractionService.EmitArc(
                context,
                origin,
                aim,
                settings.Range,
                settings.ShotShape.ConeAngle,
                DeliveryContactPhase.Impact,
                GetInstanceID());
            AddConeVisual(origin, aim, settings);
        }

        private bool HasClearPath(
            Vector2 origin,
            Collider2D intendedTarget,
            LayerMask mask)
        {
            Vector2 destination = intendedTarget.bounds.center;
            Vector2 offset = destination - origin;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return true;

            RaycastHit2D[] hits = Physics2D.RaycastAll(
                origin,
                offset / distance,
                distance,
                mask);
            System.Array.Sort(
                hits,
                (left, right) => left.distance.CompareTo(right.distance));
            GameObject intended = SpellTargetResolver.Resolve(
                intendedTarget.gameObject);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i].collider;
                if (hit == null ||
                    SpellTargetResolver.IsSameHierarchy(
                        context.Cast.Caster,
                        hit.gameObject))
                {
                    continue;
                }
                return SpellTargetResolver.IsSameHierarchy(
                    intended,
                    hit.gameObject);
            }
            return true;
        }

        private void AddBeamVisual(
            Vector2 start,
            Vector2 end,
            ProjectileDeliverySettings settings)
        {
            LineRenderer line = SpellDeliveryVisualUtility.CreateLine(
                gameObject,
                ShapeColor,
                Mathf.Max(0.025f, settings.ShotShape.BeamWidth),
                150);
            SpellDeliveryVisualUtility.SetSegment(line, start, end);
            MatchCasterSorting(line);
        }

        private void AddConeVisual(
            Vector2 origin,
            Vector2 aim,
            ProjectileDeliverySettings settings)
        {
            float angle = settings.ShotShape.ConeAngle;
            bool fullCircle = angle >= 359.9f;
            LineRenderer line = SpellDeliveryVisualUtility.CreateLine(
                gameObject,
                ShapeColor,
                Mathf.Max(0.035f, settings.CollisionRadius * 0.5f),
                150,
                fullCircle);
            MatchCasterSorting(line);

            if (fullCircle)
            {
                SpellDeliveryVisualUtility.SetCircle(
                    line,
                    origin,
                    settings.Range,
                    72);
                return;
            }

            int arcSegments = Mathf.Clamp(
                Mathf.CeilToInt(angle / 5f),
                6,
                72);
            line.loop = false;
            line.positionCount = arcSegments + 3;
            line.SetPosition(0, origin);
            for (int i = 0; i <= arcSegments; i++)
            {
                float offset = Mathf.Lerp(
                    -angle * 0.5f,
                    angle * 0.5f,
                    i / (float)arcSegments);
                Vector2 edgeDirection =
                    Quaternion.Euler(0f, 0f, offset) * aim;
                line.SetPosition(
                    i + 1,
                    origin + edgeDirection * settings.Range);
            }
            line.SetPosition(line.positionCount - 1, origin);
        }

        private void MatchCasterSorting(LineRenderer line)
        {
            Renderer casterRenderer = context.Cast.Caster != null
                ? context.Cast.Caster.GetComponentInChildren<Renderer>(true)
                : null;
            if (casterRenderer != null)
            {
                line.sortingLayerID = casterRenderer.sortingLayerID;
                line.sortingOrder = casterRenderer.sortingOrder + 50;
            }
        }
    }
}
