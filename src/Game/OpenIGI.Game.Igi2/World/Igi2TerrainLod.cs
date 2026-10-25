using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

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
/// tile and stops each one where the finer tile begins, which
/// <see cref="Igi2TerrainTile.IsSuperseded"/> decides from the same measurements
/// <c>Terrain_UpdateSubmaps</c> takes. The two agree on which ground each tile owns; where they differ
/// is the seam between them, which the original's shared-edge cache closes and separate trees cannot.
/// Also absent: <c>TerrainRender_DrawGlitchPolyRecursive</c>
/// (<c>0x66A930</c>), the original's repair for the T-junction a coarse square leaves against a
/// finer neighbour that subdivided after it was drawn.
/// </para>
/// </remarks>
public sealed class Igi2TerrainLod
{
    private readonly Igi2TerrainTile _tile;
    private readonly IReadOnlyDictionary<int, TerrainMaterialTask> _materials;
    private readonly Igi2TerrainHeightPyramid _bounds;
    private readonly Igi2TerrainDiscard[] _discards;
    private readonly int[] _slotOf = new int[256];
    private readonly List<int> _slotMaterial = [];
    private Igi2TerrainTessellationBuilder? _frameBuilder;

    /// <summary>Builds the quadtree over one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="materials">The level's terrain materials, keyed by id.</param>
    /// <param name="discards">The level's <see cref="DiscardTask"/> boxes, or null for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    public Igi2TerrainLod(
        Igi2TerrainTile tile,
        IReadOnlyDictionary<int, TerrainMaterialTask> materials,
        IReadOnlyList<Igi2TerrainDiscard>? discards = null)
    {
        ArgumentNullException.ThrowIfNull(materials);

        _tile = tile;
        _materials = materials;
        _bounds = Igi2TerrainHeightPyramid.Build(tile);
        _discards = discards as Igi2TerrainDiscard[] ?? [.. discards ?? []];

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

    /// <summary>Gets the tile the quadtree is built over.</summary>
    internal Igi2TerrainTile Tile => _tile;

    /// <summary>Gets the level's terrain materials, keyed by id.</summary>
    internal IReadOnlyDictionary<int, TerrainMaterialTask> Materials => _materials;

    /// <summary>Gets the tile's height pyramid, which bounds each square's Z extent for culling.</summary>
    internal Igi2TerrainHeightPyramid Bounds => _bounds;

    /// <summary>Gets each material id's pass slot, or -1 for a material the tile has no pass for.</summary>
    internal int[] SlotOf => _slotOf;

    /// <summary>Gets the material id each pass slot draws, in slot order.</summary>
    internal List<int> SlotMaterials => _slotMaterial;

    /// <summary>Tessellates the tile for one point of view.</summary>
    /// <param name="view">Where it is being looked at from, and what to cull against.</param>
    /// <param name="settings">The level's detail controls.</param>
    /// <returns>The geometry, and what the traversal cost.</returns>
    public Igi2TerrainTessellation Tessellate(in Igi2TerrainView view, in Igi2TerrainLodSettings settings)
    {
        Igi2TerrainTessellationBuilder builder = new(this, reuseBuffers: false);
        return builder.Run(view, settings);
    }

    /// <summary>Tessellates into retained frame buffers; the result is valid until the next call on this tile.</summary>
    /// <param name="view">The current eye and clipping planes.</param>
    /// <param name="settings">The current detail controls.</param>
    /// <returns>The frame's borrowed geometry.</returns>
    public Igi2TerrainTessellation TessellateFrame(in Igi2TerrainView view, in Igi2TerrainLodSettings settings) =>
        (_frameBuilder ??= new Igi2TerrainTessellationBuilder(this, reuseBuffers: true)).Run(view, settings);

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
        Igi2TerrainTile tile = _tile;

        if (x >= width && y >= height)
        {
            return (3.0 * tile.HeightAt(width - 1, height - 1))
                - tile.HeightAt(width - 2, height - 1)
                - tile.HeightAt(width - 1, height - 2);
        }

        if (x >= width)
        {
            return (2.0 * tile.HeightAt(width - 1, y)) - tile.HeightAt(width - 2, y);
        }

        if (y >= height)
        {
            return (2.0 * tile.HeightAt(x, height - 1)) - tile.HeightAt(x, height - 2);
        }

        return tile.HeightAt(x, y);
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

    /// <summary>Gets the material painted at a sample, clamping the row and column past the tile.</summary>
    internal byte MaterialAt(int x, int y) =>
        _tile.MaterialAt(Math.Min(x, _tile.Width - 1), Math.Min(y, _tile.Height - 1));

    /// <summary>Gets the light baked at a sample, clamping the row and column past the tile.</summary>
    internal Bgra32 LightAt(int x, int y) =>
        _tile.LightAt(Math.Min(x, _tile.Width - 1), Math.Min(y, _tile.Height - 1));

    /// <summary>Gets the normal at a sample, clamping the row and column past the tile.</summary>
    internal Vector3f NormalAt(int x, int y) =>
        _tile.NormalAt(Math.Min(x, _tile.Width - 1), Math.Min(y, _tile.Height - 1));

    // TerrainRender_CalculateRenderVertex, 0x66A7F7. X and Y only, so a morphed vertex -- which moves in
    // Z alone -- carries the same answer as the sample it was born from.
    internal bool IsDiscarded(double x, double y) =>
        _discards.Length > 0 && Igi2TerrainDiscard.AnyContains(_discards, x, y);
}
