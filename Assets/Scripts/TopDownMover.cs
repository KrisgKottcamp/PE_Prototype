using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class TopDownMover : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float skinWidth = 0.03f;

    [Header("Layers")]
    [SerializeField] private LayerMask obstaclesMask;
    [SerializeField] private LayerMask walkAreaMask; // set this to WalkArea layer in Inspector

    [Header("Debug")]
    [SerializeField] private bool logBinding = true;

    private Rigidbody2D rb;
    private Vector2 moveInput;

    private PolygonCollider2D walkArea;

    private readonly RaycastHit2D[] castResults = new RaycastHit2D[8];
    private ContactFilter2D castFilter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        castFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstaclesMask,
            useTriggers = false
        };
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindWalkArea();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindWalkArea();
    }

    private void BindWalkArea()
    {
        walkArea = null;

        // Find all PolygonCollider2D in scene, pick the one on WalkArea layer (mask).
        var polys = FindObjectsOfType<PolygonCollider2D>(true);

        Vector2 p = rb ? rb.position : (Vector2)transform.position;

        float bestScore = float.PositiveInfinity;
        PolygonCollider2D best = null;

        for (int i = 0; i < polys.Length; i++)
        {
            var pc = polys[i];
            int layerBit = 1 << pc.gameObject.layer;

            if ((walkAreaMask.value & layerBit) == 0)
                continue;

            // Prefer the collider that contains the player. Otherwise pick closest.
            float score = pc.OverlapPoint(p) ? -100000f : (pc.ClosestPoint(p) - p).sqrMagnitude;

            if (score < bestScore)
            {
                bestScore = score;
                best = pc;
            }
        }

        walkArea = best;

        if (logBinding)
        {
            if (walkArea == null)
                Debug.LogWarning("TopDownMover: No WalkArea PolygonCollider2D found on WalkArea layer. Clamping is OFF.");
            else
                Debug.Log($"TopDownMover: Bound WalkArea = {walkArea.name}");
        }
    }

    private void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(x, y).normalized;
    }

    private void FixedUpdate()
    {
        Vector2 currentPos = rb.position;

        if (moveInput == Vector2.zero)
        {
            rb.MovePosition(ClampToWalkArea(currentPos));
            return;
        }

        float moveDistance = moveSpeed * Time.fixedDeltaTime;
        Vector2 dir = moveInput;

        // Block against obstacles
        int hitCount = rb.Cast(dir, castFilter, castResults, moveDistance + skinWidth);

        float allowedDistance = moveDistance;
        for (int i = 0; i < hitCount; i++)
        {
            float d = castResults[i].distance - skinWidth;
            if (d < allowedDistance) allowedDistance = d;
        }
        if (allowedDistance < 0f) allowedDistance = 0f;

        Vector2 targetPos = currentPos + dir * allowedDistance;

        // Clamp intent
        targetPos = ClampToWalkArea(targetPos);

        rb.MovePosition(targetPos);
    }

    private void LateUpdate()
    {
        // Final safety clamp after physics has resolved collisions.
        if (walkArea == null || rb == null) return;

        Vector2 p = rb.position;
        Vector2 c = walkArea.ClosestPoint(p);

        if ((c - p).sqrMagnitude > 0.000001f)
            rb.position = c;
    }

    private Vector2 ClampToWalkArea(Vector2 pos)
    {
        if (walkArea == null) return pos;
        return walkArea.ClosestPoint(pos);
    }
}
