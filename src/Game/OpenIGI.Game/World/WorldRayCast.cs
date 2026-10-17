using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Where a ray met a surface.</summary>
/// <param name="Position">The point of contact, in world units.</param>
/// <param name="Normal">The surface's outward normal, unit length.</param>
/// <param name="Distance">How far along the ray the contact sits, in world units.</param>
/// <param name="Material">
/// Which material the struck face is made of — the fourth field of its
/// <see cref="Formats.World.MefCollisionFace"/>, and a row of the game material table.
/// </param>
/// <remarks>
/// <para>
/// <b>The material is what decides what a hit looks like.</b> The original never lets a bullet choose
/// its own mark: <c>0x4083C0</c> packages the impact and dispatches it to the <i>material's</i> own
/// task class, so a round through glass and into brick leaves two different marks without the shot
/// knowing about either. Carrying the index here is what makes that lookup possible at all.
/// </para>
/// <para>
/// <b>Which side was crossed is carried for the same reason.</b> The original's surface test
/// (<c>0x4CD300</c>) reports an entry row and an exit row as two complementary flags —
/// <c>*a7 = c0; *a8 = !c0</c>, so exactly one is ever set — and the shot resolution branches on
/// which: an entry crossing sounds and marks the surface, an exit crossing leaves only the exit
/// decal and no sound at all. The test itself is the face's own facing against the ray, which is
/// computed here anyway in order to turn the normal towards the ray.
/// </para>
/// </remarks>
/// <param name="IsExit">
/// Whether the ray crossed the surface's BACK face — leaving solid rather than entering it.
/// </param>
public readonly record struct WorldRayHit(
    Vector3d Position,
    Vector3f Normal,
    double Distance,
    int Material = 0,
    bool IsExit = false);

/// <summary>Casts a ray at the world and reports the nearest surface it meets.</summary>
/// <param name="origin">Where the ray starts, in world units.</param>
/// <param name="end">Where it stops.</param>
/// <param name="hit">Receives the nearest contact.</param>
/// <returns><see langword="false"/> when the ray reaches its end unobstructed.</returns>
/// <remarks>
/// This is the shape of what <c>0x4CDB20</c> does for one ray of its batch. Keeping it a delegate is
/// what lets the sweep in <see cref="Player.HumanWallSweep"/> be tested against surfaces built by hand
/// rather than only against a loaded level.
/// </remarks>
public delegate bool WorldRayCast(Vector3d origin, Vector3d end, out WorldRayHit hit);

/// <summary>The categories accepted by an IGI world trace.</summary>
[Flags]
public enum WorldTraceMask
{
    /// <summary>Do not visit any collision category.</summary>
    None = 0,

    /// <summary>The terrain octree and its triangle leaves.</summary>
    Terrain = 1 << 0,

    /// <summary>Placed collision meshes whose transform is fixed.</summary>
    StaticObjects = 1 << 1,

    /// <summary>Placed collision meshes with a live transform, notably doors.</summary>
    DynamicObjects = 1 << 2,

    /// <summary>Human body collision supplied by the caller.</summary>
    Humans = 1 << 3,

    /// <summary>Every world category. Humans remain a caller-owned body test.</summary>
    World = Terrain | StaticObjects | DynamicObjects,

    /// <summary>Every known category.</summary>
    All = World | Humans,
}

/// <summary>Which collision system produced a rich trace contact.</summary>
public enum WorldTraceHitKind
{
    /// <summary>No contact.</summary>
    None,

    /// <summary>A terrain-octree triangle.</summary>
    Terrain,

    /// <summary>A fixed placed-object triangle.</summary>
    StaticObject,

    /// <summary>A placed-object triangle carrying a live transform.</summary>
    DynamicObject,

    /// <summary>A human body supplied by a body evaluator.</summary>
    Human,
}

