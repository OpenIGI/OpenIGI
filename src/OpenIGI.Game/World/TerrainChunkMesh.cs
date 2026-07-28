using System.Collections.ObjectModel;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Turns a chunk the traversal picked, plus its <c>terrain.cmd</c> block, into drawable geometry —
/// the vertex and texturing half of <c>sub_510010</c>.
/// </summary>
/// <remarks>
/// <para>
/// A chunk's vertices are one dword each: three 6-bit coordinates biased by 24, in units of a
/// twenty-fourth of the chunk's extent. Unpacking them, applying the chunk's orientation and scaling by
/// the extent puts them in world space, and the heightmap — where one is bound — displaces the finest
/// levels on top of that.
/// </para>
/// <para>
/// The ground texture comes from three things at once. The level's <c>TextureModifier</c> layers decide
/// which <em>material</em> covers each vertex; the material's band for this chunk's level of detail gives
/// a scale and a tile map; and the tile map, indexed by three bits of the scaled world position, names
/// one tile of <c>terrain.tex</c>. The same scale then walks the texture coordinate across the chunk, so
/// a tile repeats every 256 steps and the 8×8 map cycles every 2048 — which is why the tile is constant
/// over a chunk and the coordinates still line up with its neighbours.
/// </para>
/// <para>
/// Positions come out <b>relative to the chunk's centre</b> and in metres, so the geometry does not move
/// as the camera does and can be uploaded once and drawn from
/// <c><see cref="TerrainChunk.Position"/></c>. That is the same reuse the original gets from its
/// render-chunk cache.
/// </para>
/// <para>
/// Two things the file does not settle are left alone rather than guessed at. The strip's winding
/// alternates within a run and resets on the <c>0xF0</c> flag, but no reading of that rule makes a
/// chunk's triangles face consistently, so indices are emitted exactly as written and terrain is drawn
/// double-sided. And nothing in terrain data holds a normal, so the normal is zero — the mesh shader
/// does not sample it.
/// </para>
/// </remarks>
public static class TerrainChunkMesh
{
    /// <summary>The largest vertex index a triangle command can hold.</summary>
    /// <remarks>Indices are single bytes, which is why a block never carries more than 256 vertices.</remarks>
    public const int MaximumVertices = 256;

    /// <summary>How many steps of the scaled world position one tile spans.</summary>
    public const int StepsPerTile = 256;

    /// <summary>The shift, less the level-of-detail exponent, that turns a scaled position into steps.</summary>
    /// <remarks>
    /// The band's second scale uses one less, so it lays tiles at twice the density. Only the first is
    /// drawn here; the second is the detail layer the original cross-fades on top.
    /// </remarks>
    public const int StepShiftBase = 23;

