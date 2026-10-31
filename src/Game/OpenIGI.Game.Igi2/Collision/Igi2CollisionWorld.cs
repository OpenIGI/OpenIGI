using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>What a collision hit on a placed object reports as struck: the placing task and the model.</summary>
/// <param name="TypeName">The type of the task that placed the object, or of its root when it hangs off another.</param>
/// <param name="LevelId">That task's level id.</param>
/// <param name="Model">The model's name.</param>
/// <param name="Depth">How deep in its root's attachment tree the object is; zero for the placed object itself.</param>
public sealed record Igi2CollisionOwner(string TypeName, int LevelId, string Model, int Depth)
{
    /// <summary>
    /// Gets the key of a pane of glass, as <c>GlassContainer_MakeHashKey</c> builds it, or <see langword="null"/> for anything else:
    /// the pane's place among its owner's attach points, then up through each model it hangs off, <c>M-</c> at the placed model's
    /// container, and the placed model's name and level id (<c>0-600_00_1-3581</c>).
    /// </summary>
    public string? GlassKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether the solid is a building's room, a <c>Sector</c>: a plain model, not a magic one, attached to a
    /// building, whose mesh has portals (<c>modelobj.c</c>'s attach, <c>igi2 0x4DFA50</c>). A dropped weapon comes to rest on one as
    /// on the ground.
    /// </summary>
    public bool IsSector { get; init; }
}

