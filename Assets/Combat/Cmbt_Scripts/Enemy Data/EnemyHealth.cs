using UnityEngine;
using ProjectEri.SkillSystemV2;
using ProjectEri.EnemyAI.V2;

public class EnemyHealth : MonoBehaviour, ISpellDamageReceiver
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
        SetupSkillSystemV2();
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

    public bool TryReceiveDamage(
        in SpellDamageRequest request,
        out SpellDamageResult result)
    {
        int before = CurrentHP;
        int requested = Mathf.CeilToInt(request.Amount);
        TakeDamage(requested);
        int applied = Mathf.Max(0, before - Mathf.Max(0, CurrentHP));
        result = new SpellDamageResult(
            request.Amount,
            applied,
            before > 0 && CurrentHP <= 0);
        return applied > 0;
    }

    private void SetupSkillSystemV2()
    {
        CombatTeamMember team = GetComponent<CombatTeamMember>();
        if (team == null)
            team = gameObject.AddComponent<CombatTeamMember>();
        team.SetTeam(CombatTeam.Enemy);

        if (GetComponent<CombatTarget>() == null)
            gameObject.AddComponent<CombatTarget>();

        // Add this during enemy Awake so EnemyLocomotionV2 can cache it during
        // its normal Configure pass. Adding it only when a slow lands is too
        // late for locomotion instances that already cached their references.
        if (GetComponent<EnemySlowReceiverV2>() == null)
            gameObject.AddComponent<EnemySlowReceiverV2>();

        if (GetComponent<Rigidbody2D>() != null)
        {
            if (GetComponent<KnockbackReceiver2D>() == null)
                gameObject.AddComponent<KnockbackReceiver2D>();

            if (GetComponent<KnockbackSpellImpulseReceiverV2>() == null)
                gameObject.AddComponent<KnockbackSpellImpulseReceiverV2>();
        }
    }


    public System.Action<int, int> OnHealthChanged; // (current, max)
    public int MaxHP => maxHP;


}
