using UnityEngine;

public enum EriHealingDeliveryResult
{
    Success,
    InvalidTarget,
    TargetChangedState,
    TargetAtFullHealth,
    NotEnoughHealingPoints,
    EriUnavailable
}

/// <summary>
/// Persistent owner of Eri's HP and finite healing-point pool.
/// PartyManager adds this component automatically so its state survives
/// exploration/combat scene changes with the rest of the party.
/// </summary>
[DisallowMultipleComponent]
public sealed class EriSupportManager : MonoBehaviour
{
    public const int SelfTargetIndex = -2;

    public static EriSupportManager Instance { get; private set; }

    public static event System.Action<int, int> HealingPointsChanged;
    public static event System.Action<int, int> EriHealthChanged;
    public static event System.Action EriDefeated;
    public static event System.Action EriRevived;

    [Header("Healing Point Progression")]
    [SerializeField, Min(1)]
    private int startingCapacity = 8;

    [SerializeField, Min(1)]
    private int maximumCapacity = 12;

    [SerializeField, Min(1)]
    private int unlockedCapacity = 8;

    [SerializeField, Min(0)]
    private int currentHealingPoints = 8;

    [Header("Healing Rules")]
    [Tooltip("One point restores exactly one third of a living character's maximum HP.")]
    [SerializeField, Range(0.01f, 1f)]
    private float partyHealFraction = 1f / 3f;

    [SerializeField, Min(1)]
    private int revivalPointCost = 2;

    [SerializeField, Range(0.01f, 1f)]
    private float partyReviveFraction = 1f / 3f;

    [Header("Eri")]
    [SerializeField, Min(1)]
    private int eriMaximumHP = 100;

    [SerializeField, Min(0)]
    private int eriCurrentHP = 100;

    [SerializeField, Range(0.01f, 1f)]
    private float eriSelfHealFraction = 1f / 3f;

    [SerializeField, Range(0.01f, 1f)]
    private float eriSelfReviveFraction = 0.25f;

    [Header("Emergency Party Recovery")]
    [SerializeField]
    private string emergencyReviveCharacterName = "Audrey";

    private bool initialized;

    public int CurrentHealingPoints =>
        Mathf.Clamp(
            currentHealingPoints,
            0,
            UnlockedCapacity
        );

    public int UnlockedCapacity =>
        Mathf.Clamp(
            unlockedCapacity,
            1,
            Mathf.Max(1, maximumCapacity)
        );

    public int MaximumCapacity =>
        Mathf.Max(1, maximumCapacity);

    public int EriCurrentHP =>
        Mathf.Clamp(
            eriCurrentHP,
            0,
            EriMaximumHP
        );

    public int EriMaximumHP =>
        Mathf.Max(1, eriMaximumHP);

    public bool IsEriDefeated =>
        EriCurrentHP <= 0;

    public int RevivalPointCost =>
        Mathf.Max(1, revivalPointCost);

    public string EmergencyReviveCharacterName =>
        emergencyReviveCharacterName;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        InitializeOnce();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void InitializeOnce()
    {
        if (initialized)
            return;

        startingCapacity =
            Mathf.Clamp(
                startingCapacity,
                1,
                Mathf.Max(1, maximumCapacity)
            );

        unlockedCapacity =
            Mathf.Clamp(
                unlockedCapacity <= 0
                    ? startingCapacity
                    : unlockedCapacity,
                startingCapacity,
                Mathf.Max(startingCapacity, maximumCapacity)
            );

        currentHealingPoints =
            Mathf.Clamp(
                currentHealingPoints,
                0,
                unlockedCapacity
            );

        eriMaximumHP =
            Mathf.Max(1, eriMaximumHP);

        eriCurrentHP =
            Mathf.Clamp(
                eriCurrentHP <= 0
                    ? eriMaximumHP
                    : eriCurrentHP,
                0,
                eriMaximumHP
            );

        initialized = true;
    }

    public int GetPartyTargetPointCost(int partyIndex)
    {
        if (partyIndex == SelfTargetIndex)
            return 1;

        PartyManager pm = PartyManager.Instance;

        if (!TryGetPartyTarget(
                pm,
                partyIndex,
                out PartyManager.CharacterState target))
        {
            return int.MaxValue;
        }

        return target.currentHP <= 0
            ? RevivalPointCost
            : 1;
    }

