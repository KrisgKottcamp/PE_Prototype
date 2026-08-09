using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellDamageRequest
    {
        public SpellEffectContext EffectContext { get; }
        public DamageTypeDefinition DamageType { get; }
        public float Amount { get; }
        public bool IgnoreInvulnerability { get; }

        public SpellDamageRequest(
            in SpellEffectContext effectContext,
            DamageTypeDefinition damageType,
            float amount,
            bool ignoreInvulnerability)
        {
            EffectContext = effectContext;
            DamageType = damageType;
            Amount = Mathf.Max(0f, amount);
            IgnoreInvulnerability = ignoreInvulnerability;
        }
    }

    public readonly struct SpellDamageResult
    {
        public float RequestedAmount { get; }
        public float AppliedAmount { get; }
        public bool WasLethal { get; }

        public SpellDamageResult(
            float requestedAmount,
            float appliedAmount,
            bool wasLethal)
        {
            RequestedAmount = Mathf.Max(0f, requestedAmount);
            AppliedAmount = Mathf.Max(0f, appliedAmount);
            WasLethal = wasLethal;
        }
    }

    public interface ISpellDamageReceiver
    {
        bool TryReceiveDamage(
            in SpellDamageRequest request,
            out SpellDamageResult result);
    }

    public readonly struct SpellHealingRequest
    {
        public SpellEffectContext EffectContext { get; }
        public float Amount { get; }
        public bool AllowRevive { get; }

        public SpellHealingRequest(
            in SpellEffectContext effectContext,
            float amount,
            bool allowRevive)
        {
            EffectContext = effectContext;
            Amount = Mathf.Max(0f, amount);
            AllowRevive = allowRevive;
        }
    }

    public readonly struct SpellHealingResult
    {
        public float RequestedAmount { get; }
        public float AppliedAmount { get; }
        public bool Revived { get; }

        public SpellHealingResult(
            float requestedAmount,
            float appliedAmount,
            bool revived)
        {
            RequestedAmount = Mathf.Max(0f, requestedAmount);
            AppliedAmount = Mathf.Max(0f, appliedAmount);
            Revived = revived;
        }
    }

    public interface ISpellHealingReceiver
    {
        bool TryReceiveHealing(
            in SpellHealingRequest request,
            out SpellHealingResult result);
    }

    public enum SpellImpulseMode
    {
        InstantVelocityChange,
        Force,
        Impulse
    }

    public readonly struct SpellImpulseRequest
    {
        public SpellEffectContext EffectContext { get; }
        public Vector2 Direction { get; }
        public float Magnitude { get; }
        public float Duration { get; }
        public SpellImpulseMode Mode { get; }

        public SpellImpulseRequest(
            in SpellEffectContext effectContext,
            Vector2 direction,
            float magnitude,
            float duration,
            SpellImpulseMode mode)
        {
            EffectContext = effectContext;
            Direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector2.zero;
            Magnitude = Mathf.Max(0f, magnitude);
            Duration = Mathf.Max(0f, duration);
            Mode = mode;
        }
    }

    public interface ISpellImpulseReceiver
    {
        bool TryReceiveImpulse(in SpellImpulseRequest request);
    }

    public enum SpellResourceOperation
    {
        Add,
        Remove,
        Set
    }

    public readonly struct SpellResourceChangeRequest
    {
        public SpellEffectContext EffectContext { get; }
        public GameplayResourceDefinition Resource { get; }
        public string ResourceId { get; }
        public SpellResourceOperation Operation { get; }
        public float Amount { get; }
        public bool AllowOverflow { get; }

        public SpellResourceChangeRequest(
            in SpellEffectContext effectContext,
            GameplayResourceDefinition resource,
            SpellResourceOperation operation,
            float amount,
            bool allowOverflow)
        {
            EffectContext = effectContext;
            Resource = resource;
            ResourceId = resource != null
                ? resource.ResourceId
                : SpellResourceCost.ActionPoints;
            Operation = operation;
            Amount = Mathf.Max(0f, amount);
            AllowOverflow = allowOverflow;
        }
    }

    public readonly struct SpellResourceChangeResult
    {
        public float PreviousValue { get; }
        public float CurrentValue { get; }
        public float AppliedDelta => CurrentValue - PreviousValue;

        public SpellResourceChangeResult(
            float previousValue,
            float currentValue)
        {
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }
    }

    public interface ISpellResourceReceiver
    {
        bool TryChangeResource(
            in SpellResourceChangeRequest request,
            out SpellResourceChangeResult result);
    }

    public interface ISpellResourcePickup
    {
        GameplayResourceDefinition Resource { get; }
        float AvailableAmount { get; }
        float Consume(float requestedAmount);
    }

    public readonly struct SpellStatusApplyRequest
    {
        public SpellEffectContext EffectContext { get; }
        public StatusDefinition Status { get; }
        public float Duration { get; }
        public int Stacks { get; }

        public SpellStatusApplyRequest(
            in SpellEffectContext effectContext,
            StatusDefinition status,
            float duration,
            int stacks)
        {
            EffectContext = effectContext;
            Status = status;
            Duration = Mathf.Max(0f, duration);
            Stacks = Mathf.Max(1, stacks);
        }
    }

    public readonly struct SpellStatusResult
    {
        public int PreviousStacks { get; }
        public int CurrentStacks { get; }
        public float RemainingDuration { get; }

        public SpellStatusResult(
            int previousStacks,
            int currentStacks,
            float remainingDuration)
        {
            PreviousStacks = Mathf.Max(0, previousStacks);
            CurrentStacks = Mathf.Max(0, currentStacks);
            RemainingDuration = Mathf.Max(0f, remainingDuration);
        }
    }

    public interface ISpellStatusReceiver
    {
        bool TryApplyStatus(
            in SpellStatusApplyRequest request,
            out SpellStatusResult result);

        bool TryRemoveStatus(
            StatusDefinition status,
            int stacksToRemove,
            out SpellStatusResult result);
    }

    public readonly struct SpellSpawnContext
    {
        public SpellEffectContext EffectContext { get; }
        public GameObject SpawnedObject { get; }

        public SpellSpawnContext(
            in SpellEffectContext effectContext,
            GameObject spawnedObject)
        {
            EffectContext = effectContext;
            SpawnedObject = spawnedObject;
        }
    }

    public interface ISpellSpawnReceiver
    {
        void InitializeSpawn(in SpellSpawnContext context);
    }
}
