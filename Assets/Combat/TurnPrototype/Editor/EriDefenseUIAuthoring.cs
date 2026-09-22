using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Additive authoring only: existing user-authored UI is not replaced or restyled.</summary>
[InitializeOnLoad]
public static class EriDefenseUIAuthoring
{
    private const string ResistanceUpgradeKey="Eri.AllOutInvertUI.v4";
    static EriDefenseUIAuthoring()
    {
        EditorApplication.delayCall+=InstallResistanceIconsOnce;
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=InstallResistanceIconsOnce;
        };
    }
    private static void InstallResistanceIconsOnce()
    {
        if(SessionState.GetBool(ResistanceUpgradeKey,false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
        var scene=SceneManager.GetSceneByPath(EriCombatUIView.ScenePath);
        if(scene.isLoaded && scene.isDirty)return;
        Install();
        SessionState.SetBool(ResistanceUpgradeKey,true);
    }
    [MenuItem("Tools/Project Eri/UI/Add Editable Defense UI")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("ERI_DEFENSE_UI: Exit Play Mode before saving UI.");return;}
        var scene=SceneManager.GetSceneByPath(EriCombatUIView.ScenePath);
        bool opened=!scene.isLoaded;
        if(!opened && scene.isDirty){Debug.LogWarning("ERI_DEFENSE_UI: Save the UI scene first; no changes made.");return;}
        if(opened)scene=EditorSceneManager.OpenScene(EriCombatUIView.ScenePath,OpenSceneMode.Additive);
        try
        {
            var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EriCombatUIView>(true)).FirstOrDefault();
            if(view==null){Debug.LogWarning("ERI_DEFENSE_UI: Existing combat UI view not found; no changes made.");return;}
            var bars=view.GetComponent<EriDefenseHUD>();if(bars==null)bars=Undo.AddComponent<EriDefenseHUD>(view.gameObject);
            var offer=view.GetComponent<EriAllOutView>();if(offer==null)offer=Undo.AddComponent<EriAllOutView>(view.gameObject);
            bars.EnsureTemplate();offer.EnsurePresentation();
            EditorUtility.SetDirty(bars);EditorUtility.SetDirty(offer);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("ERI_DEFENSE_UI: Added editable Enemy Defense Bar Template, Eri All-Out Offer, and Break Rewards. Existing UI preserved.");
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
}
