using UnityEngine;

/// <summary>Small editable uGUI clock for skills that are cooling down.</summary>
public sealed class EriCooldownIcon : UnityEngine.UI.MaskableGraphic
{
    [Range(1f, 3f)] public float StrokeWidth = 1.5f;

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Vector2 center = rectTransform.rect.center;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.43f;
        if (radius <= 0) return;
        const int steps = 20;
        for (int i = 0; i < steps; i++)
        {
            float start = i * Mathf.PI * 2f / steps;
            float end = (i + 1) * Mathf.PI * 2f / steps;
            AddLine(mesh, center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * radius,
                center + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius, StrokeWidth);
        }
        AddLine(mesh, center, center + Vector2.up * radius * 0.63f, StrokeWidth);
        AddLine(mesh, center, center + new Vector2(0.55f, -0.35f) * radius, StrokeWidth);
    }

    private void AddLine(UnityEngine.UI.VertexHelper mesh, Vector2 start, Vector2 end, float width)
    {
        Vector2 normal = new Vector2(-(end.y - start.y), end.x - start.x).normalized * (width * 0.5f);
        int first = mesh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = start - normal; mesh.AddVert(vertex);
        vertex.position = start + normal; mesh.AddVert(vertex);
        vertex.position = end + normal; mesh.AddVert(vertex);
        vertex.position = end - normal; mesh.AddVert(vertex);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}
