using System;
using System.Collections;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>Opt-in by this branch's player bridge. Never changes authored assets.</summary>
[DisallowMultipleComponent]
public sealed class EriTurnCombat : MonoBehaviour
{
    public static EriTurnCombat Active { get; private set; }
    public float Remaining { get; private set; }
    public bool Timing { get; private set; }
    public float TimingProgress { get; private set; }
    public string Message { get; private set; } = "Attack to charge AP. Tab opens commands. C switches character.";
    public bool IsBusy => Timing || (bridge != null && bridge.IsTargeting) || runner.IsCasting;
    public IReadOnlyList<SpellDefinition> Skills => skills;
    private readonly List<SpellDefinition> skills = new List<SpellDefinition>();
    private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    private readonly Dictionary<int, List<SpellDefinition>> kits = new Dictionary<int, List<SpellDefinition>>();
    private PlayerSpellV2Bridge bridge;
    private SpellRunner runner;
    private int rosterIndex = -1;
    private int timingBonus;
    private bool timingResolved;
    private bool timingCancelled;
    private EriTurnHUD hud;
    public PartyManager Party => PartyManager.Instance;
    public PartyManager.CharacterState Member => Party != null && Party.party.Count > 0 ? Party.Active : null;

    private void Awake()
    {
        Active = this;
        bridge = GetComponent<PlayerSpellV2Bridge>();
        runner = GetComponent<SpellRunner>();
        runner.CommandCheck = CheckCast;
        runner.CommandAccepted = AcceptCast;
        if (Party != null)
            foreach (var member in Party.party) { member.exhaustedSegments = 0; member.currentAP = 0; }
        hud = gameObject.AddComponent<EriTurnHUD>();
        if (GetComponent<EriEnemyRhythm>() == null) gameObject.AddComponent<EriEnemyRhythm>();
        RefreshSkills();
    }

    private void Update()
    {
        Remaining = Mathf.Max(0, Remaining - Time.deltaTime);
        RefreshSkills();
        if (Member != null)
            Member.currentAP = Mathf.Clamp(Member.currentAP, 0,
                EriTurnRules.Capacity(Member.def.maxAP, Member.exhaustedSegments));
        if (Timing && Time.frameCount > timingStartFrame)
        {
            TimingProgress = Mathf.Clamp01(TimingProgress + Time.unscaledDeltaTime / 1.3f);
            if (Input.GetKeyDown(KeyCode.Escape)) { timingCancelled = true; timingResolved = true; }
            else if (Input.GetKeyDown(KeyCode.Space) || TimingProgress >= 1)
            {
                float error = Mathf.Abs(TimingProgress - 0.7f);
                timingBonus = error <= 0.08f ? 10 : error <= 0.18f ? 5 : 0;
                Message = timingBonus == 10 ? "Perfect! +10 AP after casting" : timingBonus == 5 ? "Good! +5 AP after casting" : "Cast ready";
                timingResolved = true;
            }
        }
    }

    private int timingStartFrame;
    public void StartTiming(Action<bool> finished)
    {
        if (Timing) return;
        StartCoroutine(RunTiming(finished));
    }
    private IEnumerator RunTiming(Action<bool> finished)
    {
        Timing = true; TimingProgress = 0; timingResolved = false; timingCancelled = false; timingBonus = 0;
        timingStartFrame = Time.frameCount;
        // The open skill menu owns the pause and movement lock throughout.
        while (!timingResolved) yield return null;
        Timing = false;
        finished(!timingCancelled);
    }

    public string Reason(SpellDefinition spell)
    {
        var m = Member;
        if (m == null || m.def == null) return "Party unavailable";
        if (m.currentHP <= 0) return "Character defeated";
        var d = spell != null ? spell.Delivery as EriPrototypeDelivery : null;
        if (d == null) return "Not a prototype command";
        if (d.Kind == EriCommandKind.Recover)
        {
            if (Remaining > 0.001f) return "Command recovering";
            return m.exhaustedSegments > 0 ? "" : "No exhausted segments";
        }
        return EriTurnRules.Unavailable(m.currentHP, m.currentAP, m.currentMP,
            m.exhaustedSegments, m.def.maxAP, d.Segments, d.MPCost, Remaining);
    }
    public string CostDisplay(SpellDefinition spell)
    {
        var d = spell.Delivery as EriPrototypeDelivery;
        return d == null ? "" : d.Kind == EriCommandKind.Recover ? "0 MP · restores 1 capacity" : $"{d.Segments} segment{(d.Segments == 1 ? "" : "s")} · {d.MPCost} MP";
    }
    private SpellCastFailure CheckCast(SpellDefinition spell, CastContext context)
    {
        if (context.ChainDepth > 0 || spell == GetComponent<SpellLoadout>().BasicAttack) return SpellCastFailure.None;
        if (Remaining > 0.001f) return SpellCastFailure.OnCooldown;
        return string.IsNullOrEmpty(Reason(spell)) ? SpellCastFailure.None : SpellCastFailure.InsufficientResources;
    }
    private void AcceptCast(SpellDefinition spell, CastContext context)
    {
        if (context.ChainDepth > 0 || spell == GetComponent<SpellLoadout>().BasicAttack) return;
        var m = Member;
        var d = (EriPrototypeDelivery)spell.Delivery;
        if (d.Kind == EriCommandKind.Recover) m.exhaustedSegments = EriTurnRules.RestoreSegment(m.exhaustedSegments);
        else
        {
            m.currentAP -= EriTurnRules.Cost(m.def.maxAP, d.Segments, m.exhaustedSegments);
            m.currentMP -= d.MPCost;
            m.exhaustedSegments += d.Segments;
        }
        foreach (var other in Party.party)
            if (other != m && other.currentHP > 0)
                other.exhaustedSegments = EriTurnRules.RestoreSegment(other.exhaustedSegments);
        Remaining = EriTurnRules.RecoverySeconds;
        m.currentAP = Mathf.Clamp(m.currentAP + (d.Kind == EriCommandKind.Recover ? 0 : timingBonus),
            0, EriTurnRules.Capacity(m.def.maxAP, m.exhaustedSegments));
        timingBonus = 0;
    }

