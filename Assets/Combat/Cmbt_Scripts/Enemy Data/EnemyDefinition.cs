using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemies/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public string displayName;
    public int maxHP = 30;

    [Header("Prefab")]
    public GameObject prefab;
}
