using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEditor;
using UnityEngine;

public sealed class PlayerIntegrationSetupWindow : EditorWindow
{
    private const string ContentRoot =
        "Assets/Combat/SkillSystemV2/PlayerIntegrationContent";
    private const string SpellsRoot = ContentRoot + "/Spells";
    private const string DeliveriesRoot = ContentRoot + "/Deliveries";
    private const string EffectsRoot = ContentRoot + "/Effects";
    private const string TargetingRoot = ContentRoot + "/Targeting";

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
            "Creates four editable vertical-slice spells and equips them on the combat pawn. " +
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
            "Starter spells: Quick Shot (projectile + damage), Slash (melee arc + damage + light knockback), " +
            "Pushback (melee arc + impulse), and Slow Orb Prototype (point area + movement slow). " +
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
        SetFloat(slow, "movementMultiplier", 0.25f);
        SetFloat(slow, "duration", 0.75f);

        PlayerTargetingDefinition quickTargeting =
            AssetDatabase.LoadAssetAtPath<PlayerTargetingDefinition>(
                "Assets/Combat/SkillSystemV2/Presets/Targeting/Targeting_QuickShot.asset");
        PlayerTargetingDefinition slashTargeting =
            AssetDatabase.LoadAssetAtPath<PlayerTargetingDefinition>(
                "Assets/Combat/SkillSystemV2/Presets/Targeting/Targeting_Slash.asset");
        PlayerTargetingDefinition pushTargeting =
            AssetDatabase.LoadAssetAtPath<PlayerTargetingDefinition>(
                "Assets/Combat/SkillSystemV2/Presets/Targeting/Targeting_Pushback.asset");
        PlayerTargetingDefinition pointTargeting =
            AssetDatabase.LoadAssetAtPath<PlayerTargetingDefinition>(
                "Assets/Combat/SkillSystemV2/Presets/Targeting/Targeting_PointArea.asset");

        ProjectileDeliveryDefinition projectileDelivery =
            CreateOrLoad<ProjectileDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_Projectile.asset");

        MeleeArcDeliveryDefinition meleeArcDelivery =
            CreateOrLoad<MeleeArcDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_MeleeArc.asset");

        LingeringAreaDeliveryDefinition lingeringAreaDelivery =
            CreateOrLoad<LingeringAreaDeliveryDefinition>(
                $"{DeliveriesRoot}/Delivery_LingeringArea.asset");

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
            CreateSpell(
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
                requireSpellTarget: false)
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = spells[0];
        Debug.Log($"Created SkillSystemV2 player starter content in {ContentRoot}.");
        return spells;
    }

    private void ConfigurePawn(List<SpellDefinition> spells)
    {
        if (combatPawn == null)
            return;

        PlayerSpellV2Bridge bridge = combatPawn.GetComponent<PlayerSpellV2Bridge>();
        if (bridge == null)
            bridge = Undo.AddComponent<PlayerSpellV2Bridge>(combatPawn);

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

    [MenuItem("Tools/Project Eri/Skill System V2/Organize Player Integration Content")]
    private static void OrganizePlayerIntegrationContent()
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

        string[] guids = AssetDatabase.FindAssets(
            string.Empty,
            new[] { ContentRoot });
        int movedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guids[i]);
            string sourceDirectory = System.IO.Path
                .GetDirectoryName(sourcePath)
                ?.Replace('\\', '/');
            if (!string.Equals(
                    sourceDirectory,
                    ContentRoot,
                    StringComparison.Ordinal))
            {
                continue;
            }

            UnityEngine.Object asset =
                AssetDatabase.LoadMainAssetAtPath(sourcePath);
            string destinationRoot = GetDestinationRoot(asset);
            if (string.IsNullOrEmpty(destinationRoot))
                continue;

            string destinationPath =
                $"{destinationRoot}/{System.IO.Path.GetFileName(sourcePath)}";
            if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
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
        return null;
    }
}
