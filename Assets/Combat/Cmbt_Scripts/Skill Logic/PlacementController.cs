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

    [Header("Directional Aim Preview")]
    [SerializeField] private Color aimColor = new Color(1f, 1f, 1f, 0.75f);
    [SerializeField, Min(0.01f)] private float aimLineWidth = 0.04f;
    [SerializeField] private int aimSortingOrder = 100;

    private Camera cam;
    private GameObject previewInstance;
    private SpriteRenderer previewRenderer;
    private GameObject rangeInstance;
    private SpriteRenderer rangeRenderer;
    private GameObject aimLineInstance;
    private LineRenderer aimLine;
    private Material aimLineMaterial;

    private float previewRadius;
    private float maxRange;
    private float aimConeAngle;
    private LayerMask blockMask;
    private Transform playerTransform;
    private Action<Vector2> onConfirm;

    private bool active;
    private bool directionalAimActive;
    private Vector2 currentAimDirection = Vector2.up;
    private readonly Collider2D[] overlapBuffer = new Collider2D[8];

    public bool IsActive => active || directionalAimActive;
    public bool IsDirectionalAimActive => directionalAimActive;

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
        EndDirectionalAim();

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

    /// <summary>
    /// Starts a directional aim preview. Confirmation is handled externally so
    /// the same menu input cannot also confirm on the frame that opened aiming.
    /// </summary>
    public void BeginDirectionalAim(float aimRange, float endpointRadius,
        float coneAngle, Transform player)
    {
        EndPlacement();

        maxRange = Mathf.Max(0.1f, aimRange);
        previewRadius = Mathf.Max(0.01f, endpointRadius);
        aimConeAngle = Mathf.Clamp(coneAngle, 0f, 360f);
        playerTransform = player;
        currentAimDirection = Vector2.up;

        SpawnDirectionalAimPreview();
        directionalAimActive = true;
        UpdateDirectionalAimPreview();
    }

    /// <summary>
    /// Returns the current direction and closes the directional preview.
    /// </summary>
    public bool ConfirmDirectionalAim(out Vector2 aimDirection)
    {
        aimDirection = currentAimDirection;

        if (!directionalAimActive)
            return false;

        UpdateDirectionalAimDirection();
        aimDirection = currentAimDirection;
        EndDirectionalAim();
        return true;
    }

    public void EndDirectionalAim()
    {
        directionalAimActive = false;
        DestroyPreview();
    }

    // --------------------------------------------------
    // Update
    // --------------------------------------------------

    private void Update()
    {
        if (!active && !directionalAimActive) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (directionalAimActive)
        {
            UpdateDirectionalAimPreview();
            return;
        }

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
    // Mouse → world position, clamped to range
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

    private void SpawnDirectionalAimPreview()
    {
        DestroyPreview();

        if (previewPrefab != null)
        {
            previewInstance = Instantiate(previewPrefab);
            previewRenderer = previewInstance.GetComponentInChildren<SpriteRenderer>();
            float diameter = previewRadius * 2f;
            previewInstance.transform.localScale = new Vector3(diameter, diameter, 1f);

            if (previewRenderer != null)
                previewRenderer.color = aimColor;
        }

        aimLineInstance = new GameObject("Skill Aim Preview Line");
        aimLine = aimLineInstance.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = aimConeAngle > 0.01f ? 4 : 2;
        aimLine.startWidth = aimLineWidth;
        aimLine.endWidth = aimLineWidth;
        aimLine.startColor = aimColor;
        aimLine.endColor = aimColor;
        aimLine.numCapVertices = 4;
        aimLine.sortingOrder = aimSortingOrder;

        if (previewRenderer != null)
            aimLine.sortingLayerID = previewRenderer.sortingLayerID;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            aimLineMaterial = new Material(spriteShader);
            aimLine.material = aimLineMaterial;
        }
    }

    private void UpdateDirectionalAimPreview()
    {
        if (playerTransform == null)
            return;

        UpdateDirectionalAimDirection();

        Vector2 playerPos = playerTransform.position;
        Vector2 endPos = playerPos + currentAimDirection * maxRange;

        if (aimLine != null)
        {
            Vector3 playerPoint = new Vector3(playerPos.x, playerPos.y, 0f);

            if (aimConeAngle > 0.01f)
            {
                float halfAngle = aimConeAngle * 0.5f;
                Vector2 leftDirection =
                    RotateDirection(currentAimDirection, halfAngle);
                Vector2 rightDirection =
                    RotateDirection(currentAimDirection, -halfAngle);
                Vector2 leftEnd = playerPos + leftDirection * maxRange;
                Vector2 rightEnd = playerPos + rightDirection * maxRange;

                aimLine.SetPosition(0, playerPoint);
                aimLine.SetPosition(1, new Vector3(leftEnd.x, leftEnd.y, 0f));
                aimLine.SetPosition(2, new Vector3(rightEnd.x, rightEnd.y, 0f));
                aimLine.SetPosition(3, playerPoint);
            }
            else
            {
                aimLine.SetPosition(0, playerPoint);
                aimLine.SetPosition(1, new Vector3(endPos.x, endPos.y, 0f));
            }
        }

        if (previewInstance != null)
            previewInstance.transform.position = new Vector3(endPos.x, endPos.y, 0f);
    }

    private void UpdateDirectionalAimDirection()
    {
        if (cam == null)
            cam = Camera.main;

        if (cam == null || playerTransform == null)
            return;

        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;
        Vector2 mouseWorld = cam.ScreenToWorldPoint(mouse);
        Vector2 direction = mouseWorld - (Vector2)playerTransform.position;

        if (direction.sqrMagnitude > 0.0001f)
            currentAimDirection = direction.normalized;
    }

    private static Vector2 RotateDirection(Vector2 direction, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);

        return new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos
        );
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
        if (aimLineInstance != null) { Destroy(aimLineInstance); aimLineInstance = null; }
        if (aimLineMaterial != null) { Destroy(aimLineMaterial); aimLineMaterial = null; }
        previewRenderer = null;
        rangeRenderer = null;
        aimLine = null;
    }
}
