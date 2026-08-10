using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Editor
{
    [CustomEditor(typeof(SpellDefinition))]
    [CanEditMultipleObjects]
    public sealed class SpellDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty displayName;
        private SerializedProperty stableId;
        private SerializedProperty description;
        private SerializedProperty icon;
        private SerializedProperty category;
        private SerializedProperty timing;
        private SerializedProperty cooldown;
        private SerializedProperty resourceCost;
        private SerializedProperty targetFilter;
        private SerializedProperty deliverySlot;
        private SerializedProperty effectSlots;
        private SerializedProperty maximumChainDepth;
        private SerializedProperty maximumRootActivations;

        private readonly List<SpellValidationIssue> issues =
            new List<SpellValidationIssue>();

        private void OnEnable()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var spell = targets[i] as SpellDefinition;
                if (spell != null)
                {
                    bool changed = spell.EnsureDeliverySlot();
                    changed |= spell.EnsureEffectSlots();
                    if (changed)
                        EditorUtility.SetDirty(spell);
                }
            }

            displayName = serializedObject.FindProperty("displayName");
            stableId = serializedObject.FindProperty("stableId");
            description = serializedObject.FindProperty("description");
            icon = serializedObject.FindProperty("icon");
            category = serializedObject.FindProperty("category");
            timing = serializedObject.FindProperty("timing");
            cooldown = serializedObject.FindProperty("cooldown");
            resourceCost = serializedObject.FindProperty("resourceCost");
            targetFilter = serializedObject.FindProperty("targetFilter");
            deliverySlot = serializedObject.FindProperty("deliverySlot");
            effectSlots = serializedObject.FindProperty("effectSlots");
            maximumChainDepth = serializedObject.FindProperty(
                "maximumChainDepth");
            maximumRootActivations = serializedObject.FindProperty(
                "maximumRootActivations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawIdentity();
            DrawSection("Phase Timing", timing);
            EditorGUILayout.PropertyField(cooldown);
            DrawSection("Resource Cost", resourceCost);
            DrawSection("Target Rules", targetFilter);
            DrawComposition();
            DrawChainSafety();

            serializedObject.ApplyModifiedProperties();
            DrawValidation();
        }

        private void DrawIdentity()
        {
            EditorGUILayout.LabelField(
                "Identity",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(displayName);
            EditorGUILayout.PropertyField(stableId);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Generate Stable ID", GUILayout.Width(150f)))
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        var spell = (SpellDefinition)targets[i];
                        Undo.RecordObject(spell, "Generate Spell Stable ID");
                        spell.RegenerateStableId();
                        EditorUtility.SetDirty(spell);
                    }

                    serializedObject.Update();
                }
            }

            EditorGUILayout.PropertyField(description);
            EditorGUILayout.PropertyField(icon);
            EditorGUILayout.PropertyField(category);
            EditorGUILayout.Space();
        }

        private static void DrawSection(
            string title,
            SerializedProperty property)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(property, includeChildren: true);
            EditorGUILayout.Space();
        }

        private void DrawComposition()
        {
            EditorGUILayout.LabelField(
                "Composition",
                EditorStyles.boldLabel);

            if (targets.Length > 1)
            {
                EditorGUILayout.PropertyField(
                    deliverySlot,
                    includeChildren: true);
                EditorGUILayout.PropertyField(effectSlots, includeChildren: true);
            }
            else
            {
                DrawDeliverySlot();
                DrawEffectSlots();
            }
            EditorGUILayout.Space();
        }

        private void DrawDeliverySlot()
        {
            if (deliverySlot == null)
                return;

            SerializedProperty delivery =
                deliverySlot.FindPropertyRelative("delivery");
            SerializedProperty settings =
                deliverySlot.FindPropertyRelative("settings");
            var definition =
                delivery.objectReferenceValue as DeliveryDefinition;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Delivery",
                    EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    delivery,
                    new GUIContent("Delivery Module"));
                if (EditorGUI.EndChangeCheck())
                {
                    definition =
                        delivery.objectReferenceValue as DeliveryDefinition;
                    settings.managedReferenceValue = definition != null
                        ? definition.CreateDefaultSettings()
                        : null;
                }

                if (definition == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a delivery module to populate its settings.",
                        MessageType.Info);
                    return;
                }

                EnsureCompatibleSettings(definition, settings);
                if (definition.SettingsType == null)
                {
                    EditorGUILayout.HelpBox(
                        "This delivery currently uses its shared asset settings.",
                        MessageType.None);
                    return;
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    "Per-Spell Delivery Settings",
                    EditorStyles.boldLabel);
                DrawManagedReferenceChildren(settings);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            "Reset to Defaults",
                            GUILayout.Width(140f)))
                    {
                        settings.managedReferenceValue =
                            definition.CreateDefaultSettings();
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawEffectSlots()
        {
            if (effectSlots == null)
                return;

            for (int i = 0; i < effectSlots.arraySize; i++)
                DrawEffectSlot(i);

            if (GUILayout.Button("+ Add Effect", GUILayout.Height(24f)))
                ShowAddEffectMenu();
        }

        private void DrawEffectSlot(int index)
        {
            SerializedProperty slot = effectSlots.GetArrayElementAtIndex(index);
            SerializedProperty effect = slot.FindPropertyRelative("effect");
            SerializedProperty settings = slot.FindPropertyRelative("settings");
            var definition = effect.objectReferenceValue as EffectDefinition;
            string title = definition != null
                ? definition.DisplayName
                : $"Effect {index + 1}";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.isExpanded = EditorGUILayout.Foldout(
                        slot.isExpanded,
                        title,
                        toggleOnLabelClick: true);

                    GUI.enabled = index > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(24f)))
                    {
                        effectSlots.MoveArrayElement(index, index - 1);
                        GUI.enabled = true;
                        return;
                    }

                    GUI.enabled = index < effectSlots.arraySize - 1;
                    if (GUILayout.Button("▼", GUILayout.Width(24f)))
                    {
                        effectSlots.MoveArrayElement(index, index + 1);
                        GUI.enabled = true;
                        return;
                    }

                    GUI.enabled = true;
                    if (GUILayout.Button("×", GUILayout.Width(24f)))
                    {
                        effectSlots.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                if (!slot.isExpanded)
                    return;

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(effect, new GUIContent("Effect"));
                if (EditorGUI.EndChangeCheck())
                {
                    definition = effect.objectReferenceValue as EffectDefinition;
                    settings.managedReferenceValue = definition != null
                        ? definition.CreateDefaultSettings()
                        : null;
                }

                if (definition == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign an effect module to populate its settings.",
                        MessageType.Info);
                    return;
                }

                EnsureCompatibleSettings(definition, settings);
                if (definition.SettingsType == null)
                {
                    EditorGUILayout.HelpBox(
                        "This effect currently uses its shared asset settings.",
                        MessageType.None);
                    return;
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Per-Spell Settings", EditorStyles.boldLabel);
                DrawManagedReferenceChildren(settings);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reset to Defaults", GUILayout.Width(140f)))
                        settings.managedReferenceValue =
                            definition.CreateDefaultSettings();
                }
            }
        }

        private static void EnsureCompatibleSettings(
            EffectDefinition definition,
            SerializedProperty settings)
        {
            System.Type expected = definition.SettingsType;
            object current = settings.managedReferenceValue;
            if (expected == null)
            {
                if (current != null)
                    settings.managedReferenceValue = null;
                return;
            }

            if (current == null || current.GetType() != expected)
                settings.managedReferenceValue = definition.CreateDefaultSettings();
        }

        private static void EnsureCompatibleSettings(
            DeliveryDefinition definition,
            SerializedProperty settings)
        {
            System.Type expected = definition.SettingsType;
            object current = settings.managedReferenceValue;
            if (expected == null)
            {
                if (current != null)
                    settings.managedReferenceValue = null;
                return;
            }

            if (current == null || current.GetType() != expected)
            {
                settings.managedReferenceValue =
                    definition.CreateDefaultSettings();
            }
        }

        private static void DrawManagedReferenceChildren(
            SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                EditorGUILayout.PropertyField(iterator, includeChildren: true);
                enterChildren = false;
            }
        }

        private void ShowAddEffectMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Empty Slot"),
                false,
                () => AddEffectSlot(null));
            menu.AddSeparator(string.Empty);

            string[] guids = AssetDatabase.FindAssets("t:EffectDefinition");
            var definitions = new List<EffectDefinition>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EffectDefinition definition =
                    AssetDatabase.LoadAssetAtPath<EffectDefinition>(path);
                if (definition != null)
                    definitions.Add(definition);
            }

            definitions.Sort((a, b) => string.Compare(
                a.DisplayName,
                b.DisplayName,
                System.StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < definitions.Count; i++)
            {
                EffectDefinition definition = definitions[i];
                string path = AssetDatabase.GetAssetPath(definition);
                string folder = Path.GetFileName(
                    Path.GetDirectoryName(path));
                string label = string.IsNullOrWhiteSpace(folder)
                    ? definition.DisplayName
                    : $"{definition.DisplayName} ({folder})";
                menu.AddItem(
                    new GUIContent(label),
                    false,
                    () => AddEffectSlot(definition));
            }

            menu.ShowAsContext();
        }

        private void AddEffectSlot(EffectDefinition definition)
        {
            serializedObject.Update();
            int index = effectSlots.arraySize;
            effectSlots.arraySize++;
            SerializedProperty slot = effectSlots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("effect").objectReferenceValue = definition;
            slot.FindPropertyRelative("settings").managedReferenceValue =
                definition != null ? definition.CreateDefaultSettings() : null;
            slot.isExpanded = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawChainSafety()
        {
            EditorGUILayout.LabelField(
                "Chain Safety",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(maximumChainDepth);
            EditorGUILayout.PropertyField(maximumRootActivations);
            EditorGUILayout.Space();
        }

        private void DrawValidation()
        {
            if (targets.Length != 1)
                return;

            var spell = (SpellDefinition)target;
            issues.Clear();
            spell.CollectValidationIssues(issues);

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Spell definition is valid.",
                    MessageType.Info);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                EditorGUILayout.HelpBox(
                    issues[i].Message,
                    ToMessageType(issues[i].Severity));
            }
        }

        private static MessageType ToMessageType(
            SpellValidationSeverity severity)
        {
            switch (severity)
            {
                case SpellValidationSeverity.Error:
                    return MessageType.Error;
                case SpellValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
