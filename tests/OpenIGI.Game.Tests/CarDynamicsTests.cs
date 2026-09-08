using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The retail CAR force model ported from 0x42ECA0, 0x430D10, 0x430960 and 0x4301C0.</summary>
public sealed class CarDynamicsTests
{
    private static readonly Vector3d Forward = new(0d, 1d, 0d);
    private static readonly Vector3d Lateral = new(1d, 0d, 0d);

    /// <summary>
    /// The shared body step is handed 44.600887 as its gravity. At 4096 units to the metre and 30
    /// ticks to the second that is 9.8 m/s², which is what fixes the whole model's scale.
    /// </summary>
    [Fact]
    public void GravityConstantIsNinePointEightMetresPerSecondSquared()
    {
        double metresPerSecondSquared =
            CarDynamics.GravityUnitsPerTickSquared / WorldScene.UnitsPerMetre * 30d * 30d;
        Assert.Equal(9.8d, metresPerSecondSquared, precision: 2);
    }

    [Fact]
    public void ThrottleAcceleratesTheBodyAlongItsForwardAxis()
    {
        CarPhysicsDefinition truck = Truck();
        CarMotionState motion = new();
        CarWheel[] wheels = Wheels(truck);

        Vector3d travelled = default;
        for (int tick = 0; tick < 400; tick++)
        {
            (Vector3d step, _) = CarDynamics.Step(
                motion, truck, wheels, default, Forward, Lateral, steering: 0f, thrust: 0.25f, handbrake: false);
            travelled += step;
        }

        Assert.True(travelled.Y > 0d, "a positive throttle must drive the body forward");

        // The truck's axles are not centred on its origin (+0.99 against -0.70 across), so a
        // straight run drifts a little; it must stay far below the forward distance.
        Assert.True(
            Math.Abs(travelled.X) < Math.Abs(travelled.Y) * 0.01d,
            $"a straight run drifted {travelled.X:F1} across against {travelled.Y:F1} forward");

        // The force balance settles well inside a truck's plausible range rather than the 2.2 m/s
        // the old fixed Thrust * 1200 target produced.
        double metresPerSecond = motion.Velocity.Length / WorldScene.UnitsPerMetre * 30d;
        Assert.InRange(metresPerSecond, 2d, 12d);
    }

    /// <summary>
    /// A positive steering input turns the body's forward axis TOWARD its across axis, and a
    /// negative one away. Probing the retail game settles this: at the point its Mission 1 truck
    /// carries a smoothed steering of +0.906 its drive force reads (0.655, 0.755) in body axes,
    /// which is (sin, cos) of the wheel's own 40.7-degree angle about the across axis.
    /// </summary>
    [Fact]
    public void PositiveSteeringTurnsTheBodyTowardItsAcrossAxis()
    {
        double positive = TurnTowardAcross(steering: 1f);
        double negative = TurnTowardAcross(steering: -1f);

        Assert.True(positive > 0.05d, $"a positive input must turn toward the across axis: {positive}");
        Assert.True(negative < -0.05d, $"a negative input must turn away from it: {negative}");

        // Nowhere near a mirror: the truck's axles sit off-centre across the body -- retail holds
        // them at -0.79 and +0.85 metres -- and the tyre model is not linear, so one lock turns
        // appreciably harder than the other. They must still be the same order of turn.
        Assert.InRange(Math.Abs(negative) / Math.Abs(positive), 0.6d, 1.6d);

        // How far the forward axis leans onto the across axis. The drive force is what turns the
        // body, so the car is brought up to speed on a straight first -- which is also the order
        // the authored channels use, holding the steering at nought for the first six seconds.
        static double TurnTowardAcross(float steering)
        {
            CarPhysicsDefinition truck = Truck();
            CarMotionState motion = new();
            CarWheel[] wheels = Wheels(truck);
            Vector3d forward = Forward;
            Vector3d lateral = Lateral;
            for (int tick = 0; tick < 350; tick++)
            {
                (_, double delta) = CarDynamics.Step(
                    motion, truck, wheels, default, forward, lateral,
                    tick < 180 ? 0f : steering, thrust: 0.25f, handbrake: false);
                forward = Rotate(forward, delta);
                lateral = Rotate(lateral, delta);
            }

            return (forward.X * Lateral.X) + (forward.Y * Lateral.Y);
        }
    }

    private static Vector3d Rotate(Vector3d value, double radians)
    {
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Vector3d((value.X * cos) - (value.Y * sin), (value.X * sin) + (value.Y * cos), 0d);
    }

    /// <summary>A wheel with a negative body index has no collision body and takes no part.</summary>
    [Fact]
    public void WheelsWithoutABodyAreSkipped()
    {
        CarPhysicsDefinition truck = Truck();
        Assert.False(truck.Wheels[2].HasBody);
        Assert.False(truck.Wheels[4].HasBody);
        Assert.Equal(2, truck.DrivenWheelCount);
    }

    /// <summary>
    /// Wheel::Tick (0x4301C0) rolls a drawn wheel at speed over radius, smoothed 0.3 to 0.7, and
    /// zeroes the spin under the handbrake.
    /// </summary>
    [Fact]
    public void DrawnWheelsRollWithTheBodyAndStopUnderTheHandbrake()
    {
        CarPhysicsDefinition truck = Truck();
        CarMotionState motion = new();
        CarWheel[] wheels = Wheels(truck);
        for (int tick = 0; tick < 200; tick++)
        {
            CarDynamics.Step(motion, truck, wheels, default, Forward, Lateral, 0f, 0.25f, handbrake: false);
        }

        Assert.True(Math.Abs(motion.WheelRoll) > 0d, "the drawn wheels never rolled");
        Assert.True(motion.WheelSpinRate != 0d);

        CarDynamics.Step(motion, truck, wheels, default, Forward, Lateral, 0f, 0.25f, handbrake: true);
        Assert.Equal(0d, motion.WheelSpinRate);
    }

