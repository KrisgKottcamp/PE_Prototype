using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class DeliveryRequirementTests
    {
        private GameObject caster;

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(caster);
        }

        [Test]
        public void ProjectileDelivery_RequiresDirection()
        {
            var delivery = ScriptableObject.CreateInstance<
                ProjectileDeliveryDefinition>();
            var emptyContext = new CastContext(
                caster,
                CombatTeam.Player,
                Vector2.zero,
                Vector2.zero,
                false,
                Vector2.zero,
                false,
                null);

            Assert.That(delivery.ValidateContext(
                emptyContext,
                out string reason), Is.False);
            Assert.That(reason, Does.Contain("direction"));

            CastContext aimed = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);
            Assert.That(delivery.ValidateContext(aimed, out _), Is.True);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void AreaDelivery_RequiresTargetPoint()
        {
            var delivery = ScriptableObject.CreateInstance<
                AreaDeliveryDefinition>();

            Assert.That(delivery.ValidateContext(
                CastContext.ForDirection(
                    caster,
                    Vector2.zero,
                    Vector2.right),
                out _), Is.False);
            Assert.That(delivery.ValidateContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    Vector2.one),
                out _), Is.True);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void SelfDelivery_AcceptsContextWithoutAim()
        {
            var delivery = ScriptableObject.CreateInstance<
                SelfDeliveryDefinition>();
            var context = new CastContext(
                caster,
                CombatTeam.Player,
                Vector2.zero,
                Vector2.zero,
                false,
                Vector2.zero,
                false,
                null);

            Assert.That(delivery.ValidateContext(context, out _), Is.True);
            Object.DestroyImmediate(delivery);
        }
    }
}
