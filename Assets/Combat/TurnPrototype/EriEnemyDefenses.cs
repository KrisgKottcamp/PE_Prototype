using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Defense layers and their state transitions. Rewards/AI/UI subscribe rather than owning damage rules.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EriEnemyDefenses : MonoBehaviour
{
    public EriDefenseRules Rules;
    [Min(0)] public int MaxArmor = 60;
    [Min(0)] public int MaxShield = 60;
    public int Armor { get; private set; }
    public int Shield { get; private set; }
    public bool IsKnockedDown { get; private set; }
    public int DownHitsLanded { get; private set; }
    public float OffBalanceRemaining;
    [Range(0f, 1f)] public float TemporaryFearResistance = 1f;
    [Header("Optional enemy resistance action")]
    public bool CanRaiseFearWard;
    [Range(0.1f, 1f)] public float FearWardMultiplier = 0.5f;
    [Min(0.1f)] public float FearWardSeconds = 6f;
    [Min(0.1f)] public float FearWardCooldown = 12f;
    private float resistanceRemaining, nextWardAt;
    public event Action<bool, bool> DefenseBroken; // armor, shield
    public static event Action<EriEnemyDefenses, bool, bool> AnyDefenseBroken;
    public event Action Changed;
    private EnemyHealth health;
    private readonly HashSet<int> processedAttacks = new HashSet<int>();
    private readonly Queue<int> recentAttacks = new Queue<int>();
    public EnemyHealth Health => health;

    public static EriEnemyDefenses Ensure(EnemyHealth enemy)
    {
        return enemy.GetComponent<EriEnemyDefenses>() ?? enemy.gameObject.AddComponent<EriEnemyDefenses>();
    }

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        if (Rules == null) Rules = EriDefenseRules.Default;
        Armor = MaxArmor;
        Shield = MaxShield;
    }

    public void Configure(int armor, int shield)
    {
        MaxArmor = Mathf.Max(0, armor); MaxShield = Mathf.Max(0, shield);
        Recover();
        processedAttacks.Clear(); recentAttacks.Clear();
    }

    private void Update()
    {
        if (resistanceRemaining > 0)
        {
            resistanceRemaining = Mathf.Max(0, resistanceRemaining - Time.deltaTime);
            if (resistanceRemaining <= 0) RemoveTemporaryResistance();
        }
        OffBalanceRemaining = Mathf.Max(0, OffBalanceRemaining - Time.deltaTime);
    }

    public void Recover()
    {
        IsKnockedDown = false;
        DownHitsLanded = 0;
        Armor = MaxArmor; Shield = MaxShield;
        GetComponent<EriKnockdownVFX>()?.SetDown(false);
        Changed?.Invoke();
    }

    public void ApplyOffBalance(float seconds)
    {
        OffBalanceRemaining = Mathf.Max(OffBalanceRemaining, seconds);
        Changed?.Invoke();
    }

    public void RemoveTemporaryResistance()
    {
        TemporaryFearResistance = 1f;
        resistanceRemaining = 0;
        Changed?.Invoke();
    }
    public void TryRaiseFearWard()
    {
        if (!CanRaiseFearWard || IsKnockedDown || Time.time < nextWardAt) return;
        TemporaryFearResistance = FearWardMultiplier;
        resistanceRemaining = FearWardSeconds;
        nextWardAt = Time.time + FearWardCooldown;
        Changed?.Invoke();
    }

    private void KnockDown()
    {
        if (IsKnockedDown) return;
        IsKnockedDown = true;
        DownHitsLanded = 0;
        var effects = GetComponent<EriKnockdownVFX>() ?? gameObject.AddComponent<EriKnockdownVFX>();
        effects.SetDown(true);
        effects.BreakBurst();
        if (Application.isPlaying && Time.timeScale > 0)
        {
            HitstopManager.RequestSustained(HitstopSettings.Create(Rules.KnockdownHitstopSeconds,
                Rules.KnockdownSlowScale), 0f, Rules.KnockdownRecoverySeconds);
            CombatCameraShake.Request(CameraShakeSettings.Create(0.22f, 0.16f), transform.position, Vector2.up);
        }
    }

    private void NotifyBreaks(bool armor, bool shield)
    {
        if (!armor && !shield) return;
        KnockDown();
        DefenseBroken?.Invoke(armor, shield);
        AnyDefenseBroken?.Invoke(this, armor, shield);
    }

    // One attack ID per skill execution prevents individual fan/projectile hits cashing in their own break.
    // Zero means a standalone basic hit and intentionally does not deduplicate.
    public int ApplyHit(int damage, bool magical, bool fear, float defenseMultiplier = 1f, int attackId = 0, float attackMultiplier = 1f)
    {
        if (health.CurrentHP <= 0 || damage <= 0) return 0;
        if (attackId != 0)
        {
            if (!processedAttacks.Add(attackId)) return 0;
            recentAttacks.Enqueue(attackId);
            if (recentAttacks.Count > 128) processedAttacks.Remove(recentAttacks.Dequeue());
        }
        int before = health.CurrentHP;
        int elementalDamage = damage;
        bool hasDefense = Armor > 0 || Shield > 0;
        var mark = GetComponent<EriFearMark>();
        // Armor never benefits from emotional affinity or consumes its mark on its own.
        if (fear && mark != null && (Shield > 0 || IsKnockedDown || !hasDefense))
            elementalDamage = mark.ResolveFearDamage(damage);
        if (fear) elementalDamage = Mathf.RoundToInt(elementalDamage * TemporaryFearResistance);

        if (IsKnockedDown)
        {
            DownHitsLanded++;
            bool wakingHit = DownHitsLanded >= Mathf.Max(1, Rules.PlayerHitsToWake);
            int resolvedDamage = wakingHit
                ? EriDefenseMath.KnockdownDamage(elementalDamage, health.MaxHP, Rules.KnockdownMaxHealthBonus, attackMultiplier)
                : Mathf.RoundToInt(elementalDamage * attackMultiplier);
            health.ApplyHealthDamage(resolvedDamage);
            if (wakingHit)
            {
                if (Application.isPlaying && Time.timeScale > 0)
                {
                    HitstopManager.RequestSustained(HitstopSettings.Create(Rules.WakeHitstopSeconds,
                        Rules.WakeSlowScale), 0f, Rules.WakeRecoverySeconds);
                    CombatCameraShake.Request(CameraShakeSettings.Create(0.3f, 0.2f), transform.position, Vector2.up);
                }
                if (health.CurrentHP > 0) Recover();
            }
            else GetComponent<EriKnockdownVFX>()?.SetDownProgress(DownHitsLanded, Rules.PlayerHitsToWake);
        }
        else if (hasDefense)
        {
            int oldArmor = Armor, oldShield = Shield;
            float armorFactor = magical ? Rules.OffTypeMultiplier : 1f;
            if (!magical && OffBalanceRemaining > 0) armorFactor *= Rules.OffBalanceArmorMultiplier;
            Armor = EriDefenseMath.Remaining(Armor, damage, armorFactor * Mathf.Max(0, defenseMultiplier) * attackMultiplier);
            Shield = EriDefenseMath.Remaining(Shield, elementalDamage, (magical ? 1f : Rules.OffTypeMultiplier) * Mathf.Max(0, defenseMultiplier) * attackMultiplier);
            health.PlayHitFlash();
            NotifyBreaks(oldArmor > 0 && Armor == 0, oldShield > 0 && Shield == 0);
        }
        else
        {
            // Health is exposed only when no defense remains.
            health.ApplyHealthDamage(Mathf.RoundToInt(elementalDamage * attackMultiplier));
            CheckHealthThreshold(before);
        }
        Changed?.Invoke();
        return Mathf.Max(0, before - health.CurrentHP);
    }

    private bool CheckHealthThreshold(int previousHealth)
    {
        bool crossed = MaxArmor == 0 && MaxShield == 0 && health.CurrentHP > 0 &&
            EriDefenseMath.CrossedThreshold(previousHealth, health.CurrentHP, health.MaxHP, Rules.HealthOnlyThreshold);
        if (crossed) KnockDown();
        return crossed;
    }

    /// <summary>Damages every layer, awards any fresh breaks, then stands surviving enemies up.
    /// Returns whether the sweep broke a defense or crossed the health-only threshold.</summary>
    public bool ApplyAllOut(int healthDamage, int defenseDamage)
    {
        if (health.CurrentHP <= 0) return false;
        int before = health.CurrentHP, oldArmor = Armor, oldShield = Shield;
        health.ApplyHealthDamage(EriDefenseMath.AllOutDamage(healthDamage, health.MaxHP,
            Rules.AllOutMaxHealthFraction));
        Armor = Mathf.Max(0, Armor - Mathf.Max(0, defenseDamage));
        Shield = Mathf.Max(0, Shield - Mathf.Max(0, defenseDamage));
        if (health.CurrentHP <= 0)
        {
            IsKnockedDown = false;
            DownHitsLanded = 0;
            GetComponent<EriKnockdownVFX>()?.SetDown(false);
            Changed?.Invoke();
            return false;
        }
        bool armorBroken = oldArmor > 0 && Armor == 0;
        bool shieldBroken = oldShield > 0 && Shield == 0;
        NotifyBreaks(armorBroken, shieldBroken);
        bool threshold = CheckHealthThreshold(before);
        Recover();
        return armorBroken || shieldBroken || threshold;
    }
}
