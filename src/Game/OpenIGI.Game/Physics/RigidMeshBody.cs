using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Physics;

/// <summary>
/// A free rigid body whose collider is the convex hull of a rendered mesh, simulated against the real
/// world triangles around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real mass and real inertia.</b> The body carries the object's actual mass and the full inertia
/// tensor of its hull, integrated about its centre of mass. Rotation is integrated from
/// <b>angular momentum</b>, not angular velocity, so a long gun tumbling in the air behaves as a real
/// one does -- stable about its long and short axes, wobbling about the middle one.
/// </para>
/// <para>
/// <b>Mesh collision.</b> Once per tick the world hands over every solid triangle the body could reach.
/// Each substep then finds two kinds of contact against them: a hull vertex at or below a triangle's
/// surface (the stock or muzzle landing on a floor), and a world edge cutting into the hull (a ledge
/// or step corner the gun lies across). Contacts carry the real surface normal, so a gun on a slope
/// slides down the slope, not straight down.
/// </para>
/// <para>
/// <b>The solver</b> resolves every contact of a step together by sequential impulses with Coulomb
/// friction and velocity-threshold restitution. Contacts are speculative -- a vertex about to reach a
/// surface this substep is caught before it passes through -- so a fast drop cannot tunnel.
/// Penetration left over is removed by a separate pseudo-velocity pass that moves the body without
/// adding energy, which is what lets it come to rest instead of buzzing on the floor.
/// </para>
/// <para>
/// <b>Cost.</b> One triangle gather per tick over the swept box, a few dozen hull vertices and faces,
/// and nothing at all once the body sleeps.
/// </para>
/// <para>Units are the engine's: 4096 per metre, and time in 30 Hz ticks.</para>
/// </remarks>
public sealed class RigidMeshBody
{
    /// <summary>Gravity per tick squared, in engine units: 9.8 m/s² at 30 Hz, read at <c>0x46CEA0</c>.</summary>
    public const double GravityPerTick = DroppedGunPhysics.GravityPerTick;

    /// <summary>The engine units in one metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The simulation ticks in one second.</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>How much of a hard impact's closing speed comes back: steel on stone, concrete or wood.</summary>
    public const double Restitution = 0.3;

    /// <summary>Impacts slower than this do not bounce at all: one metre per second.</summary>
    public const double RestitutionThreshold = 1.0 * UnitsPerMetre / TicksPerSecond;

    /// <summary>The Coulomb friction coefficient of metal and polymer on typical floors.</summary>
    public const double Friction = 0.55;

    /// <summary>The fraction of angular momentum rolling resistance takes per second while in contact.</summary>
    public const double RollingResistancePerSecond = 1.2;

    /// <summary>How far a contact may penetrate before it is corrected: half a millimetre.</summary>
    public const double PenetrationSlop = 2.0;

    /// <summary>The share of remaining penetration removed per substep.</summary>
    public const double PositionCorrection = 0.4;

    /// <summary>The body sleeps once slower than this: two centimetres per second.</summary>
    public const double SleepLinear = 0.02 * UnitsPerMetre / TicksPerSecond;

    /// <summary>...and turning slower than this: a tenth of a radian per second.</summary>
    public const double SleepAngular = 0.1 / TicksPerSecond;

    /// <summary>...for this many consecutive ticks in contact.</summary>
    public const int SleepTicks = 8;

    /// <summary>A body that has met nothing for this long has left the world and is stopped.</summary>
    public const int LostTicks = 20 * 30;

    /// <summary>The closing speed above which a contact counts as an audible impact: 0.7 m/s.</summary>
    public const double ImpactSoundSpeed = 0.7 * UnitsPerMetre / TicksPerSecond;

    private const int VelocityIterations = 10;
    private const int MaximumSubsteps = 24;
    private const double MaximumTravelPerSubstep = 40.0;
    private const int PositionIterations = 4;
    private const int MaximumContacts = 24;
    private const double SpeculativeMarginUnits = 4.0;
    private const double MaximumVertexDepthUnits = 400.0;

    private readonly Vector3d[] _hull;
    private readonly HullFace[] _faces;
    private readonly Vector3d _centreLocal;
    private readonly double _inverseMass;
    private readonly Matrix3d _inverseInertiaBody;
    private readonly double _radius;
    private readonly List<CollisionTriangle> _triangles = [];
    private readonly List<Vector3d> _triangleNormals = [];
    private readonly List<Contact> _contacts = [];
    private readonly HashSet<(long, long, long, long, long, long)> _edgeKeys = [];

