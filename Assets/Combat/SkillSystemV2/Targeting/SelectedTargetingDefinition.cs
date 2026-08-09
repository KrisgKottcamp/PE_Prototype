using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Targeting_SelectedTarget",
        menuName = "Project Eri/Skill System V2/Targeting/Selected Target")]
    public sealed class SelectedTargetingDefinition : PlayerTargetingDefinition
    {
        public override CastTargetingRequirement ProvidedRequirements =>
            CastTargetingRequirement.SelectedTarget |
            CastTargetingRequirement.TargetPoint;

        public override PlayerTargetingPreviewShape PreviewShape =>
            PlayerTargetingPreviewShape.Target;

        public override bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason)
        {
            GameObject target = request.SelectedTarget;
            Vector2 point = target != null
                ? target.transform.position
                : request.PointerWorldPosition;
            Vector2 offset = point - request.Origin;
            bool valid = request.Caster != null && target != null;

            if (request.Caster == null)
            {
                rejectionReason = "Aim session has no caster.";
            }
            else if (target == null)
            {
                rejectionReason = "Place the cursor over a valid target.";
            }
            else if (!IsWithinRange(request.Origin, point))
            {
                valid = false;
                rejectionReason = "Selected target is outside the allowed range.";
            }
            else if (!request.TargetFilter.IsValid(
                         BuildContext(
                             request,
                             offset,
                             offset.sqrMagnitude > 0.000001f,
                             point,
                             true,
                             target),
                         target,
                         out rejectionReason))
            {
                valid = false;
            }
            else
            {
                rejectionReason = string.Empty;
            }

            context = BuildContext(
                request,
                offset,
                offset.sqrMagnitude > 0.000001f,
                point,
                target != null,
                target);
            preview = BuildPreview(
                request,
                point,
                offset.sqrMagnitude > 0.000001f
                    ? offset.normalized
                    : Vector2.zero,
                target,
                valid,
                rejectionReason);
            return valid;
        }
    }
}
