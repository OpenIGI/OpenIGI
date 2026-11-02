using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A model's shape as a wireframe: drag to turn it, the wheel to zoom. Edges further away are drawn fainter, so the
/// shape reads without lighting; very large models draw their first triangles only, which still shows the shape.
/// </summary>
internal sealed class ModelPreview : EditorElement
{
    private const int TriangleBudget = 30_000;

    private readonly List<(Vector3f A, Vector3f B)> _edges = [];
    private Vector3f _centre;
    private float _radius = 1f;
    private float _yaw = 0.6f;
    private float _pitch = 0.35f;
    private float _zoom = 1f;
    private UiVec2 _grab;
    private (float Yaw, float Pitch) _grabAngles;

    public ModelPreview(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Shows a model, or nothing.</summary>
    /// <param name="model">The model.</param>
    public void Show(MefModel? model)
    {
        _edges.Clear();
        if (model is null)
        {
            return;
        }

        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (MefVertex vertex in vertices)
        {
            Vector3f p = vertex.Position;
            min = new Vector3f(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new Vector3f(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
        }

        _centre = new Vector3f((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
        Vector3f half = new((max.X - min.X) * 0.5f, (max.Y - min.Y) * 0.5f, (max.Z - min.Z) * 0.5f);
        _radius = MathF.Max(1e-3f, MathF.Sqrt((half.X * half.X) + (half.Y * half.Y) + (half.Z * half.Z)));
        _zoom = 1f;

        HashSet<(int, int)> seen = [];
        int triangles = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> indices = record.Indices.Span;
            for (int i = 0; i + 2 < indices.Length && triangles < TriangleBudget; i += 3, triangles++)
            {
                int a = record.ToVertexIndex(indices[i]);
                int b = record.ToVertexIndex(indices[i + 1]);
                int c = record.ToVertexIndex(indices[i + 2]);
                AddEdge(vertices, seen, a, b);
                AddEdge(vertices, seen, b, c);
                AddEdge(vertices, seen, c, a);
            }
        }
    }

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Move;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        _grab = e.Position;
        _grabAngles = (_yaw, _pitch);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        if (IsCapturing)
        {
            _yaw = _grabAngles.Yaw + ((e.Position.X - _grab.X) * 0.01f);
            _pitch = Math.Clamp(_grabAngles.Pitch + ((e.Position.Y - _grab.Y) * 0.01f), -1.5f, 1.5f);
        }
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, notches), 0.2f, 20f);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Input);
        if (_edges.Count == 0)
        {
            return;
        }

        // An orbit around the model's centre: turn about the vertical (Z, the games' up), then tilt, then a gentle
        // perspective so near and far edges separate.
        (float sy, float cy) = MathF.SinCos(_yaw);
        (float sp, float cp) = MathF.SinCos(_pitch);
        float scale = MathF.Min(Bounds.Width, Bounds.Height) * 0.42f * _zoom / _radius;
        UiVec2 middle = Bounds.Center;
        float distance = _radius * 3.5f;

        (UiVec2 Screen, float Depth) Project(Vector3f p)
        {
            float x = p.X - _centre.X;
            float y = p.Y - _centre.Y;
            float z = p.Z - _centre.Z;
            float rx = (x * cy) - (y * sy);
            float ry = (x * sy) + (y * cy);
            float depth = (ry * cp) - (z * sp);
            float up = (ry * sp) + (z * cp);
            float perspective = distance / (distance + depth);
            return (new UiVec2(middle.X + (rx * scale * perspective), middle.Y - (up * scale * perspective)), depth / _radius);
        }

        painter.PushClip(Bounds);
        foreach ((Vector3f a, Vector3f b) in _edges)
        {
            (UiVec2 pa, float da) = Project(a);
            (UiVec2 pb, float db) = Project(b);
            float near = Math.Clamp(0.5f - ((da + db) * 0.25f), 0.15f, 1f);
            painter.StrokeLine(pa, pb, Theme.Hairline, Theme.Accent.WithAlpha((byte)(40 + (200 * near))));
        }

        painter.PopClip();
    }

    private void AddEdge(ReadOnlySpan<MefVertex> vertices, HashSet<(int, int)> seen, int a, int b)
    {
        if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length || !seen.Add(a < b ? (a, b) : (b, a)))
        {
            return;
        }

        _edges.Add((vertices[a].Position, vertices[b].Position));
    }
}
