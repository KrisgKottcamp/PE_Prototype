using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [CreateAssetMenu(
        fileName = "Effect_GameplaySignal",
        menuName = "Project Eri/Skill System V2/Effects/Raise Gameplay Signal")]
    public sealed class GameplaySignalEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private GameplaySignalDefinition signal;

        [SerializeField]
        private string label;

        [SerializeField]
        private float value = 1f;

        [SerializeField]
        private bool scaleValueWithPotency = true;

        public override bool Apply(in SpellEffectContext context)
        {
            if (signal == null)
                return false;

            float resolvedValue = scaleValueWithPotency
                ? value * context.PotencyScale
                : value;
            signal.Raise(new GameplaySignalEvent(
                signal,
                context,
                label,
                resolvedValue));
            return true;
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (signal == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Gameplay Signal effect '{DisplayName}' needs a signal asset."));
            }
        }
    }
}
