using TMPro;
using UnityEngine;

/// <summary>Compact overlay; no saved scene or prefab changes.</summary>
public sealed class EriTurnHUD : MonoBehaviour
{
    private EriTurnCombat combat;
    private GameObject root;
    private TextMeshProUGUI status, roster, help, timing;
    private UnityEngine.UI.Image readiness;
    private readonly UnityEngine.UI.Image[] segments = new UnityEngine.UI.Image[4];
    private readonly TextMeshProUGUI[] segmentLabels = new TextMeshProUGUI[4];
    private UnityEngine.UI.Button potion;
    private TextMeshProUGUI[] memberLabels;
    private UnityEngine.UI.Image[] mpBars;
    private TextMeshProUGUI eriStatus;
    private UnityEngine.UI.Graphic[] legacyGraphics;
    private float enemyScan;
    private void Start()
    {
        combat = GetComponent<EriTurnCombat>();
        root = new GameObject("Eri Turn Prototype HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        var panel = Box(root.transform, "Command Readiness", new Vector2(0.5f, 1), new Vector2(0,-24), new Vector2(690,205), new Color(0.055f,0.065f,0.105f,0.96f));
        status = Text(panel.transform, "Status", new Vector2(16,-12), new Vector2(658,32), 23);
        var track = Box(panel.transform, "Readiness Track", new Vector2(0,1), new Vector2(16,-53), new Vector2(658,8), new Color(0.18f,0.2f,0.25f));
        readiness = Box(track.transform, "Ready Fill", new Vector2(0,1), Vector2.zero, new Vector2(658,8), new Color(0.45f,0.88f,0.77f)).GetComponent<UnityEngine.UI.Image>();
        for (int i=0;i<4;i++)
        {
            var chamber = Box(panel.transform, "AP Segment " + (i+1), new Vector2(0,1), new Vector2(16+i*166,-76), new Vector2(158,36), Color.gray);
            segments[i] = chamber.GetComponent<UnityEngine.UI.Image>();
            segmentLabels[i] = Text(chamber.transform,"State",new Vector2(6,-3),new Vector2(146,30),18);
        }
        help = Text(panel.transform,"Help",new Vector2(16,-125),new Vector2(658,68),18);
        int count=combat.Party.party.Count;
        var partyPanel = Box(root.transform,"Party Resources",new Vector2(1,1),new Vector2(-22,-24),new Vector2(350,160+count*62),new Color(0.055f,0.065f,0.105f,0.98f));
        roster = Text(partyPanel.transform,"Roster",new Vector2(14,-12),new Vector2(322,35),18);
        memberLabels=new TextMeshProUGUI[count];mpBars=new UnityEngine.UI.Image[count];
        for(int i=0;i<count;i++)
        {
            memberLabels[i]=Text(partyPanel.transform,"Member "+i,new Vector2(14,-50-i*62),new Vector2(322,42),17);
            var mpTrack=Box(partyPanel.transform,"MP Track "+i,new Vector2(0,1),new Vector2(14,-95-i*62),new Vector2(322,6),new Color(0.18f,0.18f,0.25f));
            mpBars[i]=Box(mpTrack.transform,"MP",new Vector2(0,1),Vector2.zero,new Vector2(322,6),new Color(0.62f,0.48f,0.92f)).GetComponent<UnityEngine.UI.Image>();
        }
        eriStatus=Text(partyPanel.transform,"Eri",new Vector2(14,-53-count*62),new Vector2(322,32),17);
        var button = Box(partyPanel.transform,"MP Potion",new Vector2(0,1),new Vector2(14,-100-count*62),new Vector2(322,38),new Color(0.2f,0.27f,0.37f));
        potion = button.AddComponent<UnityEngine.UI.Button>(); potion.targetGraphic=button.GetComponent<UnityEngine.UI.Image>(); potion.targetGraphic.raycastTarget=true;
        potion.onClick.AddListener(combat.UsePotion);
        Text(button.transform,"Label",new Vector2(10,-4),new Vector2(302,30),18).text="MP potion +30 · uses command";
        var timingPanel = Box(root.transform,"Timed Command",new Vector2(0.5f,0.5f),Vector2.zero,new Vector2(660,130),new Color(0.06f,0.07f,0.13f,0.98f));
        timing = Text(timingPanel.transform,"Timing",new Vector2(20,-14),new Vector2(620,110),24);
        timingPanel.SetActive(false);
        FindFirstObjectByType<CombatSkillMenuController>()?.ConfigurePrototypePresentation(root.transform);
        foreach(var oldAP in FindObjectsByType<PlayerAPBarUI>(FindObjectsSortMode.None)) oldAP.enabled=false;
        var oldRoot=GameObject.Find("CombatHud");
        if(oldRoot!=null)legacyGraphics=oldRoot.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
    }
    private void Update()
    {
        if (combat == null || combat.Member == null || status == null) return;
        var m = combat.Member;
        status.text = combat.Remaining > 0 ? $"COMMAND IN {combat.Remaining:0.0}s  ·  {m.def.displayName}" : $"COMMAND READY  ·  {m.def.displayName}";
        readiness.rectTransform.sizeDelta = new Vector2(658*(1-combat.Remaining/EriTurnRules.RecoverySeconds),8);
        for(int i=0;i<4;i++)
        {
            bool spent = i >= 4-m.exhaustedSegments;
            int boundary=EriTurnRules.Cost(m.def.maxAP,i,m.exhaustedSegments);
            int width=EriTurnRules.Cost(m.def.maxAP,1,m.exhaustedSegments+i);
            float charge = Mathf.Clamp01((m.currentAP-boundary)/(float)Mathf.Max(1,width));
            segments[i].color = spent ? new Color(0.2f,0.2f,0.24f) : Color.Lerp(new Color(0.15f,0.2f,0.29f),new Color(0.15f,0.55f,0.65f),charge);
            segmentLabels[i].text = spent ? "× EXHAUSTED" : charge>=1 ? "CHARGED" : charge>0 ? $"CHARGING {charge:P0}" : "EMPTY";
        }
        help.text = "Tab: skills · C: switch · basic hits: charge AP\n" + (m.exhaustedSegments==4 ? "Capacity spent. Switch, or use Recover in the skill menu." : combat.Message);
        roster.text=$"PARTY  ·  {combat.Party.mpPotions} MP POTIONS";
        for(int i=0;i<memberLabels.Length;i++)
        {
            var member=combat.Party.party[i];
            memberLabels[i].text=$"{(member==m?"> ":"")}{member.def.displayName}  ·  {member.currentHP}/{member.def.maxHP} HP\nMP {member.currentMP}/100  ·  Capacity {4-member.exhaustedSegments}/4";
            memberLabels[i].color=member==m?new Color(0.48f,0.95f,0.82f):Color.white;
            mpBars[i].rectTransform.sizeDelta=new Vector2(322*member.currentMP/100f,6);
        }
        var support=EriSupportManager.Instance;
        eriStatus.text=support!=null?$"Eri · {support.EriCurrentHP} HP · Heals {support.CurrentHealingPoints}/{support.UnlockedCapacity}":"MP carries between fights. Rest to refill.";
        potion.interactable = !combat.IsBusy && Time.timeScale>0 && combat.Remaining<=0 && combat.Party.mpPotions>0 && m.currentMP<100;
        timing.transform.parent.gameObject.SetActive(combat.Timing);
        if(combat.Timing)
        {
            int pos=Mathf.RoundToInt(combat.TimingProgress*20);
            string bar=""; for(int i=0;i<=20;i++) bar+=i==pos?"│":i>=12&&i<=16?"■":"─";
            timing.text="PRESS SPACE IN THE BRIGHT ZONE\n<color=#73E0C5>"+bar+"</color>\n<size=18>Good +5 AP · Perfect +10 AP · Esc cancels free</size>";
        }
        if(Time.time>=enemyScan)
        {
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
    private static GameObject Box(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image)); go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform; r.anchorMin=r.anchorMax=anchor;r.pivot=anchor;r.anchoredPosition=position;r.sizeDelta=size;
        var img=go.GetComponent<UnityEngine.UI.Image>();img.color=color;img.raycastTarget=false;return go;
    }
    private static TextMeshProUGUI Text(Transform parent,string name,Vector2 position,Vector2 size,int fontSize)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=position;r.sizeDelta=size;
        var t=go.GetComponent<TextMeshProUGUI>();t.fontSize=fontSize;t.color=new Color(0.94f,0.96f,1);t.raycastTarget=false;return t;
    }
    private void OnDestroy(){if(root!=null)Destroy(root);}
}