/// <summary>An object an attach point makes of a magic type: the type, where the point puts it, and the solid it is made of.</summary>
/// <param name="Type">The magic type.</param>
/// <param name="Pose">Where it stands.</param>
/// <param name="Solid">The owner its solid is built under.</param>
public sealed record Igi2MagicPlacement(World.Igi2MagicObjType Type, Igi2Pose Pose, Igi2CollisionOwner Solid);

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
public sealed class Igi2CollisionWorld : IIgi2CollisionQuery, IIgi2SightQuery, ICollisionWorld
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

    /// <summary>
    /// The placed types sight passes through: each links the line-of-sight event, 9, to no handler. <c>GunPickup</c>
    /// (<c>GunPickup_Open</c>, <c>igi2 0x46C780</c>), <c>SCamera</c> (<c>0x490C00</c>), <c>Wire</c> (<c>0x514EF0</c>),
    /// <c>EditorRigidObj</c> (<c>0x580890</c>), <c>EditCamera</c> (<c>0x580E10</c>) and <c>Origo</c> (<c>0x5C9A50</c>).
    /// The other two pickups keep the handler they inherit, and block it.
    /// </summary>
    // The type whose mounted model is a solid of its own, a security camera's lens.
    private const string LensType = "SCamera";

    /// <summary>The magic type a pane of glass is made as, <c>Glass</c>.</summary>
    public const string GlassType = "Glass";

    private static readonly HashSet<string> SightThrough = new(StringComparer.Ordinal)
    {
        "GunPickup", "SCamera", "Wire", "EditorRigidObj", "EditCamera", "Origo",
    };

    private readonly List<Igi2RigidObject> _objects;
    private readonly Igi2SplineSegment[] _segments;
    private readonly Igi2ForestCollider[] _forests;
    private readonly List<Igi2BoneCollider> _bodies = [];
    private readonly List<IWorldLineCollider> _others = [];
    private readonly List<World.Igi2WaterBody> _waters = [];
    private readonly IWorldGroundCollider? _terrain;

    /// <summary>Initializes a new instance of the <see cref="Igi2CollisionWorld"/> class.</summary>
    /// <param name="objects">The placed solid models.</param>
    /// <param name="terrain">The ground, or <see langword="null"/> for a level with none.</param>
    /// <param name="forests">The stands of trees, or <see langword="null"/> for none.</param>
    /// <param name="segments">The straight spline segments, or <see langword="null"/> for none.</param>
    public Igi2CollisionWorld(
        IEnumerable<Igi2RigidObject> objects,
        IWorldGroundCollider? terrain,
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

    /// <summary>Puts a solid made while the level runs in the world, after the level's own.</summary>
    /// <param name="solid">The solid.</param>
    public void AddObject(Igi2RigidObject solid)
    {
        ArgumentNullException.ThrowIfNull(solid);
        _objects.Add(solid);
    }

    /// <summary>Takes a solid out of the world, as its object's delete handler does.</summary>
    /// <param name="solid">The solid.</param>
    public void RemoveObject(Igi2RigidObject solid) => _objects.Remove(solid);

    /// <summary>Gets the boned objects in the world, the humans: line traces meet them as they stand.</summary>
    public IReadOnlyList<Igi2BoneCollider> Bodies => _bodies;

    /// <summary>Puts a boned object in the world, as <c>BoneDynCubeObj_RegisterHandler</c> (<c>igi2 0x567710</c>) registers a body.</summary>
    /// <param name="body">The object.</param>
    public void AddBody(Igi2BoneCollider body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _bodies.Add(body);
    }

    /// <summary>Takes a boned object out of the world, as a player leaving a server does.</summary>
    /// <param name="body">The object.</param>
    public void RemoveBody(Igi2BoneCollider body) => _bodies.Remove(body);

    /// <summary>Puts something in the world that answers lines and sight itself, after the world's own kinds.</summary>
    /// <param name="collider">It.</param>
    public void AddCollider(IWorldLineCollider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        _others.Add(collider);
    }

    /// <summary>Gets what answers lines and sight beside the world's own kinds.</summary>
    public IReadOnlyList<IWorldLineCollider> OtherColliders => _others;

    /// <summary>Gets the bodies of water in the world: only line traces meet their surfaces.</summary>
    public IReadOnlyList<World.Igi2WaterBody> Waters => _waters;

    /// <summary>Puts a body of water in the world, as <c>Water_CreateHandler</c> registers its object with its size for a radius.</summary>
    /// <param name="water">The body.</param>
    public void AddWater(World.Igi2WaterBody water)
    {
        ArgumentNullException.ThrowIfNull(water);
        _waters.Add(water);
    }

    /// <summary>Gets the ground, or <see langword="null"/> for a level with none.</summary>
    public IWorldGroundCollider? Terrain => _terrain;

    /// <summary>Gets or sets the ground a body stands on, the level's height grid; <see langword="null"/> for none.</summary>
    public IGroundHeightField? Ground { get; set; }

    /// <summary>
    /// Casts one line through every object and the ground, as <see cref="TraceLines"/> does, and reports what it met
    /// nearest: the title-neutral question the other game's code asks.
    /// </summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="hit">Receives the nearest surface; its material is the level material row the line went into.</param>
    /// <returns><see langword="true"/> when the line met something before its end.</returns>
    public bool Cast(Vector3d origin, Vector3d target, out WorldRayHit hit)
    {
        WorldLineTrace trace = new(1);
        trace.Starts[0] = origin;
        trace.Ends[0] = target;
        TraceLines(trace, EveryTypeGroup | TerrainGroup);
        WorldIntersection met = trace.Intersections[0];
        if (!met.IsHit)
        {
            hit = default;
            return false;
        }

        hit = new WorldRayHit(met.Position, met.Normal, met.Distance, met.EnterMaterial ?? 0);
        return true;
    }

    /// <summary>Builds the collision world for a level's placements and ground.</summary>
    /// <param name="scene">What the level places.</param>
    /// <param name="models">Resolves a model name to its mesh, or <see langword="null"/> when nothing ships it.</param>
    /// <param name="ground">The level's ground, or <see langword="null"/>.</param>
    /// <param name="terrainOwner">What a hit on the ground reports as struck.</param>
    /// <param name="forests">The level's stands of trees, or <see langword="null"/> for none.</param>
    /// <param name="magic">The magic object types attach points are looked up in, or <see langword="null"/> to take every name as a model.</param>
    /// <returns>The world.</returns>
    /// <remarks>
    /// <para>
    /// Every static placement becomes an object at the pose its parameters give through <see cref="Igi2Pose"/>, and
    /// so does every model its model attaches, recursively. That is <c>ModelObj_CreateAttachObject</c>
    /// (<c>igi2 0x56D0B0</c>): an attachment's name is looked up in the magic table (<see cref="World.Igi2MagicObjTypes"/>), which gives
    /// the model it wears; a name it does not hold is used as a model; a shadow volume or a mark is no solid.
    /// </para>
    /// <para>
    /// A model with bones becomes a <c>BoneMagicObj</c>, which does not answer ground queries, so it is left out.
    /// Its own attachments are still followed.
    /// </para>
    /// <para>
    /// A straight spline segment becomes an <see cref="Igi2SplineSegment"/>, its radius taken over the model's levels
    /// of detail; one bent along its curve has its meshes bent along it (<see cref="Igi2SplineSegment.Bent"/>).
    /// </para>
    /// </remarks>
    public static Igi2CollisionWorld Build(
        Igi2WorldScene scene,
        Func<string, MefModel?> models,
        Igi2TerrainSurface? ground,
        object terrainOwner,
        Igi2Forest? forests = null,
        World.Igi2MagicObjTypes? magic = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(terrainOwner);

        List<Igi2RigidObject> objects = [];
        List<Igi2SplineSegment> segments = [];
        foreach (Igi2Placement placement in scene.StaticPlacements)
        {
            if (placement.Geometry is Igi2SplineSweep sweep)
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
            string root = string.Create(CultureInfo.InvariantCulture, $"{placement.Model}-{placement.LevelId}");
            WalkKeyed(models, placement.Model, pose, depth: 0, root, null, null, step =>
            {
                (MefModel model, string modelName, Igi2Pose at, int depth) = (step.Model, step.ModelName, step.Pose, step.Depth);
                if (model.BoneHierarchy is null && Meshes(model) is { } meshes)
                {
                    Igi2RigidObject solid = new(
                        new Igi2CollisionOwner(placement.TypeName, placement.LevelId, modelName, depth)
                        {
                            GlassKey = step.Type?.TaskType == GlassType ? step.Key : null,
                            IsSector = step.Type is null && depth > 0 && model.IsSector
                                && string.Equals(placement.TypeName, World.Igi2BuildingTask.TypeName, StringComparison.Ordinal),
                        },
                        meshes,
                        model.NativeMarkerBound,
                        isBuildingMesh: false)
                    {
                        Position = at.Position,
                    };
                    for (int i = 0; i < solid.Orientation.Length; i++)
                    {
                        solid.Orientation[i] = at.Orientation[i];
                    }

                    objects.Add(solid);
                }
            }, magic);

            // A security camera's lens is a task of its own, SCameraLens, its model at the holder's first vertex turned by its own
            // tilt and turn; it is a moving object, which bullets strike and sight never meets.
            if (string.Equals(placement.TypeName, LensType, StringComparison.Ordinal)
                && placement.MountedModel.Length > 0
                && models(placement.MountedModel) is { BoneHierarchy: null } lens
                && Meshes(lens) is { } lensMeshes)
            {
                // At rest it stands where the world draws it; its own tick turns it from then on.
                Matrix3f holder = WorldScene.ToOrientation(placement.Orientation);
                Vector3d at = placement.Position;
                if (models(placement.Model)?.TryGetMagicVertex(0, out Vector3f mount) == true)
                {
                    Vector3f offset = holder.Transposed().Transform(mount);
                    at = new Vector3d(at.X + offset.X, at.Y + offset.Y, at.Z + offset.Z);
                }

                IReadOnlyList<float> lensFrame = Igi2Pose.FromMatrix(at, WorldScene.ToOrientation(placement.MountedOrientation)).Orientation;
                Igi2RigidObject solid = new(
                    new Igi2CollisionOwner(placement.TypeName, placement.LevelId, placement.MountedModel, 1),
                    lensMeshes,
                    lens.NativeMarkerBound,
                    isBuildingMesh: false)
                {
                    Position = at,
                };
                for (int i = 0; i < solid.Orientation.Length; i++)
                {
                    solid.Orientation[i] = lensFrame[i];
                }

                objects.Add(solid);
            }
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

            Igi2CollisionOwner owner = new(placement.TypeName, placement.LevelId, placement.Model, 0);
            Igi2SplineSegment? made = sweep.IsLinear
                ? Igi2SplineSegment.Straight(owner, sweep.Start, sweep.Stop, sweep.IsFlipped, meshes, radius)
                : Igi2SplineSegment.Bent(owner, sweep, meshes);
            if (made is { } segment)
            {
                segments.Add(segment);
            }
        }

    }

    /// <summary>The solids one placement was built as, its own first and then its attachments', in the order they were built.</summary>
    /// <param name="typeName">The placing task's type.</param>
    /// <param name="levelId">The placing task's level id.</param>
    /// <returns>The solids; none for a placement that has no collision.</returns>
    public IReadOnlyList<Igi2RigidObject> SolidsOf(string typeName, int levelId) =>
        [.. _objects.Where(o => o.Owner is Igi2CollisionOwner owner && owner.LevelId == levelId && owner.TypeName == typeName)];

    /// <summary>
    /// Moves the solids one placement was built as to a new pose of that placement, each attachment turned and carried
    /// with it as it was when the level was built (<c>ModelObj_CreateAttachObject</c>, <c>igi2 0x56D0B0</c>).
    /// </summary>
    /// <param name="solids">The placement's solids, as <see cref="SolidsOf"/> gives them.</param>
    /// <param name="models">Finds a model by name.</param>
    /// <param name="modelName">The placement's model.</param>
    /// <param name="pose">The placement's new pose.</param>
    /// <param name="magic">The magic object types attach points are looked up in, as the level was built with.</param>
    public static void Repose(
        IReadOnlyList<Igi2RigidObject> solids,
        Func<string, MefModel?> models,
        string modelName,
        Igi2Pose pose,
        World.Igi2MagicObjTypes? magic = null)
    {
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(pose);
        int next = 0;
        WalkAttachments(models, modelName, pose, depth: 0, (_, name, at, depth) =>
        {
            // Build made a solid of only some of the models the walk visits; the next one it made is the next one met
            // that it could have been made of.
            if (next < solids.Count && solids[next].Owner is Igi2CollisionOwner owner && owner.Model == name && owner.Depth == depth)
            {
                Igi2RigidObject solid = solids[next++];
                solid.Position = at.Position;
                for (int i = 0; i < solid.Orientation.Length; i++)
                {
                    solid.Orientation[i] = at.Orientation[i];
                }
            }
        }, magic);
    }

    /// <summary>
    /// The objects the static placements' attach points make of a magic type, <c>ModelObj_CreateAttachObject</c>: each type the table
    /// holds, where its point puts it, with the placement it hangs off and the solid it is made of.
    /// </summary>
    /// <param name="scene">What the level places.</param>
    /// <param name="models">Resolves a model name to its mesh.</param>
    /// <param name="magic">The magic object types.</param>
    /// <returns>The objects, in the walk's order.</returns>
    public static IEnumerable<Igi2MagicPlacement> MagicObjects(Igi2WorldScene scene, Func<string, MefModel?> models, World.Igi2MagicObjTypes magic)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(magic);
        List<Igi2MagicPlacement> found = [];
        foreach (Igi2Placement placement in scene.StaticPlacements)
        {
            if (placement.Geometry is not null)
            {
                continue;
            }

            Igi2Pose pose = placement.Frame is { } frame
                ? Igi2Pose.FromMatrix(placement.Position, frame)
                : Igi2Pose.FromAngles(placement.Position, placement.Orientation);
            Collect(placement.Model, pose, 0);

            void Collect(string modelName, Igi2Pose at, int depth)
            {
                if (depth >= MaxAttachmentDepth || models(modelName) is not { } model)
                {
                    return;
                }

                foreach (MefAttachment attachment in model.Attachments)
                {
                    Igi2Pose attached = at.Attach(attachment);
                    if (magic.Find(attachment.Name) is { } type)
                    {
                        found.Add(new Igi2MagicPlacement(type, attached, new Igi2CollisionOwner(placement.TypeName, placement.LevelId, type.Model, depth + 1)));
                    }

                    if (magic.ModelOf(attachment.Name) is { } next)
                    {
                        Collect(next, attached, depth + 1);
                    }
                }
            }
        }

        return found;
    }

    // A placed model and everything hanging off it, depth first: the model, then each of its attachments in turn.
    private static void WalkAttachments(
        Func<string, MefModel?> models,
        string modelName,
        Igi2Pose pose,
        int depth,
        Action<MefModel, string, Igi2Pose, int> visit,
        World.Igi2MagicObjTypes? magic = null) =>
        WalkKeyed(models, modelName, pose, depth, string.Empty, null, null, step => visit(step.Model, step.ModelName, step.Pose, step.Depth), magic);

    // The walk with what made each attached model: its magic type, and the key GlassContainer_MakeHashKey gives an object made at that
    // attach point -- its index among its owner's points, then the owner's chain. A model's chain is its name, its index in what it
    // hangs off, M- where that is the placed model's container, and so up to the placed model's name and level id.
    private static void WalkKeyed(
        Func<string, MefModel?> models,
        string modelName,
        Igi2Pose pose,
        int depth,
        string chain,
        World.Igi2MagicObjType? type,
        string? key,
        Action<WalkStep> visit,
        World.Igi2MagicObjTypes? magic = null)
    {
        MefModel? model = models(modelName);
        if (model is null)
        {
            return;
        }

        visit(new WalkStep(model, modelName, pose, depth, type, key));
        if (depth >= MaxAttachmentDepth)
        {
            return;
        }

        for (int i = 0; i < model.Attachments.Count; i++)
        {
            MefAttachment attachment = model.Attachments[i];

            // What the point becomes, out of the magic table: a shadow volume or a mark is stood on by nothing.
            if ((magic is null ? attachment.Name : magic.ModelOf(attachment.Name)) is { } attached)
            {
                string index = i.ToString(CultureInfo.InvariantCulture);
                WalkKeyed(
                    models,
                    attached,
                    pose.Attach(attachment),
                    depth + 1,
                    $"{attached}-{index}-{(depth == 0 ? "M-" : string.Empty)}{chain}",
                    magic?.Find(attachment.Name),
                    $"{index}-{chain}",
                    visit,
                    magic);
            }
        }
    }

    // One model the walk meets: the model, where it stands, how deep, the magic type that made it and its key.
    private readonly record struct WalkStep(MefModel Model, string ModelName, Igi2Pose Pose, int Depth, World.Igi2MagicObjType? Type, string? Key);

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
    /// <remarks>
    /// Each end is the start's height plus its distance, added at the FPU's 24 bits (<c>fadd qword</c>, <c>igi2 0x551342</c>).
    /// <c>World_GetNextZNoMaterial</c> (<c>0x550BE0</c>), which physics probes the ground with, is the same query.
    /// </remarks>
    public void NextZ(WorldLineTrace trace, ReadOnlySpan<double> distances, uint groups)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (distances.Length != trace.Count)
        {
            throw new ArgumentException("One distance per line.", nameof(distances));
        }

        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i];
            trace.Ends[i] = new Vector3d(start.X, start.Y, ((Fpu)distances[i] + start.Z).Value);
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
    /// <remarks>
    /// When every line is vertical the objects are asked with the ground query's event instead (<c>isTraceAlongZ</c>,
    /// <c>igi2 0x5516CF</c>), which a stand of trees answers by its ground handler; the ground itself always takes the line
    /// trace. <c>World_TraceLines</c> (<c>0x551640</c>), which physics traces its contact points with, is this at mesh 0.
    /// </remarks>
    public void TraceLines(WorldLineTrace trace, uint groups)
    {
        ArgumentNullException.ThrowIfNull(trace);
        bool alongZ = true;
        for (int i = 0; i < trace.Count && alongZ; i++)
        {
            alongZ = trace.Starts[i].X == trace.Ends[i].X && trace.Starts[i].Y == trace.Ends[i].Y;
        }

        TraceObjects(trace, groups & ~TerrainGroup, lines: !alongZ);
        if ((groups & TerrainGroup) != 0)
        {
            _terrain?.TraceLines(trace);
        }
    }

    /// <summary>
    /// A line of sight through the objects of some groups and, when asked, the ground: <c>World_LineOfSight</c>
    /// (<c>igi2 0x551F60</c>).
    /// </summary>
    /// <param name="sight">The line, what it may meet, and its running results.</param>
    /// <param name="groups">
    /// The query groups: an object takes part when its type's group shares a bit with them, and
    /// <see cref="TerrainGroup"/> asks for the ground.
    /// </param>
    /// <returns><see langword="true"/> when the line crossed no face.</returns>
    /// <remarks>
    /// <para>
    /// The objects of the stationary tree along the line answer first, each with its own handler: the solid models
    /// (<see cref="Igi2RigidLineOfSight"/>), the spline segments (<see cref="Igi2SplineSegmentLineOfSight"/>) and the
    /// stands of trees (<see cref="Igi2ForestCollider.LineOfSight"/>) and the boned bodies (<see cref="Igi2BoneLineOfSight"/>); a
    /// line of no length meets none. Then, if anything
    /// still gets through and the ground is asked for, the line is traced against the ground alone, and striking it
    /// blocks the sight.
    /// </para>
    /// <para>
    /// The original walks its octree's cubes along the line. Each handler tests its own bounds, so a walk over every
    /// object meets the same faces; only the order can differ, and with it which of two materials crossed at exactly
    /// the same distance is kept.
    /// </para>
    /// </remarks>
    public bool LineOfSight(WorldLineOfSight sight, uint groups)
    {
        ArgumentNullException.ThrowIfNull(sight);
        uint objectGroups = groups & ~TerrainGroup;
        if (objectGroups != 0 && sight.Start != sight.End)
        {
            foreach (Igi2RigidObject solid in _objects)
            {
                if ((objectGroups & (solid.Group | EveryTypeGroup)) != 0 && !PassesSight(solid.Owner))
                {
                    Igi2RigidLineOfSight.Meet(solid, sight);
                }
            }

            foreach (Igi2SplineSegment segment in _segments)
            {
                if ((objectGroups & (segment.Group | EveryTypeGroup)) != 0)
                {
                    Igi2SplineSegmentLineOfSight.Meet(segment, sight);
                }
            }

            foreach (Igi2ForestCollider forest in _forests)
            {
                if ((objectGroups & (forest.Group | EveryTypeGroup)) != 0)
                {
                    forest.LineOfSight(sight);
                }
            }

            foreach (Igi2BoneCollider body in _bodies)
            {
                if ((objectGroups & (body.Group | EveryTypeGroup)) != 0)
                {
                    Igi2BoneLineOfSight.Meet(body, sight);
                }
            }

            foreach (IWorldLineCollider other in _others)
            {
                if ((objectGroups & (other.Group | EveryTypeGroup)) != 0 && (sight.Includes is not { } passes || passes(other.Owner)))
                {
                    other.LineOfSight(sight);
                }
            }
        }

        if (sight.Transparency != 0f && (groups & TerrainGroup) != 0 && _terrain is not null)
        {
            WorldLineTrace ground = new(1);
            ground.Starts[0] = sight.Start;
            ground.Ends[0] = sight.End;
            TraceLines(ground, TerrainGroup);
            if (ground.Intersections[0].IsHit)
            {
                sight.Transparency = 0f;
                sight.IsHit = true;
            }
        }

        return sight.IsClear;
    }

    /// <summary>Whether a placed object's type has no line-of-sight handler; an attachment is a magic object of its own and has one.</summary>
    private static bool PassesSight(object owner) =>
        owner is Igi2CollisionOwner placed
        && ((placed.Depth == 0 && SightThrough.Contains(placed.TypeName)) || string.Equals(placed.TypeName, LensType, StringComparison.Ordinal));

    /// <summary>
    /// The object half of a line trace, <c>World_TraceLinesSpecificMesh</c> (<c>igi2 0x5519C0</c>): every result is reset
    /// to the line's end, and every object whose bounds meet the lines' padded box is traced.
    /// </summary>
    private void TraceObjects(WorldLineTrace trace, uint groups, bool lines)
    {
        Vector3d min = trace.Starts[0], max = trace.Starts[0];
        for (int i = 0; i < trace.Count; i++)
        {
            Grow(trace.Starts[i]);
            Grow(trace.Ends[i]);
            WorldIntersection hit = trace.Intersections[i];
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
                if (lines)
                {
                    solid.Traced?.Invoke(trace);
                }
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

        // Water answers only line traces (Water_TraceLinesHandler), in its own group.
        if (lines && (groups & (World.Igi2Water.Group | EveryTypeGroup)) != 0)
        {
            foreach (World.Igi2WaterBody water in _waters)
            {
                Vector3d p = water.Position;
                double r = water.BoundingRadius;
                if (max.X >= p.X - r && min.X <= r + p.X
                    && max.Y >= p.Y - r && min.Y <= r + p.Y
                    && p.Z - r <= max.Z && min.Z <= r + p.Z)
                {
                    water.TraceLines(trace);
                }
            }
        }

        // A boned object answers only line traces, and tests its own sphere against each line.
        if (lines)
        {
            foreach (Igi2BoneCollider body in _bodies)
            {
                if ((groups & (body.Group | EveryTypeGroup)) != 0)
                {
                    body.TraceLines(trace);
                }
            }

            foreach (IWorldLineCollider other in _others)
            {
                if ((groups & (other.Group | EveryTypeGroup)) != 0 && (trace.Includes is not { } includes || includes(other.Owner)))
                {
                    other.TraceLines(trace);
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
