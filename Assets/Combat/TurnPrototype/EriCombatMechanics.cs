using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Encounter-local rewards and party buffs. Does not replace the existing turn/AP loop.</summary>
[DisallowMultipleComponent]
public sealed class EriCombatMechanics : MonoBehaviour
{
    public static EriCombatMechanics Active { get; private set; }
    public EriDefenseRules Rules;
    public bool AssignTestProfiles => Rules.AssignTestProfiles;
    public int DefaultArmor => Rules.DefaultArmor;
    public int DefaultShield => Rules.DefaultShield;
    public float ArmorAttackMultiplier => Rules.ArmorRewardMultiplier;
    public int AllOutHealthDamage => Rules.AllOutHealthDamage;
    public int AllOutDefenseDamage => Rules.AllOutDefenseDamage;
    public float SweepSeconds => Rules.SweepSeconds;
    public float BuffAttackMultiplier => Rules.BuffAttackMultiplier;
    public float BuffSpeedMultiplier => Rules.BuffSpeedMultiplier;
    public float BuffReceivedDamageMultiplier => Rules.BuffReceivedDamageMultiplier;
    public int ShieldActions { get; private set; }
    public bool ArmorBonusReady { get; private set; }
    public bool AllOutExecuting { get; private set; }
    public bool BlocksBasicInput => AllOutExecuting || PointerOverControl();
    private UnityEngine.EventSystems.PointerEventData attackPointer;
    private UnityEngine.EventSystems.EventSystem pointerSystem;
    private readonly List<UnityEngine.EventSystems.RaycastResult> pointerHits = new List<UnityEngine.EventSystems.RaycastResult>();
    private bool PointerOverControl()
    {
        var system = UnityEngine.EventSystems.EventSystem.current;
        if (system == null) return false;
        if (attackPointer == null || pointerSystem != system)
        {
            pointerSystem = system;
            attackPointer = new UnityEngine.EventSystems.PointerEventData(system);
        }
        attackPointer.Reset();
        attackPointer.position = Input.mousePosition;
        pointerHits.Clear();
        system.RaycastAll(attackPointer, pointerHits);
        foreach (var hit in pointerHits)
        {
            // Passive HUD graphics and world/physics raycasts must never swallow attacks.
            // Reserve only actual controls (including the all-out button) for UI input.
            if (!(hit.module is UnityEngine.UI.GraphicRaycaster) || hit.gameObject == null) continue;
            var control = hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>();
            if (control != null && control.IsActive()) return true;
        }
        return false;
    }
    public float DefenseDamageMultiplier { get; private set; } = 1f;
    public int CurrentBasicAttackId { get; private set; }
    private int nextAttackId, commandSerial, profileIndex;
    private float scanAt;
    private float allOutPreviousScale = 1f;
    private SpriteRenderer hiddenEriRenderer;
    private bool hiddenEriWasEnabled;
    private bool allOutSpent;
    private readonly List<EriEnemyDefenses> enemies = new List<EriEnemyDefenses>();
    private readonly Dictionary<int, Attack> attacks = new Dictionary<int, Attack>();
    private readonly Dictionary<int, Buff> buffs = new Dictionary<int, Buff>();
    private sealed class Attack { public float Power = 1f, Bonus = 1f, CreatedAt; public bool Landed; }
    private sealed class Buff { public int Turns, GrantedCommand; }
    private EriTurnCombat combat;
    private CombatSkillMenuController menu;
    public bool AllOutAvailable
    {
        get
        {
            if (AllOutExecuting || allOutSpent || enemies.Count == 0) return false;
            bool living = false;
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.Health.CurrentHP <= 0) continue;
                living = true;
                if (!enemy.IsKnockedDown) return false;
            }
            return living;
        }
    }
    private void Awake()
    {
        Active = this;
        if (Rules == null) Rules = EriDefenseRules.Default;
        combat = GetComponent<EriTurnCombat>();
        EriEnemyDefenses.AnyDefenseBroken += OnBreak;
    }
    private void Update()
    {
        if (Time.time >= scanAt)
        {
            scanAt = Time.time + 0.25f; ScanEnemies();
            if (attacks.Count > 256)
            {
                var stale = new List<int>();
                foreach (var pair in attacks) if (Time.time - pair.Value.CreatedAt > 60f) stale.Add(pair.Key);
                foreach (int id in stale) attacks.Remove(id);
            }
        }
    }
    public void ScanEnemies()
    {
        foreach (var health in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.InstanceID))
        {
            if (health.CurrentHP <= 0) continue;
            var defense = health.GetComponent<EriEnemyDefenses>();
            if (defense == null)
            {
                defense = EriEnemyDefenses.Ensure(health);
                // Deterministic mixed test roster, never overwrite an authored profile.
                if (AssignTestProfiles)
                {
                    int profile = profileIndex++ % 4;
                    defense.Configure(profile == 1 || profile == 3 ? 0 : DefaultArmor,
                        profile == 0 || profile == 3 ? 0 : DefaultShield);
                    defense.CanRaiseFearWard = profile == 1;
                }
            }
            if (!enemies.Contains(defense)) enemies.Add(defense);
        }
        enemies.RemoveAll(e => e == null || e.Health.CurrentHP <= 0);
    }
    private void OnBreak(EriEnemyDefenses enemy, bool armor, bool shield)
    {
        if (armor) ArmorBonusReady = true;
        if (shield) ShieldActions++;
    }
    public bool ConsumeShieldAction()
    {
        if (ShieldActions <= 0) return false;
        ShieldActions--; return true;
    }
    public void CommandStarted(int timingGrade)
    {
        commandSerial++;
        DefenseDamageMultiplier = timingGrade >= 2 ? 1.5f : timingGrade == 1 ? 1.25f : 1f;
    }
    public void CommandCompleted(int actor)
    {
        if (buffs.TryGetValue(actor, out var buff) && buff.GrantedCommand != commandSerial)
        {
            buff.Turns--;
            if (buff.Turns <= 0) buffs.Remove(actor);
        }
    }
    public int BuffTurns(int actor) => buffs.TryGetValue(actor, out var b) ? b.Turns : 0;
    public void BuffActor(int actor, int turns)
    {
        if (actor < 0) return;
        buffs[actor] = new Buff { Turns = Mathf.Max(turns, BuffTurns(actor)), GrantedCommand = commandSerial };
    }
    public void BuffParty(int turns)
    {
        var party = PartyManager.Instance;
        if (party == null) return;
        for (int i = 0; i < party.party.Count; i++) if (party.party[i].currentHP > 0) BuffActor(i, turns);
    }
    public void HealActor(int actor, int amount)
    {
        var party = PartyManager.Instance;
        if (party != null && actor >= 0 && actor < party.party.Count && party.party[actor].currentHP > 0)
            party.HealPartyMember(actor, amount);
    }
    public float MovementMultiplier => PartyManager.Instance != null && BuffTurns(PartyManager.Instance.activeIndex) > 0 ? BuffSpeedMultiplier : 1f;
    public int ResolveIncomingDamage(int amount, int actor) => Mathf.Max(1, Mathf.RoundToInt(amount * (BuffTurns(actor) > 0 ? BuffReceivedDamageMultiplier : 1f)));
    public int ResolveAttackDamage(int amount, int actor) => Mathf.RoundToInt(amount * (BuffTurns(actor) > 0 ? BuffAttackMultiplier : 1f));
    public int NextAttackId()
    {
        int actor = PartyManager.Instance != null ? PartyManager.Instance.activeIndex : -1;
        int id = ++nextAttackId;
        attacks[id] = new Attack { Power = BuffTurns(actor) > 0 ? BuffAttackMultiplier : 1f, CreatedAt = Time.time };
        return id;
    }
    public void BeginBasicAttack(Vector2 aim)
    {
        CurrentBasicAttackId = NextAttackId();
    }
    public int ApplySkillHit(EnemyHealth enemy, int baseDamage, int actor, bool magical, bool fear, float defenseMultiplier, int attackId)
    {
        if (enemy == null || enemy.CurrentHP <= 0 || baseDamage <= 0) return 0;
        if (!attacks.TryGetValue(attackId, out var attack))
            attacks[attackId] = attack = new Attack { Power = BuffTurns(actor) > 0 ? BuffAttackMultiplier : 1f, CreatedAt = Time.time };
        if (!attack.Landed)
        {
            attack.Landed = true;
            attack.Bonus = ArmorBonusReady ? ArmorAttackMultiplier : 1f;
            ArmorBonusReady = false;
        }
        var defense = enemy.GetComponent<EriEnemyDefenses>();
        if (defense == null) { ScanEnemies(); defense = EriEnemyDefenses.Ensure(enemy); }
        bool wasDown = defense.IsKnockedDown;
        int dealt = defense.ApplyHit(Mathf.RoundToInt(baseDamage * attack.Power), magical, fear, defenseMultiplier, attackId, attack.Bonus);
        // A new all-out setup starts only when a player attack deliberately wakes a survivor.
        if (wasDown && !defense.IsKnockedDown && enemy.CurrentHP > 0) allOutSpent = false;
        return dealt;
    }
    public int ApplyBasicHit(EnemyHealth enemy, int damage) => ApplyBasicHit(enemy, damage,
        PartyManager.Instance != null ? PartyManager.Instance.activeIndex : -1, CurrentBasicAttackId);
    public int ApplyBasicHit(EnemyHealth enemy, int damage, int actor, int attackId)
    {
        if (enemy == null || enemy.CurrentHP <= 0) return 0;
        var party = PartyManager.Instance;
        if (party != null && actor >= 0 && actor < party.party.Count &&
            party.party[actor].def.displayName.ToLowerInvariant().Contains("dominic"))
            enemy.GetComponent<ProjectEri.EnemyAI.V2.EnemySlowReceiverV2>()?.ApplySlow(this, 0.65f, 2f);
        var defense = enemy.GetComponent<EriEnemyDefenses>();
        if (defense == null) { ScanEnemies(); defense = EriEnemyDefenses.Ensure(enemy); }
        if (!EriDefenseMath.BasicCanDamage(defense.Armor, defense.Shield, defense.IsKnockedDown))
        {
            // Contact still awards AP through the existing attack scripts, but defense breaking belongs to skills.
            enemy.PlayHitFlash();
            return 0;
        }
        return ApplySkillHit(enemy, damage, actor, false, false, 1f, attackId == 0 ? NextAttackId() : attackId);
    }
    public void ClearProjectiles(Vector2 center, float radius, bool reflect, int actor, int attackId = 0) =>
        GetComponent<EriEnemyRhythm>()?.ClearProjectiles(center, radius, reflect, actor, attackId);
    public void PlaceCover(Vector2 point, float duration, float radius) => EriKitEffects.CreateCover(point, duration, radius);
    public bool TryAllOut()
    {
        ScanEnemies();
        if (menu == null) menu = FindFirstObjectByType<CombatSkillMenuController>();
        if (!AllOutAvailable || combat == null || combat.IsBusy || combat.Member == null || combat.Member.currentHP <= 0 ||
            Time.timeScale <= 0 || (menu != null && menu.IsOpen)) return false;
        StartCoroutine(AllOutSweep()); return true;
    }
    private IEnumerator AllOutSweep()
    {
        AllOutExecuting = true;
        HitstopManager.ReleaseForExternalTimeControl();
        allOutPreviousScale = Time.timeScale;
        Time.timeScale = 0f;
        SendMessage("CancelCurrentAttack", SendMessageOptions.DontRequireReceiver);
        var targets = new List<EriEnemyDefenses>(enemies);
        // Snapshot participants; the sweep is not a hit against downed enemies and never consumes Armor's attack reward.
        var visual = new GameObject("Eri All-Out Sweep");
        var line = visual.AddComponent<LineRenderer>();
        var shader = Shader.Find("Sprites/Default");
        var material = shader != null ? new Material(shader) : null;
        line.sharedMaterial = material; line.positionCount = 2; line.startWidth = line.endWidth = 0.18f;
        line.enabled = false;
        line.sortingLayerName = "VFX"; line.sortingOrder = 110;
        line.startColor = line.endColor = new Color(0.4f, 1f, 0.9f, 0.8f);
        var camera = Camera.main;
        var eri = EriCombatCompanion.ActiveInstance;
        Sprite eriSprite = null;
        SpriteRenderer sweepGhost = null;
        var cinematic = FindFirstObjectByType<EriAllOutView>();
        if (eri != null)
        {
            var original = eri.GetComponentInChildren<SpriteRenderer>();
            if (original != null)
            {
                eriSprite = original.sprite;
                hiddenEriRenderer = original;
                hiddenEriWasEnabled = original.enabled;
                original.enabled = false;
                sweepGhost = new GameObject("Eri sweep silhouette").AddComponent<SpriteRenderer>();
                sweepGhost.transform.SetParent(visual.transform, false); sweepGhost.sprite = original.sprite;
                sweepGhost.color = original.color; sweepGhost.flipX = original.flipX;
                sweepGhost.transform.localScale = original.transform.lossyScale;
                sweepGhost.sortingLayerName = "VFX"; sweepGhost.sortingOrder = 120;
                sweepGhost.enabled = false;
            }
        }
        if (cinematic != null) cinematic.BeginCinematic(eriSprite);
        float elapsed = 0;
        while (elapsed < SweepSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / SweepSeconds);
            cinematic?.SetCinematicProgress(progress);
            bool swiping = progress >= EriAllOutView.SwipeStart && camera != null;
            line.enabled = swiping;
            if (sweepGhost != null) sweepGhost.enabled = swiping;
            if (swiping)
            {
                float swipe = Mathf.InverseLerp(EriAllOutView.SwipeStart, 1f, progress);
                float x = Mathf.Lerp(-0.08f, 1.08f, Mathf.SmoothStep(0f, 1f, swipe));
                float depth = -camera.transform.position.z;
                visual.transform.position = camera.ViewportToWorldPoint(new Vector3(x, 0.5f, depth));
                line.SetPosition(0, camera.ViewportToWorldPoint(new Vector3(x, 0.05f, depth)));
                line.SetPosition(1, camera.ViewportToWorldPoint(new Vector3(x, 0.95f, depth)));
            }
            yield return null;
        }
        bool chain = true;
        foreach (var enemy in targets)
        {
            if (enemy == null || enemy.Health.CurrentHP <= 0) continue;
            bool freshBreak = enemy.ApplyAllOut(AllOutHealthDamage, AllOutDefenseDamage);
            if (enemy != null && enemy.Health.CurrentHP > 0 && !freshBreak) chain = false;
        }
        // A successful fresh break on every survivor permits one immediate repeat.
        // Otherwise everyone stays down until a player attack wakes one; no timer or cinematic may do it.
        allOutSpent = !chain;
        cinematic?.EndCinematic();
        RestoreEriSprite();
        Destroy(visual); if (material != null) Destroy(material);
        Time.timeScale = allOutPreviousScale;
        AllOutExecuting = false;
        HitstopManager.RequestSustained(HitstopSettings.Create(0.17f, 0.04f), 0f, 0.24f);
        CombatCameraShake.Request(CameraShakeSettings.Create(0.4f, 0.28f), transform.position, Vector2.right);
    }
    private void OnDestroy()
    {
        EriEnemyDefenses.AnyDefenseBroken -= OnBreak;
        RestoreEriSprite();
        if (AllOutExecuting)
        {
            FindFirstObjectByType<EriAllOutView>()?.EndCinematic();
            Time.timeScale = allOutPreviousScale;
        }
        if (Active == this) Active = null;
    }
    private void RestoreEriSprite()
    {
        if (hiddenEriRenderer != null) hiddenEriRenderer.enabled = hiddenEriWasEnabled;
        hiddenEriRenderer = null;
    }
}
