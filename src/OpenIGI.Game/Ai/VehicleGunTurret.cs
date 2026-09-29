using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>One gun a vehicle model carries, turning toward what its vehicle sees and deciding to fire.</summary>
/// <remarks>
/// <para>
/// A port of <c>AIStationaryGun</c>'s run handler (<c>0x46F390</c>). Each tick the gun asks its vehicle
/// for the first target the vehicle currently sees (message <c>0x489630</c>), turns its elevation
/// (<c>+444</c>) and traverse (<c>+448</c>) toward it at the authored rates, and holds the trigger only
/// while both are within five degrees of the target and the target is beyond the authored minimum range.
/// A target that drops out of sight is still aimed at for 90 ticks (<c>+468</c>), after which the gun
/// swings back to its rest pose.
/// </para>
/// <para>
/// Angles are measured in the vehicle's frame, as <c>0x414950</c> measures them: traverse is the
/// heading from local +Y with +X negative, elevation is signed by local Z. The gun's own limits clamp
/// the pose after it turns (<c>0x46F9D0</c>); the aim it compares against for firing is the unclamped one,
/// so a target outside the arc is never fired on.
/// </para>
/// </remarks>
public sealed class VehicleGunTurret
{
    /// <summary>How long a lost target is still aimed at (<c>+468</c> = 90).</summary>
    public const int LostTargetHoldTicks = 90;

    /// <summary>How close the gun must be to its aim before it fires (5 degrees, <c>0.087266468</c>).</summary>
    public const float FireToleranceRadians = 0.087266468f;

    private const float Frequency = 30f;
    private const float DegreesToRadians = 0.017453292f;

    private readonly float _yawLimit;
    private readonly float _maximumPitch;
    private readonly float _minimumPitch;
    private readonly float _pitchRate;
    private readonly float _yawRate;
    private readonly float _spinRate;
    private readonly double _minimumRangeSquared;

    private bool _hadTarget;
    private int _holdTicks;
    private float _heldPitch;
    private float _heldYaw;

    /// <summary>Initializes a new instance of the <see cref="VehicleGunTurret"/> class.</summary>
    /// <param name="mount">The gun's authored limits.</param>
    public VehicleGunTurret(MagicGunMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        Mount = mount;

        // 0x46FA50 stores angles in radians and rates per 30 Hz tick.
        _yawLimit = mount.YawLimitDegrees * DegreesToRadians;
        _maximumPitch = mount.MaximumPitchDegrees * DegreesToRadians;
        _minimumPitch = mount.MinimumPitchDegrees * DegreesToRadians;
        _pitchRate = mount.PitchRateDegreesPerSecond * DegreesToRadians * (1f / Frequency);
        _yawRate = mount.YawRateDegreesPerSecond * DegreesToRadians * (1f / Frequency);
        _spinRate = mount.SpinDegreesPerSecond * DegreesToRadians * (1f / Frequency);
        double range = mount.MinimumRangeMetres * WorldScene.UnitsPerMetre;
        _minimumRangeSquared = range * range;
    }

    /// <summary>Gets the gun's authored limits.</summary>
    public MagicGunMount Mount { get; }

    /// <summary>Gets the barrel's elevation in the vehicle's frame, in radians (<c>+444</c>).</summary>
    public float Pitch { get; private set; }

    /// <summary>Gets the gun's traverse in the vehicle's frame, in radians (<c>+448</c>).</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets the barrel spin, in radians (<c>+484</c>).</summary>
    public float Spin { get; private set; }

    /// <summary>Gets whether the gun held its trigger on the last tick.</summary>
    public bool Firing { get; private set; }

    /// <summary>Gets whether the gun is still turning toward its limited aim, which keeps its sound playing.</summary>
    public bool Turning { get; private set; }

