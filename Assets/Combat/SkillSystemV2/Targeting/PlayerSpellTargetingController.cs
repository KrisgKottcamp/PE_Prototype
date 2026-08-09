using System;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpellRunner))]
    [RequireComponent(typeof(TargetingTimeScaleController))]
    public sealed class PlayerSpellTargetingController : MonoBehaviour
    {
        [SerializeField]
        private SpellRunner spellRunner;

        [SerializeField]
        private Transform castOrigin;

        [Tooltip("Optional component implementing ITargetingTimeController.")]
        [SerializeField]
        private MonoBehaviour timeControllerSource;

        [SerializeField]
        private bool autoConfirmImmediateTargeting = true;

        [SerializeField]
        private bool keepAimingAfterRejectedCast = true;

        private ITargetingTimeController timeController;
        private SpellDefinition activeSpell;
        private PlayerTargetingDefinition activeDefinition;
        private CastContext currentContext;
        private PlayerTargetingPreview currentPreview;
        private bool canConfirm;
        private string validationMessage = string.Empty;
        private int beganOnFrame = -1;

        public event Action<PlayerTargetingEvent> TargetingStarted;
        public event Action<PlayerTargetingEvent> TargetingUpdated;
        public event Action<PlayerTargetingEvent> TargetingCancelled;
        public event Action<PlayerTargetingEvent> TargetingConfirmed;
        public event Action<SpellCastFailure> CastRejected;

        public bool IsTargeting => activeSpell != null;
        public bool CanConfirm => IsTargeting && canConfirm;
        public SpellDefinition ActiveSpell => activeSpell;
        public CastContext CurrentContext => currentContext;
        public PlayerTargetingPreview CurrentPreview => currentPreview;
        public string ValidationMessage => validationMessage;
        public int BeganOnFrame => beganOnFrame;

        private Vector2 Origin => castOrigin != null
            ? (Vector2)castOrigin.position
            : (Vector2)transform.position;

        private void Awake()
        {
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();

            ResolveTimeController();
        }

        private void OnDisable()
        {
            CancelTargeting();
        }

        public bool BeginTargeting(
            SpellDefinition spell,
            out PlayerTargetingFailure failure)
        {
            if (IsTargeting)
            {
                failure = PlayerTargetingFailure.AlreadyTargeting;
                return false;
            }

            if (spell == null)
            {
                failure = PlayerTargetingFailure.MissingSpell;
                return false;
            }

            if (spellRunner == null)
            {
                failure = PlayerTargetingFailure.MissingRunner;
                return false;
            }

            if (spell.Delivery == null)
            {
                failure = PlayerTargetingFailure.MissingDelivery;
                return false;
            }

            PlayerTargetingDefinition definition =
                spell.Delivery.PlayerTargeting;

            if (definition == null)
            {
                failure = PlayerTargetingFailure.MissingTargetingDefinition;
                return false;
            }

            if (!definition.Supports(spell.Delivery.TargetingRequirement))
            {
                failure = PlayerTargetingFailure.IncompatibleTargetingDefinition;
                return false;
            }

            activeSpell = spell;
            activeDefinition = definition;
            beganOnFrame = Time.frameCount;
            ResolveTimeController();
            timeController?.Acquire(this, definition.AimTimeScale);

            EvaluateAim(Origin, null, notify: false);

            var targetingEvent = new PlayerTargetingEvent(
                activeSpell,
                currentContext,
                currentPreview);
            TargetingStarted?.Invoke(targetingEvent);

            failure = PlayerTargetingFailure.None;

            if (autoConfirmImmediateTargeting &&
                definition.ProvidedRequirements == CastTargetingRequirement.None)
            {
                return ConfirmTargeting(out failure, out _);
            }

            return true;
        }

        public bool UpdateAim(
            Vector2 pointerWorldPosition,
            GameObject selectedTarget = null)
        {
            return EvaluateAim(
                pointerWorldPosition,
                selectedTarget,
                notify: true);
        }

        private bool EvaluateAim(
            Vector2 pointerWorldPosition,
            GameObject selectedTarget,
            bool notify)
        {
            if (!IsTargeting || activeDefinition == null)
                return false;

            var request = new PlayerTargetingRequest(
                activeSpell,
                gameObject,
                Origin,
                pointerWorldPosition,
                selectedTarget);

            canConfirm = activeDefinition.TryBuildContext(
                request,
                out currentContext,
                out currentPreview,
                out validationMessage);

            if (notify)
            {
                TargetingUpdated?.Invoke(new PlayerTargetingEvent(
                    activeSpell,
                    currentContext,
                    currentPreview));
            }

            return canConfirm;
        }

        public bool ConfirmTargeting(
            out PlayerTargetingFailure targetingFailure,
            out SpellCastFailure castFailure)
        {
            castFailure = SpellCastFailure.None;

            if (!IsTargeting || !canConfirm)
            {
                targetingFailure = PlayerTargetingFailure.InvalidAim;
                return false;
            }

            SpellDefinition spell = activeSpell;
            CastContext context = currentContext;
            PlayerTargetingPreview preview = currentPreview;

            if (!spellRunner.TryCast(spell, context, out castFailure))
            {
                targetingFailure = PlayerTargetingFailure.CastRejected;
                CastRejected?.Invoke(castFailure);

                if (!keepAimingAfterRejectedCast)
                    ClearTargeting();

                return false;
            }

            ClearTargeting();
            TargetingConfirmed?.Invoke(new PlayerTargetingEvent(
                spell,
                context,
                preview));
            targetingFailure = PlayerTargetingFailure.None;
            return true;
        }

        public bool CancelTargeting()
        {
            if (!IsTargeting)
                return false;

            SpellDefinition spell = activeSpell;
            CastContext context = currentContext;
            PlayerTargetingPreview preview = currentPreview;
            ClearTargeting();
            TargetingCancelled?.Invoke(new PlayerTargetingEvent(
                spell,
                context,
                preview));
            return true;
        }

        private void ClearTargeting()
        {
            timeController?.Release(this);
            activeSpell = null;
            activeDefinition = null;
            currentContext = default;
            currentPreview = default;
            canConfirm = false;
            validationMessage = string.Empty;
            beganOnFrame = -1;
        }

        private void ResolveTimeController()
        {
            if (timeControllerSource is ITargetingTimeController assigned)
            {
                timeController = assigned;
                return;
            }

            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ITargetingTimeController match)
                {
                    timeController = match;
                    timeControllerSource = behaviours[i];
                    return;
                }
            }
        }
    }
}
