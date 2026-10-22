using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <content>
/// How a car moves (<c>Car_Move</c>): its thrust and steering come round by their inertias; its wheels' springs hold it up and its
/// driven wheels push it, each wheel rubbing along the ground as it is steered; its wings meet the air; and the physics object takes a
/// tick under a gravity of 44.6 units a tick squared, keeping 0.97 of its speed and 0.9 of its spin.
/// </content>
public sealed partial class Igi2CarTask
{
    /// <summary>The gravity a car falls under, units a tick squared.</summary>
    public const float Gravity = 44.600887298583984f;

    /// <summary>What a car's speed keeps each tick.</summary>
    public const float AirResistance = 0.9700000286102295f;

    /// <summary>What a car's spin keeps each tick.</summary>
    public const float SpinResistance = 0.8999999761581421f;

    /// <summary>The coefficient a car's wings meet the air with.</summary>
    public const float WingCoefficient = 0.00032699998700991273f;

    /// <summary>How far above its contact point a wheel's suspension line starts, past its spring: a metre.</summary>
    public const double SuspensionReach = 4096.0;

    /// <summary>How far below its contact point a wheel's suspension line ends: half a metre.</summary>
    public const double SuspensionDrop = 2048.0;

    /// <summary>How far above its contact point a wheel touches the ground: 0.4 m.</summary>
    public const float ContactLift = 1638.4000244140625f;

    private readonly WorldLineTrace _suspension = new(CarTypeDefinition.MaxWheels);
    private readonly int[] _suspensionWheels = new int[CarTypeDefinition.MaxWheels];

    /// <summary>
    /// Sets it back where the level put it, at tick nought of its driving (<c>Car_UpdateAnimDataHandler</c>): a fresh physics object of
    /// its type there, its collision groups, its wheels' contact points lowered, still, unsteered and unbraked, at its original thrust,
    /// and not exploding.
    /// </summary>
    private void Reset()
    {
        Igi2Pose start = Built;
        _body = null;
        if (_physicsType is not null)
        {
            Igi2PhysicsObject body = new(_physicsType, _model, start.Position, Vector3f.One)
            {
                CollisionGroups = CarCollisionGroups,
                IsCollisionDetectionEnabled = CollisionDetection,
                IsForceZ = ForceZ,
            };
            float[] m = start.ToArray();
            Array.Copy(m, body.Orientation, 9);
            body.ResetLast();
            _body = body;
            AdjustWheelContactPoints();
        }

        RealDeltaGamma = 0f;
        IdealDeltaGamma = 0f;
        IsBreak = false;
        RealThrust = OriginalThrust;
        IdealThrust = OriginalThrust;
        _isExplode = false;
        _isExploded = 0;
        _lastPosition = start.Position;
        _lastOrientation = start.ToArray();
        foreach (Igi2CarWheel wheel in _wheels)
        {
            wheel.IsContact = false;
        }

        if (_body is not null && _solids.Count > 0 && Models is { } models)
        {
            Igi2CollisionWorld.Repose(_solids, models, Model, start, Magic);
            PlaceDoors(start);
        }
    }

    // Car_AdjustWheelCollPoints: each wheel's contact point lowered by its type's lowering.
    private void AdjustWheelContactPoints()
    {
        if (_body is not { } body || _type is not { } type)
        {
            return;
        }

        foreach (CarWheelTypeDefinition wheel in type.Wheels)
        {
            if (wheel.ContactPoint >= 0)
            {
                Igi2PhysicsContactPoint point = body.ContactPoints[wheel.ContactPoint];
                Vector3f p = point.LocalPosition;
                point.LocalPosition = new Vector3f(p.X, p.Y, ((Fpu)p.Z - type.WheelContactLowering).F);
            }
        }
    }

