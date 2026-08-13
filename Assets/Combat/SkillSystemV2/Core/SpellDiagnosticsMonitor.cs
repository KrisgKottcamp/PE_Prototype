using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    /// <summary>
    /// Add temporarily to any active scene object when diagnosing spell
    /// setup. It converts the central diagnostic stream into readable Console
    /// messages without changing spell behavior.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Project Eri/Skill System V2/Diagnostics Monitor")]
    public sealed class SpellDiagnosticsMonitor : MonoBehaviour
    {
        [Tooltip("Log successful effect groups as well as failures. Leave disabled during normal development to keep the Console quiet.")]
        [SerializeField] private bool logSuccessfulApplications;

        [Tooltip("Log individual effect slots that return false. This commonly identifies a missing receiver on the target prefab.")]
        [SerializeField] private bool logRejectedEffectSlots = true;

        [Tooltip("Log empty and intentionally skipped slots. Useful when diagnosing authoring or migration problems.")]
        [SerializeField] private bool logSkippedEffectSlots;

        private void OnEnable()
        {
            SpellRuntimeDiagnostics.ApplicationCompleted +=
                HandleApplication;
            SpellRuntimeDiagnostics.EffectSlotCompleted +=
                HandleEffectSlot;
        }

        private void OnDisable()
        {
            SpellRuntimeDiagnostics.ApplicationCompleted -=
                HandleApplication;
            SpellRuntimeDiagnostics.EffectSlotCompleted -=
                HandleEffectSlot;
        }

        private void HandleApplication(
            SpellEffectApplicationResult result)
        {
            if (!result.HasProblems && !logSuccessfulApplications)
                return;

            string spellName = result.Spell != null
                ? result.Spell.DisplayName
                : "<missing spell>";
            string targetName = result.ResolvedTarget != null
                ? result.ResolvedTarget.name
                : result.RequestedTarget != null
                    ? result.RequestedTarget.name
                    : "<missing target>";
            string message =
                $"[SkillSystemV2] {spellName} -> {targetName}: " +
                $"{result.Status}. Applied {result.AppliedCount}/" +
                $"{result.AttemptedCount} attempted effects. " +
                result.Message;
            Object context = result.Spell != null
                ? result.Spell
                : result.ResolvedTarget;

            if (result.HasProblems)
                Debug.LogWarning(message, context);
            else
                Debug.Log(message, context);
        }

        private void HandleEffectSlot(SpellEffectSlotDiagnostic diagnostic)
        {
            bool rejected = diagnostic.Status ==
                            SpellEffectSlotStatus.Rejected;
            bool exception = diagnostic.Status ==
                             SpellEffectSlotStatus.Exception;
            bool skipped = diagnostic.Status ==
                           SpellEffectSlotStatus.EmptySlot ||
                           diagnostic.Status ==
                           SpellEffectSlotStatus.PresenceEffectSkipped;

            if ((!rejected || !logRejectedEffectSlots) &&
                (!skipped || !logSkippedEffectSlots) &&
                !exception)
            {
                return;
            }

            string effectName = diagnostic.Effect != null
                ? diagnostic.Effect.DisplayName
                : "<empty effect>";
            string targetName = diagnostic.Target != null
                ? diagnostic.Target.name
                : "<missing target>";
            string message =
                $"[SkillSystemV2] Effect slot {diagnostic.SlotIndex} " +
                $"({effectName}) on {targetName}: " +
                $"{diagnostic.Status}. {diagnostic.Message}";
            Object context = diagnostic.Effect != null
                ? diagnostic.Effect
                : diagnostic.Spell;

            if (exception && diagnostic.Exception != null)
                Debug.LogException(diagnostic.Exception, context);
            else
                Debug.LogWarning(message, context);
        }
    }
}
