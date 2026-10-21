using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The world span a terrain tile's samples cover, on the ground plane.</summary>
/// <param name="Left">The west edge, in world units.</param>
/// <param name="Top">The north edge.</param>
/// <param name="Right">The east edge.</param>
/// <param name="Bottom">The south edge.</param>
/// <remarks>
/// The span runs from the first sample's own position to half a texel past the last, which is where
/// <c>Terrain_UpdateSubmaps</c> (<c>igi2ed 0x62C770</c>) puts it: it tests against
/// <c>tPos +/- vWorldWidth * 2048 + vTexelWidth / 2</c>, the same half-texel shift that puts a sample at
/// its texel's centre rather than on the grid corner.
/// </remarks>
public readonly record struct Igi2TerrainExtent(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The slack the original allows a cell before it counts as outside -- a millimetre.</summary>
    /// <remarks>
    /// <c>4.096</c> world units, written into both comparisons at <c>0x62C770</c> so that a submap laid
    /// exactly on the base grid by <c>TerrainMap_Align</c> is not thrown out by the last bit of a float.
    /// </remarks>
    public const double Tolerance = 4.096;

    /// <summary>Gets whether a cell lies inside this span, to within <see cref="Tolerance"/>.</summary>
    /// <param name="left">The cell's west edge, in world units.</param>
    /// <param name="top">Its north edge.</param>
    /// <param name="right">Its east edge.</param>
    /// <param name="bottom">Its south edge.</param>
    /// <returns><see langword="true"/> when the whole cell is inside.</returns>
    public bool Contains(double left, double top, double right, double bottom) =>
        left >= Left - Tolerance
        && right <= Right + Tolerance
        && top >= Top - Tolerance
        && bottom <= Bottom + Tolerance;
}

