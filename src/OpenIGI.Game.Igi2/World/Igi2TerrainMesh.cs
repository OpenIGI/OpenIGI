using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One drawing pass over a terrain tile: the triangles one material covers.</summary>
/// <param name="MaterialId">The material's own <c>ID</c>, as a <c>.tmm</c> sample carries it.</param>
/// <param name="TextureScale">The material's texture scale, in metres per repeat.</param>
/// <param name="DetailTextureScale">Its detail texture's scale, in metres per repeat.</param>
/// <param name="Indices">
/// The triangle list, three indices per triangle, into the mesh's vertices. The pass's
/// <see cref="Igi2TerrainPass.BaseTriangleCount"/> base triangles come first.
/// </param>
/// <param name="BaseTriangleCount">
/// How many of the leading triangles this pass owns as the ground's base — the ones for which it is
/// the lowest-numbered pass that covers them. See <see cref="Igi2TerrainPass.BaseIndices"/>.
/// </param>
/// <param name="Weights">
/// One weight per mesh vertex: <c>1</c> where the sample carries this material and <c>0</c> where it
/// carries another. This is the source alpha the pass blends with.
/// </param>
public readonly record struct Igi2TerrainPass(
    int MaterialId,
    float TextureScale,
    float DetailTextureScale,
    ReadOnlyMemory<int> Indices,
    int BaseTriangleCount,
    ReadOnlyMemory<float> Weights)
{
    /// <summary>Gets the triangles this pass draws as the ground's opaque base.</summary>
    /// <remarks>
    /// <para>
    /// <b>The ground has to be opaque somewhere, and the material passes on their own never are.</b>
    /// They blend SRCALPHA onto ONE — every pass ADDS — so a ground built only from them cannot hide
    /// what is behind it or what was drawn before it. The frame draws the world's objects before the
    /// terrain (walls are the best occluders, and early-Z then rejects the ground behind them), so an
    /// additive-only ground left every crate and fence it should have buried showing through the
    /// hillside, and let the far side of a valley show through the near side.
    /// </para>
    /// <para>
    /// So each triangle is written ONCE, opaquely and with depth, by the lowest-numbered pass that
    /// covers it, and every other pass that covers it adds its share on top with depth test only. The
    /// sum is unchanged — the fragment shader scales albedo by the pass's share whatever the blend, and
    /// the shares still sum to one — but the ground now owns its pixels.
    /// </para>
    /// <para>
    /// This is the arrangement I.G.I. 1's own terrain uses in this port (<c>TerrainRenderer</c> draws
    /// its base layer <c>Opaque</c> and its overlays <c>Additive</c>), and it is what the original's
    /// render state asks for: <c>TerrainRender_SetGenericState</c> (<c>igi2ed 0x66BAD0</c>) writes
    /// <c>D3DRS_ZWRITEENABLE = 1</c> and <c>D3DRS_ZFUNC = D3DCMP_LESSEQUAL</c>, so the ground writes
    /// depth and its own later passes still pass the test at equal depth.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<int> BaseIndices => Indices.Span[..(BaseTriangleCount * 3)];

    /// <summary>Gets the triangles this pass adds on top of a base another pass has written.</summary>
    public ReadOnlySpan<int> OverlayIndices => Indices.Span[(BaseTriangleCount * 3)..];
}

/// <summary>
/// A terrain tile as geometry: one vertex grid, and one pass per material painted on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A triangle belongs to every material that appears at any of its corners.</b> Not to a majority,
/// and not to a per-face id — <c>TerrainRender_DrawFace</c> (<c>igi2ed 0x66AAB0</c>) walks the
/// materials the tile uses and emits the triangle into each one whose bit is set in
/// <c>mask[v0] | mask[v1] | mask[v2]</c>. The passes then blend, each contributing where its own
/// corners are, which is what makes one material fade into the next across a shared edge.
/// </para>
/// <para>
/// The per-vertex bits are set by <c>TerrainRender_CalculateRenderVertex</c> (<c>0x66A740</c>), which
/// also keeps the weight this exposes: a vertex contributes fully to its own material's pass and
/// nothing to another's.
/// </para>
/// <para>
/// <b>The passes are additive, weighted by source alpha, and their order does not matter.</b>
/// <c>TerrainRender_SetBlendState</c> (<c>igi2ed 0x66BCB0</c>) is three calls —
/// <c>ALPHABLENDENABLE = 1</c>, <c>SRCBLEND = SRCALPHA</c>, <c>DESTBLEND = ONE</c> — so each pass adds
/// <c>texture * diffuse * weight</c> onto the frame. The weights at a sample sum to one, which turns
/// the accumulation into a weighted average without needing the passes sorted. That is why
/// <see cref="Passes"/> may be emitted in any order at all.
/// </para>
/// <para>
/// It also means a sample no pass covers contributes <em>nothing</em> rather than falling back to a
/// default — see <see cref="UncoveredSampleCount"/>.
/// </para>
/// <para>
/// <b>This is the tile at full detail, and that is a deliberate difference.</b> The original draws
/// through a quadtree whose tessellation varies with distance, morphing a vertex toward the midpoint of
/// its two parents as a level of detail fades — a cosine blend applied to Z alone. Reproducing that
/// needs the quadtree and a camera; reproducing the geometry does not, and a renderer can draw this
/// correctly today and refine later. What is <em>not</em> approximated is the material split above,
/// because that changes which pixels are which material rather than how many triangles carry them.
/// </para>
/// </remarks>
public sealed class Igi2TerrainMesh
{
    private Igi2TerrainMesh(
        IReadOnlyList<Vector3d> positions,
        IReadOnlyList<Vector3f> normals,
        IReadOnlyList<Bgra32> lights,
        IReadOnlyList<Igi2TerrainPass> passes,
        int width,
        int height)
    {
        Positions = positions;
        Normals = normals;
        Lights = lights;
        Passes = passes;
        Width = width;
        Height = height;
    }

