using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Connects the shared combat pawn to PartyManager so V2 spells use the
/// active character's real AP and HP instead of a duplicate resource pool.
/// Keep this outside the SkillSystemV2 asmdef: PartyManager lives in the
/// project's legacy Assembly-CSharp assembly.
/// </summary>
[DisallowMultipleComponent]
public sealed class PartyManagerSpellAdapter : MonoBehaviour,
    ISpellResourceProvider,
    ISpellResourceReceiver,
    ISpellDamageReceiver,
    ISpellHealingReceiver
{
    [SerializeField]
    private bool applyActiveCharacterSkillCostMultiplier = true;

    private void Awake()
    {
        EnsureV2Identity();
    }

    public bool CanSpend(in SpellResourceCost cost)
    {
        if (cost.IsFree)
            return true;

        if (!IsActionPoints(cost.ResourceId) ||
            !TryGetActive(out PartyManager.CharacterState active))
        {
            return false;
        }

        return active.currentAP >= ResolveApAmount(cost.Amount, active);
    }

    public bool TrySpend(in SpellResourceCost cost)
    {
        if (cost.IsFree)
            return true;

        if (!IsActionPoints(cost.ResourceId) ||
            !TryGetActive(out PartyManager.CharacterState active))
        {
            return false;
        }

        int amount = ResolveApAmount(cost.Amount, active);
        if (active.currentAP < amount)
            return false;

        active.currentAP -= amount;
        return true;
    }

    public void Refund(in SpellResourceCost cost)
    {
        if (cost.IsFree ||
            !IsActionPoints(cost.ResourceId) ||
            !TryGetActive(out PartyManager.CharacterState active))
        {
            return;
        }

        int maximum = Mathf.Max(0, active.def.maxAP);
        active.currentAP = Mathf.Clamp(
            active.currentAP + ResolveApAmount(cost.Amount, active),
            0,
            maximum);
    }

    public bool TryChangeResource(
        in SpellResourceChangeRequest request,
        out SpellResourceChangeResult result)
    {
        result = default;

        if (!IsActionPoints(request.ResourceId) ||
            !TryGetActive(out PartyManager.CharacterState active))
        {
            return false;
        }

        int maximum = Mathf.Max(0, active.def.maxAP);
        float previous = active.currentAP;
        float requested;

        switch (request.Operation)
        {
            case SpellResourceOperation.Remove:
                requested = previous - request.Amount;
                break;
            case SpellResourceOperation.Set:
                requested = request.Amount;
                break;
            default:
                requested = previous + request.Amount;
                break;
        }

        int next = Mathf.RoundToInt(requested);
        if (!request.AllowOverflow)
            next = Mathf.Clamp(next, 0, maximum);
        else
            next = Mathf.Max(0, next);

        active.currentAP = next;
        result = new SpellResourceChangeResult(previous, next);
        return true;
    }

    public bool TryReceiveDamage(
        in SpellDamageRequest request,
        out SpellDamageResult result)
    {
        result = default;
        PartyManager party = PartyManager.Instance;
        if (party == null || !TryGetActive(out PartyManager.CharacterState active))
            return false;

        int before = active.currentHP;
        int requested = Mathf.CeilToInt(request.Amount);
        CombatPawn pawn = GetComponent<CombatPawn>();
        bool routedThroughPawn = pawn != null &&
                                 !request.IgnoreInvulnerability;
        if (routedThroughPawn)
            pawn.ApplyDamage(requested);
        else
            party.DamagePartyMember(party.activeIndex, requested);

        int applied = Mathf.Max(0, before - active.currentHP);
        result = new SpellDamageResult(request.Amount, applied, before > 0 && active.currentHP <= 0);

        if (applied > 0 && !routedThroughPawn)
        {
            DamageFlash2D damageFlash = GetComponent<DamageFlash2D>();
            if (damageFlash == null)
                damageFlash = gameObject.AddComponent<DamageFlash2D>();

            if (!damageFlash.HasConfiguredTargets)
            {
                damageFlash.ConfigureTargets(
                    DamageFlash2D.FindLikelyCharacterSprites(transform));
            }

            damageFlash.PlayFlash();
        }

        return applied > 0;
    }

    public bool TryReceiveHealing(
        in SpellHealingRequest request,
        out SpellHealingResult result)
    {
        result = default;
        PartyManager party = PartyManager.Instance;
        if (party == null || !TryGetActive(out PartyManager.CharacterState active))
            return false;

        bool wasDefeated = active.currentHP <= 0;
        int requested = Mathf.CeilToInt(request.Amount);
        int applied = wasDefeated && request.AllowRevive
            ? party.RevivePartyMember(party.activeIndex, requested)
            : party.HealPartyMember(party.activeIndex, requested);

        result = new SpellHealingResult(request.Amount, applied, wasDefeated && applied > 0);
        return applied > 0;
    }

    public int GetDisplayedCost(SpellDefinition spell)
    {
        if (spell == null || spell.ResourceCost.IsFree)
            return 0;

        return TryGetActive(out PartyManager.CharacterState active)
            ? ResolveApAmount(spell.ResourceCost.Amount, active)
            : Mathf.CeilToInt(spell.ResourceCost.Amount);
    }

    private int ResolveApAmount(
        float baseAmount,
        PartyManager.CharacterState active)
    {
        float multiplier = applyActiveCharacterSkillCostMultiplier && active != null
            ? Mathf.Max(1f, active.skillCostMultiplier)
            : 1f;
        float efficiency = SpellStatModifierUtility.Evaluate(
            gameObject,
            SpellActorStat.ActionPointCost,
            1f);
        return Mathf.Max(
            0,
            Mathf.CeilToInt(baseAmount * multiplier * efficiency));
    }

    private static bool IsActionPoints(string resourceId)
    {
        return string.Equals(
            resourceId,
            SpellResourceCost.ActionPoints,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetActive(out PartyManager.CharacterState active)
    {
        active = null;
        PartyManager party = PartyManager.Instance;
        if (party == null || party.party == null || party.party.Count == 0 ||
            party.activeIndex < 0 || party.activeIndex >= party.party.Count)
        {
            return false;
        }

        active = party.party[party.activeIndex];
        return active != null && active.def != null;
    }

    private void EnsureV2Identity()
    {
        CombatTeamMember team = GetComponent<CombatTeamMember>();
        if (team == null)
            team = gameObject.AddComponent<CombatTeamMember>();
        team.SetTeam(CombatTeam.Player);

        if (GetComponent<CombatTarget>() == null)
            gameObject.AddComponent<CombatTarget>();
    }
}
