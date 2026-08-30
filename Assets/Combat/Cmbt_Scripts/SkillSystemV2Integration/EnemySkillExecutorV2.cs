using ProjectEri.SkillSystemV2;
using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpellRunner))]
    [RequireComponent(typeof(EnemySpellResourceProviderV2))]
    [RequireComponent(typeof(SpellBuildUpControl2D))]
    public sealed class EnemySkillExecutorV2 : MonoBehaviour
    {
        [SerializeField] private SpellRunner spellRunner;
        [SerializeField] private string debugSpell = "None";
        [SerializeField] private string debugResult = "Idle";

        private SpellDefinition activeSpell;
        private bool running;
        private bool succeeded;
        private bool failed;
        private SpellAIComboReservation activeComboReservation;

        public bool IsRunning => running;
        public string DebugResult => debugResult;

        private void Awake()
        {
            EnsureResourceProvider();
            EnsureBuildUpVisual();
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();
        }

        private void OnEnable()
        {
            EnsureResourceProvider();
            EnsureBuildUpVisual();
            if (spellRunner == null)
                spellRunner = GetComponent<SpellRunner>();
            if (spellRunner == null)
                return;
            spellRunner.CastCompleted += HandleCompleted;
            spellRunner.CastInterrupted += HandleInterrupted;
        }

        private void EnsureResourceProvider()
        {
            MonoBehaviour[] behaviours =
                GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpellResourceProvider)
                    return;
            }

            gameObject.AddComponent<EnemySpellResourceProviderV2>();
        }

        private void EnsureBuildUpVisual()
        {
            if (GetComponent<SpellBuildUpControl2D>() == null)
                gameObject.AddComponent<SpellBuildUpControl2D>();
        }

        private void OnDisable()
        {
            if (spellRunner != null)
            {
                spellRunner.CastCompleted -= HandleCompleted;
                spellRunner.CastInterrupted -= HandleInterrupted;
            }
            SpellAIComboCoordinator.ReleaseReservation(
                activeComboReservation,
                gameObject);
            activeComboReservation = default;
            running = false;
            activeSpell = null;
        }

        public bool BeginSkill(
            SpellDefinition spell,
            in CastContext context,
            in SpellAIComboReservation comboReservation)
        {
            if (spellRunner == null || spell == null || running)
            {
                debugResult = spellRunner == null
                    ? "Missing SpellRunner"
                    : spell == null
                        ? "Missing spell"
                        : "Skill executor already running";
                return false;
            }

            succeeded = false;
            failed = false;
            activeSpell = spell;
            activeComboReservation = comboReservation;
            debugSpell = spell.DisplayName;
            running = true;
            if (!spellRunner.TryCast(spell, context, out SpellCastFailure failure))
            {
                running = false;
                SpellAIComboCoordinator.ReleaseReservation(
                    activeComboReservation,
                    gameObject);
                activeComboReservation = default;
                activeSpell = null;
                failed = true;
                debugResult = $"Cast rejected: {failure}";
                return false;
            }

            if (running)
                debugResult = "Casting";
            return true;
        }

        public EnemyActionStatusV2 TickSkill(float timeoutSeconds, float elapsed)
        {
            if (succeeded)
                return EnemyActionStatusV2.Succeeded;
            if (failed)
                return EnemyActionStatusV2.Failed;
            if (!running)
                return EnemyActionStatusV2.Failed;
            if (elapsed < Mathf.Max(0.1f, timeoutSeconds))
                return EnemyActionStatusV2.Running;

            CancelSkill("Skill action timeout");
            failed = true;
            return EnemyActionStatusV2.Failed;
        }

        public void CancelSkill(string reason = "Skill action cancelled")
        {
            if (running && spellRunner != null &&
                spellRunner.ActiveSpell == activeSpell)
            {
                spellRunner.Interrupt(reason);
            }
            running = false;
            activeSpell = null;
            debugResult = reason;
            if (!running)
            {
                SpellAIComboCoordinator.ReleaseReservation(
                    activeComboReservation,
                    gameObject);
                activeComboReservation = default;
            }
        }

        private void HandleCompleted(SpellCastEvent castEvent)
        {
            if (!running || castEvent.Spell != activeSpell)
                return;
            running = false;
            succeeded = true;
            debugResult = "Cast completed";
            SpellAIComboCoordinator.CommitReservation(
                activeComboReservation,
                activeSpell,
                gameObject);
            activeComboReservation = default;
            activeSpell = null;
        }

        private void HandleInterrupted(SpellCastEvent castEvent)
        {
            if (!running || castEvent.Spell != activeSpell)
                return;
            running = false;
            failed = true;
            debugResult = string.IsNullOrWhiteSpace(castEvent.Reason)
                ? "Cast interrupted"
                : castEvent.Reason;
            SpellAIComboCoordinator.ReleaseReservation(
                activeComboReservation,
                gameObject);
            activeComboReservation = default;
            activeSpell = null;
        }
    }
}
