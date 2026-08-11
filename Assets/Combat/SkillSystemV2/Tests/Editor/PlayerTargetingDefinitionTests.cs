using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class PlayerTargetingDefinitionTests
    {
        private GameObject caster;
        private GameObject target;

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
            target = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(caster);
            Object.DestroyImmediate(target);
        }

        [Test]
        public void DirectionTargeting_NormalizesAndClampsAim()
        {
            var definition = ScriptableObject.CreateInstance<
                DirectionTargetingDefinition>();
            var request = Request(new Vector2(20f, 0f));

            bool valid = definition.TryBuildContext(
                request,
                out CastContext context,
                out _,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.AimDirection, Is.EqualTo(Vector2.right));
            Assert.That(context.TargetPoint.x, Is.EqualTo(8f).Within(0.001f));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void DirectionTargeting_RejectsZeroLengthAim()
        {
            var definition = ScriptableObject.CreateInstance<
                DirectionTargetingDefinition>();

            bool valid = definition.TryBuildContext(
                Request(Vector2.zero),
                out _,
                out _,
                out string reason);

            Assert.That(valid, Is.False);
            Assert.That(reason, Is.Not.Empty);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void PointTargeting_ProvidesPointAndDirection()
        {
            var definition = ScriptableObject.CreateInstance<
                PointTargetingDefinition>();

            bool valid = definition.TryBuildContext(
                Request(new Vector2(0f, 20f)),
                out CastContext context,
                out _,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.HasTargetPoint, Is.True);
            Assert.That(context.TargetPoint.y, Is.EqualTo(8f).Within(0.001f));
            Assert.That(context.AimDirection, Is.EqualTo(Vector2.up));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SelectedTargeting_BuildsSelectedTargetContext()
        {
            var definition = ScriptableObject.CreateInstance<
                SelectedTargetingDefinition>();
            target.transform.position = new Vector3(3f, 0f, 0f);
            var request = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                target.transform.position,
                target);

            bool valid = definition.TryBuildContext(
                request,
                out CastContext context,
                out _,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.SelectedTarget, Is.SameAs(target));
            Assert.That(context.HasTargetPoint, Is.True);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SelectedTargeting_RejectsOutOfRangeTarget()
        {
            var definition = ScriptableObject.CreateInstance<
                SelectedTargetingDefinition>();
            target.transform.position = new Vector3(20f, 0f, 0f);
            var request = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                target.transform.position,
                target);

            bool valid = definition.TryBuildContext(
                request,
                out _,
                out _,
                out string reason);

            Assert.That(valid, Is.False);
            Assert.That(reason, Does.Contain("range"));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void ImmediateTargeting_ProvidesContextWithoutAim()
        {
            var definition = ScriptableObject.CreateInstance<
                ImmediateTargetingDefinition>();

            bool valid = definition.TryBuildContext(
                Request(Vector2.zero),
                out CastContext context,
                out _,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.HasAimDirection, Is.False);
            Assert.That(context.HasTargetPoint, Is.False);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void TargetingDefinition_ReportsRequirementCompatibility()
        {
            var definition = ScriptableObject.CreateInstance<
                SelectedTargetingDefinition>();

            Assert.That(definition.Supports(
                CastTargetingRequirement.SelectedTarget |
                CastTargetingRequirement.TargetPoint), Is.True);
            Assert.That(definition.Supports(
                (CastTargetingRequirement)(1 << 10)), Is.False);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void StagedTargeting_ProvidesFirstPointBeforeDeliveryIsComplete()
        {
            var targeting = ScriptableObject.CreateInstance<
                TwoPointTargetingDefinition>();
            var delivery = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            var firstRequest = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                new Vector2(1f, 0f),
                null);

            Assert.That(targeting.TryBuildContext(
                firstRequest,
                out CastContext firstContext,
                out PlayerTargetingPreview firstPreview,
                out _), Is.True);
            Assert.That(firstPreview.IsValid, Is.True);
            Assert.That(delivery.ValidateContext(firstContext, out _), Is.False);

            var secondRequest = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                new Vector2(3f, 0f),
                null,
                new SpellTargetingPayload(firstContext.TargetPoint));
            Assert.That(targeting.TryBuildContext(
                secondRequest,
                out CastContext completedContext,
                out _,
                out _), Is.True);
            Assert.That(delivery.ValidateContext(
                completedContext,
                out _), Is.True);

            Object.DestroyImmediate(delivery);
            Object.DestroyImmediate(targeting);
        }

        private PlayerTargetingRequest Request(Vector2 pointer)
        {
            return new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                pointer,
                null);
        }
    }
}
