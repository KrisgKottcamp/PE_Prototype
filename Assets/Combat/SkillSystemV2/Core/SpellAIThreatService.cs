using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellAIThreatEvaluation
    {
        public int ThreatId { get; }
        public SpellDefinition Spell { get; }
        public GameObject Caster { get; }
        public SpellDeliveryGeometry Geometry { get; }
        public SpellAIReaction SuggestedReactions { get; }
        public float Urgency { get; }
        public float Score { get; }
        public float TimeToImpact { get; }
        public float Clearance { get; }
        public Vector2 AvoidanceDirection { get; }
        public Vector2 TravelDirection { get; }
        public bool IsInside { get; }
        public bool IsTelegraph { get; }
        public bool IsTrap { get; }

        public SpellAIThreatEvaluation(
            int threatId,
            SpellDefinition spell,
            GameObject caster,
            in SpellDeliveryGeometry geometry,
            SpellAIReaction suggestedReactions,
            float urgency,
            float score,
            float timeToImpact,
            float clearance,
            Vector2 avoidanceDirection,
            Vector2 travelDirection,
            bool isInside,
            bool isTelegraph,
            bool isTrap)
        {
            ThreatId = threatId;
            Spell = spell;
            Caster = caster;
            Geometry = geometry;
            SuggestedReactions = suggestedReactions;
            Urgency = Mathf.Clamp01(urgency);
            Score = Mathf.Clamp01(score);
            TimeToImpact = Mathf.Max(0f, timeToImpact);
            Clearance = clearance;
            AvoidanceDirection = avoidanceDirection.sqrMagnitude > 0.000001f
                ? avoidanceDirection.normalized
                : Vector2.zero;
            TravelDirection = travelDirection.sqrMagnitude > 0.000001f
                ? travelDirection.normalized
                : Vector2.zero;
            IsInside = isInside;
            IsTelegraph = isTelegraph;
            IsTrap = isTrap;
        }
    }

    /// <summary>
    /// Generic, read-only danger registry built from the same delivery events
    /// used by recipes and diagnostics. Enemy brains query it, but the service
    /// has no dependency on a particular AI implementation or named spell.
    /// </summary>
    public static class SpellAIThreatService
    {
        private sealed class TrackedThreat
        {
            public int Id;
            public int RuntimeId;
            public SpellDefinition Spell;
            public CastContext Cast;
            public GameObject Caster;
            public Component Runtime;
            public SpellDeliveryGeometry Geometry;
            public bool IsTelegraph;
            public float RegisteredAt;
            public float PerceivableAt;
            public float ActivatesAt;
            public float ExpiresAt;
            public Vector2 Velocity;
            public Vector2 LastCenter;
            public float LastSampleAt;
        }

        private static readonly List<TrackedThreat> threats =
            new List<TrackedThreat>(64);
        private static readonly List<SpellAIThreatEvaluation> queryBuffer =
            new List<SpellAIThreatEvaluation>(32);
        private static int nextThreatId = 1;

        public static int ActiveThreatCount
        {
            get
            {
                Prune();
                return threats.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearAll()
        {
            threats.Clear();
            queryBuffer.Clear();
            nextThreatId = 1;
        }

        public static void ReportDeliveryEvent(
            in SpellExecutionContext execution,
            in SpellEventOccurrence occurrence)
        {
            if (execution.SuppressGameplayEffects || execution.Spell == null ||
                !IsPotentiallyDangerous(execution.Spell))
            {
                return;
            }

            Prune();
            if (occurrence.Type == SpellEventType.CastStarted)
            {
                RegisterTelegraph(execution);
                return;
            }

            if (IsTerminalEvent(occurrence.Type))
            {
                RemoveRuntimeThreat(occurrence.DeliveryRuntime);
                return;
            }

            if (occurrence.Type == SpellEventType.DeliveryStarted)
            {
                RegisterRuntimeThreat(execution, occurrence);
                return;
            }

            UpdateRuntimeThreat(execution, occurrence);
        }

        public static void CancelCast(
            SpellDefinition spell,
            in CastContext cast)
        {
            if (spell == null)
                return;

            for (int i = threats.Count - 1; i >= 0; i--)
            {
                TrackedThreat threat = threats[i];
                if (threat.IsTelegraph && threat.Spell == spell &&
                    SameCast(threat.Cast, cast))
                {
                    threats.RemoveAt(i);
                }
            }
        }

        public static bool TryFindMostRelevantThreat(
            GameObject observer,
            Vector2 observerPosition,
            Vector2 plannedVelocity,
            float perceptionRadius,
            float observerRadius,
            float anticipationSeconds,
            out SpellAIThreatEvaluation best)
        {
            best = default;
            queryBuffer.Clear();
            CollectRelevantThreats(
                observer,
                observerPosition,
                plannedVelocity,
                perceptionRadius,
                observerRadius,
                anticipationSeconds,
                queryBuffer);
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < queryBuffer.Count; i++)
            {
                SpellAIThreatEvaluation candidate = queryBuffer[i];
                if (candidate.Score <= bestScore)
                    continue;
                bestScore = candidate.Score;
                best = candidate;
            }

            return !float.IsNegativeInfinity(bestScore);
        }

        public static int CollectRelevantThreats(
            GameObject observer,
            Vector2 observerPosition,
            Vector2 plannedVelocity,
            float perceptionRadius,
            float observerRadius,
            float anticipationSeconds,
            List<SpellAIThreatEvaluation> output)
        {
            output?.Clear();
            if (observer == null || output == null)
                return 0;

            Prune();
            float now = Time.time;
            float horizon = Mathf.Max(0.05f, anticipationSeconds);
            float awareness = Mathf.Max(0.1f, perceptionRadius);
            for (int i = 0; i < threats.Count; i++)
            {
                TrackedThreat threat = threats[i];
                if (now < threat.PerceivableAt ||
                    !CanThreatenObserver(threat, observer))
                    continue;

                RefreshMotion(threat, now);
                SpellDeliveryGeometry geometry = threat.Geometry.Snapshot();
                if (!TryEvaluateGeometry(
                        threat,
                        geometry,
                        observerPosition,
                        plannedVelocity,
                        Mathf.Max(0f, observerRadius),
                        awareness,
                        horizon,
                        now,
                        out float score,
                        out float timeToImpact,
                        out float clearance,
                        out Vector2 avoidance,
                        out bool inside))
                {
                    continue;
                }

                Vector2 travel = threat.Velocity.sqrMagnitude > 0.0001f
                    ? threat.Velocity.normalized
                    : geometry.Direction;
                SpellAIAffordance guidance = threat.Spell.AIAffordance;
                output.Add(new SpellAIThreatEvaluation(
                    threat.Id,
                    threat.Spell,
                    threat.Caster,
                    geometry,
                    guidance.SuggestedReactions,
                    guidance.ReactionUrgency,
                    score,
                    timeToImpact,
                    clearance,
                    avoidance,
                    travel,
                    inside,
                    threat.IsTelegraph,
                    IsTrapDelivery(threat.Spell)));
            }
            return output.Count;
        }

        public static bool IsThreatActive(int threatId)
        {
            Prune();
            for (int i = 0; i < threats.Count; i++)
            {
                if (threats[i].Id == threatId)
                    return true;
            }
            return false;
        }

        public static float SignedClearance(
            in SpellDeliveryGeometry geometry,
            Vector2 point,
            float bodyRadius,
            out Vector2 awayDirection)
        {
            float padding = Mathf.Max(0f, bodyRadius);
            switch (geometry.Shape)
            {
                case SpellDeliveryGeometryShape.Segment:
                {
                    Vector2 nearest = ClosestPointOnSegment(
                        geometry.Start,
                        geometry.End,
                        point);
                    Vector2 away = point - nearest;
                    awayDirection = SafeDirection(
                        away,
                        Perpendicular(geometry.Direction));
                    return away.magnitude - geometry.HalfWidth - padding;
                }

                case SpellDeliveryGeometryShape.Arc:
                    return ArcClearance(
                        geometry,
                        point,
                        padding,
                        out awayDirection);

                default:
                {
                    Vector2 away = point - geometry.Center;
                    awayDirection = SafeDirection(away, Vector2.right);
                    return away.magnitude - geometry.Radius - padding;
                }
            }
        }

        private static void RegisterTelegraph(
            in SpellExecutionContext execution)
        {
            CancelCast(execution.Spell, execution.Cast);
            SpellDeliveryGeometry geometry = InferGeometry(
                execution.Spell,
                execution.Cast,
                null,
                default,
                false);
            float now = Time.time;
            float activationDelay = EstimateActivationDelay(
                execution.Spell,
                isRuntime: false);
            float telegraphWindow = Mathf.Min(
                activationDelay,
                execution.Spell.AIAffordance.TelegraphDuration);
            if (telegraphWindow <= 0.001f)
                return;

            threats.Add(new TrackedThreat
            {
                Id = AllocateThreatId(),
                RuntimeId = 0,
                Spell = execution.Spell,
                Cast = execution.Cast,
                Caster = execution.Cast.Caster,
                Geometry = geometry,
                IsTelegraph = true,
                RegisteredAt = now,
                PerceivableAt = now + activationDelay - telegraphWindow,
                ActivatesAt = now + activationDelay,
                ExpiresAt = now + activationDelay + 0.35f,
                Velocity = Vector2.zero,
                LastCenter = geometry.Snapshot().BoundingCenter,
                LastSampleAt = now
            });
        }

        private static void RegisterRuntimeThreat(
            in SpellExecutionContext execution,
            in SpellEventOccurrence occurrence)
        {
            TrackedThreat telegraph = FindAndRemoveTelegraph(execution);
            int runtimeId = occurrence.DeliveryRuntime != null
                ? occurrence.DeliveryRuntime.GetInstanceID()
                : 0;
            if (runtimeId != 0)
                RemoveRuntimeThreat(occurrence.DeliveryRuntime);

            SpellDeliveryGeometry geometry = InferGeometry(
                execution.Spell,
                execution.Cast,
                occurrence.DeliveryRuntime,
                occurrence.Geometry,
                occurrence.HasGeometry);
            float now = Time.time;
            float activatesAt = telegraph != null
                ? telegraph.ActivatesAt
                : now + EstimateActivationDelay(
                    execution.Spell,
                    isRuntime: true);
            float lifetime = EstimateRuntimeLifetime(execution.Spell);
            if (lifetime <= 0f)
                return;

            Vector2 initialVelocity = InferInitialVelocity(
                execution.Spell,
                occurrence.Normal);
            threats.Add(new TrackedThreat
            {
                Id = telegraph != null
                    ? telegraph.Id
                    : AllocateThreatId(),
                RuntimeId = runtimeId,
                Spell = execution.Spell,
                Cast = execution.Cast,
                Caster = execution.Cast.Caster,
                Runtime = occurrence.DeliveryRuntime,
                Geometry = geometry,
                IsTelegraph = false,
                RegisteredAt = telegraph != null
                    ? telegraph.RegisteredAt
                    : now,
                PerceivableAt = now,
                ActivatesAt = activatesAt,
                ExpiresAt = float.IsPositiveInfinity(lifetime)
                    ? float.PositiveInfinity
                    : now + lifetime,
                Velocity = initialVelocity,
                LastCenter = geometry.Snapshot().BoundingCenter,
                LastSampleAt = now
            });
        }

        private static void UpdateRuntimeThreat(
            in SpellExecutionContext execution,
            in SpellEventOccurrence occurrence)
        {
            int runtimeId = occurrence.DeliveryRuntime != null
                ? occurrence.DeliveryRuntime.GetInstanceID()
                : 0;
            TrackedThreat threat = FindRuntimeThreat(runtimeId);
            if (threat == null)
                return;

            if (occurrence.HasGeometry)
            {
                threat.Geometry = InferGeometry(
                    execution.Spell,
                    execution.Cast,
                    occurrence.DeliveryRuntime,
                    occurrence.Geometry,
                    true);
            }
            if (occurrence.Type == SpellEventType.Armed)
                threat.ActivatesAt = Time.time;
            else if (occurrence.Type == SpellEventType.ProximityTriggered &&
                     execution.Spell.DeliverySettings is
                         ProximityMineDeliverySettings mine)
            {
                threat.ActivatesAt = Time.time + mine.DetonationDelay;
            }
        }

        private static bool TryEvaluateGeometry(
            TrackedThreat threat,
            in SpellDeliveryGeometry baseGeometry,
            Vector2 observerPosition,
            Vector2 observerVelocity,
            float observerRadius,
            float perceptionRadius,
            float horizon,
            float now,
            out float score,
            out float timeToImpact,
            out float clearance,
            out Vector2 avoidance,
            out bool inside)
        {
            float activationDelay = Mathf.Max(0f, threat.ActivatesAt - now);
            float currentClearance = SignedClearance(
                baseGeometry,
                observerPosition,
                observerRadius,
                out Vector2 currentAway);
            inside = activationDelay <= 0.001f && currentClearance <= 0f;
            float minimumClearance = currentClearance;
            float minimumTime = 0f;
            Vector2 minimumAway = currentAway;
            timeToImpact = inside ? 0f : float.PositiveInfinity;

            if (activationDelay <= horizon)
            {
                const int sampleCount = 12;
                float start = Mathf.Max(0f, activationDelay);
                for (int sample = 0; sample <= sampleCount; sample++)
                {
                    float t = Mathf.Lerp(
                        start,
                        horizon,
                        sample / (float)sampleCount);
                    SpellDeliveryGeometry moved = Translate(
                        baseGeometry,
                        threat.Velocity * t);
                    Vector2 observerPoint = observerPosition +
                                            observerVelocity * t;
                    float sampleClearance = SignedClearance(
                        moved,
                        observerPoint,
                        observerRadius,
                        out Vector2 sampleAway);
                    if (sampleClearance < minimumClearance)
                    {
                        minimumClearance = sampleClearance;
                        minimumTime = t;
                        minimumAway = sampleAway;
                    }
                    if (sampleClearance <= 0f &&
                        float.IsPositiveInfinity(timeToImpact))
                    {
                        timeToImpact = t;
                    }
                }

                EvaluateClosestApproach(
                    threat,
                    baseGeometry,
                    observerPosition,
                    observerVelocity,
                    observerRadius,
                    start,
                    horizon,
                    ref minimumClearance,
                    ref minimumTime,
                    ref minimumAway,
                    ref timeToImpact);
            }

            clearance = minimumClearance;
            avoidance = minimumAway;
            float awarenessClearance = Mathf.Max(
                0.25f,
                perceptionRadius);
            if (minimumClearance > awarenessClearance ||
                activationDelay > horizon)
            {
                score = 0f;
                return false;
            }

            float proximity = 1f - Mathf.Clamp01(
                Mathf.Max(0f, minimumClearance) / awarenessClearance);
            float imminence = float.IsPositiveInfinity(timeToImpact)
                ? proximity * 0.35f
                : 1f - Mathf.Clamp01(timeToImpact / horizon);
            float urgency = threat.Spell.AIAffordance.ReactionUrgency;
            float exposure = Mathf.Max(proximity, imminence);
            score = Mathf.Clamp01(
                urgency * Mathf.Lerp(0.15f, 1f, exposure));
            if (inside)
                score = Mathf.Max(score, urgency);
            return score > 0.01f;
        }

        private static void EvaluateClosestApproach(
            TrackedThreat threat,
            in SpellDeliveryGeometry geometry,
            Vector2 observerPosition,
            Vector2 observerVelocity,
            float observerRadius,
            float start,
            float horizon,
            ref float minimumClearance,
            ref float minimumTime,
            ref Vector2 minimumAway,
            ref float timeToImpact)
        {
            if (geometry.Shape != SpellDeliveryGeometryShape.Circle &&
                geometry.Shape != SpellDeliveryGeometryShape.Point)
            {
                return;
            }

            Vector2 relativePosition = geometry.Center - observerPosition;
            Vector2 relativeVelocity = threat.Velocity - observerVelocity;
            float speedSquared = relativeVelocity.sqrMagnitude;
            if (speedSquared <= 0.0001f)
                return;

            float t = Mathf.Clamp(
                -Vector2.Dot(relativePosition, relativeVelocity) /
                speedSquared,
                start,
                horizon);
            SpellDeliveryGeometry moved = Translate(
                geometry,
                threat.Velocity * t);
            Vector2 observerPoint = observerPosition + observerVelocity * t;
            float candidate = SignedClearance(
                moved,
                observerPoint,
                observerRadius,
                out Vector2 away);
            if (candidate < minimumClearance)
            {
                minimumClearance = candidate;
                minimumTime = t;
                minimumAway = away;
            }
            if (candidate <= 0f &&
                (float.IsPositiveInfinity(timeToImpact) ||
                 t < timeToImpact))
            {
                timeToImpact = t;
            }
        }

        private static bool CanThreatenObserver(
            TrackedThreat threat,
            GameObject observer)
        {
            if (threat == null || threat.Spell == null || observer == null ||
                SpellTargetResolver.IsSameHierarchy(threat.Caster, observer))
            {
                return false;
            }

            if (threat.Spell.TargetFilter.IsValid(threat.Cast, observer))
                return true;

            // Child-collider fallback is only needed when the authored layer
            // mask may intentionally live on hurtbox children. Relationship
            // or target-marker rejection cannot become valid by scanning the
            // same hierarchy again, so avoid that per-query allocation.
            if (!threat.Spell.TargetFilter.UsesLayerMask)
                return false;

            Collider2D[] colliders =
                observer.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider != null && threat.Spell.TargetFilter.IsValid(
                        threat.Cast,
                        observer,
                        collider.gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPotentiallyDangerous(SpellDefinition spell)
        {
            if (spell == null || spell.AIAffordance == null ||
                spell.AIAffordance.SuggestedReactions == SpellAIReaction.None ||
                spell.AIAffordance.ReactionUrgency <= 0f)
            {
                return false;
            }

            SpellAIIntent dangerous = SpellAIIntent.Damage |
                                      SpellAIIntent.Control |
                                      SpellAIIntent.Setup |
                                      SpellAIIntent.Execute;
            return (spell.AIAffordance.Intents & dangerous) != 0 ||
                   spell.AIAffordance.DangerRadius > 0f;
        }

        private static bool IsTrapDelivery(SpellDefinition spell)
        {
            return spell != null &&
                   (spell.DeliverySettings is
                        ProximityMineDeliverySettings ||
                    spell.DeliverySettings is TripWireDeliverySettings);
        }

        private static SpellDeliveryGeometry InferGeometry(
            SpellDefinition spell,
            in CastContext cast,
            Component runtime,
            in SpellDeliveryGeometry reported,
            bool hasReported)
        {
            Vector2 origin = cast.Origin;
            Vector2 direction = cast.HasAimDirection
                ? cast.AimDirection
                : cast.HasTargetPoint
                    ? (cast.TargetPoint - origin).normalized
                    : Vector2.up;
            Vector2 point = cast.HasTargetPoint
                ? cast.TargetPoint
                : origin;
            SpellDeliverySettings settings = spell.DeliverySettings;
            SpellDeliveryGeometry geometry;

            if (settings is ProjectileDeliverySettings projectile)
            {
                if (runtime != null && hasReported &&
                    projectile.ShotShape.HitShape ==
                        ProjectileHitShape.Projectile)
                {
                    geometry = reported;
                }
                else
                {
                    geometry = ProjectileGeometry(
                        origin,
                        direction,
                        projectile);
                }
            }
            else if (settings is RicochetProjectileDeliverySettings ricochet)
            {
                geometry = runtime != null && hasReported
                    ? reported
                    : SpellDeliveryGeometry.Segment(
                        origin,
                        origin + direction * ricochet.Range,
                        ricochet.CollisionRadius);
            }
            else if (settings is GrenadeDeliverySettings grenade)
            {
                geometry = runtime != null
                    ? SpellDeliveryGeometry.FollowCircle(
                        runtime.transform,
                        grenade.ExplosionRadius)
                    : SpellDeliveryGeometry.Circle(
                        point,
                        grenade.ExplosionRadius);
            }
            else if (settings is LingeringAreaDeliverySettings lingering)
            {
                geometry = runtime != null
                    ? SpellDeliveryGeometry.FollowCircle(
                        runtime.transform,
                        lingering.Radius)
                    : SpellDeliveryGeometry.Circle(point, lingering.Radius);
            }
            else if (settings is AreaDeliverySettings area)
            {
                geometry = SpellDeliveryGeometry.Circle(point, area.Radius);
            }
            else if (settings is ProximityMineDeliverySettings mine)
            {
                float radius = Mathf.Max(
                    mine.TriggerRadius,
                    mine.EffectRadius);
                geometry = runtime != null
                    ? SpellDeliveryGeometry.FollowCircle(
                        runtime.transform,
                        radius)
                    : SpellDeliveryGeometry.Circle(point, radius);
            }
            else if (settings is TripWireDeliverySettings wire)
            {
                Vector2 first = cast.TargetingPayload != null
                    ? cast.TargetingPayload.GetPointOrDefault(0, origin)
                    : origin;
                Vector2 second = cast.TargetingPayload != null
                    ? cast.TargetingPayload.GetPointOrDefault(1, point)
                    : point;
                geometry = SpellDeliveryGeometry.Segment(
                    first,
                    second,
                    wire.TriggerWidth);
            }
            else if (settings is MeleeArcDeliverySettings melee)
            {
                geometry = SpellDeliveryGeometry.Arc(
                    origin,
                    direction,
                    melee.Range,
                    melee.ArcAngle);
            }
            else if (hasReported)
            {
                geometry = reported;
            }
            else
            {
                geometry = SpellDeliveryGeometry.Circle(
                    point,
                    spell.AIAffordance.DangerRadius);
            }

            float overrideSize = spell.AIAffordance.DangerRadius;
            return overrideSize > 0f
                ? geometry.WithSizeOverride(overrideSize)
                : geometry;
        }

        private static SpellDeliveryGeometry ProjectileGeometry(
            Vector2 origin,
            Vector2 direction,
            ProjectileDeliverySettings projectile)
        {
            ProjectileHitShape hitShape = projectile.ShotShape.HitShape;
            if (hitShape == ProjectileHitShape.Cone)
            {
                return SpellDeliveryGeometry.Arc(
                    origin,
                    direction,
                    projectile.Range,
                    projectile.ShotShape.ConeAngle);
            }
            if (hitShape == ProjectileHitShape.InstantBeam)
            {
                return SpellDeliveryGeometry.Segment(
                    origin,
                    origin + direction * projectile.Range,
                    projectile.ShotShape.BeamWidth * 0.5f);
            }

            ProjectileEmissionSettings emission = projectile.Emission;
            if (emission.Pattern == ProjectileEmissionPattern.Ring)
            {
                return SpellDeliveryGeometry.Arc(
                    origin,
                    direction,
                    projectile.Range,
                    360f);
            }
            if (emission.Pattern == ProjectileEmissionPattern.Fan ||
                emission.Pattern == ProjectileEmissionPattern.RandomCone)
            {
                return SpellDeliveryGeometry.Arc(
                    origin,
                    direction,
                    projectile.Range,
                    Mathf.Max(1f, emission.SpreadAngle));
            }
            return SpellDeliveryGeometry.Segment(
                origin,
                origin + direction * projectile.Range,
                projectile.CollisionRadius);
        }

        private static float EstimateActivationDelay(
            SpellDefinition spell,
            bool isRuntime)
        {
            float delay = isRuntime ? 0f : spell.Timing.BuildUpDuration;
            if (spell.DeliverySettings is GrenadeDeliverySettings grenade)
                delay += grenade.FuseDuration;
            else if (spell.DeliverySettings is
                     ProximityMineDeliverySettings mine)
                delay += mine.ArmingDelay;
            return Mathf.Max(0f, delay);
        }

        private static float EstimateRuntimeLifetime(SpellDefinition spell)
        {
            if (spell.DeliverySettings is ProjectileDeliverySettings projectile)
            {
                return projectile.ShotShape.HitShape ==
                       ProjectileHitShape.Projectile
                    ? projectile.Range / projectile.Speed + 0.25f
                    : 0f;
            }
            if (spell.DeliverySettings is
                RicochetProjectileDeliverySettings ricochet)
                return ricochet.Range / ricochet.Speed + 1f;
            if (spell.DeliverySettings is GrenadeDeliverySettings grenade)
                return grenade.FuseDuration + 0.25f;
            if (spell.DeliverySettings is LingeringAreaDeliverySettings area)
                return area.Duration + 0.25f;
            if (spell.DeliverySettings is ProximityMineDeliverySettings mine)
                return mine.Lifetime > 0f
                    ? mine.Lifetime + 0.25f
                    : float.PositiveInfinity;
            if (spell.DeliverySettings is TripWireDeliverySettings wire)
                return wire.Duration > 0f
                    ? wire.Duration + 0.25f
                    : float.PositiveInfinity;
            if (spell.DeliverySettings is AreaDeliverySettings ||
                spell.DeliverySettings is MeleeArcDeliverySettings ||
                spell.DeliverySettings is PointClickDeliverySettings ||
                spell.DeliverySettings is SelfDeliverySettings ||
                spell.DeliverySettings is InstantTargetDeliverySettings)
            {
                return 0f;
            }
            return 0.35f;
        }

        private static Vector2 InferInitialVelocity(
            SpellDefinition spell,
            Vector2 eventDirection)
        {
            Vector2 direction = eventDirection.sqrMagnitude > 0.000001f
                ? eventDirection.normalized
                : Vector2.zero;
            if (spell.DeliverySettings is ProjectileDeliverySettings projectile)
                return direction * projectile.Speed;
            if (spell.DeliverySettings is
                RicochetProjectileDeliverySettings ricochet)
                return direction * ricochet.Speed;
            if (spell.DeliverySettings is GrenadeDeliverySettings grenade)
                return direction * grenade.Speed;
            return Vector2.zero;
        }

        private static void RefreshMotion(TrackedThreat threat, float now)
        {
            SpellDeliveryGeometry snapshot = threat.Geometry.Snapshot();
            Vector2 center = snapshot.BoundingCenter;
            float elapsed = now - threat.LastSampleAt;
            if (elapsed > 0.0001f && threat.Runtime != null)
            {
                Vector2 measured = (center - threat.LastCenter) / elapsed;
                if (measured.sqrMagnitude > 0.0001f)
                {
                    threat.Velocity = Vector2.Lerp(
                        threat.Velocity,
                        Vector2.ClampMagnitude(measured, 50f),
                        0.65f);
                }
            }
            threat.LastCenter = center;
            threat.LastSampleAt = now;
        }

        private static SpellDeliveryGeometry Translate(
            in SpellDeliveryGeometry geometry,
            Vector2 delta)
        {
            switch (geometry.Shape)
            {
                case SpellDeliveryGeometryShape.Segment:
                    return SpellDeliveryGeometry.Segment(
                        geometry.Start + delta,
                        geometry.End + delta,
                        geometry.HalfWidth);
                case SpellDeliveryGeometryShape.Arc:
                    return SpellDeliveryGeometry.Arc(
                        geometry.Center + delta,
                        geometry.Direction,
                        geometry.Radius,
                        geometry.ArcAngle);
                case SpellDeliveryGeometryShape.Point:
                    return SpellDeliveryGeometry.Point(
                        geometry.Center + delta);
                default:
                    return SpellDeliveryGeometry.Circle(
                        geometry.Center + delta,
                        geometry.Radius);
            }
        }

        private static float ArcClearance(
            in SpellDeliveryGeometry geometry,
            Vector2 point,
            float padding,
            out Vector2 awayDirection)
        {
            Vector2 offset = point - geometry.Center;
            float distance = offset.magnitude;
            Vector2 radial = SafeDirection(offset, -geometry.Direction);
            float halfAngle = geometry.ArcAngle * 0.5f;
            float angle = Vector2.Angle(geometry.Direction, radial);
            if (geometry.ArcAngle >= 359.9f || angle <= halfAngle)
            {
                awayDirection = radial;
                if (distance <= geometry.Radius)
                {
                    float radialExit = geometry.Radius - distance;
                    float backExit = distance;
                    if (backExit < radialExit)
                    {
                        awayDirection = -radial;
                        return -backExit - padding;
                    }
                    return -radialExit - padding;
                }
                return distance - geometry.Radius - padding;
            }

            Vector2 left = Rotate(
                geometry.Direction,
                halfAngle) * geometry.Radius + geometry.Center;
            Vector2 right = Rotate(
                geometry.Direction,
                -halfAngle) * geometry.Radius + geometry.Center;
            Vector2 nearestLeft = ClosestPointOnSegment(
                geometry.Center,
                left,
                point);
            Vector2 nearestRight = ClosestPointOnSegment(
                geometry.Center,
                right,
                point);
            Vector2 nearest = (point - nearestLeft).sqrMagnitude <=
                              (point - nearestRight).sqrMagnitude
                ? nearestLeft
                : nearestRight;
            Vector2 away = point - nearest;
            awayDirection = SafeDirection(away, Perpendicular(radial));
            return away.magnitude - padding;
        }

        private static TrackedThreat FindAndRemoveTelegraph(
            in SpellExecutionContext execution)
        {
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                TrackedThreat threat = threats[i];
                if (!threat.IsTelegraph || threat.Spell != execution.Spell ||
                    !SameCast(threat.Cast, execution.Cast))
                {
                    continue;
                }
                threats.RemoveAt(i);
                return threat;
            }
            return null;
        }

        private static TrackedThreat FindRuntimeThreat(int runtimeId)
        {
            if (runtimeId == 0)
                return null;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                if (threats[i].RuntimeId == runtimeId)
                    return threats[i];
            }
            return null;
        }

        private static void RemoveRuntimeThreat(Component runtime)
        {
            if (runtime == null)
                return;
            int runtimeId = runtime.GetInstanceID();
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                if (threats[i].RuntimeId == runtimeId)
                    threats.RemoveAt(i);
            }
        }

        private static bool SameCast(
            in CastContext first,
            in CastContext second)
        {
            if (first.RootCastId != 0L && second.RootCastId != 0L)
                return first.RootCastId == second.RootCastId;
            return first.Caster == second.Caster &&
                   (first.Origin - second.Origin).sqrMagnitude <= 0.0001f;
        }

        private static bool IsTerminalEvent(SpellEventType type)
        {
            return type == SpellEventType.DeliveryStopped ||
                   type == SpellEventType.DeliveryExpired ||
                   type == SpellEventType.Detonated;
        }

        private static void Prune()
        {
            float now = Time.time;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                TrackedThreat threat = threats[i];
                if (threat == null)
                {
                    threats.RemoveAt(i);
                    continue;
                }
                bool runtimeDestroyed = !threat.IsTelegraph &&
                                        threat.RuntimeId != 0 &&
                                        threat.Runtime == null;
                if (threat.Spell == null ||
                    threat.Caster == null || runtimeDestroyed ||
                    now >= threat.ExpiresAt)
                {
                    threats.RemoveAt(i);
                }
            }
        }

        private static int AllocateThreatId()
        {
            if (nextThreatId == int.MaxValue)
                nextThreatId = 1;
            return nextThreatId++;
        }

        private static Vector2 ClosestPointOnSegment(
            Vector2 start,
            Vector2 end,
            Vector2 point)
        {
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.000001f)
                return start;
            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / denominator);
            return start + segment * t;
        }

        private static Vector2 SafeDirection(
            Vector2 value,
            Vector2 fallback)
        {
            if (value.sqrMagnitude > 0.000001f)
                return value.normalized;
            return fallback.sqrMagnitude > 0.000001f
                ? fallback.normalized
                : Vector2.right;
        }

        private static Vector2 Perpendicular(Vector2 direction)
        {
            Vector2 safe = SafeDirection(direction, Vector2.up);
            return new Vector2(-safe.y, safe.x);
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                direction.x * cosine - direction.y * sine,
                direction.x * sine + direction.y * cosine);
        }
    }
}
