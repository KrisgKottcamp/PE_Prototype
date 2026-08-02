using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(EriHealingPointsUI))]
public sealed class EriHealingPointsUIEditor : Editor
{
    private const string RowName = "EriHealthRow";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField(
            "Canvas Setup",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Build once in Edit Mode. The resulting EriHealthRow and all " +
            "of its children remain in the Canvas hierarchy and can be " +
            "edited normally.",
            MessageType.Info
        );

        using (new EditorGUI.DisabledScope(
                   Application.isPlaying))
        {
            if (GUILayout.Button(
                    "Build Editable Eri Row",
                    GUILayout.Height(32f)))
            {
                BuildOrRepairRow();
            }
        }

        SerializedProperty rowRoot =
            serializedObject.FindProperty("rowRoot");

        if (rowRoot != null &&
            rowRoot.objectReferenceValue != null &&
            GUILayout.Button("Select Eri Row In Canvas"))
        {
            Selection.activeObject =
                rowRoot.objectReferenceValue;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void BuildOrRepairRow()
    {
        EriHealingPointsUI binder =
            (EriHealingPointsUI)target;

        RectTransform rowsContainer =
            ResolveRowsContainer(binder);

        if (rowsContainer == null)
        {
            EditorUtility.DisplayDialog(
                "RowsContainer Not Found",
                "Open Combat_Arena_Master and make sure the " +
                "PartyHealthHud prefab is present. You may also drag " +
                "PartyHealthHud/RowsContainer into the Rows Container " +
                "field manually.",
                "OK"
            );
            return;
        }

        RectTransform row =
            FindDirectChild(
                rowsContainer,
                RowName
            );

        if (row == null)
        {
            row = CreateUIObject(
                RowName,
                rowsContainer
            );

            row.sizeDelta =
                new Vector2(420f, 72f);

        }

        LayoutElement layout =
            GetOrAddComponent<LayoutElement>(
                row.gameObject
            );

        if (layout.preferredHeight < 0f)
        {
            layout.minHeight = 72f;
            layout.preferredHeight = 72f;
            layout.preferredWidth = 420f;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
        }

        CanvasGroup canvasGroup =
            GetOrAddComponent<CanvasGroup>(
                row.gameObject
            );

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Image portrait =
            BuildPortrait(row);

        TMP_Text nameText =
            BuildNameText(row);

        TMP_Text pointsText =
            BuildHealingPointsText(row);

        Image hpFill =
            BuildHealthBar(
                row,
                out TMP_Text hpText
            );

        row.SetAsLastSibling();

        serializedObject.Update();
        Assign("rowsContainer", rowsContainer);
        Assign("rowRoot", row.gameObject);
        Assign("portraitImage", portrait);
        Assign("nameText", nameText);
        Assign("hpText", hpText);
        Assign("healingPointsText", pointsText);
        Assign("hpFill", hpFill);
        Assign("rowCanvasGroup", canvasGroup);
        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(binder);
        PrefabUtility.
            RecordPrefabInstancePropertyModifications(
                binder
            );

        if (binder.gameObject.scene.IsValid() &&
            binder.gameObject.scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(
                binder.gameObject.scene
            );
        }

        Selection.activeGameObject = row.gameObject;
        EditorGUIUtility.PingObject(row.gameObject);
    }

    private RectTransform ResolveRowsContainer(
        EriHealingPointsUI binder)
    {
        SerializedProperty overrideProperty =
            serializedObject.FindProperty(
                "rowsContainer"
            );

        if (overrideProperty != null &&
            overrideProperty.objectReferenceValue != null)
        {
            return overrideProperty.objectReferenceValue
                as RectTransform;
        }

        PartyHealthHUD hud =
            binder.GetComponentInParent<PartyHealthHUD>();

        if (hud == null)
        {
            hud = Object.FindObjectOfType<
                PartyHealthHUD>(true);
        }

        if (hud == null)
            return null;

        RectTransform[] rects =
            hud.GetComponentsInChildren<
                RectTransform>(true);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];

            if (rect != null &&
                rect.name == "RowsContainer")
            {
                return rect;
            }
        }

