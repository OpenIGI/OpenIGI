using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>The simulated state of a gun after its owner has dropped it.</summary>
public readonly record struct DroppedGunState(
    Vector3d Position,
    Matrix3f Orientation,
    Vector3d Velocity,
    bool IsResting = false);

/// <summary>Collision vertices in the exact attachment-local space used to draw a gun pickup.</summary>
public sealed class DroppedGunCollider
{
    private readonly Vector3f[] _vertices;
    private readonly Vector3f[] _contactVertices;

    /// <summary>Copies the rendered weapon hierarchy's vertices as a pickup collider.</summary>
    public DroppedGunCollider(IEnumerable<Vector3f> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        _vertices = vertices.ToArray();
        if (_vertices.Length == 0)
        {
            throw new ArgumentException("A gun collider needs at least one vertex.", nameof(vertices));
        }

        _contactVertices = BuildSupportVertices(_vertices);
    }

    /// <summary>Gets the number of source vertices in this collider.</summary>
    public int VertexCount => _vertices.Length;

    /// <summary>Gets the bounded set of mesh vertices used for live collision queries.</summary>
    /// <remarks>
    /// These are actual source vertices selected as the extreme support point in each of 26 directions.
    /// They preserve the visible mesh outline without doing thousands of expensive level ray casts in a
    /// single 30 Hz tick.
    /// </remarks>
    public int ContactVertexCount => _contactVertices.Length;

    /// <summary>Creates a free rigid body whose contact points are this rendered mesh.</summary>
    public DroppedGunMeshBody CreateRigidBody(
        Vector3d origin,
        Matrix3f orientation,
        Vector3d velocity,
        Vector3d spin) => new(this, origin, orientation, velocity, spin);

    /// <summary>Finds the deepest rendered vertex that has crossed terrain.</summary>
    /// <param name="origin">The rendered model origin in engine units.</param>
    /// <param name="orientation">The rendered model orientation.</param>
    /// <param name="world">Terrain query source.</param>
    /// <param name="localPoint">The contacting vertex in model-local engine units.</param>
    /// <param name="penetration">How far that vertex is below terrain.</param>
    /// <returns><see langword="true"/> when a visible vertex is below the terrain surface.</returns>
    /// <remarks>
    /// This is deliberately a mesh contact, not a mesh bounding-box contact.  It makes the stock,
    /// receiver and magazine support a rifle at their visible contact point instead of at the radius of
    /// an enclosing capsule.  Gun models are small and drops are uncommon, so testing their source
    /// vertices here is inexpensive compared with letting the player see a floating prop.
    /// </remarks>
    public bool TryGroundContact(
        Vector3d origin,
        Matrix3f orientation,
        RagdollWorld world,
        out Vector3f localPoint,
        out double penetration)
    {
        ArgumentNullException.ThrowIfNull(world);
        localPoint = default;
        penetration = 0.0;
        Matrix3f rotation = orientation.Transposed();
        foreach (Vector3f vertex in _contactVertices)
        {
            Vector3f offset = rotation.Transform(vertex);
            Vector3d point = origin + new Vector3d(offset.X, offset.Y, offset.Z);
            if (world.TryGround(point, out double ground) && ground > point.Z + penetration)
            {
                localPoint = vertex;
                penetration = ground - point.Z;
            }
        }

        return penetration > 0.0;
    }

    /// <summary>Finds a rendered vertex that swept through a world surface in one simulation substep.</summary>
    internal bool TrySurfaceContact(
        Vector3d previousOrigin,
        Matrix3f previousOrientation,
        Vector3d origin,
        Matrix3f orientation,
        RagdollWorld world,
        out Vector3f localPoint,
        out Vector3d normal,
        out double penetration)
    {
        localPoint = default;
        normal = default;
        penetration = 0.0;
        Matrix3f previousRotation = previousOrientation.Transposed();
        Matrix3f rotation = orientation.Transposed();
        foreach (Vector3f vertex in _contactVertices)
        {
            Vector3f oldOffset = previousRotation.Transform(vertex);
            Vector3f newOffset = rotation.Transform(vertex);
            Vector3d start = previousOrigin + new Vector3d(oldOffset.X, oldOffset.Y, oldOffset.Z);
            Vector3d end = origin + new Vector3d(newOffset.X, newOffset.Y, newOffset.Z);
            Vector3d travel = end - start;
            double reach = travel.Length;
            if (reach <= 1e-6
                || !world.TrySurface(start, travel, reach, out Vector3d hit, out Vector3d hitNormal))
            {
                continue;
            }

            double depth = Math.Max(Vector3d.Dot(hit - end, hitNormal), 0.0);
            if (depth > penetration)
            {
                localPoint = vertex;
                normal = hitNormal;
                penetration = depth;
            }
        }

        return penetration > 0.0;
    }

