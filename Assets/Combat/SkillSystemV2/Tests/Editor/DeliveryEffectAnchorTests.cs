using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class DeliveryEffectAnchorTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject caster;
        private GameObject target;
        private SpellVitality vitality;

        [SetUp]
        public void SetUp()
        {
            SpellDeliveryEffectAnchorService.ClearAllAnchors();
            caster = Track(new GameObject("Anchor Caster"));
            caster.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Player);
            target = Track(new GameObject("Anchor Target"));
            target.transform.position = Vector2.right;
            target.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Enemy);
            target.AddComponent<CircleCollider2D>().radius = 0.2f;
            vitality = target.AddComponent<SpellVitality>();
            Physics2D.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            SpellDeliveryEffectAnchorService.ClearAllAnchors();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                    Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void Geometry_CircleArcAndSegmentUseTheAuthoredShape()
        {
            Collider2D collider = target.GetComponent<Collider2D>();

            Assert.That(
                SpellDeliveryGeometry.Circle(Vector2.zero, 1.1f)
                    .Contains(collider),
                Is.True);
            Assert.That(
                SpellDeliveryGeometry.Arc(
                    Vector2.zero,
                    Vector2.right,
                    2f,
                    60f).Contains(collider),
                Is.True);

            target.transform.position = Vector2.left;
            Physics2D.SyncTransforms();
            Assert.That(
                SpellDeliveryGeometry.Arc(
                    Vector2.zero,
                    Vector2.right,
                    2f,
                    60f).Contains(collider),
                Is.False);

            target.transform.position = new Vector2(1f, 0.2f);
            Physics2D.SyncTransforms();
            Assert.That(
                SpellDeliveryGeometry.Segment(
                    Vector2.zero,
                    Vector2.right * 2f,
                    0.25f).Contains(collider),
                Is.True);
            target.transform.position = new Vector2(1f, 0.7f);
            Physics2D.SyncTransforms();
            Assert.That(
                SpellDeliveryGeometry.Segment(
                    Vector2.zero,
                    Vector2.right * 2f,
                    0.25f).Contains(collider),
                Is.False);
        }

        [Test]
        public void OnEnterAnchor_AppliesOnEntryAndCanReapplyAfterExit()
        {
            SpellDefinition spell = DamageAnchorSpell(
                SpellEffectAnchorApplication.OnEnter,
                reapply: true);
            SpellExecutionContext context = Execution(spell);
            SpellEventOccurrence occurrence = CircleEvent(2f);

            Assert.That(context.DispatchEvent(occurrence), Is.EqualTo(1));
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));

            SpellDeliveryEffectAnchor2D anchor = FindAnchor();
            anchor.Step(0f);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));

            target.transform.position = Vector2.right * 4f;
            Physics2D.SyncTransforms();
            anchor.Step(0f);
            target.transform.position = Vector2.right;
            Physics2D.SyncTransforms();
            anchor.Step(0f);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(80f).Within(0.001f));
        }

        [Test]
        public void PeriodicAnchor_ReappliesAtItsIndependentInterval()
        {
            SpellDefinition spell = DamageAnchorSpell(
                SpellEffectAnchorApplication.Periodic,
                interval: 0.25f);
            SpellExecutionContext context = Execution(spell);

            context.DispatchEvent(CircleEvent(2f));
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));

            FindAnchor().Step(0.25f);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(80f).Within(0.001f));
        }

        [Test]
        public void WhilePresentAnchor_RemovesOnlyItsOwnPersistentModifier()
        {
            SpellStatModifierEffectDefinition modifier =
                Create<SpellStatModifierEffectDefinition>();
            var slot = new SpellEffectSlot(
                modifier,
                new SpellStatModifierSettings(
                    SpellActorStat.MovementSpeed,
                    SpellStatOperation.Multiply,
                    0.5f,
                    10f));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                SpellEffectAnchorApplication.WhilePresent,
                5f);
            SpellDefinition spell = SpellWith(slot);

            Execution(spell).DispatchEvent(CircleEvent(2f));
            SpellStatModifierController controller =
                target.GetComponent<SpellStatModifierController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.Evaluate(SpellActorStat.MovementSpeed),
                Is.EqualTo(0.5f).Within(0.001f));

            target.transform.position = Vector2.right * 4f;
            Physics2D.SyncTransforms();
            FindAnchor().Step(0f);
            Assert.That(
                controller.Evaluate(SpellActorStat.MovementSpeed),
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void SpatialForceAnchor_OwnsARealCenterAndExactPresence()
        {
            target.AddComponent<Rigidbody2D>().gravityScale = 0f;
            SpatialForceEffectDefinition force =
                Create<SpatialForceEffectDefinition>();
            var slot = new SpellEffectSlot(
                force,
                new SpatialForceEffectSettings(
                    SpatialForceDirection.TowardCenter,
                    SpatialForceCenter.DeliveryCenter,
                    5f,
                    8f,
                    0.25f,
                    blockByObstacles: false));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                SpellEffectAnchorApplication.WhilePresent,
                5f);
            SpellDefinition spell = SpellWith(slot);

            Execution(spell).DispatchEvent(CircleEvent(2f));
            SpellActorMotionController2D controller =
                target.GetComponent<SpellActorMotionController2D>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsControllingMotion, Is.True);

            target.transform.position = Vector2.right * 4f;
            Physics2D.SyncTransforms();
            FindAnchor().Step(0f);
            Assert.That(controller.IsControllingMotion, Is.False);
        }

        [Test]
        public void SpatialForceAnchor_TeamAndLayerChangesDoNotEndPresence()
        {
            target.AddComponent<Rigidbody2D>().gravityScale = 0f;
            SpatialForceEffectDefinition force =
                Create<SpatialForceEffectDefinition>();
            var slot = new SpellEffectSlot(
                force,
                new SpatialForceEffectSettings(
                    SpatialForceDirection.TowardCenter,
                    SpatialForceCenter.DeliveryCenter,
                    5f,
                    8f,
                    0.25f,
                    blockByObstacles: false,
                    useCurve: true,
                    preserveCurveMomentum: true));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                SpellEffectAnchorApplication.WhilePresent,
                5f);
            SpellDefinition spell = SpellWith(slot);
            int admittedLayer = target.layer;
            SetField(
                spell,
                "targetFilter",
                new TargetFilter(
                    TargetRelationship.Enemies,
                    requireTarget: false,
                    filterByLayer: true,
                    layers: 1 << admittedLayer));

            Execution(spell).DispatchEvent(CircleEvent(2f));
            SpellDeliveryEffectAnchor2D anchor = FindAnchor();
            SpellActorMotionController2D controller =
                target.GetComponent<SpellActorMotionController2D>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsControllingMotion, Is.True);

            target.layer = admittedLayer == 8 ? 9 : 8;
            target.GetComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Player);
            Physics2D.SyncTransforms();
            anchor.Step(0f);

            Assert.That(
                spell.TargetFilter.IsValid(
                    CastContext.ForDirection(
                        caster,
                        caster.transform.position,
                        Vector2.right),
                    target),
                Is.False,
                "The test must prove both entry filters changed.");
            Assert.That(
                controller.IsControllingMotion,
                Is.True,
                "An admitted presence target should keep gravity until it " +
                "physically exits the anchor.");

            target.transform.position = Vector2.right * 4f;
            Physics2D.SyncTransforms();
            anchor.Step(0f);
            Assert.That(controller.IsControllingMotion, Is.False);
        }

        [Test]
        public void OnceAtAnchor_RunsWorldPointEffectsWithoutARecipient()
        {
            GameObject prefab = Track(new GameObject("Anchor Spawn Prefab"));
            SpawnEffectDefinition spawn = Create<SpawnEffectDefinition>();
            var slot = new SpellEffectSlot(
                spawn,
                new SpawnEffectSettings(
                    prefab,
                    SpellSpawnPosition.HitPoint,
                    Vector2.zero,
                    SpellSpawnRotation.Identity,
                    0f,
                    false,
                    0f,
                    SpellTimeMode.Scaled));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.Detonated,
                SpellEffectAnchorApplication.OnceAtAnchor,
                2f);
            SpellDefinition spell = SpellWith(slot);
            Vector2 point = new Vector2(3f, 4f);

            Execution(spell).DispatchEvent(
                new SpellEventOccurrence(
                    SpellEventType.Detonated,
                    null,
                    point,
                    Vector2.zero).WithGeometry(
                        SpellDeliveryGeometry.Circle(point, 2f)));

            GameObject clone = GameObject.Find("Anchor Spawn Prefab(Clone)");
            if (clone != null)
                created.Add(clone);
            Assert.That(clone, Is.Not.Null);
            Assert.That((Vector2)clone.transform.position,
                Is.EqualTo(point));
        }

        [Test]
        public void MovingAnchor_FollowsRuntimeThenKeepsItsLastSnapshot()
        {
            GameObject delivery = Track(new GameObject("Moving Delivery"));
            CircleCollider2D runtime = delivery.AddComponent<CircleCollider2D>();
            SpellDefinition spell = DamageAnchorSpell(
                SpellEffectAnchorApplication.OnEnter);
            SpellEventOccurrence occurrence = new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                Vector2.zero,
                Vector2.right,
                runtime).WithGeometry(
                    SpellDeliveryGeometry.FollowCircle(
                        delivery.transform,
                        1f));

            Execution(spell).DispatchEvent(occurrence);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));
            SpellDeliveryEffectAnchor2D anchor = FindAnchor();

            delivery.transform.position = Vector2.right * 2f;
            Physics2D.SyncTransforms();
            anchor.Step(0f);
            Assert.That(anchor.Geometry.BoundingCenter,
                Is.EqualTo(Vector2.right * 2f));

            Object.DestroyImmediate(delivery);
            anchor.Step(0f);
            Assert.That(anchor.IsComplete, Is.False);
            Assert.That(anchor.Geometry.BoundingCenter,
                Is.EqualTo(Vector2.right * 2f));
        }

        [Test]
        public void Multiplicity_DeduplicatesPerRuntimeButNotPerOccurrence()
        {
            GameObject delivery = Track(new GameObject("Delivery Runtime"));
            CircleCollider2D runtime = delivery.AddComponent<CircleCollider2D>();
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            var perRuntime = new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f));
            perRuntime.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                SpellEffectAnchorApplication.OnEnter,
                5f,
                multiplicity:
                    SpellEffectAnchorMultiplicity.PerDeliveryRuntime);
            SpellDefinition spell = SpellWith(perRuntime);
            SpellEventOccurrence occurrence = new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                Vector2.zero,
                Vector2.zero,
                runtime).WithGeometry(
                    SpellDeliveryGeometry.Circle(Vector2.zero, 2f));
            SpellExecutionContext context = Execution(spell);

            Assert.That(context.DispatchEvent(occurrence), Is.EqualTo(1));
            Assert.That(context.DispatchEvent(occurrence), Is.Zero);
            Assert.That(
                SpellDeliveryEffectAnchorService.ActiveAnchorCount,
                Is.EqualTo(1));
            FindAnchor().Step(5f);
            Assert.That(
                SpellDeliveryEffectAnchorService.ActiveAnchorCount,
                Is.Zero);
            Assert.That(context.DispatchEvent(occurrence), Is.Zero);

            SpellDeliveryEffectAnchorService.ClearAllAnchors();
            var perOccurrence = new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f));
            perOccurrence.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                SpellEffectAnchorApplication.OnEnter,
                5f,
                multiplicity:
                    SpellEffectAnchorMultiplicity.PerEventOccurrence);
            spell.ReplaceEffectSlots(perOccurrence);

            context.DispatchEvent(occurrence);
            context.DispatchEvent(occurrence);
            Assert.That(
                SpellDeliveryEffectAnchorService.ActiveAnchorCount,
                Is.EqualTo(2));
        }

        [Test]
        public void AnchoredSlot_IsDeferredFromNormalDeliveryRecipients()
        {
            SpellDefinition spell = DamageAnchorSpell(
                SpellEffectAnchorApplication.OnEnter);
            SpellEffectApplicationResult result = Execution(spell)
                .ApplyEffectsDetailed(
                    target,
                    target.transform.position,
                    Vector2.left);

            Assert.That(result.Status,
                Is.EqualTo(
                    SpellEffectApplicationStatus.DeferredToDeliveryAnchor));
            Assert.That(result.AppliedCount, Is.Zero);
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void EventRecipeAnchor_DoesNotRequireAnObjectRecipient()
        {
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            var slot = new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.TargetHit,
                SpellEffectAnchorApplication.OnEnter,
                5f);
            SpellDefinition spell = SpellWith(slot);
            // Keep the same effect only in the recipe so this test also proves
            // the recipe's WHEN event, rather than the slot's default trigger,
            // owns anchor creation.
            spell.ReplaceEffectSlots();
            spell.ReplaceEventEffectRoutes(new SpellEventEffectRoute(
                "Area-created damage anchor",
                SpellEventType.AreaCreated,
                SpellEventRecipient.EventSubject,
                new[] { slot },
                SpellEventSubjectRuleMode.NoRestrictions));

            int applied = Execution(spell).DispatchEvent(
                new SpellEventOccurrence(
                    SpellEventType.AreaCreated,
                    null,
                    Vector2.zero,
                    Vector2.zero).WithGeometry(
                        SpellDeliveryGeometry.Circle(Vector2.zero, 2f)));

            Assert.That(applied, Is.EqualTo(1));
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void Validation_DistinguishesSupportedAnchorsFromBadWorldPointUse()
        {
            GrenadeDeliveryDefinition grenade =
                Create<GrenadeDeliveryDefinition>();
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            var slot = new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.Detonated,
                SpellEffectAnchorApplication.OnEnter,
                2f);
            SpellDefinition spell = SpellWith(slot);
            spell.ReplaceDelivery(new SpellDeliverySlot(grenade));
            var issues = new List<SpellValidationIssue>();

            spell.CollectValidationIssues(issues);
            Assert.That(
                issues.Exists(issue =>
                    issue.Message.Contains("does not report") ||
                    issue.Message.Contains("creates an anchor on")),
                Is.False);

            slot.ConfigureDeliveryAnchor(
                SpellEventType.Detonated,
                SpellEffectAnchorApplication.OnceAtAnchor,
                2f);
            issues.Clear();
            spell.CollectValidationIssues(issues);
            Assert.That(
                issues.Exists(issue =>
                    issue.Message.Contains("requires an object recipient")),
                Is.True);
        }

        private SpellDefinition DamageAnchorSpell(
            SpellEffectAnchorApplication application,
            float interval = 0.25f,
            bool reapply = true)
        {
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            var slot = new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f));
            slot.ConfigureDeliveryAnchor(
                SpellEventType.DeliveryStarted,
                application,
                5f,
                interval,
                allowReapplyAfterExit: reapply);
            return SpellWith(slot);
        }

        private SpellDefinition SpellWith(SpellEffectSlot slot)
        {
            SpellDefinition spell = Create<SpellDefinition>();
            SetField(
                spell,
                "targetFilter",
                new TargetFilter(TargetRelationship.Any));
            spell.ReplaceEffectSlots(slot);
            return spell;
        }

        private SpellExecutionContext Execution(SpellDefinition spell)
        {
            CastContext cast = CastContext.ForDirection(
                caster,
                caster.transform.position,
                Vector2.right);
            return new SpellExecutionContext(spell, cast);
        }

        private static SpellEventOccurrence CircleEvent(float radius)
        {
            return new SpellEventOccurrence(
                SpellEventType.DeliveryStarted,
                null,
                Vector2.zero,
                Vector2.zero).WithGeometry(
                    SpellDeliveryGeometry.Circle(Vector2.zero, radius));
        }

        private static SpellDeliveryEffectAnchor2D FindAnchor()
        {
            SpellDeliveryEffectAnchor2D anchor =
                Object.FindObjectOfType<SpellDeliveryEffectAnchor2D>();
            Assert.That(anchor, Is.Not.Null);
            return anchor;
        }

        private T Create<T>() where T : ScriptableObject
        {
            return Track(ScriptableObject.CreateInstance<T>());
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
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(instance, value);
        }
    }
}
