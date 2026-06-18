using UnityEngine;

public class CombatPartyController : MonoBehaviour
{
    [SerializeField] private KeyCode swapKey = KeyCode.C;

    [Header("References")]
    [SerializeField] private CombatLockout lockout;

    private void Awake()
    {
        if (lockout == null)
            lockout = GetComponent<CombatLockout>();
    }

    private void Update()
    {
        if (lockout != null && lockout.IsLockedOut)
            return;

        if (Input.GetKeyDown(swapKey))
            DoSwap();
    }

    private void DoSwap()
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null)
            return;

        bool swapped = pm.SwapNextAlive();

        if (!swapped)
        {
            CombatManager.Instance?.NotifyPlayerDown();
            return;
        }

        lockout?.TriggerLockout();
    }
}