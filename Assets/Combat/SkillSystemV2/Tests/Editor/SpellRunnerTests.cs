using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class SpellRunnerTests
    {
        private GameObject caster;
        private SpellRunner runner;
        private TestDeliveryDefinition delivery;
        private SpellDefinition spell;

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
            caster.AddComponent<CombatTeamMember>().SetTeam(CombatTeam.Player);
            runner = caster.AddComponent<SpellRunner>();

            delivery = ScriptableObject.CreateInstance<
                TestDeliveryDefinition>();
            spell = CreateSpell(delivery, cooldown: 1f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
            Object.DestroyImmediate(caster);
        }

        [Test]
        public void InstantSpellRunsDeliveryAndCompletes()
        {
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);

            bool accepted = runner.TryCast(
                spell,
                context,
                out SpellCastFailure failure);

            Assert.That(accepted, Is.True);
            Assert.That(failure, Is.EqualTo(SpellCastFailure.None));
            Assert.That(runner.IsCasting, Is.False);
            Assert.That(delivery.LastExecution.BeginCount, Is.EqualTo(1));
            Assert.That(delivery.LastExecution.EndCount, Is.EqualTo(1));
        }

        [Test]
        public void CooldownRejectsThenAllowsAnotherCast()
        {
            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);

            Assert.That(
                runner.TryCast(spell, context, out _),
                Is.True);
            Assert.That(
                runner.TryCast(spell, context, out SpellCastFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(SpellCastFailure.OnCooldown));

            runner.TickRuntime(1.1f, 1.1f);

            Assert.That(
                runner.TryCast(spell, context, out failure),
                Is.True);
            Assert.That(failure, Is.EqualTo(SpellCastFailure.None));
        }

        [Test]
        public void TimedSpellAdvancesThroughEveryPhase()
        {
            SetTiming(
                spell,
                buildUp: 0.5f,
                firing: 0.25f,
                channel: 0.25f,
                recovery: 0.5f);

            CastContext context = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);

            Assert.That(
                runner.TryCast(spell, context, out _),
                Is.True);
            Assert.That(
                runner.CurrentPhase,
                Is.EqualTo(SpellCastPhase.BuildUp));

            runner.TickRuntime(0.5f, 0.5f);
            Assert.That(
                runner.CurrentPhase,
                Is.EqualTo(SpellCastPhase.Firing));
            Assert.That(delivery.LastExecution.BeginCount, Is.EqualTo(1));

            runner.TickRuntime(0.25f, 0.25f);
            Assert.That(
                runner.CurrentPhase,
                Is.EqualTo(SpellCastPhase.Channeling));

            runner.TickRuntime(0.25f, 0.25f);
            Assert.That(
                runner.CurrentPhase,
                Is.EqualTo(SpellCastPhase.Recovery));
            Assert.That(delivery.LastExecution.EndCount, Is.EqualTo(1));

            runner.TickRuntime(0.5f, 0.5f);
            Assert.That(runner.IsCasting, Is.False);
            Assert.That(
                runner.CurrentPhase,
                Is.EqualTo(SpellCastPhase.Idle));
        }

        private static SpellDefinition CreateSpell(
            DeliveryDefinition deliveryDefinition,
            float cooldown)
        {
            SpellDefinition result = ScriptableObject.CreateInstance<
                SpellDefinition>();
            var serialized = new SerializedObject(result);

            serialized.FindProperty("displayName").stringValue =
                "Test Spell";
            serialized.FindProperty("stableId").stringValue =
                "test-spell";
            serialized.FindProperty("delivery").objectReferenceValue =
                deliveryDefinition;
            serialized.FindProperty("cooldown").floatValue = cooldown;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return result;
        }

        private static void SetTiming(
            SpellDefinition definition,
            float buildUp,
            float firing,
            float channel,
            float recovery)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("timing.buildUpDuration").floatValue =
                buildUp;
            serialized.FindProperty("timing.firingDuration").floatValue =
                firing;
            serialized.FindProperty("timing.channelDuration").floatValue =
                channel;
            serialized.FindProperty("timing.recoveryDuration").floatValue =
                recovery;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    public sealed class TestDeliveryDefinition : DeliveryDefinition
    {
        public TestDeliveryExecution LastExecution { get; private set; }

        public override CastTargetingRequirement TargetingRequirement =>
            CastTargetingRequirement.None;

        public override ISpellDeliveryExecution CreateExecution(
            in SpellExecutionContext context)
        {
            LastExecution = new TestDeliveryExecution();
            return LastExecution;
        }
    }

    public sealed class TestDeliveryExecution : ISpellDeliveryExecution
    {
        public int BeginCount { get; private set; }
        public int EndCount { get; private set; }
        public bool IsComplete { get; private set; }

        public void Begin()
        {
            BeginCount++;
        }

        public void Tick(float deltaTime)
        {
        }

        public void End()
        {
            EndCount++;
            IsComplete = true;
        }

        public void Cancel()
        {
            IsComplete = true;
        }
    }
}
