using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Tests
{
    public sealed class EventEffectRouteTests
    {
        private readonly List<Object> createdAssets = new List<Object>();
        private readonly List<GameObject> createdObjects =
            new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            SpellDeliveryInteractionService.ClearAllRegistrations();
        }

        [TearDown]
        public void TearDown()
        {
            SpellDeliveryInteractionService.ClearAllRegistrations();
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                    Object.DestroyImmediate(createdObjects[i]);
            }
            for (int i = createdAssets.Count - 1; i >= 0; i--)
            {
                if (createdAssets[i] != null)
                    Object.DestroyImmediate(createdAssets[i]);
            }
            createdObjects.Clear();
            createdAssets.Clear();
        }

        [Test]
        public void TargetHitRecipe_AppliesEffectsToEventSubject()
        {
            GameObject caster = CreateObject("Player Caster");
            caster.AddComponent<CombatTeamMember>().SetTeam(
                CombatTeam.Player);
            GameObject enemy = CreateObject("Enemy Subject");
            enemy.AddComponent<CombatTeamMember>().SetTeam(CombatTeam.Enemy);
            SpellVitality vitality = enemy.AddComponent<SpellVitality>();
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            SpellDefinition spell = CreateSpell("event-target-hit");
            spell.ReplaceEventEffectRoutes(new SpellEventEffectRoute(
                "Damage What Was Hit",
                SpellEventType.TargetHit,
                SpellEventRecipient.EventSubject,
                new[]
                {
                    new SpellEffectSlot(
                        damage,
                        new DamageEffectSettings(12f))
                }));

            int applied = Execution(
                    spell,
                    caster,
                    CombatTeam.Player)
                .DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    enemy,
                    enemy.transform.position,
                    Vector2.left));

            Assert.That(applied, Is.EqualTo(1));
            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(88f).Within(0.001f));
        }

        [Test]
        public void WorldPointRecipe_CanSpawnWithoutAnObjectRecipient()
        {
            GameObject caster = CreateObject("Caster");
            GameObject prefab = CreateObject("Route Spawn Prefab");
            SpawnEffectDefinition spawn = Create<SpawnEffectDefinition>();
            SpellDefinition spell = CreateSpell("event-world-point");
            spell.ReplaceEventEffectRoutes(new SpellEventEffectRoute(
                "Spawn at Area Creation",
                SpellEventType.AreaCreated,
                SpellEventRecipient.WorldPoint,
                new[]
                {
                    new SpellEffectSlot(
                        spawn,
                        new SpawnEffectSettings(
                            prefab,
                            SpellSpawnPosition.HitPoint,
                            Vector2.zero,
                            SpellSpawnRotation.Identity,
                            0f,
                            false,
                            0f,
                            SpellTimeMode.Scaled))
                },
                SpellEventSubjectRuleMode.NoRestrictions));

            int applied = Execution(
                    spell,
                    caster,
                    CombatTeam.Player)
                .DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.AreaCreated,
                    null,
                    new Vector2(3f, 4f),
                    Vector2.zero));

            GameObject clone = GameObject.Find("Route Spawn Prefab(Clone)");
            if (clone != null)
                createdObjects.Add(clone);
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(clone, Is.Not.Null);
            Assert.That((Vector2)clone.transform.position,
                Is.EqualTo(new Vector2(3f, 4f)));
        }

        [Test]
        public void ProjectileStopRecipe_TeleportsEnemyCasterToImpactPoint()
        {
            GameObject caster = CreateObject("Enemy Caster");
            caster.AddComponent<CombatTeamMember>().SetTeam(CombatTeam.Enemy);
            caster.AddComponent<BoxCollider2D>().size = Vector2.one;
            GameObject wall = CreateObject("Wall");
            wall.transform.position = new Vector2(2f, 0f);
            wall.AddComponent<BoxCollider2D>().size = Vector2.one;

            CasterMovementEffectDefinition movement =
                Create<CasterMovementEffectDefinition>();
            SpellDefinition spell = CreateSpell("event-impact-teleport");
            spell.ReplaceEventEffectRoutes(new SpellEventEffectRoute(
                "Teleport on Stop",
                SpellEventType.DeliveryStopped,
                SpellEventRecipient.Caster,
                new[]
                {
                    new SpellEffectSlot(
                        movement,
                        new CasterMovementEffectSettings(
                            10f,
                            5f,
                            true,
                            false,
                            0,
                            CasterMovementDestinationSource
                                .DeliveryEventPoint,
                            true,
                            0.08f))
                },
                SpellEventSubjectRuleMode.RequireEventSubject));

            GameObject projectileObject = CreateObject("Route Projectile");
            SpellProjectile2D projectile =
                projectileObject.AddComponent<SpellProjectile2D>();
            Physics2D.SyncTransforms();
            projectile.Launch(
                Execution(spell, caster, CombatTeam.Enemy),
                Vector2.right,
                10f,
                5f,
                0f,
                1 << wall.layer,
                false,
                1,
                true,
                8,
                SpellTimeMode.Scaled);
            projectile.Step(0.25f);

            Assert.That(projectile.IsComplete, Is.True);
            Assert.That(caster.transform.position.x,
                Is.EqualTo(0.92f).Within(0.06f));
            Assert.That(caster.transform.position.y,
                Is.Zero.Within(0.001f));
        }

        [Test]
        public void CustomSubjectRules_CanRejectAnOtherwiseMatchingEvent()
        {
            GameObject caster = CreateObject("Player Caster");
            GameObject ally = CreateObject("Player Ally");
            ally.AddComponent<CombatTeamMember>().SetTeam(CombatTeam.Player);
            SpellVitality vitality = ally.AddComponent<SpellVitality>();
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            SpellDefinition spell = CreateSpell("event-custom-filter");
            spell.ReplaceEventEffectRoutes(new SpellEventEffectRoute(
                "Enemy Only Hit",
                SpellEventType.TargetHit,
                SpellEventRecipient.EventSubject,
                new[]
                {
                    new SpellEffectSlot(
                        damage,
                        new DamageEffectSettings(20f))
                },
                SpellEventSubjectRuleMode.CustomRules,
                new TargetFilter(TargetRelationship.Enemies)));

            int applied = Execution(
                    spell,
                    caster,
                    CombatTeam.Player)
                .DispatchEvent(new SpellEventOccurrence(
                    SpellEventType.TargetHit,
                    ally,
                    Vector2.zero,
                    Vector2.zero));

            Assert.That(applied, Is.Zero);
            Assert.That(vitality.CurrentHealth, Is.EqualTo(100f));
        }

        [Test]
        public void DeliveryReaction_CanRunManualEventEffectRecipe()
        {
            GameObject receiverCaster = CreateObject("Receiver Caster");
            SpellVitality vitality =
                receiverCaster.AddComponent<SpellVitality>();
            GameObject sourceCaster = CreateObject("Source Caster");
            DamageEffectDefinition damage = Create<DamageEffectDefinition>();
            SpellDefinition receiverSpell = CreateSpell(
                "event-manual-reaction");
            var recipe = new SpellEventEffectRoute(
                "Damage Receiver Caster",
                SpellEventType.ManualReaction,
                SpellEventRecipient.Caster,
                new[]
                {
                    new SpellEffectSlot(
                        damage,
                        new DamageEffectSettings(5f))
                },
                SpellEventSubjectRuleMode.NoRestrictions);
            receiverSpell.ReplaceEventEffectRoutes(recipe);
            receiverSpell.ReplaceReactionSlots(new SpellReactionSlot(
                new DeliveryInteractionFilter(),
                new RunEventEffectRouteResponse(recipe.StableId),
                InteractionTriggerPolicy.OnceTotal));

            GameObject zoneObject = CreateObject("Manual Recipe Zone");
            SpellLingeringArea2D zone =
                zoneObject.AddComponent<SpellLingeringArea2D>();
            zone.Initialize(
                Execution(
                    receiverSpell,
                    receiverCaster,
                    CombatTeam.Player),
                1f,
                5f,
                0.5f,
                0,
                4,
                Color.gray,
                0,
                startsActive: false);

            SpellDefinition sourceSpell = CreateSpell(
                "event-manual-source");
            zone.ReceiveInteraction(new DeliveryInteractionContext(
                Execution(sourceSpell, sourceCaster, CombatTeam.Enemy),
                Execution(
                    receiverSpell,
                    receiverCaster,
                    CombatTeam.Player),
                DeliveryContactPhase.Impact,
                Vector2.one,
                123));

            Assert.That(vitality.CurrentHealth,
                Is.EqualTo(95f).Within(0.001f));
        }

        private SpellDefinition CreateSpell(string stableId)
        {
            SpellDefinition spell = Create<SpellDefinition>();
            var serialized = new SerializedObject(spell);
            serialized.FindProperty("displayName").stringValue = stableId;
            serialized.FindProperty("stableId").stringValue = stableId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return spell;
        }

        private static SpellExecutionContext Execution(
            SpellDefinition spell,
            GameObject caster,
            CombatTeam team)
        {
            return new SpellExecutionContext(
                spell,
                new CastContext(
                    caster,
                    team,
                    caster.transform.position,
                    Vector2.right,
                    true,
                    caster.transform.position,
                    true,
                    null));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            createdAssets.Add(instance);
            return instance;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            createdObjects.Add(result);
            return result;
        }
    }
}
