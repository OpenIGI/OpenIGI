using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Terrain;

/// <summary>What a terrain brush does to the objects standing on the ground it shapes.</summary>
internal enum TerrainObjectMode
{
    /// <summary>Objects rise and sink with the ground under them, and what they carry goes with them.</summary>
    MoveWithGround,

    /// <summary>The ground under and around objects is left as it is.</summary>
    Protect,

    /// <summary>Objects stay where they are, whatever happens to the ground.</summary>
    Ignore,
}

/// <summary>
/// The objects standing on the ground when a terrain stroke starts: where the ground was under each, and the footprint the
/// brush keeps off when it protects them.
/// </summary>
internal sealed class GroundedObjects
{
    // How far off the ground an object's origin may be and still stand on it, and how far round its footprint is kept.
    private const double StandingMetres = 1.5;
    private const double MarginMetres = 1.0;

    private readonly List<(MissionTask Task, double Ground)> _standing = [];
    private readonly List<BoxGizmo.Box> _footprints = [];

    private GroundedObjects()
    {
    }

    /// <summary>Gets how many objects stand on the ground.</summary>
    public int Count => _standing.Count;

    /// <summary>Finds the objects standing on the ground.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="live">The drawn world, for the objects' footprints.</param>
    /// <param name="terrain">The ground.</param>
    /// <returns>The objects, and the ground under each as it is now.</returns>
    public static GroundedObjects Capture(MissionDocument document, LiveWorldLink live, IEditableTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(terrain);
        GroundedObjects grounded = new();
        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (MissionGraphs.IsGraph(task) || TaskPresentation.PositionMetres(task) is not { } at
                || !terrain.TryGetHeight(at.X, at.Y, out double ground) || Math.Abs(at.Z - ground) > StandingMetres)
            {
                continue;
            }

            grounded._standing.Add((task, ground));
            grounded._footprints.AddRange(live.Boxes(task));
        }

        return grounded;
    }

    /// <summary>Gets whether a point of the ground is under or beside an object, which a protecting brush leaves alone.</summary>
    /// <param name="xMetres">The point's X, in world metres.</param>
    /// <param name="yMetres">The point's Y, in world metres.</param>
    /// <returns>Whether the point is within an object's footprint and its margin.</returns>
    public bool Protects(double xMetres, double yMetres)
    {
        foreach (BoxGizmo.Box box in _footprints)
        {
            Vector3d local = box.Orientation.Transform(new Vector3d(xMetres - box.Centre.X, yMetres - box.Centre.Y, 0.0));
            if (Math.Abs(local.X) <= box.Half.X + MarginMetres && Math.Abs(local.Y) <= box.Half.Y + MarginMetres)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Makes the steps that move each standing object by how far the ground under it moved, with what it carries.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="live">The drawn world.</param>
    /// <param name="terrain">The ground, shaped.</param>
    /// <returns>One position step per object that moves.</returns>
    public IReadOnlyList<IEditorCommand> Follow(MissionDocument document, LiveWorldLink live, IEditableTerrain terrain)
    {
        List<IEditorCommand> moves = [];
        HashSet<MissionTask> moved = new(ReferenceEqualityComparer.Instance);
        foreach ((MissionTask task, double before) in _standing)
        {
            if (TaskPresentation.PositionMetres(task) is not { } at || !terrain.TryGetHeight(at.X, at.Y, out double after)
                || Math.Abs(after - before) < 0.01 || moved.Contains(task))
            {
                continue;
            }

            double rise = after - before;
            foreach (MissionTask each in ObjectCarry.Carried(document, live, [task]).Prepend(task))
            {
                if (moved.Add(each) && TaskPresentation.Position(each) is { } position && TaskPresentation.PositionMetres(each) is { } from)
                {
                    moves.Add(SetParameterCommand.Numbers(
                        document, each, position.Name, [WorldUnits.FromMetres(from.X), WorldUnits.FromMetres(from.Y), WorldUnits.FromMetres(from.Z + rise)]));
                }
            }
        }

        return moves;
    }
}
