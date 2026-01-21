using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private int maxHP = 30;
    public int CurrentHP { get; private set; }

    public System.Action<EnemyHealth> OnDied;
    private HitMorph hitMorph;

    private void Awake()
    {
        CurrentHP = maxHP;
        hitMorph = GetComponentInChildren<HitMorph>(true);

    }

    public void Init(int hp)
    {
        maxHP = hp;
        CurrentHP = maxHP;
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    public void TakeDamage(int amount)
    {
        hitMorph?.Play();

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
