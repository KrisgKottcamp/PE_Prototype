using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class FinalVocabularyTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject caster;
        private GameObject target;

        [SetUp]
        public void SetUp()
        {
            caster = Track(new GameObject("Vocabulary Caster"));
            target = Track(new GameObject("Vocabulary Target"));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                    Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void StatModifier_MultipliesAndRemovesCleanly()
        {
            SpellStatModifierEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpellStatModifierEffectDefinition>());
            SpellStatModifierController controller =
                target.AddComponent<SpellStatModifierController>();
            var settings = new SpellStatModifierSettings(
                SpellActorStat.MovementSpeed,
                SpellStatOperation.Multiply,
                0.6f,
                10f);

            controller.SetPersistent(effect, "slow", settings);
            Assert.That(
                controller.Evaluate(SpellActorStat.MovementSpeed),
                Is.EqualTo(0.6f).Within(0.001f));

            controller.Remove(effect, "slow");
            Assert.That(
                controller.Evaluate(SpellActorStat.MovementSpeed),
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DamageEffect_UsesDealtAndReceivedStatModifiers()
        {
            SpellVitality vitality = target.AddComponent<SpellVitality>();
            SpellStatModifierEffectDefinition source =
                Track(ScriptableObject.CreateInstance<
                    SpellStatModifierEffectDefinition>());
            caster.AddComponent<SpellStatModifierController>().SetPersistent(
                source,
                "damage-up",
                new SpellStatModifierSettings(
                    SpellActorStat.DamageDealt,
                    SpellStatOperation.Multiply,
                    2f,
                    10f));
            target.AddComponent<SpellStatModifierController>().SetPersistent(
                source,
                "resistance",
                new SpellStatModifierSettings(
                    SpellActorStat.DamageReceived,
                    SpellStatOperation.Multiply,
                    0.5f,
                    10f));
            DamageEffectDefinition damage =
                Track(ScriptableObject.CreateInstance<
                    DamageEffectDefinition>());
            CastContext cast = CastContext.ForTarget(
                caster,
                caster.transform.position,
                target);
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.left,
                1f);

            Assert.That(damage.Apply(
                context,
                new DamageEffectSettings(10f)), Is.True);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_CanTeleportAnotherActorWithDistanceLimit()
        {
            Rigidbody2D body = target.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                new Vector2(10f, 0f));
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.right,
                1f);
            var settings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.AimedPoint,
                10f,
                3f,
                lineOfSight: false);

            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void SpatialForce_PresenceOwnsMotionUntilRemoved()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            GameObject area = Track(new GameObject("Force Area"));
            area.transform.position = new Vector2(4f, 0f);
            CircleCollider2D source = area.AddComponent<CircleCollider2D>();
            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var settings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                6f,
                1f,
                blockByObstacles: false);
            CastContext cast = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.zero,
                1f,
                deliveryRuntime: source);

            Assert.That(effect.ApplyPresence(context, source, settings),
                Is.True);
            SpellActorMotionController2D controller =
                target.GetComponent<SpellActorMotionController2D>();
            Assert.That(controller.IsControllingMotion, Is.True);

            effect.RemovePresence(target, source, settings);
            Assert.That(controller.IsControllingMotion, Is.False);
        }

        [Test]
        public void PlacementRules_RejectDistanceBeyondPerSpellLimit()
        {
            SpellDefinition spell = CreatePointSpell();
            SpellPlacementRules rules = new SpellPlacementRules();
            SetField(rules, "maximumDistance", 3f);
            SetField(spell, "placementRules", rules);

            Assert.That(spell.TryResolveContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(5f, 0f)),
                out _,
                out string reason), Is.False);
            Assert.That(reason, Does.Contain("farther"));
        }

        [Test]
        public void PlacementRangeStat_CanIncreasePerSpellPlacementLimit()
        {
            SpellDefinition spell = CreatePointSpell();
            SpellPlacementRules rules = new SpellPlacementRules();
            SetField(rules, "maximumDistance", 3f);
            SetField(spell, "placementRules", rules);
            SpellStatModifierEffectDefinition source = Track(
                ScriptableObject.CreateInstance<
                    SpellStatModifierEffectDefinition>());
            caster.AddComponent<SpellStatModifierController>().SetPersistent(
                source,
                "placement-range",
                new SpellStatModifierSettings(
                    SpellActorStat.SpellPlacementRange,
                    SpellStatOperation.Multiply,
                    2f,
                    10f));

            Assert.That(spell.TryResolveContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(5f, 0f)),
                out _,
                out string reason), Is.True, reason);
        }

        [Test]
        public void PlacementRules_RejectBlockedLineOfSight()
        {
            SpellDefinition spell = CreatePointSpell();
            SpellPlacementRules rules = new SpellPlacementRules();
            SetField(rules, "requireLineOfSight", true);
            SetField(rules, "lineOfSightMask", (LayerMask)1);
            SetField(spell, "placementRules", rules);
            GameObject wall = Track(new GameObject("Placement Wall"));
            wall.transform.position = new Vector2(2f, 0f);
            wall.AddComponent<BoxCollider2D>().size =
                new Vector2(0.5f, 2f);
            Physics2D.SyncTransforms();

            Assert.That(spell.TryResolveContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(4f, 0f)),
                out _,
                out string reason), Is.False);
            Assert.That(reason, Does.Contain("blocked"));
        }

        [Test]
        public void FanEmission_DistributesShotsAcrossConfiguredArc()
        {
            var emission = new ProjectileEmissionSettings(
                ProjectileEmissionPattern.Fan,
                3,
                40f);

            Vector2 left = ProjectileDeliveryDefinition.ResolveShotDirection(
                Vector2.right,
                emission,
                0,
                1);
            Vector2 center = ProjectileDeliveryDefinition.ResolveShotDirection(
                Vector2.right,
                emission,
                1,
                1);
            Vector2 right = ProjectileDeliveryDefinition.ResolveShotDirection(
                Vector2.right,
                emission,
                2,
                1);

            Assert.That(Vector2.SignedAngle(Vector2.right, left),
                Is.EqualTo(-20f).Within(0.01f));
            Assert.That(Vector2.SignedAngle(Vector2.right, center),
                Is.Zero.Within(0.01f));
            Assert.That(Vector2.SignedAngle(Vector2.right, right),
                Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void ProjectileFalloff_ReachesConfiguredMinimumAtMaxRange()
        {
            var falloff = new ProjectileFalloffSettings(
                ProjectileDamageFalloff.DistanceTraveled,
                0.25f);

            Assert.That(falloff.Evaluate(0f),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(falloff.Evaluate(1f),
                Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void AIScoring_ValuesSatisfiedComboSetup()
        {
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            SpellAIAffordance data = new SpellAIAffordance();
            SetField(data, "usableByAI", true);
            SetField(data, "consumesComboTags",
                new List<string> { "Oil" });
            SetField(spell, "aiAffordance", data);
            var plain = new SpellAIDecisionContext(
                2f, 1, 1f, 1f, 0f, 0f);
            var combo = new SpellAIDecisionContext(
                2f,
                1,
                1f,
                1f,
                0f,
                0f,
                new HashSet<string> { "Oil" });

            Assert.That(
                SpellAIDecisionUtility.Score(spell, combo),
                Is.GreaterThan(SpellAIDecisionUtility.Score(spell, plain)));
        }

        private SpellDefinition CreatePointSpell()
        {
            PointClickDeliveryDefinition delivery = Track(
                ScriptableObject.CreateInstance<
                    PointClickDeliveryDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new PointClickDeliverySettings(null)));
            return spell;
        }

        private T Track<T>(T instance) where T : Object
        {
            created.Add(instance);
            return instance;
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
