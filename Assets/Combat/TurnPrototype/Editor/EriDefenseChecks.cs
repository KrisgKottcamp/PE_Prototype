using System;
using UnityEditor;
using UnityEngine;

/// <summary>Non-mutating formula checks. Full encounter behavior still requires a Play Mode check.</summary>
public static class EriDefenseChecks
{
    [MenuItem("Tools/Project Eri/Check Defense Formulas")]
    public static void Run()
    {
        int assertions = 0;
        Action<bool, string> check = (condition, description) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Defense check failed: " + description);
        };
        check(EriDefenseMath.Remaining(100, 40, 1f) == 60, "matching physical Armor / magical Shield");
        check(EriDefenseMath.Remaining(100, 40, .35f) == 86, "off-type 35 percent");
        check(EriDefenseMath.Remaining(20, 100, 1f) == 0, "overkill clamps to zero, no negative defense");
        check(EriDefenseMath.Remaining(0, 100, 1f) == 0, "already broken defense stays broken during all-out");
        check(EriDefenseMath.Remaining(100, 40, 2f) == 20, "off-balance doubles physical armor pressure");
        check(EriDefenseMath.Remaining(100, 40, 1.5f) == 40, "mini-game modifier increases defense damage");
        check(EriDefenseMath.KnockdownDamage(20, 100, .1f, 1f) == 30, "basic knockdown finisher");
        check(EriDefenseMath.KnockdownDamage(60, 100, .1f, 1f) == 70, "skill knockdown finisher preserves skill damage");
        check(EriDefenseMath.KnockdownDamage(20, 200, .1f, 1.25f) == 50, "armor bonus includes max-health component");
        check(!EriDefenseMath.CrossedThreshold(25, 20, 100, .2f), "exactly 20 percent not below");
        check(EriDefenseMath.CrossedThreshold(20, 19, 100, .2f), "crossing threshold qualifies");
        check(!EriDefenseMath.CrossedThreshold(19, 18, 100, .2f), "remaining below threshold does not reknock");
        check(!EriDefenseMath.CrossedThreshold(30, 0, 100, .2f), "dead enemy does not knock down");
        check(EriDefenseMath.Remaining(100, -40, 1f) == 100, "negative damage cannot heal defenses");
        check(!EriDefenseMath.BasicCanDamage(20, 0, false), "basic cannot damage standing Armor");
        check(EriDefenseMath.BasicCanDamage(0, 20, false), "physical basic bypasses Shield when Armor is absent");
        check(EriDefenseMath.BasicCanDamage(0, 0, false), "basic can damage Health-only target");
        check(EriDefenseMath.BasicCanDamage(0, 20, true), "basic cashes knockdown through remaining defense");
        Debug.Log("ERI_DEFENSE_CHECKS: " + assertions + " formula checks passed. No Play Mode encounter tested.");
    }
}
