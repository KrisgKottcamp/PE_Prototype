using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public static class SpellTargetResolver
    {
        /// <summary>
        /// Converts a collider, hurtbox child, or target proxy into the one
        /// stable GameObject that represents that gameplay target.
        /// </summary>
        public static GameObject Resolve(GameObject candidate)
        {
            if (candidate == null)
                return null;

            MonoBehaviour[] behaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellTarget target &&
                    target.TargetObject != null)
                {
                    return target.TargetObject;
                }
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellSpatialForceTarget projectile &&
                    projectile.SpatialForceTargetObject != null)
                {
                    return projectile.SpatialForceTargetObject;
                }
            }

            CombatTeamMember teamMember =
                candidate.GetComponentInParent<CombatTeamMember>(true);
            if (teamMember != null)
                return teamMember.gameObject;

            Collider2D collider = candidate.GetComponent<Collider2D>();
            if (collider != null && collider.attachedRigidbody != null)
                return collider.attachedRigidbody.gameObject;

            return candidate;
        }

        /// <summary>
        /// Resolves a selected target for an immediate delivery. Player party
        /// menus use proxy objects so every roster member remains independently
        /// addressable even though the live combat pawn is shared.
        /// </summary>
        public static GameObject ResolveImmediateTarget(
            GameObject selectedTarget)
        {
            return Resolve(selectedTarget);
        }

        public static bool TryResolveValidTarget(
            in SpellExecutionContext context,
            GameObject detectedObject,
            out GameObject targetObject)
        {
            targetObject = Resolve(detectedObject);
            return targetObject != null && context.Spell != null &&
                   context.Spell.TargetFilter.IsValid(
                       context.Cast,
                       targetObject,
                       detectedObject);
        }

        /// <summary>
        /// Resolves an explicitly marked spell target. Unlike <see cref="Resolve"/>,
        /// this deliberately does not treat ordinary world geometry as a target.
        /// Deliveries use this distinction when an impact can either affect a
        /// character or bounce from a wall.
        /// </summary>
        public static bool TryResolveSpellTarget(
            GameObject candidate,
            out GameObject targetObject)
        {
            targetObject = null;
            if (candidate == null)
                return false;

            MonoBehaviour[] behaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is ISpellTarget target) ||
                    target.TargetObject == null)
                {
                    continue;
                }

                targetObject = target.TargetObject;
                return true;
            }

            return false;
        }

        public static bool IsSameHierarchy(
            GameObject first,
            GameObject second)
        {
            if (first == null || second == null)
                return false;

            GameObject resolvedFirst = Resolve(first);
            GameObject resolvedSecond = Resolve(second);
            if (resolvedFirst == null || resolvedSecond == null)
                return false;

            Transform firstTransform = resolvedFirst.transform;
            Transform secondTransform = resolvedSecond.transform;
            if (firstTransform == secondTransform)
                return true;

            bool firstHasIdentity = HasExplicitIdentity(resolvedFirst);
            bool secondHasIdentity = HasExplicitIdentity(resolvedSecond);
            if (firstHasIdentity || secondHasIdentity)
            {
                return Represents(resolvedFirst, resolvedSecond) ||
                       Represents(resolvedSecond, resolvedFirst);
            }

            return firstTransform.IsChildOf(secondTransform) ||
                   secondTransform.IsChildOf(firstTransform);
        }

        internal static bool HasExplicitIdentity(GameObject candidate)
        {
            if (candidate == null)
                return false;

            MonoBehaviour[] behaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellTargetIdentity)
                    return true;
            }

            return false;
        }

        public static int GetTargetId(GameObject candidate)
        {
            GameObject resolved = Resolve(candidate);
            return resolved != null ? resolved.GetInstanceID() : 0;
        }

        private static bool Represents(
            GameObject identityOwner,
            GameObject other)
        {
            MonoBehaviour[] behaviours =
                identityOwner.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellTargetIdentity identity &&
                    identity.Represents(other))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
