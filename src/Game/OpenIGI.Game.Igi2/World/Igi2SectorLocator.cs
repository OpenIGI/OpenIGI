using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Which sector a point stands in, as a sector object answers <c>QCube_eGetSectorQTaskEvent</c>
/// (<c>SectorObject_GetSectorHandler</c>, sectorobject.c): a line down and a line up from the point, each twice the object's bounding
/// radius long, are traced against the sector object's own mesh with its portals; the point is inside when both strike it, and of
/// several sectors the one struck closest wins.
/// </summary>
public sealed class Igi2SectorLocator
{
    /// <summary>How far above the point the lines start when asked to lift them, <c>409.6</c>.</summary>
    public const double Lift = 409.6000061035156;

    private readonly Igi2CollisionWorld _world;
    private readonly List<(Igi2Sector Sector, Igi2RigidObject Solid)> _sectors = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2SectorLocator"/> class.</summary>
    /// <param name="world">The collision world, which holds each sector object's mesh.</param>
    /// <param name="graph">The level's sectors.</param>
    public Igi2SectorLocator(Igi2CollisionWorld world, Igi2SectorGraph graph)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(graph);
        foreach (Igi2Sector sector in graph.Sectors)
        {
            Igi2Placement placement = sector.Placement;
            foreach (Igi2RigidObject solid in world.SolidsOf(placement.TypeName, placement.LevelId))
            {
                if (solid.Owner is Igi2CollisionOwner { Depth: 0 })
                {
                    _sectors.Add((sector, solid));
                    break;
                }
            }
        }
    }

    /// <summary>The sector a point stands in, or <see langword="null"/> when it stands in the open.</summary>
    /// <param name="point">The point.</param>
    /// <param name="lift">Whether the lines start a tenth of a metre above it.</param>
    /// <returns>The sector.</returns>
    public Igi2Sector? SectorAt(Vector3d point, bool lift = false)
    {
        Igi2Sector? found = null;
        double best = double.MaxValue;
        double start = lift ? point.Z + Lift : point.Z;
        Igi2LineTrace trace = new(2) { Mesh = 0, TracePortals = true };
        foreach ((Igi2Sector sector, Igi2RigidObject solid) in _sectors)
        {
            double reach = 2.0 * solid.BoundingRadius;
            trace.Starts[0] = new Vector3d(point.X, point.Y, start);
            trace.Starts[1] = trace.Starts[0];
            trace.Ends[0] = new Vector3d(point.X, point.Y, point.Z - reach);
            trace.Ends[1] = new Vector3d(point.X, point.Y, point.Z + reach);
            trace.Includes = owner => ReferenceEquals(owner, solid.Owner);
            _world.TraceLines(trace, solid.Group);
            Igi2Intersection down = trace.Intersections[0], up = trace.Intersections[1];
            if (down.IsHit && up.IsHit && ReferenceEquals(down.HitObject, solid) && ReferenceEquals(up.HitObject, solid))
            {
                double distance = Math.Min(down.Distance, up.Distance);
                if (found is null || distance < best)
                {
                    found = sector;
                    best = distance;
                }
            }
        }

        return found;
    }
}