    public bool CanRequestPartyTarget(int partyIndex)
    {
        if (IsEriDefeated)
            return false;

        if (partyIndex == SelfTargetIndex)
        {
            return CurrentHealingPoints >= 1 &&
                EriCurrentHP < EriMaximumHP;
        }

        PartyManager pm = PartyManager.Instance;

        if (!TryGetPartyTarget(
                pm,
                partyIndex,
                out PartyManager.CharacterState target))
        {
            return false;
        }

        int cost =
            target.currentHP <= 0
                ? RevivalPointCost
                : 1;

        if (CurrentHealingPoints < cost)
            return false;

        if (target.currentHP <= 0)
            return true;

        return target.currentHP <
            Mathf.Max(1, target.def.maxHP);
    }

    public EriHealingDeliveryResult TryDeliverToPartyMember(
        int partyIndex,
        bool targetWasDownedWhenRequested)
    {
        if (IsEriDefeated)
            return EriHealingDeliveryResult.EriUnavailable;

        if (partyIndex == SelfTargetIndex)
        {
            if (EriCurrentHP >= EriMaximumHP)
                return EriHealingDeliveryResult.TargetAtFullHealth;

            if (CurrentHealingPoints < 1)
            {
                return EriHealingDeliveryResult.
                    NotEnoughHealingPoints;
            }

            return TryHealEriFromPool()
                ? EriHealingDeliveryResult.Success
                : EriHealingDeliveryResult.TargetAtFullHealth;
        }

        PartyManager pm = PartyManager.Instance;

        if (!TryGetPartyTarget(
                pm,
                partyIndex,
                out PartyManager.CharacterState target))
        {
            return EriHealingDeliveryResult.InvalidTarget;
        }

        bool targetIsDowned =
            target.currentHP <= 0;

        if (targetIsDowned !=
            targetWasDownedWhenRequested)
        {
            return EriHealingDeliveryResult.TargetChangedState;
        }

        if (targetIsDowned)
        {
            if (CurrentHealingPoints <
                RevivalPointCost)
            {
                return EriHealingDeliveryResult.NotEnoughHealingPoints;
            }

            int restoredHP =
                FractionToHP(
                    target.def.maxHP,
                    partyReviveFraction
                );

            int restored =
                pm.RevivePartyMember(
                    partyIndex,
                    restoredHP
                );

            if (restored <= 0)
                return EriHealingDeliveryResult.InvalidTarget;

            SpendHealingPoints(
                RevivalPointCost
            );

            return EriHealingDeliveryResult.Success;
        }

        int maximumHP =
            Mathf.Max(1, target.def.maxHP);

        if (target.currentHP >= maximumHP)
            return EriHealingDeliveryResult.TargetAtFullHealth;

        if (CurrentHealingPoints < 1)
            return EriHealingDeliveryResult.NotEnoughHealingPoints;

        int healed =
            pm.HealPartyMember(
                partyIndex,
                FractionToHP(
                    maximumHP,
                    partyHealFraction
                )
            );

        if (healed <= 0)
            return EriHealingDeliveryResult.TargetAtFullHealth;

        SpendHealingPoints(1);
        return EriHealingDeliveryResult.Success;
    }

    public int ApplyDamageToEri(int amount)
    {
        if (amount <= 0 || IsEriDefeated)
            return 0;

        int before = EriCurrentHP;

        bool incomingWouldDefeat =
            amount >= before;

        if (incomingWouldDefeat &&
            CurrentHealingPoints > 0)
        {
            int projectedAfterHeal =
                Mathf.Min(
                    EriMaximumHP,
                    before +
                    FractionToHP(
                        EriMaximumHP,
                        eriSelfHealFraction
                    )
                );

            // Do not waste the final resource on a pre-emptive heal that
            // cannot actually prevent defeat. Keep it for the delayed revive.
            if (projectedAfterHeal > amount)
            {
                TryHealEriFromPool();
                before = EriCurrentHP;
            }
        }

        eriCurrentHP =
            Mathf.Max(
                0,
                before - amount
            );

        int actualDamage =
            before - eriCurrentHP;

        if (actualDamage > 0)
        {
            EriHealthChanged?.Invoke(
                EriCurrentHP,
                EriMaximumHP
            );
        }

        if (eriCurrentHP <= 0)
            EriDefeated?.Invoke();

        return actualDamage;
    }

