using System;
using System.Collections;
using UnityEngine;

public class OverworldEncounterTransition : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0f)]
    private float transitionLeadInSeconds = 0.38f;

    [Header("Enemy Reaction")]
    [SerializeField] private Transform enemyVisual;
    [SerializeField, Min(0f)] private float bumpDistance = 0.22f;
    [SerializeField, Min(0f)] private float pulseScale = 0.12f;

    [Header("Optional Animator Triggers")]
    [SerializeField] private Animator enemyAnimator;
    [SerializeField] private string enemyTriggerName = "Encounter";
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private string playerTriggerName = "Encounter";

    [Header("Optional Screen Flash")]
    [SerializeField] private CanvasGroup flashCanvasGroup;
    [SerializeField, Range(0f, 1f)]
    private float flashPeakAlpha = 0.9f;

    private bool isPlaying;
    private TopDownMover lockedPlayerMover;
    private bool lockedPlayerMoverWasEnabled;
    private OverworldEnemyWander lockedEnemyWander;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (enemyVisual == null)
        {
            SpriteRenderer sprite =
                GetComponentInChildren<SpriteRenderer>(true);

            if (sprite != null)
                enemyVisual = sprite.transform;
        }

        if (enemyAnimator == null)
            enemyAnimator = GetComponentInChildren<Animator>(true);

        if (flashCanvasGroup != null)
            flashCanvasGroup.alpha = 0f;
    }

    public void Play(
        Transform playerRoot,
        Action onTransitionFinished)
    {
        if (isPlaying)
            return;

        StartCoroutine(
            PlayRoutine(playerRoot, onTransitionFinished)
        );
    }

    private IEnumerator PlayRoutine(
        Transform playerRoot,
        Action onTransitionFinished)
    {
        isPlaying = true;

        LockActors(playerRoot);
        TriggerAnimators();

        Vector3 originalLocalPosition =
            enemyVisual != null
                ? enemyVisual.localPosition
                : Vector3.zero;

        Vector3 originalLocalScale =
            enemyVisual != null
                ? enemyVisual.localScale
                : Vector3.one;

        Vector3 localBumpDirection = ResolveLocalBumpDirection(
            playerRoot,
            enemyVisual
        );

        float duration = Mathf.Max(
            0.01f,
            transitionLeadInSeconds
        );

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pulse = Mathf.Sin(normalized * Mathf.PI);

            if (enemyVisual != null)
            {
                enemyVisual.localPosition =
                    originalLocalPosition +
                    localBumpDirection *
                    (bumpDistance * pulse);

                enemyVisual.localScale =
                    originalLocalScale *
                    (1f + pulseScale * pulse);
            }

            if (flashCanvasGroup != null)
            {
                flashCanvasGroup.alpha =
                    flashPeakAlpha * (1f - normalized);
            }

            yield return null;
        }

        if (enemyVisual != null)
        {
            enemyVisual.localPosition = originalLocalPosition;
            enemyVisual.localScale = originalLocalScale;
        }

        if (flashCanvasGroup != null)
            flashCanvasGroup.alpha = 0f;

        isPlaying = false;
        onTransitionFinished?.Invoke();
    }

    public void RestoreActorsAfterCancelledTransition()
    {
        if (lockedPlayerMover != null)
            lockedPlayerMover.enabled = lockedPlayerMoverWasEnabled;

        lockedEnemyWander?.SetMovementLocked(false);

        if (flashCanvasGroup != null)
            flashCanvasGroup.alpha = 0f;

        isPlaying = false;
    }

    private void LockActors(Transform playerRoot)
    {
        lockedEnemyWander =
            GetComponent<OverworldEnemyWander>();

        lockedEnemyWander?.SetMovementLocked(true);

        Rigidbody2D enemyBody = GetComponent<Rigidbody2D>();
        StopBody(enemyBody);

        if (playerRoot == null)
            return;

        lockedPlayerMover =
            playerRoot.GetComponent<TopDownMover>();

        if (lockedPlayerMover == null)
        {
            lockedPlayerMover =
                playerRoot.GetComponentInChildren<TopDownMover>(true);
        }

        if (lockedPlayerMover != null)
        {
            lockedPlayerMoverWasEnabled = lockedPlayerMover.enabled;
            lockedPlayerMover.enabled = false;
        }

        Rigidbody2D playerBody =
            playerRoot.GetComponent<Rigidbody2D>();

        if (playerBody == null)
        {
            playerBody =
                playerRoot.GetComponentInChildren<Rigidbody2D>(true);
        }

        StopBody(playerBody);
    }

    private static void StopBody(Rigidbody2D body)
    {
        if (body == null)
            return;

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    private void TriggerAnimators()
    {
        if (enemyAnimator != null &&
            !string.IsNullOrWhiteSpace(enemyTriggerName))
        {
            enemyAnimator.SetTrigger(enemyTriggerName);
        }

        if (playerAnimator != null &&
            !string.IsNullOrWhiteSpace(playerTriggerName))
        {
            playerAnimator.SetTrigger(playerTriggerName);
        }
    }

    private static Vector3 ResolveLocalBumpDirection(
        Transform playerRoot,
        Transform visual)
    {
        if (playerRoot == null || visual == null)
            return Vector3.zero;

        Vector3 worldDirection =
            playerRoot.position - visual.position;

        worldDirection.z = 0f;

        if (worldDirection.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        worldDirection.Normalize();

        Transform parent = visual.parent;

        if (parent == null)
            return worldDirection;

        Vector3 localDirection =
            parent.InverseTransformDirection(worldDirection);

        localDirection.z = 0f;
        return localDirection.normalized;
    }
}
