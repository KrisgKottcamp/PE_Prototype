using System;
using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Player-facing bridge between Project Eri's existing combat input/menu and
/// SkillSystemV2. Aim comes from the authoritative AimTracker so the reticle,
/// camera lead, previews, and final cast all agree.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpellRunner))]
[RequireComponent(typeof(SpellLoadout))]
[RequireComponent(typeof(PlayerSpellTargetingController))]
[RequireComponent(typeof(TargetingTimeScaleController))]
[RequireComponent(typeof(TargetingPreviewRenderer2D))]
[RequireComponent(typeof(PartyManagerSpellAdapter))]
[RequireComponent(typeof(PlayerSpellCastFeedback2D))]
[RequireComponent(typeof(CharacterDefinitionSpellLoadoutBinder))]
[RequireComponent(typeof(PlayerSpellTargetMenuController))]
public sealed class PlayerSpellV2Bridge : MonoBehaviour
{
    [Header("Runtime References")]
    [SerializeField] private AimTracker aimTracker;
    [SerializeField] private SpellRunner spellRunner;
    [SerializeField] private SpellLoadout spellLoadout;
    [SerializeField] private PlayerSpellTargetingController targetingController;
    [SerializeField] private PartyManagerSpellAdapter partyAdapter;
    [SerializeField] private PlayerSpellTargetMenuController targetMenu;

