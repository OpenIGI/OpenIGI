using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Physics;

/// <summary>What a physics object moves through: the collision world, its materials, and how the struck answer.</summary>
/// <param name="Query">The collision world the contact lines and the ground probes are traced in.</param>
/// <param name="Materials">The level's materials, which a struck face's names.</param>
/// <param name="Includes">
/// Which objects the lines may strike, the evaluate event the update is given (<c>eCollisionEvalEvent</c>); <see langword="null"/>
/// lets every object take part.
/// </param>
/// <param name="Hit">
/// A struck owner's answer to <c>PhysicsObjHit</c> (<c>PhysicsObj_eHit</c>), given the owner and the object; <see langword="true"/>
/// lets the object through. <see langword="null"/>, or an owner that does not answer, stops it.
/// </param>
public sealed record Igi2PhysicsWorld(
    IIgi2CollisionQuery Query,
    Igi2GameMaterials Materials,
    Func<object, bool>? Includes = null,
    Func<object, Igi2PhysicsObject, bool>? Hit = null);

/// <summary>A point an object touches the world with, <c>PhysicsObjCollPoint_s</c> (56 bytes at <c>PhysicsObj_s + 0x218</c>).</summary>
public sealed class Igi2PhysicsContactPoint
{
    /// <summary>Gets or sets the point's kind, <c>iType</c>.</summary>
    public int Type { get; set; }

    /// <summary>Gets or sets its material, <c>iMaterial</c>.</summary>
    public int Material { get; set; }

    /// <summary>Gets or sets its coefficient of restitution, <c>vBounceFactor</c>.</summary>
    public float Bounce { get; set; }

    /// <summary>Gets or sets where it is on the object, in the object's frame and scaled, <c>tPos</c>.</summary>
    public Vector3f LocalPosition { get; set; }

    /// <summary>Gets or sets a value indicating whether it takes part, <c>isActive</c>.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets a value indicating whether the object has it at all, <c>isUsed</c>.</summary>
    public bool IsUsed { get; set; }

    /// <summary>Gets or sets a value indicating whether it touched this tick, <c>isContact</c>.</summary>
    public bool IsContact { get; set; }

    /// <summary>Gets or sets its first friction coefficient, <c>vFrictionX</c>.</summary>
    public float FrictionX { get; set; }

    /// <summary>Gets or sets its second friction coefficient, <c>vFrictionY</c>: the one a contact's friction reads.</summary>
    public float FrictionY { get; set; }

    /// <summary>Gets or sets the unit normal of the surface it last struck, <c>tNormal</c>.</summary>
    public Vector3f Normal { get; set; }

    /// <summary>Gets or sets how far along its line it last struck, <c>vDistance</c>.</summary>
    public float Distance { get; set; }

    /// <summary>Gets or sets its height offset, <c>vZDisplacement</c>.</summary>
    public float ZDisplacement { get; set; }
}

/// <summary>
/// A rigid object physics moves, <c>PhysicsObj_s</c>: a body, its contact points and the tick that moves it through the
/// world (<c>PhysicsObj_Update</c>, <c>igi2 0x5AF350</c>), with the client's 24-bit truncating FPU arithmetic in
/// disassembly order.
/// </summary>
/// <remarks>
/// <para>
/// A tick: gravity and the air and spin damping (<see cref="AddNonContactForces"/>); then, with collision detection on, each
/// contact point is traced along its velocity and the first that strikes and is not leaving the surface bounces the body and
/// rubs it (<see cref="AddContactForces"/>). Fast enough, the object moves by its velocity; any point that ends up inside a
/// surface lifts it out and loses the speed into that surface (<see cref="AssertNoIntersection"/>).
/// </para>
/// <para>
/// Positions are doubles, but every addition to one goes through the FPU at 24 bits, so a moved position keeps only a
/// float's precision, as the client's does.
/// </para>
/// </remarks>
public sealed class Igi2PhysicsObject
{
    /// <summary>The contact points an object can have, <c>atCollPoint[22]</c>.</summary>
    public const int MaxContactPoints = 22;

    /// <summary>The speed below which the object does not move at all, <c>0x4223D70A</c>: 40.96 units (a centimetre) a tick.</summary>
    public const float StillSpeed = 40.96f;

