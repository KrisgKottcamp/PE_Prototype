using System.Collections.Generic;
using UnityEngine;

public class PartyManager : MonoBehaviour
{
    public static PartyManager Instance { get; private set; }

    /// <summary>
    /// Raised after a party member actually recovers HP.
    /// Arguments are party index and the amount of HP restored.
    /// </summary>
    public static event System.Action<int, int> PartyMemberHealed;

    /// <summary>
    /// Raised after a party member actually loses HP.
    /// Arguments are party index and the amount of HP lost.
    /// </summary>
    public static event System.Action<int, int> PartyMemberDamaged;

    /// <summary>
    /// Raised when a defeated party member returns with positive HP.
    /// Arguments are party index and restored HP.
    /// </summary>
    public static event System.Action<int, int> PartyMemberRevived;

    [Header("Party Setup")]
    [SerializeField] private List<CharacterDefinition> partyDefinitions = new();

    [Header("Runtime")]
    public int activeIndex;
    public List<CharacterState> party = new();

    [System.Serializable]
    public class CharacterState
    {
        public CharacterDefinition def;
        public int level = 1;
        public int xp = 0;

        public int currentHP;
        public int currentAP;

        public float skillCostMultiplier = 1f;
        public List<SkillDefinition> unlockedSkills = new();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPartyIfEmpty();

        if (GetComponent<EriSupportManager>() == null)
            gameObject.AddComponent<EriSupportManager>();
    }

    private void BuildPartyIfEmpty()
    {
        if (party.Count > 0) return;

        foreach (var def in partyDefinitions)
        {
            var st = new CharacterState();
            st.def = def;
            st.currentHP = def.maxHP;
            st.currentAP = 0;
            st.skillCostMultiplier = 1f;

            st.unlockedSkills.AddRange(def.startingSkills);
            party.Add(st);
        }
    }

    public CharacterState Active => party[activeIndex];

    /// <summary>
    /// Adds AP to whichever character is active at the moment this is called.
    /// Returns the amount that actually fit in that character's AP meter.
    /// </summary>
    public int AddAPToActive(int amount)
    {
        if (amount <= 0 || party == null || party.Count == 0)
            return 0;

        if (activeIndex < 0 || activeIndex >= party.Count)
            return 0;

        CharacterState active = party[activeIndex];

        if (active == null || active.def == null)
            return 0;

        int maximum = Mathf.Max(0, active.def.maxAP);
        int before = Mathf.Clamp(active.currentAP, 0, maximum);

        active.currentAP = Mathf.Clamp(before + amount, 0, maximum);
        return active.currentAP - before;
    }

    /// <summary>
    /// Shared healing entry point. Future spells, items, and regeneration can
    /// use this so health UI and other positive feedback stay synchronized.
    /// Returns the amount of HP that was actually restored.
    /// </summary>
    public int HealPartyMember(int partyIndex, int amount)
    {
        if (amount <= 0 || party == null ||
            partyIndex < 0 || partyIndex >= party.Count)
        {
            return 0;
        }

        CharacterState target = party[partyIndex];

        if (target == null || target.def == null)
            return 0;

        int maximumHP = Mathf.Max(0, target.def.maxHP);
        int before = Mathf.Clamp(target.currentHP, 0, maximumHP);

        target.currentHP = Mathf.Clamp(
            before + amount,
            0,
            maximumHP
        );

        int restored = target.currentHP - before;

        if (restored > 0)
            PartyMemberHealed?.Invoke(partyIndex, restored);

        return restored;
    }

    /// <summary>
    /// Shared party damage entry point. Returns the amount of HP actually lost.
    /// </summary>
    public int DamagePartyMember(int partyIndex, int amount)
    {
        if (amount <= 0 || party == null ||
            partyIndex < 0 || partyIndex >= party.Count)
        {
            return 0;
        }

        CharacterState target = party[partyIndex];

        if (target == null || target.def == null)
            return 0;

        int maximumHP = Mathf.Max(0, target.def.maxHP);
        int before = Mathf.Clamp(target.currentHP, 0, maximumHP);

        target.currentHP = Mathf.Clamp(
            before - amount,
            0,
            maximumHP
        );

        int lost = before - target.currentHP;

        if (lost > 0)
            PartyMemberDamaged?.Invoke(partyIndex, lost);

        return lost;
    }

    /// <summary>
    /// Restores a defeated party member. This is intentionally separate from
    /// normal healing so callers cannot accidentally revive with a heal.
    /// </summary>
    public int RevivePartyMember(int partyIndex, int amount)
    {
        if (amount <= 0 || party == null ||
            partyIndex < 0 || partyIndex >= party.Count)
        {
            return 0;
        }

        CharacterState target = party[partyIndex];

        if (target == null || target.def == null ||
            target.currentHP > 0)
        {
            return 0;
        }

        int maximumHP = Mathf.Max(1, target.def.maxHP);
        int restored = Mathf.Clamp(amount, 1, maximumHP);

        target.currentHP = restored;

        PartyMemberRevived?.Invoke(partyIndex, restored);
        PartyMemberHealed?.Invoke(partyIndex, restored);

        return restored;
    }

    public int FindPartyIndexByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            party == null)
        {
            return -1;
        }

        for (int i = 0; i < party.Count; i++)
        {
            CharacterState state = party[i];

            if (state == null || state.def == null)
                continue;

            if (string.Equals(
                    state.def.displayName,
                    displayName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public void RestoreAllPartyHP()
    {
        if (party == null)
            return;

        for (int i = 0; i < party.Count; i++)
        {
            CharacterState state = party[i];

            if (state == null || state.def == null)
                continue;

            int before = state.currentHP;
            state.currentHP = Mathf.Max(0, state.def.maxHP);

            int restored = state.currentHP - before;

            if (restored > 0)
                PartyMemberHealed?.Invoke(i, restored);
        }
    }

    public void SwapNext()
    {
        activeIndex = (activeIndex + 1) % party.Count;

        // Spec rules
        Active.currentAP = 0;
        Active.skillCostMultiplier = 1f;

        // unlockedSkills persists, HP persists
    }

    public bool SwapNextAlive()
    {
        if (party == null || party.Count == 0) return false;

        int start = activeIndex;

        for (int step = 0; step < party.Count; step++)
        {
            int idx = (start + 1 + step) % party.Count;
            if (party[idx].currentHP > 0)
            {
                activeIndex = idx;

                // Spec rules on swap-in
                party[idx].currentAP = 0;
                party[idx].skillCostMultiplier = 1f;

                return true;
            }
        }

        return false; // nobody alive
    }


}
