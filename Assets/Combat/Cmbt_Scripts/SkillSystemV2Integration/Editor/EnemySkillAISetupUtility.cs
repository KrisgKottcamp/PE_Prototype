using System.Collections.Generic;
using ProjectEri.EnemyAI.V2;
using ProjectEri.SkillSystemV2;
using UnityEditor;
using UnityEngine;

public static class EnemySkillAISetupUtility
{
    private const string ConfigureMenu =
        "Tools/Project Eri/Skill System V2/Enemy AI/Configure Selected Enemy";
    private const string ValidateMenu =
        "Tools/Project Eri/Skill System V2/Enemy AI/Validate Selected Enemy";

    [MenuItem(ConfigureMenu)]
    private static void ConfigureSelectedEnemy()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || selected.GetComponent<EnemyAgentV2>() == null)
        {
            EditorUtility.DisplayDialog(
                "Enemy Skill AI",
                "Select a GameObject that already has EnemyAgentV2. The setup tool will not convert legacy enemies automatically.",
                "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Enemy Skill AI V2");

        EnsureComponent<SpellLoadout>(selected);
        EnsureComponent<SpellRunner>(selected);
        EnsureComponent<EnemySpellResourceProviderV2>(selected);
        EnsureComponent<EnemySpellTargetingSolverV2>(selected);
        EnsureComponent<EnemySpellAIDecisionSupportV2>(selected);
        EnsureComponent<EnemySkillExecutorV2>(selected);

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(selected);
        Selection.activeGameObject = selected;
        EditorGUIUtility.PingObject(selected);

        EditorUtility.DisplayDialog(
            "Enemy Skill AI configured",
            "The runtime components are present. Assign Equipped Skills on SpellLoadout, enable Usable By AI on each intended spell, then opt in through EnemyAIV2Profile > Enable Skill Actions. Tune the profile's Skill Cadence fields and each spell's Placement Lookahead / AI Recast / Active Instance fields before playtesting.",
            "OK");
    }

    [MenuItem(ConfigureMenu, true)]
    [MenuItem(ValidateMenu, true)]
    private static bool HasSelectedObject()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem(ValidateMenu)]
    private static void ValidateSelectedEnemy()
    {
        GameObject selected = Selection.activeGameObject;
        var problems = new List<string>();
        if (selected == null)
        {
            problems.Add("No GameObject selected.");
        }
        else
        {
            Require<EnemyAgentV2>(selected, problems);
            Require<SpellLoadout>(selected, problems);
            Require<SpellRunner>(selected, problems);
            Require<EnemySpellResourceProviderV2>(selected, problems);
            Require<EnemySpellTargetingSolverV2>(selected, problems);
            Require<EnemySpellAIDecisionSupportV2>(selected, problems);
            Require<EnemySkillExecutorV2>(selected, problems);

            SpellLoadout loadout = selected.GetComponent<SpellLoadout>();
            if (loadout != null)
            {
                if (loadout.EquippedSkills.Count == 0)
                    problems.Add("SpellLoadout has no Equipped Skills.");

                for (int i = 0; i < loadout.EquippedSkills.Count; i++)
                {
                    SpellDefinition spell = loadout.EquippedSkills[i];
                    if (spell == null)
                    {
                        problems.Add($"Equipped Skill {i + 1} is empty.");
                        continue;
                    }
                    if (spell.Delivery == null)
                        problems.Add($"{spell.DisplayName} has no Delivery.");
                    if (!spell.AIAffordance.UsableByAI)
                        problems.Add($"{spell.DisplayName} does not have Usable By AI enabled.");
                    if (spell.AIAffordance.BaseUtility <= 0f)
                        problems.Add($"{spell.DisplayName} has zero AI Base Utility.");
                }
            }
        }

        string message = problems.Count == 0
            ? "Ready for enemy skill cadence and predictive placement. Confirm the active EnemyAIV2Profile has Enable Skill Actions checked. For a Slow Orb starting point, use Placement Lookahead 0.45-0.70, Minimum AI Recast 2-4 seconds, one active instance per caster, two per squad, and leave equivalent overlap disabled."
            : "Fix the following before enabling skill actions:\n\n- " +
              string.Join("\n- ", problems);
        EditorUtility.DisplayDialog(
            problems.Count == 0 ? "Enemy Skill AI ready" : "Enemy Skill AI needs attention",
            message,
            "OK");
    }

    private static T EnsureComponent<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null
            ? component
            : Undo.AddComponent<T>(target);
    }

    private static void Require<T>(GameObject target, List<string> problems)
        where T : Component
    {
        if (target.GetComponent<T>() == null)
            problems.Add($"Missing {typeof(T).Name}.");
    }
}