    /// <summary>How far above each contact point the intersection test starts, and how far down it looks: 2048 units.</summary>
    public const double IntersectionReach = 2048.0;

    /// <summary>The scale applied to the inverse moments after a type's box sets them, <c>0x3F8147AE</c>.</summary>
    private static readonly float InertiaScale = BitConverter.Int32BitsToSingle(0x3F8147AE);

    private readonly Igi2PhysicsContactPoint[] _points = new Igi2PhysicsContactPoint[MaxContactPoints];
    private readonly Igi2LineTrace _contactLines = new(MaxContactPoints);
    private readonly Igi2LineTrace _groundLines = new(MaxContactPoints);
    private readonly Vector3f[] _arms = new Vector3f[MaxContactPoints];
    private readonly Vector3f[] _armVelocities = new Vector3f[MaxContactPoints];
    private readonly int[] _lineOwners = new int[MaxContactPoints];
    private readonly double[] _groundDistances = new double[MaxContactPoints];

    /// <summary>
    /// Initializes a new instance of the <see cref="Igi2PhysicsObject"/> class: <c>PhysicsObj_Create</c> (<c>igi2 0x5AE880</c>)
    /// then <c>PhysicsObj_Init</c> (<c>0x5AEA20</c>) with its type, at a position, unturned and still.
    /// </summary>
    /// <param name="type">Its type.</param>
    /// <param name="model">Its model, whose magic vertices place the contact points; <see langword="null"/> puts them all at the centre.</param>
    /// <param name="position">Where it is.</param>
    /// <param name="scale">Its scale, <c>tScale</c>, which the contact points are scaled by.</param>
    /// <remarks>
    /// The body is the type's box, in metres turned to units, with its inverse moments raised by a hundredth. The object
    /// collides with every group, the ground included, and its last position starts where it is.
    /// </remarks>
    /// <param name="typeModel">
    /// The type's own model, whose contact points' magic vertices size a type defined with no size (<c>PhysicsObjType_GetSize</c>,
    /// <c>igi2 0x5AD4D0</c>); the drawn model when omitted.
    /// </param>
    public Igi2PhysicsObject(Igi2PhysicsObjectType type, MefModel? model, Vector3d position, Vector3f scale, MefModel? typeModel = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Position = position;
        Scale = scale;
        for (int i = 0; i < MaxContactPoints; i++)
        {
            _points[i] = new Igi2PhysicsContactPoint();
        }

        Vector3f size = type.Size;
        if (size.X == 0f || size.Y == 0f || size.Z == 0f)
        {
            size = SizeFromModel(type, typeModel ?? model);
        }

        Body.InitBox(type.Mass, ((Fpu)size.X * 4096f).F, ((Fpu)size.Y * 4096f).F, ((Fpu)size.Z * 4096f).F);
        Vector3f inertia = Body.InverseInertia;
        Body.InverseInertia = new Vector3f(
            ((Fpu)inertia.X * InertiaScale).F,
            ((Fpu)inertia.Y * InertiaScale).F,
            ((Fpu)inertia.Z * InertiaScale).F);

        int count = Math.Min(type.ContactPoints.Count, MaxContactPoints);
        for (int i = 0; i < count; i++)
        {
            Igi2PhysicsContactPointType defined = type.ContactPoints[i];
            Igi2PhysicsContactPoint point = _points[i];
            point.Type = defined.Type;
            point.Material = defined.Material;
            point.Bounce = defined.Bounce;
            point.IsActive = true;
            point.IsUsed = true;
            point.IsContact = false;
            point.FrictionX = defined.FrictionX;
            point.FrictionY = defined.FrictionY;
            point.ZDisplacement = 0f;
            point.LocalPosition = MagicVertex(model, defined.MagicVertex, scale);
        }

        LastPosition = position;
        LastOrientation = [.. Orientation];
    }

    /// <summary>Gets its type, <c>ptType</c>.</summary>
    public Igi2PhysicsObjectType Type { get; }

    /// <summary>Gets its body, <c>tPhysics</c>.</summary>
    public Igi2RigidBody Body { get; } = new();

