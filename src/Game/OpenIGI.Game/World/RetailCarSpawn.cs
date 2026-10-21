using OpenIGI.Game.Physics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>A car's placement in the level: the Car task's declared parameters the physics reads.</summary>
/// <param name="Position">"Position", world units, as the level's floats widen.</param>
/// <param name="Orientation">"Orientation": α, β, γ in radians.</param>
/// <param name="OriginalThrust">"Original Thrust": the initial throttle.</param>
/// <param name="CollisionDetection">"Collision detection": runs the generic collision pass.</param>
/// <param name="ForceZ">"Force Z": planar mode.</param>
/// <param name="WheelAttachments">How many <c>TASKTYPE_WHEEL</c> attachments the car's model spawns.</param>
public readonly record struct RetailCarPlacement(
    Vector3d Position,
    Vector3f Orientation,
    float OriginalThrust,
    bool CollisionDetection,
    bool ForceZ,
    int WheelAttachments);

/// <summary>Builds a <see cref="RetailCarBody"/> the way the original creates and resets a car.</summary>
/// <remarks>
/// The definition comes from <c>DefinePhysicsObjType</c>'s arguments
/// (<c>0x4EDD20</c>, <c>0x5031F0</c>, <c>0x42DB00</c>), the body from <c>Body_InitFromType</c> (<c>0x4FB700</c>)
/// with its box inertia (<c>0x4ECED0</c>, <c>0x4EC5E0</c>), the points from the model's magic vertices lowered
/// by the wheel drop (<c>0x42EC30</c>), and the wheels enabled by the model's wheel attachments (<c>0x430000</c>).
/// The state is the reset the controller's set-up packet performs (<c>0x42EA10</c> mode 0), which commits the
/// spawn pose before copying it into the previous-tick fields -- what a captured first tick holds.
/// </remarks>
public static class RetailCarSpawn
{
    /// <summary>Reads the fixed definition from the raw <c>DefinePhysicsObjType</c> arguments.</summary>
    /// <param name="physics">The parsed CAR type, whose <see cref="CarPhysicsDefinition.Arguments"/> are read.</param>
    /// <param name="wheelDrop">Receives <c>R+604</c>: how far each wheel lowers its body point, world units.</param>
    /// <param name="wheelsFromAttachments">Receives <c>R+608</c>.</param>
    /// <returns>The definition.</returns>
    public static RetailCarDefinition Definition(in CarPhysicsDefinition physics, out float wheelDrop, out bool wheelsFromAttachments)
    {
        IReadOnlyList<Scripting.Runtime.QvmValue> a = physics.Arguments;
        float F(int i) => i < a.Count ? a[i].ToSingle() : 0f;
        int I(int i) => i < a.Count ? a[i].ToInt32() : 0;

        int n = I(9);
        // 0x42DB00 from the type's own arguments: the vehicle's handling and wings (0x4EC4D0), then the wheels.
        int next = 12 + (6 * n);
        CarTypeDefinition car = CarTypeDefinition.Parse<X87>(a, ref next);
        wheelDrop = car.WheelContactLowering;
        wheelsFromAttachments = car.IsWheelsAttachObject;
        return new RetailCarDefinition
        {
            Mass = F(2),
            SleepSpeed = F(10 + (6 * n)),
            SleepSpin = F(11 + (6 * n)),
            SteeringRetention = car.Vehicle.DeltaGammaInertia,
            EngineForce = car.Vehicle.MaxThrust,
            ThrottleRetention = car.Vehicle.ThrustInertia,
            Aero = [.. car.Vehicle.Wings],
            Wheels = [.. car.Wheels],
            SkidSteer = car.IsCrawler,
        };
    }

