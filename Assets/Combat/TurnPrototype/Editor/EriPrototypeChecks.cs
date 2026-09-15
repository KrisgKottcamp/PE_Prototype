using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EriPrototypeChecks
{
    static EriPrototypeChecks() { EditorApplication.delayCall += Run; }
    [MenuItem("Tools/Project Eri/Turn Prototype/Run Rule Checks")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            Check(EriTurnRules.Capacity(100,0)==100,"initial capacity");
            Check(EriTurnRules.Capacity(100,2)==50,"two segments exhausted");
            Check(EriTurnRules.Capacity(100,4)==0,"exhausted capacity");
            Check(EriTurnRules.Cost(100,3)==75,"three segment cost");
            Check(EriTurnRules.Cost(99,1)==25,"fractional segment rounding");
            Check(EriTurnRules.RestoreSegment(4)==3,"Recover restores one");
            Check(EriTurnRules.RestoreSegment(0)==0,"recovery cannot overfill");
            Check(EriTurnRules.Unavailable(100,100,100,0,100,4,18,0)=="","full command allowed");
            Check(EriTurnRules.Unavailable(100,100,0,0,100,1,3,0).Contains("MP"),"MP gates commands");
            Check(EriTurnRules.Unavailable(100,100,100,3,100,2,3,0).Contains("Capacity"),"capacity gates commands");
            Check(EriTurnRules.Unavailable(100,0,100,0,100,1,3,0).Contains("Build"),"empty charge gates commands");
            Check(EriTurnRules.Unavailable(100,100,100,0,100,1,3,4).Contains("recovering"),"shared clock gates commands");
            Check(EriTurnRules.Unavailable(0,100,100,0,100,1,3,0).Contains("defeated"),"defeated actors rejected");
            int sum=0;for(int used=0;used<4;used++)sum+=EriTurnRules.Cost(150,1,used);
            Check(sum==150,"150 AP allows all four single-segment commands");
            Check(EriTurnRules.Cost(150,1,3)==EriTurnRules.Capacity(150,3),"last partial segment is spendable");
            Debug.Log("ERI_TURN_CHECKS: PASS (15 deterministic resource checks). Compilation succeeded.");
        }
        catch(Exception error){Debug.LogError("ERI_TURN_CHECKS: FAIL " + error);}
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);}
}
