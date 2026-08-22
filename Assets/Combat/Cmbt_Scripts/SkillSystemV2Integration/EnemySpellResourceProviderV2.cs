using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    /// <summary>
    /// Explicitly waives player-authored resource costs when enemies cast
    /// SkillSystemV2 spells. Enemies do not own, spend, regenerate, or refund
    /// AP; their pacing is governed by cooldowns, action scheduling, and AI
    /// utility instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpellResourceProviderV2 : MonoBehaviour,
        ISpellResourceProvider
    {
        [Header("Runtime Debug")]
        [SerializeField] private string debugLastWaivedCost = "None";

        public bool CanSpend(in SpellResourceCost cost)
        {
            return true;
        }

        public bool TrySpend(in SpellResourceCost cost)
        {
            if (!cost.IsFree)
                debugLastWaivedCost =
                    $"Waived {cost.Amount:0.##} {cost.ResourceId}";
            return true;
        }

        public void Refund(in SpellResourceCost cost)
        {
            // No resource was spent, so there is nothing to restore.
        }
    }
}