    /// <summary>Moves it a tick, <c>Car_Move</c>.</summary>
    private void Move()
    {
        if (_type is not { } type)
        {
            return;
        }

        VehicleTypeDefinition vehicle = type.Vehicle;
        RealThrust = ((((Fpu)1.0 - vehicle.ThrustInertia) * IdealThrust) + ((Fpu)vehicle.ThrustInertia * RealThrust)).F;
        RealDeltaGamma = ((((Fpu)1.0 - vehicle.DeltaGammaInertia) * IdealDeltaGamma) + ((Fpu)RealDeltaGamma * vehicle.DeltaGammaInertia)).F;
        if (IdealThrust == 0f && Math.Abs(RealThrust) < 0.009999999776482582)
        {
            RealThrust = 0f;
        }

        if (_isExploded == 0 && _body is { } body && _physicsWorld is { } world)
        {
            AddSpringForces(body, type, world);
            if (vehicle.Wings.Count > 0)
            {
                Vector3f v = body.Body.Velocity;
                ApplyAirResistance(body, vehicle.Wings, new Vector3f(-v.X, -v.Y, -v.Z));
            }

            body.Update(Gravity, AirResistance, SpinResistance, world);
            if (_solids.Count > 0 && Models is { } models)
            {
                Igi2Pose moved = Igi2Pose.FromMatrix(body.Position, Rows(body.Orientation));
                Igi2CollisionWorld.Repose(_solids, models, Model, moved, Magic);
                PlaceDoors(moved);
            }
        }

        float[] channels = (float[])SoundChannels;
        channels[0] = RealThrust;
        channels[1] = RealThrust > 0.009999999776482582 ? 1f : 0f;
        float speed = ((Fpu)(_body?.Body.Speed ?? 0f) * 0.0010000000474974513).F;
        channels[2] = speed >= 1f ? 1f : speed;
    }

    /// <summary>
    /// The springs, <c>Car_AddSpringForces</c>: each wheel that is there and has a contact point traces a line down through it, from a
    /// metre above where its spring would be fully compressed to half a metre below the point; its contact point stops meeting the
    /// world for itself. Ground struck within the spring pushes up at the point by how far it is compressed over its reach, times its
    /// stiffness, the car's mass and the gravity; ground above the spring's reach pushes harder the further above, and lifts the car
    /// out by the most any wheel is in. Either way the wheel touches, where its contact point is lifted 0.4 m, and drives and rubs.
    /// A wheel that is not there leaves its point to meet the world.
    /// </summary>
    private void AddSpringForces(Igi2PhysicsObject body, CarTypeDefinition type, Igi2PhysicsWorld world)
    {
        float[] m = body.Orientation;
        Vector3d origin = body.Position;
        int lines = 0;
        bool isHard = false;
        double maxHard = -double.MaxValue;
        for (int w = 0; w < type.Wheels.Count; w++)
        {
            CarWheelTypeDefinition wheel = type.Wheels[w];
            int cp = wheel.ContactPoint;
            if (cp < 0)
            {
                continue;
            }

            Igi2PhysicsContactPoint point = body.ContactPoints[cp];
            if (!_wheels[w].IsAttached)
            {
                point.IsActive = true;
                continue;
            }

            Vector3f p = point.LocalPosition;
            Fpu x = (((Fpu)m[2] * p.Z) + ((Fpu)m[1] * p.Y)) + ((Fpu)m[0] * p.X);
            Fpu y = (((Fpu)m[5] * p.Z) + ((Fpu)m[4] * p.Y)) + ((Fpu)m[3] * p.X);
            Fpu z = (((Fpu)m[8] * p.Z) + ((Fpu)m[7] * p.Y)) + ((Fpu)m[6] * p.X);
            double sz = (z + origin.Z).Value;
            Vector3d start = new((x + origin.X).Value, (y + origin.Y).Value, (((Fpu)wheel.MaxSpringDistance + sz) + SuspensionReach).Value);
            Fpu reach = (Fpu)wheel.MaxSpringDistance + SuspensionDrop;
            _suspension.Starts[lines] = start;
            _suspension.Ends[lines] = new Vector3d(start.X, start.Y, (((Fpu)start.Z - reach) - SuspensionReach).Value);
            point.IsActive = false;
            _suspensionWheels[lines] = w;
            lines++;
        }

        _suspension.Count = lines;
        _suspension.Includes = world.Includes;
        if (lines > 0)
        {
            world.Query.TraceLines(_suspension, body.CollisionGroups);
        }

        float mass = _physicsType!.Mass;
        for (int i = 0; i < lines; i++)
        {
            int w = _suspensionWheels[i];
            CarWheelTypeDefinition wheel = type.Wheels[w];
            Igi2PhysicsContactPoint point = body.ContactPoints[wheel.ContactPoint];
            WorldIntersection hit = _suspension.Intersections[i];
            float compression = 0f;
            bool contact = false;
            if (hit.IsHit)
            {
                double d = hit.Distance;
                bool hard = false;
                if (d >= SuspensionReach)
                {
                    Fpu top = (Fpu)wheel.MaxSpringDistance + SuspensionReach;
                    if (d > SuspensionReach && top.Value > d)
                    {
                        contact = true;
                        compression = (top - d).F;
                    }
                }
                else
                {
                    hard = true;
                    contact = true;
                    isHard = true;
                    compression = wheel.MaxSpringDistance;
                    double into = ((Fpu)SuspensionReach - d).Value;
                    if (maxHard < into)
                    {
                        maxHard = into;
                    }
                }

                if (contact)
                {
                    Fpu push = ((((Fpu)compression / wheel.MaxSpringDistance) * wheel.SpringStiffness) * mass) * Gravity;
                    if (hard)
                    {
                        push = ((Fpu)2.0 - ((Fpu)d * 0.000244140625)) * push;
                    }

                    body.Body.AddForce(new PointForce(new Vector3f(0f, 0f, push.F), point.LocalPosition));
                }
            }

            float displacement = point.ZDisplacement;
            if (Math.Abs(((Fpu)displacement - compression).Value) > 0.0010000000474974513)
            {
                point.ZDisplacement = compression <= displacement
                    ? (((Fpu)displacement * 0.05000000074505806) + ((Fpu)compression * 0.949999988079071)).F
                    : compression;
            }

            Igi2CarWheel state = _wheels[w];
            state.IsContact = contact;
            point.IsContact = contact;
            if (contact)
            {
                state.ContactNormal = RigidBodyKernels.Normalise<Fpu>(hit.Normal);
                Vector3f at = point.LocalPosition;
                state.ContactPoint = new Vector3f(at.X, at.Y, ((Fpu)at.Z + ContactLift).F);
                body.NoteContact();
                AddWheelForces(body, type, w, wheel.ContactPoint);
            }
        }

        if (isHard)
        {
            Vector3d at = body.Position;
            body.Position = new Vector3d(at.X, at.Y, ((Fpu)at.Z + maxHard).Value);
        }
    }

