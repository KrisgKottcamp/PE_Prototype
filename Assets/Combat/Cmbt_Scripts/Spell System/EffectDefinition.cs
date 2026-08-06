using UnityEngine;

[CreateAssetMenu(menuName = "Game/Spells/Effect Definition")]
public class EffectDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    [Tooltip("Unique id for stacking/refreshing logic.")]
    public string effectId;

    [Header("Duration")]
    [Tooltip("Total duration in seconds. 0 = instant (applied once).")]
    public float duration;

    [Header("HP Change")]
    [Tooltip("Positive = damage per tick, negative = heal per tick. Applied every tickInterval.")]
    public float hpChangePerTick;
    [Tooltip("Seconds between each HP tick. Ignored if hpChangePerTick is 0.")]
    public float tickInterval = 1f;

    [Header("HP Ramping")]
    [Tooltip("Multiplier applied to hpChangePerTick each tick. 1 = no ramp.")]
    public float hpRampMultiplier = 1f;

    [Header("Movement")]
    [Tooltip("Multiplier to movement speed. 1 = no change, 0.5 = 50% slower, 1.5 = 50% faster.")]
    public float movementSpeedMultiplier = 1f;
    public float movementRampMultiplier = 1f;

    [Header("AP Change")]
    [Tooltip("AP gained/lost per tick.")]
    public int apChangePerTick;
    public float apRampMultiplier = 1f;

    [Header("Stacking")]
    [Tooltip("If true, reapplying this effect refreshes duration instead of stacking.")]
    public bool refreshOnReapply = true;
    [Tooltip("Max stacks if refreshOnReapply is false.")]
    public int maxStacks = 1;

    [Header("VFX")]
    public GameObject applyVfxPrefab;
    public GameObject persistentVfxPrefab;
}
