using UnityEngine;

/// <summary>
/// Put this component on the same visible red GameObject as the lunge
/// SpriteRenderer and Collider2D. That Collider2D is the authoritative
/// damaging shape. It is disabled during telegraph/fade-out and armed only
/// during the active lunge.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class AttackDogLungeHitbox : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Collider2D hitboxCollider;
    [SerializeField] private SpriteRenderer visualRenderer;

    [Header("Collider Setup")]
    [SerializeField] private bool forceTrigger = true;

    [Tooltip("Optional convenience for Circle, Capsule, or Box colliders when the SpriteRenderer is on this same GameObject.")]
    [SerializeField] private bool autoFitColliderToSpriteOnAwake = false;

    [SerializeField, Min(0.01f)]
    private float colliderFitMultiplier = 1f;

    [Header("Debug")]
    [SerializeField] private bool drawColliderGizmo = true;
    [SerializeField] private bool logAcceptedHits = false;
    [SerializeField] private bool debugDamageActive;
    [SerializeField] private string debugLastHit = "None";

    private AttackDogBrain owner;
    private LayerMask playerHitMask;
    private bool damageActive;
    private bool hasHitThisLunge;

    private ContactFilter2D playerFilter;
    private readonly RaycastHit2D[] castResults = new RaycastHit2D[12];
    private readonly Collider2D[] overlapResults = new Collider2D[12];

    public Collider2D HitboxCollider => hitboxCollider;
    public bool DamageActive => damageActive;

    private void Awake()
    {
        ResolveReferences();
        ConfigureCollider();

        if (autoFitColliderToSpriteOnAwake)
            FitColliderToSprite();

        SetDamageActive(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureCollider();

        if (!damageActive && hitboxCollider != null)
            hitboxCollider.enabled = false;
    }

    private void OnDisable()
    {
        damageActive = false;
        hasHitThisLunge = false;
        debugDamageActive = false;

        if (hitboxCollider != null)
            hitboxCollider.enabled = false;
    }

    private void OnValidate()
    {
        colliderFitMultiplier = Mathf.Max(0.01f, colliderFitMultiplier);
        ResolveReferences();
        ConfigureCollider();
    }

    public void Configure(
        AttackDogBrain attackDogOwner,
        LayerMask allowedPlayerLayers)
    {
        owner = attackDogOwner;
        playerHitMask = allowedPlayerLayers;

        ResolveReferences();
        ConfigureCollider();
        RebuildFilter();
    }

    public void SetDamageActive(bool active)
    {
        ResolveReferences();
        ConfigureCollider();

        damageActive = active;
        debugDamageActive = active;

        if (active)
        {
            hasHitThisLunge = false;
            debugLastHit = "Armed";

            if (hitboxCollider != null)
                hitboxCollider.enabled = true;

            Physics2D.SyncTransforms();
            CheckCurrentOverlap();
        }
        else
        {
            if (hitboxCollider != null)
                hitboxCollider.enabled = false;
        }
    }

    /// <summary>
    /// Sweeps this exact Collider2D shape through a movement delta so a fast
    /// lunge cannot tunnel through the player between physics steps.
    /// </summary>
    public void SweepForDamage(Vector2 worldDelta)
    {
        if (!CanDamage() || worldDelta.sqrMagnitude <= 0.000001f)
            return;

        RebuildFilter();

        float distance = worldDelta.magnitude;
        Vector2 direction = worldDelta / distance;

        int hitCount = hitboxCollider.Cast(
            direction,
            playerFilter,
            castResults,
            distance,
            true
        );

        for (int i = 0; i < hitCount; i++)
        {
            if (TryDamage(castResults[i].collider))
                return;
        }
    }

    public void CheckCurrentOverlap()
    {
        if (!CanDamage())
            return;

        RebuildFilter();
        Physics2D.SyncTransforms();

        int count = hitboxCollider.Overlap(
            playerFilter,
            overlapResults
        );

        for (int i = 0; i < count; i++)
        {
            if (TryDamage(overlapResults[i]))
                return;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamage(other);
    }

    private bool CanDamage()
    {
        return damageActive &&
               !hasHitThisLunge &&
               owner != null &&
               hitboxCollider != null &&
               hitboxCollider.enabled &&
               playerHitMask.value != 0;
    }

    private bool TryDamage(Collider2D candidate)
    {
        if (!CanDamage() || candidate == null)
            return false;

        int candidateLayerBit = 1 << candidate.gameObject.layer;

        if ((playerHitMask.value & candidateLayerBit) == 0)
            return false;

        CombatPawn pawn =
            candidate.GetComponentInParent<CombatPawn>();

        if (pawn == null)
            return false;

        if (!owner.TryApplyLungeDamageFromVisibleHitbox(pawn))
            return false;

        hasHitThisLunge = true;
        debugLastHit = pawn.name;

        if (logAcceptedHits)
        {
            Debug.Log(
                $"AttackDogLungeHitbox hit '{pawn.name}' using '{hitboxCollider.name}'.",
                this
            );
        }

        return true;
    }

    private void ResolveReferences()
    {
        if (hitboxCollider == null)
            hitboxCollider = GetComponent<Collider2D>();

        if (visualRenderer == null)
            visualRenderer = GetComponent<SpriteRenderer>();
    }

    private void ConfigureCollider()
    {
        if (hitboxCollider == null)
            return;

        if (forceTrigger)
            hitboxCollider.isTrigger = true;

        RebuildFilter();
    }

    private void RebuildFilter()
    {
        playerFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerHitMask,
            useTriggers = true
        };
    }

    [ContextMenu("Fit Collider To Sprite")]
    public void FitColliderToSprite()
    {
        ResolveReferences();

        if (hitboxCollider == null ||
            visualRenderer == null ||
            visualRenderer.sprite == null)
        {
            Debug.LogWarning(
                "AttackDogLungeHitbox: Collider, SpriteRenderer, or sprite is missing.",
                this
            );
            return;
        }

        if (visualRenderer.transform != transform)
        {
            Debug.LogWarning(
                "AttackDogLungeHitbox: fitting expects the renderer and collider on the same GameObject.",
                this
            );
            return;
        }

        Bounds spriteBounds = visualRenderer.sprite.bounds;
        Vector2 size = (Vector2)spriteBounds.size * colliderFitMultiplier;
        Vector2 offset = spriteBounds.center;

        CircleCollider2D circle = hitboxCollider as CircleCollider2D;
        CapsuleCollider2D capsule = hitboxCollider as CapsuleCollider2D;
        BoxCollider2D box = hitboxCollider as BoxCollider2D;

        if (circle != null)
        {
            circle.offset = offset;
            circle.radius = Mathf.Max(size.x, size.y) * 0.5f;
        }
        else if (capsule != null)
        {
            capsule.offset = offset;
            capsule.size = new Vector2(
                Mathf.Max(0.01f, size.x),
                Mathf.Max(0.01f, size.y)
            );
            capsule.direction =
                size.x >= size.y
                    ? CapsuleDirection2D.Horizontal
                    : CapsuleDirection2D.Vertical;
        }
        else if (box != null)
        {
            box.offset = offset;
            box.size = new Vector2(
                Mathf.Max(0.01f, size.x),
                Mathf.Max(0.01f, size.y)
            );
        }
        else
        {
            Debug.LogWarning(
                "AttackDogLungeHitbox: fitting supports Circle, Capsule, and Box Collider2D.",
                this
            );
        }
    }

    public void DrawColliderGizmo()
    {
        if (!drawColliderGizmo || hitboxCollider == null)
            return;

        Color oldColor = Gizmos.color;
        Matrix4x4 oldMatrix = Gizmos.matrix;

        Gizmos.color = damageActive
            ? new Color(1f, 0f, 0f, 0.95f)
            : new Color(1f, 0.45f, 0f, 0.75f);

        Gizmos.matrix = transform.localToWorldMatrix;

        CircleCollider2D circle = hitboxCollider as CircleCollider2D;
        CapsuleCollider2D capsule = hitboxCollider as CapsuleCollider2D;
        BoxCollider2D box = hitboxCollider as BoxCollider2D;

        if (circle != null)
            Gizmos.DrawWireSphere(circle.offset, circle.radius);
        else if (capsule != null)
            Gizmos.DrawWireCube(capsule.offset, capsule.size);
        else if (box != null)
            Gizmos.DrawWireCube(box.offset, box.size);
        else
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireCube(
                hitboxCollider.bounds.center,
                hitboxCollider.bounds.size
            );
        }

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    private void OnDrawGizmosSelected()
    {
        ResolveReferences();
        DrawColliderGizmo();
    }
}
