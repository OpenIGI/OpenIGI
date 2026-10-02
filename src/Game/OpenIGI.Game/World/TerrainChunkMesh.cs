using System.Buffers;
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
    /// <param name="pool">
    /// Recycles the vertex and index arrays the geometry keeps, for a caller that evicts chunks as fast as
    /// it builds them. Omit it and each build allocates its own.
    /// </param>
    /// <returns>The geometry, or <see langword="null"/> when the block draws nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public static TerrainChunkGeometry? Build(
        in TerrainChunk chunk,
        in TerrainCommandBlock block,
        TerrainSurface surface,
        float scale = 1f,
        TerrainHeightBinding heights = default,
        TerrainGeometryPool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        int vertexCount = block.VertexCount;
        if (block.IsEmpty || vertexCount > MaximumVertices)
        {
            return null;
        }

        // Rented rather than allocated. A chunk is built whenever one comes into view at a level of detail
        // it is not already cached at, which during movement is tens of chunks a frame, and every one of
        // these is scratch that dies with the call -- the offsets and the triangle list are read into the
        // vertices below and never looked at again. Allocated, they were most of the megabytes a second
        // the profiler saw while the camera moved.
        Vector3d[]? offsetsRented = vertexCount <= 64 ? null : ArrayPool<Vector3d>.Shared.Rent(vertexCount);
        Vector3d[]? morphsRented = vertexCount <= 64 ? null : ArrayPool<Vector3d>.Shared.Rent(vertexCount);
        int[]? materialsRented = vertexCount <= 64 ? null : ArrayPool<int>.Shared.Rent(vertexCount);
        ushort[]? trianglesRented = null;
        int[]? passesRented = null;

        try
        {
        Span<Vector3d> offsets = offsetsRented is null ? stackalloc Vector3d[vertexCount] : offsetsRented.AsSpan(0, vertexCount);
        Span<Vector3d> morphs = morphsRented is null ? stackalloc Vector3d[vertexCount] : morphsRented.AsSpan(0, vertexCount);
        Span<int> materials = materialsRented is null ? stackalloc int[vertexCount] : materialsRented.AsSpan(0, vertexCount);
        Unpack(chunk, block, surface, heights, offsets, morphs, materials);

        // A rented array is at least as long as asked for and usually longer, so the count travels
        // separately and Length is never the answer to how much of it is real.
        trianglesRented = ArrayPool<ushort>.Shared.Rent(block.TriangleCount * 3);
        int triangleCount = ReadTriangles(block, vertexCount, chunk.Orientation, trianglesRented);
        if (triangleCount == 0)
        {
            return null;
        }

        ReadOnlySpan<ushort> triangles = trianglesRented.AsSpan(0, triangleCount);

        passesRented = ArrayPool<int>.Shared.Rent(vertexCount);
        int passCount = OrderMaterials(surface, materials, passesRented);
        ReadOnlySpan<int> passes = passesRented.AsSpan(0, passCount);

        // The material a vertex is sliding TOWARDS. An interpolated vertex morphs onto its source as the
        // chunk recedes, and the source may stand on a different material; sub_510010 records the
        // source's material beside the vertex's own when the two differ, and the draw weights each
        // material pass by how far along the slide the vertex is, so the texture cross-fades with the
        // shape. A base vertex, and an interpolated one whose source agrees with it, slides nowhere new.
        Span<int> others = vertexCount <= 64 ? stackalloc int[vertexCount] : new int[vertexCount];
        others.Fill(-1);
        for (int i = block.BaseVertexCount; i < vertexCount; i++)
        {
            int source = block.Vertex(i).MorphSource;
            if (source >= 0 && source < block.BaseVertexCount && source < vertexCount && materials[source] != materials[i])
            {
                others[i] = materials[source];
            }
        }

        // Two texture passes per material: the band's first scale, which the original weights by the
        // fade and which the next coarser level's second scale continues at the same density, and its
        // second at twice the density, which it weights by the fade's complement -- the dense scale
        // gives way to the sparse one as the chunk recedes, and the coarser chunk that replaces it
        // starts at exactly the density the fine one ended on. (Every band of every retail material
        // stores the same scale for both, so the ratio is fixed.) sub_510010 emits both
        // unconditionally.
        // Every element of both is written by the loops below -- one layer's worth per pass per detail,
        // covering the whole length -- so a recycled array needs no clearing first.
        int vertexTotal = passes.Length * 2 * vertexCount;
        int indexTotal = passes.Length * 2 * triangleCount;
        Vertex3D[] vertices = pool is null ? new Vertex3D[vertexTotal] : pool.RentVertices(vertexTotal);
        ushort[] indices = pool is null ? new ushort[indexTotal] : pool.RentIndices(indexTotal);

        // Exactly as many layers as the loop below writes, filled in place. The list this used to collect
        // into was copied into an array of the same length on the way out, for two allocations where the
        // count was known before either of them.
        TerrainChunkLayer[] layers = new TerrainChunkLayer[passes.Length * 2];

        int layerIndex = 0;
        for (int pass = 0; pass < passes.Length; pass++)
        {
            int materialId = passes[pass];
            bool isBase = pass == 0;

            for (int detail = 0; detail < 2; detail++)
            {
                int vertexBase = layerIndex * vertexCount;
                int indexBase = layerIndex * triangleCount;
                bool isDetail = detail == 1;

                ChunkProjection projection = Project(chunk, surface, materialId, isDetail);

                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3d offset = offsets[i];
                    Vector3d morph = morphs[i];

                    // Which of the original's per-vertex weights this pass takes at this vertex, as
                    // sub_510010 selects it: one where the vertex's own material is this pass's and it
                    // slides nowhere new; the near weight (one minus the fade) where its own material is
                    // this pass's but it slides onto a source of another; the fade itself where the
                    // SOURCE's material is this pass's; and nothing otherwise. The shader multiplies the
                    // weight by the fade for the first texture scale and by the fade's complement for
                    // the second, and the passes are added, so at every vertex the weights across every pass
                    // and both scales sum to exactly one: the near scale cross-fades into the far scale
                    // and a vertex's material into its source's as the chunk recedes, and the coarser
                    // chunk that replaces it picks up where the weights left off. Held as a selector in
                    // the colour's alpha -- thirds, so it survives a byte -- rather than as a weight,
                    // because the weight depends on the fade the shader alone knows. Every pass covers
                    // the whole chunk, the base one included: where a vertex belongs to another
                    // material the base is simply black there, and the other material's pass adds the
                    // texture in.
                    byte alpha = materials[i] == materialId
                        ? (others[i] < 0 ? (byte)255 : (byte)170)
                        : (others[i] == materialId ? (byte)85 : (byte)0);

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

                for (int i = 0; i < triangleCount; i++)
                {
                    indices[indexBase + i] = (ushort)(triangles[i] + vertexBase);
                }

                FillSurfaceNormals(vertices, triangles, vertexBase);

                layers[layerIndex] = new TerrainChunkLayer(materialId, projection.Tile, indexBase, triangleCount, isBase && !isDetail, isDetail);
                layerIndex++;
            }
        }

        return new TerrainChunkGeometry(vertices, indices, layers);
        }
        finally
        {
            Return(offsetsRented);
            Return(morphsRented);
            Return(materialsRented);
            Return(trianglesRented);
            Return(passesRented);
        }

        static void Return<T>(T[]? rented)
        {
            if (rented is not null)
            {
                ArrayPool<T>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Gives a layer's vertices a shading normal taken from the surface they describe.</summary>
    /// <param name="vertices">Every vertex of the chunk; only this layer's slice is touched.</param>
    /// <param name="triangles">The layer's triangles, as vertex indices within the layer.</param>
    /// <param name="vertexBase">The slot this layer's vertices start at.</param>
    /// <remarks>
    /// <para>
    /// The terrain has no normal to give: the channel a mesh keeps one in carries the offset to where the
    /// vertex sits at the next level of detail, and the one beside it the texture offset that goes with
    /// it. A renderer that lights the ground with that offset gets a chequerboard — the offset swings
    /// smoothly from pointing up to pointing sideways across a chunk, and a surface lit by the cosine of
    /// its angle to the sun turns a smooth swing into a hard edge.
    /// </para>
    /// <para>
    /// So the normal comes from the surface itself: the sum of the facing of every triangle meeting a
    /// vertex, weighted by area because a cross product's length is twice its triangle's. That is smooth
    /// across the chunk, where a normal taken per triangle in the pixel shader would be flat, and flat
    /// over triangles this size keeps the very patchwork it was meant to remove.
    /// </para>
    /// <para>
    /// Every normal is turned to face upwards. The ground is a heightfield, so none of them legitimately
    /// points down, and the winding these triangles arrive with is not dependable.
    /// </para>
    /// </remarks>
    private static void FillSurfaceNormals(Vertex3D[] vertices, ReadOnlySpan<ushort> triangles, int vertexBase)
    {
        int count = 0;
        for (int i = 0; i < triangles.Length; i++)
        {
            count = Math.Max(count, triangles[i] + 1);
        }

        if (count <= 0)
        {
            return;
        }

        Vector3f[]? rented = count <= 128 ? null : ArrayPool<Vector3f>.Shared.Rent(count);
        Span<Vector3f> normals = rented is null ? stackalloc Vector3f[count] : rented.AsSpan(0, count);
        normals.Clear();

        try
        {

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];

            Vector3f pa = vertices[vertexBase + a].Position;
            Vector3f pb = vertices[vertexBase + b].Position;
            Vector3f pc = vertices[vertexBase + c].Position;

            float ux = pb.X - pa.X, uy = pb.Y - pa.Y, uz = pb.Z - pa.Z;
            float vx = pc.X - pa.X, vy = pc.Y - pa.Y, vz = pc.Z - pa.Z;

            float nx = (uy * vz) - (uz * vy);
            float ny = (uz * vx) - (ux * vz);
            float nz = (ux * vy) - (uy * vx);

            if (nz < 0f)
            {
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }

            normals[a] = new Vector3f(normals[a].X + nx, normals[a].Y + ny, normals[a].Z + nz);
            normals[b] = new Vector3f(normals[b].X + nx, normals[b].Y + ny, normals[b].Z + nz);
            normals[c] = new Vector3f(normals[c].X + nx, normals[c].Y + ny, normals[c].Z + nz);
        }

        for (int i = 0; i < count; i++)
        {
            Vertex3D vertex = vertices[vertexBase + i];
            vertices[vertexBase + i] = new Vertex3D(
                vertex.Position,
                vertex.Normal,
                vertex.DiffuseU,
                vertex.DiffuseV,
                vertex.LightmapU,
                vertex.LightmapV,
                vertex.Colour,
                Vertex3D.PackSurfaceNormal(normals[i]));
        }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<Vector3f>.Shared.Return(rented);
            }
        }
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
    /// <param name="block">The chunk's command block.</param>
    /// <param name="vertexCount">How many vertices the block holds, for rejecting out-of-range indices.</param>
    /// <param name="orientation">The chunk's orientation, whose mirror bit flips the winding.</param>
    /// <param name="destination">Receives the indices; at least <c>block.TriangleCount * 3</c> long.</param>
    /// <returns>How many indices were written, which is short of the block's own count when it drops one.</returns>
    private static int ReadTriangles(in TerrainCommandBlock block, int vertexCount, int orientation, ushort[] destination)
    {
        ushort[] indices = destination;
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

        // No resize to fit: the count is the caller's answer to how much of the buffer is real, which is
        // what lets the buffer be a rented one that outlives no call.
        return cursor;
    }

    /// <summary>Lists the materials a chunk needs, least specific first.</summary>
    /// <remarks>
    /// The order is the drawing order, and it has to run from the material that covers the most to the
    /// one that covers the least — otherwise a patch would be painted over by the ground it sits on. A
    /// material's specificity is the layer that produced it, and the layers are already sorted with the
    /// smallest first, so the last layer to declare a material is the least specific place it comes from.
    /// </remarks>
    /// <param name="surface">The level's ground materials, which rank them.</param>
    /// <param name="materials">The material selected at each vertex.</param>
    /// <param name="destination">Receives the material ids; at least as long as <paramref name="materials"/>.</param>
    /// <returns>How many distinct materials were written.</returns>
    private static int OrderMaterials(TerrainSurface surface, ReadOnlySpan<int> materials, int[] destination)
    {
        // Into the caller's buffer, and sorted in place. This used to collect into a list, copy it to an
        // array, and sort through a lambda that captured the surface -- three allocations for a handful of
        // ids, once per chunk built.
        int count = 0;
        foreach (int material in materials)
        {
            bool seen = false;
            for (int i = 0; i < count; i++)
            {
                if (destination[i] == material)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen && count < destination.Length)
            {
                destination[count++] = material;
            }
        }

        // Insertion sort, descending by rank: a chunk carries a handful of materials at most, and this
        // keeps the comparison a direct call rather than a delegate.
        for (int i = 1; i < count; i++)
        {
            int material = destination[i];
            int rank = Rank(surface, material);
            int j = i - 1;
            while (j >= 0 && Rank(surface, destination[j]) < rank)
            {
                destination[j + 1] = destination[j];
                j--;
            }

            destination[j + 1] = material;
        }

        return count;

        static int Rank(TerrainSurface surface, int material)
        {
            for (int i = 0; i < surface.Layers.Length; i++)
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
            int count = surface.Terrain.Tiles?.TileCount ?? 0;
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
