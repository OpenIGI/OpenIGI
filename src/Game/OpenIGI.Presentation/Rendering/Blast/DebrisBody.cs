using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// One broken piece as a rigid body: its own mass spread over its own shape, falling, tumbling, and landing on its corners --
/// every corner that touches at once -- with bounce, friction and rolling resistance, until it lies still.
/// </summary>
/// <remarks>
/// <para>
/// A sequential-impulse solver in world units and game ticks. The piece's corners are a handful of its extreme points (its
/// hull, near enough); each tick those under the ground are pushed out and the contact impulses are solved over them
/// together, a few passes, so a slab landing flat stops flat and one landing on an edge tips over that edge instead of
/// skating. The inertia is a box's of the piece's own extents, so a long splinter spins about its length and a flat slab
/// is hard to flip, as real ones are.
/// </para>
/// <para>
/// The ground is one plane a tick under the piece, read from the world at its middle: a slope's tilt and a roof's height,
/// which is what a piece a metre across can tell apart. Walls are met by the piece's own sweep through the tick.
/// </para>
/// </remarks>
public sealed class DebrisBody
{
    /// <summary>Gravity, in world units a tick per tick: 9.8 m/s² at 30 ticks a second.</summary>
    public const double Gravity = 44.600887;

    private const int Passes = 6;
    private const double Restitution = 0.22;
    private const double Friction = 0.75;
    private const double BounceThreshold = 120.0;
    private const double SleepSpeed = 18.0;
    private const double SleepSpin = 0.008;
    private const int SleepTicks = 8;

    private readonly Vector3d[] _corners;
    private readonly Vector3d _inverseInertia;
    private readonly double _radius;
    private Vector3d _ex = new(1, 0, 0);
    private Vector3d _ey = new(0, 1, 0);
    private Vector3d _ez = new(0, 0, 1);
    private readonly double _handedness;
    private int _quietTicks;
    private bool _groundRead;
    private bool _hasGround;
    private Vector3d _groundReadAt;
    private Vector3d _groundPoint;
    private Vector3d _groundNormal = Vector3d.UnitZ;

    /// <summary>Initializes a new instance of the <see cref="DebrisBody"/> class.</summary>
    /// <param name="corners">The piece's corners about its centre, in its own axes, world units.</param>
    /// <param name="position">Where its centre starts, in world units.</param>
    /// <param name="orientation">Its turn, as a placement's: world offset = orientation · local.</param>
    /// <param name="velocity">Its velocity, world units a tick.</param>
    /// <param name="spin">Its spin about world axes, radians a tick.</param>
    public DebrisBody(IReadOnlyList<Vector3f> corners, Vector3d position, Matrix3f orientation, Vector3d velocity, Vector3d spin)
    {
        ArgumentNullException.ThrowIfNull(corners);
        _corners = new Vector3d[corners.Count];
        Vector3d low = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d high = new(double.MinValue, double.MinValue, double.MinValue);
        double radius = 0.0;
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3d c = new(corners[i].X, corners[i].Y, corners[i].Z);
            _corners[i] = c;
            low = new Vector3d(Math.Min(low.X, c.X), Math.Min(low.Y, c.Y), Math.Min(low.Z, c.Z));
            high = new Vector3d(Math.Max(high.X, c.X), Math.Max(high.Y, c.Y), Math.Max(high.Z, c.Z));
            radius = Math.Max(radius, c.Length);
        }

        // A unit mass; a box's inertia over its extents, never thinner than a few centimetres a side.
        Vector3d size = high - low;
        double a = Math.Max(size.X, 120.0);
        double b = Math.Max(size.Y, 120.0);
        double c2 = Math.Max(size.Z, 120.0);
        _inverseInertia = new Vector3d(12.0 / ((b * b) + (c2 * c2)), 12.0 / ((a * a) + (c2 * c2)), 12.0 / ((a * a) + (b * b)));
        _radius = Math.Max(radius, 60.0);

        Position = position;
        PreviousPosition = position;
        _ex = new Vector3d(orientation.M11, orientation.M21, orientation.M31);
        _ey = new Vector3d(orientation.M12, orientation.M22, orientation.M32);
        _ez = new Vector3d(orientation.M13, orientation.M23, orientation.M33);

