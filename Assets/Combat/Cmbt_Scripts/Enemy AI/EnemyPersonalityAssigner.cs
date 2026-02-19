using System;
using UnityEngine;

public class EnemyPersonalityAssigner : MonoBehaviour
{
    [SerializeField] private EnemyPersonalityWeights weights;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int fixedSeed = 12345;

    private System.Random rng;

    private void Awake()
    {
        rng = new System.Random(useFixedSeed ? fixedSeed : Environment.TickCount);
    }

    // enemyTypeKey is unused now (pass null). Later you pass EnemyDefinition or prefab.
    public EnemyPersonality Assign(GameObject enemyGO, UnityEngine.Object enemyTypeKey = null)
    {
        var state = enemyGO.GetComponent<EnemyPersonalityState>();
        if (state == null) state = enemyGO.AddComponent<EnemyPersonalityState>();

        EnemyPersonality p = weights != null ? weights.Roll(rng, enemyTypeKey) : EnemyPersonality.Aggressive;
        state.SetBase(p);
        return p;
    }
}
