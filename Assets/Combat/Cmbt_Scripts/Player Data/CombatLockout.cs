using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombatLockout : MonoBehaviour
{
    [SerializeField] private float lockoutSeconds = 0.35f;

    [Header("Disable During Lockout")]
    [SerializeField] private MonoBehaviour[] disableScripts; // TopDownMover, BasicAttack later, etc.

    private readonly HashSet<Object> externalOwners =
        new HashSet<Object>();

    private Coroutine timedRoutine;
    private bool timedLockActive;

    public bool IsLockedOut =>
        timedLockActive ||
        externalOwners.Count > 0;

    public void TriggerLockout()
    {
        TriggerLockout(lockoutSeconds);
    }

    public void TriggerLockout(float seconds)
    {
        if (timedRoutine != null)
            StopCoroutine(timedRoutine);

        timedRoutine = StartCoroutine(
            LockoutRoutine(
                Mathf.Max(0f, seconds)
            )
        );
    }

    /// <summary>
    /// Adds an interruptible external action lock. Eri uses this while the
    /// player is committed to receiving a heal.
    /// </summary>
    public void AcquireExternalLock(Object owner)
    {
        if (owner == null)
            return;

        externalOwners.Add(owner);
        ApplyControlState();
    }

    public void ReleaseExternalLock(Object owner)
    {
        if (owner == null)
            return;

        externalOwners.Remove(owner);
        ApplyControlState();
    }

    private IEnumerator LockoutRoutine(float seconds)
    {
        timedLockActive = true;
        ApplyControlState();

        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);

        timedLockActive = false;
        timedRoutine = null;
        ApplyControlState();
    }

    private void ApplyControlState()
    {
        bool controlsEnabled =
            !IsLockedOut;

        for (int i = 0; i < disableScripts.Length; i++)
        {
            if (disableScripts[i] != null)
            {
                disableScripts[i].enabled =
                    controlsEnabled;
            }
        }
    }

    private void OnDisable()
    {
        externalOwners.Clear();
        timedLockActive = false;

        if (timedRoutine != null)
        {
            StopCoroutine(timedRoutine);
            timedRoutine = null;
        }
    }
}
