using UnityEngine;

public class CombatPartyController : MonoBehaviour
{
    [SerializeField] private KeyCode swapKey = KeyCode.Tab;

    [Header("References")]
    [SerializeField] private CombatLockout lockout;

    private void Awake()
    {
        if (lockout == null) lockout = GetComponent<CombatLockout>();
    }

    private void Update()
    {
        if (lockout != null && lockout.IsLockedOut) return;

        if (Input.GetKeyDown(swapKey))
        {
            DoSwap();
        }
    }

    private void DoSwap()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        bool swapped = pm.SwapNextAlive();
        if (!swapped)
        {
            // nobody alive (should only happen if defeat logic failed)
            CombatManager.Instance?.NotifyPlayerDown();
            return;
        }

        // Trigger vulnerability window (no move/attack)
        lockout?.TriggerLockout();

        // Optional: you can add VFX/SFX here later
    }
}
