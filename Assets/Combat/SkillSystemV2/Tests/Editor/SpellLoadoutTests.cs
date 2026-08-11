using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class SpellLoadoutTests
    {
        private GameObject owner;
        private SpellDefinition basicAttack;
        private SpellDefinition firstSkill;
        private SpellDefinition secondSkill;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Loadout Owner");
            basicAttack = ScriptableObject.CreateInstance<SpellDefinition>();
            firstSkill = ScriptableObject.CreateInstance<SpellDefinition>();
            secondSkill = ScriptableObject.CreateInstance<SpellDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(secondSkill);
            Object.DestroyImmediate(firstSkill);
            Object.DestroyImmediate(basicAttack);
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void ReplaceLoadout_ReplacesBasicAttackAndOrderedSkills()
        {
            SpellLoadout loadout = owner.AddComponent<SpellLoadout>();

            loadout.ReplaceLoadout(
                basicAttack,
                new[] { firstSkill, null, secondSkill });

            Assert.That(loadout.BasicAttack, Is.SameAs(basicAttack));
            Assert.That(loadout.EquippedSkills.Count, Is.EqualTo(2));
            Assert.That(loadout.GetSkill(0), Is.SameAs(firstSkill));
            Assert.That(loadout.GetSkill(1), Is.SameAs(secondSkill));
            Assert.That(loadout.Contains(basicAttack), Is.True);
            Assert.That(loadout.Contains(secondSkill), Is.True);
        }
    }
}
