using System.Collections.ObjectModel;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// The drawable contents of a loaded mission: every task that places a model, joined to the pack
/// instance that supplies its geometry and textures.
/// </summary>
/// <remarks>
/// <para>
/// Level 1 places 501 of these, and all 501 resolve — the task's <c>Model</c> string is exactly a pack
/// instance name. They are mostly <c>EditRigidObj</c> (380) and <c>Building</c> (26), with doors, trains
/// and soldiers making up the rest.
/// </para>
/// <para>
/// Positions arrive in the engine's own units and are converted to metres here, once, so that everything
/// downstream — camera, near and far planes, movement speed — is in one unit. The original works in
/// engine units throughout and only divides for display; the reason to differ is depth precision, since
/// a level spans millions of units and a near plane a fraction of one.
/// </para>
/// </remarks>
public sealed class WorldScene
{
    private const string LogCategory = "World";

    private double? _groundHeight;

    /// <summary>Engine units per metre, the original's <c>WORLD_METER</c> script constant.</summary>
    /// <remarks>
    /// Registered as a double at <c>0x539558</c>. Model vertices use the same units as world positions:
    /// the level-1 water tower measures 95005 units tall, which is 23 metres.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The task parameter naming an object's model.</summary>
    public const string ModelParameter = "Model";

    /// <summary>The parameter naming a spline waypoint's segment model.</summary>
    /// <remarks>
    /// A <c>SplineObj</c> is a chain of <c>SplineObjWaypoint</c>s, each naming the mesh that spans from
    /// it towards the next — level 1's railway and roads are built from these. <b>Divergence:</b> the
    /// original bends the segment through a run of matrices along the spline
    /// (<c>Number of Matrices / Segment</c>); here each segment is placed rigidly at its waypoint's
    /// authored position and orientation, which shows the road but does not curve it between waypoints.
    /// </remarks>
    public const string SegmentModelParameter = "Segment Model";

    /// <summary>The task type whose model comes from <see cref="SegmentModelParameter"/>.</summary>
    public const string SplineWaypointType = "SplineObjWaypoint";

    /// <summary>The task type that chains the waypoints; its child order is the chain order.</summary>
    public const string SplineObjType = "SplineObj";

    /// <summary>The security-camera task type, whose models are engine-supplied rather than declared.</summary>
    public const string SecurityCameraType = "SCamera";

    /// <summary>The two models an <c>SCamera</c> builds in — its constructor (<c>0x4423F0</c>) hard-codes both.</summary>
    /// <remarks>
    /// The camera head and its mount. They are placed and numbered like any script-declared model,
    /// which is why a level's lightmap archive has entries no <c>Model</c> parameter accounts for.
    /// </remarks>
    public static readonly IReadOnlyList<string> SecurityCameraModels = ["313_01_1", "313_02_1"];

    /// <summary>Lists the model names a task places, in the order the object walk counts them.</summary>
    /// <param name="task">The task to read.</param>
    /// <returns>The names; empty for a task that places nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static IEnumerable<string> PlacedModelNames(DeclaredTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.TryGetValue(ModelParameter, out object? raw) && raw is string name && name.Length > 0)
        {
            yield return name;
        }

        if (string.Equals(task.Type.Name, SecurityCameraType, StringComparison.Ordinal))
        {
            foreach (string model in SecurityCameraModels)
            {
                yield return model;
            }
        }

