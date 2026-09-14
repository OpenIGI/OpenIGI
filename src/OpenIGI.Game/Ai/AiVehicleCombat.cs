using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>The authored and live state a firing vehicle presents to its combat controller.</summary>
public readonly record struct AiVehicleCombatInput(
    Vector3d VehiclePosition,
    Matrix3f VehicleOrientation,
    Vector3d TargetPosition,
    bool IsVehicleActive,
    bool CanFire,
    float ViewAlphaDegrees,
    float ViewGammaDegrees,
    float ViewLengthMetres,
    bool HasLineOfSight,
    float MuzzleVelocityMetresPerSecond,
    int RoundsPerMinute);

/// <summary>A retail vehicle-gun request ready to enter the missile task.</summary>
public readonly record struct VehicleCombatShot(
    Vector3d Origin,
    Vector3d Target,
    Vector3f Direction,
    MissileType MissileType,
    float MuzzleVelocityMetresPerSecond,
    string ModelName)
{
    /// <summary>Gets the authored vehicle that owns this shot, when emitted by a mission runtime.</summary>
    public GameTask? Owner { get; init; }
}

/// <summary>Runs the target-retention and gun gate shared by firing cars.</summary>
/// <remarks>
/// The Level 6 retail capture observes the APC acquire player task zero, retain it for 600 ticks,
/// and submit its first WEAPON_ID_APC request on the following vehicle tick. Gun_Fire then applies
/// the weapon row's ordinary 12-rounds-per-minute clock; the projectile is a type-zero direct missile.
/// </remarks>
public sealed class AiVehicleCombat
{
    /// <summary>Retail CarAI target retention written by IGI1 <c>0x00426000</c>.</summary>
    public const int TargetRetentionTicks = 600;

    /// <summary>Forward displacement of the APC gun from the car origin in the clean Level 6 trace.</summary>
    public const float CapturedMuzzleForwardMetres = 1.44f;

    /// <summary>Vertical displacement of the APC gun from the car origin in the clean Level 6 trace.</summary>
    public const float CapturedMuzzleHeightMetres = 1.39f;

    private bool _hasTarget;
    private int _retentionTicks;
    private int _cooldownTicks;
    private Vector3d _lastTarget;

    /// <summary>Gets the remaining retail target-retention interval.</summary>
    public int RetentionTicks => _retentionTicks;

    /// <summary>Advances one fixed 30 Hz vehicle-combat tick.</summary>
    public VehicleCombatShot? Tick(in AiVehicleCombatInput input)
    {
        if (!input.IsVehicleActive || !input.CanFire || input.RoundsPerMinute <= 0)
        {
            Reset();
            return null;
        }

        if (_cooldownTicks > 0)
        {
            _cooldownTicks--;
        }

        bool visible = input.HasLineOfSight && InsideAuthoredView(input);
        bool acquiredThisTick = false;
        if (visible)
        {
            acquiredThisTick = !_hasTarget;
            _hasTarget = true;
            _retentionTicks = TargetRetentionTicks;
            _lastTarget = input.TargetPosition;
        }
        else if (_hasTarget && _retentionTicks > 0)
        {
            _retentionTicks--;
        }
        else
        {
            _hasTarget = false;
            _retentionTicks = 0;
        }

        // The retail trace has one complete AI update between VISION's first visible result and
        // Gun_Fire's first request. Retaining the target here but firing next tick reproduces it.
        // CarAI's target scan (IGI1 0x00426000) returns active while its retained task pointer is
        // counting down, even when the current detection slots are empty. Keep firing at the last
        // observed pose during that interval; visibility is the acquisition/update gate, not a
        // second gun gate.
        if (!_hasTarget || acquiredThisTick || _cooldownTicks > 0)
        {
            return null;
        }

        _cooldownTicks = Math.Max(1, GunTiming.TicksPerRound(input.RoundsPerMinute));
        Vector3f forward = Normalized(input.VehicleOrientation.Column2, Vector3f.UnitY);
        Vector3d origin = input.VehiclePosition + new Vector3d(
            forward.X * CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            forward.Y * CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            (forward.Z * CapturedMuzzleForwardMetres + CapturedMuzzleHeightMetres) * WorldScene.UnitsPerMetre);
        Vector3f direction = Normalized(new Vector3f(
            (float)(_lastTarget.X - origin.X),
            (float)(_lastTarget.Y - origin.Y),
            (float)(_lastTarget.Z - origin.Z)), forward);

        return new VehicleCombatShot(
            origin,
            _lastTarget,
            direction,
            MissileType.MissileDirect,
            input.MuzzleVelocityMetresPerSecond >= 100f
                ? input.MuzzleVelocityMetresPerSecond
                : MissileFlight.FallbackLaunchMetresPerSecond,
            "110_02_1");
    }

    private static bool InsideAuthoredView(in AiVehicleCombatInput input)
    {
        Vector3d delta = input.TargetPosition - input.VehiclePosition;
        double distance = delta.Length;
        if (distance > input.ViewLengthMetres * WorldScene.UnitsPerMetre || distance <= 1e-6)
        {
            return false;
        }

        Vector3f worldDirection = new(
            (float)(delta.X / distance),
            (float)(delta.Y / distance),
            (float)(delta.Z / distance));
        Vector3f localDirection = Normalized(
            input.VehicleOrientation.Transform(worldDirection),
            Vector3f.UnitY);

        double horizontalLength = Math.Sqrt(
            (localDirection.X * localDirection.X) + (localDirection.Y * localDirection.Y));
        if (input.ViewGammaDegrees < 360f && horizontalLength > 1e-6)
        {
            double angle = Math.Acos(Math.Clamp(localDirection.Y / horizontalLength, -1d, 1d))
                * 180d / Math.PI;
            if (angle > input.ViewGammaDegrees * 0.5d)
            {
                return false;
            }
        }

        if (input.ViewAlphaDegrees < 180f)
        {
            double elevation = Math.Abs(
                Math.Asin(Math.Clamp(localDirection.Z, -1f, 1f)) * 180d / Math.PI);
            if (elevation > input.ViewAlphaDegrees * 0.5d)
            {
                return false;
            }
        }

        return true;
    }

    private void Reset()
    {
        _hasTarget = false;
        _retentionTicks = 0;
        _cooldownTicks = 0;
        _lastTarget = default;
    }

    private static Vector3f Normalized(Vector3f value, Vector3f fallback)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 1e-6f ? value * (1f / length) : fallback;
    }
}