    private Vector3d _centre;
    private QuaternionD _orientation;
    private Vector3d _velocity;
    private Vector3d _angularMomentum;
    private int _quietTicks;
    private int _ticksWithoutContact;
    private bool _wasTouching;

    /// <summary>Initializes a new instance of the <see cref="RigidMeshBody"/> class.</summary>
    /// <param name="hull">The collider, in the mesh's own space.</param>
    /// <param name="massKilograms">The real mass.</param>
    /// <param name="origin">Where the mesh origin is, in world units.</param>
    /// <param name="orientation">The mesh's world orientation, as the port applies it.</param>
    /// <param name="velocity">The launch velocity of the mesh origin, units per tick.</param>
    /// <param name="angularVelocity">The launch spin, radians per tick, in world space.</param>
    public RigidMeshBody(
        ConvexHull hull,
        double massKilograms,
        Vector3d origin,
        Matrix3f orientation,
        Vector3d velocity,
        Vector3d angularVelocity)
    {
        ArgumentNullException.ThrowIfNull(hull);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massKilograms);

        _centreLocal = hull.Centroid;
        _hull = [.. hull.Vertices.Select(vertex => vertex - _centreLocal)];
        _faces =
        [
            .. hull.Faces.Select(face => face with { Offset = face.Offset - Vector3d.Dot(face.Normal, _centreLocal) }),
        ];
        _radius = _hull.Max(vertex => vertex.Length);
        _inverseMass = 1.0 / massKilograms;
        _inverseInertiaBody = (hull.UnitInertia * massKilograms).Inverted();

        Matrix3d rotation = Matrix3d.From(orientation);
        _orientation = QuaternionD.FromMatrix(rotation);
        rotation = _orientation.ToMatrix();
        _centre = origin + rotation.Transform(_centreLocal);
        _velocity = velocity;

