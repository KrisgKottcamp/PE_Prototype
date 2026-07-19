using System;
using UnityEngine;

/// <summary>
/// Tuning shared by player basic attacks when they hit an enemy.
/// Flinch is visual/movement feedback applied on every hit. Interrupt and
/// resolve affect enemy projectile preparation without disabling enemy AI.
/// </summary>
[Serializable]
public struct BasicAttackReactionSettings
{
    [Tooltip("If false, this hit does not apply basic-attack flinch or interruption.")]
    public bool enabled;

    [Tooltip("Short movement reaction applied on every hit.")]
    [Min(0f)] public float flinchSeconds;

    [Tooltip("Minimum delay before an interrupted enemy shooter may begin another attack.")]
    [Min(0f)] public float interruptDelay;

    [Tooltip("Time before this enemy can be interrupted by another basic attack.")]
    [Min(0f)] public float resolveSeconds;

    public static BasicAttackReactionSettings Create(
        float flinch,
        float interrupt,
        float resolve)
    {
        return new BasicAttackReactionSettings
        {
            enabled = true,
            flinchSeconds = Mathf.Max(0f, flinch),
            interruptDelay = Mathf.Max(0f, interrupt),
            resolveSeconds = Mathf.Max(0f, resolve)
        };
    }

    public void Apply(EnemyStunnable target)
    {
        if (!enabled || target == null)
            return;

        target.ReactToBasicAttack(
            flinchSeconds,
            interruptDelay,
            resolveSeconds
        );
    }
}
