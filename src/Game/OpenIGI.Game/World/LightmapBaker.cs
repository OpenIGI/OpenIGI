using System.Runtime.CompilerServices;
using System.Threading;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Bakes path-traced object lightmaps: per-texel, coloured, multi-bounce indirect for the static world.
/// </summary>
/// <remarks>
/// <para>
/// The successor to <see cref="IrradianceProbeBaker"/>. That volume bake has a hard quality ceiling --
/// it traces the colourless collision mesh and stores a low-frequency grey L1-SH field at metre spacing,
/// so its bounce is monochrome, flat and coarse. This bakes into the meshes' own <b>lightmap UV channel</b>
/// (the second UV set format-1 and format-3 meshes carry, <c>world-loading.md §4.3</c>), so every texel is
/// a real surface point that can be path traced to a coloured, high-resolution, multi-bounce answer. It
/// is the right trade for I.G.I.: the geometry is static and the sun is fixed per mission, so the whole
/// thing bakes once and is cached.
/// </para>
/// <para>
/// <b>Phase 1 (this file): coloured geometry and the texel map.</b> For every lightmapped surface it
/// rasterises the draw record's triangles into its lightmap page's UV space and records, per covered
/// texel, the world position and normal it belongs to -- the map every later phase traces from. The
/// tracing (direct sun and sky, then the multi-bounce colour bleed), the BVH it traces against, seam
/// dilation and the runtime swap are the following phases; see
/// the original. A <see cref="DumpTargetNormals"/> is here so the
/// texel map can be verified on its own -- a correct one reads as a UV-space unwrap of the object with
/// its world normals -- before any light is traced.
/// </para>
/// </remarks>
public static class LightmapBaker
{
    /// <summary>One lightmap texel's surface: the point on the mesh it covers, or <see cref="IsCovered"/> false.</summary>
    /// <param name="WorldPosition">Where on the surface this texel sits, in world metres.</param>
    /// <param name="Normal">The surface normal there, world space and unit length.</param>
    /// <param name="IsCovered">Whether a triangle covered this texel at all; an uncovered texel is a gutter pixel.</param>
    public readonly record struct LightmapTexel(Vector3d WorldPosition, Vector3f Normal, bool IsCovered);

    /// <summary>One page of lightmap texels for one draw record of one placed object.</summary>
    /// <remarks>
    /// A record, not the model: a model's draw records can name different lightmap pages, and a page is
    /// what an <c>.olm</c> image is (<c>world-loading.md §4.5</c>). The grid is row-major, width fastest.
    /// </remarks>
    public sealed class LightmapTarget
    {
        internal LightmapTarget(int placementIndex, int recordIndex, int page, int width, int height)
        {
            PlacementIndex = placementIndex;
            RecordIndex = recordIndex;
            Page = page;
            Width = width;
            Height = height;
            Texels = new LightmapTexel[width * height];
            Radiance = new Vector3f[width * height];
            Direct = new Vector3f[width * height];
        }

        /// <summary>The scene placement this page belongs to.</summary>
        public int PlacementIndex { get; }

        /// <summary>The draw record within the placement's model.</summary>
        public int RecordIndex { get; }

        /// <summary>The lightmap page index within the model.</summary>
        public int Page { get; }

        /// <summary>The page width in texels.</summary>
        public int Width { get; }

        /// <summary>The page height in texels.</summary>
        public int Height { get; }

        /// <summary>The texels, row-major, width fastest.</summary>
        public LightmapTexel[] Texels { get; }

        /// <summary>The light each texel receives, filled by the bake; parallel to <see cref="Texels"/>.</summary>
        /// <remarks>Direct sun plus sky after phase 2; the multi-bounce indirect is folded in by phase 3.</remarks>
        public Vector3f[] Radiance { get; }

        /// <summary>The direct sun term alone, kept so each bounce pass can re-add it rather than re-tracing it.</summary>
        public Vector3f[] Direct { get; }
    }

    /// <summary>Builds the texel maps for every lightmapped surface a scene places.</summary>
    /// <param name="scene">The built scene whose placements are lightmapped.</param>
    /// <returns>One target per (placement, draw record) that names a lightmap page, with its texels filled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public static IReadOnlyList<LightmapTarget> EnumerateTargets(WorldScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        List<LightmapTarget> targets = [];

        for (int p = 0; p < scene.Placements.Count; p++)
        {
            WorldPlacement placement = scene.Placements[p];
            MefModel model = placement.Model;

            // Only lightmapped meshes carry the second UV set, and only a placement with a bake of its
            // own is one whose lightmap is this object's rather than shared or absent.
            if (model.LightmapPages.Count == 0 || placement.LightmapIndex < 0)
            {
                continue;
            }

            ReadOnlySpan<MefVertex> vertices = model.Vertices;

            for (int r = 0; r < model.DrawRecords.Count; r++)
            {
                MefDrawRecord record = model.DrawRecords[r];

                if (record.LightmapPage < 0 || record.LightmapPage >= model.LightmapPages.Count)
                {
                    continue;
                }

                MefLightmapPage page = model.LightmapPages[record.LightmapPage];

                if (page.Width <= 0 || page.Height <= 0)
                {
                    continue;
                }

                LightmapTarget target = new(p, r, record.LightmapPage, page.Width, page.Height);
                RasteriseRecord(placement, vertices, record, target);
                targets.Add(target);
            }
        }