/// <summary>One terrain tile of an I.G.I. 2 level, placed in the world.</summary>
/// <param name="Id">The tile id, which is what names its three files.</param>
/// <param name="Centre">Where the tile's middle sits, in world units.</param>
/// <param name="TexelWidth">The distance between samples along X, in world units.</param>
/// <param name="TexelHeight">The distance between samples along Y, in world units.</param>
/// <param name="Width">How many samples the tile is across.</param>
/// <param name="Height">How many samples the tile is down.</param>
/// <param name="Heights">The height map, whose samples are stored relative to <paramref name="BaseHeight"/>.</param>
/// <param name="BaseHeight">The Z the samples are measured from — the parent <c>Terrain</c>'s own, in world units.</param>
/// <param name="Materials">The material map, or null when the tile ships none.</param>
/// <param name="Lightmap">The baked lightmap, or null when the tile ships none.</param>
/// <param name="Submaps">The finer tiles that draw this one's ground in its place.</param>
public readonly record struct Igi2TerrainTile(
    int Id,
    Vector3d Centre,
    double TexelWidth,
    double TexelHeight,
    int Width,
    int Height,
    Igi2TerrainMap Heights,
    double BaseHeight,
    Igi2TerrainMap? Materials,
    Igi2TerrainMap? Lightmap,
    IReadOnlyList<Igi2TerrainExtent> Submaps)
{
    /// <summary>Gets the tile's span along X in metres as the engine holds it, <c>vWorldWidth</c>.</summary>
    /// <remarks>
    /// <c>TerrainMap_UpdateInternalDataHandler</c> (<c>igi2 0x575610</c>) copies it from the authored
    /// <c>World width</c> when the tile loads. The float itself is kept, not the derived spacing, because the
    /// ground query (<see cref="Igi2TerrainVertex"/>) recomputes the spacing from it at the client's precision.
    /// </remarks>
    public float WorldWidth { get; init; }

    /// <summary>Gets the tile's span along Y in metres as the engine holds it, <c>vWorldHeight</c>.</summary>
    public float WorldHeight { get; init; }

    /// <summary>Gets the position of the <c>Terrain</c> the tile belongs to, which the ground query works relative to.</summary>
    public Vector3d TerrainOrigin { get; init; }

    /// <summary>Gets the world height of one sample.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The height, in world units.</returns>
    /// <remarks>
    /// <para>
    /// <b>A sample is an offset from the level's terrain, not a world height.</b>
    /// <c>Terrain_GetZFromWorldCoordinates</c> (<c>igi2ed 0x632FF0</c>) interpolates the four samples
    /// around a world position and adds <c>ptTerrain-&gt;tPos.z</c> to the result — the <em>parent</em>
    /// <c>Terrain</c> task's Z, not the tile's. The X and Y halves of the same function convert the
    /// query and the tile's own position into terrain space by subtracting the same origin from both,
    /// so those two cancel and only Z is left as a real offset.
    /// </para>
    /// <para>
    /// Twenty-three of the twenty-four shipped levels author that Z as zero, which is why every path
    /// through here read the same either way. <c>location1/level3</c> authors it as 36,864,000 — nine
    /// kilometres — and there the difference is the whole level: the ground draws nine kilometres below
    /// the buildings standing on it, which past a four-kilometre far plane is not a misplaced terrain
    /// but a black screen.
    /// </para>
    /// </remarks>
    public double HeightAt(int x, int y) => BaseHeight + Heights.GetHeight(x, y);

    /// <summary>Gets the world position of one sample.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The position, with the sample's world height as Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside the tile.</exception>
    /// <remarks>
    /// <b>Samples sit at texel centres, not at grid corners.</b> The tile spans
    /// <c>centre ± worldExtent / 2</c> and is divided into <see cref="Width"/> texels, so sample
    /// <c>i</c> is at <c>left + (i + 0.5) * texelWidth</c> — which puts the first sample half a texel
    /// inside the edge and the last one half a texel short of the far edge.
    /// <para>
    /// That is the original's own arithmetic, read from both directions.
    /// <c>TerrainMap_ResampleAndAlign</c> (<c>igi2.exe 0x571780</c>) derives
    /// <c>vBaseTexelWidth = vWorldWidth * 4096 / nWidth</c> and snaps against
    /// <c>tPos - (vWorldWidth * 4096 + vBaseTexelWidth) / 2</c>, half a texel before the left edge;
    /// <c>Terrain_SampleMaterial</c> (<c>0x56EDC0</c>) inverts it as
    /// <c>(x - centre + half) / texelWidth</c> and rejects anything within half a texel of either edge.
    /// </para>
    /// <para>
    /// Dividing by <see cref="Width"/> rather than <c>Width - 1</c> is the part worth stating: a grid
    /// laid corner to corner would be one texel too wide and half a texel out of place, which reads as
    /// a terrain that <em>almost</em> lines up with the objects standing on it.
    /// </para>
    /// </remarks>
    public Vector3d PositionOf(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return new Vector3d(SampleX(x), SampleY(y), HeightAt(x, y));
    }

    /// <summary>Gets the world X of a sample column, which may be one past the last.</summary>
    /// <param name="x">The column.</param>
    /// <returns>The coordinate, in world units.</returns>
    /// <remarks>See <see cref="PositionOf"/> for why the half texel is there.</remarks>
    public double SampleX(int x) => Centre.X - (Width * TexelWidth * 0.5) + ((x + 0.5) * TexelWidth);

    /// <summary>Gets the world Y of a sample row, which may be one past the last.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The coordinate, in world units.</returns>
    /// <remarks>See <see cref="PositionOf"/> for why the half texel is there.</remarks>
    public double SampleY(int y) => Centre.Y - (Height * TexelHeight * 0.5) + ((y + 0.5) * TexelHeight);

    /// <summary>Gets the fractional sample column a world X falls on.</summary>
    /// <param name="x">The world X, in world units.</param>
    /// <returns>The column, which may be outside the tile.</returns>
    /// <remarks>
    /// The inverse of <see cref="SampleX"/>, and the original's own:
    /// <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>) forms
    /// <c>(x − centre + half) / texelWidth − 0.5</c>, the same half texel that puts a sample at its
    /// texel's centre rather than on the grid corner.
    /// </remarks>
    public double ColumnAt(double x) => ((x - Centre.X + (Width * TexelWidth * 0.5)) / TexelWidth) - 0.5;

    /// <summary>Gets the fractional sample row a world Y falls on.</summary>
    /// <param name="y">The world Y, in world units.</param>
    /// <returns>The row, which may be outside the tile.</returns>
    /// <remarks>See <see cref="ColumnAt"/>.</remarks>
    public double RowAt(double y) => ((y - Centre.Y + (Height * TexelHeight * 0.5)) / TexelHeight) - 0.5;

    /// <summary>Gets whether a world point is inside this tile, half a texel in from each edge.</summary>
    /// <param name="x">The world X, in world units.</param>
    /// <param name="y">The world Y, in world units.</param>
    /// <returns><see langword="true"/> when this tile answers for the point.</returns>
    /// <remarks>
    /// <para>
    /// The test both of the sequel's height queries walk the tile list with —
    /// <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>) and
    /// <c>Terrain_GetZFromWorldCoordinates</c> (<c>igi2ed 0x632FF0</c>) — and it is strict on all four
    /// sides and shrunk by half a texel, because a sample sits at its texel's centre and the outer half
    /// texel has no second sample to interpolate against.
    /// </para>
    /// <para>
    /// Both walk the list backwards and take the <em>first</em> tile that contains the point, which is
    /// what makes a later-declared finer tile win over the coarse one it sits inside — the same priority
    /// <see cref="IsSuperseded"/> draws with.
    /// </para>
    /// </remarks>
    public bool CoversPoint(double x, double y)
    {
        double halfWidth = Width * TexelWidth * 0.5;
        double halfHeight = Height * TexelHeight * 0.5;

        return Centre.X - halfWidth + (TexelWidth * 0.5) < x
            && x < Centre.X + halfWidth - (TexelWidth * 0.5)
            && Centre.Y - halfHeight + (TexelHeight * 0.5) < y
            && y < Centre.Y + halfHeight - (TexelHeight * 0.5);
    }

    /// <summary>Gets the span this tile's samples cover.</summary>
    public Igi2TerrainExtent Extent => new(SampleX(0), SampleY(0), SampleX(Width), SampleY(Height));

    /// <summary>Gets whether a finer tile draws this square's ground in this tile's place.</summary>
    /// <param name="x">The square's first column.</param>
    /// <param name="y">Its first row.</param>
    /// <param name="size">How many samples across it is.</param>
    /// <returns><see langword="true"/> when the square must not be drawn from this tile.</returns>
    /// <remarks>
    /// <para>
    /// <b>I.G.I. 2's tiles nest.</b> A level lays a coarse map over the whole of itself and then finer
    /// maps over the parts that need them -- <c>location1/level1</c>'s second tile is 512 metres of
    /// four-metre texels sitting inside two kilometres of thirty-two-metre ones -- and the ground under
    /// the finer map is drawn from the finer map alone.
    /// </para>
    /// <para>
    /// <c>Terrain_UpdateSubmaps</c> (<c>igi2ed 0x62C770</c>) is where that is decided, once per load: for
    /// every cell of every mip of every map it walks the maps declared <em>after</em> it, keeps those
    /// whose texels are no larger than its own, and records the last one that contains the cell whole.
    /// <c>TerrainRender_Draw</c> reads that index at <c>0x668CBF</c> and changes which map the square is
    /// reading rather than drawing the coarse one.
    /// </para>
    /// <para>
    /// Drawing both is not a double cost but a visible fault: the passes blend <c>SRCALPHA</c> onto
    /// <c>ONE</c>, so a coarse tile left under a fine one adds a second ground to the frame -- pale
    /// sheets that cut through the hillsides wherever the two tessellations disagree.
    /// </para>
    /// </remarks>
    public bool IsSuperseded(int x, int y, int size)
    {
        if (Submaps is not { Count: > 0 })
        {
            return false;
        }

        // ONE SAMPLE OF OVERLAP ON EVERY SIDE, and the ground depends on it.
        //
        // A finer tile is a SEPARATE height map of the same ground, not a subdivision of this one, so
        // the two disagree about where the surface is -- by however much 24 metres between samples lost
        // that 3 metres keeps. Handing over exactly at the finer tile's edge makes that disagreement a
        // seam, and a seam between two surfaces that do not line up is a hole: at mission 11's opening
        // the join runs across the far hillside and opens two wedges of sky into it, which close as the
        // camera comes nearer -- because the two sides converge on the same detail -- and open again as
        // it goes away. That is what was reported.
        //
        // So this tile keeps drawing one sample INTO the finer tile's ground, and the surfaces overlap
        // along the join instead of meeting along it. What that costs is the second ground the remark
        // above warns of, in a ring one square wide at the edge of a submap and nowhere else.
        double left = SampleX(x - 1);
        double top = SampleY(y - 1);
        double right = SampleX(x + size + 1);
        double bottom = SampleY(y + size + 1);

        for (int i = 0; i < Submaps.Count; i++)
        {
            Igi2TerrainExtent submap = Submaps[i];
            if (submap.Contains(left, top, right, bottom))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the planar projection that lays this tile's lightmap over the ground.</summary>
    /// <returns>A scale and an offset per axis: <c>u = worldX * ScaleX + OffsetX</c>.</returns>
    /// <remarks>
    /// <para>
    /// <b>The lightmap is a TEXTURE over the ground, not a colour on its vertices.</b>
    /// <c>TerrainRender_DrawRenderChunk_</c> (<c>igi2ed 0x667400</c>) draws the whole ground a second
    /// time for it: no material stream, the tile's <c>TexInfo</c> from
    /// <c>TerrainContext_t::aptLightmap[i]</c> on stage 0, <c>TerrainRender_nLightmapVertexShader</c>
    /// bound, and vertex-shader constants 11 and 12 loaded from <c>::atLightmapOffset[i]</c> and
    /// <c>::atLightmapScale[i]</c> — an affine projection of world XY, which is the pair this returns.
    /// </para>
    /// <para>
    /// The projection is the tile's own extent, half a texel in on each side, because a lightmap texel
    /// answers for the same square a height sample does — the shipped maps are the same dimensions as
    /// their tile's height map, 128 by 128 beside 128 by 128.
    /// </para>
    /// </remarks>
    public (float ScaleX, float ScaleY, float OffsetX, float OffsetY) LightmapProjection()
    {
        double spanX = Width * TexelWidth;
        double spanY = Height * TexelHeight;
        return (
            (float)(1d / spanX),
            (float)(1d / spanY),
            (float)(0.5d - (Centre.X / spanX)),
            (float)(0.5d - (Centre.Y / spanY)));
    }

    /// <summary>Gets the surface normal at one sample, from the slope of the height map around it.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The unit normal, Z up.</returns>
    /// <remarks>
    /// <para>
    /// <b>The original has no terrain normals to reproduce.</b> Its ground is lit entirely by the
    /// per-sample lightmap the tile ships, carried in the vertex colour, and nothing in
    /// <c>TerrainRender_CalculateRenderVertex</c> (<c>igi2ed 0x66A740</c>) writes a normal at all. The
    /// remaster's deferred path needs one anyway: the sun, the ambient and the screen-space passes all
    /// ask the G-buffer which way a surface faces, and a ground that answers "up" everywhere is lit as
    /// a flat plate — which is what a hillside built out of thirty-two-metre texels looked like.
    /// </para>
    /// <para>
    /// Central differences over the height map, which is the slope the ground actually has rather than
    /// the slope of whichever triangles the quadtree happened to emit. Taking it from the samples means
    /// a vertex keeps the same normal at every level of detail, so the ground does not re-light as it
    /// subdivides. Heights and texel sizes are both in world units, so the ratio is dimensionless.
    /// </para>
    /// </remarks>
    public Vector3f NormalAt(int x, int y)
    {
        int left = Math.Max(0, Math.Min(Width - 1, x - 1));
        int right = Math.Max(0, Math.Min(Width - 1, x + 1));
        int up = Math.Max(0, Math.Min(Height - 1, y - 1));
        int down = Math.Max(0, Math.Min(Height - 1, y + 1));

        double spanX = (right - left) * TexelWidth;
        double spanY = (down - up) * TexelHeight;
        double slopeX = spanX > 0d ? (HeightAt(right, y) - HeightAt(left, y)) / spanX : 0d;
        double slopeY = spanY > 0d ? (HeightAt(x, down) - HeightAt(x, up)) / spanY : 0d;

        Vector3f normal = new((float)-slopeX, (float)-slopeY, 1f);
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + 1f);
        return normal / length;
    }

    /// <summary>Gets the material id painted on one sample, or zero when the tile ships no material map.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The material id, which indexes a level's declared materials by their own <c>ID</c>.</returns>
    public byte MaterialAt(int x, int y) => Materials?.GetMaterial(x, y) ?? 0;

    /// <summary>Gets the baked light on one sample, or opaque white when the tile ships no lightmap.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// White rather than black for the missing case, which is the original's own fallback: it allocates
    /// the buffer and <c>memset</c>s it to <c>0xFF</c>, so an unlit tile draws at full brightness rather
    /// than disappearing.
    /// </remarks>
    public Bgra32 LightAt(int x, int y) => Lightmap?.GetLight(x, y) ?? new Bgra32(255, 255, 255, 255);
}

/// <summary>
/// An I.G.I. 2 level's ground: the tiles it declares, loaded and placed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The level is the authority, not the archive.</b> A level's <c>TerrainMap</c> tasks name the tiles
/// it has, and the ids are sparse — <c>location2/LEVEL6</c> declares only tile 2. Enumerating
/// <c>heightmaps.res</c> instead would load four tiles that belong to no level at all, because
/// <c>location3/LEVEL4</c> ships two while declaring none.
/// </para>
/// <para>
/// A tile missing any of its three files is complete rather than broken, which is the original's own
/// tolerance: <c>TerrainMap_Alloc</c> substitutes a zeroed material map and a white lightmap and carries
/// on. The height map is the one that cannot be substituted usefully, so a tile without it is skipped.
/// </para>
/// </remarks>
public sealed class Igi2TerrainSurface
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.terrain";

    /// <summary>The archive a level keeps its three terrain maps per tile in.</summary>
    /// <remarks>
    /// <c>TerrainMap_GetDirectory</c> (<c>igi2ed 0x62FE50</c>) builds <c>MISSION:heightmaps</c> and
    /// <c>TerrainMap_Alloc</c> appends the file name; on a retail install those files are not loose, and
    /// arrive through this archive mounted at the same place.
    /// </remarks>
    public const string ArchiveResourceName = "MISSION:heightmaps/heightmaps.res";

    private Igi2TerrainSurface(
        IReadOnlyList<Igi2TerrainTile> tiles,
        IReadOnlyDictionary<int, TerrainMaterialTask> materials,
        Igi2TerrainDiscard[] discards,
        Igi2DiscardBox[] discardBoxes,
        TerrainTask? terrain)
    {
        Tiles = tiles;
        Materials = materials;
        Discards = discards;
        DiscardBoxes = discardBoxes;
        Terrain = terrain;
    }

    /// <summary>Gets the level's terrain, the task its ground is placed by; <see langword="null"/> when it has none.</summary>
    public TerrainTask? Terrain { get; }

    /// <summary>Gets the position the ground is measured from: the terrain's.</summary>
    public Vector3d Origin => Terrain?.Position ?? default;

    /// <summary>Gets the discard boxes as the ground query tests them, relative to <see cref="Origin"/>.</summary>
    public Igi2DiscardBox[] DiscardBoxes { get; }

    /// <summary>Gets the level's tiles, in the order it declared them.</summary>
    public IReadOnlyList<Igi2TerrainTile> Tiles { get; }

    /// <summary>Gets the level's terrain materials, keyed by the id a material map's samples carry.</summary>
    /// <remarks>
    /// Keyed by <c>ID</c> rather than by position, because that is what a <c>.tmm</c> sample holds: the
    /// table is sparse and a level may declare its materials in any order.
    /// </remarks>
    public IReadOnlyDictionary<int, TerrainMaterialTask> Materials { get; }

    /// <summary>Gets the boxes the level's <see cref="DiscardTask"/>s cut out of the ground.</summary>
    /// <remarks>
    /// Empty for twenty of the twenty-four shipped levels. See <see cref="DiscardTask"/> for what a box
    /// means and <see cref="Igi2TerrainDiscard"/> for why it has no Z.
    /// </remarks>
    public Igi2TerrainDiscard[] Discards { get; }

    /// <summary>Gets a value indicating whether the level has any ground to draw.</summary>
    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>Gets whether a world position stands over ground a <see cref="DiscardTask"/> removed.</summary>
    /// <param name="x">The world X, in world units.</param>
    /// <param name="y">The world Y.</param>
    /// <returns><see langword="true"/> when the level cuts its ground away there.</returns>
    public bool IsDiscarded(double x, double y) => Igi2TerrainDiscard.AnyContains(Discards, x, y);

    /// <summary>Gets the ground's height under a point as the water reads its depth from it.</summary>
    /// <param name="x">The world X, in world units.</param>
    /// <param name="y">The world Y.</param>
    /// <returns>The height in world units, or <see langword="null"/> where the level has no ground at all.</returns>
    /// <remarks>
    /// <c>Terrain_GetZFromWorldCoordinates</c>, which is not the ground a body stands on: it walks the tiles from
    /// the last down to the second for one that covers the point and otherwise takes the first, clamps the point
    /// onto that tile's samples rather than refusing it, and knows nothing of the discard boxes.
    /// </remarks>
    public double? WaterGroundAt(double x, double y)
    {
        if (Tiles.Count == 0)
        {
            return null;
        }

        int index = Tiles.Count - 1;
        while (index > 0 && !Tiles[index].CoversPoint(x, y))
        {
            index--;
        }

        Igi2TerrainTile tile = Tiles[index];
        double column = Math.Clamp(tile.ColumnAt(x), 0.0, tile.Width - 1);
        double row = Math.Clamp(tile.RowAt(y), 0.0, tile.Height - 1);
        int left = (int)column, top = (int)row;
        int right = Math.Min(left + 1, tile.Width - 1), bottom = Math.Min(top + 1, tile.Height - 1);
        double across = column - left, down = row - top;
        double east = tile.Heights.GetHeight(right, top), south = tile.Heights.GetHeight(left, bottom);

        // The cell's two triangles, split on the diagonal from its east corner to its south one.
        if (1.0 - across < down)
        {
            double far = tile.Heights.GetHeight(right, bottom);
            return far + ((east - far) * (1.0 - down)) + ((south - far) * (1.0 - across)) + tile.BaseHeight;
        }

        double near = tile.Heights.GetHeight(left, top);
        return ((south - near) * down) + ((east - near) * across) + tile.BaseHeight + near;
    }

    /// <summary>Loads the ground a level declares, opening its archive through the registry.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="resources">The registry the level's own files are read from.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The surface, which may be empty.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The one place that knows where the maps live, because two callers want them: the presenter that
    /// draws the ground and the walk that hands the player something to stand on.
    /// </remarks>
    public static Igi2TerrainSurface Build(GameTask levelRoot, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        IresArchive? heightmaps = resources.TryLoad(ArchiveResourceName, out ReadOnlyMemory<byte> bytes)
            ? IresArchive.Read(bytes)
            : null;

        return Build(levelRoot, heightmaps, logger);
    }

    /// <summary>Loads the ground a level declares.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="heightmaps">The level's <c>heightmaps.res</c>, or null when it ships none.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The surface, which may be empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static Igi2TerrainSurface Build(GameTask levelRoot, IresArchive? heightmaps, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        List<Igi2TerrainTile> tiles = [];
        Dictionary<int, TerrainMaterialTask> materials = [];
        List<Igi2TerrainDiscard> discards = [];
        List<DiscardTask> discardTasks = [];
        TerrainTask? terrain = null;

        // heightmaps.res keys its entries by bare file name -- `heightmap000.thm`, not the alias-
        // qualified path the task builds. The original never notices: TerrainMap_Alloc sprintfs a name
        // and hands it to the file layer, which resolves through the mounted archive either way.
        Dictionary<string, IresEntry> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in heightmaps?.Entries ?? [])
        {
            byName[BareName(entry.Name)] = entry;
        }

        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is TerrainTask level)
            {
                terrain ??= level;
            }
            else if (task is TerrainMaterialTask material)
            {
                // The first one with an id wins: Terrain_GetNextZHandler (igi2 0x572E80) walks the
                // terrain's TerrainMaterial children in order and stops at the first match.
                materials.TryAdd(material.MaterialId, material);
            }
            else if (task is TerrainMapTask map && TryLoad(map, byName, log, out Igi2TerrainTile tile))
            {
                tiles.Add(tile);
            }
            else if (task is DiscardTask discard)
            {
                // A DiscardTask is a sibling of the terrain rather than a child of it: the original
                // keeps them in DiscardTask_ptQTaskList, a flat list every terrain vertex is tested
                // against, so where in the tree one sits does not matter. The whole walk is the list.
                discards.Add(discard.Region);
                discardTasks.Add(discard);
            }
        }

        BindSubmaps(tiles);

        log.Information(
            LogCategory,
            $"Terrain: {tiles.Count} tile(s), {materials.Count} material(s), {discards.Count} discard(s).");
        Vector3d origin = terrain?.Position ?? default;
        return new Igi2TerrainSurface(
            tiles,
            materials,
            [.. discards],
            [.. discardTasks.Select(d => Igi2DiscardBox.From(d, origin))],
            terrain);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 terrain, {Tiles.Count} tile(s), {Materials.Count} material(s), {Discards.Length} discard(s)";

    // Which tiles supersede which, as Terrain_UpdateSubmaps decides it: a map is superseded by the maps
    // declared after it whose texels are no larger than its own. Declaration order is the whole of the
    // priority rule -- Terrain_GetZFromWorldCoordinates searches the same list backwards and takes the
    // first containing map, which makes the last declared the finest and map 0 the fallback.
    private static void BindSubmaps(List<Igi2TerrainTile> tiles)
    {
        for (int i = 0; i < tiles.Count; i++)
        {
            List<Igi2TerrainExtent> superseding = [];
            for (int j = i + 1; j < tiles.Count; j++)
            {
                if (tiles[j].TexelWidth <= tiles[i].TexelWidth)
                {
                    superseding.Add(tiles[j].Extent);
                }
            }

            tiles[i] = tiles[i] with { Submaps = superseding };
        }
    }

    private static bool TryLoad(
        TerrainMapTask map,
        IReadOnlyDictionary<string, IresEntry> byName,
        IOpenIgiLogger log,
        out Igi2TerrainTile tile)
    {
        tile = default;
        if (!TryRead(byName, map.HeightMapResource, out Igi2TerrainMap? heights))
        {
            log.Warning(LogCategory, $"Terrain tile {map.MapId} has no height map; it is not drawn.");
            return false;
        }

        // The world extent is authored in metres and the engine multiplies by 4096 before it divides by
        // the sample count -- so the spacing is in world units, and the tile is nWidth texels wide
        // rather than nWidth - 1 spans wide.
        double texelWidth = map.WorldWidth * WorldScene.UnitsPerMetre / heights.Width;
        double texelHeight = map.WorldHeight * WorldScene.UnitsPerMetre / heights.Height;

        _ = TryRead(byName, map.MaterialMapResource, out Igi2TerrainMap? materialMap);
        _ = TryRead(byName, map.LightMapResource, out Igi2TerrainMap? lightMap);

        tile = new Igi2TerrainTile(
            map.MapId,
            map.Position,
            texelWidth,
            texelHeight,
            heights.Width,
            heights.Height,
            heights,
            BaseHeightOf(map),
            Matching(materialMap, heights) ? materialMap : null,
            Matching(lightMap, heights) ? lightMap : null,
            [])
        {
            WorldWidth = map.WorldWidth,
            WorldHeight = map.WorldHeight,
            TerrainOrigin = TerrainOf(map)?.Position ?? default,
        };

        return true;
    }

    // The Z a tile's samples are measured from, which Terrain_GetZFromWorldCoordinates takes from the
    // TerrainMap's parent rather than from the TerrainMap. A tile the level parented somewhere else is
    // measured from nothing, which is the same answer the original gives when no terrain is active.
    private static double BaseHeightOf(TerrainMapTask map) => TerrainOf(map)?.Position.Z ?? 0.0;

    // The Terrain a tile hangs under, QTask_GetFirstParentOfType's answer.
    private static TerrainTask? TerrainOf(TerrainMapTask map)
    {
        for (GameTask? parent = map.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TerrainTask terrain)
            {
                return terrain;
            }
        }

        return null;
    }

    // A map whose extent disagrees with the height map's is not usable as a per-sample channel. The
    // original never reaches this case because it validates each file against nEditWidth/nEditHeight as
    // it loads; this keeps the same guarantee for a caller that indexes all three together.
    private static bool Matching(Igi2TerrainMap? map, Igi2TerrainMap heights) =>
        map is not null && map.Width == heights.Width && map.Height == heights.Height;

    private static bool TryRead(
        IReadOnlyDictionary<string, IresEntry> byName,
        string resource,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Igi2TerrainMap? map)
    {
        map = null;
        if (!byName.TryGetValue(BareName(resource), out IresEntry? entry))
        {
            return false;
        }

        map = Igi2TerrainMap.Read(entry.Data);
        return true;
    }

    private static string BareName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }
}
