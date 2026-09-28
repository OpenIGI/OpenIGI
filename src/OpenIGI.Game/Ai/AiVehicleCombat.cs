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

    /// <summary>Gets the exact retail weapon-table row that emitted this shot.</summary>
    public string WeaponScriptId { get; init; } = string.Empty;

    /// <summary>Gets whether the row fires an instant gunshot (<c>TASKTYPE_GUNSHOT</c>) rather than a missile.</summary>
    public bool IsHitscan { get; init; }
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

        if (Track(input) is null || _cooldownTicks > 0)
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

    /// <summary>Updates what the vehicle sees and returns the target its guns should aim at.</summary>
    /// <param name="input">The vehicle's live and authored state; its gun fields are not read.</param>
    /// <param name="retainWhileHidden">
    /// Whether a target that drops out of sight is still returned for <see cref="TargetRetentionTicks"/>.
    /// The Level 6 capture's fixed gun does; a model gun (<c>0x46F390</c>) takes only a target the
    /// vehicle sees now and does its own 90-run hold -- probed on Mission 7's T80, which stopped firing
    /// the tick the player was pushed past its 50-metre view.
    /// </param>
    /// <returns>
    /// The last observed target while it is retained, or <see langword="null"/> when the vehicle sees
    /// nothing, is inactive, or acquired the target only this tick.
    /// </returns>
    /// <remarks>
    /// This is the vehicle's half of the fight: its vision record, which each of its guns asks for
    /// (<c>0x46F390</c> through message <c>0x489630</c>). The guns turn and fire on their own.
    /// </remarks>
    public Vector3d? Track(in AiVehicleCombatInput input, bool retainWhileHidden = true)
    {
        if (!input.IsVehicleActive || !input.CanFire)
        {
            Reset();
            return null;
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
        else if (_hasTarget && _retentionTicks > 0 && retainWhileHidden)
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
        return _hasTarget && !acquiredThisTick ? _lastTarget : null;
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
