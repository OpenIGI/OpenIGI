using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// How high the solid world stands over every square of ground: the level's roofs, awnings, walkways
/// and containers, seen from directly above.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it replaces.</b> "Indoors" used to mean <em>inside the axis-aligned bounding box of a task
/// named <c>Building_</c>-something</em>, and everything that cares whether the sky can be seen — the
/// fog, the falling rain, the splashes, the standing water — asked that one question. It is wrong in
/// both directions, and measurably so. On mission one a quarter of the ground inside those boxes has no
/// roof over it at all (a building standing at an angle to the world axes fills barely a third of its
/// own box, so the box swallows the apron in front of it), while two fifths of the ground that
/// genuinely <em>is</em> under a roof lies outside every one of them, because the structure sheltering
/// it was never given a <c>Building</c> task. The first is why the fog cut out and the rain stopped in
/// the middle of an open yard; the second is why puddles formed on warehouse floors.
/// </para>
/// <para>
/// <b>What it is instead.</b> A raster, one cell every <see cref="DefaultCellMetres"/> of world XY,
/// holding the height of the highest <em>solid</em> surface standing over that square — solid meaning
/// the collision geometry the level's placed objects carry, which is the same thing a bullet stops
/// against and therefore the same thing rain does. Sheltered is then a comparison rather than a
/// classification: the roof over this point is above it, and not so far above that it belongs to a
/// different part of the map.
/// </para>
/// <para>
/// <b>Why the highest surface and not the lowest one above the query.</b> Because a single number per
/// cell is what can be handed to a shader as one texel and read with a single fetch, and because IGI's
/// compounds are single-storey: the ground under a roof and the roof itself are the only two surfaces a
/// column usually holds. Storing the top means a roof does not shelter itself — its own upper face is
/// the height stored, so it fails the comparison and goes on collecting rain like any other open
/// ground, which is exactly what should happen.
/// </para>
/// <para>
/// <b>Vertical faces cost nothing.</b> A triangle is written into the cells whose centres fall inside
/// its XY projection, and a wall projects to a line, so walls and fences claim at most the thin strip
/// they actually stand on. No normal test is needed to keep them from roofing the yard beside them.
/// </para>
/// </remarks>
public sealed class ShelterHeightField
{
    /// <summary>The side of one cell, in metres.</summary>
    /// <remarks>
    /// Half a metre: fine enough that the edge of the shelter follows a wall rather than staircasing
    /// around it, coarse enough that a level's built-up area is a couple of megabytes.
    /// </remarks>
    public const double DefaultCellMetres = 0.5;

    /// <summary>The most cells the raster may hold on either axis.</summary>
    public const int MaxTexelsPerAxis = 2048;

    /// <summary>How far above a point a roof may be and still be its roof, in metres.</summary>
    /// <remarks>
    /// Generous — a hangar's ceiling is tens of metres up and is still the ceiling — but not unbounded,
    /// because a level's XY footprint can hold two areas at wildly different altitudes (mission one has
    /// a compound three kilometres of height below another) and a single top-down raster cannot tell
    /// them apart. A hundred metres separates "the roof over me" from "a different part of the map".
    /// </remarks>
    public const double MaxOverheadMetres = 100.0;

    /// <summary>How far above a surface its roof must stand before the surface counts as sheltered.</summary>
    /// <remarks>
    /// The ground itself is in the raster — a road is a placed object with collision like anything else
    /// — so the height over a stretch of open road is the road's own surface, and without a floor to the
    /// comparison every square of ground in the level would shelter itself. Three quarters of a metre
    /// clears that and clears a kerb, and is below any roof a person walks under.
    /// </remarks>
    public const double SurfaceHeadroomMetres = 0.75;

    /// <summary>How far above the eye a roof must stand before the camera counts as indoors.</summary>
    public const double EyeHeadroomMetres = 0.5;

    /// <summary>What an empty cell holds: nothing solid stands over this square at all.</summary>
    public const float OpenSky = float.NegativeInfinity;

