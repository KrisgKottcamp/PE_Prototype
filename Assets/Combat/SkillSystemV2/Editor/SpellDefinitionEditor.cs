using System.Collections.Generic;
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
        private SerializedProperty delivery;
        private SerializedProperty effects;
        private SerializedProperty maximumChainDepth;
        private SerializedProperty maximumRootActivations;

        private readonly List<SpellValidationIssue> issues =
            new List<SpellValidationIssue>();

        private void OnEnable()
        {
            displayName = serializedObject.FindProperty("displayName");
            stableId = serializedObject.FindProperty("stableId");
            description = serializedObject.FindProperty("description");
            icon = serializedObject.FindProperty("icon");
            category = serializedObject.FindProperty("category");
            timing = serializedObject.FindProperty("timing");
            cooldown = serializedObject.FindProperty("cooldown");
            resourceCost = serializedObject.FindProperty("resourceCost");
            targetFilter = serializedObject.FindProperty("targetFilter");
            delivery = serializedObject.FindProperty("delivery");
            effects = serializedObject.FindProperty("effects");
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

            EditorGUILayout.PropertyField(delivery);
            EditorGUILayout.PropertyField(effects, includeChildren: true);
            EditorGUILayout.Space();
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
