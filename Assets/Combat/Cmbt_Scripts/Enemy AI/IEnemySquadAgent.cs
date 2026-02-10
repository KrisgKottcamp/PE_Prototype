using UnityEngine;

public interface IEnemySquadAgent
{
    Transform Transform { get; }
    bool IsAlive { get; }
    float Health01 { get; }               // 0..1
    bool IsRanged { get; }
    bool IsMelee { get; }

    EnemySquadRole CurrentRole { get; }
    void SetRole(EnemySquadRole role);

    // Optional debug hook
    void SetSharedPlayerPosition(Vector2 pos);
}
