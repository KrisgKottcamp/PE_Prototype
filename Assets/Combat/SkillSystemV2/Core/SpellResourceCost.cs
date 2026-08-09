using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [Serializable]
    public struct SpellResourceCost
    {
        public const string ActionPoints = "AP";

        [SerializeField]
        private string resourceId;

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