    [Header("Target Selection")]
    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField, Min(1)] private int overlapBufferSize = 16;

    [Header("Aim Confirmation")]
    [SerializeField] private KeyCode confirmKey = KeyCode.Space;
    [SerializeField] private bool confirmWithLeftClick = true;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private bool cancelWithRightClick = true;

    private Collider2D[] overlapBuffer;

    public event Action<SpellDefinition> SpellTargetingStarted;
    public event Action<SpellDefinition> SpellConfirmed;
    public event Action<SpellDefinition> SpellCancelled;
    public event Action<SpellDefinition, SpellCastFailure> SpellRejected;

    public SpellLoadout Loadout => spellLoadout;
    public bool IsTargeting => targetingController != null && targetingController.IsTargeting;
    public int SkillCount => spellLoadout != null ? spellLoadout.EquippedSkills.Count : 0;

    private void Awake()
    {
        EnsureFeedbackComponents();
        ResolveReferences();
        EnsureBuffer();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (targetingController == null)
            return;

        targetingController.TargetingConfirmed += HandleConfirmed;
        targetingController.TargetingCancelled += HandleCancelled;
        targetingController.CastRejected += HandleRejected;
    }

    private void OnDisable()
    {
        if (targetingController == null)
            return;

        targetingController.TargetingConfirmed -= HandleConfirmed;
        targetingController.TargetingCancelled -= HandleCancelled;
        targetingController.CastRejected -= HandleRejected;
        targetMenu?.Close();
    }

    private void Update()
    {
        if (!IsTargeting)
            return;

        MenuSelectTargetingDefinition menuDefinition =
            targetingController.ActiveTargetingDefinition as
                MenuSelectTargetingDefinition;
        if (menuDefinition != null)
        {
            targetMenu?.OpenOrRefresh(menuDefinition);
            targetMenu?.HandleNavigation();
            GameObject selected = targetMenu != null
                ? targetMenu.SelectedTarget
                : null;
            Vector2 point = selected != null
                ? selected.transform.position
                : transform.position;
            targetingController.UpdateAim(point, selected);
        }
        else
        {
            targetMenu?.Close();
            if (aimTracker == null)
                return;
            aimTracker.RefreshAimImmediately();
            Vector2 pointer = aimTracker.AimWorldPosition;
            targetingController.UpdateAim(
                pointer,
                ResolveSelectedTarget(pointer));
        }

        bool beganThisFrame = targetingController.BeganOnFrame == Time.frameCount;
        bool confirm = Input.GetKeyDown(confirmKey) ||
                       (menuDefinition == null && confirmWithLeftClick &&
                        Input.GetMouseButtonDown(0)) ||
                       (targetMenu != null &&
                        targetMenu.ConsumeConfirmRequest());
        bool cancel = Input.GetKeyDown(cancelKey) ||
                      (cancelWithRightClick && Input.GetMouseButtonDown(1));

        if (!beganThisFrame && confirm)
            targetingController.ConfirmTargeting(out _, out _);
        else if (cancel)
            targetingController.CancelTargeting();
    }

    public SpellDefinition GetSkill(int index)
    {
        return spellLoadout != null ? spellLoadout.GetSkill(index) : null;
    }

    public bool CanUse(SpellDefinition spell, out SpellCastFailure failure)
    {
        failure = SpellCastFailure.None;
        if (spell == null)
        {
            failure = SpellCastFailure.MissingSpell;
            return false;
        }

        if (spellRunner == null || spellRunner.IsCasting)
        {
            failure = SpellCastFailure.RunnerBusy;
            return false;
        }

        if (SpellBuildUpControl2D.IsSkillUsageBlocked(gameObject))
        {
            failure = SpellCastFailure.RunnerBusy;
            return false;
        }

        if (spellRunner.IsOnCooldown(spell))
        {
            failure = SpellCastFailure.OnCooldown;
            return false;
        }

        var issues = new List<SpellValidationIssue>();
        spell.CollectValidationIssues(issues);
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Severity == SpellValidationSeverity.Error)
            {
                failure = SpellCastFailure.InvalidDefinition;
                return false;
            }
        }

        if (!spell.ResourceCost.IsFree &&
            (partyAdapter == null || !partyAdapter.CanSpend(spell.ResourceCost)))
        {
            failure = SpellCastFailure.InsufficientResources;
            return false;
        }

        return true;
    }

    public bool BeginSpell(SpellDefinition spell, out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (!CanUse(spell, out SpellCastFailure castFailure))
        {
            rejectionReason = castFailure.ToString();
            SpellRejected?.Invoke(spell, castFailure);
            return false;
        }

        // Targeting is a sustained time owner. End a short hitstop before it
        // captures a baseline so a 0.01-0.08 hit scale can never be restored
        // as normal gameplay speed after targeting ends.
        HitstopManager.ReleaseForExternalTimeControl();

        if (!targetingController.BeginTargeting(
                spell,
                out PlayerTargetingFailure targetingFailure))
        {
            rejectionReason = targetingFailure.ToString();
            return false;
        }

        SpellTargetingStarted?.Invoke(spell);
        return true;
    }

    public bool CancelTargeting()
    {
        return targetingController != null && targetingController.CancelTargeting();
    }

    public string GetCostDisplay(SpellDefinition spell)
    {
        if (spell == null || spell.ResourceCost.IsFree)
            return "Free";

        if (string.Equals(
                spell.ResourceCost.ResourceId,
                SpellResourceCost.ActionPoints,
                StringComparison.OrdinalIgnoreCase))
        {
            int amount = partyAdapter != null
                ? partyAdapter.GetDisplayedCost(spell)
                : Mathf.CeilToInt(spell.ResourceCost.Amount);
            return $"{amount} AP";
        }

        return $"{spell.ResourceCost.Amount:0.#} {spell.ResourceCost.ResourceId}";
    }

    private GameObject ResolveSelectedTarget(Vector2 worldPosition)
    {
        EnsureBuffer();
        var filter = new ContactFilter2D();
        filter.SetLayerMask(selectableLayers);
        filter.useTriggers = true;
        int count = Physics2D.OverlapPoint(worldPosition, filter, overlapBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D candidate = overlapBuffer[i];
            if (candidate == null)
                continue;

            GameObject resolved = SpellTargetResolver.Resolve(candidate.gameObject);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    private void HandleConfirmed(PlayerTargetingEvent evt)
    {
        targetMenu?.Close();
        SpellConfirmed?.Invoke(evt.Spell);
    }

    private void HandleCancelled(PlayerTargetingEvent evt)
    {
        targetMenu?.Close();
        SpellCancelled?.Invoke(evt.Spell);
    }

    private void HandleRejected(SpellCastFailure failure)
    {
        SpellRejected?.Invoke(
            targetingController != null ? targetingController.ActiveSpell : null,
            failure);
    }

    private void ResolveReferences()
    {
        if (aimTracker == null)
            aimTracker = GetComponent<AimTracker>();
        if (spellRunner == null)
            spellRunner = GetComponent<SpellRunner>();
        if (spellLoadout == null)
            spellLoadout = GetComponent<SpellLoadout>();
        if (targetingController == null)
            targetingController = GetComponent<PlayerSpellTargetingController>();
        if (partyAdapter == null)
            partyAdapter = GetComponent<PartyManagerSpellAdapter>();
        if (targetMenu == null)
            targetMenu = GetComponent<PlayerSpellTargetMenuController>();
    }

    private void EnsureFeedbackComponents()
    {
        // RequireComponent is applied when a component is first added, but it
        // is not guaranteed to retrofit prefabs that already contained an
        // earlier version of this bridge after a script update.
        if (GetComponent<TargetingPreviewRenderer2D>() == null)
            gameObject.AddComponent<TargetingPreviewRenderer2D>();
        if (GetComponent<PlayerSpellCastFeedback2D>() == null)
            gameObject.AddComponent<PlayerSpellCastFeedback2D>();
        if (GetComponent<CharacterDefinitionSpellLoadoutBinder>() == null)
            gameObject.AddComponent<CharacterDefinitionSpellLoadoutBinder>();
        if (GetComponent<PlayerSpellTargetMenuController>() == null)
            gameObject.AddComponent<PlayerSpellTargetMenuController>();
        if (GetComponent<SpellBuildUpControl2D>() == null)
            gameObject.AddComponent<SpellBuildUpControl2D>();
    }

    private void EnsureBuffer()
    {
        int size = Mathf.Max(1, overlapBufferSize);
        if (overlapBuffer == null || overlapBuffer.Length != size)
            overlapBuffer = new Collider2D[size];
    }

    private void OnValidate()
    {
        overlapBufferSize = Mathf.Max(1, overlapBufferSize);
    }
}
