using System.Numerics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Presentation;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The two numbers a level authors to control how finely its ground is tessellated.</summary>
/// <param name="Detail">
/// <c>Terrain_t::vDetail</c>. The angular size an edge has to reach before it is worth splitting: an
/// edge splits when its length over its distance to the eye exceeds this, so a <em>larger</em> value
/// means a coarser ground.
/// </param>
/// <param name="Adaption">
/// <c>Terrain_t::vAdapt</c>. How much local roughness lowers that threshold — the edge test divides
/// <see cref="Detail"/> by <c>1 + 0.5 * roughness * Adaption</c>, so a bumpy patch is refined sooner
/// than a flat one at the same distance.
/// </param>
/// <remarks>
/// <para>
/// Both are read straight out of <c>TerrainRender_Draw</c> (<c>igi2ed 0x6691D3</c>), which is the only
/// place either is consulted:
/// </para>
/// <code>
/// fmul [ctx+1Ch]          ; roughness * vAdapt
/// fmul 0.5
/// fadd 1.0
/// fdivr [ctx+24h]         ; vDetail / (1 + 0.5 * roughness * vAdapt)
/// </code>
/// <para>
/// <c>ctx+0x1C</c> and <c>ctx+0x24</c> are <c>TerrainContext_t::vAdapt</c> and <c>::vDetail</c>, named
/// by the dedicated server's own type information, and the level copies them from the <c>Terrain</c>
/// task's <c>Adaption</c> and <c>Detail</c> parameters.
/// </para>
/// </remarks>
public readonly record struct Igi2TerrainLodSettings(float Detail, float Adaption)
{
    /// <summary>How fast a newly split vertex rises to its true height — <c>TerrainRender_vMorphSpeed</c>.</summary>
    /// <remarks>
    /// Written unconditionally at the top of every frame's <c>TerrainRender_Draw</c>
    /// (<c>igi2ed 0x66873B</c>: <c>mov TerrainRender_vMorphSpeed, 3F000000h</c>). It is a constant in
    /// the shipped build, not a tunable, so it is one here too.
    /// </remarks>
    public const float MorphSpeed = 0.5f;

    /// <summary>How fast a split vertex takes on its own material — <c>TerrainRender_vMaterialMorphSpeed</c>.</summary>
    /// <remarks>
    /// <c>igi2ed 0x668745</c>: <c>mov TerrainRender_vMaterialMorphSpeed, 40000000h</c>. Four times
    /// <see cref="MorphSpeed"/>: the ground's <em>surface</em> reaches its final blend when the geometry
    /// is only halfway up, which hides the material change inside the geometric one.
    /// </remarks>
    public const float MaterialMorphSpeed = 2f;

    /// <summary>The shortest edge the quadtree will ever split, squared, in world units.</summary>
    /// <remarks>
    /// <c>igi2ed 0x669291</c> compares the squared edge length against <c>0x4A800000</c>, which is
    /// exactly 2²² — so an edge shorter than 2,048 world units is never split no matter how close the
    /// eye gets. At 4,096 units to the metre that is a hard floor of half a metre on tessellation, and
    /// it is what stops a camera lying on the ground from subdividing to the sample grid and beyond.
    /// </remarks>
    public const float MinimumSplitEdgeLengthSquared = 4194304f;

    /// <summary>How much steeper than horizontal an edge may run before it stops being refined.</summary>
    /// <remarks>
    /// <c>igi2ed 0x66921F</c> and <c>0x669260</c> each compare <c>|dz|</c> against <c>10 * |dx|</c> and
    /// <c>10 * |dy|</c>; an edge that fails both — a near-vertical one, the wall of a cliff — is refused
    /// a split outright, before the distance test is even reached. Splitting such an edge buys nothing:
    /// its midpoint is already where the height field puts it.
    /// </remarks>
    public const float SteepEdgeRatio = 10f;

    /// <summary>The resolution a split fraction is stored at — <c>TerrainRenderEdge_t::nMorph</c>.</summary>
    /// <remarks>
    /// The original keeps the fraction as a <c>unsigned short</c>: <c>0x66930C</c> multiplies by
    /// 65,535 and <c>fistp</c>s it, and <c>0x669583</c> multiplies by <c>0x37800080</c> — which is
    /// exactly <c>1 / 65535</c> — on the way back out. Quantising here keeps a port's morph on the same
    /// 1/65,535 lattice rather than a slightly different curve.
    /// </remarks>
    public const int MorphResolution = 65535;

    /// <summary>Reads a level's controls off its <c>Terrain</c> task.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The settings, or <see langword="null"/> when the level declares no <c>Terrain</c> task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Two of the 24 shipped levels declare no <c>Terrain</c> at all, which is why this can answer
    /// nothing rather than a default: a level with no terrain task has no ground either, and inventing
    /// numbers for it would hide that.
    /// </remarks>
    public static Igi2TerrainLodSettings? Read(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        return Find(levelRoot) is { } terrain
            ? new Igi2TerrainLodSettings(terrain.Detail, terrain.Adaption)
            : null;
    }

    /// <summary>Gets the threshold an edge's length is compared against, for one sample of roughness.</summary>
    /// <param name="roughness">
    /// How far the height field departs from the straight edge at its midpoint, divided by the square's
    /// own width — a dimensionless slope.
    /// </param>
    /// <returns>The largest length-over-distance ratio an edge may have and still not be split.</returns>
    public readonly float ThresholdFor(float roughness) => Detail / (1f + (0.5f * roughness * Adaption));

    /// <summary>Gets how far a vertex has travelled from its parents' midpoint toward its own height.</summary>
    /// <param name="morph">The edge's split fraction, from zero to one.</param>
    /// <returns>The blend, from zero at the midpoint to one at the height field.</returns>
    /// <remarks>
    /// <c>TerrainRender_CalculateRenderVertex</c> (<c>igi2ed 0x66A76D</c>) writes
    /// <c>(1 - cos(m * pi)) * 0.5 * (z - mid) + mid</c> — a raised cosine, applied to Z alone. It is
    /// flat at both ends, which is the whole point: a vertex appears and disappears with zero velocity,
    /// so the ground does not visibly pop as a square crosses a detail boundary. A straight lerp would
    /// have the same endpoints and a visible kink at each.
    /// </remarks>
    public static float MorphFraction(float morph) =>
        morph >= 1f ? 1f : (float)((1.0 - Math.Cos(morph * Math.PI)) * 0.5);

    private static TerrainTask? Find(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is TerrainTask terrain)
            {
                return terrain;
            }

            if (Find(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

/// <summary>Where the ground is being looked at from, and the half-spaces it may be culled against.</summary>
/// <remarks>
/// <para>
/// <b>All four planes pass through the eye.</b> That is the shape
/// <c>TerrainRender_SetupClippingPlanes</c> (<c>igi2ed 0x6681A0</c>) builds when no clip window is
/// active: it zeroes each plane's point, fills in the four side normals from the projection's
/// <c>tan</c> half-angles, rotates them by the inverted view rotation and then subtracts
/// <c>tDisplacement</c> — which puts every point at the camera. A plane is stored as a point followed
/// by a normal, and <c>TerrainRender_IsSquareOutOfSight</c> (<c>0x66AE50</c>) treats
/// <c>dot(p - point, normal) &gt; 0</c> as outside.
/// </para>
/// <para>
/// The original's other branch, taken when a clip window is set, copies arbitrary points and normals
/// out of <c>Clipping_GetClipPlanes</c>. That path belongs to the editor's split views and is not
/// reproduced.
/// </para>
/// <para>
/// There is no near or far plane in the set — four side planes only. Distance is what the detail test
/// is for.
/// </para>
/// </remarks>
public readonly struct Igi2TerrainView
{
    private readonly Vector3f[]? _normals;

    /// <summary>Initializes a new instance of the <see cref="Igi2TerrainView"/> struct.</summary>
    /// <param name="eye">The eye position, in world units.</param>
    /// <param name="planeNormals">
    /// The outward normals of the half-spaces to cull against, all through <paramref name="eye"/>. Pass
    /// nothing to cull against nothing.
    /// </param>
    public Igi2TerrainView(Vector3d eye, ReadOnlySpan<Vector3f> planeNormals)
    {
        Eye = eye;
        _normals = planeNormals.IsEmpty ? null : planeNormals.ToArray();
    }

    /// <summary>Gets the eye position, in world units.</summary>
    public Vector3d Eye { get; }

    /// <summary>Gets the outward normals of the culling half-spaces, all through <see cref="Eye"/>.</summary>
    public ReadOnlySpan<Vector3f> PlaneNormals => _normals ?? [];

    /// <summary>Builds the view one frame is drawn from.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The view, with the frustum's four side planes.</returns>
    /// <remarks>
    /// <para>
    /// The four normals are the original's, in view space:
    /// <c>(-1, 0, -tanX)</c>, <c>(1, 0, -tanX)</c>, <c>(0, -1, -tanY)</c> and <c>(0, 1, -tanY)</c>,
    /// each normalised — left, right, bottom and top of a frustum whose apex is the eye. They are then
    /// rotated into world space by the camera's orientation, which is what
    /// <c>TerrainRender_SetupClippingPlanes</c> does with the inverted view rotation.
    /// </para>
    /// <para>
    /// One difference, stated rather than copied: the original normalises the <em>top</em> plane's Y
    /// component with <c>1 / sqrt(tanY * tanX + 1)</c> where every other component uses
    /// <c>1 / sqrt(tanY * tanY + 1)</c> (<c>0x6681A0</c>, the store to <c>flt_85233C8</c>). That mixes
    /// the two half-angles and leaves the normal non-unit whenever the aspect ratio is not one. It
    /// looks like a typo in the original and it only ever loosens the cull, so the port uses the
    /// consistent form.
    /// </para>
    /// <para>
    /// A camera with a degenerate orientation — a default-constructed one, as a test may pass — yields
    /// zero normals, which reject nothing. That is the safe direction to fail in.
    /// </para>
    /// </remarks>
    public static Igi2TerrainView FromFrame(in TerrainFrame frame)
    {
        float tanY = MathF.Tan(frame.VerticalFieldOfView * 0.5f);
        float tanX = tanY * frame.AspectRatio;

        Span<Vector3f> view =
        [
            Normalise(new Vector3f(-1f, 0f, -tanX)),
            Normalise(new Vector3f(1f, 0f, -tanX)),
            Normalise(new Vector3f(0f, -1f, -tanY)),
            Normalise(new Vector3f(0f, 1f, -tanY)),
        ];

        // Camera3D.Orientation's rows are the view axes in world space, so it maps world to view by
        // dotting rows; Transform() dots columns, which is exactly the view-to-world direction wanted
        // here. The same asymmetry Camera3D.CreateModelView documents, used the other way round.
        Span<Vector3f> world = stackalloc Vector3f[view.Length];
        for (int i = 0; i < view.Length; i++)
        {
            world[i] = frame.Camera.Orientation.Transform(view[i]);
        }

        return new Igi2TerrainView(frame.Camera.Position, world);
    }

    private static Vector3f Normalise(Vector3f value)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 0f ? value / length : value;
    }
}

/// <summary>The lowest and highest height over every power-of-two block of a tile.</summary>
/// <remarks>
/// <para>
/// <c>TerrainMap_AllocateMinMaxHeightMipMaps</c> (<c>igi2ed 0x6296B0</c>) allocates
/// <c>floor(log2(max(nWidth, nHeight))) + 1</c> levels and <c>8 * nWidth * nHeight</c> bytes for the
/// finest — two floats a sample, a minimum and a maximum — halving both extents each level up.
/// <c>TerrainRender_IsSquareOutOfSight</c> then reads the one cell that covers the square it is testing
/// and uses the pair as the box's Z extent.
/// </para>
/// <para>
/// Levels are indexed here by the base-two logarithm of the block they cover, which is the opposite
/// direction from the original's <c>nLevels - log2(size) - 1</c> but names the same cell. The original
/// also refuses the pyramid for its two finest levels and falls back to a per-level maximum deviation
/// (<c>TerrainMap_t::avDeltaZ</c>, reached at <c>0x66AF9E</c>); this uses the pyramid all the way down,
/// which is strictly tighter and costs nothing once the pyramid exists.
/// </para>
/// </remarks>
public sealed class Igi2TerrainHeightPyramid
{
    private readonly float[][] _minimum;
    private readonly float[][] _maximum;
    private readonly int[] _pitch;

    private Igi2TerrainHeightPyramid(float[][] minimum, float[][] maximum, int[] pitch)
    {
        _minimum = minimum;
        _maximum = maximum;
        _pitch = pitch;
    }

    /// <summary>Gets how many levels the pyramid has; level <c>L</c> covers blocks of <c>2^L</c> samples.</summary>
    public int LevelCount => _pitch.Length;

    /// <summary>Builds the pyramid for one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <returns>The pyramid.</returns>
    public static Igi2TerrainHeightPyramid Build(Igi2TerrainTile tile)
    {
        int extent = Math.Max(tile.Width, tile.Height);
        int levels = 1;
        while (1 << levels <= extent)
        {
            levels++;
        }

        float[][] minimum = new float[levels][];
        float[][] maximum = new float[levels][];
        int[] pitch = new int[levels];

        int width = tile.Width;
        int height = tile.Height;
        pitch[0] = width;
        minimum[0] = new float[width * height];
        maximum[0] = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float z = tile.Heights.GetHeight(x, y);
                minimum[0][(y * width) + x] = z;
                maximum[0][(y * width) + x] = z;
            }
        }

        for (int level = 1; level < levels; level++)
        {
            int coarseWidth = Math.Max(1, (width + 1) / 2);
            int coarseHeight = Math.Max(1, (height + 1) / 2);
            pitch[level] = coarseWidth;
            minimum[level] = new float[coarseWidth * coarseHeight];
            maximum[level] = new float[coarseWidth * coarseHeight];

            for (int y = 0; y < coarseHeight; y++)
            {
                for (int x = 0; x < coarseWidth; x++)
                {
                    float low = float.PositiveInfinity;
                    float high = float.NegativeInfinity;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = (x * 2) + dx;
                            int sy = (y * 2) + dy;
                            if (sx >= width || sy >= height)
                            {
                                continue;
                            }

                            low = MathF.Min(low, minimum[level - 1][(sy * pitch[level - 1]) + sx]);
                            high = MathF.Max(high, maximum[level - 1][(sy * pitch[level - 1]) + sx]);
                        }
                    }

                    minimum[level][(y * coarseWidth) + x] = low;
                    maximum[level][(y * coarseWidth) + x] = high;
                }
            }

            width = coarseWidth;
            height = coarseHeight;
        }

        return new Igi2TerrainHeightPyramid(minimum, maximum, pitch);
    }

    /// <summary>Gets the height range over one block.</summary>
    /// <param name="level">The level; the block is <c>2^level</c> samples across.</param>
    /// <param name="blockX">The block's column at that level.</param>
    /// <param name="blockY">The block's row at that level.</param>
    /// <param name="minimum">Receives the lowest height in the block.</param>
    /// <param name="maximum">Receives the highest.</param>
    /// <remarks>
    /// A block index past the tile is clamped rather than refused. The quadtree's root spans one sample
    /// more than the tile in each axis — see <see cref="Igi2TerrainLod"/> — so the last block along an
    /// edge is genuinely asked for and its neighbour's range is the closest true answer available.
    /// </remarks>
    public void GetBounds(int level, int blockX, int blockY, out float minimum, out float maximum)
    {
        int clamped = Math.Clamp(level, 0, _pitch.Length - 1);
        int pitch = _pitch[clamped];
        int rows = _minimum[clamped].Length / pitch;
        int index = (Math.Clamp(blockY, 0, rows - 1) * pitch) + Math.Clamp(blockX, 0, pitch - 1);

        minimum = _minimum[clamped][index];
        maximum = _maximum[clamped][index];
    }
}

