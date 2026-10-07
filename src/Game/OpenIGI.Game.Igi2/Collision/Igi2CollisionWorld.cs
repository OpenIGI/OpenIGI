using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>What a collision hit on a placed object reports as struck: the placing task and the model.</summary>
/// <param name="TypeName">The type of the task that placed the object, or of its root when it hangs off another.</param>
/// <param name="LevelId">That task's level id.</param>
/// <param name="Model">The model's name.</param>
/// <param name="Depth">How deep in its root's attachment tree the object is; zero for the placed object itself.</param>
public sealed record Igi2CollisionOwner(string TypeName, int LevelId, string Model, int Depth);

/// <summary>
/// The level as the collision engine sees it: every placed solid model and the ground, and the queries that
/// trace lines through them.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps its objects in two octrees, a stationary one and one for things that move. The queries a body
/// uses to find its footing read only the stationary tree. Every type that answers such a query is a
/// <c>RigidDynCubeObj</c> or derives from one: <c>EditRigidObj</c>, <c>Door</c>, <c>Switch</c>, <c>Lift</c>,
/// <c>Building</c> and its sectors, and the <c>RigidMagicObj</c>s a model's attachments become. They all answer with
/// one handler, <see cref="Igi2RigidTrace"/>. Bone models answer line traces but not ground queries, and characters
/// are in the moving tree.
/// </para>
/// <para>
/// The octree decides only which handlers are called. Each handler then tests its own bounds, so a linear walk over
/// a sphere test finds the same surfaces. What it can change is the order, and with it which of two surfaces at
/// exactly the same distance a line keeps: a handler replaces a hit only when it is strictly nearer.
/// </para>
/// </remarks>
public sealed class Igi2CollisionWorld : IIgi2CollisionQuery
{
    /// <summary>The query-group bit that asks for the ground, <c>0x80000000</c>.</summary>
    public const uint TerrainGroup = 0x80000000;

    /// <summary>
    /// The bit every object type's group carries: <c>QTask_SetQTaskTypeGroup</c> (<c>igi2 0x40B2D0</c>) ORs
    /// <c>0x20000000</c> into whatever group a type is given, so a query naming it reaches every type.
    /// </summary>
    public const uint EveryTypeGroup = 0x20000000;

    /// <summary>The group humans are in, <c>1</c>.</summary>
    public const uint HumanGroup = 0x1;

    /// <summary>The group forests are in, <c>Forest_bQTaskGroup</c>: <c>2</c>.</summary>
    public const uint ForestGroup = 0x2;

    /// <summary>The group spline segments are in, <c>0x10000000</c>.</summary>
    public const uint SplineGroup = 0x10000000;

    /// <summary>
    /// How far a vertical query's box reaches above and below its lines: <c>16387</c> units, against <c>2</c> across.
    /// </summary>
    private const double BoxDepth = 16387.0;

    private const double BoxWidth = 2.0;

    /// <summary>How deep an attachment tree is followed before it is taken for a cycle; the shipped data has none.</summary>
    private const int MaxAttachmentDepth = 16;

    private readonly Igi2RigidObject[] _objects;
    private readonly Igi2SplineSegment[] _segments;
    private readonly Igi2ForestCollider[] _forests;
    private readonly Igi2TerrainCollider? _terrain;

    /// <summary>Initializes a new instance of the <see cref="Igi2CollisionWorld"/> class.</summary>
    /// <param name="objects">The placed solid models.</param>
    /// <param name="terrain">The ground, or <see langword="null"/> for a level with none.</param>
    /// <param name="forests">The stands of trees, or <see langword="null"/> for none.</param>
    /// <param name="segments">The straight spline segments, or <see langword="null"/> for none.</param>
    public Igi2CollisionWorld(
        IEnumerable<Igi2RigidObject> objects,
        Igi2TerrainCollider? terrain,
        IEnumerable<Igi2ForestCollider>? forests = null,
        IEnumerable<Igi2SplineSegment>? segments = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = [.. objects];
        _segments = segments is null ? [] : [.. segments];
        _forests = forests is null ? [] : [.. forests];
        _terrain = terrain;
    }

    /// <summary>Gets the placed solid models.</summary>
    public IReadOnlyList<Igi2RigidObject> Objects => _objects;

    /// <summary>Gets the straight spline segments.</summary>
    public IReadOnlyList<Igi2SplineSegment> Segments => _segments;

    /// <summary>Gets the stands of trees.</summary>
    public IReadOnlyList<Igi2ForestCollider> Forests => _forests;

    /// <summary>Gets the ground, or <see langword="null"/> for a level with none.</summary>
    public Igi2TerrainCollider? Terrain => _terrain;

