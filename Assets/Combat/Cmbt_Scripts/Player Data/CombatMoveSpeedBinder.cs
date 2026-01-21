using UnityEngine;

public class CombatMoveSpeedBinder : MonoBehaviour
{
    [SerializeField] private TopDownMover mover;

    private int lastActiveIndex = -1;

    private void Awake()
    {
        if (mover == null) mover = GetComponent<TopDownMover>();
    }

    private void Start()
    {
        Apply();
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        if (pm.activeIndex != lastActiveIndex)
            Apply();
    }

    private void Apply()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null || mover == null) return;

        lastActiveIndex = pm.activeIndex;
        mover.MoveSpeed = pm.Active.def.combatMoveSpeed;
    }
}