    /// <summary>
    /// A wheel's push and rub, <c>Wheel_AddForces</c>: a driven wheel touching while the car has thrust (or any driven wheel of a car
    /// held level) pushes along the ground, in the car's forward direction laid on it, by the thrust its share of the engine gives less
    /// the car's speed, turned by the wheel's steering; a crawler's track pushes less on the inside of a turn. A touching wheel then
    /// rubs (<see cref="AddFriction"/>), across by its point's first friction and along by its second, or by its first both ways while
    /// the car brakes, which also takes a twentieth of its thrust.
    /// </summary>
    private void AddWheelForces(Igi2PhysicsObject body, CarTypeDefinition type, int w, int cp)
    {
        CarWheelTypeDefinition wheel = type.Wheels[w];
        Igi2CarWheel state = _wheels[w];
        float[] m = body.Orientation;
        float gamma = (((Fpu)wheel.MaxAngle * 0.01745329238474369) * RealDeltaGamma).F;
        if (wheel.IsEngine && ((state.IsContact && RealThrust != 0f) || body.IsForceZ))
        {
            Fpu thrust = (Fpu)type.Vehicle.MaxThrust * RealThrust;
            Fpu ideal = (thrust * Gravity) / _engineWheels;
            Vector3f n = state.ContactNormal;
            float ax = m[1], ay = m[4], az = m[7];
            Fpu cx = ((Fpu)az * n.Y) - ((Fpu)ay * n.Z);
            Fpu cy = -(((Fpu)az * n.X) - ((Fpu)ax * n.Z));
            Fpu cz = ((Fpu)ay * n.X) - ((Fpu)ax * n.Y);
            Fpu fx = (cy * n.Z) - (cz * n.Y);
            Fpu fy = -((cx * n.Z) - (cz * n.X));
            Fpu fz = (cx * n.Y) - (cy * n.X);
            if (type.IsCrawler)
            {
                ideal = (Fpu)CrawlerScale(body, cp) * ideal;
            }

            Fpu dv = ideal - body.Body.Speed;
            Fpu vx = fx * dv, vy = dv * fy, vz = dv * fz;
            Fpu lx = ((vx * m[0]) + (vz * m[6])) + (vy * m[3]);
            Fpu ly = ((vx * m[1]) + (vy * m[4])) + (vz * m[7]);
            Fpu lz = ((vx * m[2]) + (vz * m[8])) + (vy * m[5]);
            double s = Math.Sin(-gamma), c = Math.Cos(-gamma);
            Fpu zero = lz * 0.0;
            Vector3f force = new(
                (((lx * c) + (ly * -s)) + zero).F,
                (((ly * c) + (lx * s)) + zero).F,
                (((ly + lx) * 0.0) + lz).F);
            body.Body.AddForce(new PointForce(force, state.ContactPoint));
        }

        if (state.IsContact && cp >= 0)
        {
            Igi2PhysicsContactPoint point = body.ContactPoints[cp];
            float angle = wheel.IsSteering ? gamma : 0f;
            float across = point.FrictionX;
            float along = point.FrictionY;
            if (IsBreak)
            {
                along = point.FrictionX;
                RealThrust = ((Fpu)RealThrust * 0.949999988079071).F;
            }

            AddFriction(body, state.ContactPoint, state.ContactNormal, angle, across, along);
        }
    }

