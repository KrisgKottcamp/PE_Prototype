using UnityEngine;

public class EnemyPersonalityState : MonoBehaviour
{
    [SerializeField] private EnemyPersonality basePersonality;
    [SerializeField] private bool hasOverride;
    [SerializeField] private EnemyPersonality overridePersonality;

    public EnemyPersonality Current => hasOverride ? overridePersonality : basePersonality;
    public EnemyPersonality Base => basePersonality;

    public void SetBase(EnemyPersonality p)
    {
        basePersonality = p;
        hasOverride = false;
    }

    public void SetOverride(EnemyPersonality p)
    {
        overridePersonality = p;
        hasOverride = true;
    }

    public void ClearOverride()
    {
        hasOverride = false;
    }
}
