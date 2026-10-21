using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>How near a human must be to a pickup to take it, which every pickup of the original tests the same way.</summary>
/// <remarks>
/// <c>GunPickup</c>'s and the generic pickup's proximity handlers (<c>0x46D230</c>, <c>0x46C630</c>) answer the message the
/// player sends every tick (<c>0x410F70</c>) with the Human's ORIGIN (<c>+32</c>, 3877.2 units above the feet standing), not
/// its feet: the planar distance must be under the item's reach -- 2867.2 units (0.7 m), or a generic item's model's
/// bounding radius when that is larger -- and the vertical under 4096 units (1 m). Neither asks for aim or for the activate
/// key. Tested from the feet, an item resting on a crate about a metre up only came into reach mid-jump.
/// </remarks>
public static class PickupReach
{
    /// <summary>The horizontal reach, in engine units.</summary>
    public const double Radius = 2867.2;

    /// <summary>The vertical reach, in engine units.</summary>
    public const double VerticalTolerance = 4096.0;

    /// <summary>Gets whether a human's origin is near enough to a pickup to take it.</summary>
    /// <param name="origin">The Human's origin (<c>+32</c>), not its feet.</param>
    /// <param name="pickup">Where the pickup is.</param>
    /// <param name="reach">The item's horizontal reach; <see cref="Radius"/> unless its model is larger.</param>
    /// <returns><see langword="true"/> within both reaches.</returns>
    public static bool IsWithinRange(Vector3d origin, Vector3d pickup, double reach = Radius)
    {
        double x = pickup.X - origin.X;
        double y = pickup.Y - origin.Y;
        return Math.Sqrt((x * x) + (y * y)) < reach && Math.Abs(pickup.Z - origin.Z) < VerticalTolerance;
    }
}
