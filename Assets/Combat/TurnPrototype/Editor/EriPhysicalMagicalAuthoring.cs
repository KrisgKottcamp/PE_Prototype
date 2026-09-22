using UnityEditor;
using UnityEngine;

/// <summary>One-time additive setup. Never overwrites tuning assets or user UI.</summary>
[InitializeOnLoad]
public static class EriPhysicalMagicalAuthoring
{
    private const string Folder = "Assets/Combat/TurnPrototype/Resources";
    private const string SessionKey = "Eri.PhysicalMagical.Authoring.v1";
    static EriPhysicalMagicalAuthoring() { EditorApplication.delayCall += InstallOnce; }
    private static void InstallOnce()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false)) return;
        Install();
        SessionState.SetBool(SessionKey, true);
    }
    [MenuItem("Tools/Project Eri/Physical Magical Prototype/Create Missing Settings And UI")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("ERI_PHYSICAL: Exit Play Mode before authoring."); return; }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Combat/TurnPrototype", "Resources");
        CreateIfMissing<EriDefenseRules>("EriDefenseRules");
        CreateIfMissing<EriCharacterKitSettings>("EriCharacterKitSettings");
        EriDefenseUIAuthoring.Install();
        EriDefenseChecks.Run();
        Debug.Log("ERI_PHYSICAL: Editable settings and defense UI ready. Existing assets preserved.");
    }
    private static void CreateIfMissing<T>(string name) where T : ScriptableObject
    {
        string path = Folder + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null) return;
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
    }
}