    public void RefreshSkills()
    {
        if (Member == null || Member.def == null || rosterIndex == Party.activeIndex) return;
        rosterIndex = Party.activeIndex;
        skills.Clear();
        if (kits.TryGetValue(rosterIndex, out var kit)) { skills.AddRange(kit); return; }
        string who = Member.def.displayName.ToLowerInvariant();
        if (who.Contains("dominic"))
        {
            Add("Dread Field", "Mark enemies entering the circle with Fear for 28 seconds.", EriCommandKind.Mark, 1, 6);
            Add("Dread Pulse", "Fear damage around your chosen point. Exploits Fear marks.", EriCommandKind.Burst, 2, 10);
        }
        else if (who.Contains("imogen"))
        {
            Add("Gather", "Pull nearby enemies toward a chosen point.", EriCommandKind.Pull, 2, 8);
            Add("Black Hole", "A larger, sustained pull for lining up a party combination.", EriCommandKind.BlackHole, 4, 18);
        }
        else
        {
            if (who.Contains("audrey"))
                Add("Dread Field", "Mark enemies entering the circle with Fear for 28 seconds. Set up your Fear shots.", EriCommandKind.Mark, 1, 6);
            Add("Piercing Dread", "Aim through a group. Fear damage exploits marked targets.", EriCommandKind.Pierce, 2, 10);
            Add("Fear Shot", "A narrower, cheaper Fear shot for single targets.", EriCommandKind.Shot, 1, 5);
        }
        Add("Slash", "Strike enemies in front of you. Neutral damage, no Fear reaction.", EriCommandKind.Slash, 1, 3);
        Add("Recover", "Use a command to restore one exhausted segment, uncharged. Costs no MP.", EriCommandKind.Recover, 0, 0);
        kits[rosterIndex] = new List<SpellDefinition>(skills);
    }
    private void Add(string title, string description, EriCommandKind kind, int segments, int mp)
    {
        var spell = ScriptableObject.CreateInstance<SpellDefinition>();
        owned.Add(spell);
        spell.name = title;
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new SpellInit { displayName = title, description = description }), spell);
        spell.RegenerateStableId();
        var delivery = ScriptableObject.CreateInstance<EriPrototypeDelivery>();
        owned.Add(delivery);
        delivery.Kind = kind; delivery.Segments = segments; delivery.MPCost = mp;
        delivery.Targeting = kind == EriCommandKind.Recover
            ? ScriptableObject.CreateInstance<ImmediateTargetingDefinition>()
            : kind == EriCommandKind.Pierce || kind == EriCommandKind.Shot || kind == EriCommandKind.Slash
                ? (PlayerTargetingDefinition)ScriptableObject.CreateInstance<DirectionTargetingDefinition>()
                : ScriptableObject.CreateInstance<PointTargetingDefinition>();
        owned.Add(delivery.Targeting);
        JsonUtility.FromJsonOverwrite(kind == EriCommandKind.Pierce || kind == EriCommandKind.Shot
            ? "{\"maximumRange\":10,\"previewRadius\":0.65}"
            : kind == EriCommandKind.Slash
                ? "{\"maximumRange\":2.5,\"previewRadius\":1.2,\"previewConeAngle\":60}"
                : kind == EriCommandKind.BlackHole
                    ? "{\"maximumRange\":8,\"previewRadius\":4}"
                    : "{\"maximumRange\":8,\"previewRadius\":2.5}", delivery.Targeting);
        spell.ReplaceDelivery(new SpellDeliverySlot(delivery));
        skills.Add(spell);
    }
    [Serializable] private class SpellInit { public string displayName; public string description; public string category = "Prototype"; }
    public void UsePotion()
    {
        if (Member == null || Member.currentHP <= 0 || IsBusy || Time.timeScale <= 0 || Remaining > 0 || Party.mpPotions <= 0 || Member.currentMP >= EriTurnRules.MaxMP) return;
        Party.mpPotions--; Member.currentMP = Mathf.Min(EriTurnRules.MaxMP, Member.currentMP + 30);
        Remaining = EriTurnRules.RecoverySeconds;
        Message = "MP potion: +30 MP. One command used.";
    }
    public string CallEriReason => Member == null ? "Party unavailable" :
        EriTurnRules.Unavailable(Member.currentHP, Member.currentAP, Member.currentMP,
            Member.exhaustedSegments, Member.def.maxAP, 1, 5, Remaining);
    public bool TryCallEri(int targetIndex)
    {
        if (!string.IsNullOrEmpty(CallEriReason) || EriCombatCompanion.ActiveInstance == null) return false;
        if (!EriCombatCompanion.ActiveInstance.TryRequestHealing(transform, targetIndex)) return false;
        var m = Member;
        m.currentAP -= EriTurnRules.Cost(m.def.maxAP,1,m.exhaustedSegments); m.currentMP -= 5; m.exhaustedSegments++;
        foreach(var other in Party.party)
            if(other!=m && other.currentHP>0)other.exhaustedSegments=EriTurnRules.RestoreSegment(other.exhaustedSegments);
        Remaining=EriTurnRules.RecoverySeconds;
        Message="Called Eri · 1 segment + 5 MP · existing healing rules apply";
        return true;
    }
    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (runner != null) { runner.CommandCheck = null; runner.CommandAccepted = null; }
        foreach (var item in owned) if (item != null) Destroy(item);
    }
}
