using NUnit.Framework;
using UnityEditor;
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

        [Test]
        public void DeliverySlot_CopiesModuleDefaultsIntoSpellSettings()
        {
            var delivery = ScriptableObject.CreateInstance<
                ProjectileDeliveryDefinition>();
            var serialized = new SerializedObject(delivery);
            serialized.FindProperty("speed").floatValue = 13f;
            serialized.FindProperty("range").floatValue = 18f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var slot = new SpellDeliverySlot(delivery);

            Assert.That(slot.Delivery, Is.SameAs(delivery));
            Assert.That(slot.Settings, Is.TypeOf<ProjectileDeliverySettings>());
            var settings = (ProjectileDeliverySettings)slot.Settings;
            Assert.That(settings.Speed, Is.EqualTo(13f).Within(0.001f));
            Assert.That(settings.Range, Is.EqualTo(18f).Within(0.001f));
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void SharedDeliveryModule_AllowsIndependentSpellSettings()
        {
            var delivery = ScriptableObject.CreateInstance<
                ProjectileDeliveryDefinition>();
            var quick = new SpellDeliverySlot(
                delivery,
                new ProjectileDeliverySettings(
                    null, null, false, 14f, 10f, 0.1f,
                    ~0, false, 1, true, 16));
            var heavy = new SpellDeliverySlot(
                delivery,
                new ProjectileDeliverySettings(
                    null, null, false, 7f, 20f, 0.25f,
                    ~0, true, 3, true, 32));

            Assert.That(quick.Delivery, Is.SameAs(heavy.Delivery));
            Assert.That(
                ((ProjectileDeliverySettings)quick.Settings).Speed,
                Is.EqualTo(14f).Within(0.001f));
            Assert.That(
                ((ProjectileDeliverySettings)heavy.Settings).Speed,
                Is.EqualTo(7f).Within(0.001f));
            Assert.That(
                ((ProjectileDeliverySettings)heavy.Settings).PierceTargets,
                Is.True);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void LegacyDelivery_MigratesCurrentValuesOnlyOnce()
        {
            var delivery = ScriptableObject.CreateInstance<
                MeleeArcDeliveryDefinition>();
            var serializedDelivery = new SerializedObject(delivery);
            serializedDelivery.FindProperty("range").floatValue = 3.25f;
            serializedDelivery.ApplyModifiedPropertiesWithoutUndo();
            SpellDefinition spell = CreateSpell(delivery, "delivery-migration");
            typeof(SpellDefinition).GetField(
                "deliverySlotMigrated",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.SetValue(spell, false);

            Assert.That(spell.EnsureDeliverySlot(), Is.True);
            Assert.That(spell.Delivery, Is.SameAs(delivery));
            Assert.That(
                ((MeleeArcDeliverySettings)spell.DeliverySettings).Range,
                Is.EqualTo(3.25f).Within(0.001f));

            spell.ReplaceDelivery(new SpellDeliverySlot());
            Assert.That(spell.EnsureDeliverySlot(), Is.False);
            Assert.That(spell.Delivery, Is.Null);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void ProjectileDelivery_SanitizesLegacyPrefabGameplayComponents()
        {
            GameObject visualPrefab = new GameObject("Legacy Projectile Visual");
            Rigidbody2D body = visualPrefab.AddComponent<Rigidbody2D>();
            Collider2D collider = visualPrefab.AddComponent<CircleCollider2D>();
            visualPrefab.AddComponent<SpriteRenderer>();
            visualPrefab.AddComponent<TestLegacyProjectileBehaviour>();

            var delivery = ScriptableObject.CreateInstance<
                ProjectileDeliveryDefinition>();
            var serializedDelivery = new SerializedObject(delivery);
            serializedDelivery.FindProperty("projectilePrefab")
                .objectReferenceValue = visualPrefab;
            serializedDelivery.ApplyModifiedPropertiesWithoutUndo();

            SpellDefinition spell = CreateSpell(delivery, "projectile-sanitize");
            SpellRunner runner = caster.AddComponent<SpellRunner>();
            Assert.That(runner.TryCast(
                spell,
                CastContext.ForDirection(caster, Vector2.zero, Vector2.right),
                out _), Is.True);

            SpellProjectile2D runtime =
                Object.FindObjectOfType<SpellProjectile2D>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(
                runtime.GetComponent<TestLegacyProjectileBehaviour>().enabled,
                Is.False);
            Assert.That(runtime.GetComponent<Rigidbody2D>().simulated, Is.False);
            Assert.That(runtime.GetComponent<Collider2D>().enabled, Is.False);

            Object.DestroyImmediate(runtime.gameObject);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
            Object.DestroyImmediate(visualPrefab);
        }

        [Test]
        public void LingeringAreaDelivery_CreatesVisiblePersistentZone()
        {
            var delivery = ScriptableObject.CreateInstance<
                LingeringAreaDeliveryDefinition>();
            SpellDefinition spell = CreateSpell(delivery, "lingering-area");
            SpellRunner runner = caster.AddComponent<SpellRunner>();

            Assert.That(runner.TryCast(
                spell,
                CastContext.ForPoint(caster, Vector2.zero, Vector2.one),
                out _), Is.True);

            SpellLingeringArea2D runtime =
                Object.FindObjectOfType<SpellLingeringArea2D>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.GetComponent<SpriteRenderer>(), Is.Not.Null);

            Object.DestroyImmediate(runtime.gameObject);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(delivery);
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
            serialized.FindProperty("delivery").objectReferenceValue = delivery;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return spell;
        }
    }

    public sealed class TestLegacyProjectileBehaviour : MonoBehaviour
    {
    }
}
