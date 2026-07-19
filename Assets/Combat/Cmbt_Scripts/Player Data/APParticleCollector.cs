using UnityEngine;

/// <summary>
/// Collection target for loose AP particles. The same combat pawn remains in
/// the scene while characters swap, so the active CharacterDefinition is read
/// every frame instead of being cached here.
/// </summary>
public class APParticleCollector : MonoBehaviour
{
    public static APParticleCollector Current { get; private set; }

    public Vector2 Position => transform.position;

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    public float GetMagnetizationRange()
    {
        PartyManager manager = PartyManager.Instance;

        if (manager == null || manager.Active == null ||
            manager.Active.def == null)
        {
            return 0f;
        }

        return Mathf.Max(
            0f,
            manager.Active.def.apMagnetizationRange
        );
    }

    public bool CanReceiveAP()
    {
        PartyManager manager = PartyManager.Instance;

        if (manager == null || manager.Active == null ||
            manager.Active.def == null)
        {
            return false;
        }

        return manager.Active.currentAP <
            Mathf.Max(0, manager.Active.def.maxAP);
    }

    public int Collect(int amount)
    {
        PartyManager manager = PartyManager.Instance;
        return manager != null
            ? manager.AddAPToActive(amount)
            : 0;
    }
}
