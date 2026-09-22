using UnityEngine;

/// <summary>Small, editable vector shield for the Fear resistance indicator.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EriShieldIcon : UnityEngine.UI.MaskableGraphic
{
    [Header("Editable icon appearance")]
    public Color BorderColor = new Color(0.04f, 0.05f, 0.08f, 1f);
    public Color FaceColor = new Color(0.36f, 0.76f, 1f, 1f);
    public Color DetailColor = new Color(0.92f, 0.98f, 1f, 1f);

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        // A broad top and tapered point distinguish this from the existing
        // downward weakness arrow even at the HUD's compact icon size.
        Vector2[] silhouette =
        {
            new Vector2(-0.85f, 0.88f), new Vector2(0.85f, 0.88f),
            new Vector2(0.73f, -0.3f), new Vector2(0f, -0.94f),
            new Vector2(-0.73f, -0.3f)
        };
        Polygon(mesh, silhouette, 1f, BorderColor);
        Polygon(mesh, silhouette, 0.76f, FaceColor);
        Vector2[] center =
        {
            new Vector2(-0.1f, 0.55f), new Vector2(0.1f, 0.55f),
            new Vector2(0.1f, -0.52f), new Vector2(-0.1f, -0.52f)
        };
        Polygon(mesh, center, 1f, DetailColor);
    }

    private void Polygon(UnityEngine.UI.VertexHelper mesh, Vector2[] vertices, float scale, Color tint)
    {
        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        Vector2 radius = new Vector2(rect.width, rect.height) * 0.5f * scale;
        int first = mesh.currentVertCount;
        tint *= color;
        mesh.AddVert(center, tint, Vector2.zero);
        foreach (Vector2 vertex in vertices)
            mesh.AddVert(center + Vector2.Scale(vertex, radius), tint, Vector2.zero);
        for (int i = 0; i < vertices.Length; i++)
            mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % vertices.Length);
    }
}
