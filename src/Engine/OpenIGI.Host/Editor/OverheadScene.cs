using OpenIGI.Core.Graphics;

namespace OpenIGI.Host.Editor;

/// <summary>How a group of an <see cref="OverheadScene"/>'s triangles is drawn, in the order the groups are drawn.</summary>
public enum OverheadPass
{
    /// <summary>The ground's first pass over a patch: opaque, writing its height.</summary>
    GroundFirst = 0,

    /// <summary>A later pass of the ground: added over the first where it lies, at the same height.</summary>
    GroundAdded = 1,

    /// <summary>A placed model's opaque faces, cut away above the floor being looked at.</summary>
    Solid = 2,

    /// <summary>A placed model's faces with holes (fences, leaves): texels under half alpha are not drawn.</summary>
    CutOut = 3,
}

/// <summary>A texture an <see cref="OverheadScene"/>'s triangles sample.</summary>
/// <param name="Width">Its width in texels.</param>
/// <param name="Height">Its height in texels.</param>
/// <param name="Pixels">Its texels, row by row.</param>
/// <param name="Repeat">Whether it tiles past its edges (a model's) rather than stopping at them (a ground tile's).</param>
public sealed record OverheadTexture(int Width, int Height, Bgra32[] Pixels, bool Repeat);

/// <summary>Triangles drawn one way with one texture.</summary>
/// <param name="Pass">How they are drawn.</param>
/// <param name="Texture">The texture they sample, an index into <see cref="OverheadScene.Textures"/>; -1 for plain stone.</param>
/// <param name="Corners">Three corners a triangle, <see cref="OverheadScene.Stride"/> floats a corner.</param>
public sealed record OverheadGroup(OverheadPass Pass, int Texture, float[] Corners);

/// <summary>
/// The loaded level as it looks from straight above, for a plan drawn at any zoom and cut at any floor: the ground's passes as
/// the terrain draws them, every placed model's faces with its own textures, and the collision triangles of everything built.
/// </summary>
/// <remarks>
/// A corner is seven floats: east and north in metres from the scene's south-west corner, up in metres, the texture's two
/// coordinates, its weight (a ground pass's share) or light (a model's shading), and the height of the ground under its face,
/// so a face can be cut by its height over the ground: one storey of a building at a time. The multiplayer layout page reads
/// the same floats from <c>mp_mapshot</c>'s <c>mesh.bin</c>.
/// </remarks>
public sealed class OverheadScene
{
    /// <summary>The floats a corner takes.</summary>
    public const int Stride = 7;

    /// <summary>The floats a collision triangle takes: three corners of east, north and up, then the ground under it.</summary>
    public const int CollisionStride = 10;

    private const double GroundCell = 8.0;

    private readonly int _columns;
    private readonly int _rows;
    private readonly int[] _cellStart;
    private readonly int[] _cellTriangles;
    private readonly float[] _groundCorners;

    /// <summary>Initializes a new instance of the <see cref="OverheadScene"/> class.</summary>
    /// <param name="westMetres">The world X of its west edge, in metres.</param>
    /// <param name="southMetres">The world Y of its south edge.</param>
    /// <param name="eastMetres">The world X of its east edge.</param>
    /// <param name="northMetres">The world Y of its north edge.</param>
    /// <param name="textures">The textures.</param>
    /// <param name="groups">The triangles, in the order they are drawn.</param>
    public OverheadScene(double westMetres, double southMetres, double eastMetres, double northMetres, IReadOnlyList<OverheadTexture> textures, IReadOnlyList<OverheadGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        WestMetres = westMetres;
        SouthMetres = southMetres;
        EastMetres = eastMetres;
        NorthMetres = northMetres;
        Textures = textures;
        Groups = groups;

        // The ground's first passes cover it once: indexed by the cells they lie over, for the height of the ground anywhere.
        _groundCorners = [.. groups.Where(static g => g.Pass == OverheadPass.GroundFirst).SelectMany(static g => g.Corners)];
        _columns = Math.Max(1, (int)Math.Ceiling((eastMetres - westMetres) / GroundCell) + 1);
        _rows = Math.Max(1, (int)Math.Ceiling((northMetres - southMetres) / GroundCell) + 1);
        int triangles = _groundCorners.Length / (3 * Stride);

        // Counted a cell, summed into where each cell's list starts, then filled.
        _cellStart = new int[(_columns * _rows) + 1];
        ForEachCell(triangles, (cell, _) => _cellStart[cell + 1]++);
        for (int i = 1; i < _cellStart.Length; i++)
        {
            _cellStart[i] += _cellStart[i - 1];
        }

        int[] fill = (int[])_cellStart.Clone();
        int[] list = new int[_cellStart[^1]];
        ForEachCell(triangles, (cell, t) => list[fill[cell]++] = t);
        _cellTriangles = list;
    }