    /// <summary>Gets or sets where it is, <c>tPos</c>.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets its orientation, <c>tOrientation</c>: nine floats row by row, which the update turns in place.</summary>
    public float[] Orientation { get; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets its scale, <c>tScale</c>.</summary>
    public Vector3f Scale { get; }

    /// <summary>Gets its contact points, <c>atCollPoint</c>: the type's first, the rest unused.</summary>
    public IReadOnlyList<Igi2PhysicsContactPoint> ContactPoints => _points;

    /// <summary>Gets how many points touched at the last update, <c>nContact</c>: one at most, the first that did.</summary>
    public int ContactCount { get; private set; }

    /// <summary>Gets or sets the query groups its lines are traced against, <c>nCollisionTaskGroups</c>: all of them.</summary>
    public uint CollisionGroups { get; set; } = 0xFFFFFFFF;

    /// <summary>Gets or sets a value indicating whether its points are traced at all, <c>isCollDetectionEnabled</c>.</summary>
    public bool IsCollisionDetectionEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it is held level, <c>isForceZ</c>: no vertical speed, its third row kept.</summary>
    public bool IsForceZ { get; set; }

    /// <summary>Gets or sets a value indicating whether a point moved since the last idle test, <c>isMovingCollPoint</c>.</summary>
    public bool IsMovingContactPoint { get; set; }

    /// <summary>Gets where it was before the last update, <c>tLastPosition</c>.</summary>
    public Vector3d LastPosition { get; private set; }

    /// <summary>Gets its orientation before the last update, <c>tLastOrientation</c>.</summary>
    public float[] LastOrientation { get; private set; }

    /// <summary>Gets how far the last update moved it, <c>tDeltaPosition</c>.</summary>
    public Vector3f DeltaPosition { get; private set; }

    /// <summary>Moves the object a tick, <c>PhysicsObj_Update</c> (<c>igi2 0x5AF350</c>).</summary>
    /// <param name="gravity">The gravity, units a tick squared.</param>
    /// <param name="airResistance">What the velocity keeps each tick.</param>
    /// <param name="spinResistance">What the angular momentum keeps each tick, and each contact.</param>
    /// <param name="world">What it moves through.</param>
    /// <remarks>
    /// The orientation and position are taken first. Then the forces, and the contacts if detection is on; an object held
    /// level loses its vertical speed. Only faster than <see cref="StillSpeed"/> does the position take the velocity; held
    /// level, the orientation is rebuilt about its old third row. Any point inside a surface then lifts the object out, the
    /// object takes its new position, and the change from the last is kept. Attachments and the change of orientation, which
    /// only what rides on an object reads, are not kept.
    /// </remarks>
    public void Update(float gravity, float airResistance, float spinResistance, Igi2PhysicsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        float ox = Orientation[6], oy = Orientation[7], oz = Orientation[8];
        Vector3d position = Position;
        AddNonContactForces(gravity, airResistance, spinResistance);
        if (IsCollisionDetectionEnabled)
        {
            AddContactForces(gravity, spinResistance, world);
        }

        if (IsForceZ)
        {
            Body.Velocity = new Vector3f(Body.Velocity.X, Body.Velocity.Y, 0f);
            Body.UpdateSpeedApproximation();
        }

        if (Body.Speed > StillSpeed)
        {
            Vector3f v = Body.Velocity;
            position = new Vector3d(((Fpu)v.X + position.X).Value, ((Fpu)v.Y + position.Y).Value, ((Fpu)v.Z + position.Z).Value);
        }

        if (IsForceZ)
        {
            HoldLevel(ox, oy, oz);
        }

        AssertNoIntersection(world, ref position);
        Position = position;
        DeltaPosition = new Vector3f(
            ((Fpu)position.X - LastPosition.X).F,
            ((Fpu)position.Y - LastPosition.Y).F,
            ((Fpu)position.Z - LastPosition.Z).F);
        LastPosition = position;
        LastOrientation = [.. Orientation];
    }

    /// <summary>Takes where it is and how it is turned as where it was, as <c>PhysicsObj_Init</c> does once it is placed.</summary>
    public void ResetLast()
    {
        LastPosition = Position;
        LastOrientation = [.. Orientation];
    }

    /// <summary>Marks it touching, <c>nContact = 1</c>, as a car's wheels do when they meet the ground.</summary>
    public void NoteContact() => ContactCount = 1;

    /// <summary>
    /// Whether the object has come to rest, <c>PhysicsObj_IsIdle</c> (<c>igi2 0x5AF810</c>): touching, no point moved since
    /// the last test, no force waiting, and slower and spinning less than its type's rest thresholds.
    /// </summary>
    /// <returns><see langword="true"/> when it rests.</returns>
    /// <remarks>The test forgets that a point moved, whatever it answers.</remarks>
    public bool IsIdle()
    {
        bool moved = IsMovingContactPoint;
        IsMovingContactPoint = false;
        if (ContactCount <= 0 || moved || Body.Forces.Count != 0 || !(Body.Speed < Type.RestSpeed))
        {
            return false;
        }

        Vector3f w = Body.AngularVelocity;
        Fpu spin = (((Fpu)w.X * w.X) + ((Fpu)w.Y * w.Y)) + ((Fpu)w.Z * w.Z);
        return spin.Value < Type.RestAngularVelocitySquared;
    }

    /// <summary>
    /// A contact point's place on the object, <c>RigidDynCubeObj_GetMagicVertexLocalPos</c> (<c>igi2 0x58E3B0</c>): the model's
    /// magic vertex times the scale, or the centre when the model has none of that number.
    /// </summary>
    /// <summary>
    /// A type's size from its model, <c>PhysicsObjType_GetSizeFromModel</c> (<c>igi2 0x5AD180</c>): the extent of its contact points'
    /// magic vertices along each axis, a point with no such vertex at the origin.
    /// </summary>
    private static Vector3f SizeFromModel(Igi2PhysicsObjectType type, MefModel? model)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (Igi2PhysicsContactPointType point in type.ContactPoints)
        {
            Vector3f v = model is not null && model.TryGetMagicVertex(point.MagicVertex, out Vector3f vertex) ? vertex : default;
            minX = Math.Min(minX, v.X);
            minY = Math.Min(minY, v.Y);
            minZ = Math.Min(minZ, v.Z);
            maxX = Math.Max(maxX, v.X);
            maxY = Math.Max(maxY, v.Y);
            maxZ = Math.Max(maxZ, v.Z);
        }

        return new Vector3f((float)Math.Abs(maxX - minX), (float)Math.Abs(maxY - minY), (float)Math.Abs(maxZ - minZ));
    }

