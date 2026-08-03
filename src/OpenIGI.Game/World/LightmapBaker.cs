using System.Threading;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
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
    /// <param name="cancellationToken">Cancels a bake in flight.</param>
    /// <exception cref="ArgumentNullException"><paramref name="targets"/> or <paramref name="cast"/> is null.</exception>
    public static void BakeDirectAndSky(
        IReadOnlyList<LightmapTarget> targets,
        WorldRayCast cast,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        int hemisphereRays = 64,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(cast);

        Vector3f[] hemisphere = CosineHemisphere(Math.Max(hemisphereRays, 1));
        bool hasSun = (sunDirection.X * sunDirection.X)
            + (sunDirection.Y * sunDirection.Y)
            + (sunDirection.Z * sunDirection.Z) > 1e-6f;

        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
        };

        Parallel.For(0, targets.Count, options, t =>
        {
            LightmapTarget target = targets[t];

            for (int i = 0; i < target.Texels.Length; i++)
            {
                LightmapTexel texel = target.Texels[i];

                if (texel.IsCovered)
                {
                    target.Radiance[i] = ShadeTexel(texel, cast, hasSun, sunDirection, sunColour, skyColour, hemisphere);
                }
            }
        });
    }

    // The light one texel receives: the sun if the shadow ray to it is clear, plus the share of the sky
    // hemisphere the cosine-weighted rays find open. Both start a little off the surface along its normal,
    // or a ray would begin inside the texel's own triangle and report it occluded.
    private static Vector3f ShadeTexel(
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
        return new Vector3f(
            result.X + (skyColour.X * skyVisibility),
            result.Y + (skyColour.Y * skyVisibility),
            result.Z + (skyColour.Z * skyVisibility));
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
