using UnityEngine;
using static CharacterDefinition;

public class CombatBasicAttackRouter : MonoBehaviour
{
    [SerializeField] private BasicAttack meleeAttack;
    [SerializeField] private ProjectileBasicAttack projectileAttack;
    [SerializeField] private HeavyComboAttack heavyComboAttack;
    [SerializeField] private WhipAttack whipAttack;

    private int lastIndex = -1;

    private void Awake()
    {
        if (meleeAttack == null) meleeAttack = GetComponent<BasicAttack>();
        if (projectileAttack == null) projectileAttack = GetComponent<ProjectileBasicAttack>();
        if (heavyComboAttack == null) heavyComboAttack = GetComponent<HeavyComboAttack>();
        if (whipAttack == null) whipAttack = GetComponent<WhipAttack>();
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

        BasicAttackType type = pm.Active.def.basicAttackType;

        if (meleeAttack != null) meleeAttack.enabled = type == BasicAttackType.Melee;
        if (projectileAttack != null) projectileAttack.enabled = type == BasicAttackType.Projectile;
        if (heavyComboAttack != null) heavyComboAttack.enabled = type == BasicAttackType.HeavyCombo;
        if (whipAttack != null) whipAttack.enabled = type == BasicAttackType.Whip;
    }
}