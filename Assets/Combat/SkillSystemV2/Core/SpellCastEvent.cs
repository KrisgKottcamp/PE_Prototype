namespace ProjectEri.SkillSystemV2
{
    public readonly struct SpellCastEvent
    {
        public SpellDefinition Spell { get; }
        public CastContext Context { get; }
        public SpellCastPhase Phase { get; }
        public string Reason { get; }

        public SpellCastEvent(
            SpellDefinition spell,
            in CastContext context,
            SpellCastPhase phase,
            string reason = "")
        {
            Spell = spell;
            Context = context;
            Phase = phase;
            Reason = reason ?? string.Empty;
        }
    }
}