    private static Vector3f[] BuildSupportVertices(Vector3f[] vertices)
    {
        HashSet<int> selected = [];
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    Vector3f direction = new(x, y, z);
                    int support = 0;
                    float furthest = float.NegativeInfinity;
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        float distance = Vector3f.Dot(vertices[index], direction);
                        if (distance > furthest)
                        {
                            support = index;
                            furthest = distance;
                        }
                    }

                    selected.Add(support);
                }
            }
        }

        return selected.Select(index => vertices[index]).ToArray();
    }

    /// <summary>Gets the lowest rendered vertex in world space at an orientation.</summary>
    public double LowestZ(Matrix3f orientation)
    {
        double lowest = double.MaxValue;
        foreach (Vector3f vertex in _vertices)
        {
            Vector3f offset = orientation.Transposed().Transform(vertex);
            lowest = Math.Min(lowest, offset.Z);
        }

        return lowest;
    }

    /// <summary>Gets the leading vertex for one translation, in world units.</summary>
    public Vector3d LeadingOffset(Matrix3f orientation, Vector3d direction)
    {
        Vector3d leading = default;
        double furthest = double.NegativeInfinity;
        foreach (Vector3f vertex in _vertices)
        {
            Vector3f offset = orientation.Transposed().Transform(vertex);
            Vector3d world = new(offset.X, offset.Y, offset.Z);
            double distance = Vector3d.Dot(world, direction);
            if (distance > furthest)
            {
                furthest = distance;
                leading = world;
            }
        }

        return leading;
    }

    /// <summary>Gets how far the pickup origin must stay on the safe side of a surface.</summary>
    public double ClearanceAlong(Matrix3f orientation, Vector3d surfaceNormal)
    {
        double clearance = double.NegativeInfinity;
        foreach (Vector3f vertex in _vertices)
        {
            Vector3f offset = orientation.Transposed().Transform(vertex);
            clearance = Math.Max(clearance, -Vector3d.Dot(
                new Vector3d(offset.X, offset.Y, offset.Z), surfaceNormal));
        }

        return clearance;
    }
}

/// <summary>A free rigid weapon whose collision points come from its rendered mesh, not a capsule.</summary>
/// <remarks>
/// The terrain and world APIs offer ray queries rather than direct access to their triangles. This body
/// sweeps every gun-mesh contact point through those queries and resolves the impulse at that exact point.
/// A stock, magazine, receiver or barrel therefore supports the weapon only where it is visible.
/// </remarks>
public sealed class DroppedGunMeshBody
{
    private const double InverseMass = 0.25;
    private const double InverseInertia = 0.0000001;
    private const double Restitution = 0.08;
    private const double Friction = 0.68;

    /// <summary>The ground memo's cell edge: ten centimetres, in engine units.</summary>
    private const double GroundMemoCellUnits = 409.6;

    private readonly DroppedGunCollider _collider;
    private readonly Dictionary<long, (bool Found, double GroundZ)> _groundMemo = [];
    private RagdollWorld? _memoSource;
    private RagdollWorld? _memoWorld;
    private Quaternion32 _orientation;
    private Vector3d _velocity;
    private Vector3d _spin;
    private int _quietContactTicks;

    internal DroppedGunMeshBody(
        DroppedGunCollider collider,
        Vector3d origin,
        Matrix3f orientation,
        Vector3d velocity,
        Vector3d spin)
    {
        _collider = collider;
        Position = origin;
        _orientation = RagdollPose.QuaternionOf(orientation).Normalized();
        _velocity = velocity;
        _spin = spin;
    }

    /// <summary>Gets the weapon mesh origin in world units.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the weapon mesh orientation in world space.</summary>
    public Matrix3f Orientation => BonePose.ToMatrix(_orientation);

