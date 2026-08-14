using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerSpellTargetMenuController : MonoBehaviour,
    ISpellStatModifierTargetRouter
{
    [Header("Resolution-Aware Menu Display")]
    [Tooltip("Menu width at a 1080p game resolution. The menu scales automatically with screen height.")]
    [SerializeField, Min(260f)] private float referenceWindowWidth = 480f;
    [Tooltip("Maximum fraction of the screen height used by a long target list before it becomes scrollable.")]
    [SerializeField, Range(0.35f, 0.9f)] private float maximumScreenHeight = 0.72f;

    private sealed class Entry
    {
        public GameObject Target;
        public string Label;
        public int CurrentHP;
        public int MaximumHP;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<PartyMemberSpellTargetProxy> partyProxies =
        new List<PartyMemberSpellTargetProxy>();
    private MenuSelectTargetingDefinition activeDefinition;
    private int selectedIndex;
    private GUIStyle titleStyle;
    private GUIStyle selectedStyle;
    private GUIStyle normalStyle;
    private float nextRosterRefreshTime;
    private bool mouseConfirmRequested;
    private Vector2 scrollPosition;
    private int lastAutoScrolledIndex = -1;

    public bool IsOpen => activeDefinition != null;
    public GameObject SelectedTarget => entries.Count > 0
        ? entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)].Target
        : null;

    public GameObject ResolveStatModifierTarget(
        bool applyToAllPartyMembers)
    {
        if (applyToAllPartyMembers)
            return gameObject;

        PartyManager party = PartyManager.Instance;
        if (party == null || party.party == null ||
            party.activeIndex < 0 || party.activeIndex >= party.party.Count)
        {
            return gameObject;
        }

        EnsurePartyProxyCount(party.party.Count);
        PartyMemberSpellTargetProxy proxy =
            partyProxies[party.activeIndex];
        proxy.Configure(
            party.activeIndex,
            gameObject,
            includeDefeated: false);
        return proxy.gameObject;
    }

    public void OpenOrRefresh(MenuSelectTargetingDefinition definition)
    {
        if (definition == null)
        {
            Close();
            return;
        }

        bool changedMode = activeDefinition != definition;
        activeDefinition = definition;
        if (changedMode || entries.Count == 0 ||
            Time.unscaledTime >= nextRosterRefreshTime)
        {
            RebuildEntries();
            nextRosterRefreshTime = Time.unscaledTime + 0.2f;
        }
        else
            RefreshEntryVitals();
    }

    public void HandleNavigation()
    {
        if (!IsOpen || entries.Count == 0)
            return;
        if (Input.GetKeyDown(KeyCode.UpArrow) ||
            Input.GetKeyDown(KeyCode.W))
        {
            selectedIndex = (selectedIndex - 1 + entries.Count) %
                            entries.Count;
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) ||
                 Input.GetKeyDown(KeyCode.S))
        {
            selectedIndex = (selectedIndex + 1) % entries.Count;
        }
    }

    public void Close()
    {
        activeDefinition = null;
        entries.Clear();
        selectedIndex = 0;
        mouseConfirmRequested = false;
        nextRosterRefreshTime = 0f;
        scrollPosition = Vector2.zero;
        lastAutoScrolledIndex = -1;
    }

    public bool ConsumeConfirmRequest()
    {
        bool requested = mouseConfirmRequested;
        mouseConfirmRequested = false;
        return requested;
    }

    private void RebuildEntries()
    {
        int previousTargetId = SelectedTarget != null
            ? SelectedTarget.GetInstanceID()
            : 0;
        entries.Clear();
        if (activeDefinition.TargetGroup == MenuTargetGroup.ActiveEnemies)
            AddActiveEnemies();
        else
            AddPartyMembers();

        if (activeDefinition.SortAlphabetically)
        {
            entries.Sort((a, b) => string.Compare(
                a.Label,
                b.Label,
                System.StringComparison.OrdinalIgnoreCase));
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0,
            Mathf.Max(0, entries.Count - 1));
        if (previousTargetId != 0)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Target != null &&
                    entries[i].Target.GetInstanceID() == previousTargetId)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
    }

    private void AddPartyMembers()
    {
        PartyManager party = PartyManager.Instance;
        if (party == null || party.party == null)
            return;

        EnsurePartyProxyCount(party.party.Count);
        bool includeDefeated = activeDefinition.TargetGroup ==
                               MenuTargetGroup.AllPartyMembers;
        for (int i = 0; i < party.party.Count; i++)
        {
            PartyManager.CharacterState state = party.party[i];
            if (state == null || state.def == null ||
                (!includeDefeated && state.currentHP <= 0))
            {
                continue;
            }

            PartyMemberSpellTargetProxy proxy = partyProxies[i];
            proxy.Configure(i, gameObject, includeDefeated);
            entries.Add(new Entry
            {
                Target = proxy.gameObject,
                Label = proxy.TargetDisplayName,
                CurrentHP = state.currentHP,
                MaximumHP = Mathf.Max(0, state.def.maxHP)
            });
        }
    }

    private void AddActiveEnemies()
    {
        CombatTeamMember[] members =
            FindObjectsByType<CombatTeamMember>(FindObjectsSortMode.None);
        var seen = new HashSet<int>();
        for (int i = 0; i < members.Length; i++)
        {
            CombatTeamMember member = members[i];
            if (member == null || !member.isActiveAndEnabled ||
                member.Team != CombatTeam.Enemy)
            {
                continue;
            }

            GameObject target = SpellTargetResolver.Resolve(member.gameObject);
            if (target == null || !seen.Add(target.GetInstanceID()))
                continue;
            EnemyHealth health =
                SpellEffectReceiverResolver.Find<EnemyHealth>(target);
            if (health != null && health.CurrentHP <= 0)
                continue;
            ISpellTarget spellTarget = FindTargetInterface(target);
            if (spellTarget != null && !spellTarget.IsTargetable)
                continue;
            entries.Add(new Entry
            {
                Target = target,
                Label = ResolveLabel(target),
                CurrentHP = health != null ? health.CurrentHP : -1,
                MaximumHP = health != null ? health.MaxHP : -1
            });
        }
    }

    private void EnsurePartyProxyCount(int count)
    {
        while (partyProxies.Count < count)
        {
            var proxyObject = new GameObject("Skill V2 Party Target");
            proxyObject.transform.SetParent(transform, false);
            proxyObject.hideFlags = HideFlags.HideInHierarchy;
            partyProxies.Add(
                proxyObject.AddComponent<PartyMemberSpellTargetProxy>());
        }
    }

    private void RefreshEntryVitals()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            PartyMemberSpellTargetProxy proxy = entry.Target != null
                ? entry.Target.GetComponent<PartyMemberSpellTargetProxy>()
                : null;
            if (proxy != null)
            {
                entry.CurrentHP = proxy.CurrentHP;
                entry.MaximumHP = proxy.MaximumHP;
            }
            else if (entry.Target != null)
            {
                EnemyHealth health =
                    entry.Target.GetComponentInParent<EnemyHealth>();
                if (health != null)
                {
                    entry.CurrentHP = health.CurrentHP;
                    entry.MaximumHP = health.MaxHP;
                }
            }
        }
    }

    private void OnGUI()
    {
        if (!IsOpen)
            return;
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2f);
        EnsureStyles(scale);
        float width = referenceWindowWidth * scale;
        float outerPadding = 20f * scale;
        float titleHeight = 42f * scale;
        float rowHeight = 50f * scale;
        float listContentHeight = entries.Count * rowHeight;
        float maximumListHeight = Mathf.Max(
            rowHeight,
            Screen.height * maximumScreenHeight - titleHeight - outerPadding * 2f);
        float listHeight = Mathf.Min(listContentHeight, maximumListHeight);
        float height = Mathf.Max(
            132f * scale,
            titleHeight + listHeight + outerPadding * 2f);
        Rect window = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);
        GUI.Box(window, GUIContent.none);
        GUI.Label(
            new Rect(
                window.x + outerPadding,
                window.y + outerPadding * 0.5f,
                width - outerPadding * 2f,
                titleHeight),
            ResolveTitle(),
            titleStyle);
        if (entries.Count == 0)
        {
            GUI.Label(
                new Rect(
                    window.x + outerPadding,
                    window.y + titleHeight + outerPadding,
                    width - outerPadding * 2f,
                    rowHeight),
                "No valid targets are currently available.",
                normalStyle);
            return;
        }

        Rect viewport = new Rect(
            window.x + outerPadding,
            window.y + titleHeight + outerPadding,
            width - outerPadding * 2f,
            listHeight);
        Rect content = new Rect(
            0f,
            0f,
            viewport.width - 18f * scale,
            listContentHeight);
        if (lastAutoScrolledIndex != selectedIndex)
        {
            float selectedTop = selectedIndex * rowHeight;
            scrollPosition.y = Mathf.Clamp(
                selectedTop - (listHeight - rowHeight) * 0.5f,
                0f,
                Mathf.Max(0f, listContentHeight - listHeight));
            lastAutoScrolledIndex = selectedIndex;
        }
        scrollPosition = GUI.BeginScrollView(
            viewport,
            scrollPosition,
            content,
            false,
            listContentHeight > listHeight);
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            string health = entry.MaximumHP >= 0
                ? $"   HP {entry.CurrentHP}/{entry.MaximumHP}"
                : string.Empty;
            Rect row = new Rect(
                    0f,
                    i * rowHeight,
                    content.width,
                    rowHeight - 4f * scale);
            if (GUI.Button(
                row,
                $"{(i == selectedIndex ? "> " : "  ")}{entry.Label}{health}",
                i == selectedIndex ? selectedStyle : normalStyle))
            {
                selectedIndex = i;
                mouseConfirmRequested = true;
            }
        }
        GUI.EndScrollView();
    }

    private string ResolveTitle()
    {
        switch (activeDefinition.TargetGroup)
        {
            case MenuTargetGroup.AllPartyMembers:
                return "Choose Any Party Member";
            case MenuTargetGroup.ActivePartyMembers:
                return "Choose a Living Party Member";
            default:
                return "Choose an Active Enemy";
        }
    }

    private static string ResolveLabel(GameObject target)
    {
        MonoBehaviour[] behaviours =
            target.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ISpellTargetDisplay display &&
                !string.IsNullOrWhiteSpace(display.TargetDisplayName))
            {
                return display.TargetDisplayName;
            }
        }
        return target.name.Replace("(Clone)", string.Empty).Trim();
    }

    private static ISpellTarget FindTargetInterface(GameObject target)
    {
        MonoBehaviour[] behaviours =
            target.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ISpellTarget found)
                return found;
        }
        return null;
    }

    private void EnsureStyles(float scale)
    {
        titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        selectedStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 10, 4, 4)
        };
        normalStyle ??= new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 10, 4, 4)
        };

        titleStyle.fontSize = Mathf.RoundToInt(24f * scale);
        selectedStyle.fontSize = Mathf.RoundToInt(21f * scale);
        normalStyle.fontSize = Mathf.RoundToInt(21f * scale);
        selectedStyle.normal.textColor = Color.white;
        normalStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    }
}