    /// <summary>Creates a car at its placement, as activation and the set-up reset leave it.</summary>
    /// <param name="physics">The parsed CAR type.</param>
    /// <param name="model">The car's model, for its magic vertices and its radius.</param>
    /// <param name="placement">The car's level parameters.</param>
    /// <returns>The body, ready for its first tick.</returns>
    public static RetailCarBody Create(in CarPhysicsDefinition physics, MefModel model, in RetailCarPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(model);
        RetailCarDefinition definition = Definition(physics, out float wheelDrop, out bool fromAttachments);
        IReadOnlyList<Scripting.Runtime.QvmValue> a = physics.Arguments;
        float F(int i) => i < a.Count ? a[i].ToSingle() : 0f;
        int I(int i) => i < a.Count ? a[i].ToInt32() : 0;

        RetailCarBody car = new(definition);
        car.Dimensions[0] = car.Dimensions[1] = car.Dimensions[2] = 1.0f;
        car.ModelRadius = model.NativeMarkerBound;

        // 0x4FB700: the box, from the TYPE's edge lengths in metres (z first, then y, then x).
        float c = ((X87)F(5) * 4096.0f).F;
        float b = ((X87)F(4) * 4096.0f).F;
        float aa = ((X87)F(3) * 4096.0f).F;
        car.SetBox(definition.Mass, aa, b, c);
        for (int k = 0; k < 3; k++)
        {
            car.InverseInertia[k] = ((X87)car.InverseInertia[k] * 1.00999999f).F;
        }

        int n = Math.Clamp(I(9), 0, RetailCarBody.PointCount);
        car.CollideEnabled = I(9);
        for (int p = 0; p < n; p++)
        {
            int e = 10 + (6 * p);
            RetailCarPoint point = car.Points[p];
            point.Side = I(e + 1);
            point.Restitution = F(e + 3);
            point.Collides = true;
            point.IsCollisionPoint = true;
            point.Contact = false;
            point.FrictionX = F(e + 4);
            point.FrictionY = F(e + 5);
            point.Compression = 0f;
            if (model.TryGetMagicVertex(I(e), out Vector3f v))
            {
                point.Position[0] = ((X87)car.Dimensions[0] * v.X).F;
                point.Position[1] = ((X87)car.Dimensions[1] * v.Y).F;
                point.Position[2] = ((X87)car.Dimensions[2] * v.Z).F;
            }
        }

        // 0x42EC30: each wheel naming a body lowers its point once.
        foreach (CarWheelTypeDefinition wheel in definition.Wheels)
        {
            if (wheel.ContactPoint >= 0 && wheel.ContactPoint < RetailCarBody.PointCount)
            {
                float[] position = car.Points[wheel.ContactPoint].Position;
                position[2] = ((X87)position[2] - wheelDrop).F;
            }
        }

        // 0x42F7C0 and the Wheel tasks (0x430000).
        int driven = 0;
        for (int i = 0; i < definition.Wheels.Length; i++)
        {
            if (definition.Wheels[i].IsEngine)
            {
                driven++;
            }

            car.Wheels[i].Enabled = !fromAttachments || i < placement.WheelAttachments;
        }

        car.DrivenWheels = driven;
        car.RunCollide = placement.CollisionDetection;
        car.Planar = placement.ForceZ;
        Reset(car, placement);
        return car;
    }

    /// <summary>The set-up reset (<c>0x42EA10</c> mode 0): the spawn pose committed first, then the body cleared.</summary>
    /// <param name="car">The car.</param>
    /// <param name="placement">Its level parameters.</param>
    public static void Reset(RetailCarBody car, in RetailCarPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(car);
        car.Position[0] = placement.Position.X;
        car.Position[1] = placement.Position.Y;
        car.Position[2] = placement.Position.Z;
        float mm = Math.Max(car.Dimensions[0], Math.Max(car.Dimensions[1], car.Dimensions[2]));
        car.Radius = ((X87)car.ModelRadius * mm).F;
        SpawnMatrix(placement.Orientation, car.M);
        car.MatrixCounter = 0;

        car.Speed = 0f;
        Array.Clear(car.Velocity);
        Array.Clear(car.Direction);
        Array.Clear(car.Momentum);
        Array.Clear(car.LastForce);
        Array.Clear(car.AngularVelocity);
        car.Forces.Clear();
        car.IntegratedForceCount = 0;
        car.ContactCount = 0;
        car.Wake = false;

        Array.Copy(car.Position, car.PreviousPosition, 3);
        Array.Copy(car.M, car.PreviousM, 9);
        car.PreviousMatrixCounter = car.MatrixCounter;
        Array.Clear(car.Displacement);
        Array.Clear(car.DeltaM);
        car.DeltaM[0] = car.DeltaM[4] = car.DeltaM[8] = 1.0f;
        car.DeltaMatrixCounter = 0;

        car.Throttle = placement.OriginalThrust;
        car.ThrottleTarget = placement.OriginalThrust;
        car.Steering = 0f;
        car.SteeringTarget = 0f;
        car.Handbrake = false;

        // 0x4FB700 rebuilds the points; only their per-tick state changes from creation.
        for (int p = 0; p < Math.Min(car.CollideEnabled, RetailCarBody.PointCount); p++)
        {
            RetailCarPoint point = car.Points[p];
            point.Collides = true;
            point.IsCollisionPoint = true;
            point.Contact = false;
            point.Compression = 0f;
        }
    }

    /// <summary>Reads a Car task's physics parameters.</summary>
    /// <param name="task">The Car task.</param>
    /// <param name="wheelAttachments">How many <c>TASKTYPE_WHEEL</c> attachments its model spawns.</param>
    /// <returns>The placement.</returns>
    public static RetailCarPlacement Placement(Engine.Tasks.DeclaredTask task, int wheelAttachments)
    {
        ArgumentNullException.ThrowIfNull(task);
        object? Named(string compact)
        {
            string wanted = compact.Replace(" ", string.Empty, StringComparison.Ordinal);
            for (int i = 0; i < task.Type.Parameters.Count; i++)
            {
                if (task.Type.Parameters[i].Name.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return task.GetValue(i);
                }
            }

            return null;
        }

        return new RetailCarPlacement(
            Named("Position") is Vector3d position ? position : default,
            Named("Orientation") is Vector3f orientation ? orientation : default,
            Named("Original thrust") is float thrust ? thrust : 0f,
            Named("Collision detection") is not bool collision || collision,
            Named("Force Z") is bool forceZ && forceZ,
            wheelAttachments);
    }

    /// <summary>The spawn orientation, <c>0x4B38E0(α, β, γ)</c>: <c>RotZ(γ)·RotY(β)·RotX(α)</c> with truncating products.</summary>
    private static void SpawnMatrix(Vector3f angles, float[] m) =>
        RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z).CopyTo(m, 0);
}
