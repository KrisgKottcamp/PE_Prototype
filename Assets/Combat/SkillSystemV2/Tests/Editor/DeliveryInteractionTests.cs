using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class DeliveryInteractionTests
    {
        private static readonly List<int> responseLog = new List<int>();
        private GameObject sourceCaster;
        private GameObject receiverCaster;

        [SetUp]
        public void SetUp()
        {
            sourceCaster = new GameObject("Source Caster");
            receiverCaster = new GameObject("Receiver Caster");
            responseLog.Clear();
            SpellDeliveryInteractionService.ClearAllRegistrations();
        }

        [TearDown]
        public void TearDown()
        {
            SpellDeliveryInteractionService.ClearAllRegistrations();
            Object.DestroyImmediate(sourceCaster);
            Object.DestroyImmediate(receiverCaster);
        }

        [Test]
        public void EmptyFilter_MatchesEveryDeliveryInteraction()
        {
            var filter = new DeliveryInteractionFilter();
            DeliveryInteractionContext context = InteractionContext(
                CombatTeam.Enemy,
                CombatTeam.Player,
                DeliveryContactPhase.Impact);

            Assert.That(filter.Matches(context), Is.True);
            Object.DestroyImmediate(context.SourceSpell);
            Object.DestroyImmediate(context.ReceiverSpell);
        }

        [Test]
        public void Filter_AllConditionsSupportsRelationshipAndPhase()
        {
            var filter = new DeliveryInteractionFilter();
            filter.ReplaceConditions(
                InteractionFilterMatchMode.All,
                new InteractionRelationshipCondition(
                    DeliverySourceRelationship.Enemies),
                new InteractionContactPhaseCondition(
                    DeliveryContactPhase.Impact |
                    DeliveryContactPhase.Enter));

            SpellDefinition sourceSpell = CreateSpell("filter-source");
            SpellDefinition receiverSpell = CreateSpell("filter-receiver");
            Assert.That(filter.Matches(InteractionContext(
                sourceSpell, receiverSpell, CombatTeam.Enemy,
                CombatTeam.Player, DeliveryContactPhase.Impact)), Is.True);
            Assert.That(filter.Matches(InteractionContext(
                sourceSpell, receiverSpell, CombatTeam.Player,
                CombatTeam.Player, DeliveryContactPhase.Impact)), Is.False);
            Assert.That(filter.Matches(InteractionContext(
                sourceSpell, receiverSpell, CombatTeam.Enemy,
                CombatTeam.Player, DeliveryContactPhase.Stay)), Is.False);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(receiverSpell);
        }

        [Test]
        public void DamageTypeCondition_ReadsInlineDamageSettings()
        {
            DamageTypeDefinition physical = ScriptableObject.CreateInstance<
                DamageTypeDefinition>();
            DamageTypeDefinition electric = ScriptableObject.CreateInstance<
                DamageTypeDefinition>();
            DamageEffectDefinition damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            SpellDefinition sourceSpell = CreateSpell("source-damage-type");
            sourceSpell.ReplaceEffectSlots(new SpellEffectSlot(
                damage,
                new DamageEffectSettings(10f, physical)));
            var filter = new DeliveryInteractionFilter();
            filter.ReplaceConditions(
                InteractionFilterMatchMode.All,
                new InteractionDamageTypeCondition(physical));

            DeliveryInteractionContext context = InteractionContext(
                sourceSpell,
                CreateSpell("receiver-damage-type"),
                CombatTeam.Enemy,
                CombatTeam.Player,
                DeliveryContactPhase.Impact);
            Assert.That(filter.Matches(context), Is.True);

            filter.ReplaceConditions(
                InteractionFilterMatchMode.All,
                new InteractionDamageTypeCondition(electric));
            Assert.That(filter.Matches(context), Is.False);

            Object.DestroyImmediate(context.ReceiverSpell);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(damage);
            Object.DestroyImmediate(physical);
            Object.DestroyImmediate(electric);
        }

        [Test]
        public void InteractionFilters_SeeEffectsCarriedByEventRecipes()
        {
            DamageTypeDefinition physical = ScriptableObject.CreateInstance<
                DamageTypeDefinition>();
            DamageEffectDefinition damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            SpellDefinition sourceSpell = CreateSpell(
                "source-event-carried-effect");
            sourceSpell.ReplaceEventEffectRoutes(
                new SpellEventEffectRoute(
                    "Impact Damage",
                    SpellEventType.TargetHit,
                    SpellEventRecipient.EventSubject,
                    new[]
                    {
                        new SpellEffectSlot(
                            damage,
                            new DamageEffectSettings(10f, physical))
                    }));
            SpellDefinition receiverSpell = CreateSpell(
                "receiver-event-carried-effect");
            DeliveryInteractionContext context = InteractionContext(
                sourceSpell,
                receiverSpell,
                CombatTeam.Enemy,
                CombatTeam.Player,
                DeliveryContactPhase.Impact);

            var effectFilter = new DeliveryInteractionFilter();
            effectFilter.ReplaceConditions(
                InteractionFilterMatchMode.All,
                new InteractionEffectCondition(damage),
                new InteractionDamageTypeCondition(physical));

            Assert.That(effectFilter.Matches(context), Is.True);

            Object.DestroyImmediate(receiverSpell);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(damage);
            Object.DestroyImmediate(physical);
        }

        [Test]
        public void InteractionService_NormalizesPointCircleSegmentAndArc()
        {
            var volume = new TestInteractionVolume(
                CreateSpell("receiver-volume"),
                receiverCaster,
                Vector2.zero,
                1f);
            SpellDeliveryInteractionService.Register(volume);
            SpellExecutionContext source = Execution(
                CreateSpell("source-volume"),
                sourceCaster,
                CombatTeam.Enemy);

            Assert.That(SpellDeliveryInteractionService.EmitPoint(
                source, Vector2.zero), Is.EqualTo(1));
            Assert.That(SpellDeliveryInteractionService.EmitCircle(
                source, new Vector2(1.5f, 0f), 0.6f), Is.EqualTo(1));
            Assert.That(SpellDeliveryInteractionService.EmitSegment(
                source, new Vector2(-2f, 0f), new Vector2(2f, 0f), 0f),
                Is.EqualTo(1));
            Assert.That(SpellDeliveryInteractionService.EmitArc(
                source, new Vector2(-2f, 0f), Vector2.right, 3f, 45f),
                Is.EqualTo(1));
            Assert.That(volume.ReceiveCount, Is.EqualTo(4));

            Object.DestroyImmediate(source.Spell);
            Object.DestroyImmediate(volume.InteractionExecutionContext.Spell);
        }

        [Test]
        public void DormantLingeringArea_ActivatesFromAnyDeliveryContact()
        {
            SpellDefinition receiverSpell = CreateSpell("receiver-oil");
            receiverSpell.ReplaceReactionSlots(new SpellReactionSlot(
                new DeliveryInteractionFilter(),
                new ActivateDeliveryResponse(true, false)));
            var zoneObject = new GameObject("Dormant Oil");
            SpellLingeringArea2D zone =
                zoneObject.AddComponent<SpellLingeringArea2D>();
            zone.Initialize(
                Execution(receiverSpell, receiverCaster, CombatTeam.Player),
                2f,
                5f,
                0.5f,
                0,
                8,
                Color.gray,
                0,
                startsActive: false);

            SpellDefinition sourceSpell = CreateSpell("source-any-delivery");
            Assert.That(zone.InteractionActive, Is.False);
            SpellDeliveryInteractionService.EmitPoint(
                Execution(sourceSpell, sourceCaster, CombatTeam.Enemy),
                Vector2.zero,
                DeliveryContactPhase.Impact,
                sourceRuntimeId: 42);
            Assert.That(zone.InteractionActive, Is.True);

            Object.DestroyImmediate(zoneObject);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(receiverSpell);
        }

        [Test]
        public void ReactionResponses_RunInOrderAndRespectOnceTotalPolicy()
        {
            SpellDefinition receiverSpell = CreateSpell("receiver-sequence");
            var reaction = new SpellReactionSlot(
                new DeliveryInteractionFilter(),
                null,
                InteractionTriggerPolicy.OnceTotal);
            reaction.ReplaceResponses(
                new TrackingResponse(1),
                new TrackingResponse(2),
                new TrackingResponse(3));
            receiverSpell.ReplaceReactionSlots(reaction);

            var zoneObject = new GameObject("Reaction Sequence Zone");
            SpellLingeringArea2D zone =
                zoneObject.AddComponent<SpellLingeringArea2D>();
            zone.Initialize(
                Execution(receiverSpell, receiverCaster, CombatTeam.Player),
                2f,
                5f,
                0.5f,
                0,
                8,
                Color.gray,
                0,
                startsActive: false);

            SpellDefinition sourceSpell = CreateSpell("source-sequence");
            DeliveryInteractionContext interaction =
                new DeliveryInteractionContext(
                    Execution(sourceSpell, sourceCaster, CombatTeam.Enemy),
                    Execution(receiverSpell, receiverCaster, CombatTeam.Player),
                    DeliveryContactPhase.Impact,
                    Vector2.zero,
                    123);
            zone.ReceiveInteraction(interaction);
            zone.ReceiveInteraction(interaction);

            Assert.That(responseLog, Is.EqualTo(new[] { 1, 2, 3 }));

            Object.DestroyImmediate(zoneObject);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(receiverSpell);
        }

        [Test]
        public void ReactiveEffectGroup_EnablesAndAppliesToCurrentAndFutureOccupants()
        {
            DamageEffectDefinition damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            SpellDefinition spell = CreateSpell("receiver-reactive-group");
            var group = new SpellReactiveEffectGroup(
                "Projectile Burn",
                activeAtStart: false,
                effects: new[]
                {
                    new SpellEffectSlot(
                        damage,
                        new DamageEffectSettings(5f))
                },
                inheritSpellTargetRules: false,
                groupTargetFilter: new TargetFilter(
                    TargetRelationship.Enemies));
            spell.ReplaceReactiveEffectGroups(group);
            spell.ReplaceReactionSlots(
                new SpellReactionSlot(
                    new DeliveryInteractionFilter(),
                    new SetReactiveEffectGroupActiveResponse(
                        group.StableId,
                        shouldBeActive: true,
                        applyImmediately: true),
                    InteractionTriggerPolicy.OnceTotal));

            GameObject currentTarget = CreateTarget(
                "Current Enemy",
                CombatTeam.Enemy,
                Vector2.zero);
            SpellVitality currentVitality =
                currentTarget.GetComponent<SpellVitality>();
            var zoneObject = new GameObject("Reactive Zone");
            SpellLingeringArea2D zone =
                zoneObject.AddComponent<SpellLingeringArea2D>();
            Physics2D.SyncTransforms();
            zone.Initialize(
                Execution(spell, receiverCaster, CombatTeam.Player),
                2f,
                5f,
                0.5f,
                1 << 0,
                8,
                Color.gray,
                0,
                startsActive: true);

            Assert.That(currentVitality.CurrentHealth, Is.EqualTo(100f));
            SpellDefinition sourceSpell = CreateSpell(
                "source-reactive-group-trigger");
            zone.ReceiveInteraction(new DeliveryInteractionContext(
                Execution(
                    sourceSpell,
                    sourceCaster,
                    CombatTeam.Enemy),
                Execution(
                    spell,
                    receiverCaster,
                    CombatTeam.Player),
                DeliveryContactPhase.Impact,
                Vector2.zero,
                77));
            Assert.That(zone.IsReactiveEffectGroupActive(group.StableId),
                Is.True);
            Assert.That(currentVitality.CurrentHealth, Is.EqualTo(95f));

            GameObject futureTarget = CreateTarget(
                "Future Enemy",
                CombatTeam.Enemy,
                new Vector2(0.5f, 0f));
            SpellVitality futureVitality =
                futureTarget.GetComponent<SpellVitality>();
            Physics2D.SyncTransforms();
            zone.PulseEffectsOnOccupants();

            Assert.That(futureVitality.CurrentHealth, Is.EqualTo(95f));
            Assert.That(currentVitality.CurrentHealth, Is.EqualTo(90f));

            Object.DestroyImmediate(zoneObject);
            Object.DestroyImmediate(currentTarget);
            Object.DestroyImmediate(futureTarget);
            Object.DestroyImmediate(sourceSpell);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(damage);
        }

        [Test]
        public void ReactiveEffectGroup_UsesItsOwnTargetRules()
        {
            DamageEffectDefinition damage = ScriptableObject.CreateInstance<
                DamageEffectDefinition>();
            SpellDefinition spell = CreateSpell("receiver-group-filter");
            spell.ReplaceReactiveEffectGroups(
                new SpellReactiveEffectGroup(
                    "Enemy Only",
                    activeAtStart: true,
                    effects: new[]
                    {
                        new SpellEffectSlot(
                            damage,
                            new DamageEffectSettings(5f))
                    },
                    inheritSpellTargetRules: false,
                    groupTargetFilter: new TargetFilter(
                        TargetRelationship.Enemies)));

            GameObject enemy = CreateTarget(
                "Enemy",
                CombatTeam.Enemy,
                new Vector2(-0.5f, 0f));
            GameObject ally = CreateTarget(
                "Ally",
                CombatTeam.Player,
                new Vector2(0.5f, 0f));
            var zoneObject = new GameObject("Filtered Reactive Zone");
            SpellLingeringArea2D zone =
                zoneObject.AddComponent<SpellLingeringArea2D>();
            Physics2D.SyncTransforms();
            zone.Initialize(
                Execution(spell, receiverCaster, CombatTeam.Player),
                2f,
                5f,
                0.5f,
                1 << 0,
                8,
                Color.gray,
                0,
                startsActive: true);

            Assert.That(
                enemy.GetComponent<SpellVitality>().CurrentHealth,
                Is.EqualTo(95f));
            Assert.That(
                ally.GetComponent<SpellVitality>().CurrentHealth,
                Is.EqualTo(100f));

            Object.DestroyImmediate(zoneObject);
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(ally);
            Object.DestroyImmediate(spell);
            Object.DestroyImmediate(damage);
        }

        private DeliveryInteractionContext InteractionContext(
            CombatTeam sourceTeam,
            CombatTeam receiverTeam,
            DeliveryContactPhase phase)
        {
            return InteractionContext(
                CreateSpell("temporary-source"),
                CreateSpell("temporary-receiver"),
                sourceTeam,
                receiverTeam,
                phase);
        }

        private DeliveryInteractionContext InteractionContext(
            SpellDefinition sourceSpell,
            SpellDefinition receiverSpell,
            CombatTeam sourceTeam,
            CombatTeam receiverTeam,
            DeliveryContactPhase phase)
        {
            return new DeliveryInteractionContext(
                Execution(sourceSpell, sourceCaster, sourceTeam),
                Execution(receiverSpell, receiverCaster, receiverTeam),
                phase,
                Vector2.zero,
                1);
        }

        private static SpellExecutionContext Execution(
            SpellDefinition spell,
            GameObject caster,
            CombatTeam team)
        {
            var cast = new CastContext(
                caster,
                team,
                caster.transform.position,
                Vector2.right,
                true,
                caster.transform.position,
                true,
                null);
            return new SpellExecutionContext(spell, cast);
        }

        private static SpellDefinition CreateSpell(string stableId)
        {
            SpellDefinition spell = ScriptableObject.CreateInstance<
                SpellDefinition>();
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("displayName").stringValue = stableId;
            serialized.FindProperty("stableId").stringValue = stableId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return spell;
        }

        private static GameObject CreateTarget(
            string name,
            CombatTeam team,
            Vector2 position)
        {
            var target = new GameObject(name);
            target.transform.position = position;
            target.AddComponent<CircleCollider2D>();
            target.AddComponent<SpellVitality>();
            target.AddComponent<CombatTeamMember>().SetTeam(team);
            return target;
        }

        private sealed class TestInteractionVolume :
            ISpellDeliveryInteractionVolume
        {
            public int InteractionRuntimeId { get; } = 999;
            public SpellExecutionContext InteractionExecutionContext { get; }
            public Vector2 InteractionCenter { get; }
            public float InteractionRadius { get; }
            public int ReceiveCount { get; private set; }

            public TestInteractionVolume(
                SpellDefinition spell,
                GameObject caster,
                Vector2 center,
                float radius)
            {
                InteractionExecutionContext = Execution(
                    spell,
                    caster,
                    CombatTeam.Player);
                InteractionCenter = center;
                InteractionRadius = radius;
            }

            public void ReceiveInteraction(
                in DeliveryInteractionContext context)
            {
                ReceiveCount++;
            }
        }

        [System.Serializable]
        private sealed class TrackingResponse : DeliveryInteractionResponse
        {
            [SerializeField]
            private int marker;

            public override string DisplayName => "Track Test Response";

            public TrackingResponse(int value)
            {
                marker = value;
            }

            public override void Execute(
                ISpellDeliveryReactionHost host,
                in DeliveryInteractionContext context)
            {
                responseLog.Add(marker);
            }
        }
    }
}
