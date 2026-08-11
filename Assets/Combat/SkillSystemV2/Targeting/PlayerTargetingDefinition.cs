using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public abstract class PlayerTargetingDefinition : ScriptableObject
    {
        [Tooltip("The targeting mode's designer-facing name.")]
        [SerializeField]
        private string displayName;

        [Header("Aim Session")]
        [Tooltip("Game speed while the player is aiming. For example, 0.15 means the game runs at 15% speed.")]
        [SerializeField, Range(0.01f, 1f)]
        private float aimTimeScale = 0.15f;

        [Tooltip("Farthest allowed aim distance from the caster. Zero means unlimited range.")]
        [SerializeField, Min(0f)]
        private float maximumRange = 8f;

        [Tooltip("Move an out-of-range cursor point back to Maximum Range. Disable it when aiming outside the range should be invalid instead.")]
        [SerializeField]
        private bool clampToMaximumRange = true;

        [Header("Preview")]
        [Tooltip("Radius shown by circle previews and used as the visible width reference for other previews.")]
        [SerializeField, Min(0f)]
        private float previewRadius = 0.5f;

        [Tooltip("Width of a cone preview in degrees. Zero uses a line for directional targeting.")]
        [SerializeField, Range(0f, 360f)]
        private float previewConeAngle;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();

        public float AimTimeScale => Mathf.Clamp(aimTimeScale, 0.01f, 1f);
        public float MaximumRange => Mathf.Max(0f, maximumRange);
        public bool ClampToMaximumRange => clampToMaximumRange;
        public float PreviewRadius => Mathf.Max(0f, previewRadius);
        public float PreviewConeAngle => Mathf.Clamp(previewConeAngle, 0f, 360f);

        public abstract CastTargetingRequirement ProvidedRequirements { get; }
        public abstract PlayerTargetingPreviewShape PreviewShape { get; }

        public bool Supports(CastTargetingRequirement requirements)
        {
            return (ProvidedRequirements & requirements) == requirements;
        }

        public abstract bool TryBuildContext(
            in PlayerTargetingRequest request,
            out CastContext context,
            out PlayerTargetingPreview preview,
            out string rejectionReason);

        protected Vector2 ClampPoint(Vector2 origin, Vector2 requestedPoint)
        {
            Vector2 offset = requestedPoint - origin;
            float range = MaximumRange;

            if (!clampToMaximumRange || range <= 0f ||
                offset.sqrMagnitude <= range * range)
            {
                return requestedPoint;
            }

            return origin + offset.normalized * range;
        }

        protected bool IsWithinRange(Vector2 origin, Vector2 point)
        {
            float range = MaximumRange;
            return range <= 0f ||
                   (point - origin).sqrMagnitude <= range * range + 0.0001f;
        }

        protected CastContext BuildContext(
            in PlayerTargetingRequest request,
            Vector2 aimDirection,
            bool hasAimDirection,
            Vector2 targetPoint,
            bool hasTargetPoint,
            GameObject selectedTarget)
        {
            return new CastContext(
                request.Caster,
                CombatTeamMember.ResolveTeam(request.Caster),
                request.Origin,
                aimDirection,
                hasAimDirection,
                targetPoint,
                hasTargetPoint,
                selectedTarget);
        }

        protected PlayerTargetingPreview BuildPreview(
            in PlayerTargetingRequest request,
            Vector2 aimPoint,
            Vector2 direction,
            GameObject selectedTarget,
            bool isValid,
            string validationMessage)
        {
            return new PlayerTargetingPreview(
                PreviewShape,
                request.Origin,
                aimPoint,
                direction,
                MaximumRange,
                PreviewRadius,
                PreviewConeAngle,
                selectedTarget,
                isValid,
                validationMessage);
        }

        protected virtual void OnValidate()
        {
            aimTimeScale = Mathf.Clamp(aimTimeScale, 0.01f, 1f);
            maximumRange = Mathf.Max(0f, maximumRange);
            previewRadius = Mathf.Max(0f, previewRadius);
            previewConeAngle = Mathf.Clamp(previewConeAngle, 0f, 360f);
        }
    }
}