    private static Vector3f MagicVertex(MefModel? model, int index, Vector3f scale)
    {
        if (model is null)
        {
            return default;
        }

        Vector3f v = model.TryGetMagicVertex(index, out Vector3f vertex) ? vertex : default;
        return new Vector3f(((Fpu)scale.X * v.X).F, ((Fpu)scale.Y * v.Y).F, ((Fpu)scale.Z * v.Z).F);
    }

    /// <summary>
    /// Gravity and the damping, <c>PhysicsObj_AddNonContactForces</c> (<c>igi2 0x5AF1C0</c>): the type's weight, turned into
    /// the body's frame, acts at the centre; the body takes its step, and its spin and velocity are damped.
    /// </summary>
    private void AddNonContactForces(float gravity, float airResistance, float spinResistance)
    {
        float[] m = Orientation;
        Fpu weight = -((Fpu)gravity * Type.Mass);
        float fx = ((((Fpu)m[3] + m[0]) * 0f) + (weight * m[6])).F;
        float fy = ((((Fpu)m[4] + m[1]) * 0f) + (weight * m[7])).F;
        float fz = ((((Fpu)m[5] + m[2]) * 0f) + (weight * m[8])).F;
        Body.AddForce(new Igi2PointForce(new Vector3f(fx, fy, fz), default));
        Body.Update(m);
        Body.AngularMomentum = Igi2RigidBody.Scale(Body.AngularMomentum, spinResistance);
        Body.Velocity = Igi2RigidBody.Scale(Body.Velocity, airResistance);
        Body.UpdateSpeedApproximation();
    }