    private readonly float[] _height;

    private ShelterHeightField(
        float[] height,
        int width,
        int depth,
        double originX,
        double originY,
        double cellMetres,
        float lowestZ,
        float highestZ)
    {
        _height = height;
        Width = width;
        Depth = depth;
        OriginX = originX;
        OriginY = originY;
        CellMetres = cellMetres;
        LowestZ = lowestZ;
        HighestZ = highestZ;
    }

    /// <summary>Gets a field covering nothing, which shelters nothing.</summary>
    public static ShelterHeightField Empty { get; } = new([], 0, 0, 0.0, 0.0, DefaultCellMetres, 0f, 0f);

    /// <summary>Gets the number of cells along world X.</summary>
    public int Width { get; }

    /// <summary>Gets the number of cells along world Y.</summary>
    public int Depth { get; }

    /// <summary>Gets the world X, in metres, of the low corner of cell zero.</summary>
    public double OriginX { get; }

    /// <summary>Gets the world Y, in metres, of the low corner of cell zero.</summary>
    public double OriginY { get; }

    /// <summary>Gets the side of one cell, in metres.</summary>
    public double CellMetres { get; }

    /// <summary>Gets the lowest surface height the raster holds, in world metres.</summary>
    public float LowestZ { get; }

    /// <summary>Gets the highest surface height the raster holds, in world metres.</summary>
    public float HighestZ { get; }

    /// <summary>Gets whether the field covers no ground, so that nothing is ever sheltered.</summary>
    public bool IsEmpty => _height.Length == 0;

    /// <summary>Rasterises the solid world of a scene, seen from above.</summary>
    /// <param name="placements">The level's placed objects, in world metres.</param>
    /// <param name="cellMetres">The side of one cell; defaults to <see cref="DefaultCellMetres"/>.</param>
    /// <returns>The field, or <see cref="Empty"/> when nothing placed carries collision geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Two passes over the collision meshes: the first to learn how much ground the level's objects
    /// stand on, the second to fill it. Placing the raster over the objects rather than over the whole
    /// terrain is what keeps it small — a level is kilometres across and its buildings are a few
    /// hundred metres of that.
    /// </remarks>
    public static ShelterHeightField Build(
        IEnumerable<WorldPlacement> placements,
        double cellMetres = DefaultCellMetres)
    {
        ArgumentNullException.ThrowIfNull(placements);

        List<(WorldPlacement Placement, MefCollisionMesh Mesh)> solids = CollectSolids(placements);
        if (solids.Count == 0)
        {
            return Empty;
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        Vector3d[] world = [];

        foreach ((WorldPlacement placement, MefCollisionMesh mesh) in solids)
        {
            world = Transform(placement, mesh, world);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3d vertex = world[i];
                minX = Math.Min(minX, vertex.X);
                minY = Math.Min(minY, vertex.Y);
                minZ = Math.Min(minZ, vertex.Z);
                maxX = Math.Max(maxX, vertex.X);
                maxY = Math.Max(maxY, vertex.Y);
                maxZ = Math.Max(maxZ, vertex.Z);
            }
        }

        if (minX > maxX)
        {
            return Empty;
        }

        // One cell size for both axes, decided by whichever span needs the coarser one. Cells wider than
        // they are deep would shelter differently depending on which way a wall ran.
        double cell = Math.Max(cellMetres, 0.05);
        cell = Math.Max(cell, (maxX - minX) / (MaxTexelsPerAxis - 1));
        cell = Math.Max(cell, (maxY - minY) / (MaxTexelsPerAxis - 1));

        int width = Math.Clamp((int)Math.Ceiling((maxX - minX) / cell) + 1, 1, MaxTexelsPerAxis);
        int depth = Math.Clamp((int)Math.Ceiling((maxY - minY) / cell) + 1, 1, MaxTexelsPerAxis);

        float[] height = new float[width * depth];
        Array.Fill(height, OpenSky);

        ShelterHeightField field = new(height, width, depth, minX, minY, cell, (float)minZ, (float)maxZ);

        foreach ((WorldPlacement placement, MefCollisionMesh mesh) in solids)
        {
            world = Transform(placement, mesh, world);
            int vertices = mesh.Vertices.Count;
            foreach (MefCollisionFace face in mesh.Faces)
            {
                if ((uint)face.I0 >= (uint)vertices
                    || (uint)face.I1 >= (uint)vertices
                    || (uint)face.I2 >= (uint)vertices)
                {
                    continue;
                }

                field.Splat(world[face.I0], world[face.I1], world[face.I2]);
            }
        }

        return field;
    }

