using UnityEngine;

/// <summary>A lightweight procedural uGUI ring used by the exploration reticle.</summary>
public sealed class ThinRingGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField, Min(0.25f)] private float thickness = 1.25f;
    [SerializeField, Range(12, 96)] private int segments = 48;

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Mathf.Max(0.25f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect rect = rectTransform.rect;
        float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
        float inner = Mathf.Max(0f, outer - thickness);
        if (outer <= 0f || inner >= outer)
        {
            return;
        }

        Vector2 center = rect.center;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vertex.position = center + direction * outer;
            vertexHelper.AddVert(vertex);
            vertex.position = center + direction * inner;
            vertexHelper.AddVert(vertex);
        }

        for (int i = 0; i < segments; i++)
        {
            int index = i * 2;
            vertexHelper.AddTriangle(index, index + 2, index + 1);
            vertexHelper.AddTriangle(index + 2, index + 3, index + 1);
        }
    }
}
