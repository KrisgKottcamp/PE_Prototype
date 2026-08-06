using System.Collections.Generic;
using UnityEngine;

public class EffectReceiver : MonoBehaviour
{
    public delegate void HpChangeHandler(float amount);
    public delegate void ApChangeHandler(int amount);

    public event HpChangeHandler OnHpChange;
    public event ApChangeHandler OnApChange;

    private readonly List<EffectInstance> activeEffects = new();
    private readonly List<EffectInstance> toRemove = new();

    public IReadOnlyList<EffectInstance> ActiveEffects => activeEffects;

    public float GetCombinedMovementMultiplier()
    {
        float mult = 1f;
        for (int i = 0; i < activeEffects.Count; i++)
            mult *= activeEffects[i].currentMovementMultiplier;
        return mult;
    }

    public void ApplyEffect(EffectDefinition def)
    {
        if (def == null)
            return;

        EffectInstance existing = FindEffect(def);

        if (existing != null)
        {
            if (def.refreshOnReapply)
            {
                existing.Refresh();
                return;
            }

            if (existing.stacks < def.maxStacks)
            {
                existing.stacks++;
                existing.Refresh();
                return;
            }

            return;
        }

        EffectInstance inst = new EffectInstance(def);

        if (def.applyVfxPrefab != null)
            Instantiate(def.applyVfxPrefab, transform.position, Quaternion.identity);

        if (def.persistentVfxPrefab != null)
        {
            inst.persistentVfx = Instantiate(def.persistentVfxPrefab, transform);
            inst.persistentVfx.transform.localPosition = Vector3.zero;
        }

        // Instant effects (duration 0) apply once and don't persist
        if (def.duration <= 0f)
        {
            ApplyTick(inst);
            CleanupVfx(inst);
            return;
        }

        activeEffects.Add(inst);
    }

    public void RemoveEffect(string effectId)
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            if (activeEffects[i].definition.effectId == effectId)
            {
                CleanupVfx(activeEffects[i]);
                activeEffects.RemoveAt(i);
            }
        }
    }

    public void ClearAllEffects()
    {
        for (int i = 0; i < activeEffects.Count; i++)
            CleanupVfx(activeEffects[i]);

        activeEffects.Clear();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        toRemove.Clear();

        for (int i = 0; i < activeEffects.Count; i++)
        {
            EffectInstance inst = activeEffects[i];

            inst.remainingDuration -= dt;

            if (inst.definition.hpChangePerTick != 0f ||
                inst.definition.apChangePerTick != 0)
            {
                inst.tickTimer -= dt;
                if (inst.tickTimer <= 0f)
                {
                    ApplyTick(inst);
                    inst.ApplyRamp();
                    inst.tickTimer += inst.definition.tickInterval;
                }
            }

            if (inst.IsExpired)
                toRemove.Add(inst);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            CleanupVfx(toRemove[i]);
            activeEffects.Remove(toRemove[i]);
        }
    }

    private void ApplyTick(EffectInstance inst)
    {
        float hpChange = inst.currentHpPerTick * inst.stacks;
        int apChange = inst.currentApPerTick * inst.stacks;

        if (Mathf.Abs(hpChange) > 0.001f)
            OnHpChange?.Invoke(hpChange);

        if (apChange != 0)
            OnApChange?.Invoke(apChange);
    }

    private EffectInstance FindEffect(EffectDefinition def)
    {
        for (int i = 0; i < activeEffects.Count; i++)
        {
            if (activeEffects[i].definition.effectId == def.effectId)
                return activeEffects[i];
        }
        return null;
    }

    private void CleanupVfx(EffectInstance inst)
    {
        if (inst.persistentVfx != null)
            Destroy(inst.persistentVfx);
    }

    private void OnDestroy()
    {
        ClearAllEffects();
    }
}
