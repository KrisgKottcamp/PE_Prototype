using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellResourcePickup : MonoBehaviour,
        ISpellResourcePickup
    {
        [SerializeField]
        private GameplayResourceDefinition resource;

        [SerializeField, Min(0f)]
        private float amount = 1f;

        [SerializeField]
        private bool destroyWhenEmpty = true;

        public GameplayResourceDefinition Resource => resource;
        public float AvailableAmount => Mathf.Max(0f, amount);

        public float Consume(float requestedAmount)
        {
            float consumed = Mathf.Min(
                AvailableAmount,
                Mathf.Max(0f, requestedAmount));
            amount -= consumed;

            if (destroyWhenEmpty && amount <= 0.0001f)
                Destroy(gameObject);

            return consumed;
        }

        private void OnValidate()
        {
            amount = Mathf.Max(0f, amount);
        }
    }
}