/// <summary>One frame's worth of ground for a tile, and what the quadtree did to produce it.</summary>
/// <param name="Mesh">The geometry, in the same shape a full-detail build produces.</param>
/// <param name="SquaresDrawn">How many quadtree leaves were emitted.</param>
/// <param name="SquaresCulled">How many squares the clipping planes rejected, subtrees included.</param>
/// <param name="SquaresVisited">How many squares came off the traversal stack.</param>
/// <param name="CoveredArea">
/// The emitted leaves' areas added up, in samples squared. The traversal only ever divides a square
/// into four equal quadrants, so this equalling the root's own area is a proof that the leaves tile it
/// exactly — no gap, no overlap — which is the property a level-of-detail mesh most easily loses.
/// </param>
public readonly record struct Igi2TerrainTessellation(
    Igi2TerrainMesh Mesh,
    int SquaresDrawn,
    int SquaresCulled,
    int SquaresVisited,
    long CoveredArea);

/// <summary>
/// The quadtree I.G.I. 2 tessellates a terrain tile with: how finely a square is subdivided given where
/// it is being looked at from, and how a newly split vertex is faded in.
/// </summary>
/// <remarks>
/// <para>
/// Read from <c>TerrainRender_Draw</c> (<c>igi2ed 0x668680</c>), which is one 8,224-byte function with
/// an explicit stack — <c>TerrainRender_atDrawSquareStack</c>, 5,000
/// <c>TerrainRenderStackSquare_t</c> entries — rather than recursion. The struct names come from the
/// dedicated server's own type information, which is what makes the stack layout legible:
/// <c>{ nX, nY, nSize, iMipMap, iTerrainMap, nShift, aiVertex[4], aiEdge[4] }</c>.
/// </para>
/// <para>
/// <b>A square has five edges, not four.</b> The perimeter <c>(0,1) (1,2) (2,3) (3,0)</c> and the
/// diagonal <c>(1,3)</c>, whose midpoint is the square's centre — the corner order is
/// <c>(0,0) (1,0) (1,1) (0,1)</c>, set up at <c>0x66869D</c>, and the pairs are the table built at
/// <c>0x668E3D</c>. Each of the five is tested independently, and <b>if any one of them splits the
/// whole square is subdivided into four children</b> (<c>0x66937A</c>: the traversal draws a leaf only
/// when none of the five did).
/// </para>
/// <para>
/// <b>The split decision is cached on the shared edge, which is what makes the mesh crack-free.</b>
/// <c>TerrainRenderEdge_t::eSplitStatus</c> carries bit 0 for "split" and bit 1 for "did not"
/// (<c>0x669359</c>, <c>0x66935E</c>), and a square that finds an edge already decided takes the answer
/// rather than re-deriving it (<c>0x668F94</c>). So two squares sharing an edge always agree. An edge
/// that did not split still gets a split vertex — at morph zero, which the cosine blend below puts
/// exactly on the straight line between its parents, so the extra vertex is invisible and the seam is
/// closed by construction rather than by a stitching pass.
/// </para>
/// <para>
/// <b>The test itself</b> (<c>0x668FD3</c> to <c>0x669328</c>), for an edge from <c>a</c> to <c>b</c>
/// with the eye at the origin:
/// </para>
/// <list type="number">
/// <item><description>
/// find the point on the segment nearest the eye — <c>t = clamp(-dot(a, b - a) / |b - a|², 0, 1)</c> —
/// and take <c>d²</c>, its squared distance. Not the midpoint and not either end: an edge running away
/// from the viewer is judged by whichever part of it is closest.
/// </description></item>
/// <item><description>
/// measure the roughness the edge hides: <c>|mean(a.z, b.z) - height(midpoint)|</c> over the square's
/// width in world units, which is zero where the midpoint falls outside the height map.
/// </description></item>
/// <item><description>
/// refuse the split outright if the edge is steeper than
/// <see cref="Igi2TerrainLodSettings.SteepEdgeRatio"/> to one in <em>both</em> horizontal axes.
/// </description></item>
/// <item><description>
/// split when <c>|b - a|² &gt; D²d²</c> and <c>|b - a|² &gt;</c>
/// <see cref="Igi2TerrainLodSettings.MinimumSplitEdgeLengthSquared"/>, where
/// <c>D = vDetail / (1 + 0.5 * roughness * vAdapt)</c>.
/// </description></item>
/// <item><description>
/// record how far past the threshold it went, <c>((|b - a|² / D²d²) - 1) * vMorphSpeed</c> clamped to
/// one, as the edge's morph fraction.
/// </description></item>
/// </list>
/// <para>
/// <b>The root square is one sample larger than the tile.</b> It spans sample indices <c>0..n</c>
/// inclusive with <c>n = max(width, height)</c>, so it is a clean power of two and the quadtree halves
/// evenly. The row and column at <c>n</c> lie outside the height map and the original extrapolates them
/// linearly — <c>0x668B08</c> reads <c>3h[w-1,h-1] - h[w-2,h-1] - h[w-1,h-2]</c> at the far corner,
/// <c>2h[w-1,y] - h[w-2,y]</c> and <c>2h[x,h-1] - h[x,h-2]</c> along the two far edges. Reproduced here,
/// which is why the tessellated tile reaches half a texel further than
/// <see cref="Igi2TerrainMesh.Build"/>'s does.
/// </para>
/// <para>
/// <b>What is not reproduced.</b> The original runs one quadtree over the whole terrain and descends
/// into a neighbouring tile through <c>TerrainContext_t::aapiSubmap</c>, a per-square map index that
/// lets a square change which height map it is reading (<c>0x668CBF</c>). This builds one quadtree per
/// tile. The shipped levels place at most three tiles and they do not overlap, so the difference shows
/// only as a seam between tiles at different detail — which the original's shared-edge cache would have
/// closed and this cannot. Also absent: <c>TerrainRender_DrawGlitchPolyRecursive</c>
/// (<c>0x66A930</c>), the original's repair for the T-junction a coarse square leaves against a
/// finer neighbour that subdivided after it was drawn.
/// </para>
/// </remarks>
public sealed class Igi2TerrainLod
{
    // (0,0) (1,0) (1,1) (0,1), in units of the square's own size. Set up at 0x66869D as two interleaved
    // arrays of four; this is the same table read the same way.
    private static readonly int[] CornerX = [0, 1, 1, 0];
    private static readonly int[] CornerY = [0, 0, 1, 1];

