using System.Collections;
using UnityEngine;

public class CombatPawn : MonoBehaviour
{
    [Header("Hit Settings")]
    [SerializeField] private float invulnSeconds = 0.6f;
    [SerializeField] private MonoBehaviour[] disableOnDeath; // optional, movement etc.

    [Header("Feedback")]
    [SerializeField] private SpriteRenderer[] spritesToFlash;
    [SerializeField] private float flashInterval = 0.08f;

    public bool IsInvulnerable { get; private set; }

    private Coroutine flashRoutine;

    public void ApplyDamage(int amount)
    {
        if (amount <= 0) return;
        if (IsInvulnerable) return;

        var pm = PartyManager.Instance;
        if (pm == null)
        {
            Debug.LogError("CombatPawn: PartyManager missing.");
            return;
        }

        // Apply to active character HP (persists across scenes)
        var active = pm.Active;
        active.currentHP = Mathf.Max(0, active.currentHP - amount);

        if (active.currentHP <= 0)
        {
            OnDeath();
            return;
        }

        StartCoroutine(InvulnWindow());
    }

    private IEnumerator InvulnWindow()
    {
        IsInvulnerable = true;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashSprites());

        yield return new WaitForSeconds(invulnSeconds);

        IsInvulnerable = false;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        SetSpritesVisible(true);
    }

    private IEnumerator FlashSprites()
    {
        bool visible = true;
        while (true)
        {
            visible = !visible;
            SetSpritesVisible(visible);
            yield return new WaitForSeconds(flashInterval);
        }
    }

    private void SetSpritesVisible(bool visible)
    {
        if (spritesToFlash == null) return;
        for (int i = 0; i < spritesToFlash.Length; i++)
        {
            if (spritesToFlash[i] != null)
                spritesToFlash[i].enabled = visible;
        }
    }

    private void OnDeath()
    {
        // Disable movement or other scripts so you stop interacting
        if (disableOnDeath != null)
        {
            for (int i = 0; i < disableOnDeath.Length; i++)
                if (disableOnDeath[i] != null) disableOnDeath[i].enabled = false;
        }

        // Let CombatManager decide what defeat means
        CombatManager.Instance?.NotifyPlayerDown();
    }


}
