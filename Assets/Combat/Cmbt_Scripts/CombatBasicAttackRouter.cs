using UnityEngine;
using static CharacterDefinition;

public class CombatBasicAttackRouter : MonoBehaviour
{
    [SerializeField] private BasicAttack meleeAttack;
    [SerializeField] private ProjectileBasicAttack projectileAttack;

    private int lastIndex = -1;

    private void Awake()
    {
        if (meleeAttack == null) meleeAttack = GetComponent<BasicAttack>();
        if (projectileAttack == null) projectileAttack = GetComponent<ProjectileBasicAttack>();
    }

    private void OnEnable()
    {
        ForceRefresh();
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        if (pm.activeIndex != lastIndex)
            ForceRefresh();
    }

    public void ForceRefresh()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;

        lastIndex = pm.activeIndex;

        bool useProjectile = pm.Active.def.basicAttackType == BasicAttackType.Projectile;

        if (meleeAttack != null) meleeAttack.enabled = !useProjectile;
        if (projectileAttack != null) projectileAttack.enabled = useProjectile;
    }
}
