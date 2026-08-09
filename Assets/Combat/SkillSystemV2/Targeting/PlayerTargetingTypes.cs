using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum PlayerTargetingPreviewShape
    {
        None,
        Line,
        Circle,
        Cone,
        Target
    }

    public enum PlayerTargetingFailure
    {
        None,
        MissingSpell,
        MissingRunner,
        MissingDelivery,
        MissingTargetingDefinition,
        IncompatibleTargetingDefinition,
        AlreadyTargeting,
        InvalidAim,
        CastRejected
    }

    public readonly struct PlayerTargetingRequest
    {
        public SpellDefinition Spell { get; }
        public GameObject Caster { get; }
        public Vector2 Origin { get; }
        public Vector2 PointerWorldPosition { get; }
        public GameObject SelectedTarget { get; }

        public TargetFilter TargetFilter => Spell != null
            ? Spell.TargetFilter
            : new TargetFilter(TargetRelationship.Any);

        public PlayerTargetingRequest(
            SpellDefinition spell,
            GameObject caster,
            Vector2 origin,
            Vector2 pointerWorldPosition,
            GameObject selectedTarget)
        {
            Spell = spell;
            Caster = caster;
            Origin = origin;
            PointerWorldPosition = pointerWorldPosition;
            SelectedTarget = selectedTarget;
        }
    }

    public readonly struct PlayerTargetingPreview
    {
        public PlayerTargetingPreviewShape Shape { get; }
        public Vector2 Origin { get; }
        public Vector2 AimPoint { get; }
        public Vector2 Direction { get; }
        public float Range { get; }
        public float Radius { get; }
        public float ConeAngle { get; }
        public GameObject SelectedTarget { get; }
        public bool IsValid { get; }
        public string ValidationMessage { get; }

        public PlayerTargetingPreview(
            PlayerTargetingPreviewShape shape,
            Vector2 origin,
            Vector2 aimPoint,
            Vector2 direction,
            float range,
            float radius,
            float coneAngle,
            GameObject selectedTarget,
            bool isValid,
            string validationMessage)
        {
            Shape = shape;
            Origin = origin;
            AimPoint = aimPoint;
            Direction = direction;
            Range = Mathf.Max(0f, range);
            Radius = Mathf.Max(0f, radius);
            ConeAngle = Mathf.Clamp(coneAngle, 0f, 360f);
            SelectedTarget = selectedTarget;
            IsValid = isValid;
            ValidationMessage = validationMessage ?? string.Empty;
        }
    }

    public readonly struct PlayerTargetingEvent
    {
        public SpellDefinition Spell { get; }
        public CastContext Context { get; }
        public PlayerTargetingPreview Preview { get; }

        public PlayerTargetingEvent(
            SpellDefinition spell,
            in CastContext context,
            in PlayerTargetingPreview preview)
        {
            Spell = spell;
            Context = context;
            Preview = preview;
        }
    }
}