/// <summary>One IGI-style world trace request.</summary>
/// <param name="Origin">Where the segment starts, in world units.</param>
/// <param name="End">Where the segment ends.</param>
/// <param name="Mask">Which collision categories participate.</param>
/// <param name="IgnoredOwner">A placing task whose collision must be skipped.</param>
/// <param name="IgnoredSubject">A second task to skip, used by owner/subject sight handlers.</param>
/// <param name="IncludeInactiveOwners">
/// Whether collision owned by a dormant conditional container participates. Retail uses this during
/// Human registration so staged actors can acquire their moving parent before the scene is activated.
/// </param>
/// <param name="TracePortals">
/// Whether every doorway and window portal face is solid. Most callers leave this clear because
/// portal polygons mark openings rather than physical cover.
/// </param>
/// <param name="IgnoredPlacement">One exact placed mesh to skip, without skipping its sibling attachments.</param>
public readonly record struct WorldTraceRequest(
    Vector3d Origin,
    Vector3d End,
    WorldTraceMask Mask = WorldTraceMask.World,
    GameTask? IgnoredOwner = null,
    GameTask? IgnoredSubject = null,
    bool IncludeInactiveOwners = false,
    bool TracePortals = false,
    WorldPlacement? IgnoredPlacement = null)
{
    /// <summary>Gets the segment length in world units.</summary>
    public double Length
    {
        get
        {
            double x = End.X - Origin.X;
            double y = End.Y - Origin.Y;
            double z = End.Z - Origin.Z;
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }
    }
}

/// <summary>A nearest contact carrying the information IGI's trace handlers consume.</summary>
/// <param name="Position">The point of contact.</param>
/// <param name="Normal">The outward, ray-facing surface normal.</param>
/// <param name="Distance">Distance from the request origin.</param>
/// <param name="Fraction">Fraction of the request segment at which the contact occurred.</param>
/// <param name="Material">The struck material row.</param>
/// <param name="Kind">The collision category that answered.</param>
/// <param name="Owner">The exact task owning a placed collision mesh, when applicable.</param>
/// <param name="Label">A stable diagnostic name for the blocker.</param>
/// <param name="Placement">The exact rendered placement hit, including an attached magic object.</param>
public readonly record struct WorldTraceHit(
    Vector3d Position,
    Vector3f Normal,
    double Distance,
    double Fraction,
    int Material,
    WorldTraceHitKind Kind,
    GameTask? Owner = null,
    string Label = "",
    WorldPlacement? Placement = null)
{
    /// <summary>Projects this rich contact into the legacy cast result.</summary>
    public WorldRayHit RayHit => new(Position, Normal, Distance, Material);

    /// <summary>Builds a rich contact from a legacy result.</summary>
    public static WorldTraceHit FromRayHit(
        in WorldTraceRequest request,
        in WorldRayHit hit,
        WorldTraceHitKind kind,
        GameTask? owner = null,
        string label = "",
        WorldPlacement? placement = null)
    {
        double length = request.Length;
        double fraction = length > 0.0 ? Math.Clamp(hit.Distance / length, 0.0, 1.0) : 0.0;
        return new WorldTraceHit(
            hit.Position, hit.Normal, hit.Distance, fraction, hit.Material, kind, owner, label, placement);
    }
}

/// <summary>Casts one rich request and reports its nearest accepted contact.</summary>
public delegate bool WorldTraceCast(in WorldTraceRequest request, out WorldTraceHit hit);

/// <summary>Adapters and ordering rules shared by sight and projectile callers.</summary>
public static class WorldTrace
{
    /// <summary>Wraps a legacy world ray in the richer request contract.</summary>
    public static WorldTraceCast FromRayCast(WorldRayCast cast, WorldTraceHitKind kind = WorldTraceHitKind.StaticObject)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = default;
            if ((request.Mask & MaskFor(kind)) == 0 || !cast(request.Origin, request.End, out WorldRayHit legacy))
            {
                return false;
            }

