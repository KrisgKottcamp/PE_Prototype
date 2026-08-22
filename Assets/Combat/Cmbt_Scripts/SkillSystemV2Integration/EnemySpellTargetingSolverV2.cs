using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    /// <summary>
    /// Produces valid CastContexts for the generic aim families used by V2
    /// spells. It intentionally knows delivery semantics, not named spells.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpellTargetingSolverV2 : MonoBehaviour
    {
        [Header("Prediction")]
        [SerializeField, Min(0f)] private float maximumPredictionSeconds = 1.25f;
        [SerializeField, Min(0f)] private float maximumLeadDistance = 3f;

        [Header("Point Candidate Sampling")]
        [SerializeField, Min(0f)] private float pointSampleRadius = 0.75f;
        [SerializeField, Range(0, 16)] private int radialSampleCount = 6;

        [Header("Runtime Debug")]
        [SerializeField] private string debugSolution = "Not evaluated";
        [SerializeField] private Vector2 debugChosenPoint;
        [SerializeField] private int debugCandidateCount;

        private readonly List<Vector2> pointCandidates = new List<Vector2>(20);

        public string DebugSolution => debugSolution;

        public bool TryResolveBestContext(
            SpellDefinition spell,
            GameObject preferredTarget,
            Vector2 fallbackGroundPoint,
            out CastContext resolved,
            out float solutionScore,
            out string rejection)
        {
            resolved = default;
            solutionScore = float.NegativeInfinity;
            rejection = string.Empty;

            if (spell == null || spell.Delivery == null)
            {
                rejection = "Missing spell or delivery";
                debugSolution = rejection;
                return false;
            }

            CastTargetingRequirement requirements =
                spell.Delivery.TargetingRequirement;
            if ((requirements & CastTargetingRequirement.MultipleTargetPoints) != 0)
            {
                rejection = "Two-point AI targeting is not implemented in the first vertical slice";
                debugSolution = rejection;
                return false;
            }

            SpellAITargetPreference targetPreference =
                spell.AIAffordance.TargetPreference;
            if (targetPreference == SpellAITargetPreference.SafeGround ||
                targetPreference == SpellAITargetPreference.EscapeGround)
            {
                rejection =
                    "Navigation-aware safe/escape ground targeting is reserved for the GOAP movement milestone";
                debugSolution = rejection;
                return false;
            }

            GameObject target = ResolvePreferredTarget(spell, preferredTarget);
            bool needsSelectedTarget =
                (requirements & CastTargetingRequirement.SelectedTarget) != 0;
            if (needsSelectedTarget && target == null)
            {
                rejection = "Delivery requires a selected target";
                debugSolution = rejection;
                return false;
            }

            Vector2 origin = transform.position;
            Vector2 currentTargetPoint = target != null
                ? (Vector2)target.transform.position
                : fallbackGroundPoint;
            Vector2 targetVelocity = ResolveVelocity(target);
            float arrivalDelay = SpellAITargetingUtility.EstimateArrivalDelay(
                spell,
                origin,
                currentTargetPoint,
                maximumPredictionSeconds);
            Vector2 predictedPoint = SpellAITargetingUtility.PredictTargetPoint(
                currentTargetPoint,
                targetVelocity,
                arrivalDelay,
                maximumLeadDistance);

            bool needsPoint =
                (requirements & CastTargetingRequirement.TargetPoint) != 0;
            if (!needsPoint)
            {
                CastContext requested = BuildContext(
                    requirements,
                    gameObject,
                    target,
                    predictedPoint,
                    origin);
                if (!spell.TryResolveContext(requested, out resolved, out rejection))
                {
                    debugSolution = rejection;
                    return false;
                }

                solutionScore = 1f;
                debugChosenPoint = predictedPoint;
                debugCandidateCount = 1;
                debugSolution = needsSelectedTarget
                    ? $"Selected target: {target.name}"
                    : "Directional/self context validated";
                return true;
            }

            SpellAIPlacementIntent intent = ResolvePlacementIntent(spell);
            SpellAITargetingUtility.BuildPointCandidates(
                pointCandidates,
                currentTargetPoint,
                predictedPoint,
                targetVelocity,
                pointSampleRadius,
                radialSampleCount,
                intent);
            debugCandidateCount = pointCandidates.Count;

            for (int i = 0; i < pointCandidates.Count; i++)
            {
                Vector2 candidate = pointCandidates[i];
                CastContext requested = BuildContext(
                    requirements,
                    gameObject,
                    target,
                    candidate,
                    origin);
                if (!spell.TryResolveContext(
                        requested,
                        out CastContext candidateContext,
                        out string candidateRejection))
                {
                    rejection = candidateRejection;
                    continue;
                }

                float score = ScorePoint(
                    candidate,
                    currentTargetPoint,
                    predictedPoint,
                    targetVelocity,
                    intent,
                    pointSampleRadius);
                if (score <= solutionScore)
                    continue;

                solutionScore = score;
                resolved = candidateContext;
                rejection = string.Empty;
            }

            if (float.IsNegativeInfinity(solutionScore))
            {
                debugSolution = string.IsNullOrWhiteSpace(rejection)
                    ? "No point candidate passed the spell's cast rules"
                    : rejection;
                return false;
            }

            debugChosenPoint = resolved.TargetPoint;
            debugSolution =
                $"{intent}: {debugCandidateCount} candidates, chose {debugChosenPoint}";
            return true;
        }

        private GameObject ResolvePreferredTarget(
            SpellDefinition spell,
            GameObject preferredTarget)
        {
            SpellAITargetPreference preference = spell.AIAffordance.TargetPreference;
            if (preference == SpellAITargetPreference.Self ||
                spell.AIAffordance.PlacementIntent ==
                    SpellAIPlacementIntent.ProtectSelf)
                return gameObject;
            return preferredTarget;
        }

        private static Vector2 ResolveVelocity(GameObject target)
        {
            if (target == null)
                return Vector2.zero;
            Rigidbody2D body = target.GetComponentInParent<Rigidbody2D>();
            return body != null ? body.linearVelocity : Vector2.zero;
        }

        private static CastContext BuildContext(
            CastTargetingRequirement requirements,
            GameObject caster,
            GameObject target,
            Vector2 point,
            Vector2 origin)
        {
            Vector2 direction = point - origin;
            bool hasDirection =
                (requirements & CastTargetingRequirement.Direction) != 0 &&
                direction.sqrMagnitude > 0.000001f;
            bool hasPoint =
                (requirements & CastTargetingRequirement.TargetPoint) != 0;
            bool hasTarget =
                (requirements & CastTargetingRequirement.SelectedTarget) != 0;

            return new CastContext(
                caster,
                CombatTeamMember.ResolveTeam(caster, CombatTeam.Enemy),
                origin,
                direction,
                hasDirection,
                point,
                hasPoint,
                hasTarget ? target : null);
        }

        private static SpellAIPlacementIntent ResolvePlacementIntent(
            SpellDefinition spell)
        {
            SpellAIPlacementIntent authored = spell.AIAffordance.PlacementIntent;
            if (authored != SpellAIPlacementIntent.Auto)
                return authored;

            SpellAIIntent intents = spell.AIAffordance.Intents;
            if ((intents & SpellAIIntent.Escape) != 0)
                return SpellAIPlacementIntent.ProtectSelf;
            if ((intents & SpellAIIntent.Control) != 0)
                return SpellAIPlacementIntent.ControlEscapeRoute;
            return SpellAIPlacementIntent.LeadMovingTarget;
        }

        private static float ScorePoint(
            Vector2 candidate,
            Vector2 current,
            Vector2 predicted,
            Vector2 velocity,
            SpellAIPlacementIntent intent,
            float sampleRadius)
        {
            Vector2 desired = predicted;
            if (intent == SpellAIPlacementIntent.DirectHit)
                desired = current;
            else if (intent == SpellAIPlacementIntent.ProtectSelf)
                desired = current;
            else if (intent == SpellAIPlacementIntent.ControlEscapeRoute &&
                velocity.sqrMagnitude > 0.000001f)
            {
                desired = predicted + velocity.normalized *
                    Mathf.Max(0f, sampleRadius);
            }

            float score = 1f /
                (1f + Vector2.Distance(candidate, desired));
            if (intent == SpellAIPlacementIntent.ControlEscapeRoute &&
                velocity.sqrMagnitude > 0.000001f)
            {
                Vector2 ahead = candidate - predicted;
                score += Mathf.Max(0f,
                    Vector2.Dot(ahead.normalized, velocity.normalized)) * 0.1f;
            }
            return score;
        }
    }
}
