using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum MenuTargetGroup
    {
        AllPartyMembers,
        ActivePartyMembers,
        ActiveEnemies
    }

    [CreateAssetMenu(
        fileName = "Targeting_MenuSelect",
        menuName = "Project Eri/Skill System V2/Targeting/Menu Select")]
    public sealed class MenuSelectTargetingDefinition :
        PlayerTargetingDefinition
    {
        [Tooltip("All Party Members includes the full roster, Active Party Members includes only living roster members, and Active Enemies includes living spawned enemies.")]
        [SerializeField]
        private MenuTargetGroup targetGroup =
            MenuTargetGroup.ActiveEnemies;

        [Tooltip("Sort menu entries alphabetically instead of preserving party order or scene discovery order.")]
        [SerializeField]
        private bool sortAlphabetically;

        public MenuTargetGroup TargetGroup => targetGroup;
        public bool SortAlphabetically => sortAlphabetically;

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
                : request.Origin;
            Vector2 direction = point - request.Origin;
            bool valid = request.Caster != null && target != null;

            context = BuildContext(
                request,
                direction,
                direction.sqrMagnitude > 0.000001f,
                point,
                target != null,
                target);

            if (request.Caster == null)
            {
                rejectionReason = "Target menu has no caster.";
            }
            else if (target == null)
            {
                rejectionReason = "Choose a target from the menu.";
            }
            else if (!request.TargetFilter.IsValid(
                         context,
                         target,
                         out rejectionReason))
            {
                valid = false;
            }
            else
            {
                rejectionReason = string.Empty;
            }

            preview = BuildPreview(
                request,
                point,
                direction,
                target,
                valid,
                rejectionReason);
            return valid;
        }
    }
}
