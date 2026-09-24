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
    public EriCharacterKitSettings KitSettings;
    private EriCombatMechanics mechanics;
    private int commandActor = -1;
    public bool Timing { get; private set; }
    public float TimingProgress { get; private set; }
    public string Message { get; private set; } = "Attack to charge AP. Tab opens commands. C switches character.";
    private bool IsPerformingCommand => Timing || (bridge != null && bridge.IsTargeting) || (runner != null && runner.IsCasting);
    public bool IsBusy => TurnEnding || IsPerformingCommand || (mechanics != null && mechanics.AllOutExecuting);
    public bool TurnEnding { get; private set; }
    private int lastActor = -1;
    private int acceptedFrame;
    private float nextGrazeAt;
    private CombatPawn pawn;
    public IReadOnlyList<SpellDefinition> Skills => skills;
    private readonly List<SpellDefinition> skills = new List<SpellDefinition>();
    private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    private readonly Dictionary<int, List<SpellDefinition>> kits = new Dictionary<int, List<SpellDefinition>>();
    private readonly EriSkillCooldowns skillCooldowns = new EriSkillCooldowns();
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
        if (KitSettings == null) { KitSettings = EriCharacterKitSettings.Load(); if (string.IsNullOrEmpty(KitSettings.name)) owned.Add(KitSettings); }
        mechanics = GetComponent<EriCombatMechanics>() ?? gameObject.AddComponent<EriCombatMechanics>();
        bridge = GetComponent<PlayerSpellV2Bridge>();
        runner = GetComponent<SpellRunner>();
        if (runner != null && GetComponent<SpellBuildUpControl2D>() == null)
            gameObject.AddComponent<SpellBuildUpControl2D>();
        pawn = GetComponent<CombatPawn>();
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
            Member.currentAP = TurnEnding ? 0 : Mathf.Clamp(Member.currentAP, 0,
                EriTurnRules.Capacity(Member.def.maxAP, Member.exhaustedSegments));
        if (Timing && Time.frameCount > timingStartFrame)
        {
            TimingProgress = Mathf.Clamp01(TimingProgress + Time.unscaledDeltaTime / 1.3f);
            if (Input.GetKeyDown(KeyCode.Escape)) { timingCancelled = true; timingResolved = true; }
            else if (Input.GetKeyDown(KeyCode.Space) || TimingProgress >= 1)
            {
                float error = Mathf.Abs(TimingProgress - 0.7f);
                timingBonus = error <= 0.08f ? 2 : error <= 0.18f ? 1 : 0;
                Message = timingBonus == 2 ? "Perfect! +50% defense damage · save 2 MP" : timingBonus == 1 ? "Good! +25% defense damage · save 1 MP" : "Cast ready";
                timingResolved = true;
            }
        }
    }

    private void LateUpdate()
    {
        // Acceptance happens before SpellRunner creates its active cast. Delay the
        // handoff until delivery and menu callbacks finish, without pausing movement.
        if (!TurnEnding || Time.frameCount <= acceptedFrame || Time.timeScale <= 0 || IsPerformingCommand) return;
        FindFirstObjectByType<CombatSkillMenuController>()?.CloseForTurnHandoff();
        TurnEnding = false;
        mechanics.CommandCompleted(commandActor);
        if (Member == null || Member.currentHP <= 0 || (pawn != null && pawn.IsDown)) return;
        if (mechanics.ShieldActions > 0)
        {
            Remaining = 0;
            Message = "Shield break · free second action · any target";
            return;
        }
        TrySwitchNext();
        Message = Member.def.displayName + "'s turn · one skill";
    }

    public bool IsWaiting(int index)
    {
        if (mechanics != null && mechanics.ShieldActions > 0) return false;
        int living = 0;
        if (Party != null) foreach (var member in Party.party) if (member.currentHP > 0) living++;
        return !EriTurnRules.CanTakeTurn(index, lastActor, living);
    }

    public bool TrySwitchNext()
    {
        if (Party == null || Party.party.Count == 0) return false;
        bool defeated = Member.currentHP <= 0;
        if (!defeated && (IsBusy || Time.timeScale <= 0)) return true;
        int start = Party.activeIndex;
        int next = -1;
        for (int step = 1; step <= Party.party.Count; step++)
        {
            int candidate = (start + step) % Party.party.Count;
            if (Party.party[candidate].currentHP > 0 && !IsWaiting(candidate)) { next = candidate; break; }
        }
        if (next < 0) return false;
        if (next == start) return true; // A one-person party can keep charging.
        if (defeated)
        {
            runner?.Interrupt("Character defeated");
            TurnEnding = false;
            timingCancelled = timingResolved = true;
        }
        Member.currentAP = 0;
        SendMessage("CancelCurrentAttack", SendMessageOptions.DontRequireReceiver);
        Party.activeIndex = next;
        Member.currentAP = 0;
        Member.skillCostMultiplier = 1f;
        GetComponent<CharacterDefinitionSpellLoadoutBinder>()?.RefreshNow();
        GetComponent<CombatPawnVisuals>()?.Refresh();
        RefreshSkills();
        return true;
    }

    private void EndTurn(EriPrototypeDelivery usedSkill = null)
    {
        commandActor = Party.activeIndex;
        lastActor = Party.activeIndex;
        skillCooldowns.CompleteTurn(commandActor, usedSkill != null ? usedSkill.Kind : (EriCommandKind?)null,
            usedSkill != null ? usedSkill.CooldownTurns : 0);
        Member.currentAP = 0;
        foreach (var other in Party.party)
            if (other != Member && other.currentHP > 0)
                other.exhaustedSegments = EriTurnRules.RestoreSegment(other.exhaustedSegments);
        Remaining = EriTurnRules.RecoverySeconds;
        acceptedFrame = Time.frameCount;
        TurnEnding = true;
    }

    public int TryAwardGraze()
    {
        if (Member == null || Member.currentHP <= 0 || IsBusy || Time.timeScale <= 0 ||
            Time.time < nextGrazeAt || (pawn != null && (pawn.IsDown || pawn.IsInvulnerable))) return 0;
        int gained = Party.AddAPToActive(Mathf.Max(1, Mathf.RoundToInt(Member.def.maxAP * 0.03f)));
        if (gained > 0) { nextGrazeAt = Time.time + 0.2f; Message = "Close dodge · +" + gained + " AP"; }
        return gained;
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
        // The open skill menu owns slow motion and the movement lock throughout.
        while (!timingResolved) yield return null;
        Timing = false;
        finished(!timingCancelled);
    }

    public string Reason(SpellDefinition spell)
    {
        var m = Member;
        if (m == null || m.def == null) return "Party unavailable";
        if (m.currentHP <= 0) return "Character defeated";
        if (mechanics.AllOutExecuting) return "Eri is attacking";
        if (TurnEnding || IsWaiting(Party.activeIndex)) return "Turn spent — another character must act";
        var d = spell != null ? spell.Delivery as EriPrototypeDelivery : null;
        if (d == null) return "Not a prototype command";
        int cooldown = CooldownTurnsRemaining(spell);
        if (cooldown > 0) return cooldown == 1 ? "Skill cooling down · use another turn" : $"Skill cooling down · {cooldown} turns";
        if (mechanics.ShieldActions > 0 && d.Kind != EriCommandKind.Recover) return "";
        if (d.Kind == EriCommandKind.Recover)
        {
            if (Remaining > 0.001f) return "Command recovering";
            return m.exhaustedSegments > 0 ? "" : "No exhausted segments";
        }
        return EriTurnRules.Unavailable(m.currentHP, m.currentAP, m.currentMP,
            m.exhaustedSegments, m.def.maxAP, d.Segments, d.MPCost, Remaining);
    }
    public int CooldownTurnsRemaining(SpellDefinition spell)
    {
        var delivery = spell != null ? spell.Delivery as EriPrototypeDelivery : null;
        if (delivery == null || delivery.Kind == EriCommandKind.Recover || Party == null) return 0;
        return skillCooldowns.Remaining(Party.activeIndex, delivery.Kind);
    }
    public string CostDisplay(SpellDefinition spell)
    {
        var d = spell.Delivery as EriPrototypeDelivery;
        if (d != null && d.Kind != EriCommandKind.Recover && mechanics.ShieldActions > 0) return "Shield break · free action";
        if (d == null) return "";
        if (d.Kind == EriCommandKind.Recover) return "0 MP · restores all capacity · ends turn";
        string windUp = d.WindUpSeconds > 0 ? $" · {d.WindUpSeconds:0.##}s vulnerable wind-up" : "";
        return $"{d.Segments} segment{(d.Segments == 1 ? "" : "s")} · {d.MPCost} MP{windUp}";
    }
    private SpellCastFailure CheckCast(SpellDefinition spell, CastContext context)
    {
        if (context.ChainDepth > 0) return SpellCastFailure.None;
        if (TurnEnding || mechanics.AllOutExecuting) return SpellCastFailure.RunnerBusy;
        if (spell == GetComponent<SpellLoadout>().BasicAttack) return SpellCastFailure.None;
        if (CooldownTurnsRemaining(spell) > 0) return SpellCastFailure.OnCooldown;
        if (Remaining > 0.001f && mechanics.ShieldActions <= 0) return SpellCastFailure.OnCooldown;
        return string.IsNullOrEmpty(Reason(spell)) ? SpellCastFailure.None : SpellCastFailure.InsufficientResources;
    }
    private void AcceptCast(SpellDefinition spell, CastContext context)
    {
        if (context.ChainDepth > 0 || spell == GetComponent<SpellLoadout>().BasicAttack) return;
        var m = Member;
        var d = (EriPrototypeDelivery)spell.Delivery;
        mechanics.CommandStarted(timingBonus);
        if (d.WindUpSeconds > 0)
            Message = spell.DisplayName + " charging · movement locked · vulnerable";
        if (d.Kind == EriCommandKind.Recover) m.exhaustedSegments = 0;
        else if (!mechanics.ConsumeShieldAction())
        {
            m.currentAP -= EriTurnRules.Cost(m.def.maxAP, d.Segments, m.exhaustedSegments);
            m.currentMP -= d.MPCost > 0 ? Mathf.Max(1, d.MPCost - timingBonus) : 0;
            m.exhaustedSegments += d.Segments;
        }
        timingBonus = 0;
        EndTurn(d);
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
            AddKit("Whip Slash", "Physical · single target. Applies Off-balance for heavy Armor damage.", EriCommandKind.WhipSlash);
            AddKit("Mark Shot", "Mark · piercing shot applies Fear weakness; no direct damage.", EriCommandKind.MarkShot);
            AddKit("Dispel", "Support · removes temporary elemental resistance in the chosen area.", EriCommandKind.Dispel);
            AddKit("Oil Spill", "Setup · Fear Shot or Dread Snipe ignites this oil for magical area damage.", EriCommandKind.OilSpill);
        }
        else if (who.Contains("imogen"))
        {
            AddKit("Projectile Reflect", "Fear magic · briefly catch and return nearby projectiles.", EriCommandKind.Reflect);
            AddKit("Place Cover", "Support · place a small barrier that blocks enemy bullets.", EriCommandKind.Cover);
            AddKit("Inspire Party", "Support · speed, attack and defense for each member's next turn.", EriCommandKind.Inspire);
            AddKit("Silence", "Support · remove all nearby enemy projectiles without dealing damage.", EriCommandKind.Silence);
        }
        else if (who.Contains("phil"))
        {
            AddKit("Dread Snipe", "Fear magic · heavy single-target projectile. Ignites oil.", EriCommandKind.Snipe);
            AddKit("Panic Fan", "Fear magic · quick one-segment fan; one damage hit per enemy.", EriCommandKind.Fan);
            AddKit("Grenade", "Physical · throw a grenade that explodes in an area.", EriCommandKind.Grenade);
            AddKit("Heal Self", "Support · restore your own Health.", EriCommandKind.HealSelf);
        }
        else
        {
            AddKit("Slash", "Physical · inexpensive swipe in front of you.", EriCommandKind.Slash);
            AddKit("Dash Slash", "Physical · move forward, striking enemies along your path.", EriCommandKind.DashSlash);
            AddKit("Fear Shot", "Fear magic · single projectile. Ignites oil.", EriCommandKind.Shot);
            AddKit("Motivate Self", "Support · speed, attack and defense for your next two turns.", EriCommandKind.Motivate);
        }
        Add("Recover", "Restore all exhausted capacity, uncharged, and end your turn. Costs no MP.", EriCommandKind.Recover, 0, 0);
        kits[rosterIndex] = new List<SpellDefinition>(skills);
    }
    private void AddKit(string title, string description, EriCommandKind kind)
    {
        var tuning = KitSettings.Get(kind);
        Add(title, description, kind, tuning.Segments, tuning.MPCost);
        var spell = skills[skills.Count - 1];
        var delivery = (EriPrototypeDelivery)spell.Delivery;
        delivery.CooldownTurns = Mathf.Max(1, tuning.CooldownTurns);
        delivery.Damage = tuning.Damage; delivery.Range = tuning.Range;
        delivery.Radius = tuning.Radius; delivery.Duration = tuning.Duration;
        delivery.WindUpSeconds = Mathf.Max(0, tuning.WindUpSeconds);
        if (delivery.WindUpSeconds > 0)
        {
            string seconds = delivery.WindUpSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonUtility.FromJsonOverwrite("{\"timing\":{\"buildUpDuration\":" + seconds +
                ",\"timeMode\":0,\"buildUpControl\":{\"blockPlayerMovement\":true,\"blockPlayerBasicAttacks\":true," +
                "\"blockPlayerSkillUsage\":true,\"showPowerUpParticles\":true,\"particleColor\":{\"r\":1,\"g\":0.62,\"b\":0.22,\"a\":0.95}," +
                "\"particlesPerSecond\":22,\"particleSpawnRadius\":1,\"particleInwardSpeed\":2.8,\"particleSize\":0.065}}}", spell);
        }
        JsonUtility.FromJsonOverwrite("{\"maximumRange\":" + tuning.Range.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"previewRadius\":" + tuning.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", delivery.Targeting);
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
        bool immediate = kind == EriCommandKind.Recover || kind == EriCommandKind.Motivate || kind == EriCommandKind.Inspire ||
            kind == EriCommandKind.HealSelf || kind == EriCommandKind.Reflect || kind == EriCommandKind.Silence;
        bool directional = kind == EriCommandKind.Pierce || kind == EriCommandKind.Shot || kind == EriCommandKind.Slash ||
            kind == EriCommandKind.DashSlash || kind == EriCommandKind.Snipe || kind == EriCommandKind.Fan ||
            kind == EriCommandKind.WhipSlash || kind == EriCommandKind.MarkShot;
        delivery.Targeting = immediate
            ? ScriptableObject.CreateInstance<ImmediateTargetingDefinition>()
            : directional
                ? (PlayerTargetingDefinition)ScriptableObject.CreateInstance<DirectionTargetingDefinition>()
                : ScriptableObject.CreateInstance<PointTargetingDefinition>();
        owned.Add(delivery.Targeting);
        // The command menu owns the player's chosen slowdown; targeting must not
        // silently cap a faster menu setting at the targeting asset's default 0.15.
        JsonUtility.FromJsonOverwrite("{\"aimTimeScale\":1}", delivery.Targeting);
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
        if (mechanics.AllOutExecuting) return;
        if (Member == null || Member.currentHP <= 0 || IsBusy || IsWaiting(Party.activeIndex) || Time.timeScale <= 0 || Remaining > 0 || Party.mpPotions <= 0 || Member.currentMP >= EriTurnRules.MaxMP) return;
        Party.mpPotions--; Member.currentMP = Mathf.Min(EriTurnRules.MaxMP, Member.currentMP + 30);
        mechanics.CommandStarted(0);
        EndTurn();
        Message = "MP potion: +30 MP. One command used.";
    }
    public string CallEriReason => Member == null ? "Party unavailable" :
        TurnEnding || IsWaiting(Party.activeIndex) ? "Turn spent — another character must act" :
        EriTurnRules.Unavailable(Member.currentHP, Member.currentAP, Member.currentMP,
            Member.exhaustedSegments, Member.def.maxAP, 1, 5, Remaining);
    public bool TryCallEri(int targetIndex)
    {
        if (mechanics.AllOutExecuting) return false;
        if (!string.IsNullOrEmpty(CallEriReason) || EriCombatCompanion.ActiveInstance == null) return false;
        if (!EriCombatCompanion.ActiveInstance.TryRequestHealing(transform, targetIndex)) return false;
        var m = Member;
        mechanics.CommandStarted(0);
        m.currentAP -= EriTurnRules.Cost(m.def.maxAP,1,m.exhaustedSegments); m.currentMP -= 5; m.exhaustedSegments++;
        EndTurn();
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
