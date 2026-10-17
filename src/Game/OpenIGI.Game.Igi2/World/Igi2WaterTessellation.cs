using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Cuts a sheet of water into triangles for one frame, finely near the eye and coarsely far from it:
/// <c>WaterRender_Draw</c>'s subdivision.
/// </summary>
/// <remarks>
/// <para>
/// The sheet starts as its square's two triangles. Each triangle taken off the stack has its three edges
/// measured: an edge is split when it is longer than the sheet's <c>Detail</c> times its distance from the eye
/// (the nearest point on it), and never below half a metre. No edge split, the triangle is drawn; one, two or
/// three, it is cut into two, three or four, which go back on the stack. A new vertex is a full sample of the
/// surface (<c>Water_VertexFunction</c>) at the edge's middle, and then is drawn only part of the way from the
/// edge's own midpoint towards it -- <c>(length² / threshold − 1) × 0.3</c> of it, at most all -- so a vertex
/// grows out of its edge as the eye closes on it rather than popping
/// (<c>WaterRender_CalculateRenderVertex</c>). A triangle wholly off one side of the view is dropped.
/// </para>
/// <para>
/// The water's look is computed at the vertices, so this is what decides how the water looks, not just its
/// shape: the reflection, the Fresnel term and the shallowness are all only as fine as these triangles are.
/// </para>
/// </remarks>
public sealed class Igi2WaterTessellation
{
    /// <summary>The most vertices a sheet is cut into, <c>WaterRender_ptVertexBuffer</c>'s 420,000 bytes.</summary>
    public const int MaximumVertices = 15000;

    /// <summary>The most indices, <c>WaterRender_ptIndexBuffer</c>'s 90,000 bytes.</summary>
    public const int MaximumIndices = 45000;

    /// <summary>The shortest edge ever split, squared, in engine units: half a metre.</summary>
    public const float ShortestSplitSquared = 4194304f;

    /// <summary>How quickly a new vertex grows out of its edge.</summary>
    public const float MorphRate = 0.30000001192092896f;

    private const int Behind = 16;

    private readonly Vertex[] _vertices = new Vertex[MaximumVertices];
    private readonly ushort[] _indices = new ushort[MaximumIndices];
    private readonly Stack<(int A, int B, int C)> _stack = new();
    private readonly Vertex3D[] _output = new Vertex3D[MaximumVertices];

    /// <summary>Gets how many vertices the last <see cref="Build"/> made.</summary>
    public int VertexCount { get; private set; }

    /// <summary>Gets how many indices it made.</summary>
    public int IndexCount { get; private set; }

    /// <summary>Gets the vertices, in metres in the sheet's own space, how shallow the water is in the diffuse U.</summary>
    public ReadOnlySpan<Vertex3D> Vertices => _output.AsSpan(0, VertexCount);

