using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private int maxHP = 30;
    public int CurrentHP { get; private set; }

    public System.Action<EnemyHealth> OnDied;
    private HitMorph hitMorph;
    private DamageFlash2D damageFlash;

    private void Awake()
    {
        CurrentHP = maxHP;
        hitMorph = GetComponentInChildren<HitMorph>(true);

        SetupDamageFlash();
    }

    private void SetupDamageFlash()
    {
        damageFlash = GetComponent<DamageFlash2D>();

        if (damageFlash == null)
            damageFlash = gameObject.AddComponent<DamageFlash2D>();

        if (!damageFlash.HasConfiguredTargets)
        {
            damageFlash.ConfigureTargets(
                DamageFlash2D.FindLikelyCharacterSprites(transform)
            );
        }
    }

    /// <summary>
    /// Visual confirmation for a resolved enemy hit that may not remove HP,
    /// such as Phil's zero-damage basic projectile.
    /// </summary>
    public void PlayHitFlash()
    {
        if (damageFlash == null)
            SetupDamageFlash();

        damageFlash?.PlayFlash();
    }

    public void Init(int hp)
    {
        maxHP = hp;
        CurrentHP = maxHP;
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0)
            return;

        hitMorph?.Play();
        PlayHitFlash();

        CurrentHP -= amount;
        OnHealthChanged?.Invoke(CurrentHP, maxHP);

        if (CurrentHP <= 0)
        {
            CurrentHP = 0;
            OnDied?.Invoke(this);
            Destroy(gameObject);
        }
    }


    public System.Action<int, int> OnHealthChanged; // (current, max)
    public int MaxHP => maxHP;


}