            hit = WorldTraceHit.FromRayHit(request, legacy, kind);
            return true;
        };
    }

    /// <summary>Combines trace providers and keeps the nearest valid contact.</summary>
    public static WorldTraceCast Nearest(params WorldTraceCast[] casts)
    {
        ArgumentNullException.ThrowIfNull(casts);
        return (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = default;
            bool found = false;
            double nearest = double.PositiveInfinity;

            foreach (WorldTraceCast cast in casts)
            {
                if (cast is null || !cast(request, out WorldTraceHit candidate))
                {
                    continue;
                }

                if (!double.IsFinite(candidate.Distance) || candidate.Distance < 0.0 || candidate.Distance >= nearest)
                {
                    continue;
                }

                nearest = candidate.Distance;
                hit = candidate;
                found = true;
            }

            return found;
        };
    }

    /// <summary>Returns the legacy adapter used by movement and camera callers.</summary>
    public static WorldRayCast AsRayCast(
        WorldTraceCast cast,
        WorldTraceMask mask = WorldTraceMask.World,
        bool tracePortals = false)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            bool found = cast(
                new WorldTraceRequest(
                    origin,
                    end,
                    mask,
                    TracePortals: tracePortals),
                out WorldTraceHit rich);
            hit = found ? rich.RayHit : default;
            return found;
        };
    }

    private static WorldTraceMask MaskFor(WorldTraceHitKind kind) => kind switch
    {
        WorldTraceHitKind.Terrain => WorldTraceMask.Terrain,
        WorldTraceHitKind.StaticObject => WorldTraceMask.StaticObjects,
        WorldTraceHitKind.DynamicObject => WorldTraceMask.DynamicObjects,
        WorldTraceHitKind.Human => WorldTraceMask.Humans,
        _ => WorldTraceMask.None,
    };
}

/// <summary>Ray against a triangle.</summary>
/// <remarks>
/// <para>
/// The Möller–Trumbore test. <b>This is geometry, not transcription</b> — unlike everything around it,
/// it was not read out of the executable. A ray-triangle intersection has one answer, and the original's
/// own test (reached through <c>0x4CDB20</c>'s shape dispatch) has not been read. What <i>was</i> read,
/// and is what actually decides how the player moves, is which surfaces count as walls and what happens
/// when one is met — see <see cref="Player.HumanWallSweep"/>.
/// </para>
/// <para>
/// Both faces are hit. A collision mesh's winding is not established, and treating one side as
/// invisible would let the player walk out through a wall they had somehow got behind.
/// </para>
/// </remarks>
public static class RayTriangle
{
    /// <summary>How near parallel a ray may be to a triangle before it counts as missing it.</summary>
    public const double ParallelEpsilon = 1e-9;

    /// <summary>Intersects a ray with a triangle.</summary>
    /// <param name="origin">The ray's start.</param>
    /// <param name="direction">Its direction; need not be unit length.</param>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <param name="distance">
    /// Receives the hit's distance as a fraction of <paramref name="direction"/>, so a value in
    /// <c>[0, 1)</c> lies within the segment.
    /// </param>
    /// <remarks>
    /// <b>The far end is exclusive</b>, and that matters more than it looks. After a slide the body
    /// rests exactly one skin width off the wall, so the next tick's ray — which reaches a skin further
    /// than the move — ends precisely on that same surface. Counting it would make the sweep slide to
    /// the same point ten times over and then revert the move entirely, so walking into any wall would
    /// stop you dead instead of letting you slide along it.
    /// </remarks>
    /// <returns><see langword="false"/> when the ray misses.</returns>
    public static bool Intersect(
        Vector3d origin,
        Vector3d direction,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        out double distance)
    {
        distance = 0.0;

        Vector3d ab = new(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        Vector3d ac = new(c.X - a.X, c.Y - a.Y, c.Z - a.Z);

        Vector3d p = Vector3d.Cross(direction, ac);
        double determinant = Vector3d.Dot(ab, p);

        if (Math.Abs(determinant) < ParallelEpsilon)
        {
            return false;
        }

        double inverse = 1.0 / determinant;
        Vector3d t = new(origin.X - a.X, origin.Y - a.Y, origin.Z - a.Z);

        double u = Vector3d.Dot(t, p) * inverse;
        if (u < 0.0 || u > 1.0)
        {
            return false;
        }

        Vector3d q = Vector3d.Cross(t, ab);
        double v = Vector3d.Dot(direction, q) * inverse;
        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        distance = Vector3d.Dot(ac, q) * inverse;
        return distance >= 0.0 && distance < 1.0;
    }

    /// <summary>The triangle's unit normal.</summary>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <returns>The normal, or zero for a degenerate triangle.</returns>
    public static Vector3d Normal(Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d ab = new(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        Vector3d ac = new(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        Vector3d n = Vector3d.Cross(ab, ac);

        double length = Math.Sqrt(Vector3d.Dot(n, n));
        return length > 0.0 ? new Vector3d(n.X / length, n.Y / length, n.Z / length) : default;
    }

}