    /// <summary>
    /// The contacts, <c>PhysicsObj_AddContactForces</c> (<c>igi2 0x5AEC90</c>): each point is traced from where it is along
    /// its velocity, and the first one to strike something it is not leaving bounces the body and rubs it.
    /// </summary>
    /// <returns>Zero for no contact, one for a contact, two for one meeting the surface faster than a unit a tick.</returns>
    /// <remarks>
    /// <para>
    /// A hit counts when its distance is within the point's speed plus <see cref="StillSpeed"/>, or whenever the material it
    /// leaves has density; an owner that lets physics objects through (glass, water) does not count, and slows the next
    /// contact's body to a quarter.
    /// </para>
    /// <para>
    /// The first counting hit whose point is not separating faster than a unit a tick is the contact: the surface's normal
    /// is kept on the point, the point is marked in contact, and the collision and friction responses follow. The trace
    /// stops there.
    /// </para>
    /// </remarks>
    private int AddContactForces(float gravity, float spinFriction, Igi2PhysicsWorld world)
    {
        float[] m = Orientation;
        Vector3d origin = Position;
        int lines = 0;
        for (int i = 0; i < MaxContactPoints; i++)
        {
            Igi2PhysicsContactPoint point = _points[i];
            if (!point.IsUsed || !point.IsActive)
            {
                continue;
            }

            point.IsContact = false;
            Vector3f p = point.LocalPosition;
            Vector3f arm = new(
                ((((Fpu)m[2] * p.Z) + ((Fpu)m[1] * p.Y)) + ((Fpu)m[0] * p.X)).F,
                ((((Fpu)m[5] * p.Z) + ((Fpu)m[4] * p.Y)) + ((Fpu)m[3] * p.X)).F,
                ((((Fpu)m[8] * p.Z) + ((Fpu)m[7] * p.Y)) + ((Fpu)m[6] * p.X)).F);
            Vector3d start = new(((Fpu)arm.X + origin.X).Value, ((Fpu)arm.Y + origin.Y).Value, ((Fpu)arm.Z + origin.Z).Value);
            Vector3f velocity = Body.PointVelocity(arm);
            _contactLines.Starts[lines] = start;
            _contactLines.Ends[lines] = new Vector3d(
                ((Fpu)velocity.X + start.X).Value,
                ((Fpu)velocity.Y + start.Y).Value,
                ((Fpu)velocity.Z + start.Z).Value);
            _arms[lines] = arm;
            _armVelocities[lines] = velocity;
            _lineOwners[lines] = i;
            lines++;
        }

        if (lines == 0)
        {
            return 0;
        }

        ContactCount = 0;
        _contactLines.Count = lines;
        _contactLines.Includes = world.Includes;
        world.Query.TraceLines(_contactLines, CollisionGroups);

        bool letThrough = false;
        for (int j = 0; j < lines; j++)
        {
            Igi2Intersection hit = _contactLines.Intersections[j];
            if (!hit.IsHit)
            {
                continue;
            }

            bool dense = hit.LeaveMaterial is int row && world.Materials[row] is { } leaving && leaving.QMaterial.Density != 0f;
            bool passes = false;
            if (hit.HitObject is { } owner)
            {
                letThrough = world.Hit?.Invoke(owner.Owner, this) == true;
                passes = letThrough;
            }

            Vector3f v = _armVelocities[j];
            bool counts = !passes
                && (dense || (Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z)) + StillSpeed).Value > hit.Distance);
            if (!counts)
            {
                continue;
            }

            Igi2PhysicsContactPoint point = _points[_lineOwners[j]];
            point.Distance = ((Fpu)hit.Distance).F;
            Vector3f normal = Igi2RigidBody.Normalised(hit.Normal);
            point.Normal = normal;
            Vector3f pv = Body.PointVelocity(_arms[j]);
            float relation = ((((Fpu)pv.X * normal.X) + ((Fpu)pv.Z * normal.Z)) + ((Fpu)pv.Y * normal.Y)).F;
            if (relation > 1f)
            {
                continue;
            }

            int result = relation > -1f ? 1 : 2;
            ContactCount++;
            point.IsContact = true;
            CollisionResponse(normal, _arms[j], relation, point.Bounce);
            FrictionResponse(normal, gravity, point.FrictionY, spinFriction);
            if (letThrough)
            {
                Body.Velocity = Igi2RigidBody.Scale(Body.Velocity, 0.25f);
                Body.UpdateSpeedApproximation();
            }

