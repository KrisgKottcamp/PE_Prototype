using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CombatSlowZoneV2 : MonoBehaviour
    {
        [Header("Slow Orb / Slow Zone")]
        [Tooltip("Movement multiplier applied to V2 enemies while they are inside this trigger. 0.25 means 25% normal movement speed.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float enemyMovementMultiplier = 0.25f;

        [Tooltip("How long the slow remains after the last trigger stay. Keep slightly above FixedUpdate so destroyed/moving zones do not flicker.")]
        [Min(0.02f)]
        [SerializeField] private float lingerSeconds = 0.16f;

        [Tooltip("Apply slow on trigger enter. Usually on.")]
        [SerializeField] private bool applyOnEnter = true;

        [Tooltip("Apply slow on trigger stay. This is the most important setting; keep on so enemies remain slowed every frame while inside the orb.")]
        [SerializeField] private bool applyOnStay = true;

        [Tooltip("If off, the slow naturally lingers after exit. If on, enemies immediately recover speed when leaving the orb.")]
        [SerializeField] private bool clearOnExit = false;

        [Tooltip("Require EnemyAgentV2 on the receiver root. Keep on so unrelated objects with EnemySlowReceiverV2 are not affected accidentally.")]
        [SerializeField] private bool requireEnemyAgentV2 = true;

        [Header("Player Slow / Speed Boost Stacking")]
        [Tooltip("If enabled, this same zone also applies a player slow through PlayerMoveSpeedModifierReceiverV2. CombatPawnMover multiplies this with SpeedModifier, so Speed Boost can stack with Slow Orb.")]
        [SerializeField] private bool affectPlayer = true;

        [Tooltip("Movement multiplier applied to the combat player while inside this trigger. This stacks multiplicatively with Speed Boost instead of overwriting it.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float playerMovementMultiplier = 0.25f;

        [Tooltip("Require CombatPawnMover on the receiver root. Recommended so random triggers are not slowed as the player.")]
        [SerializeField] private bool requireCombatPawnMover = true;

        [Tooltip("If the player does not already have PlayerMoveSpeedModifierReceiverV2, add it automatically to the CombatPawnMover object.")]
        [SerializeField] private bool autoAddPlayerReceiver = true;

        [Header("Runtime Debug")]
        [SerializeField] private int debugAffectedThisFrame;
        [SerializeField] private string debugLastAffected = "None";
        [SerializeField] private string debugLastPlayerAffected = "None";
        [SerializeField] private bool logApplications = false;

        private Collider2D zoneCollider;
        private int affectedFrame;

        private void Reset()
        {
            zoneCollider = GetComponent<Collider2D>();
            if (zoneCollider != null)
                zoneCollider.isTrigger = true;
        }

        private void Awake()
        {
            if (zoneCollider == null)
                zoneCollider = GetComponent<Collider2D>();

            if (zoneCollider != null && !zoneCollider.isTrigger)
                zoneCollider.isTrigger = true;
        }

        private void LateUpdate()
        {
            if (affectedFrame != Time.frameCount)
                debugAffectedThisFrame = 0;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (applyOnEnter)
                ApplyTo(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (applyOnStay)
                ApplyTo(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!clearOnExit)
                return;

            EnemySlowReceiverV2 enemyReceiver = FindEnemyReceiver(other);
            if (enemyReceiver != null)
                enemyReceiver.ClearSlow(this);

            PlayerMoveSpeedModifierReceiverV2 playerReceiver = FindPlayerReceiver(other);
            if (playerReceiver != null)
                playerReceiver.ClearSource(this);
        }

        private void ApplyTo(Collider2D other)
        {
            bool appliedAny = false;

            EnemySlowReceiverV2 enemyReceiver = FindEnemyReceiver(other);
            if (enemyReceiver != null)
            {
                enemyReceiver.ApplySlow(this, enemyMovementMultiplier, lingerSeconds);
                debugLastAffected = enemyReceiver.name;
                appliedAny = true;

                if (logApplications)
                {
                    Debug.Log(
                        $"[Enemy AI V2] {name}: slowing enemy {enemyReceiver.name} x{enemyMovementMultiplier:0.00}",
                        this
                    );
                }
            }

            if (affectPlayer)
            {
                PlayerMoveSpeedModifierReceiverV2 playerReceiver = FindPlayerReceiver(other);
                if (playerReceiver != null)
                {
                    playerReceiver.ApplySlow(this, playerMovementMultiplier, lingerSeconds);
                    debugLastPlayerAffected = playerReceiver.name;
                    appliedAny = true;

                    if (logApplications)
                    {
                        Debug.Log(
                            $"[Enemy AI V2] {name}: slowing player {playerReceiver.name} x{playerMovementMultiplier:0.00}",
                            this
                        );
                    }
                }
            }

            if (!appliedAny)
                return;

            if (affectedFrame != Time.frameCount)
            {
                affectedFrame = Time.frameCount;
                debugAffectedThisFrame = 0;
            }

            debugAffectedThisFrame++;
        }

        private EnemySlowReceiverV2 FindEnemyReceiver(Collider2D other)
        {
            if (other == null)
                return null;

            EnemySlowReceiverV2 receiver = other.GetComponentInParent<EnemySlowReceiverV2>();
            if (receiver == null)
                return null;

            if (requireEnemyAgentV2 && receiver.GetComponent<EnemyAgentV2>() == null)
                return null;

            return receiver;
        }

        private PlayerMoveSpeedModifierReceiverV2 FindPlayerReceiver(Collider2D other)
        {
            if (other == null)
                return null;

            PlayerMoveSpeedModifierReceiverV2 receiver = other.GetComponentInParent<PlayerMoveSpeedModifierReceiverV2>();
            CombatPawnMover mover = other.GetComponentInParent<CombatPawnMover>();

            if (requireCombatPawnMover && mover == null)
                return null;

            if (receiver != null)
                return receiver;

            if (!autoAddPlayerReceiver || mover == null)
                return null;

            return mover.gameObject.AddComponent<PlayerMoveSpeedModifierReceiverV2>();
        }
    }
}