    /// <summary>Gets whether the body has settled on a mesh contact.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Advances mesh collision, gravity and angular motion for one game tick.</summary>
    /// <remarks>
    /// The ground query behind <paramref name="world"/> is a full terrain-octree descent plus a
    /// collision ray. One tick asks it up to three hundred times — every contact vertex, every
    /// resolution pass, every substep — from positions millimetres apart, and a tumbling gun kept
    /// that up for seconds after every kill. The memo collapses those to one descent per ten-
    /// centimetre cell per tick, which is finer than any surface the queries can distinguish.
    /// </remarks>
    public void Step(RagdollWorld world, int substeps = 4)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (IsAtRest)
        {
            return;
        }

        _groundMemo.Clear();
        world = MemoizedWorld(world);

        double step = 1.0 / Math.Max(substeps, 1);
        bool contacted = false;
        for (int substep = 0; substep < substeps; substep++)
        {
            Vector3d previousPosition = Position;
            Matrix3f previousOrientation = Orientation;
            _velocity -= new Vector3d(0.0, 0.0, DroppedGunPhysics.GravityPerTick * step);
            Position += _velocity * step;
            _orientation = RagdollSolver.Advance(_orientation, _spin, step);

            for (int pass = 0; pass < 3; pass++)
            {
                Matrix3f orientation = Orientation;
                if (_collider.TryGroundContact(Position, orientation, world, out Vector3f groundPoint, out double groundDepth))
                {
                    ResolveContact(groundPoint, Vector3d.UnitZ, groundDepth);
                    contacted = true;
                    continue;
                }

                if (_collider.TrySurfaceContact(
                    previousPosition, previousOrientation, Position, orientation, world,
                    out Vector3f wallPoint, out Vector3d wallNormal, out double wallDepth))
                {
                    ResolveContact(wallPoint, wallNormal, wallDepth);
                    contacted = true;
                    continue;
                }

                break;
            }

            _velocity *= 0.985;
            _spin *= 0.965;
        }

        if (contacted && _velocity.Length < 7.0 && _spin.Length < 0.025)
        {
            _quietContactTicks++;
            if (_quietContactTicks >= 12)
            {
                _velocity = default;
                _spin = default;
                IsAtRest = true;
            }
        }
        else
        {
            _quietContactTicks = 0;
        }
    }

    private void ResolveContact(Vector3f localPoint, Vector3d normal, double penetration)
    {
        if (normal.LengthSquared <= 0.0)
        {
            return;
        }

        normal = normal / Math.Sqrt(normal.LengthSquared);
        Position += normal * penetration;
        Vector3f offset = Orientation.Transposed().Transform(localPoint);
        Vector3d arm = new(offset.X, offset.Y, offset.Z);
        Vector3d velocityAtPoint = _velocity + Vector3d.Cross(_spin, arm);
        double closing = Vector3d.Dot(velocityAtPoint, normal);
        if (closing >= 0.0)
        {
            return;
        }

        double normalWeight = InverseMass + (InverseInertia * Vector3d.Cross(arm, normal).LengthSquared);
        double normalImpulse = -(1.0 + Restitution) * closing / normalWeight;
        ApplyImpulse(arm, normal * normalImpulse);

        velocityAtPoint = _velocity + Vector3d.Cross(_spin, arm);
        Vector3d tangent = velocityAtPoint - (normal * Vector3d.Dot(velocityAtPoint, normal));
        double length = tangent.Length;
        if (length > 1e-6)
        {
            Vector3d direction = tangent / length;
            double tangentWeight = InverseMass + (InverseInertia * Vector3d.Cross(arm, direction).LengthSquared);
            double desired = length / tangentWeight;
            ApplyImpulse(arm, direction * -Math.Min(desired, normalImpulse * Friction));
        }
    }

    private void ApplyImpulse(Vector3d arm, Vector3d impulse)
    {
        _velocity += impulse * InverseMass;
        _spin += Vector3d.Cross(arm, impulse) * InverseInertia;
    }

    /// <summary>Wraps a world so its ground query answers repeats from <see cref="_groundMemo"/>.</summary>
    /// <remarks>
    /// Built once per source world and reused: the shell hands the same <see cref="RagdollWorld"/> to
    /// every step, so this allocates on the first step and never again. The memo itself is cleared
    /// per step by the caller. Positions are keyed at <see cref="GroundMemoCellUnits"/> — points that
    /// share a cell can differ by centimetres, which is below the resolution of the terrain descent
    /// and flat within any floor a gun can rest on; a gun overhanging a ledge edge may read the
    /// ledge's height a few centimetres past it, which for a dropped prop is invisible.
    /// </remarks>
    private RagdollWorld MemoizedWorld(RagdollWorld world)
    {
        if (ReferenceEquals(_memoSource, world) && _memoWorld is not null)
        {
            return _memoWorld;
        }

        _memoSource = world;
        RagdollGround? ground = world.Ground;
        _memoWorld = ground is null
            ? world
            : new RagdollWorld(world.Cast, (double x, double y, double z, out double groundZ) =>
            {
                long key = MemoKey(x, y, z);
                if (_groundMemo.TryGetValue(key, out (bool Found, double GroundZ) cached))
                {
                    groundZ = cached.GroundZ;
                    return cached.Found;
                }

                bool found = ground(x, y, z, out groundZ);
                _groundMemo[key] = (found, groundZ);
                return found;
            });
        return _memoWorld;
    }

    private static long MemoKey(double x, double y, double z)
    {
        long cellX = (long)Math.Floor(x / GroundMemoCellUnits);
        long cellY = (long)Math.Floor(y / GroundMemoCellUnits);
        long cellZ = (long)Math.Floor(z / GroundMemoCellUnits);
        return ((cellX & 0x1FFFFF) << 42) | ((cellY & 0x1FFFFF) << 21) | (cellZ & 0x1FFFFF);
    }
}

