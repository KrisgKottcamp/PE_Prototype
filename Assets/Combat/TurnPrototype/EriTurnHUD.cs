using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects combat to the saved UI scene; it never builds or restyles the hierarchy.</summary>
public sealed class EriTurnHUD : MonoBehaviour
{
    public EriCombatUIView View { get; private set; }
    private EriTurnCombat combat;
    private UnityEngine.UI.Graphic[] legacyGraphics;
    private float enemyScan;

    private IEnumerator Start()
    {
        combat=GetComponent<EriTurnCombat>();
        foreach(var oldAP in FindObjectsByType<PlayerAPBarUI>(FindObjectsSortMode.None))oldAP.enabled=false;
        var oldRoot=GameObject.Find("CombatHud");
        if(oldRoot!=null)
        {
            legacyGraphics=oldRoot.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            var oldCanvas=oldRoot.GetComponentInParent<Canvas>();
            if(oldCanvas!=null)EriFearIcon.ConfigureOverlay(oldCanvas,1001);
        }
        View=FindFirstObjectByType<EriCombatUIView>();
        if(View==null)
        {
            if(!Application.CanStreamedLevelBeLoaded(EriCombatUIView.ScenePath))
            {
                Debug.LogError("Eri UI scene is missing from Build Settings. Run Tools > Project Eri > UI > Create Editable UI.");
                yield break;
            }
            yield return SceneManager.LoadSceneAsync(EriCombatUIView.ScenePath,LoadSceneMode.Additive);
            View=FindFirstObjectByType<EriCombatUIView>();
        }
        if(View==null){Debug.LogError("EriCombatUI scene needs an EriCombatUIView component.");yield break;}
        View.Bind(combat);
        FindFirstObjectByType<CombatSkillMenuController>()?.ConfigurePrototypePresentation(View);
    }
    private void Update()
    {
        if(combat==null || Time.time<enemyScan)return;
        enemyScan=Time.time+1;
        var enemies=FindObjectsByType<EnemyHealth>(FindObjectsSortMode.InstanceID);
        for(int i=0;i<enemies.Length;i++)
        {
            if(enemies[i].GetComponent<EriFearMark>()!=null)continue;
            var affinity=enemies[i].gameObject.AddComponent<EriFearMark>();
            affinity.NaturalMultiplier=i%3==0?1.25f:i%3==1?0.75f:1f;
            affinity.AffinityLabel=i%3==0?"Fear WEAK (test)":i%3==1?"Fear RESIST (test)":"Fear neutral (test)";
        }
    }
    private void LateUpdate()
    {
        if(legacyGraphics==null)return;
        foreach(var graphic in legacyGraphics)
        {
            if(graphic==null)continue;
            bool keep=false;
            for(var t=graphic.transform;t!=null;t=t.parent)
                if(t.name=="PartyTargetMenu" || t.name=="CombatResultsPanel"){keep=true;break;}
            if(!keep)graphic.enabled=false;
        }
    }
    // The opaque halo is the immediate text background, independent of arena colors.
    public static readonly Color32 TextHalo = new Color32(10,13,20,255);
    public static void ConfigureReadableText(TextMeshProUGUI text)
    {
        text.fontStyle=FontStyles.Bold;
        text.outlineWidth=0.25f;
        text.outlineColor=TextHalo;
        var material=text.fontMaterial;
        material.SetColor("_FaceColor",Color.white);
        material.SetFloat("_OutlineSoftness",0);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor",TextHalo);
        material.SetFloat("_UnderlayOffsetX",0);
        material.SetFloat("_UnderlayOffsetY",0);
        material.SetFloat("_UnderlayDilate",1);
        material.SetFloat("_UnderlaySoftness",0);
        text.extraPadding=true;
        text.UpdateMeshPadding();
    }
}
