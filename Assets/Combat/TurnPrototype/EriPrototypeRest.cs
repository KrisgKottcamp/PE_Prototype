using UnityEngine;
using TMPro;

/// <summary>Explicit overworld rest control for repeated-encounter concept testing.</summary>
public sealed class EriPrototypeRest : MonoBehaviour
{
    private GameObject root;
    private void Start()
    {
        root = new GameObject("Prototype Rest Prompt", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform,false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler=root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);
        var text=new GameObject("Rest Help",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(root.transform,false);
        var rect=(RectTransform)text.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,0);rect.anchoredPosition=new Vector2(-24,24);rect.sizeDelta=new Vector2(650,44);
        var label=text.GetComponent<TextMeshProUGUI>();label.fontSize=22;label.alignment=TextAlignmentOptions.Right;label.raycastTarget=false;
        label.text="PROTOTYPE · F8: Rest — restore HP, MP and 3 potions";
    }
    private void Update()
    {
        bool canRest=CombatManager.Instance==null;
        if(root!=null)root.SetActive(canRest);
        if(canRest && Input.GetKeyDown(KeyCode.F8))
        {
            GetComponent<PartyManager>().RestPrototypeParty();
            GetComponent<EriSupportManager>()?.RestoreAtCheckpoint();
        }
    }
}
