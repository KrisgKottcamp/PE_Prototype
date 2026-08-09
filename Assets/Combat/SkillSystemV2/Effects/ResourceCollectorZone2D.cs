using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum ResourceCollectorRecipient
    {
        OriginalCaster,
        OriginalEffectTarget,
        CollectorObject
    }

    [DisallowMultipleComponent]
    public sealed class ResourceCollectorZone2D : MonoBehaviour,
        ISpellSpawnReceiver
    {
        [SerializeField]
        private ResourceCollectorRecipient recipient =
            ResourceCollectorRecipient.OriginalCaster;

        [SerializeField, Min(0f)]
        private float collectionMultiplier = 1f;

        private SpellEffectContext sourceContext;
        private bool initialized;

        public void InitializeSpawn(in SpellSpawnContext context)
        {
            sourceContext = context.EffectContext;
            initialized = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryCollect(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryCollect(other);
        }

        private void TryCollect(Collider2D other)
        {
            if (!initialized || other == null || collectionMultiplier <= 0f)
                return;

            ISpellResourcePickup pickup =
                SpellEffectReceiverResolver.Find<ISpellResourcePickup>(
                    other.gameObject);
            if (pickup == null ||
                pickup.Resource == null ||
                pickup.AvailableAmount <= 0f)
            {
                return;
            }

            GameObject target = ResolveRecipient();
            ISpellResourceReceiver receiver =
                SpellEffectReceiverResolver.Find<ISpellResourceReceiver>(target);
            if (receiver == null)
                return;

            var effectContext = new SpellEffectContext(
                sourceContext.Spell,
                sourceContext.Cast,
                target,
                transform.position,
                Vector2.zero,
                sourceContext.PotencyScale);
            var request = new SpellResourceChangeRequest(
                effectContext,
                pickup.Resource,
                SpellResourceOperation.Add,
                pickup.AvailableAmount * collectionMultiplier,
                allowOverflow: false);

            if (!receiver.TryChangeResource(request, out var result) ||
                result.AppliedDelta <= 0f)
            {
                return;
            }

            pickup.Consume(result.AppliedDelta / collectionMultiplier);
        }

        private GameObject ResolveRecipient()
        {
            switch (recipient)
            {
                case ResourceCollectorRecipient.OriginalEffectTarget:
                    return sourceContext.Target;
                case ResourceCollectorRecipient.CollectorObject:
                    return gameObject;
                default:
                    return sourceContext.Cast.Caster;
            }
        }

        private void OnValidate()
        {
            collectionMultiplier = Mathf.Max(0f, collectionMultiplier);
        }
    }
}
