using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary movement-speed buff for the shared combat pawn.
///
/// Because this component lives on PlayerRoot, the boost survives character swaps.
/// Recasting refreshes the duration and multiplier instead of stacking.
///
/// Visuals:
/// - Creates a visible blue aura from several offset sprite copies.
/// - Spawns blue afterimages while the pawn moves.
/// - Aura and afterimage strength fade as the buff expires.
/// </summary>
public class PlayerSpeedBoost : MonoBehaviour
{
    private sealed class GhostInstance
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public float age;
        public float lifetime;
        public float startingAlpha;
    }

    [Header("Character Visual")]
    [Tooltip("Assign PlayerRoot/CharacterSprite. Leave empty to auto-find it.")]
    [SerializeField] private SpriteRenderer characterRenderer;

    [Header("Blue Aura")]
    [SerializeField] private Color glowColor = new Color(0.15f, 0.65f, 1f, 1f);

    [Range(0f, 1f)]
    [SerializeField] private float glowStartingAlpha = 0.32f;

    [Tooltip("How far the aura copies extend beyond the character in local units.")]
    [SerializeField] private float glowSpread = 0.045f;

    [Tooltip("Makes each aura silhouette slightly larger than the character.")]
    [SerializeField] private float glowScaleMultiplier = 1.12f;

    [Tooltip("Sorting-order offset relative to CharacterSprite. Negative places the aura behind.")]
    [SerializeField] private int glowSortingOrderOffset = -1;

    [Tooltip("Optional custom glow material. Leave empty to use an unlit sprite material.")]
    [SerializeField] private Material glowMaterial;

    [Tooltip("Creates eight offset aura copies. This is more visible than one enlarged sprite hidden behind the character.")]
    [SerializeField] private bool useEightDirectionAura = true;

    [Header("Ghost Trail")]
    [SerializeField] private bool enableGhostTrail = true;

    [Tooltip("Real-time seconds between afterimages while moving.")]
    [SerializeField] private float ghostInterval = 0.08f;

    [Tooltip("How long each afterimage remains visible.")]
    [SerializeField] private float ghostLifetime = 0.25f;

    [Tooltip("Minimum distance the pawn must move before another afterimage can spawn.")]
    [SerializeField] private float minimumGhostMoveDistance = 0.025f;

    [SerializeField] private Color ghostColor = new Color(0.15f, 0.65f, 1f, 1f);

    [Range(0f, 1f)]
    [SerializeField] private float ghostStartingAlpha = 0.28f;

    [Tooltip("Sorting-order offset relative to CharacterSprite.")]
    [SerializeField] private int ghostSortingOrderOffset = -2;

    [Tooltip("Optional material for afterimages. Leave empty to copy the character material.")]
    [SerializeField] private Material ghostMaterial;

    [Header("Buff Timing")]
    [Tooltip("When false, the boost duration uses normal game time.")]
    [SerializeField] private bool useUnscaledDuration = false;

    [Header("Debug")]
    [SerializeField] private bool logActivation = false;

    public bool IsActive { get; private set; }
    public float RemainingTime => Mathf.Max(0f, remainingTime);
    public float CurrentMultiplier => currentMultiplier;

    private readonly List<GhostInstance> ghosts = new();
    private readonly List<SpriteRenderer> auraRenderers = new();

    private static readonly Vector2[] EightDirections =
    {
        Vector2.up,
        Vector2.down,
        Vector2.left,
        Vector2.right,
        new Vector2(1f, 1f).normalized,
        new Vector2(-1f, 1f).normalized,
        new Vector2(1f, -1f).normalized,
        new Vector2(-1f, -1f).normalized
    };

    private SpeedModifier speedModifier;
    private int boostSourceId;

    private float currentMultiplier = 1f;
    private float totalDuration;
    private float remainingTime;
    private float ghostTimer;

    private Vector3 lastGhostPosition;
    private GameObject auraRoot;
    private Material runtimeGlowMaterial;

    private void Awake()
    {
        boostSourceId = SpeedModifier.GenerateSourceId();

        FindCharacterRendererIfNeeded();
        CreateAuraIfNeeded();
        HideAura();

        lastGhostPosition = transform.position;
    }

    private void Update()
    {
        UpdateGhosts(Time.unscaledDeltaTime);

        if (!IsActive)
            return;

        float durationDelta = useUnscaledDuration
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        remainingTime -= durationDelta;

        RefreshAuraVisual();
        TickGhostTrail();

        if (remainingTime <= 0f)
            EndBoost();
    }

    /// <summary>
    /// Activates or refreshes the boost. Recasting does not stack multipliers.
    /// </summary>
    public void Activate(float multiplier, float duration)
    {
        currentMultiplier = Mathf.Max(1f, multiplier);
        totalDuration = Mathf.Max(0.05f, duration);
        remainingTime = totalDuration;

        speedModifier = GetComponent<SpeedModifier>();
        if (speedModifier == null)
            speedModifier = gameObject.AddComponent<SpeedModifier>();

        speedModifier.ApplyBoost(boostSourceId, currentMultiplier);

        FindCharacterRendererIfNeeded();
        CreateAuraIfNeeded();

        IsActive = true;
        ghostTimer = 0f;
        lastGhostPosition = transform.position;

        RefreshAuraVisual();

        if (logActivation)
        {
            Debug.Log(
                $"Speed Boost activated: {currentMultiplier:0.##}x for {totalDuration:0.##} seconds. Aura copies={auraRenderers.Count}."
            );
        }
    }

    public void EndBoost()
    {
        if (!IsActive)
            return;

        IsActive = false;
        remainingTime = 0f;
        currentMultiplier = 1f;

        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        if (speedModifier != null)
            speedModifier.RemoveBoost(boostSourceId);

        speedModifier = null;
        HideAura();
    }

    private void TickGhostTrail()
    {
        if (!enableGhostTrail || characterRenderer == null)
            return;

        ghostTimer -= Time.unscaledDeltaTime;

        Vector3 currentPosition = transform.position;
        float movedDistance = Vector3.Distance(currentPosition, lastGhostPosition);

        if (ghostTimer > 0f || movedDistance < minimumGhostMoveDistance)
            return;

        SpawnGhost();
        ghostTimer = Mathf.Max(0.01f, ghostInterval);
        lastGhostPosition = currentPosition;
    }

    private void SpawnGhost()
    {
        if (characterRenderer == null || characterRenderer.sprite == null)
            return;

        GameObject ghostObject = new GameObject("SpeedBoostGhost");
        ghostObject.transform.position = characterRenderer.transform.position;
        ghostObject.transform.rotation = characterRenderer.transform.rotation;
        ghostObject.transform.localScale = characterRenderer.transform.lossyScale;

        SpriteRenderer renderer = ghostObject.AddComponent<SpriteRenderer>();
        CopyRendererAppearance(characterRenderer, renderer);

        renderer.sharedMaterial = ghostMaterial != null
            ? ghostMaterial
            : characterRenderer.sharedMaterial;

        renderer.sortingLayerID = characterRenderer.sortingLayerID;
        renderer.sortingOrder = characterRenderer.sortingOrder + ghostSortingOrderOffset;

        float lifeStrength = GetRemainingNormalized();

        Color color = ghostColor;
        color.a = ghostStartingAlpha * lifeStrength;
        renderer.color = color;

        ghosts.Add(new GhostInstance
        {
            gameObject = ghostObject,
            renderer = renderer,
            age = 0f,
            lifetime = Mathf.Max(0.02f, ghostLifetime),
            startingAlpha = color.a
        });
    }

    private void UpdateGhosts(float unscaledDeltaTime)
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            GhostInstance ghost = ghosts[i];

            if (ghost == null || ghost.gameObject == null || ghost.renderer == null)
            {
                ghosts.RemoveAt(i);
                continue;
            }

            ghost.age += unscaledDeltaTime;
            float progress = Mathf.Clamp01(ghost.age / ghost.lifetime);

            Color color = ghost.renderer.color;
            color.a = Mathf.Lerp(ghost.startingAlpha, 0f, progress);
            ghost.renderer.color = color;

            if (progress >= 1f)
            {
                Destroy(ghost.gameObject);
                ghosts.RemoveAt(i);
            }
        }
    }

    private void RefreshAuraVisual()
    {
        if (characterRenderer == null)
            FindCharacterRendererIfNeeded();

        if (characterRenderer == null || characterRenderer.sprite == null)
        {
            HideAura();
            return;
        }

        CreateAuraIfNeeded();

        float strength = GetRemainingNormalized();
        float alpha = glowStartingAlpha * strength;

        Vector2[] directions = useEightDirectionAura
            ? EightDirections
            : new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        for (int i = 0; i < auraRenderers.Count; i++)
        {
            SpriteRenderer aura = auraRenderers[i];
            if (aura == null) continue;

            CopyRendererAppearance(characterRenderer, aura);

            aura.sharedMaterial = glowMaterial != null
                ? glowMaterial
                : GetRuntimeGlowMaterial();

            aura.sortingLayerID = characterRenderer.sortingLayerID;
            aura.sortingOrder = characterRenderer.sortingOrder + glowSortingOrderOffset;

            MatchAuraTransform(aura, directions[i % directions.Length]);

            Color color = glowColor;
            color.a = alpha;
            aura.color = color;
            aura.enabled = IsActive && alpha > 0.001f;
        }
    }

    private void CopyRendererAppearance(SpriteRenderer source, SpriteRenderer target)
    {
        if (source == null || target == null)
            return;

        target.sprite = source.sprite;
        target.flipX = source.flipX;
        target.flipY = source.flipY;
        target.drawMode = source.drawMode;
        target.size = source.size;
        target.maskInteraction = source.maskInteraction;
    }

    private void MatchAuraTransform(SpriteRenderer aura, Vector2 direction)
    {
        if (characterRenderer == null || aura == null)
            return;

        Transform source = characterRenderer.transform;
        Transform target = aura.transform;

        if (target.parent == source.parent)
        {
            target.localPosition = source.localPosition + (Vector3)(direction * glowSpread);
            target.localRotation = source.localRotation;
            target.localScale = source.localScale * glowScaleMultiplier;
        }
        else
        {
            target.position = source.position + (Vector3)(direction * glowSpread);
            target.rotation = source.rotation;
            target.localScale = source.lossyScale * glowScaleMultiplier;
        }
    }

    private float GetRemainingNormalized()
    {
        if (totalDuration <= 0f)
            return 0f;

        return Mathf.Clamp01(remainingTime / totalDuration);
    }

    private void FindCharacterRendererIfNeeded()
    {
        if (characterRenderer != null)
            return;

        Transform namedCharacter = transform.Find("CharacterSprite");
        if (namedCharacter != null)
            characterRenderer = namedCharacter.GetComponent<SpriteRenderer>();

        if (characterRenderer != null)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer candidate = renderers[i];
            if (candidate == null) continue;

            string objectName = candidate.gameObject.name.ToLowerInvariant();

            if (objectName.Contains("hurtbox")) continue;
            if (objectName.Contains("glow")) continue;
            if (objectName.Contains("aura")) continue;
            if (objectName.Contains("ghost")) continue;

            characterRenderer = candidate;
            break;
        }
    }

    private void CreateAuraIfNeeded()
    {
        int wantedCount = useEightDirectionAura ? 8 : 4;

        if (auraRoot == null)
        {
            auraRoot = new GameObject("SpeedBoostAura_Runtime");

            Transform parent = characterRenderer != null && characterRenderer.transform.parent != null
                ? characterRenderer.transform.parent
                : transform;

            auraRoot.transform.SetParent(parent, false);
        }

        while (auraRenderers.Count < wantedCount)
        {
            GameObject copy = new GameObject($"GlowCopy_{auraRenderers.Count + 1}");
            copy.transform.SetParent(auraRoot.transform.parent, false);

            SpriteRenderer renderer = copy.AddComponent<SpriteRenderer>();
            renderer.enabled = false;

            auraRenderers.Add(renderer);
        }

        while (auraRenderers.Count > wantedCount)
        {
            int last = auraRenderers.Count - 1;
            SpriteRenderer renderer = auraRenderers[last];

            if (renderer != null)
                Destroy(renderer.gameObject);

            auraRenderers.RemoveAt(last);
        }
    }

    private Material GetRuntimeGlowMaterial()
    {
        if (runtimeGlowMaterial != null)
            return runtimeGlowMaterial;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return characterRenderer != null ? characterRenderer.sharedMaterial : null;

        runtimeGlowMaterial = new Material(shader)
        {
            name = "SpeedBoostGlow_RuntimeMaterial"
        };

        return runtimeGlowMaterial;
    }

    private void HideAura()
    {
        for (int i = 0; i < auraRenderers.Count; i++)
        {
            SpriteRenderer aura = auraRenderers[i];
            if (aura == null) continue;

            Color color = aura.color;
            color.a = 0f;
            aura.color = color;
            aura.enabled = false;
        }
    }

    private void ClearGhosts()
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            GhostInstance ghost = ghosts[i];
            if (ghost != null && ghost.gameObject != null)
                Destroy(ghost.gameObject);
        }

        ghosts.Clear();
    }

    private void ClearAura()
    {
        for (int i = auraRenderers.Count - 1; i >= 0; i--)
        {
            SpriteRenderer aura = auraRenderers[i];
            if (aura != null)
                Destroy(aura.gameObject);
        }

        auraRenderers.Clear();

        if (auraRoot != null)
        {
            Destroy(auraRoot);
            auraRoot = null;
        }
    }

    private void OnDisable()
    {
        if (IsActive)
            EndBoost();

        HideAura();
        ClearGhosts();
    }

    private void OnDestroy()
    {
        if (speedModifier == null)
            speedModifier = GetComponent<SpeedModifier>();

        if (speedModifier != null)
            speedModifier.RemoveBoost(boostSourceId);

        ClearGhosts();
        ClearAura();

        if (runtimeGlowMaterial != null)
            Destroy(runtimeGlowMaterial);
    }
}
