using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellLoadout : MonoBehaviour
    {
        [SerializeField]
        private SpellDefinition basicAttack;

        [SerializeField]
        private List<SpellDefinition> equippedSkills =
            new List<SpellDefinition>();

        public SpellDefinition BasicAttack => basicAttack;
        public IReadOnlyList<SpellDefinition> EquippedSkills =>
            equippedSkills ??
            (IReadOnlyList<SpellDefinition>)Array.Empty<SpellDefinition>();

        public SpellDefinition GetSkill(int index)
        {
            return equippedSkills != null &&
                   index >= 0 &&
                   index < equippedSkills.Count
                ? equippedSkills[index]
                : null;
        }

        public bool Contains(SpellDefinition spell)
        {
            return spell != null &&
                   (spell == basicAttack ||
                    (equippedSkills != null && equippedSkills.Contains(spell)));
        }

        /// <summary>
        /// Replaces the runtime loadout without modifying its source assets.
        /// Character-definition and enemy-loadout adapters use this same entry
        /// point so SpellRunner remains unaware of who selected the spells.
        /// </summary>
        public void ReplaceLoadout(
            SpellDefinition replacementBasicAttack,
            IReadOnlyList<SpellDefinition> replacementSkills)
        {
            basicAttack = replacementBasicAttack;
            equippedSkills ??= new List<SpellDefinition>();
            equippedSkills.Clear();

            if (replacementSkills == null)
                return;

            for (int i = 0; i < replacementSkills.Count; i++)
            {
                SpellDefinition spell = replacementSkills[i];
                if (spell != null)
                    equippedSkills.Add(spell);
            }
        }
    }
}