        return null;
    }

    private static Image BuildPortrait(
        RectTransform row)
    {
        RectTransform rect =
            FindDirectChild(row, "EriPortrait");

        bool created = rect == null;

        if (created)
        {
            rect = CreateUIObject(
                "EriPortrait",
                row
            );

            rect.anchorMin =
                new Vector2(0f, 0.5f);
            rect.anchorMax =
                new Vector2(0f, 0.5f);
            rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.anchoredPosition =
                new Vector2(28f, 0f);
            rect.sizeDelta =
                new Vector2(52f, 52f);
        }

        Image image =
            GetOrAddComponent<Image>(
                rect.gameObject
            );

        image.raycastTarget = false;
        image.preserveAspect = true;

        if (created && image.sprite == null)
            image.color = Color.clear;

        return image;
    }

    private static TMP_Text BuildNameText(
        RectTransform row)
    {
        RectTransform rect =
            FindDirectChild(row, "EriNameText");

        bool applyDefaults = rect == null;

        if (rect == null)
        {
            rect = CreateUIObject(
                "EriNameText",
                row
            );

            SetTopLeftRect(
                rect,
                new Vector2(78f, -2f),
                new Vector2(180f, 30f)
            );
        }

        TextMeshProUGUI text =
            rect.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            text =
                Undo.AddComponent<TextMeshProUGUI>(
                    rect.gameObject
                );

            applyDefaults = true;
        }

        if (applyDefaults)
        {
            text.text = "Eri";
            text.fontSize = 30f;
            text.fontStyle = FontStyles.Bold;
            text.alignment =
                TextAlignmentOptions.MidlineLeft;
            text.color =
                new Color(0.19f, 0.19f, 0.19f, 1f);
        }

        text.raycastTarget = false;
        return text;
    }

    private static TMP_Text BuildHealingPointsText(
        RectTransform row)
    {
        RectTransform rect =
            FindDirectChild(
                row,
                "EriHealingPointsText"
            );

        bool applyDefaults = rect == null;

        if (rect == null)
        {
            rect = CreateUIObject(
                "EriHealingPointsText",
                row
            );

            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition =
                new Vector2(-8f, -3f);
            rect.sizeDelta =
                new Vector2(150f, 28f);
        }

        TextMeshProUGUI text =
            rect.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            text =
                Undo.AddComponent<TextMeshProUGUI>(
                    rect.gameObject
                );

            applyDefaults = true;
        }

        if (applyDefaults)
        {
            text.text = "Heals  8/8";
            text.fontSize = 22f;
            text.alignment =
                TextAlignmentOptions.MidlineRight;
            text.color =
                new Color(0.08f, 0.38f, 0.20f, 1f);
        }

        text.raycastTarget = false;
        return text;
    }

    private static Image BuildHealthBar(
        RectTransform row,
        out TMP_Text hpText)
    {
        RectTransform backgroundRect =
            FindDirectChild(
                row,
                "EriHPBarBackground"
            );

        bool backgroundCreated =
            backgroundRect == null;

        if (backgroundRect == null)
        {
            backgroundRect = CreateUIObject(
                "EriHPBarBackground",
                row
            );

            backgroundRect.anchorMin =
                new Vector2(0f, 0f);
            backgroundRect.anchorMax =
                new Vector2(1f, 0f);
            backgroundRect.pivot =
                new Vector2(0.5f, 0f);
            backgroundRect.anchoredPosition =
                new Vector2(39f, 2f);
            backgroundRect.sizeDelta =
                new Vector2(-86f, 25f);
        }

        Image background =
            GetOrAddComponent<Image>(
                backgroundRect.gameObject
            );

        if (backgroundCreated)
        {
            background.color =
                new Color(0.311f, 0f, 0.059f, 1f);
        }

        background.raycastTarget = false;

        RectTransform fillRect =
            FindDirectChild(backgroundRect, "Fill");

        bool fillCreated = fillRect == null;

        if (fillRect == null)
        {
            fillRect = CreateUIObject(
                "Fill",
                backgroundRect
            );

            StretchToParent(fillRect, 3f);
        }

        Image fill =
            GetOrAddComponent<Image>(
                fillRect.gameObject
            );

        if (fill.sprite == null)
        {
            fill.sprite =
                AssetDatabase.
                    GetBuiltinExtraResource<Sprite>(
                        "UI/Skin/UISprite.psd"
                    );
        }

        // Always repair these settings, including rows created by an older
        // version of the builder. Color and RectTransform edits are preserved.
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillClockwise = true;
        fill.preserveAspect = false;

        if (fillCreated)
        {
            fill.fillAmount = 1f;
            fill.color = Color.white;
        }

        fill.raycastTarget = false;

        RectTransform hpTextRect =
            FindDirectChild(
                backgroundRect,
                "HPText"
            );

        bool hpTextCreated = hpTextRect == null;

        if (hpTextRect == null)
        {
            hpTextRect = CreateUIObject(
                "HPText",
                backgroundRect
            );

            StretchToParent(hpTextRect, 0f);
        }

        TextMeshProUGUI text =
            hpTextRect.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            text =
                Undo.AddComponent<TextMeshProUGUI>(
                    hpTextRect.gameObject
                );

            hpTextCreated = true;
        }

        if (hpTextCreated)
        {
            text.text = "100 / 100";
            text.fontSize = 20f;
            text.fontStyle = FontStyles.Bold;
            text.alignment =
                TextAlignmentOptions.Center;
            text.color =
                new Color(0.19f, 0.04f, 0.06f, 1f);
        }

        text.raycastTarget = false;

        hpText = text;
        return fill;
    }

    private void Assign(
        string propertyName,
        Object value)
    {
        SerializedProperty property =
            serializedObject.FindProperty(
                propertyName
            );

        if (property != null)
            property.objectReferenceValue = value;
    }

    private static T GetOrAddComponent<T>(
        GameObject gameObject)
        where T : Component
    {
        T component = gameObject.GetComponent<T>();

        if (component == null)
            component = Undo.AddComponent<T>(gameObject);

        return component;
    }

    private static RectTransform CreateUIObject(
        string objectName,
        Transform parent)
    {
        GameObject created =
            new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer)
            );

        Undo.RegisterCreatedObjectUndo(
            created,
            $"Create {objectName}"
        );

        int uiLayer = LayerMask.NameToLayer("UI");
        created.layer = uiLayer >= 0 ? uiLayer : 5;

        RectTransform rect =
            created.GetComponent<RectTransform>();

        Undo.SetTransformParent(
            rect,
            parent,
            $"Parent {objectName}"
        );

        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform FindDirectChild(
        Transform parent,
        string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == childName)
                return child as RectTransform;
        }

        return null;
    }

    private static void SetTopLeftRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void StretchToParent(
        RectTransform rect,
        float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
