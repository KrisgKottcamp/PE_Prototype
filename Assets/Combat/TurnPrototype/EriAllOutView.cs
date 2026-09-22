using TMPro;
using UnityEngine;

/// <summary>Explicit click-only offer; never takes the menu or mini-game confirm key.</summary>
public sealed class EriAllOutView : MonoBehaviour
{
    public RectTransform Offer;
    public UnityEngine.UI.Button Confirm;
    public TextMeshProUGUI OfferLabel;
    public TextMeshProUGUI Rewards;
    public RectTransform Cinematic;
    public UnityEngine.UI.Image CinematicBand;
    public UnityEngine.UI.Image CinematicPortrait;
    public UnityEngine.UI.Image CinematicFlash;
    public UnityEngine.UI.Image CinematicInvert;
    public Shader InvertShader;
    public TextMeshProUGUI CinematicTitle;
    public const float SwipeStart = .38f;
    private Material invertMaterial;
    private Color introBandColor, introPortraitColor;
    private float introTitleAlpha;
    private EriCombatUIView view;
    private EriTurnCombat combat;
    private void Start()
    {
        EnsurePresentation();
        Confirm.onClick.AddListener(ConfirmOffer);
        Confirm.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
        Offer.gameObject.SetActive(false);
    }
    private void OnDestroy()
    {
        if(Confirm!=null)Confirm.onClick.RemoveListener(ConfirmOffer);
        if(invertMaterial!=null)Destroy(invertMaterial);
    }
    public void EnsurePresentation()
    {
        view=GetComponent<EriCombatUIView>();
        var style=view!=null?view.Status:null;
        if(Offer==null)
        {
            Offer=EriDefenseHUD.Rect(transform,"Eri All-Out Offer",new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-40),new Vector2(292,36));
            var bg=Offer.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.035f,.045f,.075f,.48f);bg.raycastTarget=true;
            Confirm=Offer.gameObject.AddComponent<UnityEngine.UI.Button>();Confirm.targetGraphic=bg;
            OfferLabel=EriDefenseHUD.MakeText(Offer,"Offer Label",style,new Vector2(8,-5),new Vector2(276,26),17);
            OfferLabel.alignment=TextAlignmentOptions.Center;OfferLabel.text="Eri · All-out attack";
        }
        if(Rewards==null)
        {
            Rewards=EriDefenseHUD.MakeText(transform,"Break Rewards",style,Vector2.zero,new Vector2(500,22),14);
            var r=Rewards.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,0);r.pivot=new Vector2(.5f,0);r.anchoredPosition=new Vector2(0,90);
            Rewards.alignment=TextAlignmentOptions.Center;Rewards.text="";
        }
        if(Cinematic==null)
        {
            Cinematic=EriDefenseHUD.Rect(transform,"Eri All-Out Cinematic",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            Cinematic.anchorMin=Vector2.zero;Cinematic.anchorMax=Vector2.one;
            Cinematic.offsetMin=Cinematic.offsetMax=Vector2.zero;
            CinematicBand=EriDefenseHUD.Rect(Cinematic,"HM Sweep Band",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(0,176)).gameObject.AddComponent<UnityEngine.UI.Image>();
            CinematicBand.rectTransform.anchorMin=new Vector2(0,.5f);CinematicBand.rectTransform.anchorMax=new Vector2(1,.5f);
            CinematicBand.rectTransform.offsetMin=new Vector2(0,-88);CinematicBand.rectTransform.offsetMax=new Vector2(0,88);
            CinematicBand.color=new Color(.015f,.025f,.045f,.88f);CinematicBand.raycastTarget=false;
            CinematicPortrait=EriDefenseHUD.Rect(Cinematic,"Eri Portrait",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(130,130)).gameObject.AddComponent<UnityEngine.UI.Image>();
            CinematicPortrait.preserveAspect=true;CinematicPortrait.raycastTarget=false;
            CinematicTitle=EriDefenseHUD.MakeText(Cinematic,"Eri Call",style,new Vector2(0,-62),new Vector2(330,40),25);
            CinematicTitle.rectTransform.anchorMin=CinematicTitle.rectTransform.anchorMax=new Vector2(.5f,.5f);
            CinematicTitle.rectTransform.pivot=new Vector2(.5f,.5f);CinematicTitle.alignment=TextAlignmentOptions.Center;
            CinematicTitle.text="ERI · ALL OUT";CinematicTitle.color=new Color(.86f,1f,1f);
            CinematicFlash=EriDefenseHUD.Rect(Cinematic,"Impact Flash",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();
            CinematicFlash.rectTransform.anchorMin=Vector2.zero;CinematicFlash.rectTransform.anchorMax=Vector2.one;
            CinematicFlash.rectTransform.offsetMin=CinematicFlash.rectTransform.offsetMax=Vector2.zero;
            CinematicFlash.color=new Color(.8f,1f,1f,0);CinematicFlash.raycastTarget=false;
            Cinematic.gameObject.SetActive(false);
        }
        if(CinematicInvert==null)
        {
            var existing=Cinematic.Find("Screen Inversion");
            var rect=existing!=null?(RectTransform)existing:EriDefenseHUD.Rect(Cinematic,"Screen Inversion",new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            CinematicInvert=rect.GetComponent<UnityEngine.UI.Image>();
            if(CinematicInvert==null)CinematicInvert=rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            CinematicInvert.color=Color.black;
            CinematicInvert.raycastTarget=false;
            rect.SetAsLastSibling();
        }
        if(CinematicPortrait!=null)CinematicPortrait.rectTransform.anchoredPosition=Vector2.zero;
        if(InvertShader==null)InvertShader=Shader.Find("Project Eri/UI/Screen Invert");
    }
    public void BeginCinematic(Sprite eriSprite)
    {
        EnsurePresentation();
        if(invertMaterial==null)
        {
            if(InvertShader!=null)invertMaterial=new Material(InvertShader);
        }
        CinematicInvert.material=invertMaterial;
        CinematicInvert.enabled=invertMaterial!=null;
        CinematicPortrait.sprite=eriSprite;
        CinematicPortrait.enabled=eriSprite!=null;
        introBandColor=CinematicBand.color;
        introPortraitColor=CinematicPortrait.color;
        introTitleAlpha=CinematicTitle.alpha;
        Cinematic.gameObject.SetActive(true);
        SetCinematicProgress(0);
    }
    public void SetCinematicProgress(float progress)
    {
        if(Cinematic==null)return;
        float t=Mathf.Clamp01(progress);
        // Eri holds center stage before the band disappears and the world-space swipe takes over.
        CinematicPortrait.rectTransform.anchoredPosition=Vector2.zero;
        float bandFade=1f-Mathf.InverseLerp(.3f,.48f,t);
        CinematicBand.color=new Color(introBandColor.r,introBandColor.g,introBandColor.b,introBandColor.a*bandFade);
        CinematicPortrait.color=new Color(introPortraitColor.r,introPortraitColor.g,introPortraitColor.b,introPortraitColor.a*bandFade);
        CinematicTitle.alpha=introTitleAlpha*bandFade;
        float invert=t<.33f?0:t<.42f?Mathf.InverseLerp(.33f,.42f,t):
            t<.5f?1f:t<.62f?1f-Mathf.InverseLerp(.5f,.62f,t):0;
        if(CinematicInvert!=null)CinematicInvert.color=new Color(invert,invert,invert,1);
        CinematicFlash.color=new Color(.8f,1f,1f,t>.76f?Mathf.Sin(Mathf.InverseLerp(.76f,1f,t)*Mathf.PI)*.34f:0);
    }
    public void EndCinematic()
    {
        if(Cinematic==null)return;
        if(CinematicBand!=null)CinematicBand.color=introBandColor;
        if(CinematicPortrait!=null)CinematicPortrait.color=introPortraitColor;
        if(CinematicTitle!=null)CinematicTitle.alpha=introTitleAlpha;
        if(CinematicInvert!=null)CinematicInvert.color=Color.black;
        Cinematic.gameObject.SetActive(false);
    }
    private void Update()
    {
        if(Offer==null)return;
        var mechanics=EriCombatMechanics.Active;
        if(mechanics==null){Offer.gameObject.SetActive(false);Rewards.text="";return;}
        if(combat==null)combat=FindFirstObjectByType<EriTurnCombat>();
        bool pending=mechanics.AllOutAvailable && !mechanics.AllOutExecuting;
        Offer.gameObject.SetActive(pending);
        bool canInput=combat!=null && !combat.IsBusy && Time.timeScale>0 && (view==null || view.CommandPanel==null || !view.CommandPanel.activeSelf);
        Confirm.interactable=pending && canInput;
        if(OfferLabel!=null)OfferLabel.text=canInput?"Eri · All-out attack":"Eri · All-out ready";
        Rewards.text=(mechanics.ShieldActions>0?"Shield · Extra action"+(mechanics.ShieldActions>1?" ×"+mechanics.ShieldActions:""):"")+
            (mechanics.ShieldActions>0 && mechanics.ArmorBonusReady?"   |   ":"")+
            (mechanics.ArmorBonusReady?"Armor · Next hit +"+Mathf.RoundToInt((mechanics.ArmorAttackMultiplier-1)*100)+"%":"");
    }
    private void ConfirmOffer()
    {
        // Mechanics revalidates the living-enemy condition and current input state on the click.
        EriCombatMechanics.Active?.TryAllOut();
    }
}
