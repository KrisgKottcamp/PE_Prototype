using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public static class SpellTargetResolver
    {
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

            return candidate;
        }

        public static bool IsSameHierarchy(
            GameObject first,
            GameObject second)
        {
            if (first == null || second == null)
                return false;

            Transform firstTransform = first.transform;
            Transform secondTransform = second.transform;
            return firstTransform == secondTransform ||
                   firstTransform.IsChildOf(secondTransform) ||
                   secondTransform.IsChildOf(firstTransform);
        }
    }
}
