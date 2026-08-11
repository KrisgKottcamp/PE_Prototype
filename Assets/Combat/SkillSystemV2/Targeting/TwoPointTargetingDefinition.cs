using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Targeting_TwoPoint",
        menuName = "Project Eri/Skill System V2/Targeting/Two Points")]
    public sealed class TwoPointTargetingDefinition :
        PlayerTargetingDefinition,
        IStagedPlayerTargetingDefinition
    {
        [Tooltip("Maximum distance allowed between the first and second selected points. Zero uses only the targeting asset's Maximum Range.")]
        [SerializeField, Min(0f)]
        private float maximumSegmentLength = 8f;

        [Tooltip("Unity layers that make the connection between the two points invalid.")]
        [SerializeField]
        private LayerMask obstructionMask;

        [Tooltip("Extra thickness used when checking the line for obstacles. Zero checks a perfectly thin line.")]
        [SerializeField, Min(0f)]
        private float obstacleCheckRadius = 0.03f;

        public int RequiredPointCount => 2;

        public override CastTargetingRequirement ProvidedRequirements =>
            CastTargetingRequirement.Direction |
            CastTargetingRequirement.TargetPoint |
            CastTargetingRequirement.MultipleTargetPoints;

        public override PlayerTargetingPreviewShape PreviewShape =>
            PlayerTargetingPreviewShape.Line;

        public override bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason)
        {
            Vector2 pointer = ClampPoint(
                request.Origin,
                request.PointerWorldPosition);
            Vector2 first = default;
            bool hasFirst = request.ConfirmedTargeting != null &&
                            request.ConfirmedTargeting.TryGetPoint(
                                0,
                                out first);

            if (!hasFirst)
            {
                Vector2 direction = pointer - request.Origin;
                bool firstPointValid = request.Caster != null;
                rejectionReason = firstPointValid
                    ? "Choose the first endpoint."
                    : "Aim session has no caster.";
                context = new CastContext(
                    request.Caster,
                    CombatTeamMember.ResolveTeam(request.Caster),
                    request.Origin,
                    direction,
                    direction.sqrMagnitude > 0.000001f,
                    pointer,
                    true,
                    null);
                preview = BuildPreview(
                    request,
                    pointer,
                    direction,
                    null,
                    firstPointValid,
                    rejectionReason);
                return firstPointValid;
            }

            Vector2 second = pointer;
            Vector2 segment = second - first;
            float segmentLength = segment.magnitude;
            bool validSecond = request.Caster != null &&
                               segmentLength > 0.05f;
            float maximum = Mathf.Max(0f, maximumSegmentLength);
            if (validSecond && maximum > 0f && segmentLength > maximum)
            {
                second = first + segment.normalized * maximum;
                segment = second - first;
                segmentLength = maximum;
            }

            bool obstructed = validSecond && IsObstructed(
                first,
                second);
            bool valid = validSecond && !obstructed;
            rejectionReason = request.Caster == null
                ? "Aim session has no caster."
                : segmentLength <= 0.05f
                    ? "Move the second point farther from the first point."
                    : obstructed
                        ? "An obstacle blocks the connection between these points."
                        : string.Empty;

            var payload = new SpellTargetingPayload(first, second);
            context = new CastContext(
                request.Caster,
                CombatTeamMember.ResolveTeam(request.Caster),
                request.Origin,
                segment,
                segment.sqrMagnitude > 0.000001f,
                second,
                true,
                null,
                targetingPayload: payload);
            preview = new PlayerTargetingPreview(
                PlayerTargetingPreviewShape.Line,
                first,
                second,
                segment,
                maximumSegmentLength,
                PreviewRadius,
                0f,
                null,
                valid,
                rejectionReason);
            return valid;
        }

        private bool IsObstructed(Vector2 start, Vector2 end)
        {
            if (obstructionMask.value == 0)
                return false;

            Vector2 offset = end - start;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return false;

            RaycastHit2D hit = obstacleCheckRadius > 0f
                ? Physics2D.CircleCast(
                    start,
                    obstacleCheckRadius,
                    offset / distance,
                    distance,
                    obstructionMask)
                : Physics2D.Raycast(
                    start,
                    offset / distance,
                    distance,
                    obstructionMask);
            return hit.collider != null;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maximumSegmentLength = Mathf.Max(0f, maximumSegmentLength);
            obstacleCheckRadius = Mathf.Max(0f, obstacleCheckRadius);
        }
    }
}
