using System.Collections;
using UnityEngine;

public class CombatPawn : MonoBehaviour
{
    [Header("Hit Settings")]
    [SerializeField] private float invulnSeconds = 0.6f;
    [SerializeField] private MonoBehaviour[] disableOnDeath;

    [Header("Hit Reaction Flow")]
    [Tooltip("Briefly prevents starting new basic attacks after accepted damage. Movement still works.")]
    [SerializeField] private bool applyActionLockoutOnDamage = true;

    [SerializeField, Min(0f)] private float actionLockoutOnDamage = 0.15f;

    [Tooltip("Cancels active basic attack coroutines/bursts when the player takes accepted damage.")]
    [SerializeField] private bool cancelCurrentAttackOnDamage = true;

    [SerializeField] private PlayerAttackCommitment attackCommitment;

    [Header("Momentum Damage Penalty")]
    [Tooltip(
        "How much Momentum would be lost from taking damage equal to 100% " +
        "of the active character's maximum HP."
    )]
    [SerializeField, Min(0f)]
    private float momentumLossPerFullHealthBar = 40f;

    [Tooltip(
        "Smallest Momentum loss from any accepted damage instance."
    )]
    [SerializeField, Min(0f)]
    private float minimumMomentumLossPerHit = 2f;

    [Tooltip(
        "Largest Momentum loss allowed from one accepted damage instance. " +
        "Set to 0 for no upper cap."
    )]
    [SerializeField, Min(0f)]
    private float maximumMomentumLossPerHit = 12f;

    [SerializeField]
    private bool logMomentumDamagePenalty = false;

    [Header("Feedback")]
    [SerializeField] private SpriteRenderer[] spritesToFlash;
    [SerializeField] private float flashInterval = 0.08f;

    public bool IsInvulnerable { get; private set; }

    private Coroutine flashRoutine;

    private void Awake()
    {
        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0 || IsInvulnerable)
            return;

        PartyManager partyManager = PartyManager.Instance;

        if (partyManager == null)
        {
            Debug.LogError(
                "CombatPawn: PartyManager missing."
            );

            return;
        }

        PartyManager.CharacterState active =
            partyManager.Active;

        if (active == null || active.def == null)
        {
            Debug.LogError(
                "CombatPawn: Active character or definition missing."
            );

            return;
        }

        int hpBefore = active.currentHP;

        active.currentHP = Mathf.Max(
            0,
            active.currentHP - amount
        );

        int actualDamage = Mathf.Max(
            0,
            hpBefore - active.currentHP
        );

        CombatManager.Instance?.NotifyPlayerDamaged(
            actualDamage
        );

        ApplyMomentumDamagePenalty(
            actualDamage,
            active.def.maxHP
        );

        ApplyHitReactionFlow(actualDamage);

        if (active.currentHP <= 0)
        {
            OnDeath();
            return;
        }

        StartCoroutine(InvulnWindow());
    }

    private void ApplyHitReactionFlow(int actualDamage)
    {
        if (actualDamage <= 0)
            return;

        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();

        if (applyActionLockoutOnDamage &&
            attackCommitment != null)
        {
            attackCommitment.ApplyActionLockout(
                actionLockoutOnDamage
            );
        }

        if (cancelCurrentAttackOnDamage)
        {
            SendMessage(
                "CancelCurrentAttack",
                SendMessageOptions.DontRequireReceiver
            );
        }
    }

    private void ApplyMomentumDamagePenalty(
        int actualDamage,
        int maximumHP)
    {
        if (actualDamage <= 0 || maximumHP <= 0)
            return;

        AttackMomentumManager manager =
            AttackMomentumManager.Instance;

        if (manager == null || !manager.IsRunning)
            return;

        float damageFraction =
            actualDamage / (float)maximumHP;

        float penalty =
            damageFraction *
            Mathf.Max(0f, momentumLossPerFullHealthBar);

        float minimum =
            Mathf.Max(0f, minimumMomentumLossPerHit);

        float maximum =
            Mathf.Max(0f, maximumMomentumLossPerHit);

        if (minimum > 0f)
            penalty = Mathf.Max(minimum, penalty);

        if (maximum > 0f)
            penalty = Mathf.Min(maximum, penalty);

        if (penalty <= 0f)
            return;

        manager.ApplyMomentumPenalty(penalty);

        if (logMomentumDamagePenalty)
        {
            Debug.Log(
                $"CombatPawn: Took {actualDamage}/{maximumHP} HP " +
                $"and lost {penalty:0.##} Momentum.",
                this
            );
        }
    }

    private IEnumerator InvulnWindow()
    {
        IsInvulnerable = true;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(
            FlashSprites()
        );

        yield return new WaitForSeconds(
            invulnSeconds
        );

        IsInvulnerable = false;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        SetSpritesVisible(true);
    }

    private IEnumerator FlashSprites()
    {
        bool visible = true;

        while (true)
        {
            visible = !visible;
            SetSpritesVisible(visible);

            yield return new WaitForSeconds(
                flashInterval
            );
        }
    }

    private void SetSpritesVisible(bool visible)
    {
        if (spritesToFlash == null)
            return;

        for (int i = 0; i < spritesToFlash.Length; i++)
        {
            if (spritesToFlash[i] != null)
                spritesToFlash[i].enabled = visible;
        }
    }

    private void OnDeath()
    {
        AttackMomentumManager.Instance?.
            ResetForCharacterDefeat();

        if (disableOnDeath != null)
        {
            for (int i = 0; i < disableOnDeath.Length; i++)
            {
                if (disableOnDeath[i] != null)
                    disableOnDeath[i].enabled = false;
            }
        }

        CombatManager.Instance?.NotifyPlayerDown();
    }
}
