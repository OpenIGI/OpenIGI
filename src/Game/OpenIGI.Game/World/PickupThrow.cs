using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// How I.G.I. 2 throws a pickup out of a human's hands (<c>GunPickup_NewFromWeapon</c>): a player letting go of its weapon throws it
/// far, a body that falls lets it go short. Both titles' multiplayer throw a dropped gun and a bomb mode's bomb this way.
/// </summary>
/// <remarks>
/// It leaves the hand from the body's root raised 1228.8, along the heading -- the long throw turned at random by up to an eighth of
/// a turn either way -- at 614.4 a tick along the ground and 273.07 up; the short throw at a quarter of that along the ground and
/// 68.27 up.
/// </remarks>
public static class PickupThrow
{
    /// <summary>How far above the body's root a throw leaves the hand, in world units.</summary>
    public const double AboveRoot = 1228.800048828125;

    // The long throw's speeds, units a tick.
    private const double Forward = 614.4000244140625;
    private static readonly double Up = BitConverter.Int32BitsToSingle(0x43888889);

    // The short throw's: a quarter along the ground, and its own rise.
    private const double ShortShare = 0.25;
    private static readonly double ShortUp = BitConverter.Int32BitsToSingle(0x42888889);

    /// <summary>Gets the heading a throw goes along.</summary>
    /// <param name="yaw">The thrower's heading, in radians.</param>
    /// <param name="far">Whether it is the long throw, which is turned at random.</param>
    /// <param name="draw">A draw in [0, 1) for the long throw's turn.</param>
    /// <returns>The heading, in radians.</returns>
    public static double Heading(double yaw, bool far, double draw) => far ? yaw + ((draw - 0.5) * (Math.PI / 2.0)) : yaw;

    /// <summary>Gets a throw's speed along a heading.</summary>
    /// <param name="heading">The heading, in radians, as <see cref="Heading"/> gives it: forward is (sin, cos).</param>
    /// <param name="far">Whether it is the long throw.</param>
    /// <returns>Its speed, units a tick.</returns>
    public static Vector3d Velocity(double heading, bool far)
    {
        double along = far ? Forward : Forward * ShortShare;
        return new Vector3d(Math.Sin(heading) * along, Math.Cos(heading) * along, far ? Up : ShortUp);
    }
}
