using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Proposes where a level's lights are, by finding every place it draws a texture the artists treated
/// as a source.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where this sits.</b> <see cref="EmissiveTextureSurvey"/> answers "which textures are lights" and
/// stops there — on level 1 it answers <c>236_01_1</c>, thirty uses, baked pure white on every one.
/// That is a name, not a place. This is the other half: given the names, walk the level's placements
/// and their geometry the same way the survey does, take every triangle whose subset draws with one of
/// those textures, and put it in world space. What comes out is a cloud of triangles that are lamps,
/// and the remaining question is how many lamps.
/// </para>
/// <para>
/// <b>Why clustering is the whole problem.</b> A single wall lamp is a dozen triangles, and averaging
/// all of a texture's triangles together would put one light in the middle of the level. Averaging none
/// of them would put a light on every triangle, which for a strip light is a row of a hundred
/// overlapping sources costing a hundred times what one costs. The useful answer is between the two,
/// and it is found by asking which triangles are near enough to be the same object — see
/// <see cref="DefaultClusterDistance"/> for what "near enough" means and why it is a gap rather than a
/// size.
/// </para>
/// <para>
/// <b>The output is a starting list for a human to review.</b> This inherits the survey's stance
/// deliberately, and stacks another inference on top of it: the survey says which textures are worth
/// looking at, and this says where each one is drawn — neither says that the thing found is a lamp. A
/// bright reflective sign, a window frame catching the sun in the bake, a decal on a crate: all of them
/// come out of here as proposed fixtures, and someone still has to look. Hence
/// <see cref="Propose(IEnumerable{LightFixtureExtractor.PlacedMesh}, IEnumerable{string}, Func{string, Vector3f}?, float, float)"/>
/// rather than <c>Find</c>, and hence <see cref="LightFixture.TriangleCount"/> being carried through to
/// the reviewer.
/// </para>
/// </remarks>
public static class LightFixtureExtractor
{
    /// <summary>How far apart two emitter triangles may be and still belong to the same fixture, in metres.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is a gap, not a size.</b> Triangles are joined transitively, so a fluorescent tube five
    /// metres long stays one fixture as long as no two consecutive triangles along it are further apart
    /// than this. What the threshold has to separate is the gap <em>within</em> one lamp from the gap
    /// <em>between</em> two lamps — and those two distances are far apart in these levels. A lamp
    /// housing's triangles are tens of centimetres from each other at most; two lamps face each other
    /// across a corridor several metres wide, or sit a bay apart along a wall.
    /// </para>
    /// <para>
    /// 1.5 m sits in that gap with room on both sides, and errs towards splitting rather than merging.
    /// That is the safer direction: two proposed lights where there is one is visible in a review and
    /// costs a duplicate, whereas one light hovering in the middle of a room because two wall lamps
    /// merged looks perfectly reasonable in the list and wrong in the frame.
    /// </para>
    /// <para>
    /// <b>The known limit of measuring between centroids.</b> The distance is between triangle centres,
    /// not between their surfaces, so a single triangle spanning much more than the threshold reads as
    /// far from its own neighbour — a four-metre panel modelled as two triangles has 1.9 m between their
    /// centroids and splits in half. Accounting for each triangle's own size would fix that at the cost
    /// of an unbounded search radius in the grid below, and the geometry that provokes it is rare: lamps
    /// are small, and large emissive panels in these levels are tessellated. It is left as it is, and
    /// the threshold is a parameter for the level where it is not.
    /// </para>
    /// </remarks>
    public const float DefaultClusterDistance = 1.5f;

