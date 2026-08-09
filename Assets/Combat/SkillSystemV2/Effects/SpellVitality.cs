using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellVitality : MonoBehaviour,
        ISpellDamageReceiver,
        ISpellHealingReceiver
    {
        [SerializeField, Min(0.01f)]
        private float maximumHealth = 100f;

        [SerializeField, Min(0f)]
        private float currentHealth = 100f;

        [SerializeField]
        private bool invulnerable;

        public event Action<float, float> HealthChanged;
        public event Action<SpellDamageRequest> Damaged;
        public event Action<SpellHealingRequest> Healed;
        public event Action Died;
        public event Action Revived;

        public float MaximumHealth => Mathf.Max(0.01f, maximumHealth);
        public float CurrentHealth => Mathf.Clamp(
            currentHealth,
            0f,
            MaximumHealth);
        public float HealthFraction => CurrentHealth / MaximumHealth;
        public bool IsAlive => CurrentHealth > 0f;
        public bool IsInvulnerable => invulnerable;

        private void Awake()
        {
            currentHealth = Mathf.Clamp(
                currentHealth,
                0f,
                MaximumHealth);
        }

        public void SetInvulnerable(bool value)
        {
            invulnerable = value;
        }

        public bool TryReceiveDamage(
            in SpellDamageRequest request,
            out SpellDamageResult result)
        {
            float before = CurrentHealth;
            if (before <= 0f || request.Amount <= 0f ||
                (invulnerable && !request.IgnoreInvulnerability))
            {
                result = new SpellDamageResult(request.Amount, 0f, false);
                return false;
            }

            float applied = Mathf.Min(before, request.Amount);
            currentHealth = before - applied;
            bool lethal = currentHealth <= 0f;
            result = new SpellDamageResult(request.Amount, applied, lethal);
            Damaged?.Invoke(request);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);

            if (lethal)
                Died?.Invoke();

            return applied > 0f;
        }

        public bool TryReceiveHealing(
            in SpellHealingRequest request,
            out SpellHealingResult result)
        {
            float before = CurrentHealth;
            bool wasDead = before <= 0f;

            if (request.Amount <= 0f ||
                (wasDead && !request.AllowRevive) ||
                before >= MaximumHealth)
            {
                result = new SpellHealingResult(
                    request.Amount,
                    0f,
                    false);
                return false;
            }

            currentHealth = Mathf.Min(
                MaximumHealth,
                before + request.Amount);
            float applied = currentHealth - before;
            bool revived = wasDead && currentHealth > 0f;
            result = new SpellHealingResult(
                request.Amount,
                applied,
                revived);
            Healed?.Invoke(request);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);

            if (revived)
                Revived?.Invoke();

            return applied > 0f;
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(0.01f, maximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maximumHealth);
        }
    }
}
