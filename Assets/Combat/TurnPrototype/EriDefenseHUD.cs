using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Thin screen-space enemy bars. The saved template controls appearance, not combat code.</summary>
public sealed class EriDefenseHUD : MonoBehaviour
{
    public static EriDefenseHUD Active { get; private set; }
    public RectTransform EnemyBarTemplate;
    [Tooltip("Above the enemy collider, in world units.")]
    public Vector2 WorldOffset = new Vector2(0, 0.25f);
    private readonly Dictionary<EriEnemyDefenses, Readout> readouts = new Dictionary<EriEnemyDefenses, Readout>();
    private readonly List<EriEnemyDefenses> removed = new List<EriEnemyDefenses>();
    private Camera worldCamera;
    private float scanAt;
    private sealed class Readout
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Health, Shield, Armor;
        public GameObject ShieldRow, ArmorRow;
        public GameObject FearResistanceIcons;
        public TextMeshProUGUI Status;
        public Collider2D Body;
    }
    private void Awake(){Active=this;}
    private void Start(){EnsureTemplate();}
    private void OnDestroy(){if(Active==this)Active=null;}

    /// <summary>Used by authoring and runtime fallback. Never rebuilds an existing template.</summary>
    public void EnsureTemplate()
    {
        if(EnemyBarTemplate!=null){HideOldRecoveryBar();EnsureResistanceIcons();return;}
        var view=GetComponent<EriCombatUIView>();
        EnemyBarTemplate=Rect(transform,"Enemy Defense Bar Template",new Vector2(.5f,.5f),new Vector2(.5f,0),new Vector2(0,150),new Vector2(92,44));
        AddBar(EnemyBarTemplate,"Health",0,new Color32(244,76,80,255));
        AddBar(EnemyBarTemplate,"Shield",-7,new Color32(76,167,255,255));
        AddBar(EnemyBarTemplate,"Armor",-14,new Color32(192,200,211,255));
        var label=MakeText(EnemyBarTemplate,"Status",view!=null?view.Status:null,new Vector2(0,-28),new Vector2(120,17),11);
        label.alignment=TextAlignmentOptions.Center;label.rectTransform.anchoredPosition=new Vector2(-14,-28);
        label.text="";
        EnsureResistanceIcons();
        EnemyBarTemplate.gameObject.SetActive(false);
    }
    private void HideOldRecoveryBar()
    {
        var old=EnemyBarTemplate.Find("Recovery");
        if(old!=null)old.gameObject.SetActive(false);
        var label=EnemyBarTemplate.Find("Status")?.GetComponent<TextMeshProUGUI>();
        if(label!=null && label.text.StartsWith("DOWN"))label.text="";
    }
    private void EnsureResistanceIcons()
    {
        if(EnemyBarTemplate==null || EnemyBarTemplate.Find("Fear Resistance Icons")!=null)return;
        // The two icons sit just left of the bars, away from the DOWN label
        // and the existing weakness mark above the enemy.
        var pair=Rect(EnemyBarTemplate,"Fear Resistance Icons",new Vector2(0,1),new Vector2(0,1),new Vector2(-39,-26),new Vector2(34,16));
        var eyeRect=Rect(pair,"Fear Eye",new Vector2(0,1),new Vector2(0,1),Vector2.zero,new Vector2(18,16));
        var eye=eyeRect.gameObject.AddComponent<EriFearIcon>();
        eye.Meaning=EriFearIcon.Cue.Fear;eye.raycastTarget=false;
        var shieldRect=Rect(pair,"Shield",new Vector2(0,1),new Vector2(0,1),new Vector2(19,0),new Vector2(14,16));
        var shield=shieldRect.gameObject.AddComponent<EriShieldIcon>();
        shield.raycastTarget=false;
        pair.gameObject.SetActive(false);
    }
    private void LateUpdate()
    {
        if(EnemyBarTemplate==null)return;
        if(worldCamera==null)worldCamera=Camera.main;
        if(worldCamera==null)return;
        if(Time.unscaledTime>=scanAt)
        {
            scanAt=Time.unscaledTime+.25f;
            foreach(var enemy in FindObjectsByType<EriEnemyDefenses>(FindObjectsSortMode.None))
            {
                if(readouts.ContainsKey(enemy))continue;
                var root=Instantiate(EnemyBarTemplate,EnemyBarTemplate.parent);root.name=enemy.name+" Defense Bars";
                var r=new Readout{Root=root,Body=enemy.GetComponent<Collider2D>()};
                r.Health=root.Find("Health/Fill").GetComponent<UnityEngine.UI.Image>();
                r.Shield=root.Find("Shield/Fill").GetComponent<UnityEngine.UI.Image>();
                r.Armor=root.Find("Armor/Fill").GetComponent<UnityEngine.UI.Image>();
                var oldRecovery=root.Find("Recovery");if(oldRecovery!=null)oldRecovery.gameObject.SetActive(false);
                r.FearResistanceIcons=root.Find("Fear Resistance Icons").gameObject;
                r.ShieldRow=r.Shield.transform.parent.gameObject;r.ArmorRow=r.Armor.transform.parent.gameObject;
                r.Status=root.Find("Status").GetComponent<TextMeshProUGUI>();readouts.Add(enemy,r);
            }
        }
        removed.Clear();
        foreach(var pair in readouts)
        {
            var enemy=pair.Key;var r=pair.Value;
            if(enemy==null){Destroy(r.Root.gameObject);removed.Add(enemy);continue;}
            var hp=enemy.Health;
            Vector3 world=r.Body!=null?new Vector3(r.Body.bounds.center.x,r.Body.bounds.max.y,enemy.transform.position.z):enemy.transform.position+Vector3.up*.9f;
            var screen=worldCamera.WorldToScreenPoint(world+(Vector3)WorldOffset);
            r.Root.gameObject.SetActive(hp!=null && hp.CurrentHP>0 && enemy.gameObject.activeInHierarchy && screen.z>0);
            if(!r.Root.gameObject.activeSelf)continue;
            var canvas=r.Root.GetComponentInParent<Canvas>();
            if(RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)r.Root.parent,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var position))r.Root.position=position;
            Amount(r.Health,hp.CurrentHP/(float)Mathf.Max(1,hp.MaxHP));
            r.ShieldRow.SetActive(enemy.MaxShield>0);r.ArmorRow.SetActive(enemy.MaxArmor>0);
            Amount(r.Shield,enemy.Shield/(float)Mathf.Max(1,enemy.MaxShield));
            Amount(r.Armor,enemy.Armor/(float)Mathf.Max(1,enemy.MaxArmor));
            r.FearResistanceIcons.SetActive(enemy.TemporaryFearResistance<0.999f);
            r.Status.text=!enemy.IsKnockedDown && enemy.OffBalanceRemaining>0?"! Off-balance":"";
        }
        foreach(var enemy in removed)readouts.Remove(enemy);
    }
    private static void Amount(UnityEngine.UI.Image image,float amount)
    {
        // Anchors work without requiring a generated sprite asset.
        image.rectTransform.localScale=new Vector3(Mathf.Clamp01(amount),1,1);
    }
    private static void AddBar(Transform parent,string name,float y,Color color)
    {
        var track=Rect(parent,name,new Vector2(0,1),new Vector2(0,1),new Vector2(0,y),new Vector2(92,5));
        var bg=track.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.025f,.035f,.055f,.85f);bg.raycastTarget=false;
        var fill=Rect(track,"Fill",Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
        fill.anchorMax=Vector2.one;fill.offsetMin=new Vector2(1,1);fill.offsetMax=new Vector2(-1,-1);
        var image=fill.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;
    }
    public static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=position;r.sizeDelta=size;return r;
    }
    public static TextMeshProUGUI MakeText(Transform parent,string name,TextMeshProUGUI style,Vector2 position,Vector2 size,float fontSize)
    {
        var r=Rect(parent,name,new Vector2(0,1),new Vector2(0,1),position,size);
        var text=r.gameObject.AddComponent<TextMeshProUGUI>();
        if(style!=null){text.font=style.font;text.fontSharedMaterial=style.fontSharedMaterial;}
        text.fontSize=fontSize;text.fontStyle=FontStyles.Bold;text.color=Color.white;text.raycastTarget=false;
        text.textWrappingMode=TextWrappingModes.NoWrap;text.extraPadding=true;return text;
    }
}
