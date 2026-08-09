using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Resource_New",
        menuName = "Project Eri/Skill System V2/Effects/Gameplay Resource")]
    public sealed class GameplayResourceDefinition : ScriptableObject
    {
        [SerializeField]
        private string displayName = "Action Points";

        [SerializeField]
        private string resourceId = SpellResourceCost.ActionPoints;

        [SerializeField]
        private Sprite icon;

        [SerializeField]
        private float minimumValue;

        [SerializeField, Min(0f)]
        private float defaultMaximumValue = 100f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public string ResourceId => string.IsNullOrWhiteSpace(resourceId)
            ? SpellResourceCost.ActionPoints
            : resourceId.Trim();
        public Sprite Icon => icon;
        public float MinimumValue => minimumValue;
        public float DefaultMaximumValue =>
            Mathf.Max(minimumValue, defaultMaximumValue);

        public float Clamp(float value)
        {
            return Mathf.Clamp(value, MinimumValue, DefaultMaximumValue);
        }

        private void OnValidate()
        {
            defaultMaximumValue = Mathf.Max(
                minimumValue,
                defaultMaximumValue);
        }
    }
}
