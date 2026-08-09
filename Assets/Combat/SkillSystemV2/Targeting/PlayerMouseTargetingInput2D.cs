using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerSpellTargetingController))]
    public sealed class PlayerMouseTargetingInput2D : MonoBehaviour
    {
        [SerializeField]
        private PlayerSpellTargetingController targetingController;

        [SerializeField]
        private Camera worldCamera;

        [SerializeField]
        private bool automaticallyUseMainCamera = true;

        [SerializeField]
        private LayerMask selectableLayers = ~0;

        [SerializeField, Min(1)]
        private int overlapBufferSize = 16;

        [Header("Confirmation")]
        [SerializeField]
        private int confirmMouseButton;

        [SerializeField]
        private int cancelMouseButton = 1;

        [SerializeField]
        private bool escapeCancels = true;

        private Collider2D[] overlapBuffer;

        private void Awake()
        {
            if (targetingController == null)
                targetingController = GetComponent<PlayerSpellTargetingController>();

            EnsureBuffer();
        }

        private void Update()
        {
            if (targetingController == null ||
                !targetingController.IsTargeting)
            {
                return;
            }

            RefreshCamera();
            if (worldCamera == null)
                return;

            Vector2 pointerWorldPosition = ScreenToWorld(Input.mousePosition);
            GameObject selectedTarget = ResolveTarget(pointerWorldPosition);
            targetingController.UpdateAim(pointerWorldPosition, selectedTarget);

            bool beganThisFrame = targetingController.BeganOnFrame ==
                                  Time.frameCount;

            if (!beganThisFrame && Input.GetMouseButtonDown(confirmMouseButton))
            {
                targetingController.ConfirmTargeting(out _, out _);
            }
            else if (Input.GetMouseButtonDown(cancelMouseButton) ||
                     (escapeCancels && Input.GetKeyDown(KeyCode.Escape)))
            {
                targetingController.CancelTargeting();
            }
        }

        private Vector2 ScreenToWorld(Vector3 screenPosition)
        {
            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            Plane plane = new Plane(
                Vector3.forward,
                new Vector3(0f, 0f, transform.position.z));

            return plane.Raycast(ray, out float distance)
                ? (Vector2)ray.GetPoint(distance)
                : (Vector2)transform.position;
        }

        private GameObject ResolveTarget(Vector2 worldPosition)
        {
            EnsureBuffer();
            var filter = new ContactFilter2D();
            filter.SetLayerMask(selectableLayers);
            filter.useTriggers = Physics2D.queriesHitTriggers;
            int count = Physics2D.OverlapPoint(
                worldPosition,
                filter,
                overlapBuffer);

            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = overlapBuffer[i];
                if (candidate == null)
                    continue;

                GameObject resolved = SpellTargetResolver.Resolve(
                    candidate.gameObject);

                if (resolved != null &&
                    !SpellTargetResolver.IsSameHierarchy(
                        gameObject,
                        resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        private void RefreshCamera()
        {
            if (automaticallyUseMainCamera &&
                (worldCamera == null || !worldCamera.isActiveAndEnabled))
            {
                worldCamera = Camera.main;
            }
        }

        private void EnsureBuffer()
        {
            int size = Mathf.Max(1, overlapBufferSize);
            if (overlapBuffer == null || overlapBuffer.Length != size)
                overlapBuffer = new Collider2D[size];
        }

        private void OnValidate()
        {
            overlapBufferSize = Mathf.Max(1, overlapBufferSize);
        }
    }
}
