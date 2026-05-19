using UnityEngine;

/// <summary>
/// Base class for AoE zone effects. Create subclasses for specific behaviors
/// (slow, damage over time, heal, buff, etc.).
///
/// Each AoEZone instance gets a unique sourceId. Effects use this to
/// track their per-zone state (e.g. SpeedModifier uses it to support
/// multiple overlapping slow zones).
///
/// To create a new effect type:
///   1. Create a new class extending AoEEffect.
///   2. Add [CreateAssetMenu] attribute.
///   3. Implement OnApply and OnRemove.
///   4. Create an SO instance and add it to a SkillDefinition's aoeEffects list.
/// </summary>
public abstract class AoEEffect : ScriptableObject
{
    /// <summary>Called when an entity enters the zone (first collider only).</summary>
    public abstract void OnApply(GameObject target, int sourceId);

    /// <summary>Called when an entity fully leaves the zone (or zone expires).</summary>
    public abstract void OnRemove(GameObject target, int sourceId);
}