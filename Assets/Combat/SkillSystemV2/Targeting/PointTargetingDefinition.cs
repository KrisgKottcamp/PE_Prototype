using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Targeting_Point",
        menuName = "Project Eri/Skill System V2/Targeting/Point or Area")]
    public sealed class PointTargetingDefinition : PlayerTargetingDefinition
    {
        [Tooltip("When enabled, the actual cursor must be inside Maximum Range. When disabled, an outside cursor can be clamped to the edge.")]
        [SerializeField]
        private bool requirePointerWithinRange;

        public override CastTargetingRequirement ProvidedRequirements =>
            CastTargetingRequirement.TargetPoint;

        public override PlayerTargetingPreviewShape PreviewShape =>
            PlayerTargetingPreviewShape.Circle;

        public override bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason)
        {
            bool pointerInRange = IsWithinRange(
                request.Origin,
                request.PointerWorldPosition);
            Vector2 point = ClampPoint(
                request.Origin,
                request.PointerWorldPosition);
            Vector2 offset = point - request.Origin;
            bool valid = request.Caster != null &&
                         (!requirePointerWithinRange || pointerInRange);

            rejectionReason = request.Caster == null
                ? "Aim session has no caster."
                : valid
                    ? string.Empty
                    : "Target point is outside the allowed range.";

            context = BuildContext(
                request,
                offset,
                offset.sqrMagnitude > 0.000001f,
                point,
                true,
                null);
            preview = BuildPreview(
                request,
                point,
                offset.sqrMagnitude > 0.000001f
                    ? offset.normalized
                    : Vector2.zero,
                null,
                valid,
                rejectionReason);
            return valid;
        }
    }
}
