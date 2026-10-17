using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>How near a human's feet must be to a pickup to take it, which every pickup of the original tests the same way.</summary>
/// <remarks>
/// <c>GunPickup::Update</c> (<c>0x46D230</c>) and the generic pickup's update test the feet against the pickup every tick: the
/// planar distance must be under 2867.2 units (0.7 m) and the vertical under 4096 units (1 m). Neither asks for aim or for
/// the activate key.
/// </remarks>
public static class PickupReach
{
    /// <summary>The horizontal reach, in engine units.</summary>
    public const double Radius = 2867.2;

    /// <summary>The vertical reach, in engine units.</summary>
    public const double VerticalTolerance = 4096.0;

    /// <summary>Gets whether feet stand near enough to a pickup to take it.</summary>
    /// <param name="feet">Where the human's feet are.</param>
    /// <param name="pickup">Where the pickup is.</param>
    /// <returns><see langword="true"/> within both reaches.</returns>
    public static bool IsWithinRange(Vector3d feet, Vector3d pickup)
    {
        double x = pickup.X - feet.X;
        double y = pickup.Y - feet.Y;
        return Math.Sqrt((x * x) + (y * y)) < Radius && Math.Abs(pickup.Z - feet.Z) < VerticalTolerance;
    }
}