    /// <summary>The steered wheels draw at the negative of the record's limit times the input.</summary>
    [Fact]
    public void SteeredWheelsDrawAtTheRecordAngle()
    {
        CarPhysicsDefinition truck = Truck();
        CarMotionState motion = new();
        CarWheel[] wheels = Wheels(truck);
        CarDynamics.Step(motion, truck, wheels, default, Forward, Lateral, steering: 1f, thrust: 0f, handbrake: false);
        Assert.Equal(-45f * (MathF.PI / 180f), motion.WheelSteer, precision: 5);
    }

    /// <summary>
    /// The body rides its springs: dropped onto flat ground it settles, and settles where the four
    /// springs carry its weight. At a stiffness of 0.5 that is a quarter of the travel into each
    /// spring, and the compression is measured DOWN from the surface, so each wheel's origin comes
    /// to rest that far under it.
    /// </summary>
    [Fact]
    public void TheBodySettlesOntoItsSprings()
    {
        CarPhysicsDefinition truck = Truck();
        CarWheel[] wheels = Wheels(truck);
        CarMotionState motion = new();

        const double Ground = 42_000d;
        double travel = truck.Wheels[0].SuspensionLengthUnits;
        double axle = wheels[0].OffsetUnits.Z;

        // Start a metre above where it will settle and let it fall.
        Vector3d position = new(0d, 0d, Ground - axle + WorldScene.UnitsPerMetre);
        double highest = double.MinValue;
        double lowest = double.MaxValue;
        for (int tick = 0; tick < 400; tick++)
        {
            (Vector3d step, _) = CarDynamics.Step(
                motion, truck, wheels, position, Forward, Lateral,
                steering: 0f, thrust: 0f, handbrake: false, groundHeightAt: _ => Ground);
            position += step;
            if (tick > 200)
            {
                highest = Math.Max(highest, position.Z);
                lowest = Math.Min(lowest, position.Z);
            }
        }

        // Where the springs balance the weight: each wheel's origin rides a compression of
        // travel / (4 * stiffness) UNDER the surface.
        double compression = travel / (4d * truck.Wheels[0].SpringStiffness);
        double expected = Ground - compression - axle;
        Assert.InRange(position.Z, expected - 200d, expected + 200d);

        // And it is at rest there rather than still bouncing.
        Assert.InRange(highest - lowest, 0d, 120d);
    }

    /// <summary>Without a ground probe the body keeps the height it was handed.</summary>
    [Fact]
    public void NoGroundProbeLeavesTheHeightAlone()
    {
        CarPhysicsDefinition truck = Truck();
        CarWheel[] wheels = Wheels(truck);
        CarMotionState motion = new();
        double rise = 0d;
        for (int tick = 0; tick < 60; tick++)
        {
            (Vector3d step, _) = CarDynamics.Step(
                motion, truck, wheels, default, Forward, Lateral, 0f, 0.25f, handbrake: false);
            rise += step.Z;
        }

        Assert.Equal(0d, rise);
    }

    /// <summary>The Mission 1 truck's record, as 0x42DB00 reads it.</summary>
    private static CarPhysicsDefinition Truck()
    {
        float suspension = 0.3f * (float)WorldScene.UnitsPerMetre;
        return new CarPhysicsDefinition(
            3_000f,
            new Vector3f(2.2f, 7.8f, 3f),
            4,
            [
                new CarBodyDefinition(0, 0, 5, 0f, 0.95f, 0.05f),
                new CarBodyDefinition(1, 0, 5, 0f, 0.95f, 0.05f),
                new CarBodyDefinition(2, 0, 5, 0f, 0.95f, 0.05f),
                new CarBodyDefinition(3, 0, 5, 0f, 0.95f, 0.05f),
            ],
            0.95f,
            5_000f,
            0.95f,
            0,
            [
                new CarWheelDefinition(true, true, 45f, 1, suspension, 0.5f),
                new CarWheelDefinition(false, false, 0f, 0, suspension, 0.5f),
                new CarWheelDefinition(false, false, 0f, -10, suspension, 0.5f),
                new CarWheelDefinition(true, true, 45f, 2, suspension, 0.5f),
                new CarWheelDefinition(false, false, 0f, -13, suspension, 0.5f),
                new CarWheelDefinition(false, false, 0f, 3, suspension, 0.5f),
            ],
            []);
    }

    /// <summary>
    /// The four wheel bodies the forces act at, read out of the running retail game: the tandem rear
    /// pair shares one body a side, so six drawn wheels resolve onto four corners.
    /// </summary>
    private static CarWheel[] Wheels(in CarPhysicsDefinition truck)
    {
        Vector3d[] bodies =
        [
            new(-3253.813, 11523.6445, -787.932),   // wheel 0 -> body 1, front
            new(-3253.809, -9381.4775, -787.931),   // wheel 1 -> body 0, rear
            new(-3253.809, -9381.4775, -787.931),   // wheel 2 -> body 0 by the -10 alias
            new(3492.065, 11523.6445, -787.932),    // wheel 3 -> body 2, front
            new(3492.069, -9381.4775, -787.931),    // wheel 4 -> body 3 by the -13 alias
            new(3492.069, -9381.4775, -787.931),    // wheel 5 -> body 3, rear
        ];
        CarWheel[] wheels = new CarWheel[6];
        for (int i = 0; i < wheels.Length; i++)
        {
            wheels[i] = new CarWheel(truck.Wheels[i], bodies[i], 0.95f, 0.05f);
        }

        return wheels;
    }
}
