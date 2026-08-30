using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellAIComboPlan
    {
        private readonly string[] matchedTags;

        public int OpportunityId { get; }
        public bool IsSetupPlan { get; }
        public Vector2 TargetPoint { get; }
        public float OpportunityRadius { get; }
        public SpellDefinition SourceSpell { get; }
        public GameObject Producer { get; }
        public GameObject TargetActor { get; }
        public float UtilityMultiplier { get; }
        public IReadOnlyList<string> MatchedTags =>
            matchedTags ?? Array.Empty<string>();
        public string Description { get; }

        public bool HasOpportunity => OpportunityId > 0;
        public bool NeedsReservation => HasOpportunity || IsSetupPlan;

        internal SpellAIComboPlan(
            int opportunityId,
            bool isSetupPlan,
            Vector2 targetPoint,
            float opportunityRadius,
            SpellDefinition sourceSpell,
            GameObject producer,
            GameObject targetActor,
            float utilityMultiplier,
            string[] tags,
            string description)
        {
            OpportunityId = opportunityId;
            IsSetupPlan = isSetupPlan;
            TargetPoint = targetPoint;
            OpportunityRadius = Mathf.Max(0f, opportunityRadius);
            SourceSpell = sourceSpell;
            Producer = producer;
            TargetActor = targetActor;
            UtilityMultiplier = Mathf.Max(0f, utilityMultiplier);
            matchedTags = tags ?? Array.Empty<string>();
            Description = description ?? string.Empty;
        }
    }

    public readonly struct SpellAIComboReservation
    {
        public int ReservationId { get; }
        public int OpportunityId { get; }
        public bool IsSetupReservation { get; }

        public bool IsValid => ReservationId > 0;

        internal SpellAIComboReservation(
            int reservationId,
            int opportunityId,
            bool isSetupReservation)
        {
            ReservationId = reservationId;
            OpportunityId = opportunityId;
            IsSetupReservation = isSetupReservation;
        }
    }

    /// <summary>
    /// Shared, delivery-generic squad combo memory. Produced tags are attached
    /// to real delivery geometry and lifetime. Consumers reserve a matching
    /// opportunity through their cast lifecycle so multiple allies cannot
    /// spend the same setup simultaneously.
    /// </summary>
    public static class SpellAIComboCoordinator
    {
        private sealed class Opportunity
        {
            public int Id;
            public SpellDefinition Spell;
            public GameObject Producer;
            public CombatTeam Team;
            public string[] Tags;
            public SpellDeliveryGeometry Geometry;
            public GameObject Subject;
            public bool SubjectBound;
            public int RuntimeId;
            public float ExpiresAt;
            public int ReservationId;
            public GameObject ReservedBy;
            public float ReservedUntil;
        }

        private sealed class SetupReservation
        {
            public int Id;
            public SpellDefinition Spell;
            public GameObject Caster;
            public CombatTeam Team;
            public string[] Tags;
            public Vector2 Center;
            public float Radius;
            public float ExpiresAt;
        }

        private static readonly List<Opportunity> opportunities =
            new List<Opportunity>(32);
        private static readonly List<SetupReservation> setupReservations =
            new List<SetupReservation>(16);
        private static readonly HashSet<string> tagBuffer =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int nextOpportunityId = 1;
        private static int nextReservationId = 1;

        public static int ActiveOpportunityCount
        {
            get
            {
                Prune();
                return opportunities.Count;
            }
        }

        public static int ActiveReservationCount(CombatTeam team)
        {
            Prune();
            int count = 0;
            for (int i = 0; i < opportunities.Count; i++)
            {
                Opportunity opportunity = opportunities[i];
                if (opportunity.ReservationId > 0 &&
                    SameTeam(opportunity.Team, team))
                {
                    count++;
                }
            }
            for (int i = 0; i < setupReservations.Count; i++)
            {
                if (SameTeam(setupReservations[i].Team, team))
                    count++;
            }
            return count;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearAll()
        {
            opportunities.Clear();
            setupReservations.Clear();
            tagBuffer.Clear();
            nextOpportunityId = 1;
            nextReservationId = 1;
        }

        public static void ReportDeliveryEvent(
            in SpellExecutionContext execution,
            in SpellEventOccurrence occurrence)
        {
            if (execution.SuppressGameplayEffects || execution.Spell == null)
                return;

            SpellAIAffordance guidance = execution.Spell.AIAffordance;
            string[] producedTags = NormalizeTags(
                guidance.ProducesComboTags);
            if (producedTags.Length == 0)
                return;

            Prune();
            bool activationEvent =
                occurrence.Type == guidance.ComboTagActivationEvent;
            if (activationEvent)
            {
                RegisterOpportunity(
                    execution,
                    occurrence,
                    producedTags,
                    linkToRuntime: !IsTerminalEvent(occurrence.Type));
            }

            if (IsTerminalEvent(occurrence.Type))
                RemoveRuntimeOpportunity(occurrence.DeliveryRuntime);
        }

        public static bool TryEvaluateSpell(
            SpellDefinition spell,
            GameObject caster,
            Vector2 preferredPoint,
            out SpellAIComboPlan plan,
            out string rejection)
        {
            return TryEvaluateSpell(
                spell,
                caster,
                preferredPoint,
                null,
                out plan,
                out rejection);
        }

        public static bool TryEvaluateSpell(
            SpellDefinition spell,
            GameObject caster,
            Vector2 preferredPoint,
            ISet<string> squadConsumerTags,
            out SpellAIComboPlan plan,
            out string rejection)
        {
            plan = default;
            rejection = string.Empty;
            if (spell == null || caster == null)
            {
                rejection = "Combo planning needs a spell and caster.";
                return false;
            }

            Prune();
            SpellAIAffordance guidance = spell.AIAffordance;
            string[] consumedTags = NormalizeTags(
                guidance.ConsumesComboTags);
            string[] producedTags = NormalizeTags(
                guidance.ProducesComboTags);
            CombatTeam team = CombatTeamMember.ResolveTeam(caster);

            if (consumedTags.Length > 0)
            {
                Opportunity best = null;
                float bestScore = float.NegativeInfinity;
                string[] bestMatches = null;
                for (int i = 0; i < opportunities.Count; i++)
                {
                    Opportunity candidate = opportunities[i];
                    if (!SameTeam(candidate.Team, team) ||
                        (!guidance.AllowSelfCombo &&
                         candidate.Producer == caster) ||
                        IsReservedByAnother(candidate, caster))
                    {
                        continue;
                    }

                    string[] matches = MatchTags(
                        consumedTags,
                        candidate.Tags);
                    bool satisfies = guidance.ComboRequirementMode ==
                                     SpellAIComboRequirementMode.AllTags
                        ? matches.Length == consumedTags.Length
                        : matches.Length > 0;
                    if (!satisfies)
                        continue;

                    SpellDeliveryGeometry geometry =
                        candidate.Geometry.Snapshot();
                    float distance = Vector2.Distance(
                        preferredPoint,
                        geometry.BoundingCenter);
                    float remaining = float.IsPositiveInfinity(
                        candidate.ExpiresAt)
                        ? 1f
                        : Mathf.Clamp01(
                            (candidate.ExpiresAt - Time.time) / 4f);
                    float candidateScore =
                        1f / (1f + distance * 0.15f) + remaining * 0.2f;
                    if (candidateScore <= bestScore)
                        continue;

                    bestScore = candidateScore;
                    best = candidate;
                    bestMatches = matches;
                }

                if (best != null)
                {
                    SpellDeliveryGeometry geometry = best.Geometry.Snapshot();
                    plan = new SpellAIComboPlan(
                        best.Id,
                        false,
                        geometry.BoundingCenter,
                        Mathf.Max(
                            guidance.ComboOpportunityRadius,
                            geometry.BoundingRadius),
                        best.Spell,
                        best.Producer,
                        best.Subject,
                        1f,
                        bestMatches,
                        $"Consume {string.Join(" + ", bestMatches)} from " +
                        $"{best.Spell.DisplayName}");
                    return true;
                }

                if (guidance.RequireActiveComboToCast)
                {
                    rejection =
                        $"No active allied setup provides " +
                        $"{string.Join(" / ", consumedTags)}.";
                    return false;
                }
            }

            if (producedTags.Length > 0)
            {
                bool hasSquadConsumer = SharesAnyTag(
                    producedTags,
                    squadConsumerTags);
                if (guidance.RequireSquadConsumerForSetup &&
                    !hasSquadConsumer)
                {
                    rejection =
                        "No living squad member has a compatible combo " +
                        "consumer equipped.";
                    return false;
                }

                float radius = ResolvePlanningRadius(spell, default);
                if (guidance.SuppressRedundantComboSetup &&
                    HasEquivalentSetup(
                        team,
                        producedTags,
                        preferredPoint,
                        radius,
                        caster))
                {
                    rejection =
                        "An equivalent squad combo setup already covers " +
                        "this location or is reserved.";
                    return false;
                }

                plan = new SpellAIComboPlan(
                    0,
                    true,
                    preferredPoint,
                    radius,
                    spell,
                    caster,
                    null,
                    hasSquadConsumer
                        ? guidance.SetupUtilityMultiplierWithConsumer
                        : 1f,
                    producedTags,
                    $"Set up {string.Join(" + ", producedTags)}" +
                    (hasSquadConsumer
                        ? " for a compatible squad consumer"
                        : string.Empty));
            }

            return true;
        }

        public static bool TryReservePlan(
            in SpellAIComboPlan plan,
            SpellDefinition spell,
            GameObject caster,
            in CastContext cast,
            float minimumDuration,
            out SpellAIComboReservation reservation,
            out string rejection)
        {
            reservation = default;
            rejection = string.Empty;
            if (!plan.NeedsReservation)
                return true;
            if (spell == null || caster == null)
            {
                rejection = "Combo reservation needs a spell and caster.";
                return false;
            }

            Prune();
            float duration = Mathf.Max(
                spell.AIAffordance.ComboReservationSeconds,
                minimumDuration,
                0.05f);
            int reservationId = AllocateReservationId();
            if (plan.HasOpportunity)
            {
                Opportunity opportunity = FindOpportunity(
                    plan.OpportunityId);
                if (opportunity == null)
                {
                    rejection = "The combo opportunity expired before casting.";
                    return false;
                }
                if (IsReservedByAnother(opportunity, caster))
                {
                    rejection =
                        "Another squad member reserved this combo opportunity.";
                    return false;
                }

                opportunity.ReservationId = reservationId;
                opportunity.ReservedBy = caster;
                opportunity.ReservedUntil = Time.time + duration;
                reservation = new SpellAIComboReservation(
                    reservationId,
                    opportunity.Id,
                    false);
                return true;
            }

            string[] tags = NormalizeTags(
                spell.AIAffordance.ProducesComboTags);
            if (tags.Length == 0)
                return true;

            Vector2 center = cast.HasTargetPoint
                ? cast.TargetPoint
                : plan.TargetPoint;
            float radius = Mathf.Max(
                plan.OpportunityRadius,
                ResolvePlanningRadius(spell, default));
            CombatTeam team = CombatTeamMember.ResolveTeam(caster);
            if (spell.AIAffordance.SuppressRedundantComboSetup &&
                HasEquivalentSetup(
                    team,
                    tags,
                    center,
                    radius,
                    caster))
            {
                rejection =
                    "An equivalent combo setup was reserved first.";
                return false;
            }

            setupReservations.Add(new SetupReservation
            {
                Id = reservationId,
                Spell = spell,
                Caster = caster,
                Team = team,
                Tags = tags,
                Center = center,
                Radius = radius,
                ExpiresAt = Time.time + duration
            });
            reservation = new SpellAIComboReservation(
                reservationId,
                0,
                true);
            return true;
        }

        public static void CommitReservation(
            in SpellAIComboReservation reservation,
            SpellDefinition consumingSpell,
            GameObject caster)
        {
            if (!reservation.IsValid)
                return;

            Prune();
            if (reservation.IsSetupReservation)
                return;

            Opportunity opportunity = FindOpportunity(
                reservation.OpportunityId);
            if (opportunity == null ||
                opportunity.ReservationId != reservation.ReservationId ||
                opportunity.ReservedBy != caster)
            {
                return;
            }

            if (consumingSpell != null &&
                consumingSpell.AIAffordance.ConsumeComboOpportunityOnCast)
            {
                opportunities.Remove(opportunity);
                return;
            }

            ClearOpportunityReservation(opportunity);
        }

        public static void ReleaseReservation(
            in SpellAIComboReservation reservation,
            GameObject caster)
        {
            if (!reservation.IsValid)
                return;

            for (int i = setupReservations.Count - 1; i >= 0; i--)
            {
                SetupReservation setup = setupReservations[i];
                if (setup.Id == reservation.ReservationId &&
                    (caster == null || setup.Caster == caster))
                {
                    setupReservations.RemoveAt(i);
                }
            }

            Opportunity opportunity = FindOpportunity(
                reservation.OpportunityId);
            if (opportunity != null &&
                opportunity.ReservationId == reservation.ReservationId &&
                (caster == null || opportunity.ReservedBy == caster))
            {
                ClearOpportunityReservation(opportunity);
            }
        }

        public static void CancelPendingCast(
            SpellDefinition spell,
            in CastContext cast)
        {
            if (spell == null)
                return;

            for (int i = setupReservations.Count - 1; i >= 0; i--)
            {
                SetupReservation setup = setupReservations[i];
                if (setup.Spell == spell &&
                    setup.Caster == cast.Caster)
                {
                    setupReservations.RemoveAt(i);
                }
            }
        }

        private static void RegisterOpportunity(
            in SpellExecutionContext execution,
            in SpellEventOccurrence occurrence,
            string[] tags,
            bool linkToRuntime)
        {
            int runtimeId = linkToRuntime && occurrence.DeliveryRuntime != null
                ? occurrence.DeliveryRuntime.GetInstanceID()
                : 0;
            SpellDeliveryGeometry geometry = ResolveGeometry(
                execution.Spell,
                execution.Cast,
                occurrence);
            float lifetime = ResolveOpportunityLifetime(execution.Spell);
            float expiresAt = float.IsPositiveInfinity(lifetime)
                ? float.PositiveInfinity
                : Time.time + lifetime;

            Opportunity existing = runtimeId != 0
                ? FindRuntimeOpportunity(runtimeId, execution.Spell)
                : null;
            if (existing != null)
            {
                existing.Tags = tags;
                existing.Geometry = geometry;
                existing.Subject = occurrence.Subject;
                existing.SubjectBound = occurrence.Subject != null;
                existing.ExpiresAt = expiresAt;
            }
            else
            {
                opportunities.Add(new Opportunity
                {
                    Id = AllocateOpportunityId(),
                    Spell = execution.Spell,
                    Producer = execution.Cast.Caster,
                    Team = CombatTeamMember.ResolveTeam(
                        execution.Cast.Caster,
                        execution.Cast.CasterTeam),
                    Tags = tags,
                    Geometry = geometry,
                    Subject = occurrence.Subject,
                    SubjectBound = occurrence.Subject != null,
                    RuntimeId = runtimeId,
                    ExpiresAt = expiresAt
                });
            }

            ClearMatchingSetupReservation(
                execution.Spell,
                execution.Cast.Caster);
        }

        private static bool HasEquivalentSetup(
            CombatTeam team,
            string[] tags,
            Vector2 center,
            float radius,
            GameObject requestingCaster)
        {
            float ownRadius = Mathf.Max(0.25f, radius);
            for (int i = 0; i < opportunities.Count; i++)
            {
                Opportunity opportunity = opportunities[i];
                if (!SameTeam(opportunity.Team, team) ||
                    !SharesAnyTag(tags, opportunity.Tags))
                {
                    continue;
                }

                SpellDeliveryGeometry geometry =
                    opportunity.Geometry.Snapshot();
                float overlapDistance = Mathf.Max(
                    0.5f,
                    (ownRadius + geometry.BoundingRadius) * 0.65f);
                if (Vector2.Distance(center, geometry.BoundingCenter) <=
                    overlapDistance)
                {
                    return true;
                }
            }

            for (int i = 0; i < setupReservations.Count; i++)
            {
                SetupReservation setup = setupReservations[i];
                if (!SameTeam(setup.Team, team) ||
                    setup.Caster == requestingCaster ||
                    !SharesAnyTag(tags, setup.Tags))
                {
                    continue;
                }
                float overlapDistance = Mathf.Max(
                    0.5f,
                    (ownRadius + setup.Radius) * 0.65f);
                if (Vector2.Distance(center, setup.Center) <= overlapDistance)
                    return true;
            }
            return false;
        }

        private static SpellDeliveryGeometry ResolveGeometry(
            SpellDefinition spell,
            in CastContext cast,
            in SpellEventOccurrence occurrence)
        {
            SpellDeliveryGeometry geometry;
            if (occurrence.HasGeometry)
                geometry = occurrence.Geometry.Snapshot();
            else if (occurrence.DeliveryRuntime is
                     ISpellDeliveryGeometryProvider provider &&
                     provider.TryGetDeliveryGeometry(out geometry))
            {
                geometry = geometry.Snapshot();
            }
            else
            {
                Vector2 point = occurrence.Point;
                if (point == Vector2.zero)
                {
                    point = cast.HasTargetPoint
                        ? cast.TargetPoint
                        : cast.SelectedTarget != null
                            ? (Vector2)cast.SelectedTarget.transform.position
                            : cast.Origin;
                }
                geometry = SpellDeliveryGeometry.Circle(
                    point,
                    ResolvePlanningRadius(spell, default));
            }

            float authoredRadius = spell.AIAffordance.ComboOpportunityRadius;
            return authoredRadius > 0f
                ? geometry.WithSizeOverride(authoredRadius)
                : geometry;
        }

        private static float ResolveOpportunityLifetime(
            SpellDefinition spell)
        {
            float authored = spell.AIAffordance.ComboOpportunityLifetime;
            if (authored > 0f)
                return authored;
            float delivery = SpellAITacticalMemory
                .EstimatePersistentDuration(spell);
            return delivery > 0f ? delivery : 4f;
        }

        private static float ResolvePlanningRadius(
            SpellDefinition spell,
            in SpellDeliveryGeometry geometry)
        {
            if (spell == null)
                return 0.5f;
            if (spell.AIAffordance.ComboOpportunityRadius > 0f)
                return spell.AIAffordance.ComboOpportunityRadius;
            if (geometry.BoundingRadius > 0f)
                return geometry.BoundingRadius;
            return Mathf.Max(
                0.5f,
                SpellAITacticalMemory.EstimatePersistentRadius(spell));
        }

        private static void Prune()
        {
            float now = Time.time;
            for (int i = opportunities.Count - 1; i >= 0; i--)
            {
                Opportunity opportunity = opportunities[i];
                if (opportunity == null || opportunity.Spell == null ||
                    opportunity.Producer == null ||
                    (opportunity.SubjectBound &&
                     opportunity.Subject == null) ||
                    now >= opportunity.ExpiresAt)
                {
                    opportunities.RemoveAt(i);
                    continue;
                }
                if (opportunity.ReservationId > 0 &&
                    (opportunity.ReservedBy == null ||
                     now >= opportunity.ReservedUntil))
                {
                    ClearOpportunityReservation(opportunity);
                }
            }

            for (int i = setupReservations.Count - 1; i >= 0; i--)
            {
                SetupReservation setup = setupReservations[i];
                if (setup == null || setup.Spell == null ||
                    setup.Caster == null || now >= setup.ExpiresAt)
                {
                    setupReservations.RemoveAt(i);
                }
            }
        }

        private static void RemoveRuntimeOpportunity(Component runtime)
        {
            if (runtime == null)
                return;
            int runtimeId = runtime.GetInstanceID();
            for (int i = opportunities.Count - 1; i >= 0; i--)
            {
                if (opportunities[i].RuntimeId == runtimeId)
                    opportunities.RemoveAt(i);
            }
        }

        private static Opportunity FindOpportunity(int id)
        {
            if (id <= 0)
                return null;
            for (int i = 0; i < opportunities.Count; i++)
            {
                if (opportunities[i].Id == id)
                    return opportunities[i];
            }
            return null;
        }

        private static Opportunity FindRuntimeOpportunity(
            int runtimeId,
            SpellDefinition spell)
        {
            for (int i = 0; i < opportunities.Count; i++)
            {
                Opportunity opportunity = opportunities[i];
                if (opportunity.RuntimeId == runtimeId &&
                    opportunity.Spell == spell)
                {
                    return opportunity;
                }
            }
            return null;
        }

        private static bool IsReservedByAnother(
            Opportunity opportunity,
            GameObject caster)
        {
            return opportunity.ReservationId > 0 &&
                   opportunity.ReservedBy != caster &&
                   Time.time < opportunity.ReservedUntil;
        }

        private static void ClearOpportunityReservation(
            Opportunity opportunity)
        {
            opportunity.ReservationId = 0;
            opportunity.ReservedBy = null;
            opportunity.ReservedUntil = 0f;
        }

        private static void ClearMatchingSetupReservation(
            SpellDefinition spell,
            GameObject caster)
        {
            for (int i = setupReservations.Count - 1; i >= 0; i--)
            {
                if (setupReservations[i].Spell == spell &&
                    setupReservations[i].Caster == caster)
                {
                    setupReservations.RemoveAt(i);
                }
            }
        }

        private static string[] NormalizeTags(IReadOnlyList<string> source)
        {
            tagBuffer.Clear();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    string tag = source[i]?.Trim();
                    if (!string.IsNullOrWhiteSpace(tag))
                        tagBuffer.Add(tag);
                }
            }
            string[] result = new string[tagBuffer.Count];
            tagBuffer.CopyTo(result);
            tagBuffer.Clear();
            return result;
        }

        private static string[] MatchTags(
            string[] requested,
            string[] available)
        {
            var result = new List<string>(requested.Length);
            for (int i = 0; i < requested.Length; i++)
            {
                if (ContainsTag(available, requested[i]))
                    result.Add(requested[i]);
            }
            return result.ToArray();
        }

        private static bool SharesAnyTag(string[] left, string[] right)
        {
            for (int i = 0; i < left.Length; i++)
            {
                if (ContainsTag(right, left[i]))
                    return true;
            }
            return false;
        }

        private static bool SharesAnyTag(
            string[] left,
            ISet<string> right)
        {
            if (right == null || right.Count == 0)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (right.Contains(left[i]))
                    return true;
            }
            return false;
        }

        private static bool ContainsTag(string[] tags, string value)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(
                        tags[i],
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameTeam(CombatTeam left, CombatTeam right)
        {
            return left != CombatTeam.Neutral && left == right;
        }

        private static bool IsTerminalEvent(SpellEventType eventType)
        {
            return eventType == SpellEventType.DeliveryStopped ||
                   eventType == SpellEventType.DeliveryExpired ||
                   eventType == SpellEventType.Detonated;
        }

        private static int AllocateOpportunityId()
        {
            if (nextOpportunityId <= 0)
                nextOpportunityId = 1;
            return nextOpportunityId++;
        }

        private static int AllocateReservationId()
        {
            if (nextReservationId <= 0)
                nextReservationId = 1;
            return nextReservationId++;
        }
    }
}
