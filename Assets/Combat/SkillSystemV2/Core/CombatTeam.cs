using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum CombatTeam
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2,
        Environment = 3
    }

    [DisallowMultipleComponent]
    public sealed class CombatTeamMember : MonoBehaviour
    {
        [SerializeField]
        private CombatTeam team = CombatTeam.Neutral;

        public CombatTeam Team => team;

        public void SetTeam(CombatTeam newTeam)
        {
            team = newTeam;
        }

        public static bool TryResolve(
            GameObject candidate,
            out CombatTeamMember member)
        {
            member = candidate != null
                ? candidate.GetComponentInParent<CombatTeamMember>()
                : null;

            return member != null;
        }

        public static CombatTeam ResolveTeam(
            GameObject candidate,
            CombatTeam fallback = CombatTeam.Neutral)
        {
            return TryResolve(candidate, out CombatTeamMember member)
                ? member.Team
                : fallback;
        }
    }
}
