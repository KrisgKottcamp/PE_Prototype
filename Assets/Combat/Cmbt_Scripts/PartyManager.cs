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
