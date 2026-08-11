using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public struct SpellResourceCost
    {
        public const string ActionPoints = "AP";

        [Tooltip("The resource this spell spends. Use AP for the normal player Action Point pool.")]
        [SerializeField]
        private string resourceId;

        [Tooltip("How much of the selected resource is spent when the cast is confirmed.")]
        [SerializeField, Min(0f)]
        private float amount;

        public string ResourceId => string.IsNullOrWhiteSpace(resourceId)
            ? ActionPoints
            : resourceId.Trim();

        public float Amount => Mathf.Max(0f, amount);
        public bool IsFree => Amount <= 0f;

        public SpellResourceCost(string id, float value)
        {
            resourceId = string.IsNullOrWhiteSpace(id)
                ? ActionPoints
                : id.Trim();
            amount = Mathf.Max(0f, value);
        }
    }
}
