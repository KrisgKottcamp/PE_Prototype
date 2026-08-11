using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum TargetRelationship
    {
        Any,
        Self,
        Allies,
        AlliesAndSelf,
        Enemies
    }

    [Serializable]
    public struct TargetFilter
    {
        [Tooltip("Which teams may be affected relative to the caster: enemies, allies, self, anyone, or no one.")]
        [SerializeField]
        private TargetRelationship relationship;

        [Tooltip("Enable this when team relationship alone is not enough and targets must also be on selected Unity layers.")]
        [SerializeField]
        private bool useLayerMask;

        [Tooltip("When Use Layer Mask is enabled, only objects on these Unity layers may be affected.")]
        [SerializeField]
        private LayerMask allowedLayers;

        [Tooltip("Require the object to expose SkillSystemV2's target marker. Disable this for walls, projectiles, and other valid non-character objects.")]
        [SerializeField]
        private bool requireSpellTarget;

        public TargetRelationship Relationship => relationship;
        public bool UsesLayerMask => useLayerMask;
        public LayerMask AllowedLayers => allowedLayers;
        public bool RequiresSpellTarget => requireSpellTarget;

        public TargetFilter(
            TargetRelationship targetRelationship,
            bool requireTarget = false,
            bool filterByLayer = false,
            LayerMask layers = default)
        {
            relationship = targetRelationship;
            requireSpellTarget = requireTarget;
            useLayerMask = filterByLayer;
            allowedLayers = layers;
        }

        public bool IsValid(
            in CastContext context,
            GameObject candidate)
        {
            return IsValid(context, candidate, candidate, out _);
        }

        public bool IsValid(
            in CastContext context,
            GameObject candidate,
            GameObject detectedObject)
        {
            return IsValid(
                context,
                candidate,
                detectedObject,
                out _);
        }

        public bool IsValid(
            in CastContext context,
            GameObject candidate,
            out string rejectionReason)
        {
            return IsValid(
                context,
                candidate,
                candidate,
                out rejectionReason);
        }

        public bool IsValid(
            in CastContext context,
            GameObject candidate,
            GameObject detectedObject,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;

            if (candidate == null)
            {
                rejectionReason = "Target is missing.";
                return false;
            }

            GameObject layerObject = detectedObject != null
                ? detectedObject
                : candidate;
            if (useLayerMask &&
                (allowedLayers.value & (1 << layerObject.layer)) == 0)
            {
                rejectionReason = "Target is on a filtered layer.";
                return false;
            }

            ISpellTarget spellTarget = FindInterfaceInParents<ISpellTarget>(
                candidate);

            if (requireSpellTarget && spellTarget == null)
            {
                rejectionReason = "Target has no ISpellTarget component.";
                return false;
            }

            if (spellTarget != null && !spellTarget.IsTargetable)
            {
                rejectionReason = "Target is currently untargetable.";
                return false;
            }

            bool isSelf = SpellTargetResolver.IsSameHierarchy(
                context.Caster,
                candidate);

            if (relationship == TargetRelationship.Any)
                return true;

            if (relationship == TargetRelationship.Self)
            {
                if (!isSelf)
                    rejectionReason = "Target is not the caster.";

                return isSelf;
            }

            bool hasCandidateTeam = CombatTeamMember.TryResolve(
                candidate,
                out CombatTeamMember candidateMember);

            if (!hasCandidateTeam ||
                candidateMember.Team == CombatTeam.Neutral ||
                context.CasterTeam == CombatTeam.Neutral)
            {
                rejectionReason = "Target has no comparable combat team.";
                return false;
            }

            bool sameTeam = candidateMember.Team == context.CasterTeam;

            switch (relationship)
            {
                case TargetRelationship.Allies:
                    if (!sameTeam || isSelf)
                        rejectionReason = "Target is not a different ally.";
                    return sameTeam && !isSelf;

                case TargetRelationship.AlliesAndSelf:
                    if (!sameTeam)
                        rejectionReason = "Target is not allied with the caster.";
                    return sameTeam;

                case TargetRelationship.Enemies:
                    if (sameTeam)
                        rejectionReason = "Target is not an enemy.";
                    return !sameTeam;

                default:
                    rejectionReason = "Unsupported target relationship.";
                    return false;
            }
        }

        private static T FindInterfaceInParents<T>(GameObject candidate)
            where T : class
        {
            MonoBehaviour[] behaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is T match)
                    return match;
            }

            return null;
        }
    }
}
