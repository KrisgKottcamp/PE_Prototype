using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct CastContext
    {
        public GameObject Caster { get; }
        public CombatTeam CasterTeam { get; }
        public Vector2 Origin { get; }
        public Vector2 AimDirection { get; }
        public bool HasAimDirection { get; }
        public Vector2 TargetPoint { get; }
        public bool HasTargetPoint { get; }
        public GameObject SelectedTarget { get; }
        public SpellTargetingPayload TargetingPayload { get; }
        public Vector2 SupplementalTargetPoint { get; }
        public bool HasSupplementalTargetPoint { get; }
        public Vector2 SupplementalAimDirection { get; }
        public bool HasSupplementalAimDirection { get; }
        public GameObject SupplementalSelectedTarget { get; }
        public SpellTargetingPayload SupplementalTargetingPayload { get; }
        public CastChainBudget ChainBudget { get; }
        public int ChainDepth { get; }

        public bool HasSelectedTarget => SelectedTarget != null;
        public long RootCastId => ChainBudget != null
            ? ChainBudget.RootCastId
            : 0L;

        public CastContext(
            GameObject caster,
            CombatTeam casterTeam,
            Vector2 origin,
            Vector2 aimDirection,
            bool hasAimDirection,
            Vector2 targetPoint,
            bool hasTargetPoint,
            GameObject selectedTarget,
            CastChainBudget chainBudget = null,
            int chainDepth = 0,
            SpellTargetingPayload targetingPayload = null,
            Vector2 supplementalTargetPoint = default,
            bool hasSupplementalTargetPoint = false,
            Vector2 supplementalAimDirection = default,
            bool hasSupplementalAimDirection = false,
            GameObject supplementalSelectedTarget = null,
            SpellTargetingPayload supplementalTargetingPayload = null)
        {
            Caster = caster;
            CasterTeam = casterTeam;
            Origin = origin;
            HasAimDirection = hasAimDirection &&
                              aimDirection.sqrMagnitude > 0.000001f;
            AimDirection = HasAimDirection
                ? aimDirection.normalized
                : Vector2.zero;
            TargetPoint = targetPoint;
            HasTargetPoint = hasTargetPoint;
            SelectedTarget = selectedTarget;
            TargetingPayload = targetingPayload;
            SupplementalTargetPoint = supplementalTargetPoint;
            HasSupplementalTargetPoint = hasSupplementalTargetPoint;
            HasSupplementalAimDirection = hasSupplementalAimDirection &&
                supplementalAimDirection.sqrMagnitude > 0.000001f;
            SupplementalAimDirection = HasSupplementalAimDirection
                ? supplementalAimDirection.normalized
                : Vector2.zero;
            SupplementalSelectedTarget = supplementalSelectedTarget;
            SupplementalTargetingPayload = supplementalTargetingPayload;
            ChainBudget = chainBudget;
            ChainDepth = Mathf.Max(0, chainDepth);
        }

        public static CastContext ForDirection(
            GameObject caster,
            Vector2 origin,
            Vector2 direction)
        {
            return new CastContext(
                caster,
                CombatTeamMember.ResolveTeam(caster),
                origin,
                direction,
                true,
                default,
                false,
                null);
        }

        public static CastContext ForPoint(
            GameObject caster,
            Vector2 origin,
            Vector2 targetPoint)
        {
            Vector2 direction = targetPoint - origin;

            return new CastContext(
                caster,
                CombatTeamMember.ResolveTeam(caster),
                origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                targetPoint,
                true,
                null);
        }

        public static CastContext ForTarget(
            GameObject caster,
            Vector2 origin,
            GameObject selectedTarget)
        {
            bool hasTarget = selectedTarget != null;
            Vector2 point = hasTarget
                ? selectedTarget.transform.position
                : origin;
            Vector2 direction = point - origin;

            return new CastContext(
                caster,
                CombatTeamMember.ResolveTeam(caster),
                origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                point,
                hasTarget,
                selectedTarget);
        }

        public CastContext WithCaster(GameObject caster)
        {
            return new CastContext(
                caster,
                CombatTeamMember.ResolveTeam(caster, CasterTeam),
                Origin,
                AimDirection,
                HasAimDirection,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                TargetingPayload,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalSelectedTarget,
                SupplementalTargetingPayload);
        }

        public CastContext WithBudget(CastChainBudget budget)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                AimDirection,
                HasAimDirection,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                budget,
                ChainDepth,
                TargetingPayload,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalSelectedTarget,
                SupplementalTargetingPayload);
        }

        public CastContext WithTargetPoint(Vector2 targetPoint)
        {
            Vector2 direction = targetPoint - Origin;
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                targetPoint,
                true,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                TargetingPayload,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalSelectedTarget,
                SupplementalTargetingPayload);
        }

        public CastContext WithAimDirection(Vector2 aimDirection)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                aimDirection,
                aimDirection.sqrMagnitude > 0.000001f,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                TargetingPayload,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalSelectedTarget,
                SupplementalTargetingPayload);
        }

        public CastContext WithTargetingPayload(
            SpellTargetingPayload payload)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                AimDirection,
                HasAimDirection,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                payload,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalSelectedTarget,
                SupplementalTargetingPayload);
        }

        public CastContext WithSupplementalTargetPoint(Vector2 targetPoint)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                AimDirection,
                HasAimDirection,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                TargetingPayload,
                targetPoint,
                true,
                targetPoint - Origin,
                (targetPoint - Origin).sqrMagnitude > 0.000001f);
        }

        public CastContext WithSupplementalTargeting(
            in CastContext supplemental)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                AimDirection,
                HasAimDirection,
                TargetPoint,
                HasTargetPoint,
                SelectedTarget,
                ChainBudget,
                ChainDepth,
                TargetingPayload,
                supplemental.TargetPoint,
                supplemental.HasTargetPoint,
                supplemental.AimDirection,
                supplemental.HasAimDirection,
                supplemental.SelectedTarget,
                supplemental.TargetingPayload);
        }

        public CastContext CreateSupplementalContext()
        {
            return new CastContext(
                Caster,
                CasterTeam,
                Origin,
                SupplementalAimDirection,
                HasSupplementalAimDirection,
                SupplementalTargetPoint,
                HasSupplementalTargetPoint,
                SupplementalSelectedTarget,
                ChainBudget,
                ChainDepth,
                SupplementalTargetingPayload);
        }

        public CastContext CreateChild(
            Vector2 origin,
            Vector2 aimDirection,
            Vector2 targetPoint,
            bool hasTargetPoint,
            GameObject selectedTarget)
        {
            return new CastContext(
                Caster,
                CasterTeam,
                origin,
                aimDirection,
                aimDirection.sqrMagnitude > 0.000001f,
                targetPoint,
                hasTargetPoint,
                selectedTarget,
                ChainBudget,
                ChainDepth + 1,
                TargetingPayload);
        }
    }
}
