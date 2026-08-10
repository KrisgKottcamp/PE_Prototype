using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class TargetingPreviewRenderer2D : MonoBehaviour
    {
        [SerializeField]
        private PlayerSpellTargetingController targetingController;

        [SerializeField]
        private LineRenderer outline;

        [Header("Automatic Fallback")]
        [Tooltip("Creates a usable runtime LineRenderer when no authored outline is assigned.")]
        [SerializeField]
        private bool createOutlineWhenMissing = true;

        [SerializeField, Min(0.005f)]
        private float fallbackLineWidth = 0.035f;

        [SerializeField]
        private int fallbackSortingOrder = 200;

        [SerializeField]
        private Transform targetMarker;

        [SerializeField, Min(6)]
        private int circleSegments = 48;

        [SerializeField, Min(2)]
        private int coneArcSegments = 16;

        [SerializeField]
        private Color validColor = new Color(0.25f, 1f, 0.55f, 0.9f);

        [SerializeField]
        private Color invalidColor = new Color(1f, 0.25f, 0.25f, 0.9f);

        private Material ownedFallbackMaterial;

        private void Awake()
        {
            if (targetingController == null)
            {
                targetingController =
                    GetComponentInParent<PlayerSpellTargetingController>();
            }

            EnsureOutline();
        }

        private void LateUpdate()
        {
            if (targetingController == null ||
                !targetingController.IsTargeting)
            {
                SetVisible(false);
                return;
            }

            PlayerTargetingPreview preview =
                targetingController.CurrentPreview;
            Color color = preview.IsValid ? validColor : invalidColor;

            if (outline != null)
            {
                outline.enabled = preview.Shape !=
                                  PlayerTargetingPreviewShape.None &&
                                  preview.Shape !=
                                  PlayerTargetingPreviewShape.Target;
                outline.startColor = color;
                outline.endColor = color;
                outline.useWorldSpace = true;

                if (outline.enabled)
                    DrawOutline(preview);
            }

            if (targetMarker != null)
            {
                bool showMarker = preview.Shape ==
                                  PlayerTargetingPreviewShape.Target;
                targetMarker.gameObject.SetActive(showMarker);

                if (showMarker)
                {
                    targetMarker.position = new Vector3(
                        preview.AimPoint.x,
                        preview.AimPoint.y,
                        targetMarker.position.z);
                }
            }
        }

        private void DrawOutline(in PlayerTargetingPreview preview)
        {
            switch (preview.Shape)
            {
                case PlayerTargetingPreviewShape.Line:
                    outline.loop = false;
                    outline.positionCount = 2;
                    outline.SetPosition(0, preview.Origin);
                    outline.SetPosition(1, preview.AimPoint);
                    break;

                case PlayerTargetingPreviewShape.Circle:
                    DrawCircle(preview.AimPoint, preview.Radius);
                    break;

                case PlayerTargetingPreviewShape.Cone:
                    DrawCone(preview);
                    break;
            }
        }

        private void DrawCircle(Vector2 center, float radius)
        {
            int segments = Mathf.Max(6, circleSegments);
            outline.loop = true;
            outline.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                outline.SetPosition(
                    i,
                    center + new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) * radius);
            }
        }

        private void DrawCone(in PlayerTargetingPreview preview)
        {
            int segments = Mathf.Max(2, coneArcSegments);
            float halfAngle = preview.ConeAngle * 0.5f;
            float distance = preview.Range > 0f
                ? preview.Range
                : Vector2.Distance(preview.Origin, preview.AimPoint);
            float baseAngle = Mathf.Atan2(
                preview.Direction.y,
                preview.Direction.x) * Mathf.Rad2Deg;

            outline.loop = true;
            outline.positionCount = segments + 2;
            outline.SetPosition(0, preview.Origin);

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = (baseAngle - halfAngle +
                               preview.ConeAngle * t) * Mathf.Deg2Rad;
                Vector2 point = preview.Origin + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * distance;
                outline.SetPosition(i + 1, point);
            }
        }

        private void SetVisible(bool visible)
        {
            if (outline != null)
                outline.enabled = visible;

            if (targetMarker != null && targetMarker.gameObject.activeSelf)
                targetMarker.gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            circleSegments = Mathf.Max(6, circleSegments);
            coneArcSegments = Mathf.Max(2, coneArcSegments);
            fallbackLineWidth = Mathf.Max(0.005f, fallbackLineWidth);
        }

        private void EnsureOutline()
        {
            if (outline != null || !createOutlineWhenMissing)
                return;

            Renderer sourceRenderer = null;
            Renderer[] existingRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < existingRenderers.Length; i++)
            {
                if (!(existingRenderers[i] is LineRenderer))
                {
                    sourceRenderer = existingRenderers[i];
                    break;
                }
            }

            GameObject child = new GameObject("V2 Targeting Outline");
            child.transform.SetParent(transform, false);
            child.layer = sourceRenderer != null
                ? sourceRenderer.gameObject.layer
                : gameObject.layer;
            outline = child.AddComponent<LineRenderer>();
            outline.enabled = false;
            outline.useWorldSpace = true;
            outline.widthMultiplier = Mathf.Max(0.005f, fallbackLineWidth);
            outline.numCapVertices = 2;
            outline.numCornerVertices = 2;
            outline.alignment = LineAlignment.View;
            outline.sortingOrder = fallbackSortingOrder;

            if (sourceRenderer != null)
            {
                outline.sortingLayerID = sourceRenderer.sortingLayerID;
                outline.sortingOrder = Mathf.Max(
                    fallbackSortingOrder,
                    sourceRenderer.sortingOrder + 50);
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
            {
                ownedFallbackMaterial = new Material(shader)
                {
                    name = "Runtime V2 Targeting Material"
                };
                outline.sharedMaterial = ownedFallbackMaterial;
            }
        }

        private void OnDestroy()
        {
            if (ownedFallbackMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(ownedFallbackMaterial);
            else
                DestroyImmediate(ownedFallbackMaterial);
        }
    }
}
