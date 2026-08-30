using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class SpellAIComboCoordinatorTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject producer;
        private GameObject consumerA;
        private GameObject consumerB;

        [SetUp]
        public void SetUp()
        {
            SpellAIComboCoordinator.ClearAll();
            producer = CreateActor("Combo Producer", CombatTeam.Enemy);
            consumerA = CreateActor("Combo Consumer A", CombatTeam.Enemy);
            consumerB = CreateActor("Combo Consumer B", CombatTeam.Enemy);
        }

        [TearDown]
        public void TearDown()
        {
            SpellAIComboCoordinator.ClearAll();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                    Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void ProducedTag_UsesDeliveryGeometryAndLifecycle()
        {
            SpellDefinition oil = CreateSpell(
                produced: new[] { "Oil" });
            Component runtime = CreateRuntime("Oil Runtime");
            CastContext cast = CastContext.ForPoint(
                producer,
                Vector2.zero,
                new Vector2(3f, 1f));
            var execution = new SpellExecutionContext(oil, cast);

            execution.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                cast.TargetPoint,
                Vector2.zero,
                runtime).WithGeometry(
                    SpellDeliveryGeometry.Circle(cast.TargetPoint, 2f)));

            Assert.That(
                SpellAIComboCoordinator.ActiveOpportunityCount,
                Is.EqualTo(1));

            SpellDefinition ignite = CreateSpell(
                consumed: new[] { "oil" },
                requireCombo: true);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    consumerA,
                    Vector2.zero,
                    out SpellAIComboPlan plan,
                    out string rejection),
                Is.True,
                rejection);
            Assert.That(plan.HasOpportunity, Is.True);
            Assert.That(plan.TargetPoint.x, Is.EqualTo(3f).Within(0.001f));
            Assert.That(plan.TargetPoint.y, Is.EqualTo(1f).Within(0.001f));

            execution.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryExpired,
                null,
                cast.TargetPoint,
                Vector2.zero,
                runtime));

            Assert.That(
                SpellAIComboCoordinator.ActiveOpportunityCount,
                Is.Zero);
        }

        [Test]
        public void ConsumerReservation_BlocksSecondConsumerAndReleases()
        {
            RegisterOilOpportunity();
            SpellDefinition ignite = CreateSpell(
                consumed: new[] { "Oil" },
                requireCombo: true);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    consumerA,
                    Vector2.zero,
                    out SpellAIComboPlan firstPlan,
                    out string firstRejection),
                Is.True,
                firstRejection);
            CastContext firstCast = CastContext.ForPoint(
                consumerA,
                Vector2.zero,
                firstPlan.TargetPoint);
            Assert.That(
                SpellAIComboCoordinator.TryReservePlan(
                    firstPlan,
                    ignite,
                    consumerA,
                    firstCast,
                    2f,
                    out SpellAIComboReservation reservation,
                    out string reservationRejection),
                Is.True,
                reservationRejection);

            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    consumerB,
                    Vector2.zero,
                    out _,
                    out string blocked),
                Is.False);
            Assert.That(blocked, Does.Contain("No active allied setup"));

            SpellAIComboCoordinator.ReleaseReservation(
                reservation,
                consumerA);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    consumerB,
                    Vector2.zero,
                    out _,
                    out string released),
                Is.True,
                released);
        }

        [Test]
        public void CommittedConsumer_SpendsPlanningOpportunity()
        {
            RegisterOilOpportunity();
            SpellDefinition ignite = CreateSpell(
                consumed: new[] { "Oil" },
                requireCombo: true);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    consumerA,
                    Vector2.zero,
                    out SpellAIComboPlan plan,
                    out _),
                Is.True);
            CastContext cast = CastContext.ForPoint(
                consumerA,
                Vector2.zero,
                plan.TargetPoint);
            Assert.That(
                SpellAIComboCoordinator.TryReservePlan(
                    plan,
                    ignite,
                    consumerA,
                    cast,
                    2f,
                    out SpellAIComboReservation reservation,
                    out _),
                Is.True);

            SpellAIComboCoordinator.CommitReservation(
                reservation,
                ignite,
                consumerA);

            Assert.That(
                SpellAIComboCoordinator.ActiveOpportunityCount,
                Is.Zero);
        }

        [Test]
        public void SetupReservation_PreventsDuplicateSquadSetup()
        {
            SpellDefinition oil = CreateSpell(
                produced: new[] { "Oil" });
            Vector2 point = new Vector2(2f, 0f);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    oil,
                    producer,
                    point,
                    out SpellAIComboPlan setupPlan,
                    out string setupRejection),
                Is.True,
                setupRejection);
            CastContext cast = CastContext.ForPoint(
                producer,
                Vector2.zero,
                point);
            Assert.That(
                SpellAIComboCoordinator.TryReservePlan(
                    setupPlan,
                    oil,
                    producer,
                    cast,
                    2f,
                    out SpellAIComboReservation reservation,
                    out string reservationRejection),
                Is.True,
                reservationRejection);

            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    oil,
                    consumerA,
                    point,
                    out _,
                    out string duplicate),
                Is.False);
            Assert.That(duplicate, Does.Contain("equivalent squad"));

            SpellAIComboCoordinator.ReleaseReservation(
                reservation,
                producer);
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    oil,
                    consumerA,
                    point,
                    out _,
                    out string released),
                Is.True,
                released);
        }

        [Test]
        public void OpposingTeam_CannotConsumeEnemySetup()
        {
            RegisterOilOpportunity();
            GameObject playerConsumer = CreateActor(
                "Player Consumer",
                CombatTeam.Player);
            SpellDefinition ignite = CreateSpell(
                consumed: new[] { "Oil" },
                requireCombo: true);

            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    ignite,
                    playerConsumer,
                    Vector2.zero,
                    out _,
                    out string rejection),
                Is.False);
            Assert.That(rejection, Does.Contain("No active allied setup"));
        }

        [Test]
        public void SetupPlanning_RecognizesCompatibleSquadConsumer()
        {
            SpellDefinition oil = CreateSpell(
                produced: new[] { "Oil" });
            SetField(
                oil.AIAffordance,
                "requireSquadConsumerForSetup",
                true);
            SetField(
                oil.AIAffordance,
                "setupUtilityMultiplierWithConsumer",
                1.4f);

            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    oil,
                    producer,
                    Vector2.zero,
                    out _,
                    out string missingConsumer),
                Is.False);
            Assert.That(missingConsumer, Does.Contain("compatible"));

            var consumerTags = new HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                "oil"
            };
            Assert.That(
                SpellAIComboCoordinator.TryEvaluateSpell(
                    oil,
                    producer,
                    Vector2.zero,
                    consumerTags,
                    out SpellAIComboPlan plan,
                    out string ready),
                Is.True,
                ready);
            Assert.That(plan.IsSetupPlan, Is.True);
            Assert.That(plan.UtilityMultiplier,
                Is.EqualTo(1.4f).Within(0.001f));
            Assert.That(plan.Description, Does.Contain("compatible squad"));
        }

        private void RegisterOilOpportunity()
        {
            SpellDefinition oil = CreateSpell(
                produced: new[] { "Oil" });
            Component runtime = CreateRuntime("Shared Oil Runtime");
            CastContext cast = CastContext.ForPoint(
                producer,
                Vector2.zero,
                new Vector2(3f, 0f));
            new SpellExecutionContext(oil, cast).DispatchEvent(
                new SpellEventOccurrence(
                    SpellEventType.DeliveryStarted,
                    null,
                    cast.TargetPoint,
                    Vector2.zero,
                    runtime).WithGeometry(
                        SpellDeliveryGeometry.Circle(
                            cast.TargetPoint,
                            2f)));
        }

        private SpellDefinition CreateSpell(
            string[] produced = null,
            string[] consumed = null,
            bool requireCombo = false)
        {
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.RegenerateStableId();
            SpellAIAffordance guidance = new SpellAIAffordance();
            SetField(guidance, "usableByAI", true);
            SetField(
                guidance,
                "producesComboTags",
                new List<string>(produced ?? System.Array.Empty<string>()));
            SetField(
                guidance,
                "consumesComboTags",
                new List<string>(consumed ?? System.Array.Empty<string>()));
            SetField(guidance, "requireActiveComboToCast", requireCombo);
            SetField(guidance, "comboOpportunityRadius", 2f);
            SetField(guidance, "comboOpportunityLifetime", 10f);
            SetField(guidance, "comboTagActivationEvent",
                SpellEventType.DeliveryStarted);
            SetField(guidance, "suppressRedundantComboSetup", true);
            SetField(guidance, "consumeComboOpportunityOnCast", true);
            SetField(spell, "aiAffordance", guidance);
            return spell;
        }

        private GameObject CreateActor(string name, CombatTeam team)
        {
            GameObject actor = Track(new GameObject(name));
            actor.AddComponent<CombatTeamMember>().SetTeam(team);
            return actor;
        }

        private Component CreateRuntime(string name)
        {
            GameObject runtime = Track(new GameObject(name));
            return runtime.AddComponent<CircleCollider2D>();
        }

        private T Track<T>(T value) where T : Object
        {
            created.Add(value);
            return value;
        }

        private static void SetField(
            object instance,
            string fieldName,
            object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName}.");
            field.SetValue(instance, value);
        }
    }
}
