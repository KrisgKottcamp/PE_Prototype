using System;

namespace ProjectEri.SkillSystemV2
{
    [Flags]
    public enum CastTargetingRequirement
    {
        None = 0,
        Direction = 1 << 0,
        TargetPoint = 1 << 1,
        SelectedTarget = 1 << 2
    }

    public enum SpellTimeMode
    {
        Scaled,
        Unscaled
    }

    public enum SpellCastPhase
    {
        Idle,
        BuildUp,
        Firing,
        Channeling,
        Recovery
    }

    public enum SpellCastFailure
    {
        None,
        MissingSpell,
        RunnerBusy,
        InvalidDefinition,
        InvalidContext,
        OnCooldown,
        MissingResourceProvider,
        InsufficientResources,
        ChainBudgetExceeded,
        DeliveryFailed
    }

    public enum SpellValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct SpellValidationIssue
    {
        public SpellValidationSeverity Severity { get; }
        public string Message { get; }

        public SpellValidationIssue(
            SpellValidationSeverity severity,
            string message)
        {
            Severity = severity;
            Message = message;
        }
    }
}
