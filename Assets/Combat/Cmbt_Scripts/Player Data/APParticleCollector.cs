using UnityEngine;
using ProjectEri.SkillSystemV2;

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

        float statMultiplier = SpellStatModifierUtility.Evaluate(
            gameObject,
            SpellActorStat.ActionPointCollectionRadius,
            1f);
        return Mathf.Max(
            0f,
            manager.Active.def.apMagnetizationRange * statMultiplier
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
        int resolvedAmount = Mathf.Max(0, amount);
        return manager != null
            ? manager.AddAPToActive(resolvedAmount)
            : 0;
    }

    /// <summary>
    /// Resolves the total AP represented by a newly spawned reward. Applying
    /// the pickup-value modifier before splitting the reward keeps the visible
    /// particle count synchronized with the AP those particles will grant.
    /// </summary>
    public int ResolvePickupValue(int amount)
    {
        return SpellActionPointPickupUtility.ResolveRewardValue(
            gameObject,
            amount);
    }
}
