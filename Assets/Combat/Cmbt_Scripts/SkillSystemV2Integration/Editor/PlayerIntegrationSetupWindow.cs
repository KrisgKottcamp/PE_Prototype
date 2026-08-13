using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEditor;
using UnityEngine;

public sealed class PlayerIntegrationSetupWindow : EditorWindow
{
    private const string ContentRoot =
        "Assets/Combat/SkillSystemV2/Content";
    private const string LegacyPlayerContentRoot =
        "Assets/Combat/SkillSystemV2/PlayerIntegrationContent";
    private const string LegacyPresetsRoot =
        "Assets/Combat/SkillSystemV2/Presets";
    private const string SpellsRoot = ContentRoot + "/Spells";
    private const string DeliveriesRoot = ContentRoot + "/Deliveries";
    private const string EffectsRoot = ContentRoot + "/Effects";
    private const string TargetingRoot = ContentRoot + "/Targeting";
    private const string DefinitionsRoot = ContentRoot + "/Definitions";

    private GameObject combatPawn;
    private GameObject projectileVisualPrefab;
    private Vector2 scroll;

    [MenuItem("Tools/Project Eri/Skill System V2/Player Integration Setup")]
    public static void Open()
    {
        GetWindow<PlayerIntegrationSetupWindow>(
            utility: false,
            title: "Skill V2 Player Setup");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("SkillSystemV2 Player Integration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates seven editable vertical-slice spells and equips them on the combat pawn. " +
            "The existing menu remains the UI and legacy skills remain available whenever the V2 loadout is empty.",
            MessageType.Info);

        combatPawn = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Combat Pawn", "Scene pawn or pawn prefab to receive the V2 runtime components."),
            combatPawn,
            typeof(GameObject),
            true);
        projectileVisualPrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Projectile Visual (Optional)", "V2 automatically disables legacy projectile gameplay components on the spawned copy while preserving its renderer."),
            projectileVisualPrefab,
            typeof(GameObject),
            false);

        EditorGUILayout.Space();
        if (GUILayout.Button("Organize Existing Skill Assets"))
        {
            int movedCount = OrganizeLooseContent();
            EditorUtility.DisplayDialog(
                "Skill V2 Content Organizer",
                movedCount > 0
                    ? $"Moved {movedCount} asset(s) into organized folders."
                    : "No loose skill assets needed to be moved.",
                "OK");
        }

        if (GUILayout.Button("1. Create / Refresh Starter Spell Assets"))
            CreateStarterContent();

        using (new EditorGUI.DisabledScope(combatPawn == null))
        {
            if (GUILayout.Button("2. Configure Pawn And Equip Starter Spells"))
                ConfigurePawn(CreateStarterContent());

            if (GUILayout.Button("Create Content + Configure Pawn"))
                ConfigurePawn(CreateStarterContent());
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Starter spells: Quick Shot, Slash, Pushback, Dash, Impact Teleport, Slow Orb with a projectile-activated enemy burn group, and a dormant Oil Spill that any delivery can activate. " +
            "Each spell stores compact inline settings for its equipped reusable delivery and effect modules.",
            MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    private List<SpellDefinition> CreateStarterContent()
    {
        OrganizeLooseContent();

        DamageEffectDefinition damage = CreateOrLoad<DamageEffectDefinition>(
            $"{EffectsRoot}/Effect_Damage.asset");
        SetFloat(damage, "amount", 10f);

        ImpulseEffectDefinition knockback =
            CreateOrLoad<ImpulseEffectDefinition>(
                $"{EffectsRoot}/Effect_Knockback.asset");
        SetFloat(knockback, "magnitude", 5f);
        SetFloat(knockback, "duration", 0.15f);

        LegacyProjectileReflectEffectDefinition reflect =
            CreateOrLoad<LegacyProjectileReflectEffectDefinition>(
                $"{EffectsRoot}/Effect_ReflectProjectile.asset");

        LegacyMovementSlowEffectDefinition slow =
            CreateOrLoad<LegacyMovementSlowEffectDefinition>(
                $"{EffectsRoot}/Effect_MovementSlow.asset");
        SetString(slow, "displayName", "Movement Speed Change");
        SetFloat(slow, "movementMultiplier", 0.25f);
        SetFloat(slow, "duration", 0.75f);

        DamageOverTimeEffectDefinition damageOverTime =
            CreateOrLoad<DamageOverTimeEffectDefinition>(
                $"{EffectsRoot}/Effect_DamageOverTime.asset");
        CasterMovementEffectDefinition casterMovement =
            CreateOrLoad<CasterMovementEffectDefinition>(
                $"{EffectsRoot}/Effect_CasterMovement.asset");
        SpellStatModifierEffectDefinition statModifier =
            CreateOrLoad<SpellStatModifierEffectDefinition>(
                $"{EffectsRoot}/Effect_StatModifier.asset");
        SpatialForceEffectDefinition spatialForce =
            CreateOrLoad<SpatialForceEffectDefinition>(
                $"{EffectsRoot}/Effect_SpatialForce.asset");
        ActorRelocationEffectDefinition relocateActor =
            CreateOrLoad<ActorRelocationEffectDefinition>(
                $"{EffectsRoot}/Effect_RelocateActor.asset");
        SetString(statModifier, "displayName", "Stat Modifier");
        SetString(spatialForce, "displayName", "Spatial Force");
        SetString(relocateActor, "displayName", "Relocate Actor");
        DamageTypeDefinition physicalDamage =
            AssetDatabase.LoadAssetAtPath<DamageTypeDefinition>(
                $"{DefinitionsRoot}/DamageType_Physical.asset");

        DirectionTargetingDefinition quickTargeting =
            CreateOrLoad<DirectionTargetingDefinition>(
                $"{TargetingRoot}/Targeting_QuickShot.asset");
        SetFloat(quickTargeting, "maximumRange", 10f);
        SetFloat(quickTargeting, "previewRadius", 0.08f);
        SetFloat(quickTargeting, "previewConeAngle", 0f);

        DirectionTargetingDefinition slashTargeting =
            CreateOrLoad<DirectionTargetingDefinition>(
                $"{TargetingRoot}/Targeting_Slash.asset");
        SetFloat(slashTargeting, "maximumRange", 1.75f);
        SetFloat(slashTargeting, "previewRadius", 0.1f);
        SetFloat(slashTargeting, "previewConeAngle", 90f);

        DirectionTargetingDefinition pushTargeting =
            CreateOrLoad<DirectionTargetingDefinition>(
                $"{TargetingRoot}/Targeting_Pushback.asset");
        SetFloat(pushTargeting, "maximumRange", 2.5f);
        SetFloat(pushTargeting, "previewRadius", 0.1f);
        SetFloat(pushTargeting, "previewConeAngle", 70f);

        PointTargetingDefinition pointTargeting =
            CreateOrLoad<PointTargetingDefinition>(
                $"{TargetingRoot}/Targeting_PointArea.asset");
        SetFloat(pointTargeting, "maximumRange", 8f);
        SetBool(pointTargeting, "clampToMaximumRange", true);
        SetFloat(pointTargeting, "previewRadius", 1.5f);
        SetBool(pointTargeting, "requirePointerWithinRange", false);
        PointTargetingDefinition pointClickTargeting =
            CreateOrLoad<PointTargetingDefinition>(
                $"{TargetingRoot}/Targeting_PointClick.asset");
        SetFloat(pointClickTargeting, "maximumRange", 0f);
        SetBool(pointClickTargeting, "clampToMaximumRange", false);
        SetFloat(pointClickTargeting, "previewRadius", 0.25f);
        SetBool(pointClickTargeting, "requirePointerWithinRange", false);

        TwoPointTargetingDefinition twoPointTargeting =
            CreateOrLoad<TwoPointTargetingDefinition>(
                $"{TargetingRoot}/Targeting_TwoPoint.asset");
        SetFloat(twoPointTargeting, "maximumRange", 10f);
        SetFloat(twoPointTargeting, "maximumSegmentLength", 8f);
        SetLayerMask(
            twoPointTargeting,
            "obstructionMask",
            LayerMask.GetMask("Obstacles"));

        MenuSelectTargetingDefinition allPartyTargeting =
            CreateMenuTargeting(
                "Targeting_Menu_AllParty",
                MenuTargetGroup.AllPartyMembers);
        MenuSelectTargetingDefinition activePartyTargeting =
            CreateMenuTargeting(
                "Targeting_Menu_ActiveParty",
                MenuTargetGroup.ActivePartyMembers);
        MenuSelectTargetingDefinition activeEnemyTargeting =
            CreateMenuTargeting(
                "Targeting_Menu_ActiveEnemies",
                MenuTargetGroup.ActiveEnemies);

        ProjectileDeliveryDefinition projectileDelivery =
            CreateOrLoad<ProjectileDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_Projectile.asset");
        SetObject(projectileDelivery, "playerTargeting", quickTargeting);

        MeleeArcDeliveryDefinition meleeArcDelivery =
            CreateOrLoad<MeleeArcDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_MeleeArc.asset");
        SetObject(meleeArcDelivery, "playerTargeting", slashTargeting);

        LingeringAreaDeliveryDefinition lingeringAreaDelivery =
            CreateOrLoad<LingeringAreaDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_LingeringArea.asset");
        SetObject(lingeringAreaDelivery, "playerTargeting", pointTargeting);

        PointClickDeliveryDefinition pointClickDelivery =
            CreateOrLoad<PointClickDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_PointClick.asset");
        SetObject(pointClickDelivery, "playerTargeting", pointClickTargeting);

        TripWireDeliveryDefinition tripWireDelivery =
            CreateOrLoad<TripWireDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_TripWire.asset");
        SetObject(tripWireDelivery, "playerTargeting", twoPointTargeting);

        ProximityMineDeliveryDefinition proximityMineDelivery =
            CreateOrLoad<ProximityMineDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_ProximityMine.asset");
        SetObject(proximityMineDelivery, "playerTargeting", pointTargeting);

        GrenadeDeliveryDefinition grenadeDelivery =
            CreateOrLoad<GrenadeDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_Grenade.asset");
        SetObject(grenadeDelivery, "playerTargeting", pointTargeting);

        RicochetProjectileDeliveryDefinition ricochetDelivery =
            CreateOrLoad<RicochetProjectileDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_RicochetProjectile.asset");
        SetObject(ricochetDelivery, "playerTargeting", quickTargeting);

        // One universal direct-target delivery covers menu-selected buffs,
        // heals, debuffs, and other immediate selected-target effects. The
        // spell chooses which menu group it uses through its inline targeting
        // setting, rather than needing separate delivery modules per group.
        InstantTargetDeliveryDefinition directTargetDelivery =
            CreateOrLoad<InstantTargetDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_DirectTarget.asset");
        SetObject(directTargetDelivery, "playerTargeting", activePartyTargeting);

        // Keep the three menu targeters visible and reusable. A designer can
        // equip any of them on Direct Target without creating a custom
        // targeting script.
        _ = allPartyTargeting;
        _ = activePartyTargeting;
        _ = activeEnemyTargeting;
        _ = directTargetDelivery;

        var spells = new List<SpellDefinition>
        {
            CreateSpell(
                "Spell_QuickShot",
                "Quick Shot V2",
                "player.quick-shot.v2",
                8f,
                new SpellDeliverySlot(
                    projectileDelivery,
                    new ProjectileDeliverySettings(
                        quickTargeting,
                        projectileVisualPrefab,
                        false,
                        14f,
                        10f,
                        0.1f,
                        LayerMask.GetMask("Obstacles", "EnemyHurtbox"),
                        false,
                        1,
                        true,
                        16)),
                new SpellEffectSlot(
                    damage,
                    new DamageEffectSettings(20f))),
            CreateSpell(
                "Spell_Slash",
                "Slash V2",
                "player.slash.v2",
                10f,
                new SpellDeliverySlot(
                    meleeArcDelivery,
                    new MeleeArcDeliverySettings(
                        slashTargeting,
                        1.75f,
                        90f,
                        LayerMask.GetMask("EnemyHurtbox"),
                        24)),
                new SpellEffectSlot(
                    damage,
                    new DamageEffectSettings(30f)),
                new SpellEffectSlot(
                    knockback,
                    new ImpulseEffectSettings(
                        SpellImpulseDirection.AwayFromCaster,
                        3.5f,
                        0.12f))),
            CreateSpell(
                "Spell_Pushback",
                "Pushback V2",
                "player.pushback.v2",
                12f,
                new SpellDeliverySlot(
                    meleeArcDelivery,
                    new MeleeArcDeliverySettings(
                        pushTargeting,
                        2.5f,
                        80f,
                        LayerMask.GetMask("EnemyHurtbox", "Projectile"),
                        24)),
                new[]
                {
                    new SpellEffectSlot(
                        knockback,
                        new ImpulseEffectSettings(
                            SpellImpulseDirection.AwayFromCaster,
                            12f,
                            0.3f)),
                    new SpellEffectSlot(reflect)
                },
                TargetRelationship.Any,
                requireSpellTarget: false),
            CreateSlowOrb(
                lingeringAreaDelivery,
                projectileDelivery,
                pointTargeting,
                slow,
                damageOverTime,
                physicalDamage),
            CreateSpell(
                "Spell_Dash",
                "Dash V2",
                "player.dash.v2",
                8f,
                new SpellDeliverySlot(
                    pointClickDelivery,
                    new PointClickDeliverySettings(pointClickTargeting)),
                new[]
                {
                    new SpellEffectSlot(
                        casterMovement,
                        new CasterMovementEffectSettings(
                            movementSpeed: 14f,
                            maxDistance: 4f,
                            moveInstantly: false,
                            lineOfSightRequired: true,
                            blockingLayers:
                                LayerMask.GetMask("Obstacles")))
                },
                TargetRelationship.Self,
                requireSpellTarget: false),
            CreateImpactTeleport(
                projectileDelivery,
                quickTargeting,
                casterMovement),
            CreateOilSpill(
                lingeringAreaDelivery,
                pointTargeting,
                damageOverTime,
                physicalDamage)
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = spells[0];
        Debug.Log($"Created SkillSystemV2 player starter content in {ContentRoot}.");
        return spells;
    }

    private static SpellDefinition CreateSlowOrb(
        LingeringAreaDeliveryDefinition lingeringAreaDelivery,
        ProjectileDeliveryDefinition projectileDelivery,
        PlayerTargetingDefinition pointTargeting,
        LegacyMovementSlowEffectDefinition slow,
        DamageOverTimeEffectDefinition damageOverTime,
        DamageTypeDefinition physicalDamage)
    {
        SpellDefinition spell = CreateSpell(
            "Spell_SlowOrbPrototype",
            "Slow Orb V2 (Prototype)",
            "player.slow-orb.v2",
            10f,
            new SpellDeliverySlot(
                lingeringAreaDelivery,
                new LingeringAreaDeliverySettings(
                    pointTargeting,
                    2f,
                    4f,
                    0.25f,
                    LayerMask.GetMask(
                        "EnemyHurtbox",
                        "PlayerHurtbox",
                        "Projectile",
                        "PlayerProjectile"),
                    32,
                    new Color(0.3f, 0.65f, 1f, 0.24f),
                    20)),
            new[]
            {
                new SpellEffectSlot(
                    slow,
                    new MovementSlowEffectSettings(0.25f, 0.75f))
            },
            TargetRelationship.Any,
            requireSpellTarget: false);

        var projectileBurn = new SpellReactiveEffectGroup(
            "Projectile Burn",
            activeAtStart: false,
            effects: new[]
            {
                new SpellEffectSlot(
                    damageOverTime,
                    new DamageOverTimeEffectSettings(
                        4f,
                        0.5f,
                        1.5f,
                        physicalDamage,
                        immediateTick: true,
                        shouldIgnoreInvulnerability: false,
                        stacking:
                            DamageOverTimeStackingPolicy.RefreshDuration))
            },
            inheritSpellTargetRules: false,
            groupTargetFilter: new TargetFilter(
                TargetRelationship.Enemies,
                requireTarget: false));
        spell.ReplaceReactiveEffectGroups(projectileBurn);

        var filter = new DeliveryInteractionFilter();
        filter.ReplaceConditions(
            InteractionFilterMatchMode.All,
            new InteractionDeliveryCondition(projectileDelivery));
        spell.ReplaceReactionSlots(
            new SpellReactionSlot(
                filter,
                new SetReactiveEffectGroupActiveResponse(
                    projectileBurn.StableId,
                    shouldBeActive: true,
                    applyImmediately: true),
                InteractionTriggerPolicy.OnceTotal));
        EditorUtility.SetDirty(spell);
        return spell;
    }

    private SpellDefinition CreateImpactTeleport(
        ProjectileDeliveryDefinition projectileDelivery,
        PlayerTargetingDefinition directionTargeting,
        CasterMovementEffectDefinition casterMovement)
    {
        SpellDefinition spell = CreateSpell(
            "Spell_ImpactTeleport",
            "Impact Teleport",
            "player.impact-teleport.v2",
            12f,
            new SpellDeliverySlot(
                projectileDelivery,
                new ProjectileDeliverySettings(
                    directionTargeting,
                    projectileVisualPrefab,
                    false,
                    16f,
                    9f,
                    0.1f,
                    LayerMask.GetMask("Obstacles", "EnemyHurtbox"),
                    false,
                    1,
                    true,
                    16)),
            new SpellEffectSlot[0],
            TargetRelationship.Any,
            requireSpellTarget: false);

        var teleportOnImpact = new SpellEventEffectRoute(
            "Teleport When the Projectile Lands",
            SpellEventType.DeliveryStopped,
            SpellEventRecipient.Caster,
            new[]
            {
                new SpellEffectSlot(
                    casterMovement,
                    new CasterMovementEffectSettings(
                        movementSpeed: 18f,
                        maxDistance: 9f,
                        moveInstantly: true,
                        lineOfSightRequired: true,
                        blockingLayers: LayerMask.GetMask("Obstacles"),
                        movementDestination:
                            CasterMovementDestinationSource
                                .DeliveryEventPoint,
                        remainOutsideHitSurface: true,
                        surfaceClearance: 0.08f))
            },
            SpellEventSubjectRuleMode.RequireEventSubject);
        spell.ReplaceEventEffectRoutes(teleportOnImpact);

        SerializedObject serialized = new SerializedObject(spell);
        serialized.FindProperty("description").stringValue =
            "Fires a projectile and teleports the caster safely beside the " +
            "enemy or wall it strikes. Demonstrates an Event Effect Recipe.";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spell);
        return spell;
    }

    private static SpellDefinition CreateOilSpill(
        LingeringAreaDeliveryDefinition lingeringAreaDelivery,
        PlayerTargetingDefinition pointTargeting,
        DamageOverTimeEffectDefinition damageOverTime,
        DamageTypeDefinition physicalDamage)
    {
        SpellDefinition spell = CreateSpell(
            "Spell_OilSpill",
            "Oil Spill",
            "player.oil-spill.v2",
            12f,
            new SpellDeliverySlot(
                lingeringAreaDelivery,
                new LingeringAreaDeliverySettings(
                    pointTargeting,
                    2.75f,
                    10f,
                    0.5f,
                    LayerMask.GetMask(
                        "EnemyHurtbox",
                        "PlayerHurtbox"),
                    32,
                    new Color(0.22f, 0.12f, 0.04f, 0.55f),
                    18,
                    activeAtStart: false)),
            new[]
            {
                new SpellEffectSlot(
                    damageOverTime,
                    new DamageOverTimeEffectSettings(
                        4f,
                        0.5f,
                        1.5f,
                        physicalDamage,
                        immediateTick: true,
                        shouldIgnoreInvulnerability: false,
                        stacking:
                            DamageOverTimeStackingPolicy.RefreshDuration))
            },
            TargetRelationship.Any,
            requireSpellTarget: false);

        var filter = new DeliveryInteractionFilter();
        filter.ReplaceConditions(InteractionFilterMatchMode.All);
        var ignition = new SpellReactionSlot(
            filter,
            null,
            InteractionTriggerPolicy.OnceTotal);
        ignition.ReplaceResponses(
            new ActivateDeliveryResponse(
                shouldBeActive: true,
                shouldPulseImmediately: false),
            new PulseEffectsResponse(),
            new DestroyDeliveryResponse());
        spell.ReplaceReactionSlots(ignition);
        EditorUtility.SetDirty(spell);
        return spell;
    }

    private void ConfigurePawn(List<SpellDefinition> spells)
    {
        if (combatPawn == null)
            return;

        PlayerSpellV2Bridge bridge = combatPawn.GetComponent<PlayerSpellV2Bridge>();
        if (bridge == null)
            bridge = Undo.AddComponent<PlayerSpellV2Bridge>(combatPawn);

        if (combatPawn.GetComponent<SpellBuildUpControl2D>() == null)
            Undo.AddComponent<SpellBuildUpControl2D>(combatPawn);

        SpellLoadout loadout = combatPawn.GetComponent<SpellLoadout>();
        if (loadout == null)
            loadout = Undo.AddComponent<SpellLoadout>(combatPawn);

        SerializedObject serializedLoadout = new SerializedObject(loadout);
        SerializedProperty equipped = serializedLoadout.FindProperty("equippedSkills");
        equipped.arraySize = spells.Count;
        for (int i = 0; i < spells.Count; i++)
            equipped.GetArrayElementAtIndex(i).objectReferenceValue = spells[i];
        serializedLoadout.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(combatPawn);
        PrefabUtility.RecordPrefabInstancePropertyModifications(loadout);
        Selection.activeGameObject = combatPawn;
        Debug.Log(
            $"Configured '{combatPawn.name}' for SkillSystemV2 and equipped {spells.Count} starter spells.",
            combatPawn);
    }

    private static SpellDefinition CreateSpell(
        string fileName,
        string displayName,
        string stableId,
        float apCost,
        SpellDeliverySlot delivery,
        params SpellEffectSlot[] effects)
    {
        return CreateSpell(
            fileName,
            displayName,
            stableId,
            apCost,
            delivery,
            effects,
            TargetRelationship.Enemies,
            requireSpellTarget: true);
    }

    private static SpellDefinition CreateSpell(
        string fileName,
        string displayName,
        string stableId,
        float apCost,
        SpellDeliverySlot delivery,
        SpellEffectSlot[] effects,
        TargetRelationship relationship,
        bool requireSpellTarget)
    {
        SpellDefinition spell = CreateOrLoad<SpellDefinition>(
            $"{SpellsRoot}/{fileName}.asset");
        SerializedObject serialized = new SerializedObject(spell);
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.FindProperty("stableId").stringValue = stableId;
        serialized.FindProperty("description").stringValue =
            "Player Integration vertical-slice spell. Tune or replace freely.";
        serialized.FindProperty("category").stringValue = "Player Skill V2";
        serialized.FindProperty("resourceCost.resourceId").stringValue =
            SpellResourceCost.ActionPoints;
        serialized.FindProperty("resourceCost.amount").floatValue = apCost;
        serialized.FindProperty("targetFilter.relationship").enumValueIndex =
            (int)relationship;
        serialized.FindProperty("targetFilter.requireSpellTarget").boolValue =
            requireSpellTarget;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        spell.ReplaceDelivery(delivery);
        spell.ReplaceEffectSlots(effects);
        EditorUtility.SetDirty(spell);
        return spell;
    }

    private static T CreateOrLoad<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        asset = CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static MenuSelectTargetingDefinition CreateMenuTargeting(
        string fileName,
        MenuTargetGroup group)
    {
        MenuSelectTargetingDefinition targeting =
            CreateOrLoad<MenuSelectTargetingDefinition>(
                $"{TargetingRoot}/{fileName}.asset");
        SerializedObject serialized = new SerializedObject(targeting);
        serialized.FindProperty("targetGroup").enumValueIndex = (int)group;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(targeting);
        return targeting;
    }

    private static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetFloat(UnityEngine.Object target, string field, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetString(
        UnityEngine.Object target,
        string field,
        string value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetBool(UnityEngine.Object target, string field, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetLayerMask(
        UnityEngine.Object target,
        string field,
        LayerMask value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).intValue = value.value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    [MenuItem("Tools/Project Eri/Skill System V2/Organize Skill Content Library")]
    private static void OrganizeSkillContentLibrary()
    {
        int movedCount = OrganizeLooseContent();
        EditorUtility.DisplayDialog(
            "Skill V2 Content Organizer",
            movedCount > 0
                ? $"Moved {movedCount} asset(s) into organized folders."
                : "No loose skill assets needed to be moved.",
            "OK");
    }

    private static int OrganizeLooseContent()
    {
        EnsureFolder(SpellsRoot);
        EnsureFolder(DeliveriesRoot);
        EnsureFolder(EffectsRoot);
        EnsureFolder(TargetingRoot);
        EnsureFolder(DefinitionsRoot);

        string[] searchRoots =
        {
            ContentRoot,
            LegacyPlayerContentRoot,
            LegacyPresetsRoot
        };
        var visitedGuids = new HashSet<string>();
        int movedCount = 0;

        for (int rootIndex = 0; rootIndex < searchRoots.Length; rootIndex++)
        {
            string searchRoot = searchRoots[rootIndex];
            if (!AssetDatabase.IsValidFolder(searchRoot))
                continue;

            string[] guids = AssetDatabase.FindAssets(
                string.Empty,
                new[] { searchRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                if (!visitedGuids.Add(guids[i]))
                    continue;

                string sourcePath =
                    AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object asset =
                    AssetDatabase.LoadMainAssetAtPath(sourcePath);
                string destinationRoot = GetDestinationRoot(asset);
                if (string.IsNullOrEmpty(destinationRoot))
                    continue;

                string sourceDirectory = System.IO.Path
                    .GetDirectoryName(sourcePath)
                    ?.Replace('\\', '/');
                if (string.Equals(
                        sourceDirectory,
                        destinationRoot,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string destinationPath =
                    $"{destinationRoot}/{System.IO.Path.GetFileName(sourcePath)}";
                UnityEngine.Object existing =
                    AssetDatabase.LoadMainAssetAtPath(destinationPath);
                if (existing != null)
                {
                    Debug.LogWarning(
                        $"Could not organize '{sourcePath}' because " +
                        $"'{destinationPath}' already exists.");
                    continue;
                }

                string error = AssetDatabase.MoveAsset(
                    sourcePath,
                    destinationPath);
                if (string.IsNullOrEmpty(error))
                {
                    movedCount++;
                }
                else
                {
                    Debug.LogError(
                        $"Could not organize '{sourcePath}': {error}");
                }
            }
        }

        RemoveEmptyLegacyFolders();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (movedCount > 0)
        {
            Debug.Log(
                $"Organized {movedCount} SkillSystemV2 asset(s) under " +
                $"'{ContentRoot}'.");
        }

        return movedCount;
    }

    private static void RemoveEmptyLegacyFolders()
    {
        string[] paths =
        {
            LegacyPlayerContentRoot + "/Effects",
            LegacyPlayerContentRoot + "/Deliveries",
            LegacyPlayerContentRoot + "/Spells",
            LegacyPlayerContentRoot + "/Targeting",
            LegacyPlayerContentRoot,
            LegacyPresetsRoot + "/Effects",
            LegacyPresetsRoot + "/Targeting",
            LegacyPresetsRoot
        };

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (!AssetDatabase.IsValidFolder(path) ||
                AssetDatabase.GetSubFolders(path).Length > 0)
            {
                continue;
            }

            string[] guids = AssetDatabase.FindAssets(
                string.Empty,
                new[] { path });
            bool containsAsset = false;
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath =
                    AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                if (!string.Equals(assetPath, path, StringComparison.Ordinal) &&
                    !AssetDatabase.IsValidFolder(assetPath))
                {
                    containsAsset = true;
                    break;
                }
            }

            if (!containsAsset)
                AssetDatabase.DeleteAsset(path);
        }
    }

    private static string GetDestinationRoot(UnityEngine.Object asset)
    {
        if (asset is SpellDefinition)
            return SpellsRoot;
        if (asset is DeliveryDefinition)
            return DeliveriesRoot;
        if (asset is EffectDefinition)
            return EffectsRoot;
        if (asset is PlayerTargetingDefinition)
            return TargetingRoot;
        if (asset is DamageTypeDefinition ||
            asset is GameplayResourceDefinition ||
            asset is GameplaySignalDefinition ||
            asset is StatusDefinition)
        {
            return DefinitionsRoot;
        }
        return null;
    }
}
