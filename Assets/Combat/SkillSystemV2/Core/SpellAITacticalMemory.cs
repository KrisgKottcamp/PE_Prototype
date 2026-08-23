using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Small shared memory for AI spell cadence and persistent placement.
    /// It records successful cast starts, not hypothetical candidates, and is
    /// independent of a particular enemy brain or named spell.
    /// </summary>
    public static class SpellAITacticalMemory
    {
        private sealed class ActiveInstance
        {
            public SpellDefinition Spell;
            public GameObject Caster;
            public int CasterId;
            public CombatTeam Team;
            public string SpellKey;
            public Vector2 Center;
            public float Radius;
            public float ExpiresAt;
        }

        private sealed class LastCast
        {
            public GameObject Caster;
            public float Time;
        }

        private static readonly List<ActiveInstance> activeInstances =
            new List<ActiveInstance>(32);
        private static readonly Dictionary<string, LastCast> lastCasts =
            new Dictionary<string, LastCast>();
        private static readonly List<string> staleCastKeys =
            new List<string>(16);

        public static int ActiveInstanceCount
        {
            get
            {
                Prune();
                return activeInstances.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearAll()
        {
            activeInstances.Clear();
            lastCasts.Clear();
            staleCastKeys.Clear();
        }

        public static bool TryEvaluate(
            SpellDefinition spell,
            GameObject caster,
            in CastContext context,
            out float utilityMultiplier,
            out string rejection)
        {
            utilityMultiplier = 1f;
            rejection = string.Empty;
            if (spell == null || caster == null)
            {
                rejection = "Tactical memory needs a spell and caster.";
                return false;
            }

            Prune();
            SpellAIAffordance guidance = spell.AIAffordance;
            string spellKey = ResolveSpellKey(spell);
            string castKey = BuildCastKey(caster.GetInstanceID(), spellKey);
            if (lastCasts.TryGetValue(castKey, out LastCast lastCast))
            {
                float remaining = guidance.MinimumAIRecastInterval -
                                  (Time.time - lastCast.Time);
                if (remaining > 0f)
                {
                    rejection =
                        $"AI recast cadence: {remaining:0.00}s remaining.";
                    return false;
                }
            }

            int casterCount = 0;
            int squadCount = 0;
            CombatTeam team = CombatTeamMember.ResolveTeam(caster);
            float radius = EstimatePersistentRadius(spell);
            bool hasPlacement = context.HasTargetPoint && radius > 0.01f;

            for (int i = 0; i < activeInstances.Count; i++)
            {
                ActiveInstance active = activeInstances[i];
                if (active.SpellKey != spellKey)
                    continue;

                if (active.CasterId == caster.GetInstanceID())
                    casterCount++;
                if (team != CombatTeam.Neutral && active.Team == team)
                    squadCount++;

                if (!hasPlacement)
                    continue;

                float overlapDistance = Mathf.Max(
                    0.25f,
                    (radius + active.Radius) * 0.65f);
                if ((active.Center - context.TargetPoint).sqrMagnitude >
                    overlapDistance * overlapDistance)
                {
                    continue;
                }

                if (!guidance.AllowEquivalentOverlap)
                {
                    rejection =
                        "Equivalent persistent placement already covers this area.";
                    return false;
                }

                utilityMultiplier *=
                    guidance.EquivalentOverlapUtilityMultiplier;
            }

            if (guidance.MaximumActiveInstancesPerCaster > 0 &&
                casterCount >= guidance.MaximumActiveInstancesPerCaster)
            {
                rejection =
                    "This caster already owns the maximum active instances.";
                return false;
            }

            if (guidance.MaximumActiveInstancesPerSquad > 0 &&
                squadCount >= guidance.MaximumActiveInstancesPerSquad)
            {
                rejection =
                    "The squad already owns the maximum active instances.";
                return false;
            }

            if (utilityMultiplier <= 0f)
            {
                rejection =
                    "Equivalent overlap reduced this placement to zero utility.";
                return false;
            }
            return true;
        }

        public static void RecordCast(
            SpellDefinition spell,
            GameObject caster,
            in CastContext context)
        {
            if (spell == null || caster == null)
                return;

            Prune();
            string spellKey = ResolveSpellKey(spell);
            int casterId = caster.GetInstanceID();
            lastCasts[BuildCastKey(casterId, spellKey)] = new LastCast
            {
                Caster = caster,
                Time = Time.time
            };

            float duration = EstimatePersistentDuration(spell);
            float radius = EstimatePersistentRadius(spell);
            if (!context.HasTargetPoint || duration <= 0.01f ||
                radius <= 0.01f)
            {
                return;
            }

            activeInstances.Add(new ActiveInstance
            {
                Spell = spell,
                Caster = caster,
                CasterId = casterId,
                Team = CombatTeamMember.ResolveTeam(caster),
                SpellKey = spellKey,
                Center = context.TargetPoint,
                Radius = radius,
                ExpiresAt = float.IsPositiveInfinity(duration)
                    ? float.PositiveInfinity
                    : Time.time + duration
            });
        }

        public static float EstimatePersistentDuration(SpellDefinition spell)
        {
            if (spell?.DeliverySettings is
                LingeringAreaDeliverySettings lingering)
            {
                return lingering.Duration;
            }
            if (spell?.DeliverySettings is
                ProximityMineDeliverySettings mine)
            {
                return mine.Lifetime > 0f
                    ? mine.Lifetime
                    : float.PositiveInfinity;
            }
            if (spell?.DeliverySettings is TripWireDeliverySettings wire)
            {
                return wire.Duration > 0f
                    ? wire.Duration
                    : float.PositiveInfinity;
            }
            return 0f;
        }

        public static float EstimatePersistentRadius(SpellDefinition spell)
        {
            if (spell?.DeliverySettings is
                LingeringAreaDeliverySettings lingering)
            {
                return lingering.Radius;
            }
            if (spell?.DeliverySettings is
                ProximityMineDeliverySettings mine)
            {
                return Mathf.Max(mine.TriggerRadius, mine.EffectRadius);
            }
            if (spell?.DeliverySettings is TripWireDeliverySettings wire)
                return wire.TriggerWidth;
            if (spell?.DeliverySettings is AreaDeliverySettings area)
                return area.Radius;
            return spell != null ? spell.AIAffordance.DangerRadius : 0f;
        }

        private static void Prune()
        {
            float now = Time.time;
            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                ActiveInstance active = activeInstances[i];
                if (active == null || active.Spell == null ||
                    active.Caster == null || now >= active.ExpiresAt)
                {
                    activeInstances.RemoveAt(i);
                }
            }

            staleCastKeys.Clear();
            foreach (KeyValuePair<string, LastCast> pair in lastCasts)
            {
                if (pair.Value == null || pair.Value.Caster == null)
                    staleCastKeys.Add(pair.Key);
            }
            for (int i = 0; i < staleCastKeys.Count; i++)
                lastCasts.Remove(staleCastKeys[i]);
            staleCastKeys.Clear();
        }

        private static string ResolveSpellKey(SpellDefinition spell)
        {
            return !string.IsNullOrWhiteSpace(spell.StableId)
                ? spell.StableId
                : $"instance:{spell.GetInstanceID()}";
        }

        private static string BuildCastKey(int casterId, string spellKey)
        {
            return $"{casterId}:{spellKey}";
        }
    }
}
