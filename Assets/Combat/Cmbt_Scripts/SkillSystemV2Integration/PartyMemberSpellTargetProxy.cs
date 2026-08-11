using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Gives one roster member a stable target object even while that character
/// is not represented by the shared combat pawn. Health, healing, AP, and
/// statuses can therefore address the chosen character instead of whichever
/// party member happens to be active later.
/// </summary>
[DisallowMultipleComponent]
public sealed class PartyMemberSpellTargetProxy : MonoBehaviour,
    ISpellTarget,
    ISpellTargetIdentity,
    ISpellTargetDisplay,
    ISpellDamageReceiver,
    ISpellHealingReceiver,
    ISpellResourceReceiver
{
    private int partyIndex = -1;
    private GameObject sharedCombatPawn;
    private bool allowDefeatedTarget;

    public GameObject TargetObject => gameObject;
    public bool IsTargetable
    {
        get
        {
            PartyManager.CharacterState state = ResolveState();
            return state != null && state.def != null &&
                   (allowDefeatedTarget || state.currentHP > 0);
        }
    }

    public string TargetDisplayName
    {
        get
        {
            PartyManager.CharacterState state = ResolveState();
            return state != null && state.def != null &&
                   !string.IsNullOrWhiteSpace(state.def.displayName)
                ? state.def.displayName
                : $"Party Member {partyIndex + 1}";
        }
    }

    public int PartyIndex => partyIndex;
    public int CurrentHP => ResolveState()?.currentHP ?? 0;
    public int MaximumHP => ResolveState()?.def != null
        ? Mathf.Max(0, ResolveState().def.maxHP)
        : 0;

    public void Configure(
        int rosterIndex,
        GameObject combatPawn,
        bool includeDefeated)
    {
        partyIndex = rosterIndex;
        sharedCombatPawn = combatPawn;
        allowDefeatedTarget = includeDefeated;
        name = $"Skill V2 Target - {TargetDisplayName}";
        EnsureComponents();
        RefreshPosition();
    }

    private void LateUpdate()
    {
        RefreshPosition();
    }

    public bool Represents(GameObject other)
    {
        PartyManager party = PartyManager.Instance;
        return other != null && sharedCombatPawn != null && party != null &&
               party.activeIndex == partyIndex &&
               (other == sharedCombatPawn ||
                other.transform.IsChildOf(sharedCombatPawn.transform) ||
                sharedCombatPawn.transform.IsChildOf(other.transform));
    }

    public bool TryReceiveDamage(
        in SpellDamageRequest request,
        out SpellDamageResult result)
    {
        result = default;
        PartyManager party = PartyManager.Instance;
        PartyManager.CharacterState state = ResolveState();
        if (party == null || state == null)
            return false;
        int before = state.currentHP;
        int requested = Mathf.CeilToInt(request.Amount);
        CombatPawn pawn = sharedCombatPawn != null
            ? sharedCombatPawn.GetComponent<CombatPawn>()
            : null;
        bool routedThroughPawn = party.activeIndex == partyIndex &&
                                 pawn != null &&
                                 !request.IgnoreInvulnerability;
        if (routedThroughPawn)
            pawn.ApplyDamage(requested);
        else
            party.DamagePartyMember(partyIndex, requested);

        int applied = Mathf.Max(0, before - state.currentHP);

        if (applied > 0 && party.activeIndex == partyIndex &&
            !routedThroughPawn && sharedCombatPawn != null)
        {
            DamageFlash2D damageFlash =
                sharedCombatPawn.GetComponent<DamageFlash2D>();
            if (damageFlash == null)
                damageFlash = sharedCombatPawn.AddComponent<DamageFlash2D>();
            if (!damageFlash.HasConfiguredTargets)
            {
                damageFlash.ConfigureTargets(
                    DamageFlash2D.FindLikelyCharacterSprites(
                        sharedCombatPawn.transform));
            }
            damageFlash.PlayFlash();
        }

        result = new SpellDamageResult(
            request.Amount,
            applied,
            before > 0 && state.currentHP <= 0);
        return applied > 0;
    }

    public bool TryReceiveHealing(
        in SpellHealingRequest request,
        out SpellHealingResult result)
    {
        result = default;
        PartyManager party = PartyManager.Instance;
        PartyManager.CharacterState state = ResolveState();
        if (party == null || state == null)
            return false;
        bool defeated = state.currentHP <= 0;
        int requested = Mathf.CeilToInt(request.Amount);
        int applied = defeated && request.AllowRevive
            ? party.RevivePartyMember(partyIndex, requested)
            : party.HealPartyMember(partyIndex, requested);
        result = new SpellHealingResult(
            request.Amount,
            applied,
            defeated && applied > 0);
        return applied > 0;
    }

    public bool TryChangeResource(
        in SpellResourceChangeRequest request,
        out SpellResourceChangeResult result)
    {
        result = default;
        PartyManager.CharacterState state = ResolveState();
        if (state == null || state.def == null ||
            !string.Equals(
                request.ResourceId,
                SpellResourceCost.ActionPoints,
                System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        float previous = state.currentAP;
        float requested = request.Operation == SpellResourceOperation.Remove
            ? previous - request.Amount
            : request.Operation == SpellResourceOperation.Set
                ? request.Amount
                : previous + request.Amount;
        int next = Mathf.RoundToInt(requested);
        next = request.AllowOverflow
            ? Mathf.Max(0, next)
            : Mathf.Clamp(next, 0, Mathf.Max(0, state.def.maxAP));
        state.currentAP = next;
        result = new SpellResourceChangeResult(previous, next);
        return !Mathf.Approximately(previous, next);
    }

    private PartyManager.CharacterState ResolveState()
    {
        PartyManager party = PartyManager.Instance;
        return party != null && party.party != null &&
               partyIndex >= 0 && partyIndex < party.party.Count
            ? party.party[partyIndex]
            : null;
    }

    private void RefreshPosition()
    {
        if (sharedCombatPawn != null)
            transform.position = sharedCombatPawn.transform.position;
    }

    private void EnsureComponents()
    {
        CombatTeamMember team = GetComponent<CombatTeamMember>();
        if (team == null)
            team = gameObject.AddComponent<CombatTeamMember>();
        team.SetTeam(CombatTeam.Player);
        if (GetComponent<StatusController>() == null)
            gameObject.AddComponent<StatusController>();
    }
}
