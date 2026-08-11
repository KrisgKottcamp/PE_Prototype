using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Resource_New",
        menuName = "Project Eri/Skill System V2/Effects/Gameplay Resource")]
    public sealed class GameplayResourceDefinition : ScriptableObject
    {
        [Tooltip("The resource name shown to designers and players.")]
        [SerializeField]
        private string displayName = "Action Points";

        [Tooltip("Permanent text ID used to connect effects and resource providers. Use AP for the existing Action Point pool.")]
        [SerializeField]
        private string resourceId = SpellResourceCost.ActionPoints;

        [Tooltip("Optional icon used when this resource appears in UI.")]
        [SerializeField]
        private Sprite icon;

        [Tooltip("Lowest value allowed for this resource.")]
        [SerializeField]
        private float minimumValue;

        [Tooltip("Normal maximum used by generic resource pools. Character AP uses the Character Definition maximum.")]
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
