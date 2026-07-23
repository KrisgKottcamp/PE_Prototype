using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OverworldEncounterGracePeriod : MonoBehaviour
{
    [Header("Player Flash")]
    [Tooltip(
        "Optional explicit list. When empty, every SpriteRenderer under " +
        "the persistent Player object is found automatically."
    )]
    [SerializeField] private SpriteRenderer[] playerRenderers;

    [SerializeField, Min(0.02f)]
    private float flashIntervalSeconds = 0.10f;

    [SerializeField, Range(0f, 1f)]
    private float dimAlphaMultiplier = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool logGracePeriod;

    private readonly List<CollisionPair> ignoredPairs = new();

    private Coroutine graceRoutine;
    private Color[] originalColors;

    public bool IsActive { get; private set; }

    private void Awake()
    {
        RefreshRenderers();
    }

    private void OnDisable()
    {
        StopAndRestore();
    }

    private void OnDestroy()
    {
        StopAndRestore();
    }

    public void BeginGracePeriod(float durationSeconds)
    {
        float duration = Mathf.Max(0f, durationSeconds);

        StopAndRestore();

        if (duration <= 0f)
            return;

        RefreshRenderers();
        CacheOriginalColors();
        IgnoreOverworldEncounterCollisions(true);

        IsActive = true;
        graceRoutine = StartCoroutine(GraceRoutine(duration));

        if (logGracePeriod)
        {
            Debug.Log(
                $"OverworldEncounterGracePeriod: Active for " +
                $"{duration:0.00} seconds.",
                this
            );
        }
    }

    private IEnumerator GraceRoutine(float durationSeconds)
    {
        float elapsed = 0f;
        float flashTimer = 0f;
        bool dimmed = false;

        ApplyFlash(false);

        while (elapsed < durationSeconds)
        {
            float delta = Time.unscaledDeltaTime;
            elapsed += delta;
            flashTimer += delta;

            if (flashTimer >= flashIntervalSeconds)
            {
                flashTimer = 0f;
                dimmed = !dimmed;
                ApplyFlash(dimmed);
            }

            yield return null;
        }

        graceRoutine = null;
        RestoreState();
    }

    private void StopAndRestore()
    {
        if (graceRoutine != null)
        {
            StopCoroutine(graceRoutine);
            graceRoutine = null;
        }

        RestoreState();
    }

    private void RestoreState()
    {
        IgnoreOverworldEncounterCollisions(false);
        RestoreOriginalColors();
        IsActive = false;
    }

    private void RefreshRenderers()
    {
        if (playerRenderers != null && playerRenderers.Length > 0)
            return;

        playerRenderers =
            GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void CacheOriginalColors()
    {
        if (playerRenderers == null)
        {
            originalColors = System.Array.Empty<Color>();
            return;
        }

        originalColors = new Color[playerRenderers.Length];

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            originalColors[i] = playerRenderers[i] != null
                ? playerRenderers[i].color
                : Color.white;
        }
    }

    private void ApplyFlash(bool dimmed)
    {
        if (playerRenderers == null || originalColors == null)
            return;

        int count = Mathf.Min(
            playerRenderers.Length,
            originalColors.Length
        );

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = playerRenderers[i];

            if (renderer == null)
                continue;

            Color color = originalColors[i];

            if (dimmed)
                color.a *= dimAlphaMultiplier;

            renderer.color = color;
        }
    }

    private void RestoreOriginalColors()
    {
        ApplyFlash(false);
    }

    private void IgnoreOverworldEncounterCollisions(bool ignore)
    {
        if (!ignore)
        {
            for (int i = 0; i < ignoredPairs.Count; i++)
            {
                CollisionPair pair = ignoredPairs[i];

                if (pair.playerCollider != null &&
                    pair.encounterCollider != null)
                {
                    Physics2D.IgnoreCollision(
                        pair.playerCollider,
                        pair.encounterCollider,
                        false
                    );
                }
            }

            ignoredPairs.Clear();
            return;
        }

        ignoredPairs.Clear();

        Collider2D[] playerColliders =
            GetComponentsInChildren<Collider2D>(true);

        OverworldEncounter[] encounters =
            FindObjectsOfType<OverworldEncounter>(true);

        for (int encounterIndex = 0;
             encounterIndex < encounters.Length;
             encounterIndex++)
        {
            OverworldEncounter encounter = encounters[encounterIndex];

            if (encounter == null)
                continue;

            Collider2D[] encounterColliders =
                encounter.GetComponentsInChildren<Collider2D>(true);

            for (int playerIndex = 0;
                 playerIndex < playerColliders.Length;
                 playerIndex++)
            {
                Collider2D playerCollider =
                    playerColliders[playerIndex];

                if (playerCollider == null)
                    continue;

                for (int colliderIndex = 0;
                     colliderIndex < encounterColliders.Length;
                     colliderIndex++)
                {
                    Collider2D encounterCollider =
                        encounterColliders[colliderIndex];

                    if (encounterCollider == null ||
                        encounterCollider == playerCollider)
                    {
                        continue;
                    }

                    Physics2D.IgnoreCollision(
                        playerCollider,
                        encounterCollider,
                        true
                    );

                    ignoredPairs.Add(
                        new CollisionPair(
                            playerCollider,
                            encounterCollider
                        )
                    );
                }
            }
        }
    }

    private readonly struct CollisionPair
    {
        public readonly Collider2D playerCollider;
        public readonly Collider2D encounterCollider;

        public CollisionPair(
            Collider2D playerCollider,
            Collider2D encounterCollider)
        {
            this.playerCollider = playerCollider;
            this.encounterCollider = encounterCollider;
        }
    }
}
