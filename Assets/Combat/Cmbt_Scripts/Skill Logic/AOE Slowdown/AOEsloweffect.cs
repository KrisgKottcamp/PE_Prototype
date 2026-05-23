using UnityEngine;

/// <summary>
/// AoE effect that slows movement speed via SpeedModifier.
/// Create an SO instance: Create > Game > AoE Effects > Slow.
/// </summary>
[CreateAssetMenu(menuName = "Game/AoE Effects/Slow")]
public class AoESlowEffect : AoEEffect
{
    [Tooltip("Speed multiplier while inside the zone. 0.4 = 40% speed (60% slow).")]
    [Range(0.05f, 0.95f)]
    public float slowMultiplier = 0.4f;

    public override void OnApply(GameObject target, int sourceId)
    {
        var mod = target.GetComponent<SpeedModifier>();
        if (mod == null) mod = target.AddComponent<SpeedModifier>();
        mod.ApplySlow(sourceId, slowMultiplier);
    }

    public override void OnRemove(GameObject target, int sourceId)
    {
        var mod = target.GetComponent<SpeedModifier>();
        if (mod != null) mod.RemoveSlow(sourceId);
    }
}