    /// <summary>
    /// A wheel's rub, <c>Wheel_AddFriction</c>: the point's velocity off the ground's plane, made a unit direction and taken into the car's
    /// frame, is turned into the wheel's (by its steering), scaled across and along by the two frictions and turned back. Slow enough to
    /// be held by the load on the wheel, the rub is that direction by the speed, the mass and a tenth; otherwise by the load and 0.24.
    /// </summary>
    private static void AddFriction(Igi2PhysicsObject body, Vector3f point, Vector3f n, float gamma, float across, float along)
    {
        float[] m = body.Orientation;
        float mass = body.Body.Mass;
        Fpu load = ((((Fpu)n.Y + n.X) * 0.0) + (((Fpu)mass * Gravity) * n.Z));
        Vector3f arm = new(
            ((((Fpu)m[0] * point.X) + ((Fpu)point.Y * m[1])) + ((Fpu)point.Z * m[2])).F,
            ((((Fpu)m[5] * point.Z) + ((Fpu)m[3] * point.X)) + ((Fpu)m[4] * point.Y)).F,
            ((((Fpu)point.Z * m[8]) + ((Fpu)point.X * m[6])) + ((Fpu)point.Y * m[7])).F);
        Vector3f pv = body.Body.PointVelocity(arm);
        Fpu vn = (((Fpu)pv.Z * n.Z) + ((Fpu)pv.Y * n.Y)) + ((Fpu)pv.X * n.X);
        Fpu dx = ((Fpu)n.X * vn) - pv.X, dy = ((Fpu)n.Y * vn) - pv.Y, dz = ((Fpu)n.Z * vn) - pv.Z;
        Fpu speed = Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz));
        if (dx.Value != 0.0 || dy.Value != 0.0 || dz.Value != 0.0)
        {
            Fpu inverse = (Fpu)1.0 / speed;
            (dx, dy, dz) = (dx * inverse, dy * inverse, dz * inverse);
        }

        Fpu lx = ((dy * m[3]) + (dx * m[0])) + (dz * m[6]);
        Fpu ly = ((dy * m[4]) + (dz * m[7])) + (dx * m[1]);
        Fpu lz = ((dy * m[5]) + (dx * m[2])) + (dz * m[8]);
        double s = Math.Sin(gamma), c = Math.Cos(gamma);
        Fpu rx = ((lx * c) + (ly * -s)) + (lz * 0.0);
        Fpu ry = ((ly * c) + (lx * s)) + (lz * 0.0);
        Fpu rz = ((ly + lx) * 0.0) + lz;
        Fpu fx = rx * across, fy = ry * along, zero = rz * 0.0;
        Fpu px = ((fy * s) + (fx * c)) + zero;
        Fpu py = ((fy * c) + (fx * -s)) + zero;
        Fpu pz = ((fy + fx) * 0.0) + rz;
        Vector3f force = ((load / mass) * 4.0).Value >= speed.Value
            ? new Vector3f((((px * speed) * mass) * 0.10000000149011612).F, (((speed * py) * mass) * 0.10000000149011612).F, (((speed * pz) * mass) * 0.10000000149011612).F)
            : new Vector3f(((px * load) * 0.23999999463558197).F, ((py * load) * 0.23999999463558197).F, ((pz * load) * 0.23999999463558197).F);
        body.Body.AddForce(new PointForce(force, point));
    }

    /// <summary>
    /// How hard a crawler's track pushes in a turn, <c>Wheel_GetCrawlerSpeedScale</c>: turning one way, the tracks of contact points of
    /// the first kind push less; turning the other, those of the nought kind; going straight, all fully.
    /// </summary>
    private float CrawlerScale(Igi2PhysicsObject body, int cp)
    {
        float g = RealDeltaGamma;
        int kind = cp >= 0 ? body.ContactPoints[cp].Type : -1;
        if (g <= 0.009999999776482582)
        {
            return g < -0.009999999776482582 && kind == 0 ? (((((Fpu)g + 0.5) + g) + 0.5)).F : 1f;
        }

        return kind != 1 ? 1f : (((((Fpu)0.5 - g) + 0.5) - g)).F;
    }

    /// <summary>
    /// The wings, <c>Wing_ApplyAirResistance</c>: the wind taken into the car's frame, its length squared by the coefficient; each wing
    /// with area is pushed along its normal by how squarely the wind meets it, its area, and that.
    /// </summary>
    private static void ApplyAirResistance(Igi2PhysicsObject body, IReadOnlyList<Wing> wings, Vector3f wind)
    {
        float[] m = body.Orientation;
        Vector3f local = new(
            ((((Fpu)m[6] * wind.Z) + ((Fpu)m[3] * wind.Y)) + ((Fpu)m[0] * wind.X)).F,
            ((((Fpu)m[7] * wind.Z) + ((Fpu)m[1] * wind.X)) + ((Fpu)m[4] * wind.Y)).F,
            ((((Fpu)m[8] * wind.Z) + ((Fpu)m[2] * wind.X)) + ((Fpu)m[5] * wind.Y)).F);
        Fpu length = Igi2RigidBody.FastLength(local);
        Fpu k = (length * length) * WingCoefficient;
        if (!(k.Value > 0.0))
        {
            return;
        }

        Vector3f d = RigidBodyKernels.Normalise<Fpu>(local);
        foreach (Wing wing in wings)
        {
            if (!(wing.Area > 0f))
            {
                continue;
            }

            Vector3f n = wing.Normal;
            Fpu f = (((((Fpu)d.Y * n.Y) + ((Fpu)d.Z * n.Z)) + ((Fpu)d.X * n.X)) * wing.Area) * k;
            body.Body.AddForce(new PointForce(new Vector3f(((Fpu)n.X * f).F, ((Fpu)n.Y * f).F, ((Fpu)n.Z * f).F), wing.Position));
        }
    }

    // CarDoor_UpdateMagicObjHandler, as the car moves its magic objects: each door's solid where its point puts it, turned by its swing.
    private void PlaceDoors(Igi2Pose car)
    {
        foreach (Igi2CarDoor door in _doors)
        {
            if (door.Solid is { } solid)
            {
                Igi2Pose pose = door.Pose(car);
                solid.Position = pose.Position;
                for (int i = 0; i < solid.Orientation.Length; i++)
                {
                    solid.Orientation[i] = pose.Orientation[i];
                }
            }
        }
    }
}