        Matrix3d inertiaWorld = rotation * (hull.UnitInertia * massKilograms) * rotation.Transposed();
        _angularMomentum = inertiaWorld.Transform(angularVelocity);
    }

    /// <summary>Gets the mesh origin, in world units, where the model is drawn from.</summary>
    public Vector3d Position => _centre - _orientation.ToMatrix().Transform(_centreLocal);

    /// <summary>Gets the mesh's world orientation, as the port applies it.</summary>
    public Matrix3f Orientation => _orientation.ToMatrix().ToMatrix3f();

    /// <summary>Gets the centre of mass, in world units.</summary>
    public Vector3d CentreOfMass => _centre;

    /// <summary>Gets the linear velocity of the centre of mass, units per tick.</summary>
    public Vector3d Velocity => _velocity;

    /// <summary>Gets the angular velocity, radians per tick.</summary>
    public Vector3d AngularVelocity => InverseInertiaWorld(_orientation.ToMatrix()).Transform(_angularMomentum);

    /// <summary>Gets whether the body has come to rest and stopped simulating.</summary>
    public bool IsAtRest { get; private set; }

    /// <summary>Gets the hardest impact of the last step, as a closing speed in units per tick; zero for none.</summary>
    /// <remarks>
    /// Reported once per landing: a body that stays in contact does not report again until it has left
    /// the surface, so a gun sliding to a stop is one clatter, not thirty.
    /// </remarks>
    public double LastImpactSpeed { get; private set; }

    /// <summary>Gets where the last reported impact happened.</summary>
    public Vector3d LastImpactPosition { get; private set; }

    /// <summary>Gets the kinetic energy, in kilogram-units² per tick², for diagnostics and tests.</summary>
    public double KineticEnergy
    {
        get
        {
            Vector3d omega = AngularVelocity;
            return (0.5 * _velocity.LengthSquared / _inverseMass) + (0.5 * Vector3d.Dot(omega, _angularMomentum));
        }
    }

    /// <summary>Advances the body one game tick.</summary>
    /// <param name="gather">Supplies the world triangles near the body; <see langword="null"/> for free fall.</param>
    /// <param name="substeps">Integration steps per tick.</param>
    public void Step(CollisionTriangleGather? gather, int substeps = 4)
    {
        LastImpactSpeed = 0.0;
        if (IsAtRest)
        {
            return;
        }

        // More substeps while anything on the body is moving fast, so no point travels further than
        // MaximumTravelPerSubstep in one: a contact is then never engaged more than a centimetre early,
        // and a bounce leaves from the surface rather than above it.
        double pointSpeed = _velocity.Length + GravityPerTick + (AngularVelocity.Length * _radius);
        substeps = Math.Clamp(Math.Max(substeps, (int)Math.Ceiling(pointSpeed / MaximumTravelPerSubstep)), 1, MaximumSubsteps);
        double dt = 1.0 / substeps;

        _triangles.Clear();
        if (gather is not null)
        {
            // Everything the body can reach this tick: its extent, where its velocity and spin carry it,
            // and one tick of gravity, with a margin for the speculative contacts.
            double speed = _velocity.Length + GravityPerTick;
            double reach = _radius + speed + (AngularVelocity.Length * _radius) + (SpeculativeMarginUnits * 4.0);
            Vector3d end = _centre + _velocity;
            Vector3d min = new(Math.Min(_centre.X, end.X) - reach, Math.Min(_centre.Y, end.Y) - reach, Math.Min(_centre.Z, end.Z) - reach);
            Vector3d max = new(Math.Max(_centre.X, end.X) + reach, Math.Max(_centre.Y, end.Y) + reach, Math.Max(_centre.Z, end.Z) + reach);
            gather(min, max, _triangles);
        }

        // Each triangle's unit normal once per tick, not once per substep.
        _triangleNormals.Clear();
        foreach (CollisionTriangle triangle in _triangles)
        {
            Vector3d normal = Vector3d.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            double area = normal.Length;
            _triangleNormals.Add(area > 1e-9 ? normal / area : default);
        }

        bool touched = false;
        double hardest = 0.0;
        Vector3d hardestAt = default;
        for (int substep = 0; substep < substeps; substep++)
        {
            _velocity = new Vector3d(_velocity.X, _velocity.Y, _velocity.Z - (GravityPerTick * dt));

            Matrix3d rotation = _orientation.ToMatrix();
            Matrix3d inverseInertia = InverseInertiaWorld(rotation);
            Vector3d omega = inverseInertia.Transform(_angularMomentum);

            _contacts.Clear();
            FindContacts(rotation, omega, dt);

            Vector3d pseudoVelocity = default;
            Vector3d pseudoOmega = default;
            if (_contacts.Count > 0)
            {
                touched = true;
                Prepare(inverseInertia, omega, dt, ref hardest, ref hardestAt);
                SolveVelocities(inverseInertia);
                SolvePositions(inverseInertia, dt, out pseudoVelocity, out pseudoOmega);

                // Rolling resistance: the contact patch is not a point, so a gun on its side does not
                // spin on the floor for ever.
                _angularMomentum *= Math.Max(0.0, 1.0 - (RollingResistancePerSecond * dt / TicksPerSecond));
            }

            omega = inverseInertia.Transform(_angularMomentum);
            _centre += (_velocity + pseudoVelocity) * dt;
            _orientation = _orientation.Integrate(omega + pseudoOmega, dt);
        }

        if (hardest > ImpactSoundSpeed && !_wasTouching)
        {
            LastImpactSpeed = hardest;
            LastImpactPosition = hardestAt;
        }

        _wasTouching = touched;
        UpdateSleep(touched);
    }

    private void UpdateSleep(bool touched)
    {
        if (!touched)
        {
            _quietTicks = 0;
            if (++_ticksWithoutContact >= LostTicks)
            {
                GoToSleep();
            }

            return;
        }

        _ticksWithoutContact = 0;
        if (_velocity.Length < SleepLinear && AngularVelocity.Length < SleepAngular)
        {
            if (++_quietTicks >= SleepTicks)
            {
                GoToSleep();
            }
        }
        else
        {
            _quietTicks = 0;
        }
    }

    private void GoToSleep()
    {
        _velocity = default;
        _angularMomentum = default;
        IsAtRest = true;
    }

    private Matrix3d InverseInertiaWorld(Matrix3d rotation) =>
        rotation * _inverseInertiaBody * rotation.Transposed();

    private void FindContacts(Matrix3d rotation, Vector3d omega, double dt)
    {
        if (_triangles.Count == 0)
        {
            return;
        }

        // The hull in world space, and its bounds for the edge pass.
        Span<Vector3d> world = _hull.Length <= 128 ? stackalloc Vector3d[_hull.Length] : new Vector3d[_hull.Length];
        Vector3d low = new(double.MaxValue), high = new(double.MinValue);
        for (int i = 0; i < _hull.Length; i++)
        {
            Vector3d p = _centre + rotation.Transform(_hull[i]);
            world[i] = p;
            low = new(Math.Min(low.X, p.X), Math.Min(low.Y, p.Y), Math.Min(low.Z, p.Z));
            high = new(Math.Max(high.X, p.X), Math.Max(high.Y, p.Y), Math.Max(high.Z, p.Z));
        }

        // Only triangles within reach of the hull this substep can touch it.
        double reach = ((_velocity.Length + (omega.Length * _radius)) * dt) + SpeculativeMarginUnits;
        Vector3d nearLow = new(low.X - reach, low.Y - reach, low.Z - reach);
        Vector3d nearHigh = new(high.X + reach, high.Y + reach, high.Z + reach);

        for (int t = 0; t < _triangles.Count; t++)
        {
            CollisionTriangle triangle = _triangles[t];
            Vector3d normal = _triangleNormals[t];
            if (normal.LengthSquared == 0.0 || !triangle.Overlaps(nearLow, nearHigh))
            {
                continue;
            }

            // Winding means nothing (level meshes wind inward, terrain outward), so the surface faces
            // whichever side the body's centre is on.
            if (Vector3d.Dot(_centre - triangle.A, normal) < 0.0)
            {
                normal = -normal;
            }

            VertexContacts(triangle, normal, world, omega, dt);
        }

        EdgeContacts(rotation, low, high);

        if (_contacts.Count > MaximumContacts)
        {
            _contacts.Sort(static (a, b) => a.Separation.CompareTo(b.Separation));
            _contacts.RemoveRange(MaximumContacts, _contacts.Count - MaximumContacts);
        }
    }

    // A hull vertex at, below or about to reach a triangle's surface this substep.
    private void VertexContacts(in CollisionTriangle triangle, Vector3d normal, Span<Vector3d> world, Vector3d omega, double dt)
    {
        for (int i = 0; i < world.Length; i++)
        {
            Vector3d p = world[i];
            double separation = Vector3d.Dot(p - triangle.A, normal);
            if (separation < -MaximumVertexDepthUnits)
            {
                continue;
            }

            Vector3d arm = p - _centre;
            Vector3d pointVelocity = _velocity + Vector3d.Cross(omega, arm);
            double approach = Math.Max(0.0, -Vector3d.Dot(pointVelocity, normal)) * dt;
            if (separation > approach + SpeculativeMarginUnits)
            {
                continue;
            }

            Vector3d onPlane = p - (normal * separation);
            if (!InsideTriangle(onPlane, triangle, normal))
            {
                continue;
            }

            AddContact(new Contact(onPlane, normal, separation, i));
        }
    }

    // A world edge cutting into the hull: the ledge a rifle lies across, the corner of a step.
    private void EdgeContacts(Matrix3d rotation, Vector3d low, Vector3d high)
    {
        _edgeKeys.Clear();
        foreach (CollisionTriangle triangle in _triangles)
        {
            EdgeContact(triangle.A, triangle.B, rotation, low, high);
            EdgeContact(triangle.B, triangle.C, rotation, low, high);
            EdgeContact(triangle.C, triangle.A, rotation, low, high);
        }
    }

    private void EdgeContact(Vector3d a, Vector3d b, Matrix3d rotation, Vector3d low, Vector3d high)
    {
        if (Math.Max(a.X, b.X) < low.X || Math.Min(a.X, b.X) > high.X
            || Math.Max(a.Y, b.Y) < low.Y || Math.Min(a.Y, b.Y) > high.Y
            || Math.Max(a.Z, b.Z) < low.Z || Math.Min(a.Z, b.Z) > high.Z)
        {
            return;
        }

        // Two triangles share every inner edge; test each segment once.
        (long, long, long) ka = Quantise(a), kb = Quantise(b);
        (long, long, long, long, long, long) key = ka.CompareTo(kb) <= 0
            ? (ka.Item1, ka.Item2, ka.Item3, kb.Item1, kb.Item2, kb.Item3)
            : (kb.Item1, kb.Item2, kb.Item3, ka.Item1, ka.Item2, ka.Item3);
        if (!_edgeKeys.Add(key))
        {
            return;
        }

        Vector3d la = rotation.TransformTransposed(a - _centre);
        Vector3d lb = rotation.TransformTransposed(b - _centre);

        double enter = 0.0, leave = 1.0;
        foreach (HullFace face in _faces)
        {
            double da = Vector3d.Dot(face.Normal, la) - face.Offset;
            double db = Vector3d.Dot(face.Normal, lb) - face.Offset;
            if (da > 0.0 && db > 0.0)
            {
                return;
            }

            if (da > 0.0)
            {
                enter = Math.Max(enter, da / (da - db));
            }
            else if (db > 0.0)
            {
                leave = Math.Min(leave, da / (da - db));
            }

            if (enter > leave)
            {
                return;
            }
        }

        Vector3d inside = la + ((lb - la) * ((enter + leave) * 0.5));
        double depth = double.MaxValue;
        Vector3d exitNormal = default;
        foreach (HullFace face in _faces)
        {
            double d = face.Offset - Vector3d.Dot(face.Normal, inside);
            if (d < depth)
            {
                depth = d;
                exitNormal = face.Normal;
            }
        }

        if (depth <= 0.0 || depth > MaximumVertexDepthUnits)
        {
            return;
        }

        // The body leaves by the face the edge is nearest, so it is pushed opposite that face's normal.
        Vector3d normal = -rotation.Transform(exitNormal);
        AddContact(new Contact(_centre + rotation.Transform(inside), normal, -depth, -1));
    }

    private void AddContact(Contact contact)
    {
        // A vertex on a shared edge of two coplanar triangles is one contact, not two.
        if (contact.Vertex >= 0)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact other = _contacts[i];
                if (other.Vertex == contact.Vertex && Vector3d.Dot(other.Normal, contact.Normal) > 0.95)
                {
                    if (contact.Separation < other.Separation)
                    {
                        _contacts[i] = contact;
                    }

                    return;
                }
            }
        }

        _contacts.Add(contact);
    }

    private void Prepare(Matrix3d inverseInertia, Vector3d omega, double dt, ref double hardest, ref Vector3d hardestAt)
    {
        for (int i = 0; i < _contacts.Count; i++)
        {
            Contact c = _contacts[i];
            Vector3d arm = c.Point - _centre;
            Tangents(c.Normal, out Vector3d t1, out Vector3d t2);

            double closing = Vector3d.Dot(_velocity + Vector3d.Cross(omega, arm), c.Normal);
            if (-closing > hardest && c.Separation <= SpeculativeMarginUnits)
            {
                hardest = -closing;
                hardestAt = c.Point;
            }

            // The normal speed the constraint allows. A gap may be closed within this substep and no
            // faster; a hard impact comes back at the restitution fraction.
            // Restitution only for a contact the body actually reaches within this substep; one it is
            // merely approaching must not throw it back from the air above the surface.
            double target = c.Separation > 0.0 ? -c.Separation / dt : 0.0;
            if (closing < -RestitutionThreshold && -closing * dt >= c.Separation)
            {
                target = Math.Max(target, -Restitution * closing);
            }

            _contacts[i] = c with
            {
                Arm = arm,
                Tangent1 = t1,
                Tangent2 = t2,
                NormalMass = EffectiveMass(inverseInertia, arm, c.Normal),
                Tangent1Mass = EffectiveMass(inverseInertia, arm, t1),
                Tangent2Mass = EffectiveMass(inverseInertia, arm, t2),
                TargetSpeed = target,
            };
        }
    }

    private void SolveVelocities(Matrix3d inverseInertia)
    {
        Span<double> normal = stackalloc double[_contacts.Count];
        Span<double> friction1 = stackalloc double[_contacts.Count];
        Span<double> friction2 = stackalloc double[_contacts.Count];

        for (int iteration = 0; iteration < VelocityIterations; iteration++)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact c = _contacts[i];
                Vector3d omega = inverseInertia.Transform(_angularMomentum);
                Vector3d relative = _velocity + Vector3d.Cross(omega, c.Arm);

                double lambda = c.NormalMass * (c.TargetSpeed - Vector3d.Dot(relative, c.Normal));
                double accumulated = Math.Max(normal[i] + lambda, 0.0);
                lambda = accumulated - normal[i];
                normal[i] = accumulated;
                ApplyImpulse(c.Arm, c.Normal * lambda);

                // Coulomb friction: the tangential impulse stays inside the cone the normal allows.
                omega = inverseInertia.Transform(_angularMomentum);
                relative = _velocity + Vector3d.Cross(omega, c.Arm);
                double f1 = friction1[i] - (c.Tangent1Mass * Vector3d.Dot(relative, c.Tangent1));
                double f2 = friction2[i] - (c.Tangent2Mass * Vector3d.Dot(relative, c.Tangent2));
                double limit = Friction * normal[i];
                double magnitude = Math.Sqrt((f1 * f1) + (f2 * f2));
                if (magnitude > limit && magnitude > 0.0)
                {
                    f1 *= limit / magnitude;
                    f2 *= limit / magnitude;
                }

                ApplyImpulse(c.Arm, (c.Tangent1 * (f1 - friction1[i])) + (c.Tangent2 * (f2 - friction2[i])));
                friction1[i] = f1;
                friction2[i] = f2;
            }
        }
    }

    // Split-impulse position correction: a velocity used to move the body out of the surface this
    // substep and then discarded, so fixing penetration never adds kinetic energy.
    private void SolvePositions(Matrix3d inverseInertia, double dt, out Vector3d velocity, out Vector3d omega)
    {
        velocity = default;
        Vector3d momentum = default;
        Span<double> accumulated = stackalloc double[_contacts.Count];

        for (int iteration = 0; iteration < PositionIterations; iteration++)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact c = _contacts[i];
                double penetration = -c.Separation - PenetrationSlop;
                if (penetration <= 0.0)
                {
                    continue;
                }

                double target = PositionCorrection * penetration / dt;
                Vector3d spin = inverseInertia.Transform(momentum);
                double speed = Vector3d.Dot(velocity + Vector3d.Cross(spin, c.Arm), c.Normal);
                double lambda = c.NormalMass * (target - speed);
                double total = Math.Max(accumulated[i] + lambda, 0.0);
                lambda = total - accumulated[i];
                accumulated[i] = total;

                Vector3d impulse = c.Normal * lambda;
                velocity += impulse * _inverseMass;
                momentum += Vector3d.Cross(c.Arm, impulse);
            }
        }

        omega = inverseInertia.Transform(momentum);
    }

    private void ApplyImpulse(Vector3d arm, Vector3d impulse)
    {
        _velocity += impulse * _inverseMass;
        _angularMomentum += Vector3d.Cross(arm, impulse);
    }

    private double EffectiveMass(Matrix3d inverseInertia, Vector3d arm, Vector3d direction)
    {
        Vector3d angular = Vector3d.Cross(inverseInertia.Transform(Vector3d.Cross(arm, direction)), arm);
        double denominator = _inverseMass + Vector3d.Dot(direction, angular);
        return denominator > 0.0 ? 1.0 / denominator : 0.0;
    }

    private static void Tangents(Vector3d normal, out Vector3d t1, out Vector3d t2)
    {
        Vector3d reference = Math.Abs(normal.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX;
        t1 = Vector3d.Cross(reference, normal).Normalized();
        t2 = Vector3d.Cross(normal, t1);
    }

    private static bool InsideTriangle(Vector3d p, in CollisionTriangle t, Vector3d normal)
    {
        // A hair of tolerance so a vertex exactly on a shared edge is not lost between two triangles.
        const double tolerance = -1e-6;
        Vector3d c0 = Vector3d.Cross(t.B - t.A, p - t.A);
        Vector3d c1 = Vector3d.Cross(t.C - t.B, p - t.B);
        Vector3d c2 = Vector3d.Cross(t.A - t.C, p - t.C);
        double d0 = Vector3d.Dot(c0, normal);
        double d1 = Vector3d.Dot(c1, normal);
        double d2 = Vector3d.Dot(c2, normal);
        double scale = Vector3d.Cross(t.B - t.A, t.C - t.A).Length * 1e-9;
        return (d0 >= tolerance - scale && d1 >= tolerance - scale && d2 >= tolerance - scale)
            || (d0 <= scale && d1 <= scale && d2 <= scale);
    }

    private static (long, long, long) Quantise(Vector3d v) =>
        ((long)Math.Round(v.X * 4.0), (long)Math.Round(v.Y * 4.0), (long)Math.Round(v.Z * 4.0));

    private readonly record struct Contact(Vector3d Point, Vector3d Normal, double Separation, int Vertex)
    {
        public Vector3d Arm { get; init; }

        public Vector3d Tangent1 { get; init; }

        public Vector3d Tangent2 { get; init; }

        public double NormalMass { get; init; }

        public double Tangent1Mass { get; init; }

        public double Tangent2Mass { get; init; }

        public double TargetSpeed { get; init; }
    }
}