/// <summary>World motion for a dropped gun pickup.</summary>
/// <remarks>
/// Retail's <c>GunPickup</c> becomes independent when <c>Gun_Drop</c> creates it. It cannot remain
/// constrained to a dead hand: that is what made the port's weapon float with a corpse and pass through
/// nearby walls.
/// </remarks>
public static class DroppedGunPhysics
{
    /// <summary>Gravity per 30 Hz tick in engine units, read at <c>0x46CEA0</c>.</summary>
    public const double GravityPerTick = 44.60088888888889;

    /// <summary>Retail's pickup origin has no artificial floor clearance.</summary>
    public const double CollisionRadius = 0.0;

    /// <summary>Fraction of speed retained when the gun bounces off a surface.</summary>
    public const double Restitution = 0.25;

    /// <summary>Fraction of sideways speed retained by an impact.</summary>
    public const double TangentialRetention = 0.55;

    /// <summary>Retail's stop threshold after a bounce, 10 cm per tick.</summary>
    public const double RestSpeed = 409.6;

    /// <summary>Advances a dropped weapon one 30 Hz tick against floors and world collision.</summary>
    public static DroppedGunState Advance(
        DroppedGunState state,
        RagdollWorld world,
        DroppedGunCollider? collider = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (state.IsResting)
        {
            return state;
        }

        Vector3d velocity = state.Velocity - new Vector3d(0.0, 0.0, GravityPerTick);
        Vector3d position = state.Position;
        Vector3d step = velocity;
        Vector3d probe = collider?.LeadingOffset(state.Orientation, step) ?? default;
        if (step.Length > 0.0
            && world.TrySurface(position + probe, step, step.Length, out Vector3d point, out Vector3d normal))
        {
            double clearance = collider?.ClearanceAlong(state.Orientation, normal) ?? 0.0;
            position = point + (normal * clearance);
            double alongNormal = Vector3d.Dot(velocity, normal);
            if (alongNormal < 0.0)
            {
                Vector3d tangent = velocity - (normal * alongNormal);
                velocity = (tangent * TangentialRetention) - (normal * alongNormal * Restitution);
            }
        }
        else
        {
            position += step;
        }

        bool onGround = false;
        double floorOffset = collider?.LowestZ(state.Orientation) ?? 0.0;
        if (world.TryGround(position, out double ground) && position.Z + floorOffset <= ground)
        {
            position = new Vector3d(position.X, position.Y, ground - floorOffset);
            if (velocity.Z < 0.0)
            {
                velocity = new Vector3d(
                    velocity.X * TangentialRetention,
                    velocity.Y * TangentialRetention,
                    -velocity.Z * Restitution);
            }

            onGround = true;
        }

        bool resting = onGround && Math.Abs(velocity.Z) < RestSpeed;
        if (resting)
        {
            velocity = default;
        }

        return new DroppedGunState(position, state.Orientation, velocity, resting);
    }

}