    /// <summary>Builds the collision world for a level's placements and ground.</summary>
    /// <param name="scene">What the level places.</param>
    /// <param name="models">Resolves a model name to its mesh, or <see langword="null"/> when nothing ships it.</param>
    /// <param name="ground">The level's ground, or <see langword="null"/>.</param>
    /// <param name="terrainOwner">What a hit on the ground reports as struck.</param>
    /// <param name="forests">The level's stands of trees, or <see langword="null"/> for none.</param>
    /// <returns>The world.</returns>
    /// <remarks>
    /// <para>
    /// Every static placement becomes an object at the pose its parameters give through <see cref="Igi2Pose"/>, and
    /// so does every model its model attaches, recursively. That is <c>ModelObj_CreateAttachObject</c>
    /// (<c>igi2 0x56D0B0</c>): an attachment's name that is not a magic-object type is used as a model. Names that
    /// are magic-object types are skipped, because their table is not read yet.
    /// </para>
    /// <para>
    /// A model with bones becomes a <c>BoneMagicObj</c>, which does not answer ground queries, so it is left out.
    /// Its own attachments are still followed.
    /// </para>
    /// <para>
    /// A straight spline segment becomes an <see cref="Igi2SplineSegment"/>, its radius taken over the model's levels
    /// of detail. Segments bent along a curve, and wires, have handlers of their own that are not ported yet; they are
    /// left out.
    /// </para>
    /// </remarks>
    public static Igi2CollisionWorld Build(
        Igi2WorldScene scene,
        Func<string, MefModel?> models,
        Igi2TerrainSurface? ground,
        object terrainOwner,
        Igi2Forest? forests = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(terrainOwner);

        List<Igi2RigidObject> objects = [];
        List<Igi2SplineSegment> segments = [];
        foreach (Igi2Placement placement in scene.StaticPlacements)
        {
            if (placement.Geometry is Igi2SplineSweep { IsLinear: true } sweep)
            {
                AddSegment(placement, sweep);
                continue;
            }

            if (placement.Geometry is not null)
            {
                continue;
            }

            Igi2Pose pose = placement.Frame is { } frame
                ? Igi2Pose.FromMatrix(placement.Position, frame)
                : Igi2Pose.FromAngles(placement.Position, placement.Orientation);
            Add(placement, placement.Model, pose, depth: 0);
        }

        Igi2TerrainCollider? terrain = ground is { IsEmpty: false } ? new Igi2TerrainCollider(ground, terrainOwner) : null;
        List<Igi2ForestCollider> stands = [];
        foreach (Igi2ForestStand stand in forests?.Stands ?? [])
        {
            if (models(stand.Model) is { } tree && Igi2ForestCollider.Build(stand, tree) is { } collider)
            {
                stands.Add(collider);
            }
        }

        return new Igi2CollisionWorld(objects, terrain, stands, segments);

        void AddSegment(Igi2Placement placement, Igi2SplineSweep sweep)
        {
            if (models(placement.Model) is not { } model || Meshes(model) is not { } meshes)
            {
                return;
            }

            float radius = 0f;
            foreach (string level in Igi2SplineSegment.LevelsOfDetail(placement.Model, name => models(name) is not null))
            {
                float levelRadius = models(level)!.NativeMarkerBound;
                if (radius < levelRadius)
                {
                    radius = levelRadius;
                }
            }

            if (Igi2SplineSegment.Straight(
                new Igi2CollisionOwner(placement.TypeName, placement.LevelId, placement.Model, 0),
                sweep.Start,
                sweep.Stop,
                sweep.IsFlipped,
                meshes,
                radius) is { } segment)
            {
                segments.Add(segment);
            }
        }

        void Add(Igi2Placement root, string modelName, Igi2Pose pose, int depth)
        {
            MefModel? model = models(modelName);
            if (model is null)
            {
                return;
            }

            if (model.BoneHierarchy is null && Meshes(model) is { } meshes)
            {
                Igi2RigidObject solid = new(
                    new Igi2CollisionOwner(root.TypeName, root.LevelId, modelName, depth),
                    meshes,
                    model.NativeMarkerBound,
                    isBuildingMesh: false)
                {
                    Position = pose.Position,
                };
                for (int i = 0; i < solid.Orientation.Length; i++)
                {
                    solid.Orientation[i] = pose.Orientation[i];
                }

                objects.Add(solid);
            }

            if (depth >= MaxAttachmentDepth)
            {
                return;
            }

            foreach (MefAttachment attachment in model.Attachments)
            {
                Add(root, attachment.Name, pose.Attach(attachment), depth + 1);
            }
        }
    }