    /// <summary>Advances the gun one 30 Hz tick.</summary>
    /// <param name="targetInVehicleFrame">
    /// The target's offset from the gun in the vehicle's frame and world units, or <see langword="null"/>
    /// when the vehicle sees nothing.
    /// </param>
    /// <returns>Whether the gun holds its trigger this tick.</returns>
    public bool Tick(Vector3d? targetInVehicleFrame)
    {
        float pitch = 0f;
        float yaw = 0f;
        double distanceSquared = 0d;
        bool hasTarget = targetInVehicleFrame.HasValue;
        if (targetInVehicleFrame is { } offset)
        {
            yaw = Heading(offset);
            pitch = Elevation(offset);
            distanceSquared = (offset.X * offset.X) + (offset.Y * offset.Y) + (offset.Z * offset.Z);
        }

        // A target lost this tick starts the hold; a visible one cancels it.
        if (hasTarget)
        {
            _holdTicks = 0;
        }
        else if (_hadTarget)
        {
            _holdTicks = LostTargetHoldTicks;
        }

        _hadTarget = hasTarget;
        if (_holdTicks != 0)
        {
            pitch = _heldPitch;
            yaw = _heldYaw;
            _holdTicks--;
        }
        else
        {
            _heldPitch = pitch;
            _heldYaw = yaw;
        }

        // A gun that turns freely (more than a full turn of arc) goes the short way round.
        if (_yawLimit > 6.2831855f && MathF.Abs(yaw - Yaw) > 3.1415927f)
        {
            yaw += 6.2831855f;
        }

        Pitch = Approach(Pitch, pitch, _pitchRate);
        Yaw = Approach(Yaw, yaw, _yawRate);
        ClampPose();

        bool fire = false;
        if (hasTarget
            && MathF.Abs(Pitch - pitch) < FireToleranceRadians
            && MathF.Abs(Yaw - yaw) < FireToleranceRadians
            && distanceSquared > _minimumRangeSquared)
        {
            fire = true;
            Spin += _spinRate;
        }

        Firing = fire;

        // The turning sound runs until the gun is within two degrees of the aim its limits allow.
        float limitedPitch = Math.Clamp(pitch, _minimumPitch, _maximumPitch);
        float limitedYaw = yaw;
        if (limitedYaw < -_yawLimit)
        {
            limitedYaw = -_yawLimit;
        }
        else if (_yawLimit < limitedYaw)
        {
            limitedYaw = _yawLimit;
        }

        Turning = MathF.Abs(Pitch - limitedPitch) > 0.034906585f || MathF.Abs(Yaw - limitedYaw) > 0.034906585f;
        return fire;
    }

    /// <summary>The heading of a vehicle-frame offset from +Y, negative toward +X (<c>0x414950</c>).</summary>
    public static float Heading(Vector3d offset)
    {
        double length = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y));
        if (length <= 0d)
        {
            return 0f;
        }

        float angle = (float)Math.Acos(Math.Clamp(offset.Y / length, -1d, 1d));
        return offset.X > 0d ? -angle : angle;
    }

    /// <summary>The elevation of a vehicle-frame offset, signed by its Z.</summary>
    public static float Elevation(Vector3d offset)
    {
        double full = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y) + (offset.Z * offset.Z));
        double horizontal = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y));
        if (full <= 0d || horizontal <= 0d)
        {
            return 0f;
        }

        float angle = (float)Math.Acos(Math.Min(1d, horizontal / full));
        return offset.Z < 0d ? -angle : angle;
    }

    private static float Approach(float current, float target, float rate)
    {
        if (current < target)
        {
            return MathF.Min(current + rate, target);
        }

        return current > target ? MathF.Max(current - rate, target) : current;
    }

    private void ClampPose()
    {
        // 0x46F9D0: elevation always, traverse only for a gun with less than a full turn of arc.
        Pitch = Math.Clamp(Pitch, _minimumPitch, _maximumPitch);
        if (_yawLimit >= 6.283185307179589f)
        {
            return;
        }

        Yaw = Math.Clamp(Yaw, -_yawLimit, _yawLimit);
    }
}
