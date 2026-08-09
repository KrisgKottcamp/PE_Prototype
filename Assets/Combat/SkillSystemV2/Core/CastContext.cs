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
            int chainDepth = 0)
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
                ChainDepth);
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
                ChainDepth);
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
                ChainDepth + 1);
        }
    }
}