    /// <summary>Gets the world X of the west edge, in metres.</summary>
    public double WestMetres { get; }

    /// <summary>Gets the world Y of the south edge, in metres.</summary>
    public double SouthMetres { get; }

    /// <summary>Gets the world X of the east edge, in metres.</summary>
    public double EastMetres { get; }

    /// <summary>Gets the world Y of the north edge, in metres.</summary>
    public double NorthMetres { get; }

    /// <summary>Gets the textures.</summary>
    public IReadOnlyList<OverheadTexture> Textures { get; }

    /// <summary>Gets the triangles, in the order they are drawn.</summary>
    public IReadOnlyList<OverheadGroup> Groups { get; }

    /// <summary>Gets everything built, as its collision triangles: <see cref="CollisionStride"/> floats each, east and north from the south-west corner.</summary>
    public float[] Collision { get; init; } = [];

    /// <summary>Gets whether the scene has the ground's own triangles.</summary>
    public bool HasGround => _groundCorners.Length > 0;

    /// <summary>The height of the bare ground under a point, from the ground's own triangles; NaN where it has none.</summary>
    /// <param name="xMetres">The world X, in metres.</param>
    /// <param name="yMetres">The world Y.</param>
    /// <returns>The height in metres.</returns>
    public double GroundAt(double xMetres, double yMetres)
    {
        double x = xMetres - WestMetres, y = yMetres - SouthMetres;
        int column = (int)Math.Floor(x / GroundCell), row = (int)Math.Floor(y / GroundCell);
        if ((uint)column >= (uint)_columns || (uint)row >= (uint)_rows)
        {
            return double.NaN;
        }

        int cell = (row * _columns) + column;
        float[] v = _groundCorners;
        for (int i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
        {
            int o = _cellTriangles[i] * 3 * Stride;
            if (Barycentric(v[o], v[o + 1], v[o + Stride], v[o + Stride + 1], v[o + (2 * Stride)], v[o + (2 * Stride) + 1], x, y) is { } l)
            {
                return (l.A * v[o + 2]) + (l.B * v[o + Stride + 2]) + (l.C * v[o + (2 * Stride) + 2]);
            }
        }

        return double.NaN;
    }

    /// <summary>The weights of a triangle's three corners at a point across the ground, or null when the point is outside it.</summary>
    /// <param name="ax">The first corner's east.</param>
    /// <param name="ay">Its north.</param>
    /// <param name="bx">The second's east.</param>
    /// <param name="by">Its north.</param>
    /// <param name="cx">The third's east.</param>
    /// <param name="cy">Its north.</param>
    /// <param name="x">The point's east.</param>
    /// <param name="y">Its north.</param>
    /// <returns>The weights.</returns>
    public static (double A, double B, double C)? Barycentric(double ax, double ay, double bx, double by, double cx, double cy, double x, double y)
    {
        double d = ((by - cy) * (ax - cx)) + ((cx - bx) * (ay - cy));
        if (Math.Abs(d) < 1e-9)
        {
            return null;
        }

        double l1 = (((by - cy) * (x - cx)) + ((cx - bx) * (y - cy))) / d;
        double l2 = (((cy - ay) * (x - cx)) + ((ax - cx) * (y - cy))) / d;
        double l3 = 1 - l1 - l2;
        return l1 >= -1e-5 && l2 >= -1e-5 && l3 >= -1e-5 ? (l1, l2, l3) : null;
    }

    // Every cell each ground triangle's box lies over.
    private void ForEachCell(int triangles, Action<int, int> visit)
    {
        for (int t = 0; t < triangles; t++)
        {
            (int c0, int c1, int r0, int r1) = Cells(t);
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    visit((r * _columns) + c, t);
                }
            }
        }
    }

    // The cells a ground triangle's box lies over.
    private (int C0, int C1, int R0, int R1) Cells(int triangle)
    {
        int o = triangle * 3 * Stride;
        float[] v = _groundCorners;
        double minX = Math.Min(v[o], Math.Min(v[o + Stride], v[o + (2 * Stride)]));
        double maxX = Math.Max(v[o], Math.Max(v[o + Stride], v[o + (2 * Stride)]));
        double minY = Math.Min(v[o + 1], Math.Min(v[o + Stride + 1], v[o + (2 * Stride) + 1]));
        double maxY = Math.Max(v[o + 1], Math.Max(v[o + Stride + 1], v[o + (2 * Stride) + 1]));
        return (
            Math.Clamp((int)Math.Floor(minX / GroundCell), 0, _columns - 1),
            Math.Clamp((int)Math.Floor(maxX / GroundCell), 0, _columns - 1),
            Math.Clamp((int)Math.Floor(minY / GroundCell), 0, _rows - 1),
            Math.Clamp((int)Math.Floor(maxY / GroundCell), 0, _rows - 1));
    }
}
