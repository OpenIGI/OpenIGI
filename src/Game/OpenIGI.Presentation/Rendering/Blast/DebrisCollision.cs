using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// What a bomb's pieces land on and strike: the level's ground and walls, less whatever the blast has already taken away.
/// </summary>
/// <remarks>
/// <para>
/// A shattered prop and a collapsed building are only hidden -- their collision is the level's and stays -- so a piece
/// thrown onto where a roof was would lie on nothing in the air. Every hit inside a volume the blast emptied (a gone
/// placement's box, a hole's ball) is passed through and the ray carried on beyond it, so the pieces fall to the floor
/// underneath, through the hole, onto the ground.
/// </para>
/// <para>
/// Cheap by design, for the web and phones: one downward ray for a piece's ground, reused while the piece stays within
/// <see cref="GroundReuseUnits"/> of where it was read, and one sweep a step for its walls.
/// </para>
/// </remarks>
public sealed class DebrisCollision
{
    /// <summary>How far a piece may move before its ground is read again: half a metre.</summary>
    public const double GroundReuseUnits = 2048.0;

    private const double SearchAbove = 4096.0;
    private const double SearchBelow = 409600.0;
    private const int MostPassThroughs = 6;

    private readonly WorldRayCast? _cast;
    private readonly RagdollWorld? _fallback;
    private readonly List<GoneBox> _boxes = [];
    private readonly List<(Vector3d Centre, double Radius)> _holes = [];

    /// <summary>Initializes a new instance of the <see cref="DebrisCollision"/> class.</summary>
    /// <param name="world">The level's queries, or <see langword="null"/> for flat ground at <paramref name="groundUnits"/>.</param>
    /// <param name="groundUnits">The height of the flat ground used without them.</param>
    public DebrisCollision(RagdollWorld? world, double groundUnits)
    {
        _cast = world?.Cast;
        _fallback = world ?? RagdollWorld.Plane(groundUnits);
    }

    /// <summary>Marks a placement's box as gone: what is inside it no longer stops a piece.</summary>
    /// <param name="position">The placement's position, world units.</param>
    /// <param name="orientation">Its turn: world offset = orientation · local.</param>
    /// <param name="low">Its mesh's low corner, local, world units.</param>
    /// <param name="high">Its high corner.</param>
    public void AddGone(Vector3d position, Matrix3f orientation, Vector3d low, Vector3d high)
    {
        // A little in from the faces, so the floor the building stood on still holds what falls on it.
        Vector3d margin = new(60.0, 60.0, 60.0);
        _boxes.Add(new GoneBox(position, orientation, low + margin, high - margin));
    }

    /// <summary>Marks a hole as gone.</summary>
    /// <param name="centre">Its centre, world units.</param>
    /// <param name="radius">Its radius.</param>
    public void AddHole(Vector3d centre, double radius) => _holes.Add((centre, radius));

    /// <summary>Forgets every gone volume, as the level is put back.</summary>
    public void Clear()
    {
        _boxes.Clear();
        _holes.Clear();
    }

    /// <summary>The ground under a point, as a plane.</summary>
    /// <param name="at">The point, world units.</param>
    /// <param name="reach">How far above it the ground may be found: the piece's size.</param>
    /// <param name="point">Receives a point on the ground.</param>
    /// <param name="normal">Receives its upward normal.</param>
    /// <returns>Whether there is ground.</returns>
    public bool TryGroundPlane(Vector3d at, double reach, out Vector3d point, out Vector3d normal)
    {
        normal = Vector3d.UnitZ;
        if (_cast is not null)
        {
            Vector3d from = at + new Vector3d(0.0, 0.0, SearchAbove + reach);
            Vector3d to = at - new Vector3d(0.0, 0.0, SearchBelow);
            if (TryCast(from, to, out WorldRayHit hit))
            {
                point = hit.Position;
                Vector3d n = new(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
                double length = n.Length;
                normal = length > 1e-6 && n.Z / length > 0.2 ? n / length : Vector3d.UnitZ;
                return true;
            }
        }

        if (_fallback is not null && _fallback.TryGround(at, out double groundZ))
        {
            point = new Vector3d(at.X, at.Y, groundZ);
            return true;
        }

        point = default;
        return false;
    }

    /// <summary>Whether a piece's centre, moving between two points, meets a wall.</summary>
    /// <param name="from">Where it was.</param>
    /// <param name="to">Where it goes.</param>
    /// <param name="radius">How far short of the wall it stops.</param>
    /// <param name="point">Receives where the wall was met.</param>
    /// <param name="normal">Receives the wall's normal, toward the piece.</param>
    /// <returns>Whether it met one.</returns>
    public bool TrySweep(Vector3d from, Vector3d to, double radius, out Vector3d point, out Vector3d normal)
    {
        point = default;
        normal = default;
        Vector3d travel = to - from;
        double length = travel.Length;
        if (_cast is null || length < 1.0)
        {
            return false;
        }

        Vector3d direction = travel / length;
        if (!TryCast(from, to + (direction * radius), out WorldRayHit hit))
        {
            return false;
        }

        normal = new Vector3d(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
        double size = normal.Length;
        if (size < 1e-6)
        {
            return false;
        }

        normal /= size;
        if (Vector3d.Dot(normal, direction) > 0.0)
        {
            normal = -normal;
        }

        // The ground is the ground plane's: only walls, and ceilings, stop the sweep.
        if (normal.Z > 0.6)
        {
            return false;
        }

        point = hit.Position;
        return true;
    }

    // A ray that passes through what the blast took away.
    private bool TryCast(Vector3d from, Vector3d to, out WorldRayHit hit)
    {
        hit = default;
        if (_cast is not { } cast)
        {
            return false;
        }

        Vector3d direction = to - from;
        double length = direction.Length;
        if (length < 1e-6)
        {
            return false;
        }

        direction /= length;
        Vector3d start = from;
        for (int i = 0; i < MostPassThroughs; i++)
        {
            if (!cast(start, to, out hit))
            {
                return false;
            }

            if (!IsGone(hit.Position))
            {
                return true;
            }

            start = hit.Position + (direction * 8.0);
            if (Vector3d.Dot(to - start, direction) <= 0.0)
            {
                return false;
            }
        }

        return false;
    }

    private bool IsGone(Vector3d at)
    {
        foreach ((Vector3d centre, double radius) in _holes)
        {
            if ((at - centre).LengthSquared < radius * radius)
            {
                return true;
            }
        }

        foreach (GoneBox box in _boxes)
        {
            if (box.Contains(at))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct GoneBox(Vector3d Position, Matrix3f Orientation, Vector3d Low, Vector3d High)
    {
        public bool Contains(Vector3d at)
        {
            // Local = Oᵀ · world offset: the columns of the turn are the local axes in the world.
            Vector3d d = at - Position;
            double x = (d.X * Orientation.M11) + (d.Y * Orientation.M21) + (d.Z * Orientation.M31);
            double y = (d.X * Orientation.M12) + (d.Y * Orientation.M22) + (d.Z * Orientation.M32);
            double z = (d.X * Orientation.M13) + (d.Y * Orientation.M23) + (d.Z * Orientation.M33);
            return x > Low.X && x < High.X && y > Low.Y && y < High.Y && z > Low.Z && z < High.Z;
        }
    }
}