            return result;
        }

        return 0;
    }

    /// <summary>
    /// The bounce, <c>PhysicsObj_CollisionResponse</c> (<c>igi2 0x5AFC20</c>): the impulse along the normal that turns the
    /// point's approach into the bounce's share of it, applied to the spin and the centre; then whatever velocity is left
    /// into the surface is taken away.
    /// </summary>
    private void CollisionResponse(Vector3f n, Vector3f r, float relation, float bounce)
    {
        float[] m = Orientation;
        Vector3f inertia = Body.InverseInertia;

        // The moment arm crossed with the normal, into the body's frame, through the inverse inertia and back out.
        Fpu cx = ((Fpu)n.Z * r.Y) - ((Fpu)r.Z * n.Y);
        Fpu cy = -(((Fpu)n.Z * r.X) - ((Fpu)r.Z * n.X));
        Fpu cz = ((Fpu)n.Y * r.X) - ((Fpu)n.X * r.Y);
        float ax = (((cx * m[0]) + (cz * m[6])) + (cy * m[3])).F;
        float ay = (((cx * m[1]) + (cz * m[7])) + (cy * m[4])).F;
        Fpu az = ((cz * m[8]) + (cy * m[5])) + (cx * m[2]);
        Fpu z0 = (Fpu)0f * az, y0 = (Fpu)ay * 0f;
        float bx = ((((Fpu)inertia.X * ax) + y0) + z0).F;
        Fpu x0 = (Fpu)ax * 0f;
        Fpu by = (((Fpu)inertia.Y * ay) + x0) + z0;
        Fpu bz = ((az * inertia.Z) + x0) + y0;
        float dx = ((((Fpu)bx * m[0]) + (by * m[1])) + (bz * m[2])).F;
        float dy = (((bz * m[5]) + (by * m[4])) + ((Fpu)bx * m[3])).F;
        Fpu dz = ((bz * m[8]) + (by * m[7])) + ((Fpu)bx * m[6]);
        Fpu ex = ((Fpu)dy * r.Z) - (dz * r.Y);
        Fpu ey = -(((Fpu)dx * r.Z) - (dz * r.X));
        float ez = (((Fpu)dx * r.Y) - ((Fpu)dy * r.X)).F;

        Fpu k = -(((Fpu)bounce + 1f) * relation);
        Fpu denominator = (((ex * n.X) + (ey * n.Y)) + ((Fpu)ez * n.Z)) + Body.InverseMass;
        float j = (k / denominator).F;
        float jx = ((Fpu)n.X * j).F, jy = ((Fpu)n.Y * j).F;
        Fpu jz = (Fpu)n.Z * j;

        // The impulse's moment, into the body's frame, onto the angular momentum.
        Fpu tx = (jz * r.Y) - ((Fpu)jy * r.Z);
        Fpu ty = -((jz * r.X) - ((Fpu)jx * r.Z));
        Fpu tz = ((Fpu)jy * r.X) - ((Fpu)jx * r.Y);
        float gx = (((tz * m[6]) + (ty * m[3])) + (tx * m[0])).F;
        float gy = (((tx * m[1]) + (tz * m[7])) + (ty * m[4])).F;
        Fpu gz = ((tx * m[2]) + (tz * m[8])) + (ty * m[5]);
        Vector3f l = Body.AngularMomentum;
        float lx = ((Fpu)gx + l.X).F, ly = ((Fpu)gy + l.Y).F, lz = (gz + l.Z).F;
        Body.AngularMomentum = new Vector3f(lx, ly, lz);

        Fpu lz0 = (Fpu)lz * 0f, ly0 = (Fpu)ly * 0f;
        float wx = ((((Fpu)lx * inertia.X) + ly0) + lz0).F;
        Fpu lx0 = (Fpu)lx * 0f;
        float wy = ((((Fpu)ly * inertia.Y) + lx0) + lz0).F;
        float wz = ((((Fpu)lz * inertia.Z) + lx0) + ly0).F;
        Body.AngularVelocity = new Vector3f(
            ((((Fpu)wz * m[2]) + ((Fpu)wy * m[1])) + ((Fpu)m[0] * wx)).F,
            ((((Fpu)wz * m[5]) + ((Fpu)wy * m[4])) + ((Fpu)wx * m[3])).F,
            ((((Fpu)wz * m[8]) + ((Fpu)wy * m[7])) + ((Fpu)wx * m[6])).F);

        float mInv = Body.InverseMass;
        Fpu qx = (Fpu)jx * mInv, qy = (Fpu)jy * mInv;
        float qz = (jz * mInv).F;
        Vector3f v = Body.Velocity;
        Body.Velocity = new Vector3f(((Fpu)v.X + qx).F, ((Fpu)v.Y + qy).F, ((Fpu)v.Z + qz).F);
        Body.UpdateSpeedApproximation();

        // What is still going into the surface is taken away.
        Vector3f nn = Igi2RigidBody.Normalised(n);
        v = Body.Velocity;
        Fpu dot = (((Fpu)nn.Z * v.Z) + ((Fpu)nn.Y * v.Y)) + ((Fpu)nn.X * v.X);
        if (dot.Value < 0.0)
        {
            float ux = (-((Fpu)nn.X * dot)).F, uy = (-(dot * nn.Y)).F, uz = (-(dot * nn.Z)).F;
            Body.Velocity = new Vector3f(((Fpu)ux + v.X).F, ((Fpu)uy + v.Y).F, ((Fpu)uz + v.Z).F);
            Body.UpdateSpeedApproximation();
        }
    }

    /// <summary>
    /// The rub, <c>PhysicsObj_FrictionResponse</c> (<c>igi2 0x5B00D0</c>): against the direction of travel along the surface,
    /// the friction of the weight on it, never more than the velocity that way; and the spin is damped.
    /// </summary>
    private void FrictionResponse(Vector3f n, float gravity, float friction, float spinFriction)
    {
        Fpu push = (Fpu)gravity * n.Z;
        if (push.Value < 0.0)
        {
            push = -push;
        }

        if (push.Value == 0.0)
        {
            return;
        }

        push *= friction;
        Vector3f u = Body.VelocityDirection;
        float tx = (((Fpu)u.Y * n.Z) - ((Fpu)u.Z * n.Y)).F;
        float ty = (-(((Fpu)u.X * n.Z) - ((Fpu)u.Z * n.X))).F;
        float tz = (((Fpu)u.X * n.Y) - ((Fpu)u.Y * n.X)).F;
        float length = Fpu.Sqrt((((Fpu)ty * ty) + ((Fpu)tz * tz)) + ((Fpu)tx * tx)).F;

        Vector3f against;
        if (length < 0.00001f)
        {
            against = default;
        }
        else
        {
            Vector3f across = new(tx, ty, tz);
            if (!Igi2RigidBody.IsZero(across))
            {
                Fpu r = (Fpu)1.0 / length;
                across = new Vector3f(((Fpu)tx * r).F, ((Fpu)ty * r).F, (r * tz).F);
            }

            against = new Vector3f(
                (((Fpu)across.Y * n.Z) - ((Fpu)across.Z * n.Y)).F,
                (-(((Fpu)across.X * n.Z) - ((Fpu)across.Z * n.X))).F,
                (((Fpu)across.X * n.Y) - ((Fpu)across.Y * n.X)).F);
        }

        Vector3f v = Body.Velocity;
        float along = ((((Fpu)against.Y * v.Y) + ((Fpu)against.X * v.X)) + ((Fpu)against.Z * v.Z)).F;
        Fpu limit = along < 0f ? -(Fpu)along : (Fpu)along;
        Fpu scale = push.Value > limit.Value ? limit : push;
        Body.Velocity = new Vector3f(
            (((Fpu)against.X * scale) + v.X).F,
            (((Fpu)against.Y * scale) + v.Y).F,
            ((scale * against.Z) + v.Z).F);
        Body.UpdateSpeedApproximation();
        Body.AngularMomentum = Igi2RigidBody.Scale(Body.AngularMomentum, spinFriction);
    }

    /// <summary>
    /// Lifts the object out of the ground, <c>PhysicsObj_AssertNoIntersection</c> (<c>igi2 0x5ADCA0</c>): from 2048 units above
    /// each contact point straight down to it, the deepest surface struck above the point raises the new position by its
    /// depth, and each struck surface takes away the velocity going into it.
    /// </summary>
    /// <returns>Whether any point was inside.</returns>
    /// <remarks>
    /// The test starts from where the object is, not where it is going. The velocity it changes is not re-approximated, and
    /// the height is added at 24 bits whether or not it is raised.
    /// </remarks>
    private bool AssertNoIntersection(Igi2PhysicsWorld world, ref Vector3d position)
    {
        float[] m = Orientation;
        Vector3d origin = Position;
        int lines = 0;
        for (int i = 0; i < MaxContactPoints; i++)
        {
            Igi2PhysicsContactPoint point = _points[i];
            if (!point.IsUsed || !point.IsActive)
            {
                continue;
            }

            Vector3f p = point.LocalPosition;
            Fpu rx = (((Fpu)m[2] * p.Z) + ((Fpu)m[1] * p.Y)) + ((Fpu)m[0] * p.X);
            Fpu ry = (((Fpu)m[5] * p.Z) + ((Fpu)m[4] * p.Y)) + ((Fpu)m[3] * p.X);
            float rz = ((((Fpu)m[8] * p.Z) + ((Fpu)m[7] * p.Y)) + ((Fpu)m[6] * p.X)).F;
            _groundLines.Starts[lines] = new Vector3d(
                (rx + origin.X).Value,
                (ry + origin.Y).Value,
                (((Fpu)rz + origin.Z) + IntersectionReach).Value);
            _groundDistances[lines] = -IntersectionReach;
            lines++;
        }

        if (lines == 0)
        {
            return false;
        }

        _groundLines.Count = lines;
        _groundLines.Includes = world.Includes;
        world.Query.NextZ(_groundLines, _groundDistances.AsSpan(0, lines), CollisionGroups);

        // The client unrolls the walk by four and orders the remainder's dot product differently.
        int unrolled = lines / 4 * 4;
        Fpu deepest = 0.0;
        for (int j = 0; j < lines; j++)
        {
            Igi2Intersection hit = _groundLines.Intersections[j];
            if (!hit.IsHit)
            {
                continue;
            }

            Fpu depth = (Fpu)IntersectionReach - ((Fpu)_groundLines.Starts[j].Z - hit.Position.Z);
            if (depth.Value > deepest.Value)
            {
                deepest = depth;
            }

            Vector3f n = hit.Normal;
            Vector3f v = Body.Velocity;
            Fpu dot = j < unrolled
                ? (((Fpu)n.Z * v.Z) + ((Fpu)n.Y * v.Y)) + ((Fpu)n.X * v.X)
                : (((Fpu)n.Y * v.Y) + ((Fpu)n.Z * v.Z)) + ((Fpu)n.X * v.X);
            if (dot.Value < 0.0)
            {
                float tx = ((Fpu)n.X * dot).F, ty = ((Fpu)n.Y * dot).F, tz = ((Fpu)n.Z * dot).F;
                Body.Velocity = new Vector3f(((Fpu)v.X - tx).F, ((Fpu)v.Y - ty).F, ((Fpu)v.Z - tz).F);
            }
        }

        position = new Vector3d(position.X, position.Y, (deepest + position.Z).Value);
        return deepest.Value > 0.0;
    }

    /// <summary>Rebuilds the orientation about its old third row, which it keeps: the first row from the second, the second from the first.</summary>
    private void HoldLevel(float ox, float oy, float oz)
    {
        float[] m = Orientation;
        m[0] = (((Fpu)oz * m[4]) - ((Fpu)oy * m[5])).F;
        m[1] = (-(((Fpu)oz * m[3]) - ((Fpu)ox * m[5]))).F;
        m[2] = (((Fpu)oy * m[3]) - ((Fpu)ox * m[4])).F;
        m[3] = (((Fpu)oy * m[2]) - ((Fpu)oz * m[1])).F;
        m[4] = (-(((Fpu)ox * m[2]) - ((Fpu)oz * m[0]))).F;
        m[5] = (((Fpu)ox * m[1]) - ((Fpu)oy * m[0])).F;
        m[6] = ox;
        m[7] = oy;
        m[8] = oz;
    }
}
