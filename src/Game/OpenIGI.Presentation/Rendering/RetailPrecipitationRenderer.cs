using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>How a title draws its <see cref="RetailPrecipitation"/>.</summary>
public enum PrecipitationStyle
{
    /// <summary>
    /// Project I.G.I.'s (<c>Rain_FillVB</c>, <c>0x499090</c>): rain as a pixel-wide line rising from each drop, clear at its top and
    /// the weather's alpha at its foot; snow as a square of <c>snow.tex</c> facing the eye.
    /// </summary>
    Igi1,

    /// <summary>
    /// I.G.I. 2's (<c>Rain_UpdateVertexBuffer</c>): every drop a rectangle facing the eye in the weather's colour and alpha, rain three
    /// flakes wide and ten tall, drawn once for each of its density's scattered copies.
    /// </summary>
    Igi2,
}

/// <summary>
/// Draws <see cref="RetailPrecipitation"/> as both originals do: its one tile of drops repeated over the ground around the eye, in up
/// to three layers above each tile, each stopping at the ground or roof beneath it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Rain_UpdateGrid</c> (<c>0x498BD0</c>) keeps a twelve-by-twelve ring of tiles about the eye, each holding the height a drop let
/// fall from its corner would stop at -- the terrain or whatever stands on it, so the weather stops on a roof and never falls indoors.
/// A tile that comes into the ring is dropped again; no more a frame than the eye moved tiles, all of them the first time.
/// </para>
/// <para>
/// <c>Rain_Draw</c> (<c>0x498F10</c>) and <c>Rain_DrawCell</c> (<c>0x499430</c>) draw the nine-by-nine tiles ahead of the ring's
/// seam, farthest first, each with the quarter of the drops its tile's parity picks. The layers are 24576 units tall and stand on a
/// floor that steps with the eye; a layer wholly above the tile's highest corner draws all its quarter, one below it none, and the
/// layer it ends in only the drops still above it, which the quarter's list, highest first, makes a prefix.
/// </para>
/// <para>
/// The original's outcode test leaves out tiles wholly off one side of the screen; the device clips those anyway, so every tile
/// whose four corners are known is drawn. Fog is off for it in both games; nothing it draws is far enough from the eye to fog.
/// </para>
/// </remarks>
public sealed class RetailPrecipitationRenderer : IDisposable
{
    /// <summary>The most quads one frame draws.</summary>
    public const int MaximumQuads = 16000;

    private const int Ring = 12;
    private const int Drawn = 9;
    private const double GridOrigin = 59924.48193359375;
    private const double LayerOrigin = 36864.0;
    private const double Top = 73728.0;
    private const float StreakUnits = 2048f;
    private const float FlakeUnits = 81.92f;
    private const int FirstBudget = 10000;

