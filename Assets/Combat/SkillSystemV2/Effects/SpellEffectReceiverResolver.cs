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

            return null;
        }
    }
}
