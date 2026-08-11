using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class TargetFilterTests
    {
        private GameObject caster;
        private GameObject ally;
        private GameObject enemy;

        [SetUp]
        public void SetUp()
        {
            caster = CreateTeamObject("Caster", CombatTeam.Player);
            ally = CreateTeamObject("Ally", CombatTeam.Player);
            enemy = CreateTeamObject("Enemy", CombatTeam.Enemy);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(caster);
            Object.DestroyImmediate(ally);
            Object.DestroyImmediate(enemy);
        }

        [Test]
        public void EnemyFilterAcceptsEnemyAndRejectsAlly()
        {
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);
            var filter = new TargetFilter(TargetRelationship.Enemies);

            Assert.That(filter.IsValid(context, enemy), Is.True);
            Assert.That(filter.IsValid(context, ally), Is.False);
            Assert.That(filter.IsValid(context, caster), Is.False);
        }

        [Test]
        public void AlliesFilterExcludesCaster()
        {
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);
            var filter = new TargetFilter(TargetRelationship.Allies);

            Assert.That(filter.IsValid(context, ally), Is.True);
            Assert.That(filter.IsValid(context, caster), Is.False);
            Assert.That(filter.IsValid(context, enemy), Is.False);
        }

        [Test]
        public void RequiredSpellTargetRejectsObjectsWithoutTargetComponent()
        {
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);
            var filter = new TargetFilter(
                TargetRelationship.Enemies,
                requireTarget: true);

            Assert.That(filter.IsValid(context, enemy), Is.False);

            enemy.AddComponent<CombatTarget>();
            Assert.That(filter.IsValid(context, enemy), Is.True);
        }

        [Test]
        public void LayerFilterUsesDetectedHurtboxLayerAfterResolvingRoot()
        {
            var hurtbox = new GameObject("Enemy Hurtbox");
            hurtbox.transform.SetParent(enemy.transform);
            hurtbox.layer = 8;
            enemy.AddComponent<CombatTarget>();
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);
            var filter = new TargetFilter(
                TargetRelationship.Enemies,
                requireTarget: true,
                filterByLayer: true,
                layers: 1 << 8);

            GameObject resolved = SpellTargetResolver.Resolve(hurtbox);
            Assert.That(resolved, Is.SameAs(enemy));
            Assert.That(
                filter.IsValid(context, resolved, hurtbox),
                Is.True);

            Object.DestroyImmediate(hurtbox);
        }

        private static GameObject CreateTeamObject(
            string objectName,
            CombatTeam team)
        {
            var result = new GameObject(objectName);
            result.AddComponent<CombatTeamMember>().SetTeam(team);
            return result;
        }
    }
}
