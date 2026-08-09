using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Targeting_Immediate",
        menuName = "Project Eri/Skill System V2/Targeting/Immediate")]
    public sealed class ImmediateTargetingDefinition : PlayerTargetingDefinition
    {
        public override CastTargetingRequirement ProvidedRequirements =>
            CastTargetingRequirement.None;

        public override PlayerTargetingPreviewShape PreviewShape =>
            PlayerTargetingPreviewShape.None;

        public override bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason)
        {
            bool valid = request.Caster != null;
            rejectionReason = valid ? string.Empty : "Aim session has no caster.";
            context = BuildContext(
                request,
                Vector2.zero,
                false,
                request.Origin,
                false,
                null);
            preview = BuildPreview(
                request,
                request.Origin,
                Vector2.zero,
                null,
                valid,
                rejectionReason);
            return valid;
        }
    }
}
