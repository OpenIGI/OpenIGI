using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Which room of which building a point stands in: <c>QCube_eGetSectorQTaskEvent</c>, which every sector object answers with
/// <c>SectorObject_GetSectorHandler</c> (<c>e/3/sectorobject.c</c>).
/// </summary>
/// <remarks>
/// <para>
/// A building asks each of its rooms in turn: two lines from the point, one straight down and one straight up, each twice the
/// building's registered radius long, traced against that room alone with its portals counted as surfaces. The point is in
/// the room when both strike it; of every room so placed, in every building near the point, the nearest strike wins.
/// </para>
/// <para>
/// <b>Rooms, not shells.</b> The shell is the building's outside and is never answered. A point inside a building that carries
/// no room is in no sector -- which is what the original's cull reads: such a building leaves its surroundings drawn.
/// </para>
/// </remarks>
public sealed class Igi2SectorLocator
{
    /// <summary>How far above the point the lines start when the query is made with tolerance, in world units.</summary>
    /// <remarks>
    /// <c>409.6000061035156</c>, the double the original adds to both starts when <c>GetSectorContext_t.isTolerance</c> is set. The
    /// camera asks without it (<c>ViewPortQTask_FindSector</c>); an object registering into a room and a human asks with it.
    /// </remarks>
    public const double Lift = 409.6000061035156;

    private readonly Igi2CollisionWorld _world;
    private readonly List<Candidate> _objects = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2SectorLocator"/> class over a level's buildings and their solids.</summary>
    /// <param name="world">The collision world, which holds each room's solid.</param>
    /// <param name="graph">The level's sector objects.</param>
    public Igi2SectorLocator(Igi2CollisionWorld world, Igi2SectorGraph graph)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(graph);
        foreach (Igi2SectorObject building in graph.Objects)
        {
            if (building.Rooms.Count == 0)
            {
                continue;
            }

            IReadOnlyList<Igi2RigidObject> solids = world.SolidsOf(building.Shell.TypeName, building.Shell.LevelId);
            List<(Igi2Sector Room, Igi2RigidObject Solid)> rooms = [];
            foreach (Igi2Sector room in building.Rooms)
            {
                if (SolidOf(solids, room) is { } solid)
                {
                    rooms.Add((room, solid));
                }
            }

            if (rooms.Count > 0)
            {
                _objects.Add(new Candidate(building, rooms));
            }
        }
    }

    /// <summary>Finds the solid a sector was built as among its placement's solids.</summary>
    /// <param name="solids">The placement's solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    /// <param name="sector">The sector.</param>
    /// <returns>The solid, or <see langword="null"/> when the collision world made none of it.</returns>
    /// <remarks>
    /// The shell is the placement's own model at depth nought. A room is the attach object of its model standing where the room
    /// stands: two rooms of one model in one building are told apart by place, which is all that separates them.
    /// </remarks>
    public static Igi2RigidObject? SolidOf(IReadOnlyList<Igi2RigidObject> solids, Igi2Sector sector)
    {
        ArgumentNullException.ThrowIfNull(solids);
        ArgumentNullException.ThrowIfNull(sector);
        foreach (Igi2RigidObject solid in solids)
        {
            if (solid.Owner is not Igi2CollisionOwner owner
                || !string.Equals(owner.Model, sector.Model, StringComparison.OrdinalIgnoreCase)
                || (owner.Depth == 0) != sector.IsShell)
            {
                continue;
            }

            if (sector.IsShell || solid.Position == sector.Position)
            {
                return solid;
            }
        }

        return null;
    }

    /// <summary>Finds the room a point stands in.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <param name="lift">Whether the lines start <see cref="Lift"/> above it, the query made with tolerance.</param>
    /// <returns>The room, or <see langword="null"/> when the point is in none.</returns>
    public Igi2Sector? SectorAt(Vector3d point, bool lift = false)
    {
        Igi2Sector? found = null;
        double best = double.MaxValue;
        double start = lift ? point.Z + Lift : point.Z;
        WorldLineTrace trace = new(2) { Mesh = 0, TracePortals = true };
        foreach (Candidate candidate in _objects)
        {
            // World_TriggerQTaskEventXyzSpecificTree asks only the sector objects whose cube holds the point; a building's cube
            // is at least its registered sphere, and a line twice that radius cannot reach a room from outside it.
            double reach = 2.0 * candidate.Building.Radius;
            if ((point - candidate.Building.Shell.Position).LengthSquared > reach * reach)
            {
                continue;
            }

            foreach ((Igi2Sector room, Igi2RigidObject solid) in candidate.Rooms)
            {
                trace.Starts[0] = new Vector3d(point.X, point.Y, start);
                trace.Starts[1] = trace.Starts[0];
                trace.Ends[0] = new Vector3d(point.X, point.Y, point.Z - reach);
                trace.Ends[1] = new Vector3d(point.X, point.Y, point.Z + reach);
                trace.Includes = owner => ReferenceEquals(owner, solid.Owner);
                _world.TraceLines(trace, solid.Group);
                WorldIntersection down = trace.Intersections[0], up = trace.Intersections[1];
                if (down.IsHit && up.IsHit && ReferenceEquals(down.HitObject, solid) && ReferenceEquals(up.HitObject, solid))
                {
                    double distance = Math.Min(down.Distance, up.Distance);
                    if (found is null || distance < best)
                    {
                        found = room;
                        best = distance;
                    }
                }
            }
        }

        return found;
    }

    private sealed record Candidate(Igi2SectorObject Building, IReadOnlyList<(Igi2Sector Room, Igi2RigidObject Solid)> Rooms);
}
