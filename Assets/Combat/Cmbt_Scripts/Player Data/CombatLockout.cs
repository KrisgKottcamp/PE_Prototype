using System.Collections;
using UnityEngine;

public class CombatLockout : MonoBehaviour
{
    [SerializeField] private float lockoutSeconds = 0.35f;

    [Header("Disable During Lockout")]
    [SerializeField] private MonoBehaviour[] disableScripts; // TopDownMover, BasicAttack later, etc.

    public bool IsLockedOut { get; private set; }

    public void TriggerLockout()
    {
        if (IsLockedOut) return;
        StartCoroutine(LockoutRoutine());
    }

    private IEnumerator LockoutRoutine()
    {
        IsLockedOut = true;

        for (int i = 0; i < disableScripts.Length; i++)
            if (disableScripts[i] != null) disableScripts[i].enabled = false;

        yield return new WaitForSeconds(lockoutSeconds);

        for (int i = 0; i < disableScripts.Length; i++)
            if (disableScripts[i] != null) disableScripts[i].enabled = true;

        IsLockedOut = false;
    }
}