    // The four sides then the diagonal, from the table built at 0x668E3D. The diagonal's midpoint is the
    // square's centre, which is what makes five edges enough to describe a four-way subdivision.
    private static readonly int[] EdgeFrom = [0, 1, 2, 3, 1];
    private static readonly int[] EdgeTo = [1, 2, 3, 0, 3];

    private readonly Igi2TerrainTile _tile;
    private readonly IReadOnlyDictionary<int, TerrainMaterialTask> _materials;
    private readonly Igi2TerrainHeightPyramid _bounds;
    private readonly int[] _slotOf = new int[256];
    private readonly List<int> _slotMaterial = [];

    /// <summary>Builds the quadtree over one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="materials">The level's terrain materials, keyed by id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    public Igi2TerrainLod(Igi2TerrainTile tile, IReadOnlyDictionary<int, TerrainMaterialTask> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        _tile = tile;
        _materials = materials;
        _bounds = Igi2TerrainHeightPyramid.Build(tile);

        RootSize = 1;
        while (RootSize < Math.Max(tile.Width, tile.Height))
        {
            RootSize <<= 1;
        }

        // One pass slot per material the tile actually paints and the level actually declared, in id
        // order so a run is reproducible. A sample naming a material the author later deleted gets no
        // slot at all, which under an additive blend contributes nothing -- the original's own outcome,
        // not a repair.
        Array.Fill(_slotOf, -1);
        HashSet<byte> painted = [];
        for (int y = 0; y < tile.Height; y++)
        {
            for (int x = 0; x < tile.Width; x++)
            {
                painted.Add(tile.MaterialAt(x, y));
            }
        }

