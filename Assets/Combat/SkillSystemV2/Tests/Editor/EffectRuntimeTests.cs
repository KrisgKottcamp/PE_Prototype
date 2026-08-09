using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class EffectRuntimeTests
    {
        private GameObject caster;
        private GameObject target;
        private readonly List<UnityEngine.Object> assets =
            new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            caster = new GameObject("Caster");
            target = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(caster);
            Object.DestroyImmediate(target);

            for (int i = 0; i < assets.Count; i++)
                Object.DestroyImmediate(assets[i]);

            assets.Clear();
        }

        [Test]
        public void DamageAndHealingEffects_UseReceiverContractsAndPotency()
        {
            SpellVitality vitality = target.AddComponent<SpellVitality>();
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            HealingEffectDefinition healing = Create<HealingEffectDefinition>();
            SpellEffectContext strongHit = EffectContext(potency: 2f);

            Assert.That(damage.Apply(strongHit), Is.True);
            Assert.That(vitality.CurrentHealth, Is.EqualTo(80f).Within(0.001f));

            Assert.That(healing.Apply(EffectContext()), Is.True);
            Assert.That(vitality.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void ResourcePool_ReceivesAPThenPaysSpellCost()
        {
            SpellResourcePool pool = target.AddComponent<SpellResourcePool>();
            GameplayResourceDefinition resource =
                Create<GameplayResourceDefinition>();
            SpellEffectContext context = EffectContext();
            var change = new SpellResourceChangeRequest(
                context,
                resource,
                SpellResourceOperation.Add,
                25f,
                allowOverflow: false);

            Assert.That(pool.TryChangeResource(
                change,
                out SpellResourceChangeResult result), Is.True);
            Assert.That(result.AppliedDelta, Is.EqualTo(25f).Within(0.001f));
            Assert.That(pool.TrySpend(
                new SpellResourceCost(
                    SpellResourceCost.ActionPoints,
                    10f)), Is.True);
            Assert.That(pool.GetCurrent(SpellResourceCost.ActionPoints),
                Is.EqualTo(15f).Within(0.001f));
        }

        [Test]
        public void StatusController_AppliesAndExpiresTimedStatus()
        {
            StatusController controller = target.AddComponent<StatusController>();
            StatusDefinition status = Create<StatusDefinition>();
            var request = new SpellStatusApplyRequest(
                EffectContext(),
                status,
                duration: 1f,
                stacks: 1);

            Assert.That(controller.TryApplyStatus(request, out _), Is.True);
            Assert.That(controller.HasStatus(status), Is.True);

            controller.TickStatuses(1.1f, 1.1f);

            Assert.That(controller.HasStatus(status), Is.False);
        }

        [Test]
        public void StatusController_RemovesRequestedStatus()
        {
            StatusController controller = target.AddComponent<StatusController>();
            StatusDefinition status = Create<StatusDefinition>();
            controller.TryApplyStatus(
                new SpellStatusApplyRequest(
                    EffectContext(),
                    status,
                    duration: 5f,
                    stacks: 1),
                out _);

            Assert.That(controller.TryRemoveStatus(
                status,
                stacksToRemove: 0,
                out SpellStatusResult result), Is.True);
            Assert.That(result.CurrentStacks, Is.Zero);
            Assert.That(controller.ActiveStatusCount, Is.Zero);
        }

        [Test]
        public void GameplaySignalEffect_RaisesTypedContextEvent()
        {
            GameplaySignalDefinition signal = Create<GameplaySignalDefinition>();
            GameplaySignalEffectDefinition effect =
                Create<GameplaySignalEffectDefinition>();
            SetField(effect, "signal", signal);
            int raisedCount = 0;
            GameObject receivedTarget = null;
            signal.Raised += raised =>
            {
                raisedCount++;
                receivedTarget = raised.EffectContext.Target;
            };

            Assert.That(effect.Apply(EffectContext()), Is.True);
            Assert.That(raisedCount, Is.EqualTo(1));
            Assert.That(receivedTarget, Is.SameAs(target));
        }

        [Test]
        public void SpellRunner_QueuesTriggeredCastWhileBusy()
        {
            SpellRunner runner = caster.AddComponent<SpellRunner>();
            TestDeliveryDefinition delivery = Create<TestDeliveryDefinition>();
            SpellDefinition primary = CreateSpell(
                delivery,
                recoveryDuration: 1f,
                id: "primary");
            SpellDefinition secondary = CreateSpell(
                delivery,
                recoveryDuration: 0f,
                id: "secondary");
            int secondaryStarts = 0;
            runner.CastStarted += castEvent =>
            {
                if (castEvent.Spell == secondary)
                    secondaryStarts++;
            };

            CastContext cast = new CastContext(
                caster,
                CombatTeam.Player,
                Vector2.zero,
                Vector2.zero,
                false,
                Vector2.zero,
                false,
                null);
            Assert.That(runner.TryCast(primary, cast, out _), Is.True);
            Assert.That(runner.IsCasting, Is.True);
            Assert.That(runner.QueueTriggeredCast(
                secondary,
                cast.CreateChild(
                    Vector2.zero,
                    Vector2.right,
                    Vector2.right,
                    true,
                    null),
                out _), Is.True);
            Assert.That(runner.QueuedTriggeredCastCount, Is.EqualTo(1));

            runner.TickRuntime(1.1f, 1.1f);

            Assert.That(runner.QueuedTriggeredCastCount, Is.Zero);
            Assert.That(secondaryStarts, Is.EqualTo(1));
        }

        private SpellEffectContext EffectContext(float potency = 1f)
        {
            var cast = new CastContext(
                caster,
                CombatTeam.Player,
                caster.transform.position,
                Vector2.right,
                true,
                target.transform.position,
                true,
                target);
            return new SpellEffectContext(
                null,
                cast,
                target,
                target.transform.position,
                Vector2.left,
                potency);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private SpellDefinition CreateSpell(
            DeliveryDefinition delivery,
            float recoveryDuration,
            string id)
        {
            SpellDefinition spell = Create<SpellDefinition>();
            SetField(spell, "stableId", id);
            SetField(spell, "delivery", delivery);

            object boxedTiming = default(SpellTiming);
            typeof(SpellTiming)
                .GetField(
                    "recoveryDuration",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(boxedTiming, recoveryDuration);
            SetField(spell, "timing", (SpellTiming)boxedTiming);
            return spell;
        }

        private static void SetField(object targetObject, string field, object value)
        {
            FieldInfo info = targetObject.GetType().GetField(
                field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"Missing field {field}.");
            info.SetValue(targetObject, value);
        }

    }
}