    /// <summary>Gets each sample's world position, row-major.</summary>
    public IReadOnlyList<Vector3d> Positions { get; }

    /// <summary>Gets each sample's surface normal, matching <see cref="Positions"/>.</summary>
    /// <remarks>See <see cref="Igi2TerrainTile.NormalAt"/> — the original has none and this is the
    /// height map's own slope, taken per sample so the level of detail cannot change it.</remarks>
    public IReadOnlyList<Vector3f> Normals { get; }

    /// <summary>Gets each sample's baked light, row-major and matching <see cref="Positions"/>.</summary>
    public IReadOnlyList<Bgra32> Lights { get; }

    /// <summary>Gets the passes, one per material the tile actually paints.</summary>
    public IReadOnlyList<Igi2TerrainPass> Passes { get; }

    /// <summary>Gets how many samples the grid is across.</summary>
    public int Width { get; }

    /// <summary>Gets how many samples the grid is down.</summary>
    public int Height { get; }

    /// <summary>Gets how many triangles the passes issue between them.</summary>
    public int TriangleCount => Passes.Sum(static p => p.Indices.Length) / 3;

    /// <summary>Gets how many samples no pass covers, and which therefore draw as nothing.</summary>
    /// <remarks>
    /// A sample painted with a material the level never declared. Under an additive blend that is not a
    /// fallback to some default ground — it is a contribution of zero, so the frame keeps whatever was
    /// behind the terrain there. The original behaves the same way, which is why these are carried as a
    /// count rather than repaired.
    /// </remarks>
    public int UncoveredSampleCount { get; private init; }

    /// <summary>Wraps geometry someone else tessellated — the quadtree's own output.</summary>
    /// <param name="positions">Each vertex's world position, with the morphed height as Z.</param>
    /// <param name="normals">The samples' surface normals.</param>
    /// <param name="lights">Each vertex's baked light, matching <paramref name="positions"/>.</param>
    /// <param name="passes">The passes, one per material the geometry paints.</param>
    /// <param name="width">The tile's sample width, carried through for reference.</param>
    /// <param name="height">The tile's sample height.</param>
    /// <param name="uncovered">How many vertices name a material the level never declared.</param>
    /// <returns>The mesh.</returns>
    /// <remarks>
    /// <see cref="Igi2TerrainLod"/> produces the same three arrays a full-detail build does, so a
    /// renderer consumes either without knowing which it has. <see cref="Width"/> and
    /// <see cref="Height"/> still describe the <em>tile</em> rather than the geometry, because a
    /// tessellated tile has no grid to describe.
    /// </remarks>
    internal static Igi2TerrainMesh Create(
        IReadOnlyList<Vector3d> positions,
        IReadOnlyList<Vector3f> normals,
        IReadOnlyList<Bgra32> lights,
        IReadOnlyList<Igi2TerrainPass> passes,
        int width,
        int height,
        int uncovered) =>
        new(positions, normals, lights, passes, width, height) { UncoveredSampleCount = uncovered };

