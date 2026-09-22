using UnityEngine;

/// <summary>Lightweight procedural ring used by the interaction reticle.</summary>
public sealed class UIRingGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField, Min(0.5f)] private float thickness = 1.25f;
    [SerializeField, Range(12, 96)] private int segments = 48;

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Mathf.Max(0.5f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect bounds = rectTransform.rect;
        float outerRadius = Mathf.Max(0f, Mathf.Min(bounds.width, bounds.height) * 0.5f);
        float innerRadius = Mathf.Max(0f, outerRadius - thickness);
        Vector2 center = bounds.center;
        int count = Mathf.Max(12, segments);

        for (int i = 0; i <= count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            UnityEngine.UIVertex outer = UnityEngine.UIVertex.simpleVert;
            outer.color = color;
            outer.position = center + direction * outerRadius;
            vertexHelper.AddVert(outer);

            UnityEngine.UIVertex inner = UnityEngine.UIVertex.simpleVert;
            inner.color = color;
            inner.position = center + direction * innerRadius;
            vertexHelper.AddVert(inner);
        }

        for (int i = 0; i < count; i++)
        {
            int index = i * 2;
            vertexHelper.AddTriangle(index, index + 2, index + 1);
            vertexHelper.AddTriangle(index + 2, index + 3, index + 1);
        }
    }
}
