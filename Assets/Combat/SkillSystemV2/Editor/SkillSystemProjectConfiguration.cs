using UnityEditor;
using UnityEngine;

namespace ProjectEri.SkillSystemV2.Editor
{
    /// <summary>
    /// Repairs the small set of named physics layers required by the shipped
    /// SkillSystemV2 examples. Layer configuration lives in ProjectSettings,
    /// so copying only Assets used to leave the Black Hole authoring asset with
    /// masks that could not resolve PlayerProjectile in another checkout.
    /// </summary>
    public static class SkillSystemProjectConfiguration
    {
        private const string TagManagerPath =
            "ProjectSettings/TagManager.asset";
        private const string BlackHolePath =
            "Assets/Combat/SkillSystemV2/Content/Spells/" +
            "Spell_BlackHole.asset";

        private static readonly string[] RequiredLayers =
        {
            "Obstacles",
            "PlayerHurtbox",
            "Projectile",
            "EnemyHurtbox",
            "PlayerProjectile"
        };

        private static readonly int[] PreferredLayerIndices =
        {
            7,
            8,
            9,
            10,
            11
        };

        [MenuItem(
            "Tools/Project Eri/Skill System V2/" +
            "Repair Required Project Layers")]
        public static void RepairRequiredConfiguration()
        {
            Object[] managerAssets =
                AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (managerAssets == null || managerAssets.Length == 0)
            {
                Debug.LogWarning(
                    "SkillSystemV2 could not load ProjectSettings/" +
                    "TagManager.asset to verify required layers.");
                return;
            }

            var tagManager = new SerializedObject(managerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null)
                return;

            bool tagManagerChanged = false;
            var resolvedIndices = new int[RequiredLayers.Length];
            for (int i = 0; i < RequiredLayers.Length; i++)
            {
                resolvedIndices[i] = ResolveOrCreateLayer(
                    layers,
                    RequiredLayers[i],
                    PreferredLayerIndices[i],
                    ref tagManagerChanged);
            }

            if (tagManagerChanged)
                tagManager.ApplyModifiedPropertiesWithoutUndo();

            int gameplayMask = 0;
            for (int i = 1; i < resolvedIndices.Length; i++)
            {
                if (resolvedIndices[i] >= 0)
                    gameplayMask |= 1 << resolvedIndices[i];
            }

            bool blackHoleChanged = RepairBlackHole(gameplayMask);
            if (tagManagerChanged || blackHoleChanged)
            {
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "SkillSystemV2 repaired required project layers and " +
                    "synchronized the authored Black Hole masks.");
            }
        }

        private static int ResolveOrCreateLayer(
            SerializedProperty layers,
            string layerName,
            int preferredIndex,
            ref bool changed)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return i;
            }

            int selectedIndex = -1;
            if (preferredIndex >= 0 &&
                preferredIndex < layers.arraySize &&
                string.IsNullOrEmpty(
                    layers.GetArrayElementAtIndex(preferredIndex).stringValue))
            {
                selectedIndex = preferredIndex;
            }
            else
            {
                for (int i = 8; i < layers.arraySize; i++)
                {
                    if (!string.IsNullOrEmpty(
                            layers.GetArrayElementAtIndex(i).stringValue))
                    {
                        continue;
                    }

                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                Debug.LogError(
                    $"SkillSystemV2 requires layer '{layerName}', but no " +
                    "user layer slot is available.");
                return -1;
            }

            layers.GetArrayElementAtIndex(selectedIndex).stringValue =
                layerName;
            changed = true;
            return selectedIndex;
        }

        private static bool RepairBlackHole(int gameplayMask)
        {
            SpellDefinition spell =
                AssetDatabase.LoadAssetAtPath<SpellDefinition>(BlackHolePath);
            if (spell == null || gameplayMask == 0)
                return false;

            var serialized = new SerializedObject(spell);
            bool changed = false;
            SerializedProperty filter =
                serialized.FindProperty("targetFilter");
            changed |= SetEnum(
                filter?.FindPropertyRelative("relationship"),
                (int)TargetRelationship.Any);
            changed |= SetBool(
                filter?.FindPropertyRelative("useLayerMask"),
                true);
            changed |= SetInt(
                filter?.FindPropertyRelative("allowedLayers"),
                gameplayMask);

            SerializedProperty deliverySlot =
                serialized.FindProperty("deliverySlot");
            SerializedProperty deliverySettings =
                deliverySlot?.FindPropertyRelative("settings");
            changed |= SetInt(
                deliverySettings?.FindPropertyRelative("hitMask"),
                gameplayMask);

            SerializedProperty effectSlots =
                serialized.FindProperty("effectSlots");
            if (effectSlots != null && effectSlots.arraySize > 0)
            {
                SerializedProperty force = effectSlots
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative("settings");
                changed |= SetBool(
                    force?.FindPropertyRelative("useSpatialCurve"),
                    true);
                changed |= SetBool(
                    force?.FindPropertyRelative(
                        "preserveCurveMomentumAfterExit"),
                    true);
                changed |= SetFloat(
                    force?.FindPropertyRelative("gravityExponent"),
                    2f);
            }

            if (!changed)
                return false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spell);
            return true;
        }

        private static bool SetInt(
            SerializedProperty property,
            int value)
        {
            if (property == null || property.intValue == value)
                return false;
            property.intValue = value;
            return true;
        }

        private static bool SetEnum(
            SerializedProperty property,
            int value)
        {
            if (property == null || property.enumValueIndex == value)
                return false;
            property.enumValueIndex = value;
            return true;
        }

        private static bool SetBool(
            SerializedProperty property,
            bool value)
        {
            if (property == null || property.boolValue == value)
                return false;
            property.boolValue = value;
            return true;
        }

        private static bool SetFloat(
            SerializedProperty property,
            float value)
        {
            if (property == null ||
                Mathf.Approximately(property.floatValue, value))
            {
                return false;
            }

            property.floatValue = value;
            return true;
        }
    }
}
