using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class AdvancedDeliveryTests
    {
        private GameObject caster;

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
            caster.AddComponent<CombatTeamMember>()
                .SetTeam(CombatTeam.Player);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyAll<SpellTripWire2D>();
            DestroyAll<SpellProximityMine2D>();
            DestroyAll<SpellGrenade2D>();
            DestroyAll<SpellRicochetProjectile2D>();
            Object.DestroyImmediate(caster);
        }

        [Test]
        public void TargetingPayload_CopiesAndReturnsMultiplePoints()
        {
            Vector2[] source = { Vector2.left, Vector2.right };
            var payload = new SpellTargetingPayload(source);
            source[0] = Vector2.up;

            Assert.That(payload.PointCount, Is.EqualTo(2));
            Assert.That(payload.TryGetPoint(0, out Vector2 first), Is.True);
            Assert.That(first, Is.EqualTo(Vector2.left));
            Assert.That(payload.TryGetPoint(2, out _), Is.False);
        }

        [Test]
        public void TwoPointTargeting_BuildsFinalSegmentPayload()
        {
            var targeting = ScriptableObject.CreateInstance<
                TwoPointTargetingDefinition>();
            var confirmed = new SpellTargetingPayload(new Vector2(1f, 0f));
            var request = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                new Vector2(4f, 0f),
                null,
                confirmed);

            bool valid = targeting.TryBuildContext(
                request,
                out CastContext context,
                out PlayerTargetingPreview preview,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.TargetingPayload.PointCount, Is.EqualTo(2));
            Assert.That(preview.Origin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(preview.AimPoint, Is.EqualTo(new Vector2(4f, 0f)));
            Object.DestroyImmediate(targeting);
        }

        [Test]
        public void TripWireDelivery_RequiresTwoConfirmedPoints()
        {
            var delivery = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            CastContext pointOnly = CastContext.ForPoint(
                caster,
                Vector2.zero,
                Vector2.right);
            CastContext complete = pointOnly.WithTargetingPayload(
                new SpellTargetingPayload(Vector2.left, Vector2.right));

            Assert.That(delivery.ValidateContext(pointOnly, out _), Is.False);
            Assert.That(delivery.ValidateContext(complete, out _), Is.True);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void MenuSelectTargeting_UsesExplicitSelectedObject()
        {
            var targeting = ScriptableObject.CreateInstance<
                MenuSelectTargetingDefinition>();
            var target = new GameObject("Menu Target");
            target.transform.position = new Vector2(3f, 2f);
            var request = new PlayerTargetingRequest(
                null,
                caster,
                Vector2.zero,
                target.transform.position,
                target);

            bool valid = targeting.TryBuildContext(
                request,
                out CastContext context,
                out _,
                out _);

            Assert.That(valid, Is.True);
            Assert.That(context.SelectedTarget, Is.SameAs(target));
            Assert.That(context.TargetPoint, Is.EqualTo((Vector2)target.transform.position));
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(targeting);
        }

        [Test]
        public void NewDeliveries_CreateIndependentInlineSettings()
        {
            var trip = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            var mine = ScriptableObject.CreateInstance<
                ProximityMineDeliveryDefinition>();
            var grenade = ScriptableObject.CreateInstance<
                GrenadeDeliveryDefinition>();
            var ricochet = ScriptableObject.CreateInstance<
                RicochetProjectileDeliveryDefinition>();

            Assert.That(new SpellDeliverySlot(trip).Settings,
                Is.TypeOf<TripWireDeliverySettings>());
            Assert.That(new SpellDeliverySlot(mine).Settings,
                Is.TypeOf<ProximityMineDeliverySettings>());
            Assert.That(new SpellDeliverySlot(grenade).Settings,
                Is.TypeOf<GrenadeDeliverySettings>());
            Assert.That(new SpellDeliverySlot(ricochet).Settings,
                Is.TypeOf<RicochetProjectileDeliverySettings>());

            Object.DestroyImmediate(trip);
            Object.DestroyImmediate(mine);
            Object.DestroyImmediate(grenade);
            Object.DestroyImmediate(ricochet);
        }

        [Test]
        public void TripWireExecution_CreatesPersistentRuntime()
        {
            var delivery = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "test-trip-wire");
            CastContext cast = CastContext.ForPoint(
                    caster, Vector2.zero, Vector2.right)
                .WithTargetingPayload(new SpellTargetingPayload(
                    Vector2.left, Vector2.right));
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(spell, cast));

            execution.Begin();

            SpellTripWire2D runtime = Object.FindObjectOfType<SpellTripWire2D>();
            Assert.That(runtime, Is.Not.Null);
            LineRenderer visual = runtime.GetComponent<LineRenderer>();
            Assert.That(visual, Is.Not.Null);
            Assert.That(
                visual.sortingLayerID,
                Is.EqualTo(SortingLayer.NameToID("World")));
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void ProximityMineExecution_CreatesPersistentRuntime()
        {
            var delivery = ScriptableObject.CreateInstance<
                ProximityMineDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "test-mine");
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(
                    spell,
                    CastContext.ForPoint(caster, Vector2.zero, Vector2.one)));

            execution.Begin();

            SpellProximityMine2D runtime =
                Object.FindObjectOfType<SpellProximityMine2D>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.GetComponent<LineRenderer>(), Is.Null);
            SpriteRenderer marker = runtime.GetComponent<SpriteRenderer>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(
                marker.sortingLayerID,
                Is.EqualTo(SortingLayer.NameToID("World")));
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void ProximityMine_AnyFilterLetsCasterTriggerAndTakeDamage()
        {
            caster.AddComponent<CombatTarget>();
            caster.AddComponent<CircleCollider2D>().radius = 0.25f;
            SpellVitality vitality = caster.AddComponent<SpellVitality>();
            var delivery = ScriptableObject.CreateInstance<
                ProximityMineDeliveryDefinition>();
            var damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            SpellDefinition spell = CreateAnyDamageSpell(
                delivery,
                new ProximityMineDeliverySettings(
                    null,
                    0f,
                    1f,
                    0f,
                    1.5f,
                    5f,
                    ~0,
                    16),
                damage,
                "test-self-mine");
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                Vector2.zero);
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(spell, cast),
                spell.DeliverySettings);

            execution.Begin();
            Physics2D.SyncTransforms();
            SpellProximityMine2D runtime =
                Object.FindObjectOfType<SpellProximityMine2D>();
            runtime.Step(0f);

            Assert.That(vitality.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(runtime.IsComplete, Is.True);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
            Object.DestroyImmediate(damage);
        }

        [Test]
        public void TripWire_AnyFilterLetsCasterCrossAndTakeDamage()
        {
            caster.AddComponent<CombatTarget>();
            caster.AddComponent<CircleCollider2D>().radius = 0.25f;
            SpellVitality vitality = caster.AddComponent<SpellVitality>();
            var delivery = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            var damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            var settings = new TripWireDeliverySettings(
                null,
                5f,
                0.2f,
                ~0,
                16);
            SpellDefinition spell = CreateAnyDamageSpell(
                delivery,
                settings,
                damage,
                "test-self-trip-wire");
            CastContext cast = CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    Vector2.right)
                .WithTargetingPayload(new SpellTargetingPayload(
                    Vector2.left,
                    Vector2.right));
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(spell, cast),
                spell.DeliverySettings);

            execution.Begin();
            Physics2D.SyncTransforms();
            SpellTripWire2D runtime =
                Object.FindObjectOfType<SpellTripWire2D>();
            runtime.Step(0f);

            Assert.That(vitality.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(runtime.IsComplete, Is.True);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
            Object.DestroyImmediate(damage);
        }

        [Test]
        public void GrenadeExecution_CreatesTimedRuntime()
        {
            var delivery = ScriptableObject.CreateInstance<
                GrenadeDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "test-grenade");
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(
                    spell,
                    CastContext.ForPoint(caster, Vector2.zero, Vector2.right)));

            execution.Begin();

            Assert.That(Object.FindObjectOfType<SpellGrenade2D>(), Is.Not.Null);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void GrenadeExplosionTargetMask_IsIndependentOfCollisionMask()
        {
            LayerMask obstacleOnly = 1 << 7;
            var settings = new GrenadeDeliverySettings(
                null,
                null,
                8f,
                1.5f,
                0.1f,
                2f,
                obstacleOnly,
                GrenadeCollisionMode.Regular,
                0.8f,
                4,
                32);

            Assert.That(
                settings.CollisionMask.value,
                Is.EqualTo(obstacleOnly.value));
            Assert.That(settings.ExplosionTargetMask.value, Is.EqualTo(~0));
        }

        [Test]
        public void RicochetProjectile_DeflectionCanBeEnabledOrDisabled()
        {
            var delivery = ScriptableObject.CreateInstance<
                RicochetProjectileDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "test-ricochet");
            var enabledSettings = new RicochetProjectileDeliverySettings(
                null, null, 10f, 10f, 0.1f, ~0, 2, 1f,
                false, 2, true, 1f, 16);
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(
                    spell,
                    CastContext.ForDirection(
                        caster, Vector2.zero, Vector2.right)),
                enabledSettings);
            execution.Begin();
            SpellRicochetProjectile2D runtime =
                Object.FindObjectOfType<SpellRicochetProjectile2D>();
            var secondCaster = new GameObject("Deflector");

            Assert.That(runtime.TryDeflect(secondCaster, Vector2.up), Is.True);

            Object.DestroyImmediate(secondCaster);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void RicochetProjectile_BouncesFromUnmarkedWall_WhenFilterIsAny()
        {
            var delivery = ScriptableObject.CreateInstance<
                RicochetProjectileDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "test-ricochet-wall");
            var wall = new GameObject("Unmarked Wall");
            wall.transform.position = Vector2.right;
            BoxCollider2D wallCollider = wall.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.1f, 2f);

            var settings = new RicochetProjectileDeliverySettings(
                null, null, 10f, 10f, 0.05f, ~0, 3, 1f,
                false, 4, true, 1f, 16);
            ISpellDeliveryExecution execution = delivery.CreateExecution(
                new SpellExecutionContext(
                    spell,
                    CastContext.ForDirection(
                        caster, Vector2.zero, Vector2.right)),
                settings);
            execution.Begin();
            SpellRicochetProjectile2D runtime =
                Object.FindObjectOfType<SpellRicochetProjectile2D>();

            runtime.Step(0.2f);

            Assert.That(runtime.IsComplete, Is.False);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void EventSupport_ReportsAdvancedDeliveryMoments()
        {
            var trip = ScriptableObject.CreateInstance<
                TripWireDeliveryDefinition>();
            var mine = ScriptableObject.CreateInstance<
                ProximityMineDeliveryDefinition>();
            var grenade = ScriptableObject.CreateInstance<
                GrenadeDeliveryDefinition>();
            var ricochet = ScriptableObject.CreateInstance<
                RicochetProjectileDeliveryDefinition>();

            Assert.That(SpellEventSupport.DeliveryReports(
                trip, SpellEventType.TargetCrossed), Is.True);
            Assert.That(SpellEventSupport.DeliveryReports(
                mine, SpellEventType.ProximityTriggered), Is.True);
            Assert.That(SpellEventSupport.DeliveryReports(
                grenade, SpellEventType.Detonated), Is.True);
            Assert.That(SpellEventSupport.DeliveryReports(
                ricochet, SpellEventType.Deflected), Is.True);

            Object.DestroyImmediate(trip);
            Object.DestroyImmediate(mine);
            Object.DestroyImmediate(grenade);
            Object.DestroyImmediate(ricochet);
        }

        private static SpellDefinition CreateSpell(
            DeliveryDefinition delivery,
            string stableId)
        {
            SpellDefinition spell = ScriptableObject.CreateInstance<
                SpellDefinition>();
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("displayName").stringValue = "Test Spell";
            serialized.FindProperty("stableId").stringValue = stableId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            spell.ReplaceDelivery(new SpellDeliverySlot(delivery));
            return spell;
        }

        private static SpellDefinition CreateAnyDamageSpell(
            DeliveryDefinition delivery,
            SpellDeliverySettings settings,
            DamageEffectDefinition damage,
            string stableId)
        {
            SpellDefinition spell = CreateSpell(delivery, stableId);
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("targetFilter.relationship")
                .enumValueIndex = (int)TargetRelationship.Any;
            serialized.FindProperty("targetFilter.requireSpellTarget")
                .boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            spell.ReplaceDelivery(new SpellDeliverySlot(delivery, settings));
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f)));
            return spell;
        }

        private static void DestroyAll<T>() where T : Component
        {
            T[] instances = Object.FindObjectsOfType<T>();
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i] != null)
                    Object.DestroyImmediate(instances[i].gameObject);
            }
        }
    }
}
