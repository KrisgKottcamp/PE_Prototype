using UnityEngine;
using static CharacterDefinition;

public class CombatBasicAttackRouter : MonoBehaviour
{
    [SerializeField] private BasicAttack meleeAttack;
    [SerializeField] private ProjectileBasicAttack projectileAttack;
    [SerializeField] private HeavyComboAttack heavyComboAttack;
    [SerializeField] private WhipAttack whipAttack;

    [Header("Basic Attack Release Shake")]
    [Tooltip("Very small impulse played when a valid basic attack begins. Stronger hit-confirm shakes remain separate.")]
    [SerializeField] private CameraShakeSettings releaseShake =
        CameraShakeSettings.Create(0.035f, 0.045f);

    [Tooltip("Per-attack-style multipliers applied to Release Shake.")]
    [SerializeField] private float meleeShakeMultiplier = 1f;
    [SerializeField] private float projectileShakeMultiplier = 0.80f;
    [SerializeField] private float heavyComboShakeMultiplier = 1.10f;
    [SerializeField] private float whipShakeMultiplier = 0.90f;

    private int lastIndex = -1;

    private void Awake()
    {
        if (meleeAttack == null) meleeAttack = GetComponent<BasicAttack>();
        if (projectileAttack == null) projectileAttack = GetComponent<ProjectileBasicAttack>();
        if (heavyComboAttack == null) heavyComboAttack = GetComponent<HeavyComboAttack>();
        if (whipAttack == null) whipAttack = GetComponent<WhipAttack>();
    }

    private void OnEnable()
    {
        ForceRefresh();
    }

    private void Update()
    {
        var pm = PartyManager.Instance;
        if (pm == null) return;

        if (pm.activeIndex != lastIndex)
            ForceRefresh();
    }

    public void ForceRefresh()
    {
        var pm = PartyManager.Instance;
        if (pm == null || pm.Active == null || pm.Active.def == null) return;

        lastIndex = pm.activeIndex;

        BasicAttackType type = pm.Active.def.basicAttackType;

        if (meleeAttack != null) meleeAttack.enabled = type == BasicAttackType.Melee;
        if (projectileAttack != null) projectileAttack.enabled = type == BasicAttackType.Projectile;
        if (heavyComboAttack != null) heavyComboAttack.enabled = type == BasicAttackType.HeavyCombo;
        if (whipAttack != null) whipAttack.enabled = type == BasicAttackType.Whip;
    }

    /// <summary>
    /// Plays the shared, intentionally subtle basic-attack release impulse.
    /// Call only after the attack passes its cooldown and lockout checks.
    /// </summary>
    public void RequestReleaseShake(Vector2 aimDirection)
    {
        PartyManager pm = PartyManager.Instance;

        if (pm == null || pm.Active == null || pm.Active.def == null)
            return;

        float multiplier = GetReleaseShakeMultiplier(
            pm.Active.def.basicAttackType
        );

        Vector2 recoilDirection =
            aimDirection.sqrMagnitude > 0.0001f
                ? -aimDirection.normalized
                : Vector2.down;

        CombatCameraShake.Request(
            releaseShake,
            transform.position,
            recoilDirection,
            multiplier
        );
    }

    private float GetReleaseShakeMultiplier(BasicAttackType type)
    {
        switch (type)
        {
            case BasicAttackType.Projectile:
                return Mathf.Max(0f, projectileShakeMultiplier);

            case BasicAttackType.HeavyCombo:
                return Mathf.Max(0f, heavyComboShakeMultiplier);

            case BasicAttackType.Whip:
                return Mathf.Max(0f, whipShakeMultiplier);

            default:
                return Mathf.Max(0f, meleeShakeMultiplier);
        }
    }

    private void OnValidate()
    {
        meleeShakeMultiplier = Mathf.Max(0f, meleeShakeMultiplier);
        projectileShakeMultiplier = Mathf.Max(0f, projectileShakeMultiplier);
        heavyComboShakeMultiplier = Mathf.Max(0f, heavyComboShakeMultiplier);
        whipShakeMultiplier = Mathf.Max(0f, whipShakeMultiplier);
    }
}