    /// <summary>The height of the highest solid surface over a square of ground, in world metres.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y, in metres.</param>
    /// <returns>The surface height, or <see cref="OpenSky"/> where nothing stands.</returns>
    public float SurfaceOver(double x, double y)
    {
        if (_height.Length == 0)
        {
            return OpenSky;
        }

        int cellX = (int)Math.Floor((x - OriginX) / CellMetres);
        int cellY = (int)Math.Floor((y - OriginY) / CellMetres);
        if ((uint)cellX >= (uint)Width || (uint)cellY >= (uint)Depth)
        {
            return OpenSky;
        }

        return _height[(cellY * Width) + cellX];
    }

    /// <summary>Reports whether something solid stands over a point, near enough to be its roof.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y, in metres.</param>
    /// <param name="z">The world Z, in metres.</param>
    /// <param name="headroomMetres">How far above the point the roof must stand to count.</param>
    /// <param name="reachMetres">How far above the point a roof may be and still be its roof.</param>
    /// <returns><see langword="true"/> when rain falling straight down would not reach the point.</returns>
    public bool IsSheltered(
        double x,
        double y,
        double z,
        double headroomMetres = SurfaceHeadroomMetres,
        double reachMetres = MaxOverheadMetres)
    {
        float over = SurfaceOver(x, y);
        return over > z + headroomMetres && over < z + reachMetres;
    }

    /// <summary>Reports whether a camera at this point has a roof over it.</summary>
    /// <param name="eye">Where the camera stands, in world metres.</param>
    /// <returns><see langword="true"/> when the camera is under cover.</returns>
    /// <remarks>
    /// A five-tap cross a metre wide rather than the one cell the eye stands in, and a majority of it
    /// rather than any of it. What this decides is whether a whole frame is drawn as an interior — the
    /// fog stands down, the rain stops — so it must not turn on for the width of a pipe or a railing
    /// passing overhead, and it must not flicker as the player walks the line of an eave.
    /// </remarks>
    public bool IsUnderCover(Vector3d eye)
    {
        if (_height.Length == 0 || !IsSheltered(eye.X, eye.Y, eye.Z, EyeHeadroomMetres))
        {
            return false;
        }

        const double arm = 1.0;
        int covered = 1;
        if (IsSheltered(eye.X - arm, eye.Y, eye.Z, EyeHeadroomMetres))
        {
            covered++;
        }

        if (IsSheltered(eye.X + arm, eye.Y, eye.Z, EyeHeadroomMetres))
        {
            covered++;
        }

        if (IsSheltered(eye.X, eye.Y - arm, eye.Z, EyeHeadroomMetres))
        {
            covered++;
        }

        if (IsSheltered(eye.X, eye.Y + arm, eye.Z, EyeHeadroomMetres))
        {
            covered++;
        }

        return covered >= 4;
    }

