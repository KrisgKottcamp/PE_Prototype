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
        var bridge=combat.GetComponent<PlayerSpellV2Bridge>();var runner=combat.GetComponent<SpellRunner>();
        var menu=FindFirstObjectByType<CombatSkillMenuController>();
        Check(menu!=null,"existing skill menu found");
        typeof(CombatSkillMenuController).GetMethod("OpenSkillPanel",Private).Invoke(menu,null);
        Check(Time.timeScale==0,"skill selection fully pauses combat");
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
        Check(combat.Timing && Time.timeScale==0,"minigame runs while world is paused");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(bridge.IsTargeting && !combat.Timing,"minigame hands off to aiming");
        targeting.UpdateAim((Vector2)bridge.transform.position+Vector2.right*2);
        targeting.CancelTargeting();
        Check(m.currentAP==m.def.maxAP && m.currentMP==100 && m.exhaustedSegments==0,"cancel spends nothing");
        var context=CastContext.ForDirection(bridge.gameObject,bridge.transform.position,Vector2.right);
        Check(runner.TryCast(slash,context,out var failure),"valid cast accepted: "+failure);
        Check(m.currentMP==97 && m.exhaustedSegments==1,"MP spent and segment exhausted exactly once");
        Check(Mathf.Approximately(combat.Remaining,4),"fixed recovery starts");
        Check(!runner.TryCast(slash,context,out _),"immediate repeat rejected");
        Check(m.currentMP==97 && m.exhaustedSegments==1,"rejected cast spends nothing");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(Mathf.Approximately(combat.Remaining,4),"recovery freezes during menu pause");
        typeof(CombatSkillMenuController).GetMethod("CloseSkillPanel",Private).Invoke(menu,null);
        Check(Time.timeScale>0,"closing menu restores time");
        int oldIndex=party.activeIndex;party.SwapNextAlive();
        Check(party.activeIndex!=oldIndex && party.Active.currentAP==0,"switch starts empty");
        Check(combat.Remaining>3.9f,"switch preserves shared recovery");
        combat.RefreshSkills();
        // Freeze enemies while testing real-time recovery; this does not affect assets.
        foreach(var enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
            foreach(var behaviour in enemy.GetComponents<MonoBehaviour>())
                if(behaviour!=enemy && !(behaviour is EriFearMark))behaviour.enabled=false;
        yield return new WaitForSeconds(4.1f);
        Check(combat.Remaining<=0,"recovery completes in game time");
        var incoming=party.Active;party.AddAPToActive(999);
        slash=new List<SpellDefinition>(combat.Skills).Find(s=>(s.Delivery as EriPrototypeDelivery).Kind==EriCommandKind.Slash);
        Check(runner.TryCast(slash,context,out _),"next character can cast");
        Check(m.exhaustedSegments==0 && m.currentMP==97,"reserve capacity restored without restoring MP");
        yield return new WaitForSeconds(4.1f);
        incoming.exhaustedSegments=4;incoming.currentAP=0;incoming.currentMP=0;
        foreach(var other in party.party)if(other!=incoming)other.currentHP=0;
        var recover=new List<SpellDefinition>(combat.Skills).Find(s=>(s.Delivery as EriPrototypeDelivery).Kind==EriCommandKind.Recover);
        Check(runner.TryCast(recover,context,out _),"Recover available at zero MP/AP");
        Check(incoming.exhaustedSegments==3 && incoming.currentAP==0,"Recover restores capacity without charge");
        foreach(var other in party.party)other.currentHP=other.def.maxHP;
        var markHost=new GameObject("Fear reaction check");var mark=markHost.AddComponent<EriFearMark>();
        mark.Remaining=16;mark.NaturalMultiplier=1.25f;
        Check(mark.ResolveFearDamage(40)==88 && mark.Remaining==0,"Fear weakness and consumed mark multiply correctly");
        Check(mark.ResolveFearDamage(40)==50,"unmarked attack has only natural weakness");
        Destroy(markHost);
        yield return new WaitForSeconds(4.1f);
        int potionCount=party.mpPotions;combat.UsePotion();
        Check(incoming.currentMP==30 && party.mpPotions==potionCount-1,"potion restores MP and consumes stock");
        Check(combat.Remaining>3.9f,"potion uses shared command");
        yield return new WaitForSeconds(4.1f);
        incoming.exhaustedSegments=0;party.AddAPToActive(999);
        // Eri can heal herself without the danger refusal for a party-target call.
        var support=EriSupportManager.Instance;
        Check(support!=null,"original Eri support remains available");
        // An invalid target must never spend the new resources.
        int beforeMp=incoming.currentMP;int beforeAp=incoming.currentAP;
        Check(!combat.TryCallEri(int.MaxValue),"Call Eri rejects an invalid ally");
        Check(incoming.currentMP==beforeMp && incoming.currentAP==beforeAp,"rejected Eri call spends nothing");
        foreach(var member in party.party){member.currentHP=member.def.maxHP;member.currentAP=0;member.exhaustedSegments=0;}
        typeof(CombatSkillMenuController).GetMethod("OpenSkillPanel",Private).Invoke(menu,null);
        yield return new WaitForSecondsRealtime(0.3f);
        System.IO.Directory.CreateDirectory("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs");
        ScreenCapture.CaptureScreenshot("C:/Users/Kristian/Documents/Codex/2026-09-13/i-want-you-to-make-an/outputs/eri-turn-prototype-"+DateTime.Now.ToString("HHmmss")+".png");
        yield return new WaitForSecondsRealtime(1);
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
}
