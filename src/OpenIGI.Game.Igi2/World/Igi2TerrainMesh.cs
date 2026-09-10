using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One drawing pass over a terrain tile: the triangles one material covers.</summary>
/// <param name="MaterialId">The material's own <c>ID</c>, as a <c>.tmm</c> sample carries it.</param>
/// <param name="TextureScale">The material's texture scale, in metres per repeat.</param>
/// <param name="DetailTextureScale">Its detail texture's scale, in metres per repeat.</param>
/// <param name="Indices">The triangle list, three indices per triangle, into the mesh's vertices.</param>
/// <param name="Weights">
/// One weight per mesh vertex: <c>1</c> where the sample carries this material and <c>0</c> where it
/// carries another. This is the source alpha the pass blends with.
/// </param>
public readonly record struct Igi2TerrainPass(
    int MaterialId,
    float TextureScale,
    float DetailTextureScale,
    ReadOnlyMemory<int> Indices,
    ReadOnlyMemory<float> Weights);

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
        IReadOnlyList<Bgra32> lights,
        IReadOnlyList<Igi2TerrainPass> passes,
        int width,
        int height)
    {
        Positions = positions;
        Lights = lights;
        Passes = passes;
        Width = width;
        Height = height;
    }

    /// <summary>Gets each sample's world position, row-major.</summary>
    public IReadOnlyList<Vector3d> Positions { get; }

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
        IReadOnlyList<Bgra32> lights,
        IReadOnlyList<Igi2TerrainPass> passes,
        int width,
        int height,
        int uncovered) =>
        new(positions, lights, passes, width, height) { UncoveredSampleCount = uncovered };

    /// <summary>Builds the geometry for one tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="materials">The level's terrain materials, keyed by id.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A material a sample names but the level never declared is skipped rather than faulted: 758 of the
    /// corpus's 6.3 million samples are painted with a material their author later deleted, and the
    /// original resolves those to a null texture and draws them untextured.
    /// </remarks>
    public static Igi2TerrainMesh Build(Igi2TerrainTile tile, IReadOnlyDictionary<int, TerrainMaterialTask> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        int width = tile.Width;
        int height = tile.Height;
        int count = width * height;

        Vector3d[] positions = new Vector3d[count];
        Bgra32[] lights = new Bgra32[count];
        byte[] material = new byte[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                positions[i] = tile.PositionOf(x, y);
                lights[i] = tile.LightAt(x, y);
                material[i] = tile.MaterialAt(x, y);
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

            List<int> indices = [];
            for (int y = 0; y + 1 < height; y++)
            {
                for (int x = 0; x + 1 < width; x++)
                {
                    int a = (y * width) + x;
                    int b = a + 1;
                    int c = a + width;
                    int d = c + 1;

                    Emit(indices, material, id, a, c, b);
                    Emit(indices, material, id, b, c, d);
                }
            }

            if (indices.Count == 0)
            {
                continue;
            }

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
                weights));
        }

        int uncovered = 0;
        foreach (byte id in material)
        {
            uncovered += materials.ContainsKey(id) ? 0 : 1;
        }

        return new Igi2TerrainMesh(positions, lights, passes, width, height)
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

    private static void Emit(List<int> into, byte[] material, byte id, int a, int b, int c)
    {
        // The OR that TerrainRender_DrawFace performs: one corner of this material is enough.
        if (material[a] == id || material[b] == id || material[c] == id)
        {
            into.Add(a);
            into.Add(b);
            into.Add(c);
        }
    }
}
