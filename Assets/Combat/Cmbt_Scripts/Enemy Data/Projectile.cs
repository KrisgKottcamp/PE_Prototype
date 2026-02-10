using UnityEngine;

public class Projectile : MonoBehaviour
{
    public enum ProjectileTeam
    {
        Enemy,
        Player,
        Neutral
    }

    [Header("Movement")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private float speed = 7f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private float armDelay = 0.03f;

    [Header("Damage")]
    [SerializeField] private int damage = 8;
    [SerializeField] private ProjectileTeam team = ProjectileTeam.Enemy;
    [SerializeField] private bool damageAnyNonOwner = false;
    [SerializeField] private string[] validTargetTags = new[] { "Player", "PlayerCombatPawn" };

    [Header("Collision")]
    [SerializeField] private bool destroyOnSolidHit = true;
    [SerializeField] private LayerMask solidMask;

    private Vector2 direction = Vector2.right;
    private bool initialized = false;
    private float armedAt = 0f;
    private Transform ownerRoot;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        armedAt = Time.time + Mathf.Max(0f, armDelay);
        Destroy(gameObject, Mathf.Max(0.05f, lifetime));
    }

    // Backward-compatible initialize
    public void Initialize(Vector2 dir, float newSpeed)
    {
        Initialize(dir, newSpeed, null, team);
    }

    // Preferred initialize from shooter
    public void Initialize(Vector2 dir, float newSpeed, GameObject ownerObj, ProjectileTeam projectileTeam)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        speed = Mathf.Max(0.01f, newSpeed);
        team = projectileTeam;
        ownerRoot = ownerObj != null ? ownerObj.transform.root : null;
        initialized = true;
        ApplyVelocity();
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = direction * speed;
#else
            rb.velocity = direction * speed;
#endif
        }
        else
        {
            transform.position += (Vector3)(direction * speed * Time.fixedDeltaTime);
        }
    }

    private void ApplyVelocity()
    {
        if (rb == null) return;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = direction * speed;
#else
        rb.velocity = direction * speed;
#endif
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleHit(other);
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        HandleHit(other.collider);
    }

    private void HandleHit(Collider2D other)
    {
        if (other == null) return;
        if (Time.time < armedAt) return;

        Transform hitRoot = other.transform.root;

        // Ignore owner
        if (ownerRoot != null && hitRoot == ownerRoot) return;

        bool isValidTarget = damageAnyNonOwner || IsValidTargetTag(other);

        if (isValidTarget)
        {
            ApplyDamage(other);
            Destroy(gameObject);
            return;
        }

        if (destroyOnSolidHit && ((solidMask.value & (1 << other.gameObject.layer)) != 0))
        {
            Destroy(gameObject);
        }
    }

    private bool IsValidTargetTag(Collider2D other)
    {
        if (validTargetTags == null || validTargetTags.Length == 0) return false;

        string tagSelf = other.tag;
        string tagRoot = other.transform.root.tag;

        for (int i = 0; i < validTargetTags.Length; i++)
        {
            string t = validTargetTags[i];
            if (string.IsNullOrEmpty(t)) continue;

            if (tagSelf == t || tagRoot == t)
                return true;
        }

        return false;
    }

    private void ApplyDamage(Collider2D other)
    {
        GameObject selfObj = other.gameObject;
        GameObject rootObj = other.transform.root.gameObject;

        // Try common damage entry points
        selfObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

        selfObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);

        selfObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);
        if (rootObj != selfObj)
            rootObj.SendMessage("ReceiveDamage", damage, SendMessageOptions.DontRequireReceiver);
    }
}