    /// <summary>Builds a chunk's geometry.</summary>
    /// <param name="chunk">The chunk, as the traversal emitted it.</param>
    /// <param name="block">The chunk's block, from <see cref="TerrainCommands.TryGetBlock"/>.</param>
    /// <param name="surface">The level's ground materials and the layers that select between them.</param>
    /// <param name="scale">
    /// A factor applied to every position. Terrain uses the same engine units as everything else, so pass
    /// <c>1 / <see cref="WorldScene.UnitsPerMetre"/></c> to get metres.
    /// </param>
    /// <param name="heights">The heightmap binding, if the level has one bound.</param>
    /// <returns>The geometry, or <see langword="null"/> when the block draws nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public static TerrainChunkGeometry? Build(
        in TerrainChunk chunk,
        in TerrainCommandBlock block,
        TerrainSurface surface,
        float scale = 1f,
        TerrainHeightBinding heights = default)
    {
        ArgumentNullException.ThrowIfNull(surface);

        int vertexCount = block.VertexCount;
        if (block.IsEmpty || vertexCount > MaximumVertices)
        {
            return null;
        }

        Span<Vector3d> offsets = vertexCount <= 64 ? stackalloc Vector3d[vertexCount] : new Vector3d[vertexCount];
        Span<Vector3d> morphs = vertexCount <= 64 ? stackalloc Vector3d[vertexCount] : new Vector3d[vertexCount];
        Span<int> materials = vertexCount <= 64 ? stackalloc int[vertexCount] : new int[vertexCount];
        Unpack(chunk, block, surface, heights, offsets, morphs, materials);

        ushort[] triangles = ReadTriangles(block, vertexCount, chunk.Orientation);
        if (triangles.Length == 0)
        {
            return null;
        }

        int[] passes = OrderMaterials(surface, materials);

        // Two texture passes per material: the band's first scale, and its second at twice the density,
        // which the renderer fades in with proximity. sub_510010 emits both unconditionally.
        Vertex3D[] vertices = new Vertex3D[passes.Length * 2 * vertexCount];
        ushort[] indices = new ushort[passes.Length * 2 * triangles.Length];
        List<TerrainChunkLayer> layers = new(passes.Length * 2);

        int layerIndex = 0;
        for (int pass = 0; pass < passes.Length; pass++)
        {
            int materialId = passes[pass];
            bool isBase = pass == 0;

            for (int detail = 0; detail < 2; detail++)
            {
                int vertexBase = layerIndex * vertexCount;
                int indexBase = layerIndex * triangles.Length;
                bool isDetail = detail == 1;

                ChunkProjection projection = Project(chunk, surface, materialId, isDetail);

                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3d offset = offsets[i];
                    Vector3d morph = morphs[i];

                    // The base pass fills the chunk, so it is opaque everywhere; every later pass shows
                    // only where its own material was selected, and fades across the triangles that
                    // straddle a boundary. A detail pass carries its material's coverage and the
                    // distance fade multiplies on top in the shader.
                    byte alpha = isBase || materials[i] == materialId ? (byte)255 : (byte)0;

                    // Bits 6-7 (base) and 8-9 (detail) of the packed vertex choose the projection axes:
                    // zero maps the texture across the ground, anything else marks a steep face whose V
                    // runs down the vertical.
                    TerrainPackedVertex packed = block.Vertex(i);
                    int axis = isDetail ? packed.DetailAxis : packed.BaseAxis;
                    (double u, double v) = ProjectVertex(projection, offset, axis, chunk.Orientation);

                    // The seam morph rides the unused channels: the normal is the world-axis offset to
                    // the vertex's far position, and the lightmap coordinate the texture offset that
                    // goes with it. The shader applies both, weighted by the LOD fade.
                    (double mu, double mv) = ProjectVertex(
                        projection,
                        new Vector3d(offset.X + morph.X, offset.Y + morph.Y, offset.Z + morph.Z),
                        axis,
                        chunk.Orientation);

                    vertices[vertexBase + i] = new Vertex3D(
                        new Vector3f((float)(offset.X * scale), (float)(offset.Y * scale), (float)(offset.Z * scale)),
                        new Vector3f((float)(morph.X * scale), (float)(morph.Y * scale), (float)(morph.Z * scale)),
                        (float)u,
                        (float)v,
                        (float)(mu - u),
                        (float)(mv - v),
                        new Bgra32(255, 255, 255, alpha));
                }

                for (int i = 0; i < triangles.Length; i++)
                {
                    indices[indexBase + i] = (ushort)(triangles[i] + vertexBase);
                }

                layers.Add(new TerrainChunkLayer(materialId, projection.Tile, indexBase, triangles.Length, isBase && !isDetail, isDetail));
                layerIndex++;
            }
        }

        return new TerrainChunkGeometry(vertices, indices, new ReadOnlyCollection<TerrainChunkLayer>(layers));
    }

    /// <summary>Builds one vertex's texture coordinate from its pass's projection.</summary>
    /// <remarks>
    /// The axis selector is authored in the node's own frame, so when the chunk's orientation swaps the
    /// X and Y axes the selector's choice of horizontal axis flips with it — the
    /// <c>(swap ^ selector) &amp; 1</c> in <c>sub_512C30</c>.
    /// </remarks>
    private static (double U, double V) ProjectVertex(
        in ChunkProjection projection,
        in Vector3d offset,
        int axis,
        int orientation)
    {
        if (axis == 0)
        {
            return (
                (offset.X * projection.Rate) + projection.OriginU,
                (offset.Y * projection.Rate) + projection.OriginV);
        }

        bool useY = (((orientation & TerrainOrientation.SwapAxes) ^ axis) & 1) != 0;
        double u = useY
            ? (offset.Y * projection.Rate) + projection.OriginV
            : (offset.X * projection.Rate) + projection.OriginU;

        return (u, (offset.Z * projection.Rate) + projection.OriginW);
    }

    /// <summary>
    /// Unpacks a block's vertices to chunk-relative offsets, computes each one's seam-morph target,
    /// and picks each one's material.
    /// </summary>
    /// <remarks>
    /// The morph is <c>sub_512C30</c>'s: a vertex renders at <c>own + morph · t</c>, with <c>t</c> the
    /// LOD fade. An interpolated vertex's target is its <em>source</em> vertex's far position, so at
    /// distance it collapses onto the edge the coarser neighbour draws and the seam closes; a base
    /// vertex at the heightmap's threshold exponent carries the negated height, which fades the
    /// displacement in as the camera approaches (the original's separate fourth channel).
    /// </remarks>
    private static void Unpack(
        in TerrainChunk chunk,
        in TerrainCommandBlock block,
        TerrainSurface surface,
        in TerrainHeightBinding heights,
        Span<Vector3d> offsets,
        Span<Vector3d> morphs,
        Span<int> materials)
    {
        double unit = chunk.UnitScale;
        int orientation = chunk.Orientation;

        // The heightmap only reaches Z past the exponent it starts contributing at. At the exponent
        // itself the height is applied and faded back out with distance; past it, it is unconditional.
        bool bound = heights.IsBound && chunk.LodExponent >= TerrainTraversal.HeightmapExponent;
        bool baseDisplaces = bound && chunk.AppliesHeightmap;

        for (int i = 0; i < offsets.Length; i++)
        {
            TerrainPackedVertex packed = block.Vertex(i);
            (int localX, int localY) = TerrainOrientation.Transform(orientation, packed.X, packed.Y);

            double offsetX = localX * unit;
            double offsetY = localY * unit;
            double offsetZ = packed.Z * unit;

            double worldX = chunk.Position.X + offsetX;
            double worldY = chunk.Position.Y + offsetY;

            bool interpolated = i >= block.BaseVertexCount;
            double morphZ = 0.0;
            if (bound)
            {
                double height = heights.Sample(worldX, worldY);
                offsetZ += height;
                if (!interpolated && !baseDisplaces)
                {
                    morphZ = -height;
                }
            }

            offsets[i] = new Vector3d(offsetX, offsetY, offsetZ);
            morphs[i] = new Vector3d(0.0, 0.0, morphZ);
            materials[i] = surface.MaterialAt(worldX, worldY);
        }

        // An interpolated vertex's far target is its source's far position — offsets and morphs of the
        // base vertices are final by now, because sources are always base vertices.
        for (int i = block.BaseVertexCount; i < offsets.Length; i++)
        {
            int source = block.Vertex(i).MorphSource;
            if (source >= block.BaseVertexCount || source >= offsets.Length)
            {
                continue;
            }

            morphs[i] = new Vector3d(
                offsets[source].X + morphs[source].X - offsets[i].X,
                offsets[source].Y + morphs[source].Y - offsets[i].Y,
                offsets[source].Z + morphs[source].Z - offsets[i].Z);
        }
    }

    /// <summary>Reads the triangle list with a consistent winding, dropping any triangle that leaves its own block.</summary>
    /// <remarks>
    /// The triples are uniformly wound as authored — over every block of level 1, 110554 shared edges
    /// run in opposite directions and none in the same — so they are emitted as written. The sign
    /// alternation in <c>sub_512C30</c> compensates its own strip walk, which carries a continuation
    /// triangle's shared vertices in shifted register order; it is not a property of the data. What
    /// does flip winding is the orientation's mirror bit (the bit <c>sub_512C30</c> publishes to
    /// <c>0xA7E06C</c> as its cull seed): a reflected chunk's transformed triangles wind the other way,
    /// so its triples are emitted swapped and every chunk lands with the same facing.
    /// </remarks>
    private static ushort[] ReadTriangles(in TerrainCommandBlock block, int vertexCount, int orientation)
    {
        ushort[] indices = new ushort[block.TriangleCount * 3];
        int cursor = 0;
        bool flip = (orientation & TerrainOrientation.Mirror) != 0;

        for (int t = 0; t < block.TriangleCount; t++)
        {
            TerrainTriangle triangle = block.Triangle(t);
            if (triangle.I0 >= vertexCount || triangle.I1 >= vertexCount || triangle.I2 >= vertexCount)
            {
                // A block whose indices leave its own vertices is not a block. Every retail one is sound;
                // dropping the triangle rather than the chunk keeps a damaged file drawable.
                continue;
            }

            indices[cursor++] = (ushort)triangle.I0;
            indices[cursor++] = (ushort)(flip ? triangle.I2 : triangle.I1);
            indices[cursor++] = (ushort)(flip ? triangle.I1 : triangle.I2);
        }

        if (cursor != indices.Length)
        {
            Array.Resize(ref indices, cursor);
        }

        return indices;
    }

    /// <summary>Lists the materials a chunk needs, least specific first.</summary>
    /// <remarks>
    /// The order is the drawing order, and it has to run from the material that covers the most to the
    /// one that covers the least — otherwise a patch would be painted over by the ground it sits on. A
    /// material's specificity is the layer that produced it, and the layers are already sorted with the
    /// smallest first, so the last layer to declare a material is the least specific place it comes from.
    /// </remarks>
    private static int[] OrderMaterials(TerrainSurface surface, ReadOnlySpan<int> materials)
    {
        List<int> present = [];
        foreach (int material in materials)
        {
            if (!present.Contains(material))
            {
                present.Add(material);
            }
        }

        if (present.Count > 1)
        {
            present.Sort((a, b) => Rank(surface, b).CompareTo(Rank(surface, a)));
        }

        return [.. present];

        static int Rank(TerrainSurface surface, int material)
        {
            for (int i = 0; i < surface.Layers.Count; i++)
            {
                if (surface.Layers[i].MaterialId == material)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }

    /// <summary>One texture pass's projection over a chunk: its tile, its origins and its rate.</summary>
    /// <param name="Tile">The tile of the level's atlas the pass samples.</param>
    /// <param name="OriginU">The coordinate at the chunk's centre, from its X.</param>
    /// <param name="OriginV">The same from its Y.</param>
    /// <param name="OriginW">The same from its Z, which the steep-face projection substitutes for V.</param>
    /// <param name="Rate">The coordinate per engine unit of offset from the centre.</param>
    private readonly record struct ChunkProjection(int Tile, double OriginU, double OriginV, double OriginW, double Rate);

    /// <summary>Picks a chunk's tile and the texture coordinates that walk across it.</summary>
    /// <remarks>
    /// Everything here is per chunk, as it is in the original: the tile and the coordinate at the chunk's
    /// centre are computed once, and a vertex only adds its own offset times the rate. The rate is
    /// exactly the derivative of the origin with respect to position, which is what keeps the coordinates
    /// continuous from one chunk to the next even though each picks its own tile. The detail pass uses
    /// the band's second scale and tile map at one less shift, so its tiles repeat at twice the density;
    /// its rate divisor is the extent rather than twice the extent, which is the same doubling.
    /// </remarks>
    private static ChunkProjection Project(
        in TerrainChunk chunk,
        TerrainSurface surface,
        int materialId,
        bool detail)
    {
        if (!surface.TryGetMaterial(materialId, out TerrainMaterial? material) || material is null)
        {
            return default;
        }

        int exponent = Math.Clamp(chunk.LodExponent, 0, TerrainMaterial.BandCount - 1);
        TerrainMaterialBand band = material.Band(exponent);

        float scale = detail ? band.Scale1 : band.Scale0;
        int tileMapId = detail ? band.TileMap1 : band.TileMap0;

        int shift = StepShiftBase - exponent - (detail ? 1 : 0);
        if (shift is < 0 or > 31)
        {
            return default;
        }

        int stepX = Steps(scale, chunk.Position.X, shift);
        int stepY = Steps(scale, chunk.Position.Y, shift);
        int stepZ = Steps(scale, chunk.Position.Z, shift);

        int tile = 0;
        if (surface.Terrain.TileMaps.TryGetValue(tileMapId, out TerrainTileMap? tileMap) && tileMap is not null)
        {
            tile = tileMap.Tile((stepX & 0x7FF) >> 8, (stepY & 0x7FF) >> 8);

            // A deliberate divergence, in a branch four levels do reach. Levels 7, 8, 11 and 12 ship a
            // twelve-tile atlas but their tile maps name tiles up to 14 — 128 entries between them. The
            // original indexes straight past its atlas and takes whatever follows as a texture; wrapping
            // keeps the named tiles distinct from each other and keeps the ground textured, which is the
            // best that can be done with a reference to something that is not there.
            int count = surface.Terrain.Tiles?.Tiles.Count ?? 0;
            if (count > 0 && tile >= count)
            {
                tile %= count;
            }
        }

        // The stored scale is already halved, and the extent is the chunk's half-width, so the first
        // pass's rate is one step per texel of the 256-texel tile and the detail pass's is two.
        double rate = detail
            ? (double)scale / chunk.Extent
            : scale * 0.5 / chunk.Extent;

        return new ChunkProjection(
            tile,
            ((byte)stepX + 1.0) / StepsPerTile,
            ((byte)stepY + 1.0) / StepsPerTile,
            ((byte)stepZ + 1.0) / StepsPerTile,
            rate);
    }

    /// <summary>Scales a world coordinate into tile steps, truncating and wrapping as the original does.</summary>
    private static int Steps(float scale, double world, int shift) =>
        unchecked((int)(long)(scale * world)) >> shift;
}
