using UnityEngine;

[System.Serializable]
public class SpellModifiers
{
    [Header("Behavior Modifiers")]
    public bool homing;
    [Tooltip("Percent per second the velocity adjusts toward the target.")]
    [Range(0f, 1f)]
    public float homingStrength = 0.3f;

    public bool tracking;
    [Tooltip("Max degrees per second the projectile can turn toward the target.")]
    public float trackingDegreesPerSecond = 90f;

    public bool boomerang;
    public bool returning;

    [Header("Secondary")]
    [Tooltip("Spawns this spell on each impact. Fires along the surface normal by default.")]
    public SpellDefinition secondaryEffect;

    [Header("Channeling")]
    public bool channeling;
    public ChannelMode channelMode = ChannelMode.WindDown;

    [Header("Bounce / Passthrough")]
    [Tooltip("How many times the projectile can hit before being destroyed. 0 = destroyed on first hit.")]
    public int bounce;
    [Tooltip("How many enemies the projectile passes through before being destroyed. 0 = stops on first enemy.")]
    public int passThrough;

    [Header("Vortex (Zone Only)")]
    [Tooltip("Zone pulls enemies and projectiles toward its center.")]
    public bool vortex;
    [Tooltip("Pull force applied per second.")]
    public float vortexStrength = 5f;

    [Header("Reflectable")]
    [Tooltip("Player can hit this projectile to redirect it and refresh its bounce count.")]
    public bool reflectable;

    [Header("Angle Modifiers")]
    [Tooltip("Added to the caster's aim angle.")]
    public float initialAngleDeg;
    [Tooltip("Overrides any computed angle. -1 means unused.")]
    public float setAngleDeg = -1f;
    [Tooltip("Rotates the firing angle between each shot in a multi-fire sequence.")]
    public float continuousAngleDeg;
}