    /// <summary>Packs the raster into a texture the shading and reflection passes can fetch from.</summary>
    /// <param name="scale">Receives the metres one step of the packed height stands for.</param>
    /// <returns>The texels, row-major from the low corner; empty for an empty field.</returns>
    /// <remarks>
    /// Sixteen bits of height across the blue and green channels, because a level's Z can span
    /// kilometres and eight bits of that is a step taller than a person. Zero is the sentinel for open
    /// sky, so a real height is stored one step up from the bottom of the range and the reader takes it
    /// off again. Fetched with <c>texelFetch</c> rather than sampled: a filtered read would interpolate
    /// the low byte against the high one and invent heights halfway up the level.
    /// </remarks>
    public Bgra32[] Encode(out float scale)
    {
        scale = 1f;
        if (_height.Length == 0)
        {
            return [];
        }

        const int steps = 65534;
        float span = Math.Max(HighestZ - LowestZ, 1e-3f);
        scale = span / steps;

        Bgra32[] texels = new Bgra32[_height.Length];
        for (int i = 0; i < _height.Length; i++)
        {
            float here = _height[i];
            if (float.IsNegativeInfinity(here))
            {
                texels[i] = new Bgra32(0, 0, 0, 255);
                continue;
            }

            int packed = 1 + (int)Math.Clamp(MathF.Round((here - LowestZ) / scale), 0f, steps);
            texels[i] = new Bgra32((byte)(packed & 0xFF), (byte)((packed >> 8) & 0xFF), 0, 255);
        }

        return texels;
    }

    /// <summary>Reads a packed texel back, exactly as the shading pass's GLSL reads it.</summary>
    /// <param name="texel">The texel, as <see cref="Encode"/> wrote it.</param>
    /// <param name="lowestZ">The height the packed value counts up from; <see cref="LowestZ"/>.</param>
    /// <param name="scale">The metres one step stands for, as <see cref="Encode"/> reported.</param>
    /// <returns>The surface height in world metres, or <see cref="OpenSky"/> for an empty texel.</returns>
    /// <remarks>
    /// The mirror of <c>ShelterSurface</c> in <c>WetnessShaderSource</c>. Kept here so that the packing
    /// the game writes and the unpacking the shader performs can be held against each other in a test:
    /// they are two expressions of one format, in two languages, and nothing else would notice them
    /// drifting apart until a level came up with its puddles in the wrong places.
    /// </remarks>
    public static float Decode(Bgra32 texel, float lowestZ, float scale)
    {
        int packed = texel.B + (texel.G * 256);
        return packed == 0 ? OpenSky : lowestZ + ((packed - 1) * scale);
    }

    private static List<(WorldPlacement Placement, MefCollisionMesh Mesh)> CollectSolids(
        IEnumerable<WorldPlacement> placements)
    {
        Dictionary<MefModel, MefCollisionMesh?> cache = [];
        List<(WorldPlacement Placement, MefCollisionMesh Mesh)> solids = [];

        foreach (WorldPlacement placement in placements)
        {
            if (placement.Model.IsEmpty || IsCharacter(placement))
            {
                continue;
            }

            if (!cache.TryGetValue(placement.Model, out MefCollisionMesh? mesh))
            {
                mesh = MefCollisionMesh.TryRead(placement.Model.Container, meshIndex: 0, out MefCollisionMesh? read)
                    ? read
                    : null;
                cache[placement.Model] = mesh;
            }

            if (mesh is { Faces.Count: > 0, Vertices.Count: > 0 })
            {
                solids.Add((placement, mesh));
            }
        }

        return solids;
    }

    // A living body is not architecture: a soldier standing in the yard must not roof the ground he is
    // standing on. The same exclusion the collision index makes, and for the same reason.
    private static bool IsCharacter(WorldPlacement placement) =>
        placement.Owner.Type.Name is "HumanPlayer" or "HumanSoldier" or "HumanSoldierFemale";

