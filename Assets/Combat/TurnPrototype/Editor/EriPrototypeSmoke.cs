using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ProjectEri.SkillSystemV2;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit local Play Mode check. Does not save or modify scene assets.</summary>
[InitializeOnLoad]
public static class EriPrototypeSmoke
{
    private const string Key="EriTurnSmokeRunning";
    private static double began;
    static EriPrototypeSmoke(){EditorApplication.update+=Update;began=EditorApplication.timeSinceStartup;}
    [MenuItem("Tools/Project Eri/Turn Prototype/Play Mode Smoke Test %&F9")]
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
        {Debug.LogWarning("ERI_TURN_SMOKE: Start from a saved Bootstrap scene outside Play Mode.");return;}
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Bootstrap")
        {Debug.LogWarning("ERI_TURN_SMOKE: Open Bootstrap first; the check will not change saved scenes.");return;}
        SessionState.SetBool(Key,true);began=EditorApplication.timeSinceStartup;EditorApplication.isPlaying=true;
    }
    private static void Update()
    {
        if(!SessionState.GetBool(Key,false))return;
        if(EditorApplication.timeSinceStartup-began>90)
        {SessionState.SetBool(Key,false);Debug.LogError("ERI_TURN_SMOKE: timeout");EditorApplication.isPlaying=false;return;}
        if(!EditorApplication.isPlaying || PartyManager.Instance==null || CombatContext.Instance==null)return;
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Bootstrap")return;
        SessionState.SetBool(Key,false);
        var host=new GameObject("Eri Prototype Verification");UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<EriPrototypePlayChecks>();
    }
}