    /// <summary>
    /// Vertical lines from each start by its distance, against the objects and, when asked, the ground:
    /// <c>World_GetNextZSpecificMesh</c> (<c>igi2 0x5512C0</c>).
    /// </summary>
    /// <param name="trace">The lines: their starts are read, their ends written, their results cleared and filled.</param>
    /// <param name="distances">How far each line reaches along Z: negative down, positive up.</param>
    /// <param name="groups">
    /// The query groups: an object takes part when its type's group shares a bit with them (every type has
    /// <see cref="EveryTypeGroup"/>), and <see cref="TerrainGroup"/> asks for the ground.
    /// </param>
    public void NextZ(Igi2LineTrace trace, ReadOnlySpan<double> distances, uint groups)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (distances.Length != trace.Count)
        {
            throw new ArgumentException("One distance per line.", nameof(distances));
        }

        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i];
            trace.Ends[i] = new Vector3d(start.X, start.Y, distances[i] + start.Z);
            trace.Intersections[i].IsHit = false;
        }

        uint objectGroups = groups & ~TerrainGroup;
        if (objectGroups != 0)
        {
            TraceObjects(trace, objectGroups, lines: false);
        }

        if ((groups & TerrainGroup) != 0)
        {
            _terrain?.NextZ(trace);
        }
    }

    /// <summary>
    /// Lines in any direction against the objects of some groups: <c>World_TraceLinesSpecificMesh</c>
    /// (<c>igi2 0x5519C0</c>).
    /// </summary>
    /// <param name="trace">The lines: their starts and ends are read, their results reset to the ends and filled.</param>
    /// <param name="groups">The query groups; <see cref="TerrainGroup"/> asks for the ground as well.</param>
    public void TraceLines(Igi2LineTrace trace, uint groups)
    {
        ArgumentNullException.ThrowIfNull(trace);
        TraceObjects(trace, groups & ~TerrainGroup, lines: true);
        if ((groups & TerrainGroup) != 0)
        {
            _terrain?.TraceLines(trace);
        }
    }

    /// <summary>
    /// The object half of a line trace, <c>World_TraceLinesSpecificMesh</c> (<c>igi2 0x5519C0</c>): every result is reset
    /// to the line's end, and every object whose bounds meet the lines' padded box is traced.
    /// </summary>
    private void TraceObjects(Igi2LineTrace trace, uint groups, bool lines)
    {
        Vector3d min = trace.Starts[0], max = trace.Starts[0];
        for (int i = 0; i < trace.Count; i++)
        {
            Grow(trace.Starts[i]);
            Grow(trace.Ends[i]);
            Igi2Intersection hit = trace.Intersections[i];
            hit.HitObject = null;
            hit.IsHit = false;
            hit.Position = trace.Ends[i];
        }

        min = new Vector3d(min.X - BoxWidth, min.Y - BoxWidth, min.Z - BoxDepth);
        max = new Vector3d(max.X + BoxWidth, max.Y + BoxWidth, max.Z + BoxDepth);
        foreach (Igi2RigidObject solid in _objects)
        {
            if ((groups & (solid.Group | EveryTypeGroup)) == 0)
            {
                continue;
            }

            Vector3d p = solid.Position;
            double r = solid.BoundingRadius;
            if (max.X >= p.X - r && min.X <= r + p.X
                && max.Y >= p.Y - r && min.Y <= r + p.Y
                && p.Z - r <= max.Z && min.Z <= r + p.Z)
            {
                solid.NextZ(trace);
            }
        }

        foreach (Igi2SplineSegment segment in _segments)
        {
            if ((groups & (segment.Group | EveryTypeGroup)) == 0)
            {
                continue;
            }

            Vector3d p = segment.Position;
            double r = segment.Radius;
            if (max.X >= p.X - r && min.X <= r + p.X
                && max.Y >= p.Y - r && min.Y <= r + p.Y
                && p.Z - r <= max.Z && min.Z <= r + p.Z)
            {
                segment.NextZ(trace);
            }
        }

        // The stands answer the two events with different handlers: ground queries steer by the first line's start,
        // line traces by the lines' rectangle and through the foliage.
        foreach (Igi2ForestCollider forest in _forests)
        {
            if ((groups & (forest.Group | EveryTypeGroup)) == 0)
            {
                continue;
            }

            Vector3d p = forest.Position;
            double r = forest.BoundingRadius;
            if (max.X >= p.X - r && min.X <= r + p.X
                && max.Y >= p.Y - r && min.Y <= r + p.Y
                && p.Z - r <= max.Z && min.Z <= r + p.Z)
            {
                if (lines)
                {
                    forest.TraceLines(trace);
                }
                else
                {
                    forest.NextZ(trace);
                }
            }
        }

        void Grow(Vector3d v)
        {
            min = new Vector3d(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new Vector3d(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }
    }

    /// <summary>The model's collision mesh levels, or <see langword="null"/> when it has none.</summary>
    internal static Igi2CollisionMesh?[]? Meshes(MefModel model)
    {
        Igi2CollisionMesh?[] levels = new Igi2CollisionMesh?[MefCollisionMesh.MeshLevels];
        bool any = false;
        for (int level = 0; level < levels.Length; level++)
        {
            if (MefCollisionMesh.TryRead(model.Container, level, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                && mesh is { Faces.Length: > 0 })
            {
                levels[level] = new Igi2CollisionMesh(mesh);
                any = true;
            }
        }

        return any ? levels : null;
    }
}
