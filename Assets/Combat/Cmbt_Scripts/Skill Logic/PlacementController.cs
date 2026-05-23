using System;
using UnityEngine;

/// <summary>
/// Generic placement targeting system. Shows a preview circle at the mouse
/// cursor, validates placement (range + obstacle check), and returns a
/// world position on confirm.
///
/// Decoupled from any specific skill type. CombatSkillMenuController starts
/// placement for ANY skill with usesPlacement = true. CombatSkillSystem
/// decides what to do with the confirmed position based on execution type.
///
/// Put this on the CombatHUD or a persistent scene object.
/// </summary>
public class PlacementController : MonoBehaviour
{
    [Header("Preview")]
    [Tooltip("Prefab with a SpriteRenderer (circle sprite). Scaled to match preview radius.")]
    [SerializeField] private GameObject previewPrefab;
    [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 0.35f);
    [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 0.35f);

    [Header("Range Indicator (optional)")]
    [Tooltip("If assigned, a ring is shown around the player indicating max placement range.")]
    [SerializeField] private GameObject rangeIndicatorPrefab;
    [SerializeField] private Color rangeColor = new Color(1f, 1f, 1f, 0.15f);

    private Camera cam;
    private GameObject previewInstance;
    private SpriteRenderer previewRenderer;
    private GameObject rangeInstance;
    private SpriteRenderer rangeRenderer;

    private float previewRadius;
    private float maxRange;
    private LayerMask blockMask;
    private Transform playerTransform;
    private Action<Vector2> onConfirm;

    private bool active;
    private readonly Collider2D[] overlapBuffer = new Collider2D[8];

    public bool IsActive => active;

    // --------------------------------------------------
    // Public API
    // --------------------------------------------------

    /// <summary>
    /// Starts placement mode. Shows preview at cursor, validates position.
    /// On left-click (valid position): calls confirm with world position.
    /// Cancel is handled externally by CombatSkillMenuController.
    /// </summary>
    public void BeginPlacement(float previewRadius, float placementRange,
        LayerMask obstacleMask, Transform player, Action<Vector2> confirm)
    {
        this.previewRadius = previewRadius;
        maxRange = placementRange;
        blockMask = obstacleMask;
        playerTransform = player;
        onConfirm = confirm;

        SpawnPreview();
        active = true;
    }

    /// <summary>
    /// Cleans up preview without invoking any callback.
    /// Called by CombatSkillMenuController on cancel.
    /// </summary>
    public void EndPlacement()
    {
        active = false;
        DestroyPreview();
        onConfirm = null;
    }

    // --------------------------------------------------
    // Update
    // --------------------------------------------------

    private void Update()
    {
        if (!active) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 worldPos = GetClampedWorldPos();
        bool valid = IsValidPlacement(worldPos);

        UpdatePreviewTransform(worldPos, valid);
        UpdateRangeIndicator();

        // Confirm on left click when valid
        if (Input.GetMouseButtonDown(0) && valid)
        {
            active = false;
            DestroyPreview();
            onConfirm?.Invoke(worldPos);
            onConfirm = null;
        }
    }

    // --------------------------------------------------
    // Mouse ¨ world position, clamped to range
    // --------------------------------------------------

    private Vector2 GetClampedWorldPos()
    {
        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;
        Vector2 worldPos = cam.ScreenToWorldPoint(mouse);

        if (playerTransform == null) return worldPos;

        Vector2 playerPos = (Vector2)playerTransform.position;
        Vector2 offset = worldPos - playerPos;

        if (offset.magnitude > maxRange)
            worldPos = playerPos + offset.normalized * maxRange;

        return worldPos;
    }

    // --------------------------------------------------
    // Validity
    // --------------------------------------------------

    private bool IsValidPlacement(Vector2 pos)
    {
        if (blockMask.value == 0) return true;

        int count = Physics2D.OverlapCircleNonAlloc(pos, previewRadius, overlapBuffer, blockMask);
        return count == 0;
    }

    // --------------------------------------------------
    // Preview visuals
    // --------------------------------------------------

    private void SpawnPreview()
    {
        DestroyPreview();

        if (previewPrefab != null)
        {
            previewInstance = Instantiate(previewPrefab);
            previewRenderer = previewInstance.GetComponentInChildren<SpriteRenderer>();
            float diameter = previewRadius * 2f;
            previewInstance.transform.localScale = new Vector3(diameter, diameter, 1f);
        }

        if (rangeIndicatorPrefab != null && playerTransform != null)
        {
            rangeInstance = Instantiate(rangeIndicatorPrefab);
            rangeRenderer = rangeInstance.GetComponentInChildren<SpriteRenderer>();
            float rangeDiam = maxRange * 2f;
            rangeInstance.transform.localScale = new Vector3(rangeDiam, rangeDiam, 1f);
            if (rangeRenderer != null) rangeRenderer.color = rangeColor;
        }
    }

    private void UpdatePreviewTransform(Vector2 worldPos, bool valid)
    {
        if (previewInstance != null)
            previewInstance.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

        if (previewRenderer != null)
            previewRenderer.color = valid ? validColor : invalidColor;
    }

    private void UpdateRangeIndicator()
    {
        if (rangeInstance != null && playerTransform != null)
            rangeInstance.transform.position = playerTransform.position;
    }

    private void DestroyPreview()
    {
        if (previewInstance != null) { Destroy(previewInstance); previewInstance = null; }
        if (rangeInstance != null) { Destroy(rangeInstance); rangeInstance = null; }
        previewRenderer = null;
        rangeRenderer = null;
    }
}