        // A mirrored placement keeps its mirror as it turns.
        _handedness = Vector3d.Dot(Vector3d.Cross(_ex, _ey), _ez) < 0.0 ? -1.0 : 1.0;
        PreviousOrientation = Orientation;
        Velocity = velocity;
        Spin = spin;
    }

    /// <summary>Gets where its centre is, in world units.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets where its centre was a tick ago.</summary>
    public Vector3d PreviousPosition { get; private set; }

    /// <summary>Gets its turn a tick ago.</summary>
    public Matrix3f PreviousOrientation { get; private set; }

    /// <summary>Gets its velocity, world units a tick.</summary>
    public Vector3d Velocity { get; private set; }

    /// <summary>Gets its spin about world axes, radians a tick.</summary>
    public Vector3d Spin { get; private set; }

    /// <summary>Gets a value indicating whether it lies still.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Gets how far its farthest corner is from its centre, in world units.</summary>
    public double Radius => _radius;

    /// <summary>Gets its turn, as a placement's.</summary>
    public Matrix3f Orientation => new(
        (float)_ex.X, (float)_ey.X, (float)_ez.X,
        (float)_ex.Y, (float)_ey.Y, (float)_ez.Y,
        (float)_ex.Z, (float)_ey.Z, (float)_ez.Z);

    /// <summary>Steps it one tick.</summary>
    /// <param name="world">What it lands on and strikes.</param>
    /// <param name="substeps">How many steps the tick is taken in.</param>
    public void Step(DebrisCollision world, int substeps = 2)
    {
        ArgumentNullException.ThrowIfNull(world);
        PreviousPosition = Position;
        PreviousOrientation = Orientation;
        if (IsAtRest)
        {
            return;
        }

        // The ground under it: the plane at its middle, read again only once it has moved half a metre.
        Vector3d moved = Position - _groundReadAt;
        if (!_groundRead || (moved.X * moved.X) + (moved.Y * moved.Y) > DebrisCollision.GroundReuseUnits * DebrisCollision.GroundReuseUnits
            || moved.Z < -DebrisCollision.GroundReuseUnits)
        {
            _hasGround = world.TryGroundPlane(Position, _radius, out _groundPoint, out _groundNormal);
            _groundReadAt = Position;
            _groundRead = true;
        }

        bool hasGround = _hasGround;
        Vector3d groundPoint = _groundPoint;
        Vector3d groundNormal = _groundNormal;
        double dt = 1.0 / Math.Max(substeps, 1);
        bool touched = false;
        Span<Vector3d> arms = stackalloc Vector3d[24];
        Span<double> accumulated = stackalloc double[24];

        for (int s = 0; s < substeps; s++)
        {
            Vector3d from = Position;
            Velocity -= new Vector3d(0.0, 0.0, Gravity * dt);
            Position += Velocity * dt;
            Turn(Spin * dt);

            // Walls: the centre's sweep through the substep, stopped a radius short of what it meets.
            if (world.TrySweep(from, Position, _radius * 0.5, out Vector3d wallPoint, out Vector3d wallNormal))
            {
                Position = wallPoint + (wallNormal * (_radius * 0.5));
                double into = Vector3d.Dot(Velocity, wallNormal);
                if (into < 0.0)
                {
                    Vector3d along = Velocity - (wallNormal * into);
                    Velocity = (along * 0.6) - (wallNormal * (into * Restitution));
                    Spin *= 0.7;
                }

                touched = true;
            }

            if (!hasGround)
            {
                continue;
            }

            // Every corner under the ground plane is a contact.
            int count = 0;
            double deepest = 0.0;
            for (int i = 0; i < _corners.Length && count < arms.Length; i++)
            {
                Vector3d arm = ToWorld(_corners[i]);
                double depth = Vector3d.Dot(groundPoint - (Position + arm), groundNormal);
                if (depth > 0.0)
                {
                    arms[count] = arm;
                    accumulated[count] = 0.0;
                    count++;
                    deepest = Math.Max(deepest, depth);
                }
            }

            if (count == 0)
            {
                continue;
            }

            touched = true;
            Position += groundNormal * deepest;

            // Restitution only for a real fall; a resting contact is not bounced, which is what lets a slab settle.
            for (int pass = 0; pass < Passes; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3d arm = arms[i];
                    Vector3d pointVelocity = Velocity + Vector3d.Cross(Spin, arm);
                    double closing = Vector3d.Dot(pointVelocity, groundNormal);
                    double bounce = pass == 0 && closing < -BounceThreshold ? Restitution : 0.0;
                    double weight = 1.0 + Vector3d.Dot(Vector3d.Cross(ApplyInverseInertia(Vector3d.Cross(arm, groundNormal)), arm), groundNormal);
                    double impulse = -(1.0 + bounce) * closing / weight;
                    double total = Math.Max(accumulated[i] + impulse, 0.0);
                    impulse = total - accumulated[i];
                    accumulated[i] = total;
                    Apply(arm, groundNormal * impulse);

                    // Friction, bounded by what holds the corner down.
                    pointVelocity = Velocity + Vector3d.Cross(Spin, arm);
                    Vector3d slip = pointVelocity - (groundNormal * Vector3d.Dot(pointVelocity, groundNormal));
                    double slipSpeed = slip.Length;
                    if (slipSpeed > 1e-6)
                    {
                        Vector3d direction = slip / slipSpeed;
                        double tangentWeight = 1.0 + Vector3d.Dot(Vector3d.Cross(ApplyInverseInertia(Vector3d.Cross(arm, direction)), arm), direction);
                        double stop = Math.Min(slipSpeed / tangentWeight, accumulated[i] * Friction);
                        Apply(arm, direction * -stop);
                    }
                }
            }

            // Rolling and scraping lose a little more than the corners' friction says, as rubble does.
            Spin *= 0.9;
        }

        Velocity *= 0.998;
        Spin *= 0.99;

        if (touched && Velocity.Length < SleepSpeed && Spin.Length < SleepSpin)
        {
            if (++_quietTicks >= SleepTicks)
            {
                Velocity = default;
                Spin = default;
                IsAtRest = true;
            }
        }
        else
        {
            _quietTicks = 0;
        }

        // Fell out of the world: stop rather than fall for ever.
        if (!hasGround && Position.Z < PreviousPosition.Z - 40960.0)
        {
            IsAtRest = true;
        }
    }

    private Vector3d ToWorld(Vector3d local) => (_ex * local.X) + (_ey * local.Y) + (_ez * local.Z);

    private Vector3d ToLocal(Vector3d world) => new(Vector3d.Dot(world, _ex), Vector3d.Dot(world, _ey), Vector3d.Dot(world, _ez));

    private Vector3d ApplyInverseInertia(Vector3d torque)
    {
        Vector3d local = ToLocal(torque);
        return ToWorld(new Vector3d(local.X * _inverseInertia.X, local.Y * _inverseInertia.Y, local.Z * _inverseInertia.Z));
    }

    private void Apply(Vector3d arm, Vector3d impulse)
    {
        Velocity += impulse;
        Spin += ApplyInverseInertia(Vector3d.Cross(arm, impulse));
    }

    // Turns the frame by a small rotation vector, then keeps it square.
    private void Turn(Vector3d rotation)
    {
        double angle = rotation.Length;
        if (angle < 1e-9)
        {
            return;
        }

        Vector3d axis = rotation / angle;
        _ex = Rotate(_ex, axis, angle);
        _ey = Rotate(_ey, axis, angle);
        _ex /= _ex.Length;
        _ey -= _ex * Vector3d.Dot(_ex, _ey);
        _ey /= _ey.Length;
        _ez = Vector3d.Cross(_ex, _ey) * _handedness;
    }

    private static Vector3d Rotate(Vector3d v, Vector3d axis, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        return (v * cos) + (Vector3d.Cross(axis, v) * sin) + (axis * (Vector3d.Dot(axis, v) * (1.0 - cos)));
    }
}
