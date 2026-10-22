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
/// (the second UV set format-1 and format-3 meshes carry), so every texel is
/// a real surface point that can be path traced to a coloured, high-resolution, multi-bounce answer. It
/// is the right trade for I.G.I.: the geometry is static and the sun is fixed per mission, so the whole
/// thing bakes once and is cached.
/// </para>
/// <para>
/// <b>Phase 1 (this file): coloured geometry and the texel map.</b> For every lightmapped surface it
/// rasterises the draw record's triangles into its lightmap page's UV space and records, per covered
/// texel, the world position and normal it belongs to -- the map every later phase traces from. The
/// tracing (direct sun and sky, then the multi-bounce colour bleed), the BVH it traces against, seam
/// dilation and the runtime swap are the following phases. A <see cref="DumpTargetNormals"/> is here so
/// the texel map can be verified on its own -- a correct one reads as a UV-space unwrap of the object with
/// its world normals -- before any light is traced.
/// </para>
/// </remarks>
public static class LightmapBaker
{
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
                LightmapRasteriser.RasteriseRecord(placement, vertices, record, target);
                targets.Add(target);
            }
        }

        return targets;
    }

    /// <summary>How far off a surface, in world units, a ray starts, so it does not hit its own texel.</summary>
    internal const double RayBiasUnits = 96.0;

    /// <summary>How far, in metres, a ray looks before a direction counts as open (sun or sky).</summary>
    internal const float RayReachMetres = 60f;

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

        LightmapTexelBlock[] blocks = LightmapTexelBlock.Of(targets);

        return Stepping.Range(
            work,
            blocks.Length,
            Parallelism,
            b =>
            {
                LowerPriority(work);

                LightmapTexelBlock block = blocks[b];
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
    internal static int Parallelism => Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Puts a worker below the render thread, where the bake runs beside the frame at all.</summary>
    /// <remarks>
    /// So the bake yields to a frame that needs a core rather than starving it. Set once per pooled thread;
    /// the guard skips the call after the first piece it runs.
    /// </remarks>
    internal static void LowerPriority(IWorkScheduler work)
    {
        if (work.IsConcurrent && Thread.CurrentThread.Priority == ThreadPriority.Normal)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
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
    internal static (Vector3f Tangent, Vector3f Bitangent) TangentFrame(Vector3f normal)
    {
        Vector3f reference = MathF.Abs(normal.Z) < 0.9f ? new Vector3f(0f, 0f, 1f) : new Vector3f(1f, 0f, 0f);
        Vector3f tangent = (Vector3f.Cross(reference, normal)).NormalizedOr(Vector3f.UnitZ, 1e-8f);
        return (tangent, Vector3f.Cross(normal, tangent));
    }

    // A cosine-weighted set of hemisphere directions in local space (z up), spaced by the golden angle so
    // they never fall into rows. Cosine-weighted because irradiance is the cosine-weighted integral of
    // incoming light, so a plain average of these estimates it.
    internal static Vector3f[] CosineHemisphere(int count)
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
                MefVertex va = LightmapRasteriser.ResolveVertex(vertices, record, indices[i]);
                MefVertex vb = LightmapRasteriser.ResolveVertex(vertices, record, indices[i + 1]);
                MefVertex vc = LightmapRasteriser.ResolveVertex(vertices, record, indices[i + 2]);

                triangles.Add(new LightmapTriangle(
                    LightmapRasteriser.ToWorldMetres(placement, va.Position),
                    LightmapRasteriser.ToWorldMetres(placement, vb.Position),
                    LightmapRasteriser.ToWorldMetres(placement, vc.Position),
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
                colour = MeanColour(TexFile.LoadLevelZero(data.Span));
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

    // Averages a decoded texture into a single albedo colour, in linear light as the bounce uses it: each
    // texel decoded before it is summed, since the mean of encoded bytes reads paler and greyer than the
    // texture. I.G.I.'s textures carry some painted-in lighting so this over-reads the true reflectance a
    // little, but it is the HUE that matters for a coloured bounce, and the bounce's own scale carries the
    // magnitude.
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
            r += SrgbCurve.ToLinear(pixel.R);
            g += SrgbCurve.ToLinear(pixel.G);
            b += SrgbCurve.ToLinear(pixel.B);
        }

        return new Vector3f((float)(r / count), (float)(g / count), (float)(b / count));
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

        return LightmapBounceBaker.BounceSlices(
            targets, triangles, bvh, skyColour, passes, hemisphereRays, work, cancellationToken);
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
            t => LightmapDenoiser.DenoiseTarget(targets[t], iterations, positionSigmaMetres, normalPower),
            cancellationToken);
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