    /// <summary>Gets the triangles.</summary>
    public ReadOnlySpan<ushort> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Cuts the sheet up for one view.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="field">Its waves.</param>
    /// <param name="seconds">The wave clock.</param>
    /// <param name="eye">The eye, in the sheet's space and in engine units.</param>
    /// <param name="orientation">The eye's axes: right, up and forward, row by row.</param>
    /// <param name="projection">The view's projection, which says where the view's sides are.</param>
    /// <param name="ground">The ground's height under a world point, in world units; none for no ground.</param>
    public void Build(
        in Igi2WaterSheet sheet,
        Igi2WaterWaveField field,
        double seconds,
        Vector3f eye,
        Matrix3f orientation,
        in Matrix4f projection,
        Func<double, double, double?>? ground)
    {
        ArgumentNullException.ThrowIfNull(field);

        VertexCount = 0;
        IndexCount = 0;
        _stack.Clear();

        Context context = new(sheet, field, seconds, eye, orientation, projection, ground);
        float radius = (float)sheet.Radius;

        // The square's corners, (−,−), (+,−), (−,+), (+,+), each at its depth on the sheet's plane.
        for (int i = 0; i < 4; i++)
        {
            float x = (((i & 1) * 2) - 1) * radius;
            float y = ((i & 2) - 1) * radius;
            Vertex corner = context.Sample(x, y, context.DepthOf(x, y));
            corner.Outcode = context.Outcode(corner.Position);
            _vertices[VertexCount++] = corner;
        }

        Push(0, 1, 2);
        Push(3, 2, 1);

        Span<bool> split = stackalloc bool[3];
        Span<float> morph = stackalloc float[3];
        while (_stack.Count > 0)
        {
            (int a, int b, int c) = _stack.Pop();
            ReadOnlySpan<int> corners = [a, b, c];
            int splits = 0, lastSplit = 0, lastWhole = 0;

            for (int edge = 0; edge < 3; edge++)
            {
                Vector3f from = _vertices[corners[edge]].Position - eye;
                Vector3f to = _vertices[corners[(edge + 1) % 3]].Position - eye;
                Vector3f along = to - from;
                float length = Vector3f.Dot(along, along);

                // The point of the edge nearest the eye.
                float t = Vector3f.Dot(along, from) * (-1f / length);
                Vector3f nearest = t < 0f ? from : t <= 1f ? from + (along * t) : to;
                float threshold = sheet.Detail * sheet.Detail * Vector3f.Dot(nearest, nearest);

                // Room for the vertices a split makes, which the original never runs out of.
                if (length <= threshold || length <= ShortestSplitSquared || VertexCount + 3 > MaximumVertices)
                {
                    split[edge] = false;
                    lastWhole = edge;
                }
                else
                {
                    splits++;
                    split[edge] = true;
                    lastSplit = edge;
                    morph[edge] = MathF.Min(((length / threshold) - 1f) * MorphRate, 1f);
                }
            }

            switch (splits)
            {
                case 0:
                    Emit(a, b, c);
                    break;
                case 1:
                    SplitOne(context, corners, lastSplit, morph[lastSplit]);
                    break;
                case 2:
                    SplitTwo(context, corners, lastWhole, morph);
                    break;
                default:
                    SplitThree(context, corners, morph);
                    break;
            }
        }

        for (int i = 0; i < VertexCount; i++)
        {
            ref Vertex vertex = ref _vertices[i];
            _output[i] = new Vertex3D(
                vertex.Position * (float)(1.0 / WorldScene.UnitsPerMetre),
                vertex.Normal,
                vertex.Shallowness,
                0f,
                0f,
                0f,
                Bgra32.White);
        }
    }

    private bool Culled(int a, int b, int c) =>
        (_vertices[a].Outcode & _vertices[b].Outcode & _vertices[c].Outcode) != 0;

    private void Push(int a, int b, int c)
    {
        if (!Culled(a, b, c))
        {
            _stack.Push((a, b, c));
        }
    }

    private void Emit(int a, int b, int c)
    {
        if (Culled(a, b, c) || IndexCount + 3 > MaximumIndices)
        {
            return;
        }

        _indices[IndexCount++] = (ushort)a;
        _indices[IndexCount++] = (ushort)b;
        _indices[IndexCount++] = (ushort)c;
    }

    // A new vertex on the edge from one corner to another: the surface sampled at the edge's middle, at the
    // middle of the two corners' depths, then grown only part of the way out of the edge.
    private int Midpoint(in Context context, int from, int to, float grown)
    {
        ref Vertex a = ref _vertices[from];
        ref Vertex b = ref _vertices[to];
        float depth = (a.Depth + b.Depth) * 0.5f;
        Vector3f middle = (a.Position + b.Position) * 0.5f;
        Vertex vertex = context.Sample(middle.X, middle.Y, depth);

        if (grown < 1f)
        {
            float z = (a.Position.Z + b.Position.Z) * 0.5f;
            Vector3f normal = (a.Normal + b.Normal) * 0.5f;
            float shallowness = (a.Shallowness + b.Shallowness) * 0.5f;
            vertex.Position = new Vector3f(vertex.Position.X, vertex.Position.Y, ((vertex.Position.Z - z) * grown) + z);
            vertex.Normal = ((vertex.Normal - normal) * grown) + normal;
            vertex.Shallowness = ((vertex.Shallowness - shallowness) * grown) + shallowness;
        }

        vertex.Outcode = context.Outcode(vertex.Position);
        int index = VertexCount++;
        _vertices[index] = vertex;
        return index;
    }

