using UnityEngine;

/// <summary>Shared, editable tuning for the physical/magical prototype.</summary>
[CreateAssetMenu(menuName = "Project Eri/Defense Rules")]
public sealed class EriDefenseRules : ScriptableObject
{
    [Range(0f, 1f)] public float OffTypeMultiplier = 0.35f;
    [Range(0f, 1f)] public float KnockdownMaxHealthBonus = 0.1f;
    [Min(1)] public int PlayerHitsToWake = 2;
    [Range(0f, 1f)] public float HealthOnlyThreshold = 0.2f;
    [Min(1f)] public float OffBalanceArmorMultiplier = 2f;
    [Min(0)] public int DefaultArmor = 60;
    [Min(0)] public int DefaultShield = 60;
    [Min(1f)] public float ArmorRewardMultiplier = 1.25f;
    [Header("Test roster (only enemies without an authored defense component)")]
    public bool AssignTestProfiles = true;
    [Header("All-out sweep")]
    [Min(1)] public int AllOutHealthDamage = 150;
    [Min(1)] public int AllOutDefenseDamage = 60;
    [Min(0.1f)] public float SweepSeconds = 0.65f;
    [Header("Enemy pressure")]
    [Min(1)] public int EnemyProjectileDamage = 12;
    [Header("Combat feel")]
    [Min(1f)] public float APGainMultiplier = 1.5f;
    [Min(1f)] public float PhilMagnetMultiplier = 1.8f;
    [Min(0f)] public float KnockdownHitstopSeconds = 0.14f;
    [Range(0.005f, 1f)] public float KnockdownSlowScale = 0.12f;
    [Min(0f)] public float KnockdownRecoverySeconds = 0.2f;
    [Min(0f)] public float WakeHitstopSeconds = 0.19f;
    [Range(0.005f, 1f)] public float WakeSlowScale = 0.06f;
    [Min(0f)] public float WakeRecoverySeconds = 0.23f;
    [Header("Party buffs")]
    [Range(1f, 2f)] public float BuffAttackMultiplier = 1.2f;
    [Range(1f, 2f)] public float BuffSpeedMultiplier = 1.2f;
    [Range(0.1f, 1f)] public float BuffReceivedDamageMultiplier = 0.8f;
    private static EriDefenseRules fallback;
    public static EriDefenseRules Default
    {
        get
        {
            if (fallback == null)
            {
                fallback = Resources.Load<EriDefenseRules>("EriDefenseRules");
                if (fallback == null) fallback = CreateInstance<EriDefenseRules>();
            }
            return fallback;
        }
    }
}

/// <summary>Deterministic formulas, kept free of Unity objects for cheap regression tests.</summary>
public static class EriDefenseMath
{
    public static bool BasicCanDamage(int armor, int shield, bool knockedDown) => knockedDown || (armor <= 0 && shield <= 0);
    public static int Remaining(int current, int incoming, float multiplier)
        => System.Math.Max(0, current - (int)System.Math.Round(System.Math.Max(0, incoming) * System.Math.Max(0, multiplier)));
    public static int KnockdownDamage(int attackDamage, int maxHealth, float fraction, float multiplier)
        => (int)System.Math.Round((System.Math.Max(0, attackDamage) + System.Math.Max(0, maxHealth) * fraction) * multiplier);
    public static bool CrossedThreshold(int before, int after, int maxHealth, float threshold)
    {
        // Decimal preserves designer-entered thresholds such as .2 across Mono/CoreCLR.
        decimal boundary = maxHealth * (decimal)threshold;
        return after > 0 && before >= boundary && after < boundary;
    }
}
