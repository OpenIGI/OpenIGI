using System.Collections.ObjectModel;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

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

    /// <summary>Engine units per metre, the original's <c>WORLD_METER</c> script constant.</summary>
    /// <remarks>
    /// Registered as a double at <c>0x539558</c>. Model vertices use the same units as world positions:
    /// the level-1 water tower measures 95005 units tall, which is 23 metres.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The task parameter naming an object's model.</summary>
    public const string ModelParameter = "Model";

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
        int unresolved,
        int unplaced,
        int attached)
    {
        Mission = mission;
        Placements = placements;
        UnresolvedCount = unresolved;
        UnplacedCount = unplaced;
        AttachedCount = attached;
    }

    /// <summary>Gets the mission the scene was built from.</summary>
    public LoadedMission Mission { get; }

    /// <summary>Gets everything to draw, in the order the level script placed it.</summary>
    public IReadOnlyList<WorldPlacement> Placements { get; }

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

        // Which baked-lightmap set each placing task belongs to, and where in it the task's own model
        // sits. Both come from expanding the owner's whole subtree at once, because the numbering is a
        // single running count across it — see WorldObject.
        Dictionary<GameTask, (string Set, WorldObject Root)> lightmapped = [];
        if (magic is not null)
        {
            BindLightmapSets(levelRoot, mission, magic, lightmapped);
        }

        List<WorldPlacement> placements = [];
        int unresolved = 0;
        int unplaced = 0;
        int attached = 0;

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask declared
                || !declared.TryGetValue(ModelParameter, out object? rawModel)
                || rawModel is not string name
                || name.Length == 0)
            {
                continue;
            }

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

            lightmapped.TryGetValue(task, out (string Set, WorldObject Root) bake);
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
            // available when a magic-object table was supplied; without one a level draws its shells and
            // none of their contents.
            if (bake.Root is not null)
            {
                attached += AddAttached(bake.Root, position, orientation, bake.Set ?? string.Empty, task.Name, byName, placements);
            }
            else if (magic is not null
                && WorldObject.TryBuild(name, mission.TryGetModel, magic, out WorldObject? loose)
                && loose is not null)
            {
                attached += AddAttached(loose, position, orientation, string.Empty, task.Name, byName, placements);
            }
        }

        log.Information(
            LogCategory,
            $"Scene: {placements.Count} placement(s) over {byName.Count} bound instance(s)"
                + $", {attached} attached"
                + (unresolved > 0 ? $", {unresolved} unresolved" : string.Empty)
                + (unplaced > 0 ? $", {unplaced} with no position" : string.Empty)
                + ".");

        return new WorldScene(
            mission,
            new ReadOnlyCollection<WorldPlacement>(placements),
            unresolved,
            unplaced,
            attached);
    }

    /// <summary>
    /// Expands every lightmap owner's subtree and records, per placing task, which set it belongs to and
    /// the tree its own model heads.
    /// </summary>
    private static void BindLightmapSets(
        GameTask levelRoot,
        LoadedMission mission,
        MagicObjectRegistry magic,
        Dictionary<GameTask, (string Set, WorldObject Root)> bound)
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
                if (task is DeclaredTask declared
                    && declared.TryGetValue(ModelParameter, out object? raw)
                    && raw is string name
                    && name.Length > 0)
                {
                    placing.Add(task);
                    models.Add(name);
                }
            }

            IReadOnlyList<WorldObject?> roots = WorldObject.BuildAll(models, mission.TryGetModel, magic);
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] is { } root)
                {
                    bound[placing[i]] = (set, root);
                }
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
        List<WorldPlacement> placements)
    {
        int added = 0;
        foreach (WorldObject child in node.Children)
        {
            Vector3f offset = orientation.Transform(child.Position);
            Vector3d childPosition = new(
                position.X + (offset.X / UnitsPerMetre),
                position.Y + (offset.Y / UnitsPerMetre),
                position.Z + (offset.Z / UnitsPerMetre));
            Matrix3f childOrientation = orientation * child.Orientation;

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

            added += AddAttached(child, childPosition, childOrientation, set, taskName, byName, placements);
        }

        return added;
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
    /// concatenates three axis rotations in <c>sub_4B38E0</c>. <b>That function's axis order is an open
    /// item</b>, so this composes Z(gamma)·Y(beta)·X(alpha) — which is right for the overwhelming
    /// majority of level-1 placements, where alpha and beta are zero and only gamma turns the object
    /// about the vertical axis. Objects that tilt are the ones to check once the order is pinned.
    /// </remarks>
    public static Matrix3f ToOrientation(Vector3f angles)
    {
        Matrix3f x = RotationX(angles.X);
        Matrix3f y = RotationY(angles.Y);
        Matrix3f z = RotationZ(angles.Z);
        return z * y * x;
    }

    private static Matrix3f RotationX(float a) => Matrix3f.FromRows(
        new Vector3f(1f, 0f, 0f),
        new Vector3f(0f, MathF.Cos(a), -MathF.Sin(a)),
        new Vector3f(0f, MathF.Sin(a), MathF.Cos(a)));

    private static Matrix3f RotationY(float a) => Matrix3f.FromRows(
        new Vector3f(MathF.Cos(a), 0f, MathF.Sin(a)),
        new Vector3f(0f, 1f, 0f),
        new Vector3f(-MathF.Sin(a), 0f, MathF.Cos(a)));

    private static Matrix3f RotationZ(float a) => Matrix3f.FromRows(
        new Vector3f(MathF.Cos(a), -MathF.Sin(a), 0f),
        new Vector3f(MathF.Sin(a), MathF.Cos(a), 0f),
        new Vector3f(0f, 0f, 1f));

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
