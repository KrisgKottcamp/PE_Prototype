using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Nonlethal component tests in an isolated temporary preview scene; never touch the encounter.</summary>
[InitializeOnLoad]
public static class EriDefenseStateChecks
{
    static EriDefenseStateChecks() { EditorApplication.delayCall += AutoRun; }
    private static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SessionState.GetBool("Eri.DefenseChecks.AllOutWake.v5", false)) return;
        Run();
        SessionState.SetBool("Eri.DefenseChecks.AllOutWake.v5", true);
    }
    [MenuItem("Tools/Project Eri/Check Defense State Transitions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode to run isolated defense checks."); return; }
        EriDefenseChecks.Run();
        var scene = EditorSceneManager.NewPreviewScene();
        var rules = ScriptableObject.CreateInstance<EriDefenseRules>();
        int count = 0;
        Action<bool, string> check = (ok, label) => { count++; if (!ok) throw new InvalidOperationException("Defense state: " + label); };
        try
        {
            var obj = new GameObject("Isolated defense check");
            SceneManager.MoveGameObjectToScene(obj, scene);
            var hp = obj.AddComponent<EnemyHealth>(); hp.Init(200);
            var defense = obj.AddComponent<EriEnemyDefenses>();
            if (defense.Health == null) typeof(EriEnemyDefenses).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(defense, null);
            defense.Rules = rules; defense.Configure(60, 60);
            int armorBreaks = 0, shieldBreaks = 0;
            defense.DefenseBroken += (armor, shield) => { if (armor) armorBreaks++; if (shield) shieldBreaks++; };
            defense.ApplyHit(40, false, false, 1f, 1);
            check(defense.Armor == 20 && defense.Shield == 46 && hp.CurrentHP == 200, "physical both defenses, health protected");
            defense.ApplyHit(20, true, false, 1f, 2);
            check(defense.Armor == 13 && defense.Shield == 26, "magic inverse routing");
            defense.ApplyOffBalance(8);
            defense.ApplyHit(13, false, false, 1f, 3);
            check(defense.IsKnockedDown && armorBreaks == 1 && shieldBreaks == 0, "armor break single reward");
            defense.ApplyHit(10, false, false, 1f, 4);
            check(hp.CurrentHP == 190 && defense.IsKnockedDown && defense.DownHitsLanded == 1 && defense.Armor == 0,
                "first down hit deals normal damage without waking");
            defense.ApplyHit(10, false, false, 1f, 4);
            check(hp.CurrentHP == 190 && defense.DownHitsLanded == 1, "one cast cannot count twice");
            defense.ApplyHit(10, false, false, 1f, 12);
            check(hp.CurrentHP == 160 && !defense.IsKnockedDown && defense.Armor == 60 && defense.Shield == 60,
                "second distinct hit adds max-Health bonus and restores defenses");
            defense.OffBalanceRemaining = 0;
            defense.Configure(20, 20);
            defense.ApplyHit(100, true, false, 1f, 5);
            check(defense.IsKnockedDown && armorBreaks == 2 && shieldBreaks == 1 && hp.CurrentHP == 160, "simultaneous breaks reward both, no overflow");
            check(!defense.ApplyAllOut(10, 10) && hp.CurrentHP == 150 && !defense.IsKnockedDown &&
                defense.Armor == 60 && defense.Shield == 60,
                "all-out stands a survivor up and restores both defenses");
            defense.Configure(60, 60);
            defense.ApplyHit(60, false, false, 1f, 6);
            check(defense.Shield == 39 && defense.IsKnockedDown, "intact second layer survives initial knockdown");
            check(defense.ApplyAllOut(10, 40) && hp.CurrentHP == 140 && !defense.IsKnockedDown &&
                defense.Shield == 60 && defense.Armor == 60 && defense.DownHitsLanded == 0,
                "all-out stands survivor up even when the other bar breaks");
            defense.ApplyHit(1, false, false, 1f, 14);
            check(hp.CurrentHP == 140 && !defense.IsKnockedDown,
                "next player hit faces restored defenses");
            hp.Init(200); defense.Configure(0, 0);
            defense.ApplyHit(160, false, false, 1f, 7);
            check(hp.CurrentHP == 40 && !defense.IsKnockedDown, "exactly20percent does not knock down");
            defense.ApplyHit(1, false, false, 1f, 8);
            check(hp.CurrentHP == 39 && defense.IsKnockedDown, "below20percent threshold knocks down");
            defense.Recover(); defense.ApplyHit(1, false, false, 1f, 9);
            check(!defense.IsKnockedDown, "remaining below threshold does not repeatedly knock down");
            hp.Init(200); defense.Configure(60, 60);
            defense.ApplyHit(40, false, false, 1.5f, 10);
            check(defense.Armor == 0 && defense.Shield == 39, "perfect mini-game boosts both defenses");
            defense.ApplyHit(20, false, false, 1f, 11, 1.25f);
            check(hp.CurrentHP == 175 && defense.IsKnockedDown && defense.DownHitsLanded == 1,
                "armor reward multiplies first normal down hit without consuming wake bonus");
            defense.ApplyHit(20, false, false, 1f, 13);
            check(hp.CurrentHP == 135 && !defense.IsKnockedDown,
                "final down hit alone receives the ten-percent-max-Health bonus");
            hp.Init(200); defense.Configure(0, 60);
            int rewardsBefore = armorBreaks + shieldBreaks;
            defense.ApplyHit(40, false, false, 1.5f, 21);
            check(hp.CurrentHP == 200 && defense.Shield == 39 && !defense.IsKnockedDown,
                "physical hits remaining Shield off-type without bypassing to Health");
            hp.Init(200); defense.Configure(60, 0);
            var mark = obj.AddComponent<EriFearMark>(); mark.Remaining = 28;
            defense.ApplyHit(40, true, true, 1.5f, 22);
            check(hp.CurrentHP == 200 && defense.Armor == 39 && mark.Remaining == 28,
                "magic hits remaining Armor off-type without bypassing or consuming Fear");
            check(armorBreaks + shieldBreaks == rewardsBefore && !defense.IsKnockedDown,
                "off-type chip does not grant a break reward before the bar breaks");
            hp.Init(200); defense.Configure(60, 0);
            defense.ApplyHit(100, false, false, 1f, 23);
            check(hp.CurrentHP == 200 && defense.IsKnockedDown && defense.DownHitsLanded == 0,
                "matching defense still absorbs breaking hit without Health overflow");
            Debug.Log("ERI_DEFENSE_STATE: " + count + " isolated component checks passed. Not a gameplay playtest.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(rules); }
    }
}
