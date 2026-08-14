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
        public void ActionPointPickupValue_AdjustsRewardParticleCount()
        {
            SpellStatModifierEffectDefinition source =
                Track(ScriptableObject.CreateInstance<
                    SpellStatModifierEffectDefinition>());
            caster.AddComponent<SpellStatModifierController>().SetPersistent(
                source,
                "ap-pickup-value",
                new SpellStatModifierSettings(
                    SpellActorStat.ActionPointPickupValue,
                    SpellStatOperation.Multiply,
                    2f,
                    10f));

            const int baseReward = 4;
            int adjustedReward =
                SpellActionPointPickupUtility.ResolveRewardValue(
                    caster,
                    baseReward);

            Assert.That(adjustedReward, Is.EqualTo(8));
            Assert.That(
                SpellActionPointPickupUtility.ResolveParticleCount(
                    baseReward,
                    baseReward,
                    4,
                    6),
                Is.EqualTo(1));
            Assert.That(
                SpellActionPointPickupUtility.ResolveParticleCount(
                    baseReward,
                    adjustedReward,
                    4,
                    6),
                Is.EqualTo(2));

            Assert.That(
                SpellActionPointPickupUtility.ResolveParticleCount(
                    5,
                    6,
                    4,
                    6),
                Is.EqualTo(3),
                "A modest AP increase must still add a visible particle " +
                "instead of remaining in the same four-AP bucket.");
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
        public void RelocateActor_AimedPointFallsBackToAimDirection()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);
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
                4f,
                lineOfSight: false);

            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_EventPointWorksForDefaultEffects()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                new Vector2(3f, 0f),
                Vector2.left,
                1f);
            var settings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.EventPoint,
                10f,
                8f,
                lineOfSight: false);

            Assert.That(context.HasDeliveryEvent, Is.False);
            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_DeliveryCenterUsesRuntimeTransform()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            GameObject delivery = Track(
                new GameObject("Relocation Delivery Runtime"));
            delivery.transform.position = new Vector2(5f, 0f);
            CircleCollider2D runtime =
                delivery.AddComponent<CircleCollider2D>();
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                new Vector2(2f, 0f));
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.left,
                1f,
                deliveryRuntime: runtime);
            var settings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.DeliveryCenter,
                10f,
                8f,
                lineOfSight: false);

            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_DeliveryCenterUsesPlacedAreaPointWithoutRuntime()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                new Vector2(7f, 0f));
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.left,
                1f);
            var settings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.DeliveryCenter,
                10f,
                8f,
                lineOfSight: false);

            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(7f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_SelectedTargetCanMoveDifferentRecipient()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            GameObject selected = Track(
                new GameObject("Relocation Selected Target"));
            selected.transform.position = new Vector2(6f, 0f);
            ActorRelocationEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            CastContext cast = CastContext.ForTarget(
                caster,
                Vector2.zero,
                selected);
            var context = new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.right,
                1f);
            var settings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.SelectedTarget,
                10f,
                8f,
                lineOfSight: false);

            Assert.That(effect.Apply(context, settings), Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_PlayerChoosesActorThenAimedPoint()
        {
            target.transform.position = new Vector2(2f, 0f);
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;

            SelectedTargetingDefinition actorTargeting = Track(
                ScriptableObject.CreateInstance<
                    SelectedTargetingDefinition>());
            PointTargetingDefinition pointTargeting = Track(
                ScriptableObject.CreateInstance<
                    PointTargetingDefinition>());
            InstantTargetDeliveryDefinition actorDelivery = Track(
                ScriptableObject.CreateInstance<
                    InstantTargetDeliveryDefinition>());
            PointClickDeliveryDefinition pointDelivery = Track(
                ScriptableObject.CreateInstance<
                    PointClickDeliveryDefinition>());
            ActorRelocationEffectDefinition effect = Track(
                ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());

            var destinationDelivery = new SpellDeliverySlot(
                pointDelivery,
                new PointClickDeliverySettings(pointTargeting));
            var relocation = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.AimedPoint,
                10f,
                8f,
                lineOfSight: false,
                supplementalDelivery: destinationDelivery);
            spell.ReplaceDelivery(new SpellDeliverySlot(
                actorDelivery,
                new InstantTargetDeliverySettings(actorTargeting)));
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                effect,
                relocation));
            spell.RegenerateStableId();
            SetField(
                spell,
                "targetFilter",
                new TargetFilter(TargetRelationship.Any));

            SpellRunner runner = caster.AddComponent<SpellRunner>();
            caster.AddComponent<TargetingTimeScaleController>();
            PlayerSpellTargetingController targeting =
                caster.AddComponent<PlayerSpellTargetingController>();
            SetField(targeting, "spellRunner", runner);

            Assert.That(targeting.BeginTargeting(
                spell,
                out PlayerTargetingFailure beginFailure), Is.True,
                beginFailure.ToString());
            Assert.That(targeting.UpdateAim(
                target.transform.position,
                target), Is.True);
            Assert.That(targeting.ConfirmTargeting(
                out PlayerTargetingFailure actorFailure,
                out _), Is.True, actorFailure.ToString());
            Assert.That(targeting.IsChoosingSupplementalTarget, Is.True);
            Assert.That(target.transform.position.x,
                Is.EqualTo(2f).Within(0.001f),
                "The actor must not move until the destination is confirmed.");

            Assert.That(targeting.UpdateAim(
                new Vector2(6f, 0f)), Is.True);
            Assert.That(targeting.ConfirmTargeting(
                out PlayerTargetingFailure pointFailure,
                out SpellCastFailure castFailure), Is.True,
                $"{pointFailure}: {castFailure}");
            SpellRelocationDestinationDelivery2D destinationRuntime =
                Object.FindObjectOfType<
                    SpellRelocationDestinationDelivery2D>();
            Assert.That(destinationRuntime, Is.Not.Null);
            Track(destinationRuntime.gameObject);
            Assert.That(targeting.IsTargeting, Is.False);
            Assert.That(target.transform.position.x,
                Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void RelocateActor_ProjectileMovesActorWhereItStops()
        {
            target.AddComponent<Rigidbody2D>().bodyType =
                RigidbodyType2D.Dynamic;
            GameObject blocker = Track(
                new GameObject("Relocation Projectile Blocker"));
            blocker.transform.position = new Vector2(3f, 0f);
            blocker.AddComponent<BoxCollider2D>().size =
                new Vector2(0.5f, 1f);
            SpellVitality blockerVitality =
                blocker.AddComponent<SpellVitality>();
            Physics2D.SyncTransforms();

            DirectionTargetingDefinition projectileTargeting = Track(
                ScriptableObject.CreateInstance<
                    DirectionTargetingDefinition>());
            ProjectileDeliveryDefinition projectileDelivery = Track(
                ScriptableObject.CreateInstance<
                    ProjectileDeliveryDefinition>());
            var projectileSettings = new ProjectileDeliverySettings(
                projectileTargeting,
                null,
                false,
                10f,
                5f,
                0f,
                (LayerMask)1,
                false,
                1,
                true,
                16);
            var destinationDelivery = new SpellDeliverySlot(
                projectileDelivery,
                projectileSettings);
            ActorRelocationEffectDefinition relocationEffect = Track(
                ScriptableObject.CreateInstance<
                    ActorRelocationEffectDefinition>());
            var relocationSettings = new ActorRelocationEffectSettings(
                ActorRelocationMode.InstantTeleport,
                ActorRelocationDestination.AimedPoint,
                10f,
                8f,
                lineOfSight: false,
                supplementalDelivery: destinationDelivery);

            DamageEffectDefinition damage = Track(
                ScriptableObject.CreateInstance<DamageEffectDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f)));
            SetField(
                spell,
                "targetFilter",
                new TargetFilter(TargetRelationship.Any));

            CastContext primary = CastContext.ForTarget(
                caster,
                Vector2.zero,
                target);
            CastContext destination = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);
            CastContext cast = primary.WithSupplementalTargeting(destination);
            var context = new SpellEffectContext(
                spell,
                cast,
                target,
                target.transform.position,
                Vector2.right,
                1f);

            Assert.That(relocationEffect.Apply(
                context,
                relocationSettings), Is.True);
            Assert.That(target.transform.position.x,
                Is.Zero.Within(0.001f),
                "The actor must wait for the projectile to stop.");

            SpellProjectile2D projectile =
                Object.FindObjectOfType<SpellProjectile2D>();
            SpellRelocationDestinationDelivery2D runtime =
                Object.FindObjectOfType<
                    SpellRelocationDestinationDelivery2D>();
            Assert.That(projectile, Is.Not.Null);
            Assert.That(runtime, Is.Not.Null);
            Track(projectile.gameObject);
            Track(runtime.gameObject);

            projectile.Step(1f);

            Assert.That(runtime.DestinationResolved, Is.True);
            Assert.That(runtime.RelocationApplied, Is.True);
            Assert.That(target.transform.position.x,
                Is.GreaterThan(2f));
            Assert.That(blockerVitality.CurrentHealth,
                Is.EqualTo(100f).Within(0.001f),
                "A destination delivery must not apply the spell's effects " +
                "to objects it hits.");
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
            Assert.That(
                SpellActorMotionUtility.IsControllingMotion(target),
                Is.True,
                "Every mover sharing this Rigidbody2D must be able to see " +
                "that Spatial Force currently owns actor movement.");

            effect.RemovePresence(target, source, settings);
            Assert.That(controller.IsControllingMotion, Is.False);
            Assert.That(
                SpellActorMotionUtility.IsControllingMotion(target),
                Is.False);
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
