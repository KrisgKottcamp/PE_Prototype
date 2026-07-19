using System.Collections.Generic;
using UnityEngine;

public class PartyManager : MonoBehaviour
{
    public static PartyManager Instance { get; private set; }

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
