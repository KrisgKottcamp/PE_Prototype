using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public interface ISpellTarget
    {
        GameObject TargetObject { get; }
        bool IsTargetable { get; }
    }

    public interface ISpellResourceProvider
    {
        bool CanSpend(in SpellResourceCost cost);
        bool TrySpend(in SpellResourceCost cost);
        void Refund(in SpellResourceCost cost);
    }

    [DisallowMultipleComponent]
    public sealed class CombatTarget : MonoBehaviour, ISpellTarget
    {
        [SerializeField]
        private bool isTargetable = true;

        public GameObject TargetObject => gameObject;
        public bool IsTargetable => isTargetable && isActiveAndEnabled;

        public void SetTargetable(bool value)
        {
            isTargetable = value;
        }
    }
}
