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
        [Header("References")]
        [SerializeField] private EnemyAgentV2 agent;

        [Header("Prediction")]
        [SerializeField, Min(0f)] private float maximumPredictionSeconds = 1.25f;
        [SerializeField, Min(0f)] private float maximumLeadDistance = 3f;
        [Tooltip("Fallback lead time for instant point/area spells whose authored lookahead is zero.")]
        [SerializeField, Min(0f)] private float defaultInstantLookaheadSeconds =
            0.45f;
        [SerializeField, Range(0.01f, 1f)] private float velocitySmoothing =
            0.45f;
        [SerializeField, Min(0.1f)] private float maximumObservedSpeed = 20f;

        [Header("Point Candidate Sampling")]
        [SerializeField, Min(0f)] private float pointSampleRadius = 0.75f;
        [SerializeField, Range(0, 16)] private int radialSampleCount = 6;

        [Header("Runtime Debug")]
        [SerializeField] private string debugSolution = "Not evaluated";
        [SerializeField] private Vector2 debugChosenPoint;
        [SerializeField] private int debugCandidateCount;
        [SerializeField] private Vector2 debugObservedVelocity;
        [SerializeField] private float debugPredictionSeconds;
        [SerializeField] private Vector2 debugPredictedPoint;

        private readonly List<Vector2> pointCandidates = new List<Vector2>(20);
        private Transform observedTarget;
        private Vector2 lastObservedPosition;
        private float lastObservationTime = -1f;
        private Vector2 sampledVelocity;

        public string DebugSolution => debugSolution;

        private void Awake()
        {
            if (agent == null)
                agent = GetComponent<EnemyAgentV2>();
        }

        private void Update()
        {
            if (agent == null)
                agent = GetComponent<EnemyAgentV2>();
            SampleTarget(agent != null ? agent.PlayerTarget : null);
        }

        public bool TryResolveBestContext(
            SpellDefinition spell,
            GameObject preferredTarget,
            Vector2 fallbackGroundPoint,
            out CastContext resolved,
            out float solutionScore,
            out string rejection,
            bool preferFallbackGroundPoint = false)
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
            Vector2 currentTargetPoint = preferFallbackGroundPoint
                ? fallbackGroundPoint
                : target != null
                    ? (Vector2)target.transform.position
                    : fallbackGroundPoint;
            if (preferredTarget != null)
                SampleTarget(preferredTarget.transform);
            Vector2 targetVelocity = preferFallbackGroundPoint
                ? Vector2.zero
                : ResolveVelocity(target);
            SpellAIPlacementIntent intent = ResolvePlacementIntent(spell);
            float arrivalDelay = SpellAITargetingUtility.EstimateArrivalDelay(
                spell,
                origin,
                currentTargetPoint,
                maximumPredictionSeconds);
            float authoredLookahead =
                spell.AIAffordance.PlacementLookaheadSeconds;
            if (authoredLookahead <= 0f && arrivalDelay <= 0.01f &&
                (intent == SpellAIPlacementIntent.LeadMovingTarget ||
                 intent == SpellAIPlacementIntent.ControlEscapeRoute ||
                 intent == SpellAIPlacementIntent.AffectCluster))
            {
                authoredLookahead = defaultInstantLookaheadSeconds;
            }
            if (intent == SpellAIPlacementIntent.DirectHit ||
                intent == SpellAIPlacementIntent.ProtectSelf ||
                intent == SpellAIPlacementIntent.ComboLocation ||
                preferFallbackGroundPoint)
            {
                authoredLookahead = 0f;
            }
            float predictionSeconds = Mathf.Clamp(
                arrivalDelay + authoredLookahead,
                0f,
                Mathf.Max(0f, maximumPredictionSeconds));
            Vector2 predictedPoint = SpellAITargetingUtility.PredictTargetPoint(
                currentTargetPoint,
                targetVelocity,
                predictionSeconds,
                maximumLeadDistance);
            debugObservedVelocity = targetVelocity;
            debugPredictionSeconds = predictionSeconds;
            debugPredictedPoint = predictedPoint;

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

            float effectiveSampleRadius = Mathf.Max(
                pointSampleRadius,
                SpellAITacticalMemory.EstimatePersistentRadius(spell) *
                0.65f);
            SpellAITargetingUtility.BuildPointCandidates(
                pointCandidates,
                currentTargetPoint,
                predictedPoint,
                targetVelocity,
                effectiveSampleRadius,
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
                    effectiveSampleRadius);
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

        private Vector2 ResolveVelocity(GameObject target)
        {
            if (target == null)
                return Vector2.zero;
            Rigidbody2D body = target.GetComponentInParent<Rigidbody2D>();
            if (body == null)
                body = target.GetComponentInChildren<Rigidbody2D>();
            Vector2 physicsVelocity = body != null
                ? body.linearVelocity
                : Vector2.zero;
            if (physicsVelocity.sqrMagnitude > 0.0001f)
                return Vector2.ClampMagnitude(
                    physicsVelocity,
                    maximumObservedSpeed);

            Transform targetTransform = target.transform;
            bool sameObservedHierarchy = observedTarget != null &&
                (observedTarget == targetTransform ||
                 observedTarget.IsChildOf(targetTransform) ||
                 targetTransform.IsChildOf(observedTarget));
            return sameObservedHierarchy
                ? sampledVelocity
                : Vector2.zero;
        }

        private void SampleTarget(Transform target)
        {
            if (target == null)
            {
                observedTarget = null;
                lastObservationTime = -1f;
                sampledVelocity = Vector2.zero;
                return;
            }

            float now = Time.unscaledTime;
            Vector2 position = target.position;
            if (observedTarget != target || lastObservationTime < 0f)
            {
                observedTarget = target;
                lastObservedPosition = position;
                lastObservationTime = now;
                sampledVelocity = Vector2.zero;
                return;
            }

            float elapsed = now - lastObservationTime;
            if (elapsed <= 0.0001f)
                return;

            Vector2 measured = (position - lastObservedPosition) / elapsed;
            measured = Vector2.ClampMagnitude(
                measured,
                maximumObservedSpeed);
            sampledVelocity = Vector2.Lerp(
                sampledVelocity,
                measured,
                velocitySmoothing);
            lastObservedPosition = position;
            lastObservationTime = now;
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
