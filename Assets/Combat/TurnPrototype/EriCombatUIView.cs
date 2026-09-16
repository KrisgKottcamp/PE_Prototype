using System;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>Saved scene presentation. Runtime changes data/state only, never authored layout or materials.</summary>
public sealed class EriCombatUIView : MonoBehaviour
{
    public const string ScenePath = "Assets/Combat/TurnPrototype/UI/EriCombatUI.unity";
    [Header("Panels — edit their Images and RectTransforms directly")]
    public GameObject CommandPanel;
    public GameObject TimingPanel;
    public RectTransform EnemyMarkTemplate;
    [Header("Readiness and AP")]
    public TextMeshProUGUI Status;
    public UnityEngine.UI.Image Readiness;
    public APSegment[] Segments;
    [Header("Party and Eri")]
    public TextMeshProUGUI PartyHeader;
    public PartyRow[] Members;
    public TextMeshProUGUI EriName;
    public TextMeshProUGUI EriDetails;
    public UnityEngine.UI.Image EriHP;
    public UnityEngine.UI.Button Potion;
    [Header("Commands — each row is a separate editable object")]
    public CommandRow[] Commands;
    public TextMeshProUGUI Description;
    public TextMeshProUGUI UnavailableReason;
    [Header("Timing")]
    public RectTransform TimingTrack;
    public RectTransform TimingCursor;
    [Header("State colors — used only while playing")]
    public Color ChargedAP = new Color(0.35f,0.95f,0.52f,1);
    public Color ExhaustedAP = new Color(0.15f,0.12f,0.16f,0.2f);
    public Color ActiveMember = new Color(0.48f,0.95f,0.82f);
    public Color LowHP = new Color(1,0.38f,0.32f);
    [Range(0,1)] public float LowHPThreshold = 0.25f;
    public Color SelectedCommand = new Color(0.48f,0.95f,0.82f);
    public Color UnavailableCommand = new Color(0.76f,0.80f,0.86f);
    [Tooltip("Runtime tint for segment-cost orbs while their command is unavailable.")]
    public Color UnavailableSegmentOrb = new Color(0.40f,0.44f,0.48f,0.8f);
    [Header("Enemy status placement")]
    [Tooltip("Offset above the enemy collider in world units. Size/style come from Enemy Mark Template.")]
    public Vector2 EnemyMarkOffset = new Vector2(0,0.25f);
    [Header("Dynamic text formats")]
    public string ReadyFormat = "{0} · Ready";
    public string RecoveringFormat = "{0} · {1:0.0}s";
    public string PartyHeaderFormat = "Party · Potions {0}";
    public string MemberDetailsFormat = "MP {0} · {1}/4";
    public string EriDetailsFormat = "Heals {0}/{1}";
    public string MPCostFormat = "{0} MP";
    public string RecoverCost = "Restore 1 segment";
    public string TurnEndingFormat = "{0} · Passing turn";
    public string WaitingMemberFormat = "MP {0} · Waiting";
    public string FullRecoverCost = "Restore capacity";

    [Serializable] public sealed class APSegment
    {
        public UnityEngine.UI.Image Track;
        public UnityEngine.UI.Image Fill;
        public GameObject ExhaustedSymbol;
        [NonSerialized] public Color TrackColor, ChargingColor;
    }
    [Serializable] public sealed class PartyRow
    {
        public GameObject Root;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Details;
        public GameObject ActiveIndicator;
        public UnityEngine.UI.Image HP;
        public UnityEngine.UI.Image MP;
        [NonSerialized] public Color NameColor, HPColor;
    }
    [Serializable] public sealed class CommandRow
    {
        public GameObject Root;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Cost;
        public GameObject Selection;
        public GameObject MarkBadge;
        public GameObject DamageBadge;
        [Tooltip("One editable circle for each possible AP segment cost.")]
        public UnityEngine.UI.Image[] SegmentOrbs;
        [NonSerialized] public Color NameColor, CostColor;
        [NonSerialized] public Color[] SegmentOrbColors;
    }

