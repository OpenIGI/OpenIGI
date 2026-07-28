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

    private WorldScene(
        LoadedMission mission,
        IReadOnlyList<WorldPlacement> placements,
        int unresolved,
        int unplaced)
    {
        Mission = mission;
        Placements = placements;
        UnresolvedCount = unresolved;
        UnplacedCount = unplaced;
    }

    /// <summary>Gets the mission the scene was built from.</summary>
    public LoadedMission Mission { get; }

    /// <summary>Gets everything to draw, in the order the level script placed it.</summary>
    public IReadOnlyList<WorldPlacement> Placements { get; }

    /// <summary>Gets how many placing tasks named a model no pack holds.</summary>
    public int UnresolvedCount { get; }

    /// <summary>Gets how many tasks named a model but declared no position this recognises.</summary>
    public int UnplacedCount { get; }

    /// <summary>Walks a level's task tree and joins every placed model to its pack instance.</summary>
    /// <param name="mission">The loaded mission, with both packs bound.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="logger">Where to report placements that do not resolve.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static WorldScene Build(LoadedMission mission, GameTask levelRoot, IIgiLogger? logger = null)
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

        List<WorldPlacement> placements = [];
        int unresolved = 0;
        int unplaced = 0;

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

            placements.Add(new WorldPlacement(
                position,
                rawOrientation is Vector3f angles ? ToOrientation(angles) : Matrix3f.Identity,
                bound.Instance,
                bound.Pack,
                bound.Model,
                task.Name));
        }

        log.Information(
            LogCategory,
            $"Scene: {placements.Count} placement(s) over {byName.Count} bound instance(s)"
                + (unresolved > 0 ? $", {unresolved} unresolved" : string.Empty)
                + (unplaced > 0 ? $", {unplaced} with no position" : string.Empty)
                + ".");

        return new WorldScene(mission, new ReadOnlyCollection<WorldPlacement>(placements), unresolved, unplaced);
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
