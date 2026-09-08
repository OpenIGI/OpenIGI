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
                motion, truck, wheels, Forward, Lateral, steering: 0f, thrust: 0.25f, handbrake: false);
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
    /// The drive direction is rotated by the NEGATIVE steer angle (0x430D10 passes -angle to
    /// sub_4B3BE0), which is what decides the side the nose goes.
    /// </summary>
    [Fact]
    public void SteeringYawsTheBodyAndReversesWithTheInput()
    {
        double positive = TotalYaw(steering: 1f);
        double negative = TotalYaw(steering: -1f);

        // The drive direction is rotated by -steer, so a positive input yaws the body the
        // counter-clockwise way about the up axis. Feeding the Mission 1 truck's authored steering
        // through the other sign sent it away from its gate.
        Assert.True(Math.Abs(positive) > 1e-3d, "a steered, driven wheel must yaw the body");
        Assert.True(
            positive > 0d && negative < 0d,
            $"steering must reverse the yaw: positive={positive}, negative={negative}");

        // Not an exact mirror: the axles sit off-centre across the body, so the two locks differ
        // slightly. They must still be the same turn to within a few per cent.
        Assert.InRange(Math.Abs(negative) / Math.Abs(positive), 0.9d, 1.1d);

        static double TotalYaw(float steering)
        {
            CarPhysicsDefinition truck = Truck();
            CarMotionState motion = new();
            CarWheel[] wheels = Wheels(truck);
            double yaw = 0d;
            for (int tick = 0; tick < 200; tick++)
            {
                (_, double delta) = CarDynamics.Step(
                    motion, truck, wheels, Forward, Lateral, steering, thrust: 0.25f, handbrake: false);
                yaw += delta;
            }

            return yaw;
        }
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
            CarDynamics.Step(motion, truck, wheels, Forward, Lateral, 0f, 0.25f, handbrake: false);
        }

        Assert.True(Math.Abs(motion.WheelRoll) > 0d, "the drawn wheels never rolled");
        Assert.True(motion.WheelSpinRate != 0d);

        CarDynamics.Step(motion, truck, wheels, Forward, Lateral, 0f, 0.25f, handbrake: true);
        Assert.Equal(0d, motion.WheelSpinRate);
    }

    /// <summary>The steered wheels draw at the negative of the record's limit times the input.</summary>
    [Fact]
    public void SteeredWheelsDrawAtTheRecordAngle()
    {
        CarPhysicsDefinition truck = Truck();
        CarMotionState motion = new();
        CarWheel[] wheels = Wheels(truck);
        CarDynamics.Step(motion, truck, wheels, Forward, Lateral, steering: 1f, thrust: 0f, handbrake: false);
        Assert.Equal(-45f * (MathF.PI / 180f), motion.WheelSteer, precision: 5);
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

    /// <summary>The six drawn wheels at the offsets the Mission 1 truck's placements carry.</summary>
    private static CarWheel[] Wheels(in CarPhysicsDefinition truck)
    {
        double[] across = [0.99d, 0.99d, 0.99d, -0.70d, -0.70d, -0.70d];
        double[] along = [2.83d, -1.21d, -2.30d, 2.83d, -1.22d, -2.30d];
        CarWheel[] wheels = new CarWheel[6];
        for (int i = 0; i < wheels.Length; i++)
        {
            wheels[i] = new CarWheel(
                truck.Wheels[i],
                new Vector3d(
                    across[i] * WorldScene.UnitsPerMetre,
                    along[i] * WorldScene.UnitsPerMetre,
                    0d),
                0.95f,
                0.05f);
        }

        return wheels;
    }
}