    // One edge split: the two halves, the one on the far corner's side of the nearer end pushed first.
    private void SplitOne(in Context context, ReadOnlySpan<int> corners, int edge, float grown)
    {
        int start = corners[edge], end = corners[(edge + 1) % 3], opposite = corners[(edge + 2) % 3];
        int middle = Midpoint(context, start, end, grown);

        if (_vertices[start].Depth >= _vertices[end].Depth)
        {
            Push(end, opposite, middle);
            Push(opposite, start, middle);
        }
        else
        {
            Push(opposite, start, middle);
            Push(end, opposite, middle);
        }
    }

    // Two edges split, the whole one from the first corner to the second: a corner triangle at the third and
    // the quad left beside it in two.
    private void SplitTwo(in Context context, ReadOnlySpan<int> corners, int whole, ReadOnlySpan<float> morph)
    {
        int first = corners[whole], second = corners[(whole + 1) % 3], third = corners[(whole + 2) % 3];
        int nearSecond = Midpoint(context, second, third, morph[(whole + 1) % 3]);
        int nearFirst = Midpoint(context, first, third, morph[(whole + 2) % 3]);

        Push(third, nearFirst, nearSecond);
        Push(nearSecond, nearFirst, first);
        Push(nearSecond, first, second);
    }

    // Every edge split: the middle triangle and the three at the corners.
    private void SplitThree(in Context context, ReadOnlySpan<int> corners, ReadOnlySpan<float> morph)
    {
        Span<int> middles = stackalloc int[3];
        for (int edge = 0; edge < 3; edge++)
        {
            middles[edge] = Midpoint(context, corners[edge], corners[(edge + 1) % 3], morph[edge]);
        }

        Push(middles[0], middles[1], middles[2]);
        for (int corner = 0; corner < 3; corner++)
        {
            Push(corners[corner], middles[corner], middles[(corner + 2) % 3]);
        }
    }

    private struct Vertex
    {
        public Vector3f Position;
        public Vector3f Normal;
        public float Shallowness;
        public float Depth;
        public int Outcode;
    }

    private readonly struct Context(
        Igi2WaterSheet sheet,
        Igi2WaterWaveField field,
        double seconds,
        Vector3f eye,
        Matrix3f orientation,
        Matrix4f projection,
        Func<double, double, double?>? ground)
    {
        private readonly Vector3f _right = orientation.Row1;
        private readonly Vector3f _up = orientation.Row2;
        private readonly Vector3f _forward = orientation.Row3;

        // The view's half-widths per unit of depth: the projection's scales, inverted.
        private readonly float _wide = projection.M11 != 0f ? 1f / MathF.Abs(projection.M11) : float.MaxValue;
        private readonly float _tall = projection.M22 != 0f ? 1f / MathF.Abs(projection.M22) : float.MaxValue;

        /// <summary>How far in front of the eye a point of the sheet's plane is, the depth the waves flatten with.</summary>
        public float DepthOf(float x, float y) =>
            (_forward.X * (x - eye.X)) + (_forward.Y * (y - eye.Y)) - (_forward.Z * eye.Z);

        /// <summary><c>Water_VertexFunction</c> at a point of the sheet.</summary>
        public Vertex Sample(float x, float y, float depth)
        {
            float z = field.HeightAt(x, y, seconds, depth);
            return new Vertex
            {
                Position = new Vector3f(x, y, z),
                Normal = field.NormalAt(x, y, seconds, depth),
                Shallowness = Igi2WaterPresenter.ShallownessAt(
                    z + sheet.Centre.Z,
                    ground?.Invoke(sheet.Centre.X + x, sheet.Centre.Y + y),
                    sheet.AbsorptionLength),
                Depth = depth,
            };
        }

        /// <summary>Which sides of the view a point is beyond, <c>WaterRender_CalculateRenderVertex</c>'s outcode.</summary>
        public int Outcode(Vector3f position)
        {
            Vector3f offset = position - eye;
            float across = Vector3f.Dot(_right, offset), upward = Vector3f.Dot(_up, offset), ahead = Vector3f.Dot(_forward, offset);
            if (ahead <= 0f)
            {
                return Behind;
            }

            float halfWidth = ahead * _wide, halfHeight = ahead * _tall;
            int code = across < -halfWidth ? 1 : 0;
            code |= upward < -halfHeight ? 2 : 0;
            code |= across > halfWidth ? 4 : 0;
            code |= upward > halfHeight ? 8 : 0;
            return code;
        }
    }
}