    /// <summary>How far a fixture's light reaches, as a multiple of the fixture's own radius.</summary>
    /// <remarks>
    /// <para>
    /// The radius is <c>extent × scale × √luminance</c>, and the square root is the one part of it that
    /// is derived rather than chosen. Cut an inverse-square falloff off at a fixed threshold and the
    /// cut-off distance goes as the square root of the emitted power; power goes as area times
    /// radiance, so a fixture twice as bright reaches √2 times as far. Scaling on the fixture's own
    /// extent rather than on its area then keeps a long thin strip light from claiming the reach of a
    /// compact source with the same total area.
    /// </para>
    /// <para>
    /// <b>The multiplier itself is a guess.</b> The retail data records no radiance for any of this —
    /// the bakes are the only evidence, and they record where the light landed rather than how strong
    /// it was. 12 puts a typical wall fixture, a quarter of a metre across, in charge of the three or
    /// four metres around it, which is about what the lightmaps in those corridors show. It is a number
    /// to start looking from, not a measurement, and it is a parameter for that reason.
    /// </para>
    /// </remarks>
    public const float DefaultRadiusScale = 12f;

    /// <summary>The smallest influence radius any proposed fixture is given, in metres.</summary>
    /// <remarks>
    /// A floor rather than a correction. A lamp modelled as a single small quad has an extent of a few
    /// centimetres, and the scaled radius would then be smaller than the lamp's own housing — a light
    /// that lights nothing, which reads in a review as "the extractor missed it" rather than as "the
    /// geometry is small". Two metres is close enough to nothing that a false positive stays cheap.
    /// </remarks>
    public const float MinimumRadius = 2f;

    private const string LogCategory = "Emissive";

    /// <summary>One drawable mesh and where in the world it stands.</summary>
    /// <remarks>
    /// <b>Plain geometry and a transform, rather than a <see cref="WorldPlacement"/>.</b> A placement
    /// names a <c>MefModel</c>, a type only a real pack file can construct, so a test that wanted to
    /// check the arithmetic would first have to load a level. Taking the mesh and the transform
    /// directly puts every decision this class makes — which triangles count, where they land, how they
    /// group — behind an argument a test can build by hand, and leaves the scene walk with nothing in
    /// it but a loop.
    /// </remarks>
    /// <param name="Geometry">The mesh, with positions in metres in the model's own space.</param>
    /// <param name="Position">Where the model stands, in world space and in metres.</param>
    /// <param name="Orientation">The orientation the renderer draws it with.</param>
    /// <param name="TextureForSlot">
    /// Resolves a subset's material slot to a texture name, or returns <see langword="null"/> when the
    /// slot names nothing. Supplied by the caller because the mapping lives on the drawing instance
    /// rather than on the mesh.
    /// </param>
    public readonly record struct PlacedMesh(
        MeshGeometry Geometry,
        Vector3d Position,
        Matrix3f Orientation,
        Func<int, string?> TextureForSlot);