    // The placement's collision vertices in world metres. The rotation is the transposed stored basis --
    // the column-vector product the collision index builds its own triangles with -- and the engine-unit
    // offset is divided to metres before the placement's own metre position is added.
    private static Vector3d[] Transform(WorldPlacement placement, MefCollisionMesh mesh, Vector3d[] scratch)
    {
        int count = mesh.Vertices.Count;
        if (scratch.Length < count)
        {
            scratch = new Vector3d[Math.Max(count, scratch.Length * 2)];
        }

        Matrix3f rotation = placement.Orientation.Transposed();
        for (int i = 0; i < count; i++)
        {
            Vector3f placed = rotation.Transform(mesh.Vertices[i]);
            scratch[i] = new Vector3d(
                (placed.X / WorldScene.UnitsPerMetre) + placement.Position.X,
                (placed.Y / WorldScene.UnitsPerMetre) + placement.Position.Y,
                (placed.Z / WorldScene.UnitsPerMetre) + placement.Position.Z);
        }

        return scratch;
    }

    // One triangle into the raster, keeping the higher of what is already there and what this covers.
    // Cells are claimed by their centres, which is what makes a wall free: projected onto the ground a
    // vertical face is a line, and a line holds no cell centres worth speaking of.
    private void Splat(Vector3d a, Vector3d b, Vector3d c)
    {
        double area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        if (Math.Abs(area) < 1e-9)
        {
            return;
        }

        double minX = Math.Min(a.X, Math.Min(b.X, c.X));
        double maxX = Math.Max(a.X, Math.Max(b.X, c.X));
        double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
        double maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));

        int x0 = Math.Max((int)Math.Floor((minX - OriginX) / CellMetres), 0);
        int x1 = Math.Min((int)Math.Floor((maxX - OriginX) / CellMetres), Width - 1);
        int y0 = Math.Max((int)Math.Floor((minY - OriginY) / CellMetres), 0);
        int y1 = Math.Min((int)Math.Floor((maxY - OriginY) / CellMetres), Depth - 1);

        double inverseArea = 1.0 / area;

        for (int y = y0; y <= y1; y++)
        {
            double sampleY = OriginY + ((y + 0.5) * CellMetres);
            for (int x = x0; x <= x1; x++)
            {
                double sampleX = OriginX + ((x + 0.5) * CellMetres);

                double w0 = (((b.X - sampleX) * (c.Y - sampleY)) - ((b.Y - sampleY) * (c.X - sampleX))) * inverseArea;
                double w1 = (((c.X - sampleX) * (a.Y - sampleY)) - ((c.Y - sampleY) * (a.X - sampleX))) * inverseArea;
                double w2 = 1.0 - w0 - w1;
                if (w0 < 0.0 || w1 < 0.0 || w2 < 0.0)
                {
                    continue;
                }

                Raise((y * Width) + x, (float)((w0 * a.Z) + (w1 * b.Z) + (w2 * c.Z)));
            }
        }

        // The corners as well, for cover too finely divided to catch a cell centre: a roof assembled from
        // pieces smaller than a cell would otherwise leave the odd hole in it, and a hole in a roof is a
        // column of rain in the middle of a room. Only where the face lies flat enough to be part of a
        // roof -- stamping a wall's corners would put its full height into the cell of the floor beside
        // it, which is the one thing the centre rule exists to prevent.
        Vector3d edge0 = new(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        Vector3d edge1 = new(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        double normalX = (edge0.Y * edge1.Z) - (edge0.Z * edge1.Y);
        double normalY = (edge0.Z * edge1.X) - (edge0.X * edge1.Z);
        double normalZ = (edge0.X * edge1.Y) - (edge0.Y * edge1.X);
        double length = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (length <= 0.0 || Math.Abs(normalZ) / length < 0.7)
        {
            return;
        }

        Stamp(a);
        Stamp(b);
        Stamp(c);
    }

    private void Stamp(Vector3d point)
    {
        int x = (int)Math.Floor((point.X - OriginX) / CellMetres);
        int y = (int)Math.Floor((point.Y - OriginY) / CellMetres);
        if ((uint)x < (uint)Width && (uint)y < (uint)Depth)
        {
            Raise((y * Width) + x, (float)point.Z);
        }
    }

    private void Raise(int index, float z)
    {
        if (_height[index] < z)
        {
            _height[index] = z;
        }
    }
}
