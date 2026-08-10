using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum TriggeredSpellRunnerSource
    {
        OriginalCaster,
        EffectTarget
    }

    public enum TriggeredSpellTargetSource
    {
        EffectTarget,
        TriggeredCaster,
        None
    }

    [Serializable]
    public sealed class TriggerSpellEffectSettings : SpellEffectSettings
    {
        [SerializeField]
        private SpellDefinition secondarySpell;

        [SerializeField]
        private TriggeredSpellRunnerSource runnerSource =
            TriggeredSpellRunnerSource.OriginalCaster;

        [SerializeField]
        private TriggeredSpellTargetSource targetSource =
            TriggeredSpellTargetSource.EffectTarget;

        [SerializeField]
        private bool useHitPointAsTargetPoint = true;

        public SpellDefinition SecondarySpell => secondarySpell;
        public TriggeredSpellRunnerSource RunnerSource => runnerSource;
        public TriggeredSpellTargetSource TargetSource => targetSource;
        public bool UseHitPointAsTargetPoint => useHitPointAsTargetPoint;

        public TriggerSpellEffectSettings()
        {
        }

        public TriggerSpellEffectSettings(
            SpellDefinition triggeredSpell,
            TriggeredSpellRunnerSource spellRunnerSource,
            TriggeredSpellTargetSource spellTargetSource,
            bool useHitPoint)
        {
            secondarySpell = triggeredSpell;
            runnerSource = spellRunnerSource;
            targetSource = spellTargetSource;
            useHitPointAsTargetPoint = useHitPoint;
        }
    }

    [CreateAssetMenu(
        fileName = "Effect_TriggerSpell",
        menuName = "Project Eri/Skill System V2/Effects/Trigger Secondary Spell")]
    public sealed class TriggerSpellEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private SpellDefinition secondarySpell;

        [SerializeField]
        private TriggeredSpellRunnerSource runnerSource =
            TriggeredSpellRunnerSource.OriginalCaster;

        [SerializeField]
        private TriggeredSpellTargetSource targetSource =
            TriggeredSpellTargetSource.EffectTarget;

        [SerializeField]
        private bool useHitPointAsTargetPoint = true;

        public override Type SettingsType =>
            typeof(TriggerSpellEffectSettings);

        public override SpellEffectSettings CreateDefaultSettings()
        {
            return new TriggerSpellEffectSettings(
                secondarySpell,
                runnerSource,
                targetSource,
                useHitPointAsTargetPoint);
        }

        public override bool Apply(in SpellEffectContext context)
        {
            return Apply(context, CreateDefaultSettings());
        }

        public override bool Apply(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            TriggerSpellEffectSettings resolved =
                settings as TriggerSpellEffectSettings ??
                (TriggerSpellEffectSettings)CreateDefaultSettings();
            if (resolved.SecondarySpell == null)
                return false;

            GameObject runnerObject = resolved.RunnerSource ==
                                      TriggeredSpellRunnerSource.EffectTarget
                ? context.Target
                : context.Cast.Caster;
            SpellRunner runner = runnerObject != null
                ? runnerObject.GetComponentInParent<SpellRunner>()
                : null;
            if (runner == null)
                return false;

            GameObject selectedTarget;
            switch (resolved.TargetSource)
            {
                case TriggeredSpellTargetSource.TriggeredCaster:
                    selectedTarget = runner.gameObject;
                    break;
                case TriggeredSpellTargetSource.None:
                    selectedTarget = null;
                    break;
                default:
                    selectedTarget = context.Target;
                    break;
            }

            Vector2 origin = runner.transform.position;
            Vector2 targetPoint = resolved.UseHitPointAsTargetPoint
                ? context.HitPoint
                : context.Cast.HasTargetPoint
                    ? context.Cast.TargetPoint
                    : context.HitPoint;
            Vector2 direction = selectedTarget != null
                ? (Vector2)selectedTarget.transform.position - origin
                : targetPoint - origin;

            if (direction.sqrMagnitude <= 0.000001f &&
                context.Cast.HasAimDirection)
            {
                direction = context.Cast.AimDirection;
            }

            var childContext = new CastContext(
                runner.gameObject,
                CombatTeamMember.ResolveTeam(
                    runner.gameObject,
                    context.Cast.CasterTeam),
                origin,
                direction,
                direction.sqrMagnitude > 0.000001f,
                targetPoint,
                resolved.UseHitPointAsTargetPoint || context.Cast.HasTargetPoint,
                selectedTarget,
                context.Cast.ChainBudget,
                context.Cast.ChainDepth + 1);

            return runner.QueueTriggeredCast(
                resolved.SecondarySpell,
                childContext,
                out _);
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            CollectValidationIssues(issues, CreateDefaultSettings());
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues,
            SpellEffectSettings settings)
        {
            TriggerSpellEffectSettings resolved =
                settings as TriggerSpellEffectSettings ??
                (TriggerSpellEffectSettings)CreateDefaultSettings();
            if (resolved.SecondarySpell == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Trigger Spell effect '{DisplayName}' needs a secondary spell."));
            }
        }
    }
}