    /// <summary>Proposes a light for every cluster of emitter triangles among the given meshes.</summary>
    /// <param name="meshes">The drawable meshes and where each one stands.</param>
    /// <param name="emitterTextures">
    /// The texture names confirmed to be light sources, compared case-insensitively. Typically the top
    /// of an <see cref="EmissiveTextureSurvey"/> ranking after a human has looked at it.
    /// </param>
    /// <param name="colour">
    /// Supplies an emitter texture's colour, linear and normalised so its brightest channel is around
    /// one. <see langword="null"/> gives every fixture white, which is the right default when the
    /// caller has not decoded the textures: it costs the colour but not the placement, and the
    /// placement is the part that cannot be guessed later.
    /// </param>
    /// <param name="clusterDistance">The joining distance in metres; see <see cref="DefaultClusterDistance"/>.</param>
    /// <param name="radiusScale">The reach multiplier; see <see cref="DefaultRadiusScale"/>.</param>
    /// <returns>The proposed fixtures, strongest first. Empty when nothing draws an emitter texture.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="meshes"/> or <paramref name="emitterTextures"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="clusterDistance"/> is not positive, or <paramref name="radiusScale"/> is negative.</exception>
    public static IReadOnlyList<LightFixture> Propose(
        IEnumerable<PlacedMesh> meshes,
        IEnumerable<string> emitterTextures,
        Func<string, Vector3f>? colour = null,
        float clusterDistance = DefaultClusterDistance,
        float radiusScale = DefaultRadiusScale)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(emitterTextures);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clusterDistance);
        ArgumentOutOfRangeException.ThrowIfNegative(radiusScale);

        HashSet<string> emitters = new(emitterTextures, StringComparer.OrdinalIgnoreCase);

        if (emitters.Count == 0)
        {
            return [];
        }

        List<Triangle> triangles = [];

        foreach (PlacedMesh mesh in meshes)
        {
            Gather(mesh, emitters, triangles);
        }

        if (triangles.Count == 0)
        {
            return [];
        }

        List<LightFixture> fixtures = [];

        foreach (List<int> cluster in Cluster(triangles, clusterDistance))
        {
            fixtures.Add(Describe(triangles, cluster, colour, radiusScale));
        }

        // Strongest first, because the list exists to be read top-down by someone deciding which
        // proposals are worth keeping. The position breaks ties so that two identical fixtures on
        // opposite walls always come out in the same order -- the engine's determinism contract applies
        // to a tool's output as much as to a frame's.
        fixtures.Sort(Compare);

        return fixtures;
    }

    /// <summary>Proposes a light for every cluster of a level's detected lamp sub-meshes.</summary>
    /// <param name="scene">The loaded level.</param>
    /// <param name="lamps">The lamps the detector found: which textures, and which sub-meshes.</param>
    /// <param name="colour">Supplies an emitter texture's colour, or <see langword="null"/> for white.</param>
    /// <param name="clusterDistance">The joining distance in metres; see <see cref="DefaultClusterDistance"/>.</param>
    /// <param name="radiusScale">The reach multiplier; see <see cref="DefaultRadiusScale"/>.</param>
    /// <param name="logger">Where to report how many fixtures were proposed.</param>
    /// <returns>The proposed fixtures, strongest first.</returns>
    /// <remarks>
    /// The sub-mesh list is what keeps a light out of the furniture. A texture is shared — the page that
    /// skins a ceiling tube also skins the panel it hangs from and, in level 1, an office cabinet — so
    /// gathering every triangle that merely DRAWS an emitter texture proposes fixtures inside things that
    /// are not fixtures. Only the sub-meshes the shape test passed contribute.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<LightFixture> ProposeFromScene(
        WorldScene scene,
        LampDetector.Lamps lamps,
        Func<string, Vector3f>? colour = null,
        float clusterDistance = DefaultClusterDistance,
        float radiusScale = DefaultRadiusScale,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return ProposeFromScene(
            scene,
            lamps.Textures ?? (IReadOnlySet<string>)new HashSet<string>(),
            colour,
            clusterDistance,
            radiusScale,
            logger,
            lamps);
    }

    /// <summary>Proposes a light for every cluster of emitter triangles in a loaded level.</summary>
    /// <param name="scene">The loaded level.</param>
    /// <param name="emitterTextures">The texture names confirmed to be light sources.</param>
    /// <param name="colour">Supplies an emitter texture's colour, or <see langword="null"/> for white.</param>
    /// <param name="clusterDistance">The joining distance in metres; see <see cref="DefaultClusterDistance"/>.</param>
    /// <param name="radiusScale">The reach multiplier; see <see cref="DefaultRadiusScale"/>.</param>
    /// <param name="logger">Where to report how many fixtures were proposed.</param>
    /// <param name="lamps">
    /// Which sub-meshes are fixtures, when the caller knows. Default means it does not, and every
    /// triangle drawing one of <paramref name="emitterTextures"/> counts — which is what a test that
    /// hands this a texture name and nothing else expects.
    /// </param>
    /// <returns>The proposed fixtures, strongest first.</returns>
    /// <remarks>
    /// A convenience layer over
    /// <see cref="Propose(IEnumerable{PlacedMesh}, IEnumerable{string}, Func{string, Vector3f}?, float, float)"/>
    /// and nothing more. It resolves each placement to a mesh, builds a model's geometry once however
    /// many times the level draws it — a level draws the same lamp dozens of times, and the mesh is a
    /// property of the model rather than of where it stands — and hands the rest over.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="emitterTextures"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<LightFixture> ProposeFromScene(
        WorldScene scene,
        IEnumerable<string> emitterTextures,
        Func<string, Vector3f>? colour = null,
        float clusterDistance = DefaultClusterDistance,
        float radiusScale = DefaultRadiusScale,
        IOpenIgiLogger? logger = null,
        LampDetector.Lamps lamps = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(emitterTextures);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        Dictionary<MefModel, MeshGeometry?> built = [];
        List<PlacedMesh> meshes = [];

        foreach (WorldPlacement placement in scene.Placements)
        {
            // A spline segment carries its own bent mesh, which is unique to the placement and must not
            // go in the shared cache; everything else is the model's own geometry, built once.
            MeshGeometry? geometry = placement.BentGeometry;

            if (geometry is null && !built.TryGetValue(placement.Model, out geometry))
            {
                geometry = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
                built[placement.Model] = geometry;
            }

            if (geometry is null)
            {
                continue;
            }

            WorldPlacement drawn = placement;

            bool known = lamps.SubMeshes is not null;

            meshes.Add(new PlacedMesh(
                geometry,
                placement.Position,
                placement.Orientation,
                slot => (!known || lamps.Contains(drawn.Instance.Name, slot))
                    && drawn.Pack.TryGetTextureName(drawn.Instance, slot, out string name)
                        ? name
                        : null));
        }

        IReadOnlyList<LightFixture> fixtures =
            Propose(meshes, emitterTextures, colour, clusterDistance, radiusScale);

        log.Information(
            LogCategory,
            $"Proposed {fixtures.Count} light fixture(s) from {meshes.Count} placement(s). "
            + "Every one of them is a candidate for review, not a light the level declared.");

        return fixtures;
    }

    /// <summary>Collects one mesh's emitter triangles, in world space.</summary>
    private static void Gather(in PlacedMesh mesh, HashSet<string> emitters, List<Triangle> into)
    {
        if (mesh.Geometry is null || mesh.TextureForSlot is null)
        {
            return;
        }

        Vertex3D[] vertices = mesh.Geometry.Vertices;
        ushort[] indices = mesh.Geometry.Indices;

        foreach (MeshSubset subset in mesh.Geometry.Subsets)
        {
            if (subset.IndexCount < 3 || !subset.HasMaterial)
            {
                continue;
            }

            if (mesh.TextureForSlot(subset.MaterialSlot) is not { } texture || !emitters.Contains(texture))
            {
                continue;
            }

            // Clamped rather than trusted. A subset's range comes out of a pack file, and a malformed
            // one should cost the triangles past the end of the buffer rather than the whole survey.
            int start = Math.Max(subset.IndexOffset, 0);
            int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

            for (int at = start; at + 2 < end; at += 3)
            {
                int a = indices[at];
                int b = indices[at + 1];
                int c = indices[at + 2];

                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    continue;
                }

                into.Add(new Triangle(
                    texture,
                    ToWorld(vertices[a].Position, mesh),
                    ToWorld(vertices[b].Position, mesh),
                    ToWorld(vertices[c].Position, mesh)));
            }
        }
    }

    /// <summary>Takes a model-space position into world space.</summary>
    /// <remarks>
    /// The same composition <c>SceneDistanceField</c> uses, and it has to stay the same: a fixture
    /// placed by a different convention from the one the renderer draws its own lamp with sits somewhere
    /// the player can see is wrong. The orientation is orthonormal and applied as a row-vector
    /// multiply, so drawing with it means transposing it.
    /// </remarks>
    private static Vector3d ToWorld(Vector3f local, in PlacedMesh mesh)
    {
        Vector3d rotated = mesh.Orientation.Transposed().Transform(new Vector3d(local.X, local.Y, local.Z));

        return new Vector3d(
            mesh.Position.X + rotated.X,
            mesh.Position.Y + rotated.Y,
            mesh.Position.Z + rotated.Z);
    }

    /// <summary>Groups triangles that are near enough to each other to be one fixture.</summary>
    /// <remarks>
    /// <para>
    /// Union-find over a spatial hash. The grid's cells are exactly the joining distance across, so
    /// every triangle near enough to join is in this cell or one of the twenty-six around it, and the
    /// pass costs a constant per triangle instead of the square of the count. Nothing more elaborate
    /// earns its keep here: the input is the few hundred triangles a level's lamps amount to, not the
    /// level.
    /// </para>
    /// <para>
    /// Joining is transitive by construction, which is the property that makes the threshold a statement
    /// about gaps rather than about sizes — a long fixture stays whole however much longer it is than
    /// the distance, provided it has no hole in it that big.
    /// </para>
    /// </remarks>
    private static List<List<int>> Cluster(List<Triangle> triangles, float clusterDistance)
    {
        int count = triangles.Count;
        int[] parent = new int[count];
        Vector3d[] centroids = new Vector3d[count];

        for (int i = 0; i < count; i++)
        {
            parent[i] = i;
            centroids[i] = triangles[i].Centroid;
        }

        double cell = clusterDistance;
        double limit = (double)clusterDistance * clusterDistance;
        Dictionary<(int X, int Y, int Z), List<int>> grid = [];

        for (int i = 0; i < count; i++)
        {
            Vector3d at = centroids[i];
            (int x, int y, int z) = ((int)Math.Floor(at.X / cell), (int)Math.Floor(at.Y / cell), (int)Math.Floor(at.Z / cell));

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((x + dx, y + dy, z + dz), out List<int>? bucket))
                        {
                            continue;
                        }

                        foreach (int other in bucket)
                        {
                            if (Vector3d.DistanceSquared(at, centroids[other]) <= limit)
                            {
                                Union(parent, i, other);
                            }
                        }
                    }
                }
            }

            if (!grid.TryGetValue((x, y, z), out List<int>? own))
            {
                own = [];
                grid[(x, y, z)] = own;
            }

            own.Add(i);
        }

        // First-seen order rather than the dictionary's, so the grouping does not depend on hashing.
        Dictionary<int, int> group = [];
        List<List<int>> clusters = [];

        for (int i = 0; i < count; i++)
        {
            int root = Find(parent, i);

            if (!group.TryGetValue(root, out int index))
            {
                index = clusters.Count;
                group[root] = index;
                clusters.Add([]);
            }

            clusters[index].Add(i);
        }

        return clusters;
    }

    /// <summary>Turns one cluster of triangles into the fixture it proposes.</summary>
    private static LightFixture Describe(
        List<Triangle> triangles,
        List<int> cluster,
        Func<string, Vector3f>? colour,
        float radiusScale)
    {
        double area = 0.0;
        Vector3d weighted = default;
        Vector3d plain = default;

        foreach (int index in cluster)
        {
            Triangle triangle = triangles[index];
            double own = triangle.Area;
            Vector3d centroid = triangle.Centroid;

            area += own;
            weighted += centroid * own;
            plain += centroid;
        }

        // Weighted by area, because a lamp is often tessellated unevenly -- a housing modelled as one
        // large quad and a dozen slivers along its rim would drag an unweighted mean onto the rim. The
        // fallback matters for degenerate geometry only, where every triangle has zero area and the
        // weighted sum carries no information at all.
        Vector3d position = area > 0.0
            ? weighted * (1.0 / area)
            : plain * (1.0 / cluster.Count);

        double extent = 0.0;
        List<(string Texture, double Area, int Count)> byTexture = [];

        // The cluster's facing, accumulated as the SUM of the raw face cross products. Each one has the
        // triangle's own area for its length, so the sum is area-weighted without weighting anything by
        // hand, and its length against the sum of the individual lengths is exactly how coherently the
        // faces agree -- which is the test for whether this fixture faces anywhere at all.
        Vector3d facing = default;
        double facingArea = 0.0;

        foreach (int index in cluster)
        {
            Triangle triangle = triangles[index];

            extent = Math.Max(extent, Vector3d.DistanceSquared(position, triangle.A));
            extent = Math.Max(extent, Vector3d.DistanceSquared(position, triangle.B));
            extent = Math.Max(extent, Vector3d.DistanceSquared(position, triangle.C));

            Accumulate(byTexture, triangle.Texture, triangle.Area);

            // From the WINDING, not from the authored vertex normals. This game's normals face inward on
            // interior shells and, smoothed across a box, tilt away from each face, so a flat roof built
            // from them would not light as flat.
            Vector3d cross = Vector3d.Cross(triangle.B - triangle.A, triangle.C - triangle.A);

            facing += cross;
            facingArea += cross.Length;
        }

        extent = Math.Sqrt(extent);

        string texture = Dominant(byTexture);
        Vector3f tint = Normalise(colour is null ? new Vector3f(1f, 1f, 1f) : colour(texture));

        Vector3f direction = Facing(facing, facingArea);
        (Vector3f axis, float halfLength, float halfWidth) = EmittingRect(triangles, cluster, position, direction);

        LightFixture fixture = new(
            texture,
            position,
            0f,
            tint,
            (float)area,
            (float)extent,
            cluster.Count,
            direction,
            axis,
            halfLength,
            halfWidth);

        // The radius wants the fixture's brightness, and Luminance is the fixture's own definition of
        // it -- computed here rather than inline so the two can never disagree.
        float reach = (float)extent * radiusScale * MathF.Sqrt(MathF.Max(fixture.Luminance, 0f));

        return fixture with { Radius = MathF.Max(reach, MinimumRadius) };
    }

    /// <summary>Measures the rectangle a facing fixture emits from.</summary>
    /// <remarks>
    /// <para>
    /// The long axis is the principal axis of the cluster's vertices in the plane square to the facing,
    /// each triangle's corners weighted by a third of its area, so a batten's rect runs along its tube
    /// whatever angle it was placed at. The half-extents are how far the vertices reach along that axis and
    /// across it, measured from the fixture's centroid -- the point the lamp is lit from -- so the rect
    /// covers the glass the light comes out of and nothing beside it.
    /// </para>
    /// <para>
    /// Nothing for a fixture that faces nowhere: the renderer lights and shadows that as a point.
    /// </para>
    /// </remarks>
    private static (Vector3f Axis, float HalfLength, float HalfWidth) EmittingRect(
        List<Triangle> triangles,
        List<int> cluster,
        Vector3d centre,
        Vector3f facing)
    {
        double facingLength = Math.Sqrt((facing.X * facing.X) + (facing.Y * facing.Y) + (facing.Z * facing.Z));
        if (facingLength < 0.5)
        {
            return (default, 0f, 0f);
        }

        Vector3d n = new Vector3d(facing.X, facing.Y, facing.Z) * (1.0 / facingLength);

        // Any vector not parallel to the facing starts the in-plane basis.
        Vector3d seed = Math.Abs(n.Z) < 0.9 ? new Vector3d(0.0, 0.0, 1.0) : new Vector3d(1.0, 0.0, 0.0);
        Vector3d e1 = Vector3d.Cross(n, seed);
        e1 *= 1.0 / e1.Length;
        Vector3d e2 = Vector3d.Cross(n, e1);

        double sw = 0.0, su = 0.0, sv = 0.0, suu = 0.0, svv = 0.0, suv = 0.0;
        foreach (int index in cluster)
        {
            Triangle triangle = triangles[index];
            double w = triangle.Area / 3.0;
            foreach (Vector3d q in (ReadOnlySpan<Vector3d>)[triangle.A, triangle.B, triangle.C])
            {
                double u = Vector3d.Dot(q - centre, e1);
                double v = Vector3d.Dot(q - centre, e2);
                sw += w; su += w * u; sv += w * v; suu += w * u * u; svv += w * v * v; suv += w * u * v;
            }
        }

        Vector3d axis = e1;
        if (sw > 0.0)
        {
            double mu = su / sw, mv = sv / sw;
            double cuu = (suu / sw) - (mu * mu), cvv = (svv / sw) - (mv * mv), cuv = (suv / sw) - (mu * mv);
            double angle = 0.5 * Math.Atan2(2.0 * cuv, cuu - cvv);
            axis = (e1 * Math.Cos(angle)) + (e2 * Math.Sin(angle));
        }

        Vector3d across = Vector3d.Cross(n, axis);
        double halfLength = 0.0, halfWidth = 0.0;
        foreach (int index in cluster)
        {
            Triangle triangle = triangles[index];
            foreach (Vector3d q in (ReadOnlySpan<Vector3d>)[triangle.A, triangle.B, triangle.C])
            {
                halfLength = Math.Max(halfLength, Math.Abs(Vector3d.Dot(q - centre, axis)));
                halfWidth = Math.Max(halfWidth, Math.Abs(Vector3d.Dot(q - centre, across)));
            }
        }

        // The longer side is the axis, whichever the covariance picked on a square fitting.
        if (halfWidth > halfLength)
        {
            (halfLength, halfWidth) = (halfWidth, halfLength);
            axis = across;
        }

        // Floored at two centimetres: a fitting measured flatter than that is a sliver of geometry, and a
        // rect of nought width would give every ray the same target.
        return (
            new Vector3f((float)axis.X, (float)axis.Y, (float)axis.Z),
            (float)Math.Max(halfLength, 0.02),
            (float)Math.Max(halfWidth, 0.02));
    }

    /// <summary>Turns a cluster's summed face normals into the direction it throws its light.</summary>
    /// <remarks>
    /// <para>
    /// <b>Zero unless the faces agree.</b> The sum of the cross products is area-weighted by
    /// construction, so its length against the summed lengths says how much of the fixture's surface
    /// points one way: a flat tube's underside is near one, and a lamp modelled as a CLOSED BOX is near
    /// nought, because its top cancels its bottom exactly. That second case is not a failure to fix, it
    /// is the geometry saying this fixture has no single face — and the honest answer is no direction,
    /// which the renderer lights as a point rather than as a spot aimed at a guess.
    /// </para>
    /// <para>
    /// Turned downward where it points up, because a fixture is mounted on something. The winding this
    /// is derived from is not consistently outward in this game's models — the ceiling lamp and the
    /// ceiling it hangs from can disagree — so the sign carries no information and the mounting does:
    /// a lamp lights the room, never the slab it is bolted to. For a fixture whose normal is near
    /// horizontal, a wall bracket, the flip barely moves it, which is the right amount of opinion to
    /// have about a case this evidence cannot settle.
    /// </para>
    /// </remarks>
    private static Vector3f Facing(Vector3d summed, double totalArea)
    {
        // Coherent enough to be one face. Two thirds admits a shallow curved reflector and a tube with
        // its short end caps; it rejects anything that is as much one way as another.
        const double MinimumCoherence = 0.66;

        double length = summed.Length;

        if (totalArea <= 0.0 || length <= 0.0 || (length / totalArea) < MinimumCoherence)
        {
            return default;
        }

        Vector3d unit = summed * (1.0 / length);

        // Z is up in this game's world. A fixture facing up is facing its own mounting.
        if (unit.Z > 0.0)
        {
            unit = new Vector3d(-unit.X, -unit.Y, -unit.Z);
        }

        return new Vector3f((float)unit.X, (float)unit.Y, (float)unit.Z);
    }

    /// <summary>Picks the texture that contributed most of a cluster's area.</summary>
    /// <remarks>
    /// <b>Clusters span textures rather than being built per texture, and this is the consequence.</b> A
    /// fixture is a place, and a lamp whose glass and whose housing are two different emitter textures
    /// is one lamp — clustering each texture separately would propose two lights standing inside each
    /// other, which is both wrong and expensive. The cost is that a fixture can only name one texture as
    /// its origin, and the largest contributor is the most useful one to name when a reviewer goes
    /// looking for the thing in the level.
    /// </remarks>
    private static string Dominant(List<(string Texture, double Area, int Count)> byTexture)
    {
        string best = byTexture[0].Texture;
        double bestArea = byTexture[0].Area;
        int bestCount = byTexture[0].Count;

        foreach ((string texture, double area, int count) in byTexture)
        {
            // Area first, count only to settle a cluster of degenerate triangles with no area to weigh.
            if (area > bestArea || (area == bestArea && count > bestCount))
            {
                best = texture;
                bestArea = area;
                bestCount = count;
            }
        }

        return best;
    }

    private static void Accumulate(List<(string Texture, double Area, int Count)> byTexture, string texture, double area)
    {
        for (int i = 0; i < byTexture.Count; i++)
        {
            if (string.Equals(byTexture[i].Texture, texture, StringComparison.OrdinalIgnoreCase))
            {
                byTexture[i] = (byTexture[i].Texture, byTexture[i].Area + area, byTexture[i].Count + 1);
                return;
            }
        }

        byTexture.Add((texture, area, 1));
    }

    /// <summary>Clamps a supplied colour into the range a light can be given.</summary>
    /// <remarks>
    /// A negative channel is meaningless and a caller that produces one has a bug elsewhere, but the
    /// consequence here would be a negative luminance and an imaginary radius, so it is clamped rather
    /// than propagated. Values above one are left alone: an over-bright colour is a legitimate way for a
    /// caller to say a source is strong.
    /// </remarks>
    private static Vector3f Normalise(Vector3f colour) =>
        new(MathF.Max(colour.X, 0f), MathF.Max(colour.Y, 0f), MathF.Max(colour.Z, 0f));

    private static int Compare(LightFixture left, LightFixture right)
    {
        int order = right.Intensity.CompareTo(left.Intensity);

        if (order != 0)
        {
            return order;
        }

        order = left.Position.X.CompareTo(right.Position.X);
        order = order != 0 ? order : left.Position.Y.CompareTo(right.Position.Y);

        return order != 0 ? order : left.Position.Z.CompareTo(right.Position.Z);
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            // Path halving: every lookup shortens the chain it walked, which is what keeps the union
            // pass linear without needing a rank alongside it.
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        int a = Find(parent, left);
        int b = Find(parent, right);

        if (a == b)
        {
            return;
        }

        // Always towards the lower index, so the root of a cluster is its first triangle whatever order
        // the unions happened in.
        if (a < b)
        {
            parent[b] = a;
        }
        else
        {
            parent[a] = b;
        }
    }

    /// <summary>One emitter triangle, already in world space.</summary>
    private readonly record struct Triangle(string Texture, Vector3d A, Vector3d B, Vector3d C)
    {
        /// <summary>Gets the triangle's centroid.</summary>
        public Vector3d Centroid => new((A.X + B.X + C.X) / 3.0, (A.Y + B.Y + C.Y) / 3.0, (A.Z + B.Z + C.Z) / 3.0);

        /// <summary>Gets the triangle's area, in square metres.</summary>
        public double Area => 0.5 * Vector3d.Cross(B - A, C - A).Length;
    }
}