        return targets;
    }

    /// <summary>How far off a surface, in world units, a ray starts, so it does not hit its own texel.</summary>
    private const double RayBiasUnits = 96.0;

    /// <summary>How far, in metres, a ray looks before a direction counts as open (sun or sky).</summary>
    private const float RayReachMetres = 60f;

    /// <summary>Bakes each texel's direct sun and sky into <see cref="LightmapTarget.Radiance"/> (phase 2).</summary>
    /// <param name="targets">The texel maps from <see cref="EnumerateTargets"/>.</param>
    /// <param name="cast">Whether a ray is occluded, in engine units — the level's objects and terrain.</param>
    /// <param name="sunDirection">The direction towards the sun; zero when the sun is below the horizon.</param>
    /// <param name="sunColour">The sun's radiance, per channel.</param>
    /// <param name="skyColour">The radiance a fully open texel receives from the sky, per channel.</param>
    /// <param name="hemisphereRays">Cosine-weighted rays per texel for the sky term.</param>
    /// <param name="work">
    /// Where the texels are traced: all at once on a scheduler that runs work concurrently, a block a
    /// slice on one that does not.
    /// </param>
    /// <param name="cancellationToken">Cancels a bake in flight.</param>
    /// <returns>The phase, a slice at a time; <see cref="Stepping.Drain"/> runs it outright.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IEnumerable<bool> BakeDirectAndSky(
        IReadOnlyList<LightmapTarget> targets,
        WorldRayCast cast,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        int hemisphereRays,
        IWorkScheduler work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(cast);
        ArgumentNullException.ThrowIfNull(work);

        Vector3f[] hemisphere = CosineHemisphere(Math.Max(hemisphereRays, 1));
        bool hasSun = (sunDirection.X * sunDirection.X)
            + (sunDirection.Y * sunDirection.Y)
            + (sunDirection.Z * sunDirection.Z) > 1e-6f;

        TexelBlock[] blocks = TexelBlock.Of(targets);

        return Stepping.Range(
            work,
            blocks.Length,
            Parallelism,
            b =>
            {
                LowerPriority(work);

                TexelBlock block = blocks[b];
                LightmapTarget target = targets[block.Target];

                // A block of a page rather than the whole page per piece of work: a big page is thousands
                // of texels each casting a hemisphere of rays, seconds of work nothing can interrupt from
                // outside. Cancelling a bake used to leave every core on it for the better part of a
                // minute, which is what leaving a level felt like; and a host tracing a slice of a frame at
                // a time needs pieces that fit. The token is still read per texel -- one field read against
                // a hundred and twenty-eight ray casts -- so it stops inside a block too.
                for (int i = block.From; i < block.To; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    LightmapTexel texel = target.Texels[i];

                    if (texel.IsCovered)
                    {
                        (target.Direct[i], target.Radiance[i]) =
                            ShadeTexel(texel, cast, hasSun, sunDirection, sunColour, skyColour, hemisphere);
                    }
                }
            },
            cancellationToken);
    }

    /// <summary>How many pieces of a phase run at once where they run together: every core but one.</summary>
    /// <remarks>
    /// The bake used to have the machine to itself because it blocked the frame loop; now that it does
    /// not, saturating every core makes the frames it no longer blocks stutter instead.
    /// </remarks>
    private static int Parallelism => Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Puts a worker below the render thread, where the bake runs beside the frame at all.</summary>
    /// <remarks>
    /// So the bake yields to a frame that needs a core rather than starving it. Set once per pooled thread;
    /// the guard skips the call after the first piece it runs.
    /// </remarks>
    private static void LowerPriority(IWorkScheduler work)
    {
        if (work.IsConcurrent && Thread.CurrentThread.Priority == ThreadPriority.Normal)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }
    }

    /// <summary>A run of one page's texels: the piece of work a phase is divided into.</summary>
    /// <param name="Target">Which page.</param>
    /// <param name="From">Its first texel.</param>
    /// <param name="To">One past its last.</param>
    private readonly record struct TexelBlock(int Target, int From, int To)
    {
        /// <summary>How many texels a block holds: small enough for a slice of a frame to trace.</summary>
        private const int Texels = 64;

        /// <summary>Every page's texels, in blocks, in page order.</summary>
        public static TexelBlock[] Of(IReadOnlyList<LightmapTarget> targets)
        {
            List<TexelBlock> blocks = [];

            for (int t = 0; t < targets.Count; t++)
            {
                int count = targets[t].Texels.Length;

                for (int from = 0; from < count; from += Texels)
                {
                    blocks.Add(new TexelBlock(t, from, Math.Min(count, from + Texels)));
                }
            }

            return [.. blocks];
        }
    }

    // The light one texel receives: the sun if the shadow ray to it is clear, plus the share of the sky
    // hemisphere the cosine-weighted rays find open. Both start a little off the surface along its normal,
    // or a ray would begin inside the texel's own triangle and report it occluded.
    private static (Vector3f Direct, Vector3f Total) ShadeTexel(
        LightmapTexel texel, WorldRayCast cast, bool hasSun, Vector3f sunDirection, Vector3f sunColour,
        Vector3f skyColour, Vector3f[] hemisphere)
    {
        Vector3d originUnits = new(
            (texel.WorldPosition.X * WorldScene.UnitsPerMetre) + (texel.Normal.X * RayBiasUnits),
            (texel.WorldPosition.Y * WorldScene.UnitsPerMetre) + (texel.Normal.Y * RayBiasUnits),
            (texel.WorldPosition.Z * WorldScene.UnitsPerMetre) + (texel.Normal.Z * RayBiasUnits));

        double reachUnits = RayReachMetres * WorldScene.UnitsPerMetre;
        Vector3f result = default;

        // Direct sun: one shadow ray, and only where the sun is above the surface's own horizon (a face
        // pointing away is dark for a reason no ray can change).
        if (hasSun)
        {
            float nDotL = (texel.Normal.X * sunDirection.X)
                + (texel.Normal.Y * sunDirection.Y)
                + (texel.Normal.Z * sunDirection.Z);

            if (nDotL > 0f)
            {
                Vector3d sunEnd = new(
                    originUnits.X + (sunDirection.X * reachUnits),
                    originUnits.Y + (sunDirection.Y * reachUnits),
                    originUnits.Z + (sunDirection.Z * reachUnits));

                if (!cast(originUnits, sunEnd, out _))
                {
                    result = new Vector3f(sunColour.X * nDotL, sunColour.Y * nDotL, sunColour.Z * nDotL);
                }
            }
        }

        // Sky: cosine-weighted rays about the normal. The fraction that leave the level unobstructed is the
        // texel's sky visibility; the cosine weighting is already in the sampling, so a flat average of "did
        // this ray reach the sky" is the sky irradiance estimate.
        (Vector3f tangent, Vector3f bitangent) = TangentFrame(texel.Normal);
        int open = 0;

        for (int r = 0; r < hemisphere.Length; r++)
        {
            Vector3f local = hemisphere[r];
            Vector3f dir = new(
                (tangent.X * local.X) + (bitangent.X * local.Y) + (texel.Normal.X * local.Z),
                (tangent.Y * local.X) + (bitangent.Y * local.Y) + (texel.Normal.Y * local.Z),
                (tangent.Z * local.X) + (bitangent.Z * local.Y) + (texel.Normal.Z * local.Z));

            Vector3d end = new(
                originUnits.X + (dir.X * reachUnits),
                originUnits.Y + (dir.Y * reachUnits),
                originUnits.Z + (dir.Z * reachUnits));

            if (!cast(originUnits, end, out _))
            {
                open++;
            }
        }

        float skyVisibility = (float)open / hemisphere.Length;
        Vector3f total = new(
            result.X + (skyColour.X * skyVisibility),
            result.Y + (skyColour.Y * skyVisibility),
            result.Z + (skyColour.Z * skyVisibility));
        return (result, total);
    }

    // A right-handed tangent frame around a normal, for turning the local hemisphere set into world
    // directions. The reference axis is whichever the normal leans on least, so the cross never collapses.
    private static (Vector3f Tangent, Vector3f Bitangent) TangentFrame(Vector3f normal)
    {
        Vector3f reference = MathF.Abs(normal.Z) < 0.9f ? new Vector3f(0f, 0f, 1f) : new Vector3f(1f, 0f, 0f);
        Vector3f tangent = Normalise(Cross(reference, normal));
        return (tangent, Cross(normal, tangent));
    }

    private static Vector3f Cross(Vector3f a, Vector3f b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    // A cosine-weighted set of hemisphere directions in local space (z up), spaced by the golden angle so
    // they never fall into rows. Cosine-weighted because irradiance is the cosine-weighted integral of
    // incoming light, so a plain average of these estimates it.
    private static Vector3f[] CosineHemisphere(int count)
    {
        Vector3f[] directions = new Vector3f[count];
        float goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

        for (int i = 0; i < count; i++)
        {
            float fraction = (i + 0.5f) / count;
            float radius = MathF.Sqrt(fraction);
            float theta = goldenAngle * i;
            float z = MathF.Sqrt(MathF.Max(1f - fraction, 0f));
            directions[i] = new Vector3f(MathF.Cos(theta) * radius, MathF.Sin(theta) * radius, z);
        }

        return directions;
    }

    // ---------- Phase 3: multi-bounce via a BVH over the lightmapped triangles ----------

    /// <summary>
    /// One lightmapped triangle a gather ray can hit, carrying what a bounce needs: its world corners, its
    /// albedo, and which target and lightmap UVs to read the light already on it.
    /// </summary>
    /// <param name="A">First corner, world metres.</param>
    /// <param name="B">Second corner.</param>
    /// <param name="C">Third corner.</param>
    /// <param name="Target">Index into the targets list of the page this triangle draws into.</param>
    /// <param name="U0">First corner's lightmap U.</param>
    /// <param name="V0">First corner's lightmap V.</param>
    /// <param name="U1">Second corner's lightmap U.</param>
    /// <param name="V1">Second corner's lightmap V.</param>
    /// <param name="U2">Third corner's lightmap U.</param>
    /// <param name="V2">Third corner's lightmap V.</param>
    /// <param name="Albedo">The surface's diffuse reflectance, per channel.</param>
    public readonly record struct LightmapTriangle(
        Vector3d A, Vector3d B, Vector3d C, int Target,
        float U0, float V0, float U1, float V1, float U2, float V2, Vector3f Albedo);

    /// <summary>The albedo a surface whose texture cannot be resolved falls back to -- a neutral mid grey.</summary>
    private static readonly Vector3f DefaultAlbedo = new(0.5f, 0.5f, 0.5f);

    /// <summary>Builds the world-space lightmapped triangles the gather traces against.</summary>
    /// <param name="scene">The scene the targets came from.</param>
    /// <param name="targets">The targets from <see cref="EnumerateTargets"/>; a triangle's <c>Target</c> indexes this.</param>
    /// <param name="resources">Where a record's diffuse texture is read from, for its bounce colour; null keeps every surface grey.</param>
    /// <returns>Every lightmapped triangle, tagged with its target, lightmap UVs and albedo colour.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="targets"/> is null.</exception>
    public static LightmapTriangle[] BuildTriangles(
        WorldScene scene, IReadOnlyList<LightmapTarget> targets, ResourceRegistry? resources = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);

        List<LightmapTriangle> triangles = [];

        // One decode per distinct texture, not per record: a texture drawn a hundred times is read once.
        Dictionary<string, Vector3f> albedoByTexture = new(StringComparer.OrdinalIgnoreCase);

        for (int t = 0; t < targets.Count; t++)
        {
            LightmapTarget target = targets[t];
            WorldPlacement placement = scene.Placements[target.PlacementIndex];
            MefModel model = placement.Model;
            MefDrawRecord record = model.DrawRecords[target.RecordIndex];
            ReadOnlySpan<MefVertex> vertices = model.Vertices;
            ReadOnlySpan<ushort> indices = record.Indices.Span;

            // The surface's own colour, which is what turns the bounce from grey to coloured -- a warm
            // wooden floor throws warm light, a red sign tints the wall beside it.
            Vector3f albedo = ResolveAlbedo(placement, record, resources, albedoByTexture);

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                MefVertex va = ResolveVertex(vertices, record, indices[i]);
                MefVertex vb = ResolveVertex(vertices, record, indices[i + 1]);
                MefVertex vc = ResolveVertex(vertices, record, indices[i + 2]);

                triangles.Add(new LightmapTriangle(
                    ToWorldMetres(placement, va.Position),
                    ToWorldMetres(placement, vb.Position),
                    ToWorldMetres(placement, vc.Position),
                    t,
                    va.LightmapU, va.LightmapV, vb.LightmapU, vb.LightmapV, vc.LightmapU, vc.LightmapV,
                    albedo));
            }
        }

        return [.. triangles];
    }

    // The mean colour of a record's diffuse texture, used as its bounce albedo. The texture name comes
    // from the pack's material binding (REND slot -> instance texture); the pixels from the level's own
    // textures/<name>.tex, which decode direct-colour (no palette). A texture that will not resolve or
    // decode keeps the grey default -- a miss costs the tint on that surface, never the bake. Cached per
    // name so a texture used a hundred times is read once.
    private static Vector3f ResolveAlbedo(
        WorldPlacement placement, MefDrawRecord record, ResourceRegistry? resources, Dictionary<string, Vector3f> cache)
    {
        if (resources is null
            || record.MaterialSlot == MefDrawRecord.NoMaterialSlot
            || !placement.Pack.TryGetTextureName(placement.Instance, record.MaterialSlot, out string texture))
        {
            return DefaultAlbedo;
        }

        if (cache.TryGetValue(texture, out Vector3f cached))
        {
            return cached;
        }

        Vector3f colour = DefaultAlbedo;

        try
        {
            if (resources.TryLoad(LoadedPack.TextureResourceName(texture), out ReadOnlyMemory<byte> data)
                && data.Length >= 4
                && TexFile.IsTexture(data.Span))
            {
                colour = MeanColour(TexFile.Load(data.Span));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            // Illegal name, atlas rather than a plain texture, paletted with no palette shipped: the tint
            // is never worth taking the bake down for. It stays grey.
        }

        cache[texture] = colour;
        return colour;
    }

    // Averages a decoded texture into a single albedo colour. Divided into [0,1]; I.G.I.'s textures carry
    // some painted-in lighting so this over-reads the true reflectance a little, but it is the HUE that
    // matters for a coloured bounce, and the bounce's own scale carries the magnitude.
    private static Vector3f MeanColour(TextureImage image)
    {
        ReadOnlySpan<Bgra32> pixels = image.Pixels;
        (int width, int height) = image.GetLevelSize(0);
        int count = Math.Min(width * height, pixels.Length);

        if (count <= 0)
        {
            return DefaultAlbedo;
        }

        double r = 0;
        double g = 0;
        double b = 0;

        for (int i = 0; i < count; i++)
        {
            Bgra32 pixel = pixels[i];
            r += pixel.R;
            g += pixel.G;
            b += pixel.B;
        }

        return new Vector3f(
            (float)(r / count / 255.0),
            (float)(g / count / 255.0),
            (float)(b / count / 255.0));
    }

    // Walks a record's triangle list and rasterises each one into the target's lightmap-UV space, filling
    // every texel a triangle covers with the world position and normal barycentrically interpolated there.
    private static void RasteriseRecord(
        WorldPlacement placement, ReadOnlySpan<MefVertex> vertices, MefDrawRecord record, LightmapTarget target)
    {
        ReadOnlySpan<ushort> indices = record.Indices.Span;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            MefVertex a = ResolveVertex(vertices, record, indices[i]);
            MefVertex b = ResolveVertex(vertices, record, indices[i + 1]);
            MefVertex c = ResolveVertex(vertices, record, indices[i + 2]);

            RasteriseTriangle(placement, a, b, c, target);
        }
    }

    // A record's indices are relative to its BaseVertex on the lightmapped format and absolute otherwise
    // (world-loading.md §4.4); ResolveVertex hides that so the caller always gets the right vertex.
    private static MefVertex ResolveVertex(ReadOnlySpan<MefVertex> vertices, MefDrawRecord record, ushort index)
    {
        int vertexIndex = record.IndicesAreRelative ? record.BaseVertex + index : index;
        return vertices[vertexIndex];
    }

    // One triangle into UV space. The lightmap UV maps [0,1] onto the page, so a texel centre at (x+0.5,
    // y+0.5) sits at UV ((x+0.5)/width, (y+0.5)/height); a texel is covered when that point is inside the
    // triangle's UV projection, and its surface point is the same barycentric blend of the three vertices'
    // world positions and normals. Only the UV bounding box is scanned, so the cost is the triangle's area
    // in texels rather than the whole page.
    private static void RasteriseTriangle(
        WorldPlacement placement, MefVertex a, MefVertex b, MefVertex c, LightmapTarget target)
    {
        // The three UV corners in texel coordinates.
        float uvAx = a.LightmapU * target.Width, uvAy = a.LightmapV * target.Height;
        float uvBx = b.LightmapU * target.Width, uvBy = b.LightmapV * target.Height;
        float uvCx = c.LightmapU * target.Width, uvCy = c.LightmapV * target.Height;

        // A degenerate UV triangle covers no area; its edge cross product is the doubled signed area, and
        // dividing by it below would be a divide by zero.
        float area = ((uvBx - uvAx) * (uvCy - uvAy)) - ((uvBy - uvAy) * (uvCx - uvAx));

        if (MathF.Abs(area) < 1e-8f)
        {
            return;
        }

        float inverseArea = 1f / area;

        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(uvAx, MathF.Min(uvBx, uvCx))), 0, target.Width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(uvAx, MathF.Max(uvBx, uvCx))), 0, target.Width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(uvAy, MathF.Min(uvBy, uvCy))), 0, target.Height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(uvAy, MathF.Max(uvBy, uvCy))), 0, target.Height - 1);

        // The three world positions and normals the barycentric blend runs over.
        Vector3d worldA = ToWorldMetres(placement, a.Position);
        Vector3d worldB = ToWorldMetres(placement, b.Position);
        Vector3d worldC = ToWorldMetres(placement, c.Position);
        Vector3f normalA = ToWorldNormal(placement, a.Normal);
        Vector3f normalB = ToWorldNormal(placement, b.Normal);
        Vector3f normalC = ToWorldNormal(placement, c.Normal);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                // Barycentric weights of the texel centre against the UV triangle.
                float wA = (((uvBx - px) * (uvCy - py)) - ((uvBy - py) * (uvCx - px))) * inverseArea;
                float wB = (((uvCx - px) * (uvAy - py)) - ((uvCy - py) * (uvAx - px))) * inverseArea;
                float wC = 1f - wA - wB;

                // A small negative tolerance keeps the shared edge between two triangles from leaving a
                // one-texel gap, which reads as a dark seam once the page is lit.
                const float edge = -0.001f;
                if (wA < edge || wB < edge || wC < edge)
                {
                    continue;
                }

                Vector3d world = new(
                    (worldA.X * wA) + (worldB.X * wB) + (worldC.X * wC),
                    (worldA.Y * wA) + (worldB.Y * wB) + (worldC.Y * wC),
                    (worldA.Z * wA) + (worldB.Z * wB) + (worldC.Z * wC));

                Vector3f normal = Normalise(new Vector3f(
                    (normalA.X * wA) + (normalB.X * wB) + (normalC.X * wC),
                    (normalA.Y * wA) + (normalB.Y * wB) + (normalC.Y * wC),
                    (normalA.Z * wA) + (normalB.Z * wB) + (normalC.Z * wC)));

                target.Texels[(y * target.Width) + x] = new LightmapTexel(world, normal, true);
            }
        }
    }

    // A raw MEF vertex is in engine units in the model's own frame; the world point is the placement's
    // rotation applied and its position added, matching the transform ObjectCollisionIndex builds its
    // triangles with. The rotation is in units, so it is divided to metres before the metre position is
    // added.
    private static Vector3d ToWorldMetres(WorldPlacement placement, Vector3f localUnits)
    {
        Vector3f rotated = placement.Orientation.Transform(localUnits);
        return new Vector3d(
            (rotated.X / WorldScene.UnitsPerMetre) + placement.Position.X,
            (rotated.Y / WorldScene.UnitsPerMetre) + placement.Position.Y,
            (rotated.Z / WorldScene.UnitsPerMetre) + placement.Position.Z);
    }

    // A normal rotates with the object but does not translate or scale; the placement's orientation is a
    // pure rotation, so transforming and renormalising is all it takes.
    private static Vector3f ToWorldNormal(WorldPlacement placement, Vector3f localNormal) =>
        Normalise(placement.Orientation.Transform(localNormal));

    private static Vector3f Normalise(Vector3f value)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 1e-8f ? new Vector3f(value.X / length, value.Y / length, value.Z / length) : new Vector3f(0f, 0f, 1f);
    }

    /// <summary>A bounding-volume hierarchy over the lightmapped triangles, for the gather rays to trace.</summary>
    /// <remarks>
    /// A median-split BVH: the triangles are recursively partitioned by the midpoint of their centroids
    /// along the widest axis, so a ray tests a logarithmic number of them rather than all thirty-odd
    /// thousand. Distances are in world metres, matching the triangle corners.
    /// </remarks>
    public sealed class LightmapBvh
    {
        private readonly int[] _order;
        private readonly Node[] _nodes;
        private readonly Edges[] _edges;

        private LightmapBvh(LightmapTriangle[] triangles, int[] order, Node[] nodes)
        {
            _order = order;
            _nodes = nodes;

            // The corners a leaf actually intersects, copied into traversal order and reduced to what
            // Moeller-Trumbore reads. Two things were costing more than the arithmetic ever did.
            //
            // A leaf's triangles were reached as `_triangles[_order[start + k]]`: a dependent load, then
            // a hundred-and-twelve-byte record picked out of the original array at a scattered index.
            // Four triangles of one leaf could sit anywhere, so a leaf was four scattered lines rather
            // than a run. Here they are contiguous and in the order they are read.
            //
            // And the record carries a target index, six lightmap coordinates and an albedo, none of
            // which an intersection test looks at. Keeping only the corner and the two edges -- already
            // subtracted, since they are the same for every ray -- is seventy-two bytes rather than a
            // hundred and twelve, and no subtractions per test.
            _edges = new Edges[order.Length];

            for (int i = 0; i < order.Length; i++)
            {
                LightmapTriangle triangle = triangles[order[i]];

                _edges[i] = new Edges(
                    triangle.A,
                    new Vector3d(triangle.B.X - triangle.A.X, triangle.B.Y - triangle.A.Y, triangle.B.Z - triangle.A.Z),
                    new Vector3d(triangle.C.X - triangle.A.X, triangle.C.Y - triangle.A.Y, triangle.C.Z - triangle.A.Z));
            }
        }

        /// <summary>A triangle as an intersection test wants it: one corner and the two edges from it.</summary>
        private readonly struct Edges(Vector3d a, Vector3d edge1, Vector3d edge2)
        {
            public readonly Vector3d A = a;
            public readonly Vector3d Edge1 = edge1;
            public readonly Vector3d Edge2 = edge2;
        }

        /// <summary>A node waiting to be visited, with the distance at which the ray enters it.</summary>
        private readonly struct StackEntry(int index, double entry)
        {
            public readonly int Index = index;
            public readonly double Entry = entry;
        }

        private struct Node
        {
            public Vector3d Min;
            public Vector3d Max;
            public int Start;
            public int Count;
            public int Left;
            public int Right;
        }

        /// <summary>Builds a hierarchy over the triangles.</summary>
        /// <param name="triangles">The triangles from <see cref="BuildTriangles"/>.</param>
        /// <returns>The built hierarchy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="triangles"/> is null.</exception>
        public static LightmapBvh Build(LightmapTriangle[] triangles)
        {
            ArgumentNullException.ThrowIfNull(triangles);

            int count = triangles.Length;
            int[] order = new int[count];
            Vector3d[] centroid = new Vector3d[count];
            Vector3d[] triMin = new Vector3d[count];
            Vector3d[] triMax = new Vector3d[count];

            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                LightmapTriangle t = triangles[i];
                triMin[i] = MinVec(MinVec(t.A, t.B), t.C);
                triMax[i] = MaxVec(MaxVec(t.A, t.B), t.C);
                centroid[i] = new Vector3d(
                    (triMin[i].X + triMax[i].X) * 0.5,
                    (triMin[i].Y + triMax[i].Y) * 0.5,
                    (triMin[i].Z + triMax[i].Z) * 0.5);
            }

            List<Node> nodes = [];
            if (count > 0)
            {
                BuildRange(nodes, order, centroid, triMin, triMax, 0, count);
            }

            return new LightmapBvh(triangles, order, [.. nodes]);
        }

        private static int BuildRange(
            List<Node> nodes, int[] order, Vector3d[] centroid, Vector3d[] triMin, Vector3d[] triMax,
            int start, int count)
        {
            Vector3d min = new(double.MaxValue, double.MaxValue, double.MaxValue);
            Vector3d max = new(double.MinValue, double.MinValue, double.MinValue);
            Vector3d centreMin = min;
            Vector3d centreMax = max;

            for (int i = start; i < start + count; i++)
            {
                int tri = order[i];
                min = MinVec(min, triMin[tri]);
                max = MaxVec(max, triMax[tri]);
                centreMin = MinVec(centreMin, centroid[tri]);
                centreMax = MaxVec(centreMax, centroid[tri]);
            }

            int node = nodes.Count;
            nodes.Add(default);

            // A small leaf, or a range whose centroids all coincide (no axis to split on), becomes a leaf.
            double ex = centreMax.X - centreMin.X;
            double ey = centreMax.Y - centreMin.Y;
            double ez = centreMax.Z - centreMin.Z;

            if (count <= 4 || (ex <= 1e-9 && ey <= 1e-9 && ez <= 1e-9))
            {
                nodes[node] = new Node { Min = min, Max = max, Start = start, Count = count, Left = -1, Right = -1 };
                return node;
            }

            int axis = ex >= ey && ex >= ez ? 0 : (ey >= ez ? 1 : 2);
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
                Axis(centroid[a], axis).CompareTo(Axis(centroid[b], axis))));

            int mid = count / 2;
            int left = BuildRange(nodes, order, centroid, triMin, triMax, start, mid);
            int right = BuildRange(nodes, order, centroid, triMin, triMax, start + mid, count - mid);
            nodes[node] = new Node { Min = min, Max = max, Start = 0, Count = 0, Left = left, Right = right };
            return node;
        }

        /// <summary>The nearest triangle a ray meets, with the barycentric coordinates of the hit.</summary>
        /// <param name="origin">The ray start, world metres.</param>
        /// <param name="direction">The ray direction, need not be unit length.</param>
        /// <param name="maxDistance">How far to look, in the same units as <paramref name="direction"/> is long.</param>
        /// <param name="triangle">The hit triangle's index, or −1 for a miss.</param>
        /// <param name="u">The hit's barycentric weight towards <see cref="LightmapTriangle.B"/>.</param>
        /// <param name="v">Its weight towards <see cref="LightmapTriangle.C"/>.</param>
        /// <returns>Whether anything was hit.</returns>
        public bool ClosestHit(Vector3d origin, Vector3d direction, double maxDistance, out int triangle, out double u, out double v)
        {
            triangle = -1;
            u = 0;
            v = 0;

            if (_nodes.Length == 0)
            {
                return false;
            }

            Vector3d inverse = new(1.0 / direction.X, 1.0 / direction.Y, 1.0 / direction.Z);
            double closest = maxDistance;

            // Front to back, and the stack carries the distance at which each node is entered alongside
            // its index. The first version pushed both children in a fixed order and slab-tested them on
            // the way out, which is correct but does almost no pruning: `closest` only tightens once a
            // triangle is actually hit, and visiting the far side of the level first means it tightens
            // last. Testing the children before pushing, visiting the nearer one first, and dropping a
            // node whose entry distance is already beyond the closest hit lets the very first triangle
            // found cut off most of what remains.
            //
            // Exact, not approximate: a node entered at a distance no nearer than the closest hit cannot
            // contain anything nearer, so nothing that could win is skipped.
            // Sized to the depth a median split can reach, not to a round number. The span is
            // zero-initialised on entry -- that is what C# does for a stackalloc unless the assembly
            // opts out -- so every unused entry is a store this ray pays for and never reads. At 128
            // entries of two spans that was a kilobyte and a half of zeroing per ray, against a
            // traversal that visits nine nodes.
            //
            // Sixty-four is past what the build can produce: it halves the range at every level, so a
            // level with a hundred million triangles would still fit inside forty.
            Span<StackEntry> stack = stackalloc StackEntry[64];
            int top = 0;

            ref readonly Node root = ref _nodes[0];

            if (!SlabTest(in root.Min, in root.Max, in origin, in inverse, closest, out double rootEntry))
            {
                return false;
            }

            stack[top] = new StackEntry(0, rootEntry);
            top++;

            while (top > 0)
            {
                top--;

                // Re-checked on the way out as well as on the way in: this node was worth visiting when
                // it was pushed, but a hit found since may have moved the closest distance in front of
                // it.
                if (stack[top].Entry >= closest)
                {
                    continue;
                }

                ref readonly Node node = ref _nodes[stack[top].Index];

                if (node.Count > 0)
                {
                    for (int k = 0; k < node.Count; k++)
                    {
                        int slot = node.Start + k;

                        if (Intersect(origin, direction, in _edges[slot], out double t, out double bu, out double bv)
                            && t > 1e-5 && t < closest)
                        {
                            closest = t;

                            // The caller's index, read only when something is actually hit rather than
                            // once per candidate.
                            triangle = _order[slot];
                            u = bu;
                            v = bv;
                        }
                    }

                    continue;
                }

                ref readonly Node left = ref _nodes[node.Left];
                ref readonly Node right = ref _nodes[node.Right];

                bool takeLeft =
                    SlabTest(in left.Min, in left.Max, in origin, in inverse, closest, out double leftEntry);
                bool takeRight =
                    SlabTest(in right.Min, in right.Max, in origin, in inverse, closest, out double rightEntry);

                if (takeLeft && takeRight)
                {
                    if (top + 2 > stack.Length)
                    {
                        continue;
                    }

                    // The farther child goes on first so the nearer one comes off first.
                    if (leftEntry <= rightEntry)
                    {
                        stack[top++] = new StackEntry(node.Right, rightEntry);
                        stack[top++] = new StackEntry(node.Left, leftEntry);
                    }
                    else
                    {
                        stack[top++] = new StackEntry(node.Left, leftEntry);
                        stack[top++] = new StackEntry(node.Right, rightEntry);
                    }
                }
                else if (takeLeft && top < stack.Length)
                {
                    stack[top++] = new StackEntry(node.Left, leftEntry);
                }
                else if (takeRight && top < stack.Length)
                {
                    stack[top++] = new StackEntry(node.Right, rightEntry);
                }
            }

            return triangle >= 0;
        }

        private static double Axis(Vector3d value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

        private static Vector3d MinVec(Vector3d a, Vector3d b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

        private static Vector3d MaxVec(Vector3d a, Vector3d b) =>
            new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        // The ray-slab test: the ray hits the box if the entry into all three slabs precedes the exit from
        // any of them, and the exit is ahead of the ray and nearer than the current closest hit.
        //
        // Also reports where the ray enters, which is what lets the traversal visit the nearer child
        // first and drop a node the closest hit has already overtaken. Clamped at zero so a box the ray
        // starts inside reports an entry of zero rather than a negative distance, and therefore sorts
        // ahead of everything.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SlabTest(
            in Vector3d min, in Vector3d max, in Vector3d origin, in Vector3d inverse,
            double closest, out double entry)
        {
            double tMin = double.NegativeInfinity;
            double tMax = double.PositiveInfinity;

            Narrow(min.X, max.X, origin.X, inverse.X, ref tMin, ref tMax);
            Narrow(min.Y, max.Y, origin.Y, inverse.Y, ref tMin, ref tMax);
            Narrow(min.Z, max.Z, origin.Z, inverse.Z, ref tMin, ref tMax);

            entry = Math.Max(tMin, 0.0);
            return tMax >= entry && tMin < closest;
        }

        /// <summary>Narrows a ray's live range to one slab of a box.</summary>
        /// <remarks>
        /// <b>The NaN case is a real bug this had, not a hypothetical.</b> A ray parallel to an axis has
        /// a zero in that component of its direction, so the reciprocal is an infinity; if its origin
        /// also lies exactly on one of the box's two planes for that axis, the extent is zero and the
        /// product is zero times infinity, which is NaN. <see cref="Math.Min(double, double)"/> and
        /// <see cref="Math.Max(double, double)"/> propagate NaN, so the range came back NaN, the final
        /// comparison was false, and <b>the node was rejected</b> — the traversal quietly discarded
        /// geometry the ray genuinely meets.
        /// <para>
        /// Checked against a full search over every triangle, that lost 240 of 3,456 axis-aligned rays:
        /// a ray fired straight down from just above the ground reported open sky. In a bake that is a
        /// texel lit that should have been shadowed, on a level whose architecture is almost entirely
        /// axis-aligned, in a result that is cached and never compared against anything.
        /// </para>
        /// <para>
        /// A ray in that position lies exactly on the boundary of the slab, so the slab constrains
        /// nothing and the axis is skipped. Erring towards keeping the node is the safe direction: a
        /// node kept costs a triangle test, a node dropped costs the hit.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Narrow(
            double low, double high, double origin, double inverse, ref double tMin, ref double tMax)
        {
            double t1 = (low - origin) * inverse;
            double t2 = (high - origin) * inverse;

            // Min and Max propagate a NaN operand, so a poisoned axis arrives here as a NaN pair, and
            // MaxNumber and MinNumber are the IEEE forms that return the other operand instead of the
            // NaN -- which is exactly "leave the running range alone". An explicit `if (IsNaN)` measured
            // the same to within noise, so this is written for what it says rather than for speed.
            double near = Math.Min(t1, t2);
            double far = Math.Max(t1, t2);

            tMin = double.MaxNumber(tMin, near);
            tMax = double.MinNumber(tMax, far);
        }

        // Moeller-Trumbore, in double precision because the world coordinates are tens of kilometres from
        // the origin and a single-precision cross product there loses the triangle.
        private static bool Intersect(
            Vector3d origin, Vector3d direction, in Edges tri, out double t, out double u, out double v)
        {
            t = 0;
            u = 0;
            v = 0;

            Vector3d edge1 = tri.Edge1;
            Vector3d edge2 = tri.Edge2;

            Vector3d p = new(
                (direction.Y * edge2.Z) - (direction.Z * edge2.Y),
                (direction.Z * edge2.X) - (direction.X * edge2.Z),
                (direction.X * edge2.Y) - (direction.Y * edge2.X));

            double determinant = (edge1.X * p.X) + (edge1.Y * p.Y) + (edge1.Z * p.Z);

            if (determinant is > -1e-12 and < 1e-12)
            {
                return false;
            }

            double inverse = 1.0 / determinant;
            Vector3d s = new(origin.X - tri.A.X, origin.Y - tri.A.Y, origin.Z - tri.A.Z);
            u = ((s.X * p.X) + (s.Y * p.Y) + (s.Z * p.Z)) * inverse;

            if (u is < 0.0 or > 1.0)
            {
                return false;
            }

            Vector3d q = new(
                (s.Y * edge1.Z) - (s.Z * edge1.Y),
                (s.Z * edge1.X) - (s.X * edge1.Z),
                (s.X * edge1.Y) - (s.Y * edge1.X));

            v = ((direction.X * q.X) + (direction.Y * q.Y) + (direction.Z * q.Z)) * inverse;

            if (v < 0.0 || u + v > 1.0)
            {
                return false;
            }

            t = ((edge2.X * q.X) + (edge2.Y * q.Y) + (edge2.Z * q.Z)) * inverse;
            return t > 0.0;
        }
    }

    /// <summary>Folds the multi-bounce indirect into each texel's radiance (phase 3), reading the light the last pass left.</summary>
    /// <param name="targets">The targets, with <see cref="LightmapTarget.Direct"/> and an initial <see cref="LightmapTarget.Radiance"/> from <see cref="BakeDirectAndSky"/>.</param>
    /// <param name="triangles">The triangles the <paramref name="bvh"/> was built over; a hit's triangle indexes this.</param>
    /// <param name="bvh">The hierarchy the gather rays trace.</param>
    /// <param name="skyColour">The radiance a ray that leaves the level receives.</param>
    /// <param name="passes">How many bounces to accumulate; each reads the last.</param>
    /// <param name="hemisphereRays">Cosine-weighted gather rays per texel.</param>
    /// <param name="work">
    /// Where the texels are gathered: all at once on a scheduler that runs work concurrently, a block a
    /// slice on one that does not.
    /// </param>
    /// <param name="cancellationToken">Cancels a bake in flight.</param>
    /// <returns>The phase, a slice at a time; <see cref="Stepping.Drain"/> runs it outright.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IEnumerable<bool> BakeBounces(
        IReadOnlyList<LightmapTarget> targets,
        LightmapTriangle[] triangles,
        LightmapBvh bvh,
        Vector3f skyColour,
        int passes,
        int hemisphereRays,
        IWorkScheduler work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(bvh);
        ArgumentNullException.ThrowIfNull(work);

        return BounceSlices(targets, triangles, bvh, skyColour, passes, hemisphereRays, work, cancellationToken);
    }

    /// <summary>The bounce passes, checked by <see cref="BakeBounces"/> before they are handed out.</summary>
    private static IEnumerable<bool> BounceSlices(
        IReadOnlyList<LightmapTarget> targets,
        LightmapTriangle[] triangles,
        LightmapBvh bvh,
        Vector3f skyColour,
        int passes,
        int hemisphereRays,
        IWorkScheduler work,
        CancellationToken cancellationToken)
    {
        Vector3f[] hemisphere = CosineHemisphere(Math.Max(hemisphereRays, 1));
        float inverseRays = 1f / hemisphere.Length;
        float biasMetres = (float)(RayBiasUnits / WorldScene.UnitsPerMetre);
        double reachMetres = RayReachMetres;
        TexelBlock[] blocks = TexelBlock.Of(targets);

        for (int pass = 0; pass < Math.Max(passes, 1); pass++)
        {
            // Before the snapshot, not after: it clones every page in the level, and a bake already asked
            // to stop must not allocate a level's worth of radiance to throw away.
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            // The light the last pass left, so this pass reads a stable snapshot rather than racing itself.
            Vector3f[][] previous = new Vector3f[targets.Count][];
            for (int t = 0; t < targets.Count; t++)
            {
                previous[t] = (Vector3f[])targets[t].Radiance.Clone();
            }

            IEnumerable<bool> gathered = Stepping.Range(work, blocks.Length, Parallelism, b =>
            {
                LowerPriority(work);

                TexelBlock block = blocks[b];
                LightmapTarget target = targets[block.Target];

                // As in the direct pass: the token is read per texel as well as per block.
                for (int i = block.From; i < block.To; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    LightmapTexel texel = target.Texels[i];
                    if (!texel.IsCovered)
                    {
                        continue;
                    }

                    Vector3d origin = new(
                        texel.WorldPosition.X + (texel.Normal.X * biasMetres),
                        texel.WorldPosition.Y + (texel.Normal.Y * biasMetres),
                        texel.WorldPosition.Z + (texel.Normal.Z * biasMetres));

                    (Vector3f tangent, Vector3f bitangent) = TangentFrame(texel.Normal);
                    Vector3f gather = default;

                    for (int r = 0; r < hemisphere.Length; r++)
                    {
                        Vector3f local = hemisphere[r];
                        Vector3d dir = new(
                            (tangent.X * local.X) + (bitangent.X * local.Y) + (texel.Normal.X * local.Z),
                            (tangent.Y * local.X) + (bitangent.Y * local.Y) + (texel.Normal.Y * local.Z),
                            (tangent.Z * local.X) + (bitangent.Z * local.Y) + (texel.Normal.Z * local.Z));

                        if (bvh.ClosestHit(origin, dir, reachMetres, out int hitTriangle, out double bu, out double bv))
                        {
                            LightmapTriangle tri = triangles[hitTriangle];
                            double bw = 1.0 - bu - bv;
                            float lightmapU = (float)((bw * tri.U0) + (bu * tri.U1) + (bv * tri.U2));
                            float lightmapV = (float)((bw * tri.V0) + (bu * tri.V1) + (bv * tri.V2));

                            LightmapTarget hitTarget = targets[tri.Target];
                            int hx = Math.Clamp((int)(lightmapU * hitTarget.Width), 0, hitTarget.Width - 1);
                            int hy = Math.Clamp((int)(lightmapV * hitTarget.Height), 0, hitTarget.Height - 1);
                            Vector3f incoming = previous[tri.Target][(hy * hitTarget.Width) + hx];

                            gather = new Vector3f(
                                gather.X + (tri.Albedo.X * incoming.X),
                                gather.Y + (tri.Albedo.Y * incoming.Y),
                                gather.Z + (tri.Albedo.Z * incoming.Z));
                        }
                        else
                        {
                            gather = new Vector3f(gather.X + skyColour.X, gather.Y + skyColour.Y, gather.Z + skyColour.Z);
                        }
                    }

                    Vector3f direct = target.Direct[i];
                    target.Radiance[i] = new Vector3f(
                        direct.X + (gather.X * inverseRays),
                        direct.Y + (gather.Y * inverseRays),
                        direct.Z + (gather.Z * inverseRays));
                }
            },
            cancellationToken);

            foreach (bool slice in gathered)
            {
                yield return slice;
            }
        }
    }

    /// <summary>Smooths the baked radiance with an edge-aware à-trous filter, taming the ray tracer's noise.</summary>
    /// <param name="targets">The baked targets, their <see cref="LightmapTarget.Radiance"/> filled.</param>
    /// <param name="iterations">How many à-trous passes, each at twice the previous spacing.</param>
    /// <param name="positionSigmaMetres">How far, in metres, radiance is allowed to blur across a flat surface.</param>
    /// <param name="normalPower">How sharply the filter stops at a change of surface orientation; higher keeps edges.</param>
    /// <remarks>
    /// <para>
    /// A per-texel Monte-Carlo estimate is noisy, and more rays only buy so much before the cost is not worth
    /// it; the standard answer a baker reaches for is a denoise that averages a texel with its neighbours but
    /// refuses to blur across the geometry the atlas packs side by side. This is that: a joint bilateral
    /// à-trous, weighting each neighbour by the B3-spline kernel, by how near it sits in the WORLD (so two
    /// islands that touch in the atlas but face different walls do not bleed into each other), and by how
    /// alike its normal is (so a convex edge stays an edge). Uncovered texels are left alone and never
    /// contribute.
    /// </para>
    /// <para>
    /// Runs on the bake worker after the bounces and before the pages are composited, so the smoothing is
    /// baked into the cached result and costs nothing at runtime.
    /// </para>
    /// </remarks>
    /// <param name="work">Where the pages are filtered: all at once where the scheduler can, a page a slice where not.</param>
    /// <param name="cancellationToken">Stops the filter part-way, for a level left during its bake.</param>
    /// <returns>The filter, a slice at a time; <see cref="Stepping.Drain"/> runs it outright.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targets"/> or <paramref name="work"/> is null.</exception>
    public static IEnumerable<bool> Denoise(
        IReadOnlyList<LightmapTarget> targets,
        IWorkScheduler work,
        int iterations = 4,
        float positionSigmaMetres = 0.35f,
        float normalPower = 48f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(work);

        // A page at a time: the filter spreads each texel over its neighbours, so a page is the smallest
        // piece it divides into.
        return Stepping.Range(
            work,
            iterations > 0 ? targets.Count : 0,
            Parallelism,
            t => DenoiseTarget(targets[t], iterations, positionSigmaMetres, normalPower),
            cancellationToken);
    }

    // The B3-spline row an à-trous filter uses along each axis; the 2D kernel is its outer product.
    private static readonly float[] AtrousKernel = [1f / 16f, 1f / 4f, 3f / 8f, 1f / 4f, 1f / 16f];

    private static void DenoiseTarget(LightmapTarget target, int iterations, float positionSigmaMetres, float normalPower)
    {
        int width = target.Width;
        int height = target.Height;
        float inversePositionVariance = 1f / (2f * positionSigmaMetres * positionSigmaMetres);

        Vector3f[] source = target.Radiance;
        Vector3f[] destination = new Vector3f[source.Length];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int step = 1 << iteration;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int centre = (y * width) + x;
                    if (!target.Texels[centre].IsCovered)
                    {
                        destination[centre] = source[centre];
                        continue;
                    }

                    Vector3d centrePosition = target.Texels[centre].WorldPosition;
                    Vector3f centreNormal = target.Texels[centre].Normal;

                    Vector3f sum = default;
                    float weightSum = 0f;

                    for (int ky = 0; ky < 5; ky++)
                    {
                        int sy = y + ((ky - 2) * step);
                        if (sy < 0 || sy >= height)
                        {
                            continue;
                        }

                        for (int kx = 0; kx < 5; kx++)
                        {
                            int sx = x + ((kx - 2) * step);
                            if (sx < 0 || sx >= width)
                            {
                                continue;
                            }

                            int sample = (sy * width) + sx;
                            if (!target.Texels[sample].IsCovered)
                            {
                                continue;
                            }

                            Vector3d samplePosition = target.Texels[sample].WorldPosition;
                            double dx = samplePosition.X - centrePosition.X;
                            double dy = samplePosition.Y - centrePosition.Y;
                            double dz = samplePosition.Z - centrePosition.Z;
                            float positionWeight = MathF.Exp(-(float)((dx * dx) + (dy * dy) + (dz * dz)) * inversePositionVariance);

                            Vector3f sampleNormal = target.Texels[sample].Normal;
                            float alignment = (centreNormal.X * sampleNormal.X)
                                + (centreNormal.Y * sampleNormal.Y)
                                + (centreNormal.Z * sampleNormal.Z);
                            if (alignment <= 0f)
                            {
                                continue;
                            }

                            float weight = AtrousKernel[kx] * AtrousKernel[ky] * positionWeight * MathF.Pow(alignment, normalPower);

                            Vector3f radiance = source[sample];
                            sum = new Vector3f(sum.X + (radiance.X * weight), sum.Y + (radiance.Y * weight), sum.Z + (radiance.Z * weight));
                            weightSum += weight;
                        }
                    }

                    destination[centre] = weightSum > 1e-6f
                        ? new Vector3f(sum.X / weightSum, sum.Y / weightSum, sum.Z / weightSum)
                        : source[centre];
                }
            }

            (source, destination) = (destination, source);
        }

        // Ensure the smoothed result lands in the target's own array, whichever buffer the ping-pong ended on.
        if (!ReferenceEquals(source, target.Radiance))
        {
            Array.Copy(source, target.Radiance, source.Length);
        }
    }

    /// <summary>Renders a target's normals as an image, for verifying the texel map before any light is traced.</summary>
    /// <param name="target">The target whose texels to draw.</param>
    /// <param name="destination">Receives the image, one pixel per texel, row-major.</param>
    /// <remarks>
    /// Tangent-space would need a basis; this is the raw world normal packed <c>n*0.5+0.5</c>, so a correct
    /// map reads as smoothly varying colour over each face and hard changes at edges -- and a gutter texel,
    /// which no triangle covered, comes out black. That is enough to see the unwrap is right.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void DumpTargetNormals(LightmapTarget target, Span<Bgra32> destination)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (destination.Length < target.Texels.Length)
        {
            throw new ArgumentException(
                $"A {target.Width}x{target.Height} page needs {target.Texels.Length} pixels, got {destination.Length}.",
                nameof(destination));
        }

        for (int i = 0; i < target.Texels.Length; i++)
        {
            LightmapTexel texel = target.Texels[i];

            if (!texel.IsCovered)
            {
                destination[i] = new Bgra32(0, 0, 0, 255);
                continue;
            }

            destination[i] = new Bgra32(
                r: Quantise((texel.Normal.X * 0.5f) + 0.5f),
                g: Quantise((texel.Normal.Y * 0.5f) + 0.5f),
                b: Quantise((texel.Normal.Z * 0.5f) + 0.5f),
                a: 255);
        }
    }

    /// <summary>Renders a target's baked radiance as an image, for verifying the direct+sky pass.</summary>
    /// <param name="target">The target whose radiance to draw.</param>
    /// <param name="destination">Receives the image, one pixel per texel, row-major.</param>
    /// <param name="exposure">A linear scale before display encoding, so a dim or bright bake can be read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void DumpTargetRadiance(LightmapTarget target, Span<Bgra32> destination, float exposure = 1f)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (destination.Length < target.Radiance.Length)
        {
            throw new ArgumentException(
                $"A {target.Width}x{target.Height} page needs {target.Radiance.Length} pixels, got {destination.Length}.",
                nameof(destination));
        }

        for (int i = 0; i < target.Radiance.Length; i++)
        {
            Vector3f value = target.Radiance[i];
            destination[i] = new Bgra32(
                r: Quantise(value.X * exposure),
                g: Quantise(value.Y * exposure),
                b: Quantise(value.Z * exposure),
                a: 255);
        }
    }

    private static byte Quantise(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
