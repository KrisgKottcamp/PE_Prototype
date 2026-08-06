using UnityEngine;

public class EffectInstance
{
    public EffectDefinition definition;
    public int stacks;
    public float remainingDuration;
    public float tickTimer;
    public float currentHpPerTick;
    public float currentMovementMultiplier;
    public int currentApPerTick;
    public GameObject persistentVfx;

    public EffectInstance(EffectDefinition def)
    {
        definition = def;
        stacks = 1;
        remainingDuration = def.duration;
        tickTimer = def.tickInterval;
        currentHpPerTick = def.hpChangePerTick;
        currentMovementMultiplier = def.movementSpeedMultiplier;
        currentApPerTick = def.apChangePerTick;
    }

    public void Refresh()
    {
        remainingDuration = definition.duration;
        tickTimer = definition.tickInterval;
        currentHpPerTick = definition.hpChangePerTick;
        currentMovementMultiplier = definition.movementSpeedMultiplier;
        currentApPerTick = definition.apChangePerTick;
    }

    public void ApplyRamp()
    {
        currentHpPerTick *= definition.hpRampMultiplier;
        currentMovementMultiplier *= definition.movementRampMultiplier;
        currentApPerTick = Mathf.RoundToInt(currentApPerTick * definition.apRampMultiplier);
    }

    public bool IsExpired => definition.duration > 0f && remainingDuration <= 0f;
}
