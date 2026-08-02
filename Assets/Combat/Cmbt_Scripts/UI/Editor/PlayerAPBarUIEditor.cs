using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(PlayerAPBarUI))]
public sealed class PlayerAPBarUIEditor : Editor
{
    private const string CostTextName =
        "RotationCostMultiplierText";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField(
            "Optional Cost Readout Setup",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "The white sweat cue requires no Canvas setup. This button only " +
            "creates the optional multiplier label as an editable UI object.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button(
                    "Build Editable Cost Readout UI",
                    GUILayout.Height(34f)))
            {
                BuildOrRepairCostUI();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void BuildOrRepairCostUI()
    {
        PlayerAPBarUI controller = (PlayerAPBarUI)target;
        SerializedProperty apFillProperty =
            serializedObject.FindProperty("apFill");
        Image apFill =
            apFillProperty?.objectReferenceValue as Image;

        if (apFill == null)
        {
            EditorUtility.DisplayDialog(
                "AP Fill Missing",
                "Assign the existing AP_Fill Image to PlayerAPBarUI first.",
                "OK");
            return;
        }

        RectTransform apFillRect = apFill.rectTransform;
        RectTransform hudParent = apFillRect.parent as RectTransform;

        if (hudParent == null ||
            apFill.GetComponentInParent<Canvas>() == null)
        {
            EditorUtility.DisplayDialog(
                "Combat Canvas Missing",
                "The AP fill must be inside the CombatHud Canvas.",
                "OK");
            return;
        }

        TMP_Text costText = BuildCostText(hudParent, apFillRect);

        serializedObject.Update();
        SerializedProperty costProperty =
            serializedObject.FindProperty("costMultiplierText");

        if (costProperty != null)
            costProperty.objectReferenceValue = costText;

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);

        if (controller.gameObject.scene.IsValid() &&
            controller.gameObject.scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        Selection.activeGameObject = costText.gameObject;
        EditorGUIUtility.PingObject(costText.gameObject);
    }

    private static TMP_Text BuildCostText(
        RectTransform parent,
        RectTransform apFillRect)
    {
        RectTransform rect = FindDirectChild(parent, CostTextName);
        bool created = rect == null;

        if (created)
        {
            rect = CreateUIObject(CostTextName, parent);
            rect.anchorMin = apFillRect.anchorMin;
            rect.anchorMax = apFillRect.anchorMax;
            rect.pivot = apFillRect.pivot;
            rect.anchoredPosition =
                apFillRect.anchoredPosition + new Vector2(385f, 0f);
            rect.sizeDelta = new Vector2(220f, 44f);
        }

        TextMeshProUGUI text =
            rect.GetComponent<TextMeshProUGUI>();

        if (text == null)
            text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);

        if (created)
        {
            text.text = "COST  x1.56";
            text.fontSize = 25f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.68f, 0.24f, 1f);
        }

        text.raycastTarget = false;
        return text;
    }

    private static RectTransform FindDirectChild(
        Transform parent,
        string objectName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == objectName)
                return child as RectTransform;
        }

        return null;
    }

    private static RectTransform CreateUIObject(
        string objectName,
        Transform parent)
    {
        GameObject created = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer));

        Undo.RegisterCreatedObjectUndo(created, $"Create {objectName}");
        int uiLayer = LayerMask.NameToLayer("UI");
        created.layer = uiLayer >= 0 ? uiLayer : 5;

        RectTransform rect = created.GetComponent<RectTransform>();
        Undo.SetTransformParent(rect, parent, $"Parent {objectName}");
        rect.localScale = Vector3.one;
        return rect;
    }
}