    /// <summary>Builds the geometry for one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="materials">The level's terrain materials, keyed by id.</param>
    /// <param name="discards">The level's <see cref="DiscardTask"/> boxes, or null for none.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A material a sample names but the level never declared is skipped rather than faulted: 758 of the
    /// corpus's 6.3 million samples are painted with a material their author later deleted, and the
    /// original resolves those to a null texture and draws them untextured.
    /// </remarks>
    public static Igi2TerrainMesh Build(
        Igi2TerrainTile tile,
        IReadOnlyDictionary<int, TerrainMaterialTask> materials,
        IReadOnlyList<Igi2TerrainDiscard>? discards = null)
    {
        ArgumentNullException.ThrowIfNull(materials);

        int width = tile.Width;
        int height = tile.Height;
        int count = width * height;

        Vector3d[] positions = new Vector3d[count];
        Vector3f[] normals = new Vector3f[count];
        Bgra32[] lights = new Bgra32[count];
        byte[] material = new byte[count];
        bool[] discarded = new bool[count];

        Igi2TerrainDiscard[] boxes = discards as Igi2TerrainDiscard[] ?? [.. discards ?? []];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                positions[i] = tile.PositionOf(x, y);
                normals[i] = tile.NormalAt(x, y);
                lights[i] = tile.LightAt(x, y);
                material[i] = tile.MaterialAt(x, y);

                // TerrainRender_CalculateRenderVertex, 0x66A7F7: the flag is per vertex and is taken
                // from its X and Y alone, so it is fixed for a sample whatever the level of detail.
                discarded[i] = boxes.Length > 0
                    && Igi2TerrainDiscard.AnyContains(boxes, positions[i].X, positions[i].Y);
            }
        }

        // One pass per material the tile actually paints, in id order purely so a run is reproducible.
        // Nothing depends on the order: the blend is SRCALPHA/ONE, so the passes accumulate and commute.
        List<Igi2TerrainPass> passes = [];
        foreach (byte id in material.Distinct().Order())
        {
            if (!materials.TryGetValue(id, out TerrainMaterialTask? declared))
            {
                continue;
            }

            List<int> baseIndices = [];
            List<int> overlayIndices = [];
            for (int y = 0; y + 1 < height; y++)
            {
                for (int x = 0; x + 1 < width; x++)
                {
                    // A cell a finer tile covers is that tile's to draw. Leaving it here would put a
                    // second ground in the frame rather than a hidden one, because the passes add.
                    if (tile.IsSuperseded(x, y, 1))
                    {
                        continue;
                    }

                    int a = (y * width) + x;
                    int b = a + 1;
                    int c = a + width;
                    int d = c + 1;

                    Emit(baseIndices, overlayIndices, materials, material, discarded, id, a, c, b);
                    Emit(baseIndices, overlayIndices, materials, material, discarded, id, b, c, d);
                }
            }

            if (baseIndices.Count + overlayIndices.Count == 0)
            {
                continue;
            }

            int baseTriangles = baseIndices.Count / 3;
            List<int> indices = baseIndices;
            indices.AddRange(overlayIndices);

            float[] weights = new float[count];
            for (int i = 0; i < count; i++)
            {
                weights[i] = material[i] == id ? 1f : 0f;
            }

            passes.Add(new Igi2TerrainPass(
                id,
                declared.TextureScale,
                declared.DetailTextureScale,
                indices.ToArray(),
                baseTriangles,
                weights));
        }

        int uncovered = 0;
        foreach (byte id in material)
        {
            uncovered += materials.ContainsKey(id) ? 0 : 1;
        }

        return new Igi2TerrainMesh(positions, normals, lights, passes, width, height)
        {
            UncoveredSampleCount = uncovered,
        };
    }

    /// <summary>Gets the texture coordinate a pass samples at one world position.</summary>
    /// <param name="scale">The material's texture scale, in metres per repeat.</param>
    /// <param name="position">The world position.</param>
    /// <returns>The coordinate, in texture space.</returns>
    /// <remarks>
    /// <b>A world-space planar projection, and the same one for both texture stages.</b>
    /// <c>TerrainRender_SetMaterialTextures</c> (<c>igi2ed 0x667330</c>) uploads
    /// <c>1 / (vScale * 4096)</c> into vertex-shader constants 11 and 12 with U and V set alike, and the
    /// shader multiplies the world position by it. So the scale is metres per repeat, the terrain's
    /// texturing does not follow the tile's own grid at all, and two adjacent tiles at different sample
    /// densities still line up.
    /// </remarks>
    public static (float U, float V) TextureCoordinate(float scale, Vector3d position)
    {
        double factor = 1.0 / (scale * WorldScene.UnitsPerMetre);
        return ((float)(position.X * factor), (float)(position.Y * factor));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 terrain mesh {Width}x{Height}, {Passes.Count} pass(es), {TriangleCount} triangles";

    private static void Emit(
        List<int> baseIndices,
        List<int> overlayIndices,
        IReadOnlyDictionary<int, TerrainMaterialTask> materials,
        byte[] material,
        bool[] discarded,
        byte id,
        int a,
        int b,
        int c)
    {
        // The AND that TerrainRender_DrawFace performs first (0x66AB24): a face goes only when all three
        // of its corners stand over a DiscardTask. One corner outside keeps the whole triangle, which is
        // what closes the hole's edge against the building sitting in it.
        if (discarded[a] && discarded[b] && discarded[c])
        {
            return;
        }

        // The OR that TerrainRender_DrawFace performs: one corner of this material is enough.
        if (material[a] != id && material[b] != id && material[c] != id)
        {
            return;
        }

        // The lowest declared material among the three corners writes the triangle as the ground's
        // base; the rest add over it. Passes are built in ascending id order, so "lowest" is the first
        // pass to reach the triangle and every later one finds a base already written.
        int lowest = int.MaxValue;
        foreach (int corner in (ReadOnlySpan<int>)[a, b, c])
        {
            if (materials.ContainsKey(material[corner]))
            {
                lowest = Math.Min(lowest, material[corner]);
            }
        }

        List<int> into = lowest == id ? baseIndices : overlayIndices;
        into.Add(a);
        into.Add(b);
        into.Add(c);
    }
}
