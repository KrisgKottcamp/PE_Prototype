using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One-time migration and navigation. Existing authored UI is never regenerated.</summary>
public static class EriUIAuthoring
{
    private const string Folder="Assets/Combat/TurnPrototype/UI";
    private const string PrefabPath=Folder+"/EriCombatUI.prefab";
    private static TMP_FontAsset font;
    private static Material textMaterial;
    private static Sprite fillSprite;

    [MenuItem("Tools/Project Eri/UI/Create Editable UI %&F8")]
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("ERI_UI: Leave Play Mode before authoring UI.");return;}
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(EriCombatUIView.ScenePath)!=null){Open();return;}
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)!=null){Debug.LogError("ERI_UI: A prefab already exists; refusing to overwrite it.");return;}
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
        {Debug.LogWarning("ERI_UI: Save your open scene before creating the UI scene.");return;}
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Combat/TurnPrototype","UI");
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        textMaterial=new Material(font.material){name="Eri Readable Text"};
        textMaterial.SetColor("_FaceColor",Color.white);textMaterial.SetColor("_OutlineColor",EriTurnHUD.TextHalo);
        textMaterial.SetFloat("_OutlineWidth",0.25f);textMaterial.SetFloat("_OutlineSoftness",0);
        textMaterial.EnableKeyword("UNDERLAY_ON");textMaterial.SetColor("_UnderlayColor",EriTurnHUD.TextHalo);
        textMaterial.SetFloat("_UnderlayOffsetX",0);textMaterial.SetFloat("_UnderlayOffsetY",0);
        textMaterial.SetFloat("_UnderlayDilate",1);textMaterial.SetFloat("_UnderlaySoftness",0);
        AssetDatabase.CreateAsset(textMaterial,Folder+"/Eri Readable Text.mat");
        var texture=new Texture2D(2,2){name="White Fill",filterMode=FilterMode.Point};
        texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();
        AssetDatabase.CreateAsset(texture,Folder+"/Bar Fill.asset");
        fillSprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(0.5f,0.5f),100);
        fillSprite.name="Bar Fill";AssetDatabase.AddObjectToAsset(fillSprite,texture);

        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Eri Combat UI",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster),typeof(EriCombatUIView));
        EriFearIcon.ConfigureOverlay(root.GetComponent<Canvas>(),1000);
        var scaler=root.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=0.5f;
        var view=root.GetComponent<EriCombatUIView>();

        var markers=Rect(root.transform,"Enemy Status Markers",Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
        Stretch(markers);
        view.EnemyMarkTemplate=Rect(markers,"Enemy Mark Template",new Vector2(0.5f,0.5f),new Vector2(0.5f,0.5f),new Vector2(0,210),new Vector2(40,24));
        var enemyIcon=view.EnemyMarkTemplate.gameObject.AddComponent<EriFearIcon>();enemyIcon.Meaning=EriFearIcon.Cue.Weakness;enemyIcon.raycastTarget=false;

        var panel=Panel(root.transform,"Command Readiness",new Vector2(0.5f,0),new Vector2(0,18),new Vector2(410,66));
        view.Status=Text(panel,"Status",new Vector2(10,-5),new Vector2(390,22),16,"Phil · Ready");
        var track=Image(panel,"Readiness Track",new Vector2(10,-30),new Vector2(390,3),new Color(0.18f,0.2f,0.25f,0.4f));
        view.Readiness=Bar(track.transform,"Ready Fill",new Color(0.45f,0.88f,0.77f));
        view.Segments=new EriCombatUIView.APSegment[4];
        for(int i=0;i<4;i++)
        {
            var chamber=Image(panel,"AP Segment "+(i+1),new Vector2(10+i*99,-44),new Vector2(93,12),new Color(0.1f,0.15f,0.2f,0.45f));
            var fill=Bar(chamber.transform,"Charge Fill",new Color(0.28f,0.8f,0.9f,0.9f));
            fill.fillAmount=i<2?1:i==2?0.5f:0;
            var exhausted=Text(chamber.transform,"Exhausted Symbol",new Vector2(40,1),new Vector2(20,14),12,"×");
            exhausted.gameObject.SetActive(i==3);
            view.Segments[i]=new EriCombatUIView.APSegment{Track=chamber,Fill=fill,ExhaustedSymbol=exhausted.gameObject};
        }
        var party=Panel(root.transform,"Party Resources",Vector2.one,new Vector2(-18,-18),new Vector2(254,262));
        view.PartyHeader=Text(party,"Party Header",new Vector2(10,-5),new Vector2(234,20),13,"Party · Potions 2");
        view.Members=new EriCombatUIView.PartyRow[4];
        string[] names={"Audrey","Phil","Imogen","Dominic"};
        for(int i=0;i<4;i++)
        {
            var row=Rect(party,names[i]+" Row",new Vector2(0,1),new Vector2(0,1),new Vector2(10,-24-i*44),new Vector2(234,44));
            var name=Text(row,"Name",new Vector2(10,0),new Vector2(82,21),15,names[i]);
            var details=Text(row,"Details",new Vector2(95,-2),new Vector2(139,19),12,"MP 100 · 4/4");
            var active=Text(row,"Active Indicator",Vector2.zero,new Vector2(10,21),15,">");active.color=view.ActiveMember;
            active.gameObject.SetActive(i==1);
            var hpTrack=Image(row,"HP Track",new Vector2(0,-23),new Vector2(234,6),new Color(0.15f,0.12f,0.15f,0.45f));
            var hp=Bar(hpTrack.transform,"HP",new Color(0.42f,0.88f,0.65f));hp.fillAmount=i==1?0.65f:1;
            var mpTrack=Image(row,"MP Track",new Vector2(0,-32),new Vector2(234,3),new Color(0.18f,0.18f,0.25f,0.4f));
            var mp=Bar(mpTrack.transform,"MP",new Color(0.68f,0.55f,0.95f));
            view.Members[i]=new EriCombatUIView.PartyRow{Root=row.gameObject,Name=name,Details=details,ActiveIndicator=active.gameObject,HP=hp,MP=mp};
        }
        view.EriName=Text(party,"Eri Name",new Vector2(10,-200),new Vector2(45,21),15,"Eri");
        view.EriDetails=Text(party,"Eri Details",new Vector2(58,-202),new Vector2(180,19),12,"Heals 8/8");
        var eriTrack=Image(party,"Eri HP Track",new Vector2(10,-223),new Vector2(234,5),new Color(0.15f,0.12f,0.15f,0.45f));
        view.EriHP=Bar(eriTrack.transform,"Eri HP",new Color(0.42f,0.88f,0.65f));
        var potion=Image(party,"MP Potion",new Vector2(10,-236),new Vector2(234,20),new Color(0.2f,0.27f,0.37f,0.25f));
        potion.raycastTarget=true;view.Potion=potion.gameObject.AddComponent<UnityEngine.UI.Button>();view.Potion.targetGraphic=potion;
        Text(potion.transform,"Label",new Vector2(6,-1),new Vector2(222,19),13,"MP potion +30");

        var commands=Panel(root.transform,"Command Menu",new Vector2(0.5f,0.5f),Vector2.zero,new Vector2(530,222));
        commands.GetComponent<UnityEngine.UI.Image>().color=new Color(0.045f,0.055f,0.09f,0.33f);view.CommandPanel=commands.gameObject;
        Text(commands,"Heading",new Vector2(12,-10),new Vector2(506,20),14,"COMMANDS · SLOW MOTION").color=new Color32(188,235,222,255);
        view.Commands=new EriCombatUIView.CommandRow[5];
        string[] titles={"Dread Field","Dread Pulse","Slash","Recover","Call Eri"};
        string[] costs={"6 MP","10 MP","3 MP","Restore capacity","5 MP"};
        int[] segmentCosts={1,2,1,0,1};
        for(int i=0;i<5;i++)
        {
            var row=Rect(commands,"Command Row "+(i+1),new Vector2(0,1),new Vector2(0,1),new Vector2(12,-32-i*22),new Vector2(506,22));
            var selected=Text(row,"Selection",Vector2.zero,new Vector2(16,22),18,">");selected.gameObject.SetActive(i==0);
            var name=Text(row,"Skill Name",new Vector2(16,0),new Vector2(172,22),18,titles[i]);
            var orbs=SegmentOrbs(row,segmentCosts[i]);
            var cost=Text(row,"Cost",new Vector2(248,-3),new Vector2(62,20),13,costs[i]);
            var mark=Badge(row,"Mark Badge",true);var damage=Badge(row,"Damage Badge",false);
            mark.SetActive(i==0);damage.SetActive(i==1);
            view.Commands[i]=new EriCombatUIView.CommandRow{Root=row.gameObject,Name=name,Cost=cost,Selection=selected.gameObject,MarkBadge=mark,DamageBadge=damage,SegmentOrbs=orbs};
        }
        view.Description=Text(commands,"Description",new Vector2(12,-149),new Vector2(506,42),14,"Mark enemies entering the circle with Fear for 16 seconds.");
        view.Description.textWrappingMode=TextWrappingModes.Normal;view.Description.color=new Color32(224,232,238,255);
        view.UnavailableReason=Text(commands,"Unavailable Reason",new Vector2(12,-194),new Vector2(506,18),13,"Build AP with basic attacks");
        view.UnavailableReason.color=new Color32(242,215,179,255);

        var timing=Panel(root.transform,"Timed Command",new Vector2(0.5f,0.5f),new Vector2(0,-145),new Vector2(300,46));view.TimingPanel=timing.gameObject;
        Text(timing,"Heading",new Vector2(10,-3),new Vector2(280,20),14,"Focus");
        var timingTrack=Image(timing,"Timing Track",new Vector2(10,-29),new Vector2(280,6),new Color(0.15f,0.2f,0.24f,0.55f));view.TimingTrack=timingTrack.rectTransform;
        var good=Image(timingTrack.transform,"Good Zone",Vector2.zero,Vector2.zero,new Color(0.37f,0.7f,0.65f,0.7f)).rectTransform;
        good.anchorMin=new Vector2(0.52f,0);good.anchorMax=new Vector2(0.88f,1);good.offsetMin=good.offsetMax=Vector2.zero;
        var perfect=Image(timingTrack.transform,"Perfect Zone",Vector2.zero,Vector2.zero,new Color(0.55f,1,0.8f)).rectTransform;
        perfect.anchorMin=new Vector2(0.62f,0);perfect.anchorMax=new Vector2(0.78f,1);perfect.offsetMin=perfect.offsetMax=Vector2.zero;
        view.TimingCursor=Image(timingTrack.transform,"Timing Cursor",new Vector2(0,3),new Vector2(2,12),Color.white).rectTransform;
        // Components hold normal-color styles; special state colors are on the view's Inspector.
        AssetDatabase.SaveAssetIfDirty(textMaterial);AssetDatabase.SaveAssetIfDirty(texture);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root,PrefabPath,InteractionMode.AutomatedAction);
        EditorSceneManager.SaveScene(scene,EriCombatUIView.ScenePath);
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(s=>s.path==EriCombatUIView.ScenePath))scenes.Add(new EditorBuildSettingsScene(EriCombatUIView.ScenePath,true));
        EditorBuildSettings.scenes=scenes.ToArray();
        SaveBuildSceneList();
        Selection.activeGameObject=root;
        Debug.Log("ERI_UI: Created editable scene, prefab, persistent text material and bar sprite. Save the UI scene to change all encounters.");
        // Restore the saved Bootstrap scene for the existing verification flow.
        EditorSceneManager.CloseScene(scene,true);
    }

    [MenuItem("Tools/Project Eri/UI/Open Editable UI Scene %&F7")]
    public static void Open()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("ERI_UI: Exit Play Mode to edit permanently.");return;}
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        EditorSceneManager.OpenScene(EriCombatUIView.ScenePath,OpenSceneMode.Single);
        var view=UnityEngine.Object.FindFirstObjectByType<EriCombatUIView>();
        if(view!=null){Selection.activeGameObject=view.gameObject;SceneView.lastActiveSceneView?.FrameSelected();}
    }
    [MenuItem("Tools/Project Eri/UI/Add Segment Cost Orbs %&o")]
    public static void AddSegmentCostOrbs()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("ERI_UI_ORBS: Exit Play Mode before editing the saved UI.");return;}
        var scene=SceneManager.GetSceneByPath(EriCombatUIView.ScenePath);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(EriCombatUIView.ScenePath,OpenSceneMode.Additive);
        try
        {
            var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EriCombatUIView>(true)).Single();
            int[] previewCosts={1,2,1,0,1};
            for(int i=0;i<view.Commands.Length;i++)
            {
                var row=view.Commands[i];
                if(row.Root==null)throw new Exception("Command row "+(i+1)+" is missing its root binding.");
                row.SegmentOrbs=SegmentOrbs(row.Root.transform,i<previewCosts.Length?previewCosts[i]:1);
                if(row.Cost!=null)
                {
                    var rect=row.Cost.rectTransform;rect.anchoredPosition=new Vector2(248,rect.anchoredPosition.y);rect.sizeDelta=new Vector2(62,rect.sizeDelta.y);
                    if(i<previewCosts.Length)row.Cost.text=i==3?view.FullRecoverCost:(i==1?"10 MP":i==0?"6 MP":i==2?"3 MP":"5 MP");
                }
            }
            EditorUtility.SetDirty(view);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("ERI_UI_ORBS: PASS added four editable segment-cost orb slots to every command row.");
        }
        catch(Exception e){Debug.LogError("ERI_UI_ORBS: FAIL "+e);}
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
    [MenuItem("Tools/Project Eri/UI/Validate Saved UI %&F6")]
    public static void ValidateSavedUI()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var scene=SceneManager.GetSceneByPath(EriCombatUIView.ScenePath);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(EriCombatUIView.ScenePath,OpenSceneMode.Additive);
        try
        {
            var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EriCombatUIView>(true)).Single();
            if(view.Commands.Length<5 || view.Members.Length<4 || view.Segments.Length!=4)throw new Exception("Required row bindings are missing.");
            foreach(var row in view.Commands)
                if(row.Root==null || row.Name==null || row.Cost==null || row.Selection==null || row.MarkBadge==null || row.DamageBadge==null || row.SegmentOrbs==null || row.SegmentOrbs.Length!=4 || row.SegmentOrbs.Any(o=>o==null || o.sprite==null))throw new Exception("An editable command binding is missing.");
            foreach(var image in view.Segments.Select(s=>s.Fill).Concat(view.Members.Select(m=>m.HP)).Concat(view.Members.Select(m=>m.MP)))
                if(image==null || image.sprite==null || image.type!=UnityEngine.UI.Image.Type.Filled)throw new Exception("Bar must use a Filled Image with a sprite.");
            if(view.EnemyMarkTemplate==null || view.Potion==null || view.TimingTrack==null || view.TimingCursor==null)throw new Exception("Status/timing/potion binding is missing.");
            bool included=EditorBuildSettings.scenes.Any(s=>s.enabled && s.path==EriCombatUIView.ScenePath);
            Debug.Log("ERI_UI_AUTHORING: PASS saved scene bindings and bar assets. Build entry="+included+"; on disk="+System.IO.File.ReadAllText("ProjectSettings/EditorBuildSettings.asset").Contains(EriCombatUIView.ScenePath));
        }
        catch(Exception e){Debug.LogError("ERI_UI_AUTHORING: FAIL "+e);}
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
    [MenuItem("Tools/Project Eri/UI/Save UI Build Registration %&F5")]
    public static void SaveBuildSceneList()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(s=>s.path==EriCombatUIView.ScenePath))scenes.Add(new EditorBuildSettingsScene(EriCombatUIView.ScenePath,true));
        EditorBuildSettings.scenes=scenes.ToArray();
        // Unity 6 may defer ProjectSettings serialization until editor shutdown.
        // Flush this one settings object so the new scene registration is durable immediately.
        var settings=Resources.FindObjectsOfTypeAll<EditorBuildSettings>();
        if(settings.Length!=1)throw new Exception("Could not locate the existing build-scene settings object.");
        UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(settings,"ProjectSettings/EditorBuildSettings.asset",true);
        Debug.Log("ERI_UI: Build scene registration saved; on disk="+System.IO.File.ReadAllText("ProjectSettings/EditorBuildSettings.asset").Contains(EriCombatUIView.ScenePath));
    }
    private static RectTransform Rect(Transform parent,string name,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=pos;r.sizeDelta=size;return r;
    }
    private static RectTransform Panel(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size)
    {
        var r=Rect(parent,name,anchor,anchor,pos,size);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color=new Color(0.055f,0.065f,0.105f,0.30f);image.raycastTarget=false;return r;
    }
    private static UnityEngine.UI.Image Image(Transform parent,string name,Vector2 pos,Vector2 size,Color color)
    {
        var r=Rect(parent,name,new Vector2(0,1),new Vector2(0,1),pos,size);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color=color;image.raycastTarget=false;return image;
    }
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    private static UnityEngine.UI.Image Bar(Transform parent,string name,Color color)
    {
        var image=Image(parent,name,Vector2.zero,Vector2.zero,color);Stretch(image.rectTransform);
        image.sprite=fillSprite;image.type=UnityEngine.UI.Image.Type.Filled;image.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
        image.fillOrigin=0;image.fillAmount=1;return image;
    }
    private static UnityEngine.UI.Image[] SegmentOrbs(Transform row,int visibleCount)
    {
        var holder=row.Find("Segment Cost Orbs") as RectTransform;
        if(holder==null)holder=Rect(row,"Segment Cost Orbs",new Vector2(0,1),new Vector2(0,1),new Vector2(190,-6),new Vector2(50,10));
        else{holder.anchoredPosition=new Vector2(190,-6);holder.sizeDelta=new Vector2(50,10);}
        var sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var result=new UnityEngine.UI.Image[4];
        for(int i=0;i<result.Length;i++)
        {
            var child=holder.Find("Segment Orb "+(i+1));
            UnityEngine.UI.Image orb;
            if(child==null)
            {
                orb=Image(holder,"Segment Orb "+(i+1),new Vector2(i*13,0),new Vector2(10,10),new Color32(86,238,132,255));
                orb.sprite=sprite;orb.preserveAspect=true;
            }
            else
            {
                orb=child.GetComponent<UnityEngine.UI.Image>();
                if(orb==null)orb=child.gameObject.AddComponent<UnityEngine.UI.Image>();
                var rect=orb.rectTransform;rect.anchoredPosition=new Vector2(i*13,0);rect.sizeDelta=new Vector2(10,10);
                if(orb.sprite==null)orb.sprite=sprite;orb.preserveAspect=true;orb.raycastTarget=false;
            }
            orb.gameObject.SetActive(i<visibleCount);result[i]=orb;
        }
        return result;
    }
    private static TextMeshProUGUI Text(Transform parent,string name,Vector2 pos,Vector2 size,int fontSize,string value)
    {
        var r=Rect(parent,name,new Vector2(0,1),new Vector2(0,1),pos,size);var text=r.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font;text.fontSharedMaterial=textMaterial;text.fontSize=fontSize;text.fontStyle=FontStyles.Bold;
        text.enableAutoSizing=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.color=Color.white;
        text.raycastTarget=false;text.extraPadding=true;text.text=value;return text;
    }
    private static GameObject Badge(Transform parent,string name,bool mark)
    {
        var r=Rect(parent,name,new Vector2(0,1),new Vector2(0,1),new Vector2(312,0),new Vector2(194,22));
        var iconRect=Rect(r,"Icon",new Vector2(0,1),new Vector2(0,1),Vector2.zero,new Vector2(34,20));
        var icon=iconRect.gameObject.AddComponent<EriFearIcon>();icon.Meaning=mark?EriFearIcon.Cue.Weakness:EriFearIcon.Cue.Damage;icon.raycastTarget=false;
        var label=Text(r,"Label",new Vector2(40,-3),new Vector2(154,19),13,mark?"Mark · Fear":"Fear damage");
        label.color=mark?new Color32(255,217,140,255):new Color32(191,234,255,255);return r.gameObject;
    }
}
