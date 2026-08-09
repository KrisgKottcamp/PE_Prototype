using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Targeting_Direction",
        menuName = "Project Eri/Skill System V2/Targeting/Direction")]
    public sealed class DirectionTargetingDefinition : PlayerTargetingDefinition
    {
        [SerializeField, Min(0f)]
        private float minimumAimDistance = 0.05f;

        public override CastTargetingRequirement ProvidedRequirements =>
            CastTargetingRequirement.Direction |
            CastTargetingRequirement.TargetPoint;

        public override PlayerTargetingPreviewShape PreviewShape =>
            PreviewConeAngle > 0.01f
                ? PlayerTargetingPreviewShape.Cone
                : PlayerTargetingPreviewShape.Line;

        public override bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason)
        {
            Vector2 point = ClampPoint(
                request.Origin,
                request.PointerWorldPosition);
            Vector2 offset = point - request.Origin;
            float minimum = Mathf.Max(0f, minimumAimDistance);
            bool valid = request.Caster != null &&
                         offset.sqrMagnitude >= minimum * minimum;
            Vector2 direction = valid ? offset.normalized : Vector2.zero;

            rejectionReason = request.Caster == null
                ? "Aim session has no caster."
                : valid
                    ? string.Empty
                    : "Move the cursor farther from the caster.";

            context = BuildContext(
                request,
                direction,
                valid,
                point,
                valid,
                null);
            preview = BuildPreview(
                request,
                point,
                direction,
                null,
                valid,
                rejectionReason);
            return valid;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            minimumAimDistance = Mathf.Max(0f, minimumAimDistance);
        }
    }
}