public sealed class EriPrototypePlayChecks:MonoBehaviour
{
    private int assertions;
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private void Start(){StartCoroutine(Guard(Run()));}
    private IEnumerator Guard(IEnumerator routine)
    {
        while(true)
        {
            object next;
            try{if(!routine.MoveNext())break;next=routine.Current;}
            catch(Exception error){Debug.LogError("ERI_TURN_SMOKE: FAIL "+error);EditorApplication.isPlaying=false;yield break;}
            yield return next;
        }
    }
    private IEnumerator Run()
    {
        var debug=FindFirstObjectByType<EnterCombatDebug>();
        Check(debug!=null,"existing debug encounter present");
        var so=new SerializedObject(debug);
        var profile=CombatContext.Instance.proceduralProfile ?? so.FindProperty("proceduralProfile").objectReferenceValue as ProceduralEncounterProfile;
        string arena=CombatContext.Instance.arenaSceneName;
        if(profile!=null) CombatContext.Instance.ConfigureProceduralEncounter(profile,12345);
        else
        {
            var enemy=so.FindProperty("testEnemy").objectReferenceValue as EnemyDefinition;
            Check(enemy!=null,"existing manual enemy definition");
            var enemies=new List<EnemyDefinition>();
            for(int i=0;i<Mathf.Max(1,so.FindProperty("enemyCount").intValue);i++)enemies.Add(enemy);
            CombatContext.Instance.ConfigureManualEncounter(enemies);
        }
        SceneTransitionManager.Instance.TransitionTo(arena,"");
        float deadline=Time.realtimeSinceStartup+20;
        while(EriTurnCombat.Active==null && Time.realtimeSinceStartup<deadline)yield return null;
        Check(EriTurnCombat.Active!=null,"prototype attached to existing combat pawn");
        yield return new WaitForSecondsRealtime(1);
        var combat=EriTurnCombat.Active;var party=PartyManager.Instance;
        // This is an isolated rules test: prevent battle AI from damaging the test actors.
        combat.GetComponent<EriEnemyRhythm>().enabled=false;
        foreach(var enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
            foreach(var behaviour in enemy.GetComponents<MonoBehaviour>())
                if(behaviour!=enemy && !(behaviour is EriFearMark))behaviour.enabled=false;
        float uiDeadline=Time.realtimeSinceStartup+10;
        while(combat.GetComponent<EriTurnHUD>().View==null && Time.realtimeSinceStartup<uiDeadline)yield return null;
        var authoredUI=combat.GetComponent<EriTurnHUD>().View;
        Check(authoredUI!=null && authoredUI.gameObject.scene.path==EriCombatUIView.ScenePath,"combat loads the saved editable UI scene");
        var combatPawns=GameObject.FindGameObjectsWithTag("PlayerCombatPawn");
        Check(combatPawns.Length==1,"combat arena has exactly one player pawn");
        var overworldPlayer=PlayerSingleton.Instance;
        bool overworldPlayerVisible=false;
        if(overworldPlayer!=null)
        {
            foreach(var renderer in overworldPlayer.GetComponentsInChildren<SpriteRenderer>(true))
                if(renderer.enabled){overworldPlayerVisible=true;break;}
        }
        Check(!overworldPlayerVisible,"persistent overworld player stays hidden while HUD loads additively");
        var bridge=combat.GetComponent<PlayerSpellV2Bridge>();var runner=combat.GetComponent<SpellRunner>();
        var menu=FindFirstObjectByType<CombatSkillMenuController>();
        Check(menu!=null,"existing skill menu found");
        typeof(CombatSkillMenuController).GetMethod("OpenSkillPanel",Private).Invoke(menu,null);
        Check(Time.timeScale>0 && Mathf.Approximately(Time.timeScale,menu.CommandMenuTimeScale),"skill selection uses configured slow motion");
        foreach(var row in authoredUI.Commands)
            Check(row.SegmentOrbs!=null && row.SegmentOrbs.Length==4,"command row has four editable segment-cost orb slots");
        authoredUI.ShowCommand(0,"Orb Test","9 MP",3,true,true,null);
        int visibleCostOrbs=0;
        foreach(var orb in authoredUI.Commands[0].SegmentOrbs)if(orb!=null && orb.gameObject.activeSelf)visibleCostOrbs++;
        Check(visibleCostOrbs==3,"three-segment command displays exactly three cost orbs");
        typeof(CombatSkillMenuController).GetMethod("RefreshCompactPrototypeText",Private).Invoke(menu,null);
        // Runtime binding must preserve art-direction changes, including nested bar layout.
        var background=authoredUI.CommandPanel.GetComponent<UnityEngine.UI.Image>();
        Color savedBackground=background.color;float savedFont=authoredUI.Commands[0].Cost.fontSize;
        var barRect=(RectTransform)authoredUI.Members[0].HP.transform.parent;Vector2 savedBarSize=barRect.sizeDelta;
        background.color=new Color(0.1f,0.2f,0.3f,0.41f);authoredUI.Commands[0].Cost.fontSize=17;
        barRect.sizeDelta=new Vector2(177,9);
        yield return new WaitForSecondsRealtime(0.2f);
        Check(background.color.a==0.41f && authoredUI.Commands[0].Cost.fontSize==17 && barRect.sizeDelta==new Vector2(177,9),"combat refresh preserves edited opacity, font size and bar dimensions");
        background.color=savedBackground;authoredUI.Commands[0].Cost.fontSize=savedFont;barRect.sizeDelta=savedBarSize;
        var skills=new List<SpellDefinition>(combat.Skills);
        foreach(var spell in skills)
        {
            var issues=new List<SpellValidationIssue>();spell.CollectValidationIssues(issues);
            Check(!issues.Exists(i=>i.Severity==SpellValidationSeverity.Error),"valid skill "+spell.DisplayName);
        }
        var m=party.Active;m.currentAP=0;m.currentMP=100;
        var slash=skills.Find(s=>(s.Delivery as EriPrototypeDelivery).Kind==EriCommandKind.Slash);
        Check(!bridge.CanUse(slash,out _),"zero AP rejects skill");
        party.AddAPToActive(999);
        Check(m.currentAP==m.def.maxAP,"basic AP award respects cap");
        var targeting=bridge.GetComponent<PlayerSpellTargetingController>();
        Check(bridge.BeginSpell(slash,out _),"timed command begins");
        Check(combat.Timing && Mathf.Approximately(Time.timeScale,menu.CommandMenuTimeScale),"minigame retains configured slow motion");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(bridge.IsTargeting && !combat.Timing,"minigame hands off to aiming");
        Check(Mathf.Approximately(Time.timeScale,menu.CommandMenuTimeScale),"aiming respects menu speed");
        targeting.UpdateAim((Vector2)bridge.transform.position+Vector2.right*2);
        targeting.CancelTargeting();
        Check(m.currentAP==m.def.maxAP && m.currentMP==100 && m.exhaustedSegments==0,"cancel spends nothing");
        var context=CastContext.ForDirection(bridge.gameObject,bridge.transform.position,Vector2.right);
        Check(runner.TryCast(slash,context,out var failure),"valid cast accepted: "+failure);
        Check(m.currentMP==97 && m.exhaustedSegments==1,"MP spent and segment exhausted exactly once");
        Check(Mathf.Approximately(combat.Remaining,EriTurnRules.RecoverySeconds),"fixed recovery starts");
        int outgoingIndex=party.activeIndex;
        Check(combat.TurnEnding && m.currentAP==0,"accepted skill ends turn and discards AP");
        Check(!runner.TryCast(slash,context,out _),"immediate repeat rejected");
        Check(m.currentMP==97 && m.exhaustedSegments==1,"rejected cast spends nothing");
        // A genuine external pause must still defer handoff and freeze recovery.
        float menuSpeed=Time.timeScale;Time.timeScale=0;
        yield return new WaitForSecondsRealtime(0.3f);
        Check(Mathf.Approximately(combat.Remaining,EriTurnRules.RecoverySeconds),"recovery freezes during external pause");
        Check(party.activeIndex==outgoingIndex,"handoff waits for external pause to finish");
        Time.timeScale=menuSpeed;
        typeof(CombatSkillMenuController).GetMethod("CloseSkillPanel",Private).Invoke(menu,null);
        Check(Time.timeScale>0,"closing menu restores time");
        yield return null;yield return null;
        Check(party.activeIndex!=outgoingIndex && party.Active.currentAP==0 && !combat.TurnEnding,"skill automatically hands off at zero AP");
        Check(combat.IsWaiting(outgoingIndex),"outgoing member is locked until another action");
        for(int i=0;i<party.party.Count;i++)
        {
            var leaving=party.Active;leaving.currentAP=10;
            party.SwapNextAlive();
            Check(party.activeIndex!=outgoingIndex && leaving.currentAP==0 && party.Active.currentAP==0,"early switches discard charge and cannot bypass turn lock");
        }
        Check(combat.Remaining>0,"switch preserves shared recovery");
        combat.RefreshSkills();
        // Freeze enemies while testing real-time recovery; this does not affect assets.
        foreach(var enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
            foreach(var behaviour in enemy.GetComponents<MonoBehaviour>())
                if(behaviour!=enemy && !(behaviour is EriFearMark))behaviour.enabled=false;
        yield return new WaitForSeconds(EriTurnRules.RecoverySeconds+0.1f);
        Check(combat.Remaining<=0,"recovery completes in game time");
        var incoming=party.Active;party.AddAPToActive(999);
        slash=new List<SpellDefinition>(combat.Skills).Find(s=>(s.Delivery as EriPrototypeDelivery).Kind==EriCommandKind.Slash);
        Check(runner.TryCast(slash,context,out _),"next character can cast");
        Check(m.exhaustedSegments==0 && m.currentMP==97,"reserve capacity restored without restoring MP");
        Check(!combat.IsWaiting(outgoingIndex),"another skill unlocks previous actor");
        yield return new WaitForSeconds(EriTurnRules.RecoverySeconds+0.1f);
        incoming=party.Active;
        incoming.exhaustedSegments=4;incoming.currentAP=0;incoming.currentMP=0;
        foreach(var other in party.party)if(other!=incoming)other.currentHP=0;
        var recover=new List<SpellDefinition>(combat.Skills).Find(s=>(s.Delivery as EriPrototypeDelivery).Kind==EriCommandKind.Recover);
        Check(runner.TryCast(recover,context,out _),"Recover available at zero MP/AP");
        Check(incoming.exhaustedSegments==0 && incoming.currentAP==0,"Recover restores all capacity without charge");
        int soloIndex=party.activeIndex;
        yield return new WaitForSeconds(EriTurnRules.RecoverySeconds+0.1f);
        Check(party.activeIndex==soloIndex && !combat.TurnEnding && !combat.IsWaiting(soloIndex),"solo survivor starts a fresh turn");
        Check(combat.TryAwardGraze()>0,"close dodge awards AP");
        Check(combat.TryAwardGraze()==0,"graze rewards are rate limited");
        foreach(var other in party.party)other.currentHP=other.def.maxHP;
        party.SwapNextAlive();incoming=party.Active;incoming.currentMP=0;
        var markHost=new GameObject("Fear reaction check");var mark=markHost.AddComponent<EriFearMark>();
        mark.Remaining=16;mark.NaturalMultiplier=1.25f;
        Check(mark.ResolveFearDamage(40)==88 && mark.Remaining==0,"Fear weakness and consumed mark multiply correctly");
        Check(mark.ResolveFearDamage(40)==50,"unmarked attack has only natural weakness");
        Destroy(markHost);
        yield return new WaitForSeconds(EriTurnRules.RecoverySeconds+0.1f);
        int potionCount=party.mpPotions;combat.UsePotion();
        Check(incoming.currentMP==30 && party.mpPotions==potionCount-1,"potion restores MP and consumes stock");
        Check(combat.TurnEnding && combat.Remaining>0,"potion ends turn with shared recovery");
        yield return new WaitForSeconds(EriTurnRules.RecoverySeconds+0.1f);
        Check(party.Active!=incoming,"potion hands control to another member");
        incoming=party.Active;incoming.currentMP=30;
        incoming.exhaustedSegments=0;party.AddAPToActive(999);
        // Eri can heal herself without the danger refusal for a party-target call.
        var support=EriSupportManager.Instance;
        Check(support!=null,"original Eri support remains available");
        // An invalid target must never spend the new resources.
        int beforeMp=incoming.currentMP;int beforeAp=incoming.currentAP;
        Check(!combat.TryCallEri(int.MaxValue),"Call Eri rejects an invalid ally");
        Check(incoming.currentMP==beforeMp && incoming.currentAP==beforeAp,"rejected Eri call spends nothing");
        foreach(var member in party.party){member.currentHP=member.def.maxHP;member.currentAP=0;member.exhaustedSegments=0;}
        incoming.currentHP=Mathf.RoundToInt(incoming.def.maxHP*0.65f);
        incoming.exhaustedSegments=1;
        incoming.currentAP=EriTurnRules.Cost(incoming.def.maxAP,1,1)+EriTurnRules.Cost(incoming.def.maxAP,1,2)/2;
        foreach(var enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            var indicator=enemy.GetComponent<EriFearMark>()??enemy.gameObject.AddComponent<EriFearMark>();
            indicator.Remaining=16;
        }
        yield return new WaitForSecondsRealtime(0.2f);
        var charged=GameObject.Find("AP Segment 1").transform.Find("Charge Fill").GetComponent<UnityEngine.UI.Image>();
        var partial=GameObject.Find("AP Segment 2").transform.Find("Charge Fill").GetComponent<UnityEngine.UI.Image>();
        Check(charged.color.g>charged.color.r && charged.color.g>charged.color.b,"usable AP segment is green");
        Check(partial.color.b>partial.color.g && partial.color!=charged.color,"charging AP stays cyan");
        var icons=FindObjectsByType<EriFearIcon>(FindObjectsSortMode.None);
        Check(icons.Length>0,"marked enemies create visible overlay icons");
        foreach(var graphic in icons)
            Check(graphic.GetComponent<CanvasRenderer>()!=null && graphic.canvas.renderMode==RenderMode.ScreenSpaceOverlay,"mark icon has overlay renderer");
        System.IO.Directory.CreateDirectory("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs");
        ScreenCapture.CaptureScreenshot("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs/eri-compact-hud-"+DateTime.Now.ToString("HHmmss")+".png");
        yield return new WaitForSecondsRealtime(0.5f);
        typeof(CombatSkillMenuController).GetMethod("OpenSkillPanel",Private).Invoke(menu,null);
        yield return new WaitForSecondsRealtime(0.3f);
        // Pause holds the brief beam long enough to inspect its layering without changing spell timing.
        Vector2 visualCenter=Camera.main.ViewportToWorldPoint(new Vector3(0.5f,0.64f,10));
        var fieldCheck=new GameObject("Field Layer Check").AddComponent<EriFearField>();
        fieldCheck.Initialize(EriCommandKind.Mark,visualCenter,visualCenter,Vector2.right);
        var beamCheck=new GameObject("Beam Layer Check").AddComponent<EriFearField>();
        beamCheck.Initialize(EriCommandKind.Pierce,visualCenter+Vector2.left*5,visualCenter,Vector2.right);
        foreach(var effect in new[]{fieldCheck,beamCheck})
            Check(effect.GetComponent<LineRenderer>().sortingLayerName=="VFX","spell graphics use VFX layer above arena");
        float minimumContrast=float.MaxValue;int contrastCharacters=0;
        foreach(var label in FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            if(label.GetComponentInParent<EriCombatUIView>()==null)continue;
            label.ForceMeshUpdate();
            Check(label.fontMaterial.GetColor("_OutlineColor").a==1 && label.fontMaterial.IsKeywordEnabled("UNDERLAY_ON"),"text has opaque contrast halo");
            for(int i=0;i<label.textInfo.characterCount;i++)
            {
                var character=label.textInfo.characterInfo[i];if(!character.isVisible)continue;
                Color face=character.color;
                float ratio=Contrast(face,EriTurnHUD.TextHalo);
                minimumContrast=Mathf.Min(minimumContrast,ratio);contrastCharacters++;
                Check(ratio>=4.5f,"visible text palette meets WCAG AA contrast");
            }
        }
        Check(contrastCharacters>0,"contrast check covers rendered UI text");
        Debug.Log($"ERI_TEXT_CONTRAST: PASS {contrastCharacters} visible characters, minimum {minimumContrast:0.00}:1 against opaque text halo (target 4.5:1). Visual raster check still required.");
        System.IO.Directory.CreateDirectory("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs");
        ScreenCapture.CaptureScreenshot("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs/eri-turn-prototype-"+DateTime.Now.ToString("HHmmss")+".png");
        yield return new WaitForSecondsRealtime(1);
        int visualPartyIndex=party.activeIndex;
        int dominicIndex=party.party.FindIndex(member=>member.def.displayName=="Dominic");
        if(dominicIndex>=0)
        {
            party.activeIndex=dominicIndex;combat.RefreshSkills();
            typeof(CombatSkillMenuController).GetMethod("RefreshCompactPrototypeText",Private).Invoke(menu,null);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(authoredUI.Commands[0].MarkBadge.activeSelf && authoredUI.Commands[1].DamageBadge.activeSelf,"setup and damage tags are distinct");
            foreach(var label in authoredUI.CommandPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
            {label.ForceMeshUpdate();Check(!label.isTextOverflowing,"authored command labels fit their rectangles: "+label.name);}
            ScreenCapture.CaptureScreenshot("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs/eri-spell-types-"+DateTime.Now.ToString("HHmmss")+".png");
            yield return new WaitForSecondsRealtime(0.5f);
            party.activeIndex=visualPartyIndex;combat.RefreshSkills();
        }
        int persistedMp=incoming.currentMP;
        UnityEngine.SceneManagement.SceneManager.LoadScene(arena);
        yield return null;
        yield return new WaitForSecondsRealtime(1);
        Check(PartyManager.Instance==party && incoming.currentMP==persistedMp,"MP persists into the next encounter");
        Check(incoming.currentAP==0 && incoming.exhaustedSegments==0,"new encounter resets short-term AP only");
        Debug.Log("ERI_TURN_SMOKE: PASS ("+assertions+" integration assertions). Returning to Edit Mode.");
        EditorApplication.isPlaying=false;
    }
    private void Check(bool condition,string label){assertions++;if(!condition)throw new Exception(label);}
    private static float Contrast(Color foreground,Color background)
    {
        float a=Luminance(foreground),b=Luminance(background);
        return (Mathf.Max(a,b)+0.05f)/(Mathf.Min(a,b)+0.05f);
    }
    private static float Luminance(Color color)
    {
        return 0.2126f*Linear(color.r)+0.7152f*Linear(color.g)+0.0722f*Linear(color.b);
    }
    private static float Linear(float channel){return channel<=0.04045f?channel/12.92f:Mathf.Pow((channel+0.055f)/1.055f,2.4f);}
}
