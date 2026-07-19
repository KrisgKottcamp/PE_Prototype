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

    [Header("Player Damage Hitstop")]
    [Tooltip("Base hitstop when the active player accepts damage.")]
    [SerializeField] private HitstopSettings damageHitstop =
        HitstopSettings.Create(0.055f, 0.025f);

    [Tooltip("Scales hitstop duration by the amount of health lost. The time scale remains unchanged.")]
    [SerializeField] private bool scaleHitstopByDamage = true;

    [Tooltip("Damage at or above this value uses the maximum duration multiplier.")]
    [SerializeField, Min(1)] private int damageForMaximumHitstop = 30;

    [SerializeField, Range(0.1f, 1f)]
    private float minimumDamageDurationMultiplier = 0.75f;

    [SerializeField, Range(1f, 3f)]
    private float maximumDamageDurationMultiplier = 1.35f;

    [Header("Player Damage Camera Shake")]
    [Tooltip("Camera impact requested whenever the player accepts damage.")]
    [SerializeField] private CameraShakeSettings damageCameraShake =
        CameraShakeSettings.Create(0.28f, 0.14f);

    [Tooltip("Damage at or above this value uses the maximum camera shake strength multiplier.")]
    [SerializeField, Min(1)] private int damageForMaximumCameraShake = 30;

    [SerializeField, Range(0.1f, 1f)]
    private float minimumDamageShakeMultiplier = 0.70f;

    [SerializeField, Range(1f, 2f)]
    private float maximumDamageShakeMultiplier = 1.30f;

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
    [SerializeField, Min(0.01f)] private float flashInterval = 0.08f;

    public bool IsInvulnerable { get; private set; }

    private Coroutine flashRoutine;
    private DamageFlash2D damageFlash;

    private void Awake()
    {
        if (attackCommitment == null)
            attackCommitment = GetComponent<PlayerAttackCommitment>();

        if (GetComponent<APParticleCollector>() == null)
            gameObject.AddComponent<APParticleCollector>();

        damageFlash = GetComponent<DamageFlash2D>();

        if (damageFlash == null)
            damageFlash = gameObject.AddComponent<DamageFlash2D>();

        if (!damageFlash.HasConfiguredTargets)
        {
            damageFlash.ConfigureTargets(
                DamageFlash2D.FindLikelyCharacterSprites(transform)
            );
        }
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

        if (actualDamage > 0)
            damageFlash?.PlayFlash();

        RequestDamageHitstop(actualDamage);
        RequestDamageCameraShake(actualDamage);

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

    private void RequestDamageHitstop(int actualDamage)
    {
        if (actualDamage <= 0)
            return;

        float durationMultiplier = 1f;

        if (scaleHitstopByDamage)
        {
            float damageRatio = Mathf.Clamp01(
                actualDamage /
                (float)Mathf.Max(1, damageForMaximumHitstop)
            );

            durationMultiplier = Mathf.Lerp(
                minimumDamageDurationMultiplier,
                maximumDamageDurationMultiplier,
                damageRatio
            );
        }

        HitstopManager.Request(
            damageHitstop,
            durationMultiplier
        );
    }

    private void RequestDamageCameraShake(int actualDamage)
    {
        if (actualDamage <= 0)
            return;

        float damageRatio = Mathf.Clamp01(
            actualDamage /
            (float)Mathf.Max(1, damageForMaximumCameraShake)
        );

        float strengthMultiplier = Mathf.Lerp(
            minimumDamageShakeMultiplier,
            maximumDamageShakeMultiplier,
            damageRatio
        );

        CombatCameraShake.Request(
            damageCameraShake,
            transform.position,
            Random.insideUnitCircle,
            strengthMultiplier
        );
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

        // Keep the sprite visible while the white damage shader reads, then
        // continue the existing invulnerability blink if targets were assigned.
        yield return new WaitForSecondsRealtime(
            damageFlash != null
                ? damageFlash.FlashDuration
                : Mathf.Max(0.01f, flashInterval)
        );

        while (true)
        {
            visible = !visible;
            SetSpritesVisible(visible);

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.01f, flashInterval)
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
