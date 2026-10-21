using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Weapons;

/// <summary>
/// The spent casing a round throws out, as the tail of <c>Gun_Fire</c> (<c>0x478BA0</c>) launches it.
/// </summary>
/// <remarks>
/// <para>
/// A casing is only ever produced when the weapon names a <see cref="WeaponType.CasingModel"/>
/// <b>and</b> the gun belongs to the local player — the original compares the gun's owner against the
/// player before doing any of this, so an AI soldier's brass is never simulated.
/// </para>
/// <para>
/// Both random draws are integers, and the speed conversion is integer arithmetic rather than the
/// floating-point factor the player's movement uses. That is worth keeping: the same
/// <c>4096000 / 108000</c> ratio appears in both places, but only one of them rounds.
/// </para>
/// </remarks>
public static class GunCasing
{
    /// <summary>The numerator of the kilometres-per-hour to units-per-tick conversion.</summary>
    /// <remarks>A kilometre is 4096000 world units.</remarks>
    public const int SpeedNumerator = 4096000;

    /// <summary>The denominator of the same conversion.</summary>
    /// <remarks>An hour is 108000 ticks — 3600 seconds at 30 Hz.</remarks>
    public const int SpeedDenominator = 108000;

    /// <summary>The slowest a casing leaves the weapon, in kilometres per hour.</summary>
    public const int MinimumSpeedKilometresPerHour = 4;

    /// <summary>How many whole speeds the draw spans.</summary>
    /// <remarks>Four, so the speed is 4, 5, 6 or 7 kilometres per hour and never anything between.</remarks>
    public const int SpeedRange = 4;

    /// <summary>How many whole degrees the pitch draw spans.</summary>
    /// <remarks>Thirty, so the pitch is a whole number of degrees from 0 to 29.</remarks>
    public const int PitchRangeDegrees = 30;

    /// <summary>Converts a whole speed in kilometres per hour to units per tick.</summary>
    /// <param name="kilometresPerHour">The speed.</param>
    /// <returns>The speed in world units per tick, truncated.</returns>
    /// <remarks>
    /// <b>Integer arithmetic, and the truncation is real.</b> The multiply happens first and the
    /// division truncates, so 4 km/h gives 151 units per tick where the exact value is 151.7. The
    /// player's movement converts the same quantity in floating point and does not lose this, so the
    /// two paths genuinely disagree by a fraction of a unit.
    /// </remarks>
    public static int SpeedInUnitsPerTick(int kilometresPerHour) =>
        SpeedNumerator * kilometresPerHour / SpeedDenominator;

    /// <summary>Draws a casing's launch speed.</summary>
    /// <param name="uniformSpeedSample">A uniform integer on 0 to <see cref="SpeedRange"/> minus one.</param>
    /// <returns>The speed in world units per tick.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside its range.</exception>
    public static int LaunchSpeed(int uniformSpeedSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformSpeedSample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformSpeedSample, SpeedRange);

        return SpeedInUnitsPerTick(uniformSpeedSample + MinimumSpeedKilometresPerHour);
    }

    /// <summary>Draws a casing's launch pitch.</summary>
    /// <param name="uniformPitchSample">A uniform integer on 0 to <see cref="PitchRangeDegrees"/> minus one.</param>
    /// <returns>The pitch in radians, above the weapon's own axis.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside its range.</exception>
    /// <remarks>
    /// The original converts the integer to a float <i>before</i> scaling to radians and then negates
    /// it, so the casing pitches consistently to one side rather than either.
    /// </remarks>
    public static float LaunchPitch(int uniformPitchSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformPitchSample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformPitchSample, PitchRangeDegrees);

        return uniformPitchSample * GunFiring.DegreesToRadians;
    }
}
