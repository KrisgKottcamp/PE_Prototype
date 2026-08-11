using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public static class SpellEffectReceiverResolver
    {
        public static T Find<T>(GameObject target)
            where T : class
        {
            if (target == null)
                return null;

            MonoBehaviour[] behaviours =
                target.GetComponentsInParent<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is T receiver)
                    return receiver;
            }

            // A CombatTarget is often placed on the character root while a
            // legacy health, movement, or status receiver lives on a child.
            // Treat that hierarchy as one target instead of silently failing
            // based on component placement.
            GameObject canonicalTarget = SpellTargetResolver.Resolve(target);
            if (canonicalTarget == null)
                return null;

            behaviours = canonicalTarget.GetComponentsInChildren<MonoBehaviour>(
                true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is T receiver)
                    return receiver;
            }

            return null;
        }
    }
}