        // A wall switch's model is the literal name "switch" (sub_4450F0), like "waypoint" and
        // "missing" a lowercase entry the common pack registers.
        if (string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal))
        {
            yield return "switch";
        }

        // A spline waypoint owns the segment that spans from the previous waypoint to it
        // (sub_500EF0), and that segment object is numbered like any other. The chain's first
        // waypoint has no previous, so its named model builds no segment and counts nothing.
        if (string.Equals(task.Type.Name, SplineWaypointType, StringComparison.Ordinal)
            && task.TryGetValue(SegmentModelParameter, out object? segment)
            && segment is string segmentName
            && segmentName.Length > 0
            && HasEarlierWaypointSibling(task))
        {
            yield return segmentName;
        }
    }

    /// <summary>Tells whether a waypoint has a waypoint sibling before it under the same parent.</summary>
    private static bool HasEarlierWaypointSibling(DeclaredTask waypoint)
    {
        if (waypoint.Parent is not { } parent)
        {
            return false;
        }

        foreach (GameTask sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, waypoint))
            {
                return false;
            }

            if (sibling is DeclaredTask declared
                && string.Equals(declared.Type.Name, SplineWaypointType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The task parameters an object's position may be named by, in order of preference.</summary>
    /// <remarks>
    /// Not every placing type calls it <c>Position</c>: a <c>Door</c> declares <c>Position start</c>,
    /// because it also carries the position it opens to. A task with none of these is not placed at the
    /// origin — it is skipped and counted, since a stray object at (0,0,0) drags the level's bounds out
    /// by kilometres and takes the camera with it.
    /// </remarks>
    public static readonly string[] PositionParameters = ["Position", "Position start"];

    /// <summary>The task parameter naming an object's orientation.</summary>
    public const string OrientationParameter = "Orientation";

    /// <summary>The task type whose <c>Filename</c> names an object's baked-lightmap set.</summary>
    public const string LightmapInfoType = "LightmapInfo";

    /// <summary>The <see cref="LightmapInfoType"/> parameter holding the set name.</summary>
    public const string LightmapFilenameParameter = "Filename";

    private WorldScene(
        LoadedMission mission,
        IReadOnlyList<WorldPlacement> placements,
        IReadOnlyList<LadderPlacement> ladders,
        int unresolved,
        int unplaced,
        int attached)
    {
        Mission = mission;
        Placements = placements;
        Ladders = ladders;
        UnresolvedCount = unresolved;
        UnplacedCount = unplaced;
        AttachedCount = attached;
    }

    /// <summary>Gets the mission the scene was built from.</summary>
    public LoadedMission Mission { get; }

    /// <summary>Gets everything to draw, in the order the level script placed it.</summary>
    public IReadOnlyList<WorldPlacement> Placements { get; }

    /// <summary>Gets the level's ground height, in metres, as an absolute world Z.</summary>
    /// <remarks>
    /// <para>
    /// A level's coordinates carry a large absolute offset — mission 1's ground sits some 42 km from
    /// the world origin — so a height authored as "four metres up", like the sky plane's or the fog
    /// base's, is nowhere near a world Z of four. It is an offset, and it has to be anchored to
    /// something before it means anything. This is that anchor.
    /// </para>
    /// <para>
    /// It is the <em>median</em> placement height rather than the middle of the bounding box, because
    /// most of what a level places sits on the ground: the median lands on it, while a box is dragged
    /// upwards by a single radio mast and downwards by a basement.
    /// </para>
    /// </remarks>
    public double GroundHeight => _groundHeight ??= MedianPlacementHeight();

    private double MedianPlacementHeight()
    {
        if (Placements.Count == 0)
        {
            return 0.0;
        }

        double[] heights = new double[Placements.Count];

        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = Placements[i].Position.Z;
        }

        Array.Sort(heights);
        return heights[heights.Length / 2];
    }

    /// <summary>Gets the ladders the level places, for the player and AI to climb.</summary>
    /// <remarks>
    /// Collected from the <c>TASKTYPE_LADDER</c> magic objects a building attaches, with their foot and
    /// head taken from the ladder model's magic vertices 2 and 3. See
    /// the original.
    /// </remarks>
    public IReadOnlyList<LadderPlacement> Ladders { get; }

    /// <summary>Gets how many placing tasks named a model no pack holds.</summary>
    public int UnresolvedCount { get; }

    /// <summary>Gets how many tasks named a model but declared no position this recognises.</summary>
    public int UnplacedCount { get; }

    /// <summary>Gets how many placements came from model attachments rather than from the level script.</summary>
    /// <remarks>
    /// These are the magic objects (see <see cref="MagicObjectRegistry"/>) and they are the majority of a
    /// level's contents: the script names 501 objects in level 1, and their models attach thousands more.
    /// Zero when <see cref="Build"/> was given no magic-object table.
    /// </remarks>
    public int AttachedCount { get; }

    /// <summary>Walks a level's task tree and joins every placed model to its pack instance.</summary>
    /// <param name="mission">The loaded mission, with both packs bound.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="magic">
    /// The magic-object table. Without it a level draws only what its script names — every model's
    /// attachments, and the baked-lightmap numbering that depends on them, are left out.
    /// </param>
    /// <param name="logger">Where to report placements that do not resolve.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static WorldScene Build(
        LoadedMission mission,
        GameTask levelRoot,
        MagicObjectRegistry? magic = null,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);

        IIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName =
            new(StringComparer.OrdinalIgnoreCase);

        // The level pack wins: it is opened second and its names shadow the common pack's.
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? model) && model is not null && !model.IsEmpty)
                {
                    byName[instance.Name] = (instance, pack, model);
                }
            }
        }

        // Which baked-lightmap set each placing task belongs to, and where in it each of the task's
        // models sits — one entry per placed model, because a security camera places two. Both come
        // from expanding the owner's whole subtree at once, because the numbering is a single running
        // count across it — see WorldObject.
        Dictionary<GameTask, List<(string Set, WorldObject Root)>> lightmapped = [];
        if (magic is not null)
        {
            BindLightmapSets(levelRoot, mission, magic, lightmapped);
        }

        List<WorldPlacement> placements = [];
        List<LadderPlacement> ladders = [];
        int unresolved = 0;
        int unplaced = 0;
        int attached = 0;

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask declared)
            {
                continue;
            }

            // Spline waypoints do not place their own model — each names the segment that spans from
            // it to the next waypoint, and the chains are laid below once every waypoint is known.
            if (string.Equals(declared.Type.Name, SplineWaypointType, StringComparison.Ordinal))
            {
                continue;
            }

            lightmapped.TryGetValue(task, out List<(string Set, WorldObject Root)>? bakes);

            int slot = 0;
            foreach (string name in PlacedModelNames(declared))
            {
                int modelSlot = slot++;
                if (!byName.TryGetValue(name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
                {
                    unresolved++;
                    continue;
                }

                if (!TryGetPosition(declared, out Vector3d position))
                {
                    unplaced++;
                    continue;
                }

                declared.TryGetValue(OrientationParameter, out object? rawOrientation);
                Matrix3f orientation = rawOrientation is Vector3f angles ? ToOrientation(angles) : Matrix3f.Identity;

                (string Set, WorldObject Root) bake = bakes is not null && modelSlot < bakes.Count
                    ? bakes[modelSlot]
                    : default;

                placements.Add(new WorldPlacement(
                    position,
                    orientation,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    task.Name,
                    bake.Set ?? string.Empty,
                    bake.Root?.LightmapIndex ?? -1));

                // Everything the model attaches, and everything those attach in turn. The tree is only
                // available when a magic-object table was supplied; without one a level draws its
                // shells and none of their contents.
                if (bake.Root is not null)
                {
                    attached += AddAttached(bake.Root, position, orientation, bake.Set ?? string.Empty, task.Name, byName, magic, placements, ladders);
                }
                else if (magic is not null
                    && WorldObject.TryBuild(name, mission.TryGetModel, magic, out WorldObject? loose)
                    && loose is not null)
                {
                    attached += AddAttached(loose, position, orientation, string.Empty, task.Name, byName, magic, placements, ladders);
                }
            }
        }

        int segments = AddSplineSegments(levelRoot, byName, placements);

        log.Information(
            LogCategory,
            $"Scene: {placements.Count} placement(s) over {byName.Count} bound instance(s)"
                + $", {attached} attached"
                + (segments > 0 ? $", {segments} spline segment(s)" : string.Empty)
                + (unresolved > 0 ? $", {unresolved} unresolved" : string.Empty)
                + (unplaced > 0 ? $", {unplaced} with no position" : string.Empty)
                + ".");

        return new WorldScene(
            mission,
            new ReadOnlyCollection<WorldPlacement>(placements),
            new ReadOnlyCollection<LadderPlacement>(ladders),
            unresolved,
            unplaced,
            attached);
    }

    /// <summary>Lays every <c>SplineObj</c> chain: one bent segment per consecutive waypoint pair.</summary>
    /// <remarks>
    /// <para>
    /// A segment spans from the <em>previous</em> waypoint to the waypoint that names its model, which
    /// is <c>sub_500EF0</c>'s arrangement — a chain of N waypoints lays N−1 segments and the first
    /// waypoint's own <c>Segment Model</c> goes unused. The curve is the one <c>sub_4E4530</c> builds:
    /// a Hermite cubic between the two waypoint positions whose tangents are the waypoints' local X
    /// axes scaled by the chord length, evaluated here per vertex rather than at the original's
    /// <c>NumAreas</c> sampled matrices — the same curve, continuously.
    /// </para>
    /// <para>
    /// A vertex's local X picks its place along the curve (the original measures the model's X extent
    /// the same way, <c>sub_4E4530</c>'s tail); its local Y and Z ride the curve's zero-roll frame.
    /// <b>Divergence:</b> the original also interpolates a twist angle between the endpoint
    /// orientations; every level-1 waypoint is authored yaw-only, so the twist is dropped until a
    /// level needs it.
    /// </para>
    /// </remarks>
    private static int AddSplineSegments(
        GameTask levelRoot,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        List<WorldPlacement> placements)
    {
        int added = 0;
        int chain = 0;
        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask spline
                || !string.Equals(spline.Type.Name, SplineObjType, StringComparison.Ordinal))
            {
                continue;
            }

            chain++;
            List<(DeclaredTask Task, Vector3d Position)> waypoints = [];
            foreach (GameTask child in spline.Children)
            {
                if (child is DeclaredTask waypoint
                    && string.Equals(waypoint.Type.Name, SplineWaypointType, StringComparison.Ordinal)
                    && TryGetPosition(waypoint, out Vector3d position))
                {
                    waypoints.Add((waypoint, position));
                }
            }

            for (int i = 1; i < waypoints.Count; i++)
            {
                (DeclaredTask waypoint, Vector3d to) = waypoints[i];
                (DeclaredTask previous, Vector3d from) = waypoints[i - 1];

                if (!waypoint.TryGetValue(SegmentModelParameter, out object? raw)
                    || raw is not string name
                    || name.Length == 0
                    || !byName.TryGetValue(name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
                {
                    continue;
                }

                MeshGeometry? bent = BendSegment(bound.Model, from, to, Tangent(previous), Tangent(waypoint));
                if (bent is null)
                {
                    continue;
                }

                placements.Add(new WorldPlacement(
                    from,
                    Matrix3f.Identity,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    $"spline:{chain}:{i}",
                    string.Empty,
                    -1)
                {
                    BentGeometry = bent,
                });
                added++;
            }
        }

        return added;

        // The tangent direction is the waypoint orientation's local X axis in world space — the
        // matrix's first column, exactly the elements [0], [3], [6] that sub_4E4530 reads.
        static Vector3f Tangent(DeclaredTask waypoint)
        {
            waypoint.TryGetValue(OrientationParameter, out object? raw);
            Matrix3f m = raw is Vector3f angles ? ToOrientation(angles) : Matrix3f.Identity;
            return new Vector3f(m.M11, m.M21, m.M31);
        }
    }

    /// <summary>Bends one segment model along its span's Hermite curve.</summary>
    /// <param name="model">The segment model, authored along its local X.</param>
    /// <param name="from">The span's start, in metres — also the placement's position.</param>
    /// <param name="to">The span's end, in metres.</param>
    /// <param name="fromTangent">The start waypoint's unit tangent.</param>
    /// <param name="toTangent">The end waypoint's unit tangent.</param>
    /// <returns>The geometry, positioned relative to <paramref name="from"/>, or <see langword="null"/>.</returns>
    private static MeshGeometry? BendSegment(
        MefModel model,
        Vector3d from,
        Vector3d to,
        Vector3f fromTangent,
        Vector3f toTangent)
    {
        if (model.IsEmpty || model.DrawRecords.Count == 0)
        {
            return null;
        }

        ReadOnlySpan<MefVertex> source = model.Vertices;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (MefVertex vertex in source)
        {
            minX = Math.Min(minX, vertex.Position.X);
            maxX = Math.Max(maxX, vertex.Position.X);
        }

        float length = maxX - minX;
        if (length <= 0)
        {
            return null;
        }

        // The Hermite endpoints relative to the span's start, with tangents scaled by the chord — the
        // v79 factor sub_4E4530 multiplies both endpoint axes by.
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double chord = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (chord < 1e-3)
        {
            return null;
        }

        Span<double> t0 = [fromTangent.X * chord, fromTangent.Y * chord, fromTangent.Z * chord];
        Span<double> t1 = [toTangent.X * chord, toTangent.Y * chord, toTangent.Z * chord];
        Span<double> p1 = [dx, dy, dz];

        Vertex3D[] vertices = new Vertex3D[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            MefVertex v = source[i];
            double t = Math.Clamp((v.Position.X - minX) / length, 0.0, 1.0);

            // Hermite basis and its derivative, once per vertex.
            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = (2 * t3) - (3 * t2) + 1;
            double h10 = t3 - (2 * t2) + t;
            double h01 = (-2 * t3) + (3 * t2);
            double h11 = t3 - t2;
            double d00 = (6 * t2) - (6 * t);
            double d10 = (3 * t2) - (4 * t) + 1;
            double d01 = (6 * t) - (6 * t2);
            double d11 = (3 * t2) - (2 * t);

            double px = (h10 * t0[0]) + (h01 * p1[0]) + (h11 * t1[0]);
            double py = (h10 * t0[1]) + (h01 * p1[1]) + (h11 * t1[1]);
            double pz = (h10 * t0[2]) + (h01 * p1[2]) + (h11 * t1[2]);
            _ = h00; // the start point is the origin, so its term vanishes

            double gx = (d00 * 0) + (d10 * t0[0]) + (d01 * p1[0]) + (d11 * t1[0]);
            double gy = (d10 * t0[1]) + (d01 * p1[1]) + (d11 * t1[1]);
            double gz = (d10 * t0[2]) + (d01 * p1[2]) + (d11 * t1[2]);

            // The curve's zero-roll frame: X down the tangent, Y level, Z perpendicular up — what
            // sub_4E4530 builds through sub_4B38E0 from the direction angles with no twist.
            double glen = Math.Sqrt((gx * gx) + (gy * gy) + (gz * gz));
            if (glen < 1e-9)
            {
                gx = dx / chord;
                gy = dy / chord;
                gz = dz / chord;
                glen = 1.0;
            }

            double fx = gx / glen;
            double fy = gy / glen;
            double fz = gz / glen;

            double sx = -fy;
            double sy = fx;
            double slen = Math.Sqrt((sx * sx) + (sy * sy));
            if (slen < 1e-9)
            {
                sx = 0.0;
                sy = 1.0;
                slen = 1.0;
            }

            sx /= slen;
            sy /= slen;

            double ux = -(fz * sy);
            double uy = fz * sx;
            double uz = (fx * sy) - (fy * sx);

            double offY = v.Position.Y / UnitsPerMetre;
            double offZ = v.Position.Z / UnitsPerMetre;

            vertices[i] = new Vertex3D(
                new Vector3f(
                    (float)(px + (sx * offY) + (ux * offZ)),
                    (float)(py + (sy * offY) + (uy * offZ)),
                    (float)(pz + (uz * offZ))),
                new Vector3f(
                    (float)((fx * v.Normal.X) + (sx * v.Normal.Y) + (ux * v.Normal.Z)),
                    (float)((fy * v.Normal.X) + (sy * v.Normal.Y) + (uy * v.Normal.Z)),
                    (float)((fz * v.Normal.X) + (uz * v.Normal.Z))),
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV);
        }

        int total = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            total += record.Indices.Length;
        }

        ushort[] indices = new ushort[total];
        List<MeshSubset> subsets = new(model.DrawRecords.Count);
        int cursor = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> run = record.Indices.Span;
            int start = cursor;
            foreach (ushort index in run)
            {
                indices[cursor++] = (ushort)record.ToVertexIndex(index);
            }

            subsets.Add(new MeshSubset(start, run.Length, record.MaterialSlot, record.LightmapPage));
        }

        return new MeshGeometry(vertices, indices, new ReadOnlyCollection<MeshSubset>(subsets));
    }

    /// <summary>
    /// Expands every lightmap owner's subtree and records, per placing task, which set it belongs to and
    /// the tree its own model heads.
    /// </summary>
    private static void BindLightmapSets(
        GameTask levelRoot,
        LoadedMission mission,
        MagicObjectRegistry magic,
        Dictionary<GameTask, List<(string Set, WorldObject Root)>> bound)
    {
        foreach (GameTask owner in Walk(levelRoot))
        {
            if (!TryGetLightmapSet(owner, out string set))
            {
                continue;
            }

            List<GameTask> placing = [];
            List<string> models = [];
            foreach (GameTask task in Subtree(owner))
            {
                if (task is not DeclaredTask declared)
                {
                    continue;
                }

                foreach (string name in PlacedModelNames(declared))
                {
                    placing.Add(task);
                    models.Add(name);
                }
            }

            IReadOnlyList<WorldObject?> roots = WorldObject.BuildAll(models, mission.TryGetModel, magic);
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] is not { } root)
                {
                    continue;
                }

                if (!bound.TryGetValue(placing[i], out List<(string Set, WorldObject Root)>? list))
                {
                    bound[placing[i]] = list = [];
                }

                list.Add((set, root));
            }
        }
    }

    /// <summary>Reads the set name off a task's <c>LightmapInfo</c> child, if it has one.</summary>
    private static bool TryGetLightmapSet(GameTask task, out string set)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is DeclaredTask declared
                && string.Equals(declared.Type.Name, LightmapInfoType, StringComparison.Ordinal)
                && declared.TryGetValue(LightmapFilenameParameter, out object? raw)
                && raw is string name
                && name.Length > 0)
            {
                set = name;
                return true;
            }
        }

        set = string.Empty;
        return false;
    }

    /// <summary>Places a node's attached children, and theirs, in the parent's frame.</summary>
    /// <remarks>
    /// An attachment's position is in its parent model's space and in the same engine units as a vertex,
    /// so it converts to metres the same way a placement's does. The rotations compose in the order the
    /// camera already uses for a placement's own orientation.
    /// </remarks>
    private static int AddAttached(
        WorldObject node,
        Vector3d position,
        Matrix3f orientation,
        string set,
        string taskName,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        MagicObjectRegistry? magic,
        List<WorldPlacement> placements,
        List<LadderPlacement> ladders)
    {
        int added = 0;
        foreach (WorldObject child in node.Children)
        {
            // Parent-to-world is the column-vector product M·v, matching how the renderer applies the
            // parent's own orientation (`uModelView * v`); Transform is the row-vector v·M, its
            // inverse, which placed a rotated parent's contents on the wrong side of it. The rotation
            // composition below is already the column-vector convention's parent∘child order.
            Vector3f offset = orientation.Transposed().Transform(child.Position);
            Vector3d childPosition = new(
                position.X + (offset.X / UnitsPerMetre),
                position.Y + (offset.Y / UnitsPerMetre),
                position.Z + (offset.Z / UnitsPerMetre));
            Matrix3f childOrientation = orientation * child.Orientation;

            // A ladder is a TASKTYPE_LADDER magic object whose model carries the climb line as its magic
            // vertices 2 (foot) and 3 (head). Collected here, where its world transform is known.
            TryCollectLadder(child, childPosition, childOrientation, magic, ladders);

            // A child draws only if its model is also a pack instance, because the textures a sub-mesh
            // uses are named by the instance, not by the model. Its subtree is walked either way: the
            // lightmap numbering already counted it, and its own children may well be instances.
            if (byName.TryGetValue(child.Name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
            {
                placements.Add(new WorldPlacement(
                    childPosition,
                    childOrientation,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    taskName,
                    set,
                    set.Length > 0 ? child.LightmapIndex : -1));
                added++;
            }

            added += AddAttached(child, childPosition, childOrientation, set, taskName, byName, magic, placements, ladders);
        }

        return added;
    }

    /// <summary>The magic-object task type a ladder is spawned with.</summary>
    private const string LadderTaskType = "TASKTYPE_LADDER";

    /// <summary>Records a ladder when a node is one — its climb line from magic vertices 2 and 3.</summary>
    private static void TryCollectLadder(
        WorldObject child,
        Vector3d positionMetres,
        Matrix3f orientation,
        MagicObjectRegistry? magic,
        List<LadderPlacement> ladders)
    {
        if (magic is null
            || !magic.TryGet(child.Name, out MagicObjectDefinition definition)
            || !string.Equals(magic.TaskTypeName(definition.TaskType), LadderTaskType, StringComparison.Ordinal))
        {
            return;
        }

        MefModel model = child.Models.Highest;
        if (!model.TryGetMagicVertex(2, out Vector3f foot) || !model.TryGetMagicVertex(3, out Vector3f head))
        {
            return;
        }

        Vector3d origin = new(
            positionMetres.X * UnitsPerMetre,
            positionMetres.Y * UnitsPerMetre,
            positionMetres.Z * UnitsPerMetre);

        ladders.Add(new LadderPlacement(
            ToWorldUnits(origin, orientation, foot), ToWorldUnits(origin, orientation, head), orientation));
    }

    /// <summary>Places a model-space magic vertex in the world, in engine units.</summary>
    /// <remarks>
    /// <b>Verified against the running game</b> (a probe: the ladder mount computed to the byte from
    /// this): the magic vertex is rotated as <c>v · M</c> (<see cref="Matrix3f.Transform(Vector3f)"/>), NOT
    /// the transpose. Using the transpose negated the vertex's X and Y (Z survives on an axis-aligned
    /// ladder), which stood the climbing body ~6 cm off the rungs. Unlike an attachment offset, a magic
    /// vertex takes the plain product.
    /// </remarks>
    private static Vector3d ToWorldUnits(Vector3d originUnits, Matrix3f orientation, Vector3f local)
    {
        Vector3f rotated = orientation.Transform(local);
        return new Vector3d(originUnits.X + rotated.X, originUnits.Y + rotated.Y, originUnits.Z + rotated.Z);
    }

    /// <summary>Walks a task and everything under it, the task itself first.</summary>
    private static IEnumerable<GameTask> Subtree(GameTask task)
    {
        yield return task;
        foreach (GameTask descendant in Walk(task))
        {
            yield return descendant;
        }
    }

    /// <summary>Reads whichever position parameter a placing task declares.</summary>
    /// <param name="task">The task to read.</param>
    /// <param name="position">Receives the position, in metres.</param>
    /// <returns><see langword="false"/> when the task declares none of <see cref="PositionParameters"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static bool TryGetPosition(DeclaredTask task, out Vector3d position)
    {
        ArgumentNullException.ThrowIfNull(task);

        foreach (string name in PositionParameters)
        {
            if (task.TryGetValue(name, out object? value) && value is Vector3d p)
            {
                position = new Vector3d(p.X / UnitsPerMetre, p.Y / UnitsPerMetre, p.Z / UnitsPerMetre);
                return true;
            }
        }

        position = default;
        return false;
    }

    /// <summary>Builds an orientation from the three Euler angles level data stores.</summary>
    /// <param name="angles">The alpha, beta and gamma angles in radians.</param>
    /// <returns>The rotation.</returns>
    /// <remarks>
    /// A <c>Real32x9</c> parameter is three angles on the wire, not nine components, and the original
    /// concatenates three axis rotations in <c>0x4B38E0</c>. That function's axis order <b>has now been
    /// read</b> and is Z(gamma)·Y(beta)·X(alpha), which is what this always composed — see
    /// <see cref="Matrix3f.FromEngineEulerAngles"/>, where the composition now lives so the world and
    /// the first-person eye share one reading of it.
    /// </remarks>
    public static Matrix3f ToOrientation(Vector3f angles) =>
        Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z);

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            // A ConditionalContainer whose condition is false is not registered into the world, so its
            // subtree draws nothing — the intro/outro cut-scene props and actors level 1 hides. See
            // ConditionalContainerGate.
            if (ConditionalContainerGate.IsDormant(child))
            {
                continue;
            }

            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
