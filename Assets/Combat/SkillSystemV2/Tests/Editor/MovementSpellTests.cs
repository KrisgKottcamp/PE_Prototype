using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class MovementSpellTests
    {
        private GameObject caster;
        private readonly List<Object> createdAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Movement Caster");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(caster);
            for (int i = 0; i < createdAssets.Count; i++)
            {
                if (createdAssets[i] != null)
                    Object.DestroyImmediate(createdAssets[i]);
            }
            createdAssets.Clear();
        }

        [Test]
        public void PointClickDelivery_RequiresTargetPoint()
        {
            PointClickDeliveryDefinition delivery =
                Create<PointClickDeliveryDefinition>();

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
                    Vector2.right),
                out _), Is.True);
        }

        [Test]
        public void MovementContext_ClampsDestinationToPerSpellMaximum()
        {
            SpellDefinition spell = CreateMovementSpell(
                new CasterMovementEffectSettings(
                    10f,
                    3f,
                    false,
                    false,
                    0));

            Assert.That(spell.TryResolveContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(10f, 0f)),
                out CastContext resolved,
                out _), Is.True);
            Assert.That(resolved.TargetPoint.x, Is.EqualTo(3f).Within(0.001f));
            Assert.That(resolved.TargetPoint.y, Is.Zero.Within(0.001f));
        }

        [Test]
        public void InstantMovement_CastsThroughPointDeliveryAndMovesCaster()
        {
            SpellDefinition spell = CreateMovementSpell(
                new CasterMovementEffectSettings(
                    10f,
                    3f,
                    true,
                    false,
                    0));
            SpellRunner runner = caster.AddComponent<SpellRunner>();

            Assert.That(runner.TryCast(
                spell,
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(10f, 0f)),
                out SpellCastFailure failure), Is.True, failure.ToString());
            Assert.That(caster.transform.position.x,
                Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void NonInstantMovement_TravelsAtConfiguredSpeed()
        {
            SpellDefinition spell = CreateMovementSpell(
                new CasterMovementEffectSettings(
                    4f,
                    10f,
                    false,
                    false,
                    0));
            CasterMovementEffectDefinition effect =
                (CasterMovementEffectDefinition)spell.EffectSlots[0].Effect;
            CastContext cast = CastContext.ForPoint(
                caster,
                Vector2.zero,
                new Vector2(4f, 0f));
            var effectContext = new SpellEffectContext(
                spell,
                cast,
                caster,
                cast.TargetPoint,
                Vector2.right,
                1f);

            Assert.That(effect.Apply(
                effectContext,
                spell.EffectSlots[0].Settings), Is.True);
            SpellCasterMovementRuntime2D runtime =
                caster.GetComponent<SpellCasterMovementRuntime2D>();
            Assert.That(runtime, Is.Not.Null);

            runtime.TickMovement(0.25f);
            Assert.That(caster.transform.position.x,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(runtime.IsMoving, Is.True);

            runtime.TickMovement(0.75f);
            Assert.That(caster.transform.position.x,
                Is.EqualTo(4f).Within(0.001f));
            Assert.That(runtime.IsMoving, Is.False);
        }

        [Test]
        public void LineOfSight_RejectsBlockedMovementBeforeCast()
        {
            caster.AddComponent<CircleCollider2D>().radius = 0.25f;
            var obstacle = new GameObject("Movement Obstacle");
            obstacle.transform.position = new Vector2(1.5f, 0f);
            obstacle.AddComponent<BoxCollider2D>().size =
                new Vector2(0.5f, 2f);
            Physics2D.SyncTransforms();

            SpellDefinition spell = CreateMovementSpell(
                new CasterMovementEffectSettings(
                    10f,
                    4f,
                    true,
                    true,
                    1 << obstacle.layer));

            Assert.That(spell.TryResolveContext(
                CastContext.ForPoint(
                    caster,
                    Vector2.zero,
                    new Vector2(3f, 0f)),
                out _,
                out string reason), Is.False);
            Assert.That(reason, Does.Contain("blocked"));

            Object.DestroyImmediate(obstacle);
        }

        private SpellDefinition CreateMovementSpell(
            CasterMovementEffectSettings settings)
        {
            PointClickDeliveryDefinition delivery =
                Create<PointClickDeliveryDefinition>();
            CasterMovementEffectDefinition movement =
                Create<CasterMovementEffectDefinition>();
            SpellDefinition spell = Create<SpellDefinition>();

            var serialized = new SerializedObject(spell);
            serialized.FindProperty("displayName").stringValue =
                "Movement Test Spell";
            serialized.FindProperty("stableId").stringValue =
                $"movement-test-{createdAssets.Count}";
            serialized.FindProperty("targetFilter.relationship")
                .enumValueIndex = (int)TargetRelationship.Self;
            serialized.FindProperty("targetFilter.requireSpellTarget")
                .boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            spell.ReplaceDelivery(new SpellDeliverySlot(
                delivery,
                new PointClickDeliverySettings(null)));
            spell.ReplaceEffectSlots(new SpellEffectSlot(
                movement,
                settings));
            return spell;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            createdAssets.Add(instance);
            return instance;
        }
    }
}
