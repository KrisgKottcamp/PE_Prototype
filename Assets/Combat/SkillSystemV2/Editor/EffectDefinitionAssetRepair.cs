using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Editor
{
    /// <summary>
    /// Explicit recovery for the three final-vocabulary assets. This tool never
    /// runs automatically, so compilation, tests, and domain reloads cannot
    /// mutate designer content behind the user's back.
    /// </summary>
    internal static class EffectDefinitionAssetRepair
    {
        private const string EffectsFolder =
            "Assets/Combat/SkillSystemV2/Content/Effects";

        [MenuItem(
            "Tools/Project Eri/Skill System V2/Repair Final Vocabulary Assets")]
        private static void RepairFromMenu()
        {
            if (!AssetDatabase.IsValidFolder(EffectsFolder))
            {
                EditorUtility.DisplayDialog(
                    "Skill System V2",
                    $"The Effects folder does not exist:\n{EffectsFolder}",
                    "OK");
                return;
            }

            int recovered = 0;
            recovered += Ensure<SpatialForceEffectDefinition>(
                "Effect_SpatialForce",
                "Spatial Force");
            recovered += Ensure<ActorRelocationEffectDefinition>(
                "Effect_RelocateActor",
                "Relocate Actor");
            recovered += Ensure<SpellStatModifierEffectDefinition>(
                "Effect_StatModifier",
                "Stat Modifier");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Skill System V2",
                recovered > 0
                    ? $"Recovered {recovered} final-vocabulary asset(s)."
                    : "All three final-vocabulary assets are already healthy.",
                "OK");
        }

        private static int Ensure<T>(string fileName, string displayName)
            where T : EffectDefinition
        {
            string path = $"{EffectsFolder}/{fileName}.asset";
            bool changed = false;
            if (File.Exists(path) && TryRelink<T>(path))
                changed = true;

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                if (File.Exists(path) ||
                    AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    if (!AssetDatabase.DeleteAsset(path))
                    {
                        Debug.LogError(
                            $"Could not replace invalid effect asset at {path}.");
                        return 0;
                    }
                }

                asset = ScriptableObject.CreateInstance<T>();
                asset.name = fileName;
                AssetDatabase.CreateAsset(asset, path);
                changed = true;
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty displayNameProperty =
                serialized.FindProperty("displayName");
            if (displayNameProperty != null &&
                displayNameProperty.stringValue != displayName)
            {
                displayNameProperty.stringValue = displayName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                changed = true;
            }

            return changed ? 1 : 0;
        }

        private static bool TryRelink<T>(string assetPath)
            where T : EffectDefinition
        {
            T temporary = ScriptableObject.CreateInstance<T>();
            MonoScript script = MonoScript.FromScriptableObject(temporary);
            UnityEngine.Object.DestroyImmediate(temporary);
            if (script == null)
                return false;

            string scriptGuid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(script));
            if (string.IsNullOrWhiteSpace(scriptGuid))
                return false;

            const string marker =
                "m_Script: {fileID: 11500000, guid: ";
            string yaml = File.ReadAllText(assetPath);
            int markerIndex = yaml.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return false;

            int guidStart = markerIndex + marker.Length;
            int guidEnd = yaml.IndexOf(',', guidStart);
            if (guidEnd <= guidStart)
                return false;

            string existingGuid = yaml.Substring(
                guidStart,
                guidEnd - guidStart);
            if (string.Equals(
                    existingGuid,
                    scriptGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relinked = yaml.Substring(0, guidStart) +
                scriptGuid + yaml.Substring(guidEnd);
            File.WriteAllText(assetPath, relinked);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(assetPath) != null;
        }
    }
}
