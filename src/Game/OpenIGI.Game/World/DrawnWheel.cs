using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

/// <summary>
/// How a vehicle's drawn wheel rolls and steers, the same in both games: Project I.G.I.'s <c>Wheel_Run</c> (<c>0x4301C0</c>) and
/// <c>Wheel_Move</c> (<c>0x430640</c>), I.G.I. 2's <c>Wheel_RunHandler</c> and <c>Wheel_UpdateAttachmentHandler</c>.
/// </summary>
/// <remarks>
/// A wheel takes the speed it is carried at, ignoring anything under 40.96 units a tick, and folds speed over its circumference into a
/// smoothed spin rate at 0.3 of the new to 0.7 of the old. The roll counts down by that rate, so it decreases as the car goes forward.
/// The drawn wheel is turned about its axle by the roll and about the car's up axis by its steer.
/// </remarks>
public static class DrawnWheel
{
    /// <summary>The speed a drawn wheel ignores before it starts rolling, in world units a tick.</summary>
    public const float SpeedDeadzoneUnits = 40.96f;

    /// <summary>How much of the new spin rate a drawn wheel takes each tick.</summary>
    public const float SpinBlend = 0.3f;

    /// <summary>A wheel's radius from its own physics type, in world units, or nought when the type says none.</summary>
    /// <param name="type">The wheel model's physics type.</param>
    /// <returns>The radius.</returns>
    /// <remarks>
    /// Both games read a wheel type's own arguments the same way (Project I.G.I.'s <c>Wheel_ParseDef</c>, <c>0x42FE80</c>; I.G.I. 2's
    /// <c>WheelType_ParsePhysicsObjTypeHandler</c>): a name, the radius in metres, then how far it may be lifted.
    /// </remarks>
    public static float RadiusOf(PhysicsObjectType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        int next = 1;
        return VehicleTypeDefinition.Single(type.Parameters, ref next) * 4096f;
    }

    /// <summary>The next tick's spin rate.</summary>
    /// <param name="speedUnits">How fast the wheel is carried, world units a tick.</param>
    /// <param name="radiusUnits">The wheel's radius, world units.</param>
    /// <param name="rate">The spin rate it had, radians a tick.</param>
    /// <returns>The spin rate, radians a tick.</returns>
    public static double NextSpinRate(double speedUnits, double radiusUnits, double rate)
    {
        double speed = speedUnits < SpeedDeadzoneUnits ? 0d : speedUnits;
        return (radiusUnits > 0d ? speed / (radiusUnits * (2d * Math.PI)) * (2d * Math.PI) * SpinBlend : 0d)
            + (rate * (1d - SpinBlend));
    }

    /// <summary>The wheel's own turn, in its placement frame: the roll about the axle, then the steer about the up axis.</summary>
    /// <param name="roll">The accumulated roll, radians.</param>
    /// <param name="steer">The steer, radians; nought for a wheel that does not steer.</param>
    /// <returns>The rotation.</returns>
    /// <remarks>
    /// The roll is applied negated: the wheel's own axle points the opposite way from its placement frame's X, so rolling by the raw
    /// accumulator turns the tyres backwards while the car drives forwards.
    /// </remarks>
    public static Matrix3f Rotation(double roll, double steer)
    {
        Matrix3f rotation = Matrix3f.RotationX((float)-roll);
        return steer != 0d ? Matrix3f.RotationZ((float)steer) * rotation : rotation;
    }
}
