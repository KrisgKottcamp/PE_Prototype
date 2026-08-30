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
            SpellAITacticalMemory.ClearAll();
            SpellAIThreatService.ClearAll();
            SpellAIComboCoordinator.ClearAll();
            caster = Track(new GameObject("Vocabulary Caster"));
            target = Track(new GameObject("Vocabulary Target"));
        }

        [TearDown]
        public void TearDown()
        {
            SpellAITacticalMemory.ClearAll();
            SpellAIThreatService.ClearAll();
            SpellAIComboCoordinator.ClearAll();
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
        public void MeleeArcPreview_UsesPerSpellRangeAndAngle()
        {
            DirectionTargetingDefinition directionTargeting = Track(
                ScriptableObject.CreateInstance<
                    DirectionTargetingDefinition>());
            MeleeArcDeliveryDefinition delivery = Track(
                ScriptableObject.CreateInstance<
                    MeleeArcDeliveryDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new MeleeArcDeliverySettings(
                    directionTargeting,
                    5.5f,
                    135f,
                    ~0,
                    24)));

            SpellRunner runner = caster.AddComponent<SpellRunner>();
            caster.AddComponent<TargetingTimeScaleController>();
            PlayerSpellTargetingController targeting =
                caster.AddComponent<PlayerSpellTargetingController>();
            SetField(targeting, "spellRunner", runner);

            Assert.That(targeting.BeginTargeting(
                spell,
                out PlayerTargetingFailure beginFailure), Is.True,
                beginFailure.ToString());
            Assert.That(targeting.UpdateAim(Vector2.right), Is.True);

            Assert.That(targeting.CurrentPreview.Shape,
                Is.EqualTo(PlayerTargetingPreviewShape.Cone));
            Assert.That(targeting.CurrentPreview.Range,
                Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(targeting.CurrentPreview.ConeAngle,
                Is.EqualTo(135f).Within(0.001f));
        }

        [Test]
        public void TargetingTime_ReleaseCannotRestoreStaleSlowBaseline()
        {
            float originalScale = Time.timeScale;
            float originalFixedDelta = Time.fixedDeltaTime;
            var owner = new object();
            try
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                TargetingTimeScaleController controller =
                    caster.AddComponent<TargetingTimeScaleController>();

                controller.Acquire(owner, 0.15f);
                Assert.That(Time.timeScale,
                    Is.EqualTo(0.15f).Within(0.0001f));

                // Simulate an outer menu restoring normal time before the
                // targeting cancellation callback arrives.
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                controller.Release(owner);

                Assert.That(Time.timeScale,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Time.fixedDeltaTime,
                    Is.EqualTo(0.02f).Within(0.0001f));
            }
            finally
            {
                Time.timeScale = originalScale;
                Time.fixedDeltaTime = originalFixedDelta;
            }
        }

        [Test]
        public void TargetingTime_ReleaseRestoresThroughTransientHitstop()
        {
            float originalScale = Time.timeScale;
            float originalFixedDelta = Time.fixedDeltaTime;
            var owner = new object();
            try
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                TargetingTimeScaleController controller =
                    caster.AddComponent<TargetingTimeScaleController>();

                controller.Acquire(owner, 0.15f);
                Time.timeScale = 0.04f;
                controller.Release(owner);

                Assert.That(Time.timeScale,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Time.fixedDeltaTime,
                    Is.EqualTo(0.02f).Within(0.0001f));
            }
            finally
            {
                Time.timeScale = originalScale;
                Time.fixedDeltaTime = originalFixedDelta;
            }
        }

        [Test]
        public void TargetingTime_PrunesInactiveTargetingOwner()
        {
            float originalScale = Time.timeScale;
            float originalFixedDelta = Time.fixedDeltaTime;
            try
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                TargetingTimeScaleController controller =
                    caster.AddComponent<TargetingTimeScaleController>();
                PlayerSpellTargetingController inactiveTargeting =
                    caster.AddComponent<PlayerSpellTargetingController>();

                Assert.That(inactiveTargeting.IsTargeting, Is.False);
                controller.Acquire(inactiveTargeting, 0.15f);
                Assert.That(controller.HasRequests, Is.True);
                controller.PruneInactiveOwnersNow();

                Assert.That(controller.HasRequests, Is.False);
                Assert.That(Time.timeScale,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(Time.fixedDeltaTime,
                    Is.EqualTo(0.02f).Within(0.0001f));
            }
            finally
            {
                Time.timeScale = originalScale;
                Time.fixedDeltaTime = originalFixedDelta;
            }
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
        public void SpatialForce_PullsSkillProjectileThroughItsChildSensor()
        {
            GameObject projectile = Track(
                new GameObject("Spatial Force Skill Projectile"));
            projectile.AddComponent<SpellProjectile2D>();
            GameObject sensor = new GameObject("Projectile Sensor");
            sensor.transform.SetParent(projectile.transform, false);
            Rigidbody2D sensorBody = sensor.AddComponent<Rigidbody2D>();
            sensorBody.bodyType = RigidbodyType2D.Kinematic;
            sensor.AddComponent<CircleCollider2D>().isTrigger = true;

            GameObject area = Track(new GameObject("Projectile Force Area"));
            area.transform.position = new Vector2(4f, 0f);
            CircleCollider2D source = area.AddComponent<CircleCollider2D>();
            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var settings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                8f,
                1f,
                blockByObstacles: false);
            GameObject resolved = SpellTargetResolver.Resolve(sensor);
            var context = new SpellEffectContext(
                null,
                CastContext.ForDirection(
                    caster,
                    Vector2.zero,
                    Vector2.right),
                resolved,
                projectile.transform.position,
                Vector2.zero,
                1f,
                deliveryRuntime: source);

            Assert.That(resolved, Is.SameAs(projectile));
            Assert.That(effect.ApplyPresence(context, source, settings),
                Is.True);
            SpellActorMotionController2D controller =
                projectile.GetComponent<SpellActorMotionController2D>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                sensor.GetComponent<SpellActorMotionController2D>(),
                Is.Null,
                "The child sensor must resolve to and move the projectile root.");

            controller.StepProjectileSpatialForce(0.5f);

            Assert.That(projectile.transform.position.x,
                Is.EqualTo(2f).Within(0.001f));
            effect.RemovePresence(projectile, source, settings);
            Assert.That(controller.IsControllingMotion, Is.False);
        }

        [Test]
        public void SpatialForce_GrenadePullSurvivesDetonationRuntime()
        {
            GameObject projectile = Track(
                new GameObject("Grenade Pull Projectile"));
            projectile.AddComponent<SpellProjectile2D>();

            GameObject grenadeObject = Track(
                new GameObject("Expiring Grenade Runtime"));
            SpellGrenade2D grenade =
                grenadeObject.AddComponent<SpellGrenade2D>();
            var grenadeSettings = new GrenadeDeliverySettings(
                null,
                null,
                8f,
                1.5f,
                0.1f,
                4f,
                ~0,
                GrenadeCollisionMode.Regular,
                0.8f,
                4,
                32);
            grenade.Launch(
                new SpellExecutionContext(
                    null,
                    CastContext.ForPoint(
                        caster,
                        new Vector2(4f, 0f),
                        new Vector2(4f, 0f))),
                grenadeSettings);

            Assert.That(
                ((ISpellDeliveryRadiusProvider)grenade).DeliveryRadius,
                Is.EqualTo(4f).Within(0.001f));

            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var forceSettings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                20f,
                1f,
                forceFalloff: SpatialForceFalloff.None,
                falloffRange: 0f,
                blockByObstacles: false,
                useCurve: true,
                curveExponent: 2f,
                curveSofteningDistance: 0.25f);
            var effectContext = new SpellEffectContext(
                null,
                CastContext.ForDirection(
                    caster,
                    Vector2.zero,
                    Vector2.right),
                projectile,
                projectile.transform.position,
                Vector2.zero,
                1f,
                deliveryRuntime: grenade);

            Assert.That(effect.Apply(effectContext, forceSettings), Is.True);
            SpellActorMotionController2D controller =
                projectile.GetComponent<SpellActorMotionController2D>();

            // Grenades are destroyed immediately after applying their
            // detonation effects. The timed force must remain independently
            // owned until its configured Duration expires.
            Object.DestroyImmediate(grenadeObject);
            controller.StepProjectileSpatialForce(0.5f);

            Assert.That(projectile.transform.position.x,
                Is.EqualTo(1f).Within(0.01f),
                "The pull must survive grenade destruction and auto-scale " +
                "from the four-unit explosion radius.");
        }

        [Test]
        public void SpatialCurve_PreservesEntryPathAndExitMomentum()
        {
            GameObject projectile = Track(
                new GameObject("Curved Skill Projectile"));
            projectile.transform.position = new Vector2(4f, 0f);
            projectile.AddComponent<SpellProjectile2D>();
            GameObject area = Track(new GameObject("Gravity Center"));
            area.transform.position = Vector2.zero;
            CircleCollider2D source = area.AddComponent<CircleCollider2D>();
            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var settings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                20f,
                1f,
                forceFalloff: SpatialForceFalloff.None,
                falloffRange: 4f,
                blockByObstacles: false,
                useCurve: true,
                curveExponent: 2f,
                curveSofteningDistance: 0.25f,
                preserveCurveMomentum: true);
            var context = new SpellEffectContext(
                null,
                CastContext.ForDirection(
                    caster,
                    Vector2.zero,
                    Vector2.right),
                projectile,
                projectile.transform.position,
                Vector2.zero,
                1f,
                deliveryRuntime: source);

            Assert.That(effect.ApplyPresence(context, source, settings),
                Is.True);
            SpellActorMotionController2D controller =
                projectile.GetComponent<SpellActorMotionController2D>();
            controller.StepProjectileSpatialForce(0.25f);

            projectile.transform.position += Vector3.up;
            controller.StepProjectileSpatialForce(0.25f);

            Assert.That(projectile.transform.position.x, Is.LessThan(3.5f));
            Assert.That(projectile.transform.position.y, Is.GreaterThan(0.8f),
                "Gravity must bend rather than replace perpendicular entry motion.");

            effect.RemovePresence(projectile, source, settings);
            Vector2 exitPoint = projectile.transform.position;
            controller.StepProjectileSpatialForce(0.25f);
            Vector2 exitTravel =
                (Vector2)projectile.transform.position - exitPoint;

            Assert.That(controller.IsControllingMotion, Is.True);
            Assert.That(exitTravel.magnitude, Is.GreaterThan(0.1f));
            Assert.That(exitTravel.x, Is.LessThan(0f));
            Assert.That(exitTravel.y, Is.LessThan(0f));
        }

        [Test]
        public void SpatialCurve_GravityStrengthIncreasesNearCenter()
        {
            GameObject farProjectile = Track(
                new GameObject("Far Gravity Projectile"));
            farProjectile.transform.position = new Vector2(4f, 0f);
            farProjectile.AddComponent<SpellProjectile2D>();
            GameObject nearProjectile = Track(
                new GameObject("Near Gravity Projectile"));
            nearProjectile.transform.position = new Vector2(1f, 0f);
            nearProjectile.AddComponent<SpellProjectile2D>();
            GameObject area = Track(new GameObject("Gravity Comparison Area"));
            area.transform.position = Vector2.zero;
            CircleCollider2D source = area.AddComponent<CircleCollider2D>();
            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var settings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                20f,
                1f,
                forceFalloff: SpatialForceFalloff.None,
                falloffRange: 4f,
                blockByObstacles: false,
                useCurve: true,
                curveExponent: 2f,
                curveSofteningDistance: 0.25f);
            CastContext cast = CastContext.ForDirection(
                caster,
                Vector2.zero,
                Vector2.right);

            Assert.That(effect.ApplyPresence(
                new SpellEffectContext(
                    null,
                    cast,
                    farProjectile,
                    farProjectile.transform.position,
                    Vector2.zero,
                    1f,
                    deliveryRuntime: source),
                source,
                settings), Is.True);
            Assert.That(effect.ApplyPresence(
                new SpellEffectContext(
                    null,
                    cast,
                    nearProjectile,
                    nearProjectile.transform.position,
                    Vector2.zero,
                    1f,
                    deliveryRuntime: source),
                source,
                settings), Is.True);

            SpellActorMotionController2D farController =
                farProjectile.GetComponent<SpellActorMotionController2D>();
            SpellActorMotionController2D nearController =
                nearProjectile.GetComponent<SpellActorMotionController2D>();
            farController.StepProjectileSpatialForce(0.1f);
            nearController.StepProjectileSpatialForce(0.1f);

            Assert.That(
                nearController.ForcedVelocity.magnitude,
                Is.GreaterThan(farController.ForcedVelocity.magnitude * 4f));
        }

        [Test]
        public void SpatialCurve_ActorEntryMomentumReturnsOnExit()
        {
            target.transform.position = new Vector2(4f, 0f);
            Rigidbody2D body = target.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.linearVelocity = Vector2.up * 3f;
            GameObject area = Track(new GameObject("Actor Gravity Area"));
            area.transform.position = Vector2.zero;
            CircleCollider2D source = area.AddComponent<CircleCollider2D>();
            SpatialForceEffectDefinition effect =
                Track(ScriptableObject.CreateInstance<
                    SpatialForceEffectDefinition>());
            var settings = new SpatialForceEffectSettings(
                SpatialForceDirection.TowardCenter,
                SpatialForceCenter.DeliveryCenter,
                4f,
                20f,
                1f,
                falloffRange: 4f,
                blockByObstacles: false,
                useCurve: true,
                preserveCurveMomentum: true);
            var context = new SpellEffectContext(
                null,
                CastContext.ForDirection(
                    caster,
                    Vector2.zero,
                    Vector2.right),
                target,
                target.transform.position,
                Vector2.zero,
                1f,
                deliveryRuntime: source);

            Assert.That(effect.ApplyPresence(context, source, settings),
                Is.True);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero),
                "The force controller should own the captured entry momentum while inside.");

            effect.RemovePresence(target, source, settings);

            Assert.That(body.linearVelocity.x, Is.Zero.Within(0.001f));
            Assert.That(body.linearVelocity.y,
                Is.EqualTo(3f).Within(0.001f));
            Assert.That(
                SpellActorMotionUtility.IsControllingMotion(target),
                Is.False,
                "After exit, actor momentum returns to Rigidbody2D physics.");
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

        [Test]
        public void AISupportScoring_AllowsApproachForWoundedAlly()
        {
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            SpellAIAffordance data = new SpellAIAffordance();
            SetField(data, "usableByAI", true);
            SetField(data, "intents", SpellAIIntent.Support);
            SetField(
                data,
                "targetPreference",
                SpellAITargetPreference.LowestHealthAlly);
            SetField(data, "healthThreshold", 0.65f);
            SetField(data, "preferredMaximumRange", 1.5f);
            SetField(data, "moveIntoRangeBeforeCasting", true);
            SetField(spell, "aiAffordance", data);

            var tooFarWithoutApproach = new SpellAIDecisionContext(
                5f, 1, 1f, 0.4f, 0f, 0f);
            var approachingWoundedAlly = new SpellAIDecisionContext(
                5f, 1, 1f, 0.4f, 0f, 0f, null, true);
            var approachingHealthyAlly = new SpellAIDecisionContext(
                5f, 1, 1f, 0.9f, 0f, 0f, null, true);

            Assert.That(
                SpellAIDecisionUtility.Score(
                    spell,
                    tooFarWithoutApproach),
                Is.EqualTo(float.NegativeInfinity));
            Assert.That(
                SpellAIDecisionUtility.Score(
                    spell,
                    approachingWoundedAlly),
                Is.GreaterThan(0f));
            Assert.That(
                SpellAIDecisionUtility.Score(
                    spell,
                    approachingHealthyAlly),
                Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void EnemyHealth_ReceivesGenericSpellHealing()
        {
            System.Type enemyHealthType = System.Type.GetType(
                "EnemyHealth, Assembly-CSharp");
            Assert.That(enemyHealthType, Is.Not.Null);

            GameObject enemy = Track(new GameObject("Healing Test Enemy"));
            Component healthComponent = enemy.AddComponent(enemyHealthType);
            var healingReceiver = healthComponent as ISpellHealingReceiver;
            Assert.That(healingReceiver, Is.Not.Null);

            enemyHealthType.GetMethod("Init").Invoke(
                healthComponent,
                new object[] { 30 });
            enemyHealthType.GetMethod("TakeDamage").Invoke(
                healthComponent,
                new object[] { 12 });
            var effectContext = new SpellEffectContext(
                null,
                default,
                enemy,
                enemy.transform.position,
                Vector2.zero,
                1f);

            bool healed = healingReceiver.TryReceiveHealing(
                new SpellHealingRequest(effectContext, 7f, false),
                out SpellHealingResult result);
            int currentHP = (int)enemyHealthType
                .GetProperty("CurrentHP")
                .GetValue(healthComponent);

            Assert.That(healed, Is.True);
            Assert.That(currentHP, Is.EqualTo(25));
            Assert.That(result.AppliedAmount, Is.EqualTo(7f));
        }

        [Test]
        public void EnemyActionRunner_ProtectsActiveSkillFromOrdinaryOrders()
        {
            System.Type runnerType = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyActionRunnerV2, " +
                "Assembly-CSharp");
            System.Type actionKindType = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyActionKindV2, " +
                "Assembly-CSharp");
            Assert.That(runnerType, Is.Not.Null);
            Assert.That(actionKindType, Is.Not.Null);

            MethodInfo policy = runnerType.GetMethod(
                "CanReplaceCurrentAction",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(policy, Is.Not.Null);
            MethodInfo threatPolicy = runnerType.GetMethod(
                "MeetsSupportThreatInterruptThreshold",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(threatPolicy, Is.Not.Null);
            MethodInfo planningCancelPolicy = runnerType.GetMethod(
                "CanPlanningCancelCurrent",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(planningCancelPolicy, Is.Not.Null);

            object castSkill = System.Enum.Parse(
                actionKindType,
                "CastSkill");
            object approachAndCast = System.Enum.Parse(
                actionKindType,
                "ApproachAndCastSkill");
            object moveToSlot = System.Enum.Parse(
                actionKindType,
                "MoveToSlot");
            object attack = System.Enum.Parse(
                actionKindType,
                "AttackPattern");
            object evade = System.Enum.Parse(
                actionKindType,
                "EvadeThreat");
            object holdForSupport = System.Enum.Parse(
                actionKindType,
                "HoldForSupport");

            Assert.That(
                (bool)policy.Invoke(
                    null,
                    new[] { castSkill, moveToSlot }),
                Is.False);
            Assert.That(
                (bool)policy.Invoke(
                    null,
                    new[] { approachAndCast, attack }),
                Is.False);
            Assert.That(
                (bool)policy.Invoke(
                    null,
                    new[] { castSkill, evade }),
                Is.True);
            Assert.That(
                (bool)policy.Invoke(
                    null,
                    new[] { holdForSupport, evade }),
                Is.False);
            Assert.That(
                (bool)threatPolicy.Invoke(
                    null,
                    new object[] { 0.8f, 0.72f, 0.5f, 0.9f, false }),
                Is.True);
            Assert.That(
                (bool)threatPolicy.Invoke(
                    null,
                    new object[] { 0.6f, 0.72f, 0.2f, 0.9f, false }),
                Is.False);
            Assert.That(
                (bool)threatPolicy.Invoke(
                    null,
                    new object[] { 0.8f, 0.72f, 1.5f, 0.9f, false }),
                Is.False);
            Assert.That(
                (bool)planningCancelPolicy.Invoke(
                    null,
                    new[] { castSkill, (object)true }),
                Is.False,
                "Squad replanning must not cancel a committed Support cast.");
            Assert.That(
                (bool)planningCancelPolicy.Invoke(
                    null,
                    new[] { approachAndCast, (object)true }),
                Is.False,
                "Repositioning must not cancel a Support approach/cast.");
            Assert.That(
                (bool)planningCancelPolicy.Invoke(
                    null,
                    new[] { holdForSupport, (object)false }),
                Is.False,
                "Squad replanning must not release the coordinated target.");
            Assert.That(
                (bool)planningCancelPolicy.Invoke(
                    null,
                    new[] { attack, (object)false }),
                Is.True,
                "Ordinary attacks should remain replaceable by replanning.");
        }

        [Test]
        public void AISupportCoordination_DefaultsToImminentThreatInterrupt()
        {
            var guidance = new SpellAIAffordance();

            Assert.That(
                guidance.RequestTargetHoldDuringSupportCast,
                Is.True);
            Assert.That(
                guidance.InterruptSupportCastWhenDamaged,
                Is.False);
            Assert.That(
                guidance.InterruptSupportCastForImminentThreat,
                Is.True);
            Assert.That(
                guidance.SupportCastThreatInterruptScore,
                Is.EqualTo(0.72f).Within(0.001f));
            Assert.That(
                guidance.SupportCastThreatInterruptWindow,
                Is.EqualTo(0.9f).Within(0.001f));
        }

        [Test]
        public void AISupportCoordination_MigratesExistingSpellDefaults()
        {
            var guidance = new SpellAIAffordance();
            SetField(guidance, "interruptSupportCastWhenDamaged", true);
            SetField(guidance, "interruptSupportCastForImminentThreat", false);
            SetField(guidance, "supportCastThreatInterruptScore", 0f);
            SetField(guidance, "supportCastThreatInterruptWindow", 0f);
            SetField(guidance, "supportCoordinationVersion", 1);

            guidance.OnAfterDeserialize();

            Assert.That(
                guidance.RequestTargetHoldDuringSupportCast,
                Is.True);
            Assert.That(
                guidance.InterruptSupportCastWhenDamaged,
                Is.False);
            Assert.That(
                guidance.InterruptSupportCastForImminentThreat,
                Is.True);
            Assert.That(
                guidance.SupportCastThreatInterruptScore,
                Is.EqualTo(0.72f).Within(0.001f));
            Assert.That(
                guidance.SupportCastThreatInterruptWindow,
                Is.EqualTo(0.9f).Within(0.001f));
        }

        [Test]
        public void AITargetPrediction_ClampsLeadDistance()
        {
            Vector2 predicted = SpellAITargetingUtility.PredictTargetPoint(
                Vector2.zero,
                new Vector2(10f, 0f),
                1f,
                3f);

            Assert.That(predicted, Is.EqualTo(new Vector2(3f, 0f)));
        }

        [Test]
        public void AITargetCandidates_ControlIntentIncludesEscapeCutoff()
        {
            var candidates = new List<Vector2>();
            SpellAITargetingUtility.BuildPointCandidates(
                candidates,
                Vector2.zero,
                new Vector2(1f, 0f),
                Vector2.right,
                0.75f,
                4,
                SpellAIPlacementIntent.ControlEscapeRoute);

            Assert.That(candidates, Does.Contain(new Vector2(1.75f, 0f)));
            Assert.That(candidates.Count, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void AITargetPrediction_UsesProjectileTravelTime()
        {
            ProjectileDeliveryDefinition delivery = Track(
                ScriptableObject.CreateInstance<ProjectileDeliveryDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new ProjectileDeliverySettings(
                    null,
                    null,
                    false,
                    4f,
                    20f,
                    0.1f,
                    ~0,
                    false,
                    1,
                    true,
                    16)));

            float delay = SpellAITargetingUtility.EstimateArrivalDelay(
                spell,
                Vector2.zero,
                new Vector2(8f, 0f),
                5f);

            Assert.That(delay, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void AITargetPrediction_ExposesAuthoredPlacementLookahead()
        {
            SpellAIAffordance guidance = new SpellAIAffordance();
            SetField(guidance, "placementLookaheadSeconds", 0.65f);

            Assert.That(
                guidance.PlacementLookaheadSeconds,
                Is.EqualTo(0.65f).Within(0.001f));
            Vector2 predicted = SpellAITargetingUtility.PredictTargetPoint(
                Vector2.zero,
                new Vector2(2f, 0f),
                guidance.PlacementLookaheadSeconds,
                3f);
            Assert.That(predicted.x, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(predicted.y, Is.Zero.Within(0.001f));
        }

        [Test]
        public void AITacticalMemory_EnforcesPerSpellRecastCadence()
        {
            SpellDefinition spell = CreateLingeringAreaSpell();
            SpellAIAffordance guidance = new SpellAIAffordance();
            SetField(guidance, "minimumAIRecastInterval", 10f);
            SetField(guidance, "maximumActiveInstancesPerCaster", 0);
            SetField(guidance, "maximumActiveInstancesPerSquad", 0);
            SetField(spell, "aiAffordance", guidance);
            CastContext cast = CastContext.ForPoint(
                caster,
                caster.transform.position,
                new Vector2(2f, 0f));

            Assert.That(
                SpellAITacticalMemory.TryEvaluate(
                    spell,
                    caster,
                    cast,
                    out _,
                    out _),
                Is.True);
            SpellAITacticalMemory.RecordCast(spell, caster, cast);

            Assert.That(
                SpellAITacticalMemory.TryEvaluate(
                    spell,
                    caster,
                    cast,
                    out _,
                    out string rejection),
                Is.False);
            Assert.That(rejection, Does.Contain("recast cadence"));
        }

        [Test]
        public void AITacticalMemory_RejectsEquivalentPersistentOverlap()
        {
            SpellDefinition spell = CreateLingeringAreaSpell();
            SpellAIAffordance guidance = new SpellAIAffordance();
            SetField(guidance, "minimumAIRecastInterval", 0f);
            SetField(guidance, "maximumActiveInstancesPerCaster", 0);
            SetField(guidance, "maximumActiveInstancesPerSquad", 0);
            SetField(guidance, "allowEquivalentOverlap", false);
            SetField(spell, "aiAffordance", guidance);
            CastContext first = CastContext.ForPoint(
                caster,
                caster.transform.position,
                new Vector2(2f, 0f));
            SpellAITacticalMemory.RecordCast(spell, caster, first);
            CastContext overlap = CastContext.ForPoint(
                caster,
                caster.transform.position,
                new Vector2(2.25f, 0f));
            CastContext separate = CastContext.ForPoint(
                caster,
                caster.transform.position,
                new Vector2(8f, 0f));

            Assert.That(
                SpellAITacticalMemory.TryEvaluate(
                    spell,
                    caster,
                    overlap,
                    out _,
                    out string rejection),
                Is.False);
            Assert.That(rejection, Does.Contain("already covers"));
            Assert.That(
                SpellAITacticalMemory.TryEvaluate(
                    spell,
                    caster,
                    separate,
                    out float multiplier,
                    out _),
                Is.True);
            Assert.That(multiplier, Is.EqualTo(1f));
        }

        [Test]
        public void AIThreatPerception_StandingInsideAreaRequestsLeaveArea()
        {
            PrepareOpposingTeams();
            SpellDefinition spell = CreateAreaThreatSpell(2f);
            target.transform.position = new Vector2(3f, 0f);
            CastContext cast = CastContext.ForPoint(
                caster,
                caster.transform.position,
                target.transform.position);
            RegisterRuntimeThreat(
                spell,
                cast,
                target.transform.position,
                2f,
                out _);

            bool found = SpellAIThreatService.TryFindMostRelevantThreat(
                target,
                target.transform.position,
                Vector2.zero,
                6f,
                0.35f,
                1f,
                out SpellAIThreatEvaluation threat);

            Assert.That(found, Is.True);
            Assert.That(threat.IsInside, Is.True);
            Assert.That(
                threat.SuggestedReactions & SpellAIReaction.LeaveArea,
                Is.Not.EqualTo(SpellAIReaction.None));
            Assert.That(threat.TimeToImpact, Is.Zero.Within(0.001f));
        }

        [Test]
        public void AIThreatPerception_PlannedPathDetectsAreaBeforeEntry()
        {
            PrepareOpposingTeams();
            SpellDefinition spell = CreateAreaThreatSpell(1f);
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                new Vector2(3f, 0f));
            RegisterRuntimeThreat(
                spell,
                cast,
                new Vector2(3f, 0f),
                1f,
                out _);

            bool found = SpellAIThreatService.TryFindMostRelevantThreat(
                target,
                Vector2.zero,
                new Vector2(3f, 0f),
                6f,
                0.25f,
                1.5f,
                out SpellAIThreatEvaluation threat);

            Assert.That(found, Is.True);
            Assert.That(threat.IsInside, Is.False);
            Assert.That(threat.TimeToImpact, Is.LessThan(1.5f));
            Assert.That(threat.Clearance, Is.LessThanOrEqualTo(0f));
        }

        [Test]
        public void AIThreatPerception_FriendlyTargetRulesIgnoreThreat()
        {
            caster.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Enemy);
            target.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Enemy);
            SpellDefinition spell = CreateAreaThreatSpell(2f);
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                target.transform.position);
            RegisterRuntimeThreat(
                spell,
                cast,
                target.transform.position,
                2f,
                out _);

            Assert.That(
                SpellAIThreatService.TryFindMostRelevantThreat(
                    target,
                    target.transform.position,
                    Vector2.zero,
                    6f,
                    0.35f,
                    1f,
                    out _),
                Is.False);
        }

        [Test]
        public void AIThreatPerception_DeliveryStoppedImmediatelyForgetsRuntime()
        {
            PrepareOpposingTeams();
            SpellDefinition spell = CreateAreaThreatSpell(2f);
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                target.transform.position);
            RegisterRuntimeThreat(
                spell,
                cast,
                target.transform.position,
                2f,
                out Component runtime);
            Assert.That(SpellAIThreatService.ActiveThreatCount, Is.EqualTo(1));

            var context = new SpellExecutionContext(spell, cast);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStopped,
                null,
                target.transform.position,
                Vector2.zero,
                runtime));

            Assert.That(SpellAIThreatService.ActiveThreatCount, Is.Zero);
        }

        [Test]
        public void AIThreatPerception_ZeroLengthTelegraphIsNotRegistered()
        {
            PrepareOpposingTeams();
            SpellDefinition spell = CreateAreaThreatSpell(2f);
            SpellAIAffordance guidance = spell.AIAffordance;
            SetField(guidance, "telegraphDuration", 1f);
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                target.transform.position);

            new SpellExecutionContext(spell, cast).DispatchEvent(
                new SpellEventOccurrence(
                    SpellEventType.CastStarted,
                    null,
                    cast.TargetPoint,
                    Vector2.zero));

            Assert.That(
                SpellAIThreatService.ActiveThreatCount,
                Is.Zero,
                "A zero-build-up cast has no visible pre-activation window " +
                "and must not grant frame-perfect foreknowledge.");
        }

        [Test]
        public void AIThreatGeometry_SegmentFootprintUsesAuthoredWidth()
        {
            SpellDeliveryGeometry wire = SpellDeliveryGeometry.Segment(
                new Vector2(-2f, 0f),
                new Vector2(2f, 0f),
                0.2f);

            float inside = SpellAIThreatService.SignedClearance(
                wire,
                new Vector2(0f, 0.15f),
                0.1f,
                out Vector2 away);
            float outside = SpellAIThreatService.SignedClearance(
                wire,
                new Vector2(0f, 1f),
                0.1f,
                out _);

            Assert.That(inside, Is.LessThanOrEqualTo(0f));
            Assert.That(outside, Is.GreaterThan(0f));
            Assert.That(away.y, Is.GreaterThan(0f));
        }

        [Test]
        public void EnemySkillVerticalSlice_RuntimeTypesAreAvailable()
        {
            System.Type actionKind = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyActionKindV2, Assembly-CSharp");
            System.Type actionOrder = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyActionOrderV2, Assembly-CSharp");
            System.Type solver = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemySpellTargetingSolverV2, Assembly-CSharp");
            System.Type executor = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemySkillExecutorV2, Assembly-CSharp");
            System.Type resourceProvider = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemySpellResourceProviderV2, Assembly-CSharp");
            System.Type profile = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyAIV2Profile, Assembly-CSharp");
            System.Type threatPerception = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemySpellThreatPerceptionV2, Assembly-CSharp");
            System.Type threatProfile = System.Type.GetType(
                "ProjectEri.EnemyAI.V2.EnemyThreatResponseProfileV2, Assembly-CSharp");
            System.Type comboCoordinator =
                typeof(SpellAIComboCoordinator);
            System.Type enemyHealth = System.Type.GetType(
                "EnemyHealth, Assembly-CSharp");
            System.Type buildUpControl = System.Type.GetType(
                "SpellBuildUpControl2D, Assembly-CSharp");

            Assert.That(actionKind, Is.Not.Null);
            Assert.That(actionOrder, Is.Not.Null);
            Assert.That(System.Enum.IsDefined(actionKind, "CastSkill"), Is.True);
            Assert.That(
                System.Enum.IsDefined(actionKind, "EvadeThreat"),
                Is.True);
            Assert.That(
                System.Enum.IsDefined(
                    actionKind,
                    "ApproachAndCastSkill"),
                Is.True);
            Assert.That(solver, Is.Not.Null);
            Assert.That(executor, Is.Not.Null);
            Assert.That(resourceProvider, Is.Not.Null);
            Assert.That(profile, Is.Not.Null);
            Assert.That(threatPerception, Is.Not.Null);
            Assert.That(threatProfile, Is.Not.Null);
            Assert.That(comboCoordinator, Is.Not.Null);
            Assert.That(buildUpControl, Is.Not.Null);
            Assert.That(actionOrder.GetField("threatId"), Is.Not.Null);
            Assert.That(actionOrder.GetField("threatScore"), Is.Not.Null);
            Assert.That(
                actionOrder.GetField("threatTimeToImpact"),
                Is.Not.Null);
            Assert.That(actionOrder.GetField("threatIsInside"), Is.Not.Null);
            Assert.That(
                profile.GetField("minimumSecondsBetweenSkillStarts"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("minimumLegacyAttacksBetweenSkills"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("maximumConsecutiveSkillActions"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("enableSpellThreatReactions"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("maximumConcurrentThreatReactions"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("emergencyThreatScore"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("enableSquadComboCoordination"),
                Is.Not.Null);
            Assert.That(
                profile.GetField("maximumConcurrentComboReservations"),
                Is.Not.Null);
            Assert.That(enemyHealth, Is.Not.Null);
            Assert.That(
                enemyHealth.GetField("OnDamaged"),
                Is.Not.Null);
            Assert.That(
                typeof(ISpellHealingReceiver).IsAssignableFrom(
                    enemyHealth),
                Is.True);
            Assert.That(
                typeof(ISpellResourceProvider).IsAssignableFrom(
                    resourceProvider),
                Is.True);
        }

        [Test]
        public void AIIntentFlags_ExecutePersistsAlongsideDamage()
        {
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            var serialized = new UnityEditor.SerializedObject(spell);
            UnityEditor.SerializedProperty intents =
                serialized.FindProperty("aiAffordance.intents");

            Assert.That(intents, Is.Not.Null);
            intents.intValue = (int)(
                SpellAIIntent.Damage |
                SpellAIIntent.Execute);
            serialized.ApplyModifiedProperties();
            serialized.Update();

            SpellAIIntent saved = (SpellAIIntent)serialized
                .FindProperty("aiAffordance.intents")
                .intValue;
            Assert.That(
                (saved & SpellAIIntent.Damage) != 0,
                Is.True);
            Assert.That(
                (saved & SpellAIIntent.Execute) != 0,
                Is.True);
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

        private SpellDefinition CreateLingeringAreaSpell()
        {
            LingeringAreaDeliveryDefinition delivery = Track(
                ScriptableObject.CreateInstance<
                    LingeringAreaDeliveryDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new LingeringAreaDeliverySettings(
                    null,
                    2f,
                    4f,
                    0.25f,
                    ~0,
                    16,
                    Color.cyan,
                    0)));
            return spell;
        }

        private SpellDefinition CreateAreaThreatSpell(float radius)
        {
            LingeringAreaDeliveryDefinition delivery = Track(
                ScriptableObject.CreateInstance<
                    LingeringAreaDeliveryDefinition>());
            SpellDefinition spell = Track(
                ScriptableObject.CreateInstance<SpellDefinition>());
            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new LingeringAreaDeliverySettings(
                    null,
                    radius,
                    4f,
                    0.25f,
                    ~0,
                    16,
                    Color.cyan,
                    0)));
            SetField(
                spell,
                "targetFilter",
                new TargetFilter(TargetRelationship.Enemies));
            SpellAIAffordance guidance = new SpellAIAffordance();
            SetField(guidance, "intents", SpellAIIntent.Control);
            SetField(
                guidance,
                "suggestedReactions",
                SpellAIReaction.LeaveArea |
                SpellAIReaction.DodgeSideways);
            SetField(guidance, "reactionUrgency", 1f);
            SetField(spell, "aiAffordance", guidance);
            return spell;
        }

        private void RegisterRuntimeThreat(
            SpellDefinition spell,
            in CastContext cast,
            Vector2 center,
            float radius,
            out Component runtime)
        {
            GameObject runtimeObject = Track(
                new GameObject("Vocabulary Delivery Runtime"));
            runtimeObject.transform.position = center;
            runtime = runtimeObject.AddComponent<CircleCollider2D>();
            var context = new SpellExecutionContext(spell, cast);
            context.DispatchEvent(new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                center,
                Vector2.zero,
                runtime).WithGeometry(
                    SpellDeliveryGeometry.FollowCircle(
                        runtime.transform,
                        radius)));
        }

        private void PrepareOpposingTeams()
        {
            caster.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Player);
            target.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Enemy);
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
