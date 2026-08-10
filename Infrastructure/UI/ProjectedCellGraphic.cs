using UnityEngine;
using UnityEngine.UI;

internal sealed class ProjectedCellGraphic : MaskableGraphic
{
    private readonly Vector2[] _points = new Vector2[4];
    private Color32 _fill = new Color32(255, 20, 20, 74);
    private Color32 _border = new Color32(255, 32, 24, 235);
    private bool _hasQuad;

    internal void SetColors(Color32 fill, Color32 border)
    {
        if (_fill.Equals(fill) && _border.Equals(border))
            return;
        _fill = fill;
        _border = border;
        SetVerticesDirty();
    }

    internal void SetQuad(Vector2 left, Vector2 top, Vector2 right, Vector2 bottom)
    {
        var changed = !_hasQuad ||
                      !Approximately(_points[0], left) ||
                      !Approximately(_points[1], top) ||
                      !Approximately(_points[2], right) ||
                      !Approximately(_points[3], bottom);
        if (!changed)
            return;

        _points[0] = left;
        _points[1] = top;
        _points[2] = right;
        _points[3] = bottom;
        _hasQuad = true;
        SetVerticesDirty();
    }

    internal void ClearQuad()
    {
        if (!_hasQuad)
            return;
        _hasQuad = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!_hasQuad)
            return;

        vh.AddVert(_points[0], _fill, Vector2.zero);
        vh.AddVert(_points[1], _fill, Vector2.zero);
        vh.AddVert(_points[2], _fill, Vector2.zero);
        vh.AddVert(_points[3], _fill, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);

        AddEdge(vh, _points[0], _points[1], 2.4f, _border);
        AddEdge(vh, _points[1], _points[2], 2.4f, _border);
        AddEdge(vh, _points[2], _points[3], 2.4f, _border);
        AddEdge(vh, _points[3], _points[0], 2.4f, _border);
    }

    private static void AddEdge(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        var direction = to - from;
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        var normal = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        var start = vh.currentVertCount;
        vh.AddVert(from - normal, color, Vector2.zero);
        vh.AddVert(from + normal, color, Vector2.zero);
        vh.AddVert(to + normal, color, Vector2.zero);
        vh.AddVert(to - normal, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static bool Approximately(Vector2 left, Vector2 right)
    {
        return (left - right).sqrMagnitude <= 0.0001f;
    }
}
