using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class DeliveryRequirementTests
    {
        private GameObject caster;
        private GameObject targetAlias;

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(targetAlias);
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

        [Test]
        public void DirectTarget_SelfProxyAppliesStatModifierToLiveCaster()
        {
            targetAlias = new GameObject("Caster Party Target Proxy");
            TestRepresentedSpellTarget alias =
                targetAlias.AddComponent<TestRepresentedSpellTarget>();
            alias.Configure(caster);

            var delivery = ScriptableObject.CreateInstance<
                InstantTargetDeliveryDefinition>();
            var effect = ScriptableObject.CreateInstance<
                SpellStatModifierEffectDefinition>();
            SpellDefinition spell = CreateSpell(
                delivery,
                "direct-target-self-proxy");
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("targetFilter")
                .FindPropertyRelative("relationship")
                .enumValueIndex = (int)TargetRelationship.Self;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            spell.ReplaceDelivery(new SpellDeliverySlot(delivery));
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                effect,
                new SpellStatModifierSettings(
                    SpellActorStat.DamageReceived,
                    SpellStatOperation.Multiply,
                    0.5f,
                    5f)));

            SpellRunner runner = caster.AddComponent<SpellRunner>();
            Assert.That(
                runner.TryCast(
                    spell,
                    CastContext.ForTarget(
                        caster,
                        caster.transform.position,
                        targetAlias),
                    out SpellCastFailure failure),
                Is.True,
                failure.ToString());

            Assert.That(
                SpellStatModifierUtility.Evaluate(
                    caster,
                    SpellActorStat.DamageReceived),
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(
                targetAlias.GetComponent<SpellStatModifierController>(),
                Is.Null);

            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void DirectTarget_InactivePartyMemberModifierFollowsActiveState()
        {
            caster.AddComponent<CombatTeamMember>()
                .SetTeam(CombatTeam.Player);
            targetAlias = new GameObject("Inactive Party Target Proxy");
            targetAlias.transform.SetParent(caster.transform);
            TestRepresentedSpellTarget alias =
                targetAlias.AddComponent<TestRepresentedSpellTarget>();
            alias.Configure(caster);
            alias.SetActiveForRepresentedActor(false);
            targetAlias.AddComponent<CombatTeamMember>()
                .SetTeam(CombatTeam.Player);
            Assert.That(
                SpellTargetResolver.IsSameHierarchy(caster, targetAlias),
                Is.False);

            var delivery = ScriptableObject.CreateInstance<
                InstantTargetDeliveryDefinition>();
            var effect = ScriptableObject.CreateInstance<
                SpellStatModifierEffectDefinition>();
            SpellDefinition spell = CreateSpell(
                delivery,
                "direct-target-inactive-party-member");
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("targetFilter")
                .FindPropertyRelative("relationship")
                .enumValueIndex =
                    (int)TargetRelationship.AlliesAndSelf;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            spell.ReplaceDelivery(new SpellDeliverySlot(delivery));
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                effect,
                new SpellStatModifierSettings(
                    SpellActorStat.DamageReceived,
                    SpellStatOperation.Multiply,
                    0.5f,
                    5f)));

            SpellRunner runner = caster.AddComponent<SpellRunner>();
            Assert.That(
                runner.TryCast(
                    spell,
                    CastContext.ForTarget(
                        caster,
                        caster.transform.position,
                        targetAlias),
                    out SpellCastFailure failure),
                Is.True,
                failure.ToString());

            Assert.That(
                targetAlias.GetComponent<SpellStatModifierController>(),
                Is.Not.Null);
            Assert.That(
                SpellStatModifierUtility.Evaluate(
                    caster,
                    SpellActorStat.DamageReceived),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                SpellStatModifierUtility.Evaluate(
                    targetAlias,
                    SpellActorStat.DamageReceived),
                Is.EqualTo(1f).Within(0.001f));

            alias.SetActiveForRepresentedActor(true);
            Assert.That(
                SpellTargetResolver.IsSameHierarchy(caster, targetAlias),
                Is.True);
            Assert.That(
                SpellStatModifierUtility.Evaluate(
                    caster,
                    SpellActorStat.DamageReceived),
                Is.EqualTo(0.5f).Within(0.001f));

            alias.SetActiveForRepresentedActor(false);
            Assert.That(
                SpellStatModifierUtility.Evaluate(
                    caster,
                    SpellActorStat.DamageReceived),
                Is.EqualTo(1f).Within(0.001f));

            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(delivery);
        }

        [Test]
        public void TakeLessDamageAsset_TargetsPartyAndReducesIncomingDamage()
        {
            const string path =
                "Assets/Combat/SkillSystemV2/Content/Spells/" +
                "Spell_TakeLessDamage.asset";
            SpellDefinition spell =
                AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);

            Assert.That(spell, Is.Not.Null);
            Assert.That(
                spell.Delivery,
                Is.TypeOf<InstantTargetDeliveryDefinition>());
            Assert.That(
                spell.TargetFilter.Relationship,
                Is.EqualTo(TargetRelationship.AlliesAndSelf));
            Assert.That(spell.EffectSlots.Count, Is.EqualTo(1));
            Assert.That(
                spell.EffectSlots[0].Settings,
                Is.TypeOf<SpellStatModifierSettings>());

            var settings =
                (SpellStatModifierSettings)spell.EffectSlots[0].Settings;
            Assert.That(
                settings.Stat,
                Is.EqualTo(SpellActorStat.DamageReceived));
            Assert.That(
                settings.Operation,
                Is.EqualTo(SpellStatOperation.Multiply));
            Assert.That(settings.Value, Is.EqualTo(0.75f).Within(0.001f));
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

    public sealed class TestRepresentedSpellTarget : MonoBehaviour,
        ISpellTarget,
        ISpellTargetIdentity,
        ISpellStatModifierActivationGate
    {
        private GameObject representedObject;
        private bool activeForRepresentedActor = true;

        public GameObject TargetObject => gameObject;
        public bool IsTargetable => true;
        public bool AreSpellStatModifiersActive => activeForRepresentedActor;

        public void Configure(GameObject represented)
        {
            representedObject = represented;
        }

        public void SetActiveForRepresentedActor(bool active)
        {
            activeForRepresentedActor = active;
        }

        public bool Represents(GameObject other)
        {
            return activeForRepresentedActor &&
                   representedObject != null && other != null &&
                   (other == representedObject ||
                    other.transform.IsChildOf(
                        representedObject.transform) ||
                    representedObject.transform.IsChildOf(other.transform));
        }
    }
}