        foreach (byte id in painted.Order())
        {
            if (_materials.ContainsKey(id))
            {
                _slotOf[id] = _slotMaterial.Count;
                _slotMaterial.Add(id);
            }
        }
    }

    /// <summary>Gets how many samples the quadtree's root square spans, along each axis.</summary>
    /// <remarks>
    /// One more grid line than this is produced, because the root covers sample indices <c>0..n</c>
    /// inclusive. Every shipped tile is already a square power of two between 128 and 512, so this is
    /// the tile's own extent in practice.
    /// </remarks>
    public int RootSize { get; }

    /// <summary>Gets how many drawing passes the tile has, one per material it paints.</summary>
    public int PassCount => _slotMaterial.Count;

    /// <summary>Tessellates the tile for one point of view.</summary>
    /// <param name="view">Where it is being looked at from, and what to cull against.</param>
    /// <param name="settings">The level's detail controls.</param>
    /// <returns>The geometry, and what the traversal cost.</returns>
    public Igi2TerrainTessellation Tessellate(in Igi2TerrainView view, in Igi2TerrainLodSettings settings)
    {
        Builder builder = new(this, view, settings);
        return builder.Run();
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 terrain quadtree over tile {_tile.Id}, root {RootSize}x{RootSize}, {PassCount} pass(es)";

    /// <summary>Gets the height at a sample, extrapolating the row and column past the tile.</summary>
    /// <param name="x">The sample's column, which may be <see cref="Igi2TerrainTile.Width"/>.</param>
    /// <param name="y">The sample's row, which may be <see cref="Igi2TerrainTile.Height"/>.</param>
    /// <returns>The height, in world units.</returns>
    /// <remarks>
    /// The original's arithmetic at <c>igi2ed 0x668AFB</c>, which reflects the last two samples through
    /// the edge rather than repeating the last one — so the ground keeps its slope over the extra band
    /// instead of flattening into a lip. It compares the row index against <c>nWidth</c> as well as the
    /// column, which is only correct for a square map; every shipped tile is square, and this uses each
    /// axis's own extent.
    /// </remarks>
    internal double HeightAt(int x, int y)
    {
        int width = _tile.Width;
        int height = _tile.Height;
        Igi2TerrainMap heights = _tile.Heights;

        if (x >= width && y >= height)
        {
            return (3.0 * heights.GetHeight(width - 1, height - 1))
                - heights.GetHeight(width - 2, height - 1)
                - heights.GetHeight(width - 1, height - 2);
        }

        if (x >= width)
        {
            return (2.0 * heights.GetHeight(width - 1, y)) - heights.GetHeight(width - 2, y);
        }

        if (y >= height)
        {
            return (2.0 * heights.GetHeight(x, height - 1)) - heights.GetHeight(x, height - 2);
        }

        return heights.GetHeight(x, y);
    }

    /// <summary>Gets the world position of a sample, extrapolating past the tile.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The position.</returns>
    internal Vector3d PositionOf(int x, int y) => new(EdgeX(x), EdgeY(y), HeightAt(x, y));

    /// <summary>Gets the world X of a sample column, extrapolating past the tile.</summary>
    /// <param name="x">The column.</param>
    /// <returns>The coordinate.</returns>
    internal double EdgeX(int x) =>
        _tile.Centre.X - (_tile.Width * _tile.TexelWidth * 0.5) + ((x + 0.5) * _tile.TexelWidth);

    /// <summary>Gets the world Y of a sample row, extrapolating past the tile.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The coordinate.</returns>
    internal double EdgeY(int y) =>
        _tile.Centre.Y - (_tile.Height * _tile.TexelHeight * 0.5) + ((y + 0.5) * _tile.TexelHeight);

    private byte MaterialAt(int x, int y) =>
        _tile.MaterialAt(Math.Min(x, _tile.Width - 1), Math.Min(y, _tile.Height - 1));

    private Bgra32 LightAt(int x, int y) =>
        _tile.LightAt(Math.Min(x, _tile.Width - 1), Math.Min(y, _tile.Height - 1));

    private readonly record struct Square(int X, int Y, int Size, int V0, int V1, int V2, int V3)
    {
        public int Corner(int index) => index switch
        {
            0 => V0,
            1 => V1,
            2 => V2,
            _ => index == 3 ? V3 : throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private readonly record struct EdgeDecision(bool Split, float Morph);

    private sealed class Builder
    {
        private readonly Igi2TerrainLod _owner;
        private readonly Igi2TerrainView _view;
        private readonly Igi2TerrainLodSettings _settings;

        private readonly List<Vector3d> _positions = [];
        private readonly List<double> _trueZ = [];
        private readonly List<Bgra32> _lights = [];
        private readonly List<float> _weights = [];
        private readonly Dictionary<long, int> _vertices = [];
        private readonly Dictionary<long, EdgeDecision> _edges = [];
        private readonly List<List<int>> _indices = [];

        private int _culled;
        private int _visited;
        private int _drawn;
        private int _uncovered;
        private long _covered;

        public Builder(Igi2TerrainLod owner, in Igi2TerrainView view, in Igi2TerrainLodSettings settings)
        {
            _owner = owner;
            _view = view;
            _settings = settings;
        }

        public Igi2TerrainTessellation Run()
        {
            int slots = _owner._slotMaterial.Count;
            for (int i = 0; i < slots; i++)
            {
                _indices.Add([]);
            }

            int n = _owner.RootSize;
            Stack<Square> stack = new();
            Square root = new(0, 0, n, Vertex(0, 0), Vertex(n, 0), Vertex(n, n), Vertex(0, n));
            if (!IsOutOfSight(root))
            {
                stack.Push(root);
            }
            else
            {
                _culled++;
            }

            Span<int> midpoint = stackalloc int[5];
            Span<float> morph = stackalloc float[5];

            while (stack.Count > 0)
            {
                Square square = stack.Pop();
                _visited++;

                // nSize <= (1 << nShift) at 0x668F48: a square already at the sample grid cannot be
                // subdivided, whatever the detail test would like. nShift is the submap shift, zero for
                // a tile the port loads on its own.
                if (square.Size <= 1)
                {
                    EmitLeaf(square);
                    continue;
                }

                int splits = 0;
                for (int e = 0; e < 5; e++)
                {
                    int a = square.Corner(EdgeFrom[e]);
                    int b = square.Corner(EdgeTo[e]);
                    EdgeDecision decision = Decide(square, e, a, b);
                    morph[e] = decision.Split ? decision.Morph : 0f;
                    splits += decision.Split ? 1 : 0;
                }

                if (splits == 0)
                {
                    EmitLeaf(square);
                    continue;
                }

                for (int e = 0; e < 5; e++)
                {
                    int a = square.Corner(EdgeFrom[e]);
                    int b = square.Corner(EdgeTo[e]);
                    (int mx, int my) = Midpoint(square, e);
                    midpoint[e] = SplitVertex(mx, my, a, b, morph[e]);
                }

                int half = square.Size / 2;
                Push(stack, new Square(square.X, square.Y, half, square.V0, midpoint[0], midpoint[4], midpoint[3]));
                Push(stack, new Square(square.X + half, square.Y, half, midpoint[0], square.V1, midpoint[1], midpoint[4]));
                Push(stack, new Square(square.X + half, square.Y + half, half, midpoint[4], midpoint[1], square.V2, midpoint[2]));
                Push(stack, new Square(square.X, square.Y + half, half, midpoint[3], midpoint[4], midpoint[2], square.V3));
            }

            return new Igi2TerrainTessellation(BuildMesh(), _drawn, _culled, _visited, _covered);
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private void Push(Stack<Square> stack, Square square)
        {
            if (IsOutOfSight(square))
            {
                _culled++;
                return;
            }

            stack.Push(square);
        }

        private static (int X, int Y) Midpoint(Square square, int edge)
        {
            int ax = square.X + (CornerX[EdgeFrom[edge]] * square.Size);
            int ay = square.Y + (CornerY[EdgeFrom[edge]] * square.Size);
            int bx = square.X + (CornerX[EdgeTo[edge]] * square.Size);
            int by = square.Y + (CornerY[EdgeTo[edge]] * square.Size);
            return ((ax + bx) / 2, (ay + by) / 2);
        }

        private int Vertex(int x, int y)
        {
            long key = ((long)y * (_owner.RootSize + 1)) + x;
            if (_vertices.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = _positions.Count;
            _vertices[key] = index;

            Vector3d position = _owner.PositionOf(x, y);
            _positions.Add(position);
            _trueZ.Add(position.Z);
            _lights.Add(_owner.LightAt(x, y));

            byte material = _owner.MaterialAt(x, y);
            int slot = _owner._slotOf[material];
            for (int i = 0; i < _owner._slotMaterial.Count; i++)
            {
                _weights.Add(i == slot ? 1f : 0f);
            }

            _uncovered += slot < 0 ? 1 : 0;
            return index;
        }

        private int SplitVertex(int x, int y, int a, int b, float morph)
        {
            long key = ((long)y * (_owner.RootSize + 1)) + x;
            if (_vertices.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = _positions.Count;
            _vertices[key] = index;

            double trueZ = _owner.HeightAt(x, y);

            // TerrainRender_CalculateRenderVertex, 0x66A76D: the new vertex starts at the height field
            // and is pulled back toward the straight line between its two parents by a raised cosine,
            // applied to Z alone. At morph zero it lands exactly on that line, which is what lets an
            // unsplit edge carry a vertex without showing one.
            double parentMean = (_positions[a].Z + _positions[b].Z) * 0.5;
            double z = parentMean + (Igi2TerrainLodSettings.MorphFraction(morph) * (trueZ - parentMean));

            _positions.Add(new Vector3d(_owner.EdgeX(x), _owner.EdgeY(y), z));
            _trueZ.Add(trueZ);
            _lights.Add(_owner.LightAt(x, y));

            // 0x66A7C4: the material weights morph too, and four times as fast -- the vertex takes the
            // mean of its parents' weights scaled by (1 - w) and adds w to its own material, with
            // w = min(1, morph * vMaterialMorphSpeed). So the surface has finished changing by the time
            // the geometry is halfway up.
            float weight = MathF.Min(1f, morph * Igi2TerrainLodSettings.MaterialMorphSpeed);
            int slots = _owner._slotMaterial.Count;
            int slot = _owner._slotOf[_owner.MaterialAt(x, y)];
            for (int i = 0; i < slots; i++)
            {
                float inherited = (_weights[(a * slots) + i] + _weights[(b * slots) + i]) * (1f - weight) * 0.5f;
                _weights.Add(i == slot ? inherited + weight : inherited);
            }

            _uncovered += slot < 0 ? 1 : 0;
            return index;
        }

        private EdgeDecision Decide(Square square, int edge, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (_edges.TryGetValue(key, out EdgeDecision cached))
            {
                return cached;
            }

            EdgeDecision decision = Evaluate(square, edge, a, b);
            _edges[key] = decision;
            return decision;
        }

        private EdgeDecision Evaluate(Square square, int edge, int a, int b)
        {
            Vector3d eye = _view.Eye;
            Vector3d p0 = _positions[a];
            Vector3d p1 = _positions[b];

            float ax = (float)(p0.X - eye.X);
            float ay = (float)(p0.Y - eye.Y);
            float az = (float)(p0.Z - eye.Z);
            float dx = (float)(p1.X - p0.X);
            float dy = (float)(p1.Y - p0.Y);
            float dz = (float)(p1.Z - p0.Z);

            // 0x66921F: a near-vertical edge is refused before anything else is computed. Its midpoint
            // is already where the height field wants it, so subdividing spends triangles on nothing.
            float steep = Igi2TerrainLodSettings.SteepEdgeRatio;
            if (MathF.Abs(dz) > steep * MathF.Abs(dx) && MathF.Abs(dz) > steep * MathF.Abs(dy))
            {
                return new EdgeDecision(false, 0f);
            }

            float lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
            if (lengthSquared <= Igi2TerrainLodSettings.MinimumSplitEdgeLengthSquared)
            {
                return new EdgeDecision(false, 0f);
            }

            // 0x669049: the parameter of the point on the segment nearest the eye, clamped to the
            // segment. An edge running away from the viewer is judged by whichever end is closest.
            float t = Math.Clamp(-(((ax * dx) + (ay * dy) + (az * dz)) / lengthSquared), 0f, 1f);
            float cx = ax + (t * dx);
            float cy = ay + (t * dy);
            float cz = az + (t * dz);
            float distanceSquared = (cx * cx) + (cy * cy) + (cz * cz);

            float threshold = Threshold(square, edge, a, b);
            threshold = threshold * threshold * distanceSquared;
            if (lengthSquared <= threshold)
            {
                return new EdgeDecision(false, 0f);
            }

            // 0x6692A4: how far past the threshold the edge went, halved, is the fraction it has morphed
            // in by -- so a vertex is born flat and rises as its square keeps approaching.
            float morph = ((lengthSquared / threshold) - 1f) * Igi2TerrainLodSettings.MorphSpeed;
            if (!(morph > 0f))
            {
                return new EdgeDecision(false, 0f);
            }

            if (morph >= 1f)
            {
                return new EdgeDecision(true, 1f);
            }

            return new EdgeDecision(
                true,
                MathF.Round(morph * Igi2TerrainLodSettings.MorphResolution) / Igi2TerrainLodSettings.MorphResolution);
        }

        private float Threshold(Square square, int edge, int a, int b)
        {
            // 0x669160: how far the height field departs from the straight edge at its midpoint,
            // measured against the square's own width. Zero where the midpoint falls outside the map,
            // which the original reaches through an unsigned compare against nWidth and nHeight.
            (int mx, int my) = Midpoint(square, edge);
            float roughness = 0f;
            if ((uint)mx < (uint)_owner._tile.Width && (uint)my < (uint)_owner._tile.Height)
            {
                double mean = (_trueZ[a] + _trueZ[b]) * 0.5;
                double error = Math.Abs(mean - _owner._tile.Heights.GetHeight(mx, my));
                roughness = (float)(error / (square.Size * _owner._tile.TexelWidth));
            }

            return _settings.ThresholdFor(roughness);
        }

        private bool IsOutOfSight(Square square)
        {
            ReadOnlySpan<Vector3f> planes = _view.PlaneNormals;
            if (planes.IsEmpty)
            {
                return false;
            }

            int level = BitOperations.Log2((uint)square.Size);
            _owner._bounds.GetBounds(level, square.X >> level, square.Y >> level, out float low, out float high);

            // 0x66AECF folds the four corners into the range as well, which is what covers the row the
            // block index cannot reach and any morph that has moved a corner off the height field.
            for (int corner = 0; corner < 4; corner++)
            {
                float z = (float)_positions[square.Corner(corner)].Z;
                low = MathF.Min(low, z);
                high = MathF.Max(high, z);
            }

            Vector3d eye = _view.Eye;
            float x0 = (float)(_owner.EdgeX(square.X) - eye.X);
            float x1 = (float)(_owner.EdgeX(square.X + square.Size) - eye.X);
            float y0 = (float)(_owner.EdgeY(square.Y) - eye.Y);
            float y1 = (float)(_owner.EdgeY(square.Y + square.Size) - eye.Y);
            float z0 = (float)(low - eye.Z);
            float z1 = (float)(high - eye.Z);

            foreach (Vector3f normal in planes)
            {
                bool allOutside = true;
                for (int corner = 0; corner < 8 && allOutside; corner++)
                {
                    float x = (corner & 1) == 0 ? x0 : x1;
                    float y = (corner & 2) == 0 ? y0 : y1;
                    float z = (corner & 4) == 0 ? z0 : z1;
                    allOutside = (x * normal.X) + (y * normal.Y) + (z * normal.Z) > 0f;
                }

                if (allOutside)
                {
                    return true;
                }
            }

            return false;
        }

        private void EmitLeaf(Square square)
        {
            _drawn++;
            _covered += (long)square.Size * square.Size;

            // Two triangles across the square's own diagonal, the one its five edges already name. The
            // winding is Igi2TerrainMesh's, so the full-detail and tessellated paths agree; the
            // original's own leaf at 0x669D6B issues (v0, v1, v3) and takes CULLMODE = CW.
            Emit(square.V0, square.V3, square.V1);
            Emit(square.V1, square.V3, square.V2);
        }

        private void Emit(int a, int b, int c)
        {
            int slots = _owner._slotMaterial.Count;
            for (int slot = 0; slot < slots; slot++)
            {
                // TerrainRender_DrawFace's OR (0x66AAB0): a triangle belongs to every pass that has any
                // weight at any of its three corners, so a triangle straddling a boundary is issued once
                // per material and each pass carries weight only where its own corners are.
                if (_weights[(a * slots) + slot] > 0f
                    || _weights[(b * slots) + slot] > 0f
                    || _weights[(c * slots) + slot] > 0f)
                {
                    _indices[slot].Add(a);
                    _indices[slot].Add(b);
                    _indices[slot].Add(c);
                }
            }
        }

        private Igi2TerrainMesh BuildMesh()
        {
            int slots = _owner._slotMaterial.Count;
            int count = _positions.Count;
            List<Igi2TerrainPass> passes = [];

            for (int slot = 0; slot < slots; slot++)
            {
                if (_indices[slot].Count == 0)
                {
                    continue;
                }

                int id = _owner._slotMaterial[slot];
                TerrainMaterialTask material = _owner._materials[id];

                float[] weights = new float[count];
                for (int i = 0; i < count; i++)
                {
                    weights[i] = _weights[(i * slots) + slot];
                }

                passes.Add(new Igi2TerrainPass(
                    id,
                    material.TextureScale,
                    material.DetailTextureScale,
                    _indices[slot].ToArray(),
                    weights));
            }

            return Igi2TerrainMesh.Create(
                _positions,
                _lights,
                passes,
                _owner._tile.Width,
                _owner._tile.Height,
                _uncovered);
        }
    }
}
