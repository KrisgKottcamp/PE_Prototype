using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "DamageType_New",
        menuName = "Project Eri/Skill System V2/Effects/Damage Type")]
    public sealed class DamageTypeDefinition : ScriptableObject
    {
        [Tooltip("The name designers and UI use for this damage category.")]
        [SerializeField]
        private string displayName = "Damage";

        [Tooltip("Permanent unique ID used by saves, reactions, resistances, and other systems.")]
        [SerializeField]
        private string stableId;

        [Tooltip("Suggested UI and visual color for this damage category.")]
        [SerializeField]
        private Color displayColor = Color.white;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public string StableId => stableId;
        public Color DisplayColor => displayColor;

        [ContextMenu("Regenerate Stable ID")]
        public void RegenerateStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }
    }
}
