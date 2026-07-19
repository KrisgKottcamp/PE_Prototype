using System;
using UnityEngine;

/// <summary>
/// Per-impact camera shake tuning. Keep this on attack/damage sources so new
/// heavy attacks can opt in without adding camera-specific logic.
/// </summary>
[Serializable]
public struct CameraShakeSettings
{
    [Tooltip("Allows this impact's camera shake to be disabled while preserving its tuning.")]
    public bool enabled;

    [Tooltip("World-space impulse strength. Small top-down combat impacts generally sit between 0.15 and 0.5.")]
    [Min(0f)] public float strength;

    [Tooltip("Real-time impulse duration in seconds.")]
    [Min(0.01f)] public float duration;

    public CameraShakeSettings(bool enabled, float strength, float duration)
    {
        this.enabled = enabled;
        this.strength = Mathf.Max(0f, strength);
        this.duration = Mathf.Max(0.01f, duration);
    }

    public static CameraShakeSettings Create(float strength, float duration)
    {
        return new CameraShakeSettings(true, strength, duration);
    }
}