    private EriTurnCombat combat;
    private float cursorY;
    private void Awake()
    {
        foreach(var segment in Segments){segment.TrackColor=segment.Track.color;segment.ChargingColor=segment.Fill.color;}
        foreach(var row in Members){row.NameColor=row.Name.color;row.HPColor=row.HP.color;}
        foreach(var row in Commands)
        {
            row.NameColor=row.Name.color;row.CostColor=row.Cost.color;
            row.SegmentOrbColors=row.SegmentOrbs==null?Array.Empty<Color>():row.SegmentOrbs.Select(orb=>orb!=null?orb.color:Color.white).ToArray();
        }
        cursorY=TimingCursor.anchoredPosition.y;
        CommandPanel.SetActive(false);TimingPanel.SetActive(false);EnemyMarkTemplate.gameObject.SetActive(false);
        Potion.onClick.AddListener(UsePotion);
    }
    public void Bind(EriTurnCombat value){combat=value;}
    private void UsePotion(){if(combat!=null)combat.UsePotion();}
    private static void Fill(UnityEngine.UI.Image image,float amount)
    {
        // Filled Image preserves its authored rectangle, anchors, sprite, and appearance.
        image.fillAmount=Mathf.Clamp01(amount);
    }
    private void Update()
    {
        if(combat==null || combat.Member==null)return;
        var m=combat.Member;
        Status.text=combat.Remaining>0?string.Format(RecoveringFormat,m.def.displayName,combat.Remaining):string.Format(ReadyFormat,m.def.displayName);
        if(combat.TurnEnding)Status.text=string.Format(TurnEndingFormat,m.def.displayName);
        Fill(Readiness,1-combat.Remaining/EriTurnRules.RecoverySeconds);
        for(int i=0;i<Segments.Length;i++)
        {
            var s=Segments[i];bool spent=i>=4-m.exhaustedSegments;
            int boundary=EriTurnRules.Cost(m.def.maxAP,i,m.exhaustedSegments);
            int width=EriTurnRules.Cost(m.def.maxAP,1,m.exhaustedSegments+i);
            float charge=Mathf.Clamp01((m.currentAP-boundary)/(float)Mathf.Max(1,width));
            s.Track.color=spent?ExhaustedAP:s.TrackColor;
            Fill(s.Fill,spent?0:charge);s.Fill.color=!spent && charge>=1?ChargedAP:s.ChargingColor;
            s.ExhaustedSymbol.SetActive(spent);
        }
        PartyHeader.text=string.Format(PartyHeaderFormat,combat.Party.mpPotions);
        for(int i=0;i<Members.Length;i++)
        {
            var row=Members[i];bool exists=i<combat.Party.party.Count;row.Root.SetActive(exists);if(!exists)continue;
            var member=combat.Party.party[i];bool active=member==m;
            row.Name.text=member.def.displayName;row.Name.color=active?ActiveMember:row.NameColor;
            row.Details.text=string.Format(MemberDetailsFormat,member.currentMP,4-member.exhaustedSegments);
            if(combat.IsWaiting(i))row.Details.text=string.Format(WaitingMemberFormat,member.currentMP);
            row.ActiveIndicator.SetActive(active);
            float hp=member.currentHP/(float)Mathf.Max(1,member.def.maxHP);
            Fill(row.HP,hp);row.HP.color=hp<=LowHPThreshold?LowHP:row.HPColor;
            Fill(row.MP,member.currentMP/(float)EriTurnRules.MaxMP);
        }
        var support=EriSupportManager.Instance;
        EriDetails.text=support!=null?string.Format(EriDetailsFormat,support.CurrentHealingPoints,support.UnlockedCapacity):"";
        Fill(EriHP,support!=null?support.EriCurrentHP/(float)Mathf.Max(1,support.EriMaximumHP):0);
        Potion.interactable=!combat.IsBusy && Time.timeScale>0 && combat.Remaining<=0 && combat.Party.mpPotions>0 && m.currentMP<EriTurnRules.MaxMP;
        TimingPanel.SetActive(combat.Timing);
        if(combat.Timing)
            TimingCursor.anchoredPosition=new Vector2(TimingTrack.rect.width*combat.TimingProgress,cursorY);
    }
    public void ShowCommand(int index,string name,string cost,int segmentCost,bool selected,bool usable,EriCommandKind? kind)
    {
        if(index<0 || index>=Commands.Length)return;
        var row=Commands[index];row.Root.SetActive(true);row.Name.text=name;row.Cost.text=cost;
        row.Selection.SetActive(selected);
        row.Name.color=!usable?UnavailableCommand:selected?SelectedCommand:row.NameColor;
        row.Cost.color=!usable?UnavailableCommand:selected?SelectedCommand:row.CostColor;
        if(row.SegmentOrbs!=null)for(int i=0;i<row.SegmentOrbs.Length;i++)
        {
            var orb=row.SegmentOrbs[i];if(orb==null)continue;
            orb.gameObject.SetActive(i<Mathf.Clamp(segmentCost,0,row.SegmentOrbs.Length));
            if(orb.gameObject.activeSelf)
                orb.color=!usable?UnavailableSegmentOrb:selected?SelectedCommand:
                    i<row.SegmentOrbColors.Length?row.SegmentOrbColors[i]:Color.white;
        }
        row.MarkBadge.SetActive(kind==EriCommandKind.Mark);
        row.DamageBadge.SetActive(kind==EriCommandKind.Pierce || kind==EriCommandKind.Shot || kind==EriCommandKind.Burst);
    }
    public void FinishCommands(int count,string detail,string reason)
    {
        for(int i=count;i<Commands.Length;i++)Commands[i].Root.SetActive(false);
        Description.text=detail;UnavailableReason.text=reason;
    }
    public RectTransform CreateEnemyMark()
    {
        var instance=Instantiate(EnemyMarkTemplate,EnemyMarkTemplate.parent);
        instance.name="Fear Weakness";instance.gameObject.SetActive(true);return instance;
    }
}
