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
        [Tooltip("The Spell Definition queued when this effect runs.")]
        [SerializeField]
        private SpellDefinition secondarySpell;

        [Tooltip("Original Caster uses the current spell's caster. Effect Target makes the recipient cast the secondary spell.")]
        [SerializeField]
        private TriggeredSpellRunnerSource runnerSource =
            TriggeredSpellRunnerSource.OriginalCaster;

        [Tooltip("Which object becomes the secondary spell's selected target, if any.")]
        [SerializeField]
        private TriggeredSpellTargetSource targetSource =
            TriggeredSpellTargetSource.EffectTarget;

        [Tooltip("Use the current effect's hit or event point as the secondary spell's target point. Disable this to preserve the original cast point.")]
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
        [Tooltip("Default secondary spell copied into inline settings.")]
        [SerializeField]
        private SpellDefinition secondarySpell;

        [Tooltip("Default choice for which object owns and runs the secondary cast.")]
        [SerializeField]
        private TriggeredSpellRunnerSource runnerSource =
            TriggeredSpellRunnerSource.OriginalCaster;

        [Tooltip("Default choice for the secondary spell's selected target.")]
        [SerializeField]
        private TriggeredSpellTargetSource targetSource =
            TriggeredSpellTargetSource.EffectTarget;

        [Tooltip("Default choice for whether the current hit point becomes the secondary spell's target point.")]
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

        public override bool CanApplyWithoutRecipient(
            SpellEffectSettings settings)
        {
            TriggerSpellEffectSettings resolved =
                settings as TriggerSpellEffectSettings ??
                (TriggerSpellEffectSettings)CreateDefaultSettings();
            return resolved.RunnerSource ==
                   TriggeredSpellRunnerSource.OriginalCaster;
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

        public override string DescribeApplicationFailure(
            in SpellEffectContext context,
            SpellEffectSettings settings)
        {
            TriggerSpellEffectSettings resolved =
                settings as TriggerSpellEffectSettings ??
                (TriggerSpellEffectSettings)CreateDefaultSettings();
            if (resolved.SecondarySpell == null)
                return "No secondary Spell Definition is assigned.";

            GameObject runnerObject = resolved.RunnerSource ==
                                      TriggeredSpellRunnerSource.EffectTarget
                ? context.Target
                : context.Cast.Caster;
            if (runnerObject == null ||
                runnerObject.GetComponentInParent<SpellRunner>() == null)
            {
                return "The selected Triggered Spell source has no " +
                       "SpellRunner.";
            }

            return "The SpellRunner rejected the triggered cast. Check the " +
                   "chain budget and secondary spell requirements.";
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