    private static readonly SurfaceMaterial Unlit = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly PrecipitationStyle _style;
    private readonly Cell[] _cells = new Cell[Ring * Ring];
    private readonly List<int>[] _groups = [[], [], [], []];
    private readonly List<float>[] _groupHeights = [[], [], [], []];
    private readonly List<(double Depth, int I, int J)> _blocks = [];
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumQuads * 4];
    private readonly ushort[] _indices = QuadIndices.Build(MaximumQuads);
    private Vector3f[] _offsets = [Vector3f.Zero];
    private MeshHandle _mesh = MeshHandle.None;
    private bool _hasLast;
    private Vector3d _last;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RetailPrecipitationRenderer"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="style">Whose drawing it follows.</param>
    public RetailPrecipitationRenderer(IGraphicsDevice device, PrecipitationStyle style)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _style = style;
    }

    /// <summary>Gets or sets what a flake, or an I.G.I. 2 drop, is drawn with.</summary>
    public TextureHandle Texture { get; set; } = TextureHandle.None;

    /// <summary>Gets or sets the weather's colour and alpha: the foot of an I.G.I. drop, or all of an I.G.I. 2 drop.</summary>
    public Bgra32 Colour { get; set; } = new(255, 255, 255, 80);

    /// <summary>Gets or sets how far above the eye a tile's drop starts, in world units.</summary>
    public double HeightUnits { get; set; } = 50 * 4096.0;

    /// <summary>Gets or sets how far it falls before it is let go, in world units.</summary>
    public double FallUnits { get; set; } = 70 * 4096.0;

    /// <summary>Gets or sets the scattered copies I.G.I. 2 draws each drop at, in world units (<c>Rain_atRainOffset</c>, as many as its density).</summary>
    public IReadOnlyList<Vector3f> Copies
    {
        get => _offsets;
        set => _offsets = value is { Count: > 0 } ? [.. value] : [Vector3f.Zero];
    }

    /// <summary>Forgets the ring, as a new level's weather starts without one.</summary>
    public void Reset()
    {
        Array.Clear(_cells);
        _hasLast = false;
    }

    /// <summary>Draws the weather around the eye.</summary>
    /// <param name="weather">The weather.</param>
    /// <param name="camera">The eye, in metres.</param>
    /// <param name="projection">The projection the frame is drawn with.</param>
    /// <param name="viewportHeight">The picture's height in pixels, which a line is one of.</param>
    /// <param name="ground">What a tile's drop falls onto: the terrain and what stands on it.</param>
    public void Draw(RetailPrecipitation weather, in Camera3D camera, in Matrix4f projection, int viewportHeight, WorldRayCast? ground)
    {
        ArgumentNullException.ThrowIfNull(weather);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Rain_Render: nothing, and the ring forgotten, while the weather is faded out.
        if (weather.Fade == 0f || weather.Count == 0)
        {
            _hasLast = false;
            return;
        }

        Vector3d eye = camera.Position * WorldScene.UnitsPerMetre;
        (int seamX, int seamY) = UpdateRing(eye, ground);
        BuildGroups(weather);

        double floor = Math.Floor(((eye.Z - LayerOrigin) / RetailPrecipitation.LayerUnits) + 0.5) * RetailPrecipitation.LayerUnits;
        Matrix3f view = camera.Orientation;
        Vector3f right = new(view.M11, view.M12, view.M13);
        Vector3f up = new(view.M21, view.M22, view.M23);
        Vector3f forward = new(view.M31, view.M32, view.M33);

        // Farthest first, as the sorted list draws them blended over one another.
        _blocks.Clear();
        for (int a = 0; a < Drawn; a++)
        {
            for (int b = 0; b < Drawn; b++)
            {
                int i = (seamX + 1 + a) % Ring, j = (seamY + 1 + b) % Ring;
                if (!Known(i, j) || !Known(i, j + 1) || !Known(i + 1, j) || !Known(i + 1, j + 1))
                {
                    continue;
                }

                double depth = Math.Max(
                    Math.Max(Depth(i, j, eye, forward, floor), Depth(i, j + 1, eye, forward, floor)),
                    Math.Max(Depth(i + 1, j, eye, forward, floor), Depth(i + 1, j + 1, eye, forward, floor)));
                _blocks.Add((depth, i, j));
            }
        }

        _blocks.Sort(static (x, y) => y.Depth.CompareTo(x.Depth));

        // One pixel at a metre's depth, in metres: what a line is widened to either side by half of.
        float pixel = 2f / Math.Max(viewportHeight, 1) / MathF.Max(MathF.Abs(projection.M22), 1e-6f);
        int quads = 0;
        foreach ((_, int i, int j) in _blocks)
        {
            ref Cell corner = ref At(i, j);
            double highest = Math.Max(Math.Max(At(i, j).Z, At(i, j + 1).Z), Math.Max(At(i + 1, j).Z, At(i + 1, j + 1).Z));
            int group = (((int)Math.Floor(corner.Y / RetailPrecipitation.TileUnits) & 1) << 1)
                | ((int)Math.Floor(corner.X / RetailPrecipitation.TileUnits) & 1);
            double above = highest - floor - Top;
            for (int layer = 1; layer <= 3; layer++)
            {
                double layerFloor = floor + Top - (layer * RetailPrecipitation.LayerUnits);
                double standing = above + (layer * RetailPrecipitation.LayerUnits);
                int count = Showing(group, standing);
                for (int n = 0; n < count && quads < MaximumQuads; n++)
                {
                    WeatherDrop drop = weather.Drops[_groups[group][n]];
                    Vector3d at = new(corner.X + drop.X, corner.Y + drop.Y, layerFloor + drop.Z);
                    quads = Emit(weather, at - eye, quads, right, up, forward, pixel);
                }
            }
        }

        if (quads == 0)
        {
            return;
        }

        if (!_mesh.IsValid)
        {
            _mesh = _device.CreateAnimatedMesh(_vertices, _indices);
        }

        _device.UpdateAnimatedMesh(_mesh, _vertices);
        _device.ClearPoseInterpolation();
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(Unlit);
        try
        {
            TextureHandle texture = _style == PrecipitationStyle.Igi1 && !weather.IsSnow ? TextureHandle.None : Texture;
            _device.DrawMesh(_mesh, 0, quads * 6, camera.CreateModelView(camera.Position), texture, TextureHandle.None, BlendMode.AlphaBlend);
        }
        finally
        {
            _device.SetMaterialOverride(SurfaceMaterial.Default);
            _device.SetDepthState(DepthState.TestAndWrite);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }
    }

    // Rain_UpdateGrid: the ring's tiles placed about the eye, and any that came in since the last frame dropped onto what is below.
    private (int SeamX, int SeamY) UpdateRing(Vector3d eye, WorldRayCast? ground)
    {
        int ix = (int)Math.Floor(((eye.X - GridOrigin) / RetailPrecipitation.TileUnits) + 0.5);
        int iy = (int)Math.Floor(((eye.Y - GridOrigin) / RetailPrecipitation.TileUnits) + 0.5);
        int seamX = ((ix % Ring) + Ring) % Ring;
        int seamY = ((iy % Ring) + Ring) % Ring;
        int budget = _hasLast
            ? (int)Math.Ceiling(Math.Abs(eye.X - _last.X) / RetailPrecipitation.TileUnits * Ring)
                + (int)Math.Ceiling(Math.Abs(eye.Y - _last.Y) / RetailPrecipitation.TileUnits * Ring)
            : FirstBudget;
        _last = eye;
        _hasLast = true;

        double x0 = (ix - seamX) * (double)RetailPrecipitation.TileUnits;
        double y0 = (iy - seamY) * (double)RetailPrecipitation.TileUnits;
        double span = Ring * (double)RetailPrecipitation.TileUnits;
        for (int i = 0; i < Ring; i++)
        {
            double x = x0 + (i * (double)RetailPrecipitation.TileUnits) + (i < seamX ? span : 0);
            for (int j = 0; j < Ring; j++)
            {
                double y = y0 + (j * (double)RetailPrecipitation.TileUnits) + (j < seamY ? span : 0);
                ref Cell cell = ref At(i, j);
                if (cell.Known && (cell.X != x || cell.Y != y))
                {
                    cell.Known = false;
                }

                if (!cell.Known && budget-- > 0)
                {
                    Vector3d from = new(x, y, eye.Z + HeightUnits);
                    Vector3d to = new(x, y, from.Z - FallUnits);
                    cell = new Cell(x, y, ground is not null && ground(from, to, out WorldRayHit hit) ? hit.Position.Z : to.Z, Known: true);
                }
            }
        }

        return (seamX, seamY);
    }

    // Rain_FillVB's order: from the highest drop back through the list, each into its group, which keeps the height of the drop that
    // follows it -- what the layer cut compares against.
    private void BuildGroups(RetailPrecipitation weather)
    {
        ReadOnlySpan<WeatherDrop> drops = weather.Drops;
        int top = 0;
        for (int i = 1; i < drops.Length; i++)
        {
            if (drops[i].Z > drops[top].Z)
            {
                top = i;
            }
        }

        for (int k = 0; k < 4; k++)
        {
            _groups[k].Clear();
            _groupHeights[k].Clear();
        }

        int d = top;
        for (int i = 0; i < drops.Length; i++)
        {
            int group = drops[d].Group & 3;
            _groups[group].Add(d);
            d = d == 0 ? drops.Length - 1 : d - 1;
            _groupHeights[group].Add(drops[d].Z);
        }
    }

    // How many of a group's drops a layer shows, its tile's highest corner the given height above the layer's floor.
    private int Showing(int group, double ground)
    {
        List<float> heights = _groupHeights[group];
        if (ground < 0)
        {
            return heights.Count;
        }

        if (ground > RetailPrecipitation.LayerUnits)
        {
            return 0;
        }

        int n = 0;
        while (n < heights.Count && heights[n] > ground)
        {
            n++;
        }

        return n;
    }

    // One drop's quad, camera-relative, in metres.
    private int Emit(RetailPrecipitation weather, Vector3d fromEye, int quads, Vector3f right, Vector3f up, Vector3f forward, float pixel)
    {
        float fade = weather.Fade;
        Vector3f centre = new(
            (float)(fromEye.X / WorldScene.UnitsPerMetre),
            (float)(fromEye.Y / WorldScene.UnitsPerMetre),
            (float)(fromEye.Z / WorldScene.UnitsPerMetre));
        if (_style == PrecipitationStyle.Igi1 && !weather.IsSnow)
        {
            // A line from the drop up by the streak's length, clear at the top: widened across the view to a pixel.
            float length = fade * StreakUnits / (float)WorldScene.UnitsPerMetre;
            Vector3f top = centre + new Vector3f(0f, 0f, length);
            float depth = MathF.Max(Vector3f.Dot(centre, forward), 0.05f);
            Vector3f across = Vector3f.Cross(Vector3f.UnitZ, centre).NormalizedOrSelf() * (0.5f * pixel * depth);
            Bgra32 foot = Colour;
            Bgra32 clear = new(foot.B, foot.G, foot.R, 0);
            return Quad(quads, centre - across, centre + across, top + across, top - across, foot, foot, clear, clear);
        }

        float half = fade * FlakeUnits / (float)WorldScene.UnitsPerMetre;
        float wide = half, tall = half;
        Bgra32 colour = new(255, 255, 255, 255);
        if (_style == PrecipitationStyle.Igi2 && !weather.IsSnow)
        {
            wide = 3f * half;
            tall = 10f * half;
            colour = Colour;
        }

        foreach (Vector3f offset in _offsets)
        {
            if (quads >= MaximumQuads)
            {
                break;
            }

            Vector3f at = centre + (offset / (float)WorldScene.UnitsPerMetre);
            Vector3f x = right * wide, y = up * tall;
            quads = Quad(quads, at - x - y, at + x - y, at + x + y, at - x + y, colour, colour, colour, colour);
        }

        return quads;
    }

    // A quad in drawing order: its two lower corners, then its two upper ones.
    private int Quad(int quads, Vector3f a, Vector3f b, Vector3f c, Vector3f d, Bgra32 ca, Bgra32 cb, Bgra32 cc, Bgra32 cd)
    {
        Vector3f normal = Vector3f.UnitZ;
        int v = quads * 4;
        _vertices[v] = new Vertex3D(a, normal, 0f, 1f, colour: ca);
        _vertices[v + 1] = new Vertex3D(b, normal, 1f, 1f, colour: cb);
        _vertices[v + 2] = new Vertex3D(c, normal, 1f, 0f, colour: cc);
        _vertices[v + 3] = new Vertex3D(d, normal, 0f, 0f, colour: cd);
        return quads + 1;
    }

    private ref Cell At(int i, int j) => ref _cells[((i % Ring) * Ring) + (j % Ring)];

    private bool Known(int i, int j) => At(i, j).Known;

    // Rain_ClipCells keeps a corner's depth halfway up the layers: the mean of its depth at their floor and at their top.
    private double Depth(int i, int j, Vector3d eye, Vector3f forward, double floor)
    {
        ref Cell cell = ref At(i, j);
        return ((cell.X - eye.X) * forward.X) + ((cell.Y - eye.Y) * forward.Y) + ((floor + (Top * 0.5) - eye.Z) * forward.Z);
    }

    private record struct Cell(double X, double Y, double Z, bool Known);
}