    public bool TryHealEriFromPool()
    {
        if (IsEriDefeated ||
            CurrentHealingPoints < 1 ||
            EriCurrentHP >= EriMaximumHP)
        {
            return false;
        }

        int before = EriCurrentHP;

        eriCurrentHP =
            Mathf.Min(
                EriMaximumHP,
                eriCurrentHP +
                FractionToHP(
                    EriMaximumHP,
                    eriSelfHealFraction
                )
            );

        if (eriCurrentHP <= before)
            return false;

        SpendHealingPoints(1);

        EriHealthChanged?.Invoke(
            EriCurrentHP,
            EriMaximumHP
        );

        return true;
    }

    public bool TryReviveEriFromPool()
    {
        if (!IsEriDefeated ||
            CurrentHealingPoints < 1)
        {
            return false;
        }

        eriCurrentHP =
            FractionToHP(
                EriMaximumHP,
                eriSelfReviveFraction
            );

        SpendHealingPoints(1);

        EriHealthChanged?.Invoke(
            EriCurrentHP,
            EriMaximumHP
        );

        EriRevived?.Invoke();
        return true;
    }

    public bool CanEmergencyRevive(out int partyIndex)
    {
        if (IsEriDefeated ||
            CurrentHealingPoints <
            RevivalPointCost)
        {
            partyIndex = -1;
            return false;
        }

        return TryGetEmergencyReviveTarget(
            out partyIndex
        );
    }

    public bool CanEventuallyEmergencyRevive(
        out int partyIndex)
    {
        if (!IsEriDefeated ||
            CurrentHealingPoints <
            RevivalPointCost + 1)
        {
            partyIndex = -1;
            return false;
        }

        return TryGetEmergencyReviveTarget(
            out partyIndex
        );
    }

    private bool TryGetEmergencyReviveTarget(
        out int partyIndex)
    {
        partyIndex = -1;

        PartyManager pm = PartyManager.Instance;

        if (pm == null)
            return false;

        partyIndex =
            pm.FindPartyIndexByDisplayName(
                emergencyReviveCharacterName
            );

        if (partyIndex < 0 ||
            partyIndex >= pm.party.Count)
        {
            return false;
        }

        PartyManager.CharacterState target =
            pm.party[partyIndex];

        return target != null &&
            target.currentHP <= 0;
    }

    public void RestoreAtCheckpoint()
    {
        PartyManager.Instance?.
            RestoreAllPartyHP();

        eriCurrentHP =
            EriMaximumHP;

        currentHealingPoints =
            UnlockedCapacity;

        EriHealthChanged?.Invoke(
            EriCurrentHP,
            EriMaximumHP
        );

        HealingPointsChanged?.Invoke(
            CurrentHealingPoints,
            UnlockedCapacity
        );
    }

    public bool UnlockOneCapacityPoint(
        bool refillNewPoint = true)
    {
        if (UnlockedCapacity >=
            MaximumCapacity)
        {
            return false;
        }

        unlockedCapacity =
            Mathf.Min(
                MaximumCapacity,
                UnlockedCapacity + 1
            );

        if (refillNewPoint)
        {
            currentHealingPoints =
                Mathf.Min(
                    UnlockedCapacity,
                    CurrentHealingPoints + 1
                );
        }

        HealingPointsChanged?.Invoke(
            CurrentHealingPoints,
            UnlockedCapacity
        );

        return true;
    }

    private void SpendHealingPoints(int amount)
    {
        if (amount <= 0)
            return;

        currentHealingPoints =
            Mathf.Max(
                0,
                CurrentHealingPoints - amount
            );

        HealingPointsChanged?.Invoke(
            CurrentHealingPoints,
            UnlockedCapacity
        );
    }

    private static int FractionToHP(
        int maximumHP,
        float fraction)
    {
        return Mathf.Max(
            1,
            Mathf.CeilToInt(
                Mathf.Max(1, maximumHP) *
                Mathf.Clamp01(fraction)
            )
        );
    }

    private static bool TryGetPartyTarget(
        PartyManager pm,
        int partyIndex,
        out PartyManager.CharacterState target)
    {
        target = null;

        if (pm == null ||
            pm.party == null ||
            partyIndex < 0 ||
            partyIndex >= pm.party.Count)
        {
            return false;
        }

        target =
            pm.party[partyIndex];

        return target != null &&
            target.def != null;
    }
}
