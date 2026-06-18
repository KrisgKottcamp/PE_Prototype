using UnityEngine;

public class CombatMoveSpeedBinder : MonoBehaviour
{
    [SerializeField] private CombatPawnMover mover;

    private int lastActiveIndex = -1;

    private void Awake()
    {
        if (mover == null)
            mover = GetComponent<CombatPawnMover>();
    }

    private void Start()
    {
        Apply();
    }

    private void Update()
    {
        PartyManager pm = PartyManager.Instance;
        if (pm == null) return;

        if (pm.activeIndex != lastActiveIndex)
            Apply();
    }

    private void Apply()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.def == null || mover == null)
            return;

        lastActiveIndex = pm.activeIndex;

        // This is only the character's base combat speed.
        // Focus Mode and SpeedModifier are applied inside CombatPawnMover.
        mover.MoveSpeed = pm.Active.def.combatMoveSpeed;
    }

    [ContextMenu("Apply Move Speed Now")]
    private void ApplyFromContextMenu()
    {
        Apply();
    }
}