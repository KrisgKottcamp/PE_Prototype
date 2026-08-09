using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct GameplaySignalEvent
    {
        public GameplaySignalDefinition Signal { get; }
        public SpellEffectContext EffectContext { get; }
        public string Label { get; }
        public float Value { get; }

        public GameplaySignalEvent(
            GameplaySignalDefinition signal,
            in SpellEffectContext effectContext,
            string label,
            float value)
        {
            Signal = signal;
            EffectContext = effectContext;
            Label = label ?? string.Empty;
            Value = value;
        }
    }

    [CreateAssetMenu(
        fileName = "Signal_New",
        menuName = "Project Eri/Skill System V2/Effects/Gameplay Signal")]
    public sealed class GameplaySignalDefinition : ScriptableObject
    {
        [SerializeField]
        private string displayName = "Gameplay Signal";

        [SerializeField]
        private string stableId;

        public event Action<GameplaySignalEvent> Raised;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public string StableId => stableId;

        [ContextMenu("Regenerate Stable ID")]
        public void RegenerateStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }

        public void Raise(in GameplaySignalEvent signalEvent)
        {
            Raised?.Invoke(signalEvent);
        }

        private void OnDisable()
        {
            Raised = null;
        }
    }
}
