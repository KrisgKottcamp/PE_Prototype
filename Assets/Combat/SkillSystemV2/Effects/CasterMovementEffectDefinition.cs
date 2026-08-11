using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum CasterMovementDestinationSource
    {
        AimedPoint,
        DeliveryEventPoint
    }

    [Serializable]
    public sealed class CasterMovementEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        [Tooltip("Choose the aimed point for a normal dash/blink, or the point reported by an Event Effect Recipe for impact movement.")]
        private CasterMovementDestinationSource destinationSource =
            CasterMovementDestinationSource.AimedPoint;

        [SerializeField, Min(0.01f)]
        [Tooltip("World units per second when movement is not instantaneous.")]
        private float speed = 14f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Maximum distance from the caster to the resolved destination.")]
        private float maximumDistance = 4f;

        [SerializeField]
        [Tooltip("Move to the destination immediately. Speed is ignored.")]
        private bool instantaneous;

        [SerializeField]
        [Tooltip("Reject the cast when the caster's collision shape cannot reach the destination without crossing a blocking collider.")]
        private bool requireLineOfSight = true;

        [SerializeField]
        [Tooltip("Layers that block movement when Require Line Of Sight is enabled.")]
        private LayerMask obstructionMask = ~0;

        [SerializeField]
        [Tooltip("At a delivery event point, offset the destination by the caster's collider size so the caster remains outside the surface that was hit.")]
        private bool keepOutsideHitSurface = true;

        [SerializeField, Min(0f)]
        [Tooltip("Additional space left between the caster and an impacted surface.")]
        private float extraSurfaceClearance = 0.05f;

        public CasterMovementDestinationSource DestinationSource =>
            destinationSource;
        public float Speed => Mathf.Max(0.01f, speed);
        public float MaximumDistance => Mathf.Max(0.01f, maximumDistance);
        public bool Instantaneous => instantaneous;
        public bool RequireLineOfSight => requireLineOfSight;
        public LayerMask ObstructionMask => obstructionMask;
        public bool KeepOutsideHitSurface => keepOutsideHitSurface;
        public float ExtraSurfaceClearance =>
            Mathf.Max(0f, extraSurfaceClearance);

        public CasterMovementEffectSettings() { }

        public CasterMovementEffectSettings(
            float movementSpeed,
            float maxDistance,
            bool moveInstantly,
            bool lineOfSightRequired,
            LayerMask blockingLayers,
            CasterMovementDestinationSource movementDestination =
                CasterMovementDestinationSource.AimedPoint,
            bool remainOutsideHitSurface = true,
            float surfaceClearance = 0.05f)
        {
            destinationSource = movementDestination;
            speed = Mathf.Max(0.01f, movementSpeed);
            maximumDistance = Mathf.Max(0.01f, maxDistance);
            instantaneous = moveInstantly;
            requireLineOfSight = lineOfSightRequired;
            obstructionMask = blockingLayers;
            keepOutsideHitSurface = remainOutsideHitSurface;
            extraSurfaceClearance = Mathf.Max(0f, surfaceClearance);
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_CasterMovement",
        menuName = "Project Eri/Skill System V2/Effects/Caster Movement")]
    public sealed class CasterMovementEffectDefinition :
        EffectDefinition,
        ISpellCastContextModifierEffectDefinition
    {
        [Tooltip("Default non-instant movement speed in world units per second.")]
        [SerializeField, Min(0.01f)] private float speed = 14f;
        [Tooltip("Default farthest distance the caster may move.")]
        [SerializeField, Min(0.01f)] private float maximumDistance = 4f;
        [Tooltip("Default choice for moving immediately instead of traveling over time.")]
        [SerializeField] private bool instantaneous;
        [Tooltip("Default choice for rejecting movement paths blocked by selected layers.")]
        [SerializeField] private bool requireLineOfSight = true;
        [Tooltip("Default Unity layers that block movement when line of sight is required.")]
        [SerializeField] private LayerMask obstructionMask = ~0;
        [Tooltip("Default source of the movement destination: player aim or a point reported by a delivery event.")]
        [SerializeField] private CasterMovementDestinationSource
            destinationSource = CasterMovementDestinationSource.AimedPoint;
        [Tooltip("Default choice for keeping an impact teleport outside the surface that was hit.")]
        [SerializeField] private bool keepOutsideHitSurface = true;
        [Tooltip("Default additional gap left between the caster and an impacted surface.")]
        [SerializeField, Min(0f)] private float extraSurfaceClearance = 0.05f;

        public override Type SettingsType =>
            typeof(CasterMovementEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new CasterMovementEffectSettings(
                speed,
                maximumDistance,
                instantaneous,
                requireLineOfSight,
                obstructionMask,
                destinationSource,
                keepOutsideHitSurface,
                extraSurfaceClearance);
        }

        public override bool CanApplyWithoutRecipient(
            SpellEffectSettings settings)
        {
            return true;
        }

        public bool TryModifyCastContext(
            in CastContext requestedContext,
            SpellEffectSettings settings,
            out CastContext resolvedContext,
            out string rejectionReason)
        {
            CasterMovementEffectSettings resolved = Resolve(settings);
            if (resolved.DestinationSource ==
                CasterMovementDestinationSource.DeliveryEventPoint)
            {
                resolvedContext = requestedContext;
                rejectionReason = string.Empty;
                return true;
            }

            if (!SpellMovementPath2D.TryResolveDestination(
                    requestedContext.Caster,
                    requestedContext.TargetPoint,
                    resolved.MaximumDistance,
                    resolved.RequireLineOfSight,
                    resolved.ObstructionMask,
                    out Vector2 destination,
                    out rejectionReason))
            {
                resolvedContext = requestedContext.HasTargetPoint
                    ? requestedContext.WithTargetPoint(destination)
                    : requestedContext;
                return false;
            }

            resolvedContext = requestedContext.WithTargetPoint(destination);
            return true;
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            GameObject caster = context.Cast.Caster;
            if (caster == null)
                return false;

            CasterMovementEffectSettings resolved = Resolve(settings);
            Vector2 requestedDestination;
            if (resolved.DestinationSource ==
                CasterMovementDestinationSource.DeliveryEventPoint)
            {
                if (!context.HasDeliveryEvent)
                    return false;

                requestedDestination = context.HitPoint;
                if (resolved.KeepOutsideHitSurface &&
                    context.HitNormal.sqrMagnitude > 0.000001f)
                {
                    Vector2 normal = context.HitNormal.normalized;
                    float clearance = SpellMovementPath2D
                        .GetBodyClearance(caster, normal) +
                        resolved.ExtraSurfaceClearance;
                    requestedDestination += normal * clearance;
                }
            }
            else
            {
                if (!context.Cast.HasTargetPoint)
                    return false;
                requestedDestination = context.Cast.TargetPoint;
            }

            if (!SpellMovementPath2D.TryResolveDestination(
                    caster,
                    requestedDestination,
                    resolved.MaximumDistance,
                    resolved.RequireLineOfSight,
                    resolved.ObstructionMask,
                    out Vector2 destination,
                    out _))
            {
                return false;
            }

            SpellCasterMovementRuntime2D runtime =
                caster.GetComponent<SpellCasterMovementRuntime2D>();
            if (runtime == null)
                runtime = caster.AddComponent<SpellCasterMovementRuntime2D>();

            runtime.BeginMovement(
                destination,
                resolved.Speed,
                resolved.Instantaneous,
                resolved.RequireLineOfSight,
                resolved.ObstructionMask,
                context.Spell != null
                    ? context.Spell.Timing.TimeMode
                    : SpellTimeMode.Scaled);
            return true;
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            CasterMovementEffectSettings resolved = Resolve(settings);
            if (resolved.MaximumDistance <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Caster Movement requires a positive maximum distance."));
            }

            if (!resolved.Instantaneous && resolved.Speed <= 0f)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    "Non-instant Caster Movement requires a positive speed."));
            }

            if (resolved.RequireLineOfSight &&
                resolved.ObstructionMask.value == 0)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Warning,
                    "Caster Movement requires line of sight but its obstruction mask is empty."));
            }
        }

        private CasterMovementEffectSettings Resolve(
            SpellEffectSettings settings)
        {
            return settings as CasterMovementEffectSettings ??
                   (CasterMovementEffectSettings)CreateDefaultSettings();
        }

        private void OnValidate()
        {
            speed = Mathf.Max(0.01f, speed);
            maximumDistance = Mathf.Max(0.01f, maximumDistance);
            extraSurfaceClearance = Mathf.Max(0f, extraSurfaceClearance);
        }
    }

    internal static class SpellMovementPath2D
    {
        private static readonly RaycastHit2D[] castResults =
            new RaycastHit2D[16];

        public static bool TryResolveDestination(
            GameObject caster,
            Vector2 requestedDestination,
            float maximumDistance,
            bool requireLineOfSight,
            LayerMask obstructionMask,
            out Vector2 destination,
            out string rejectionReason)
        {
            destination = caster != null
                ? (Vector2)caster.transform.position
                : requestedDestination;

            if (caster == null)
            {
                rejectionReason = "Movement cast has no caster.";
                return false;
            }

            Vector2 origin = caster.transform.position;
            Vector2 offset = requestedDestination - origin;
            float maxDistance = Mathf.Max(0.01f, maximumDistance);
            if (offset.sqrMagnitude > maxDistance * maxDistance)
                offset = offset.normalized * maxDistance;

            destination = origin + offset;
            float distance = offset.magnitude;
            if (distance <= 0.001f)
            {
                rejectionReason = "Movement destination is already occupied by the caster.";
                return false;
            }

            if (!requireLineOfSight || obstructionMask.value == 0)
            {
                rejectionReason = string.Empty;
                return true;
            }

            Vector2 direction = offset / distance;
            Collider2D bodyCollider = FindBodyCollider(caster);
            int hitCount;
            if (bodyCollider != null &&
                bodyCollider.attachedRigidbody != null)
            {
                var filter = new ContactFilter2D();
                filter.SetLayerMask(obstructionMask);
                filter.useTriggers = false;
                hitCount = bodyCollider.Cast(
                    direction,
                    filter,
                    castResults,
                    distance,
                    true);
            }
            else
            {
                var filter = new ContactFilter2D();
                filter.SetLayerMask(obstructionMask);
                filter.useTriggers = false;
                hitCount = Physics2D.Raycast(
                    origin,
                    direction,
                    filter,
                    castResults,
                    distance);
            }

            Collider2D blocker = FindFirstExternalBlocker(
                caster,
                hitCount);
            if (blocker != null)
            {
                rejectionReason =
                    $"Movement path is blocked by '{blocker.name}'.";
                return false;
            }

            rejectionReason = string.Empty;
            return true;
        }

        private static Collider2D FindFirstExternalBlocker(
            GameObject caster,
            int hitCount)
        {
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D candidate = castResults[i].collider;
                if (candidate != null &&
                    !SpellTargetResolver.IsSameHierarchy(
                        caster,
                        candidate.gameObject))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Collider2D FindBodyCollider(GameObject caster)
        {
            Collider2D[] colliders =
                caster.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D candidate = colliders[i];
                if (candidate != null && candidate.enabled &&
                    !candidate.isTrigger)
                {
                    return candidate;
                }
            }

            return null;
        }

        public static float GetBodyClearance(
            GameObject caster,
            Vector2 surfaceNormal)
        {
            if (caster == null ||
                surfaceNormal.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            Vector2 normal = surfaceNormal.normalized;
            Collider2D[] colliders =
                caster.GetComponentsInChildren<Collider2D>(true);
            float greatest = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D candidate = colliders[i];
                if (candidate == null || !candidate.enabled ||
                    candidate.isTrigger)
                {
                    continue;
                }

                Vector3 extents = candidate.bounds.extents;
                float projected =
                    Mathf.Abs(normal.x) * extents.x +
                    Mathf.Abs(normal.y) * extents.y;
                greatest = Mathf.Max(greatest, projected);
            }

            return greatest;
        }
    }

    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class SpellCasterMovementRuntime2D : MonoBehaviour
    {
        private Rigidbody2D body;
        private Vector2 destination;
        private float speed;
        private bool requireLineOfSight;
        private LayerMask obstructionMask;
        private SpellTimeMode timeMode;

        public bool IsMoving { get; private set; }
        public Vector2 Destination => destination;

        public void BeginMovement(
            Vector2 resolvedDestination,
            float movementSpeed,
            bool instantaneous,
            bool lineOfSightRequired,
            LayerMask blockingLayers,
            SpellTimeMode movementTimeMode)
        {
            body = GetComponent<Rigidbody2D>();
            destination = resolvedDestination;
            speed = Mathf.Max(0.01f, movementSpeed);
            requireLineOfSight = lineOfSightRequired;
            obstructionMask = blockingLayers;
            timeMode = movementTimeMode;
            StopVelocity();

            if (instantaneous)
            {
                SetPosition(destination, useMovePosition: false);
                FinishMovement();
                return;
            }

            IsMoving = true;
        }

        private void FixedUpdate()
        {
            float delta = timeMode == SpellTimeMode.Unscaled
                ? Time.fixedUnscaledDeltaTime
                : Time.fixedDeltaTime;
            TickMovement(delta);
        }

        internal void TickMovement(float deltaTime)
        {
            if (!IsMoving)
                return;

            Vector2 current = body != null
                ? body.position
                : (Vector2)transform.position;
            float step = speed * Mathf.Max(0f, deltaTime);
            Vector2 next = Vector2.MoveTowards(current, destination, step);

            if (requireLineOfSight &&
                !SpellMovementPath2D.TryResolveDestination(
                    gameObject,
                    next,
                    Vector2.Distance(current, next) + 0.001f,
                    true,
                    obstructionMask,
                    out next,
                    out _))
            {
                FinishMovement();
                return;
            }

            StopVelocity();
            SetPosition(next, useMovePosition: true);
            if ((next - destination).sqrMagnitude <= 0.000001f)
                FinishMovement();
        }

        private void SetPosition(Vector2 position, bool useMovePosition)
        {
            if (body != null)
            {
                if (useMovePosition)
                    body.MovePosition(position);
                else
                    body.position = position;
                return;
            }

            transform.position = new Vector3(
                position.x,
                position.y,
                transform.position.z);
        }

        private void StopVelocity()
        {
            if (body == null)
                return;

#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = Vector2.zero;
#else
            body.velocity = Vector2.zero;
#endif
        }

        private void FinishMovement()
        {
            IsMoving = false;
            StopVelocity();
        }
    }
}
