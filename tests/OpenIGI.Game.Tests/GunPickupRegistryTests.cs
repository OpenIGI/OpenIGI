using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class GunPickupRegistryTests
{
    [Fact]
    public void PickupRangeUsesTheRetailHorizontalAndVerticalThresholds()
    {
        Vector3d pickup = new(1000.0, 2000.0, 3000.0);

        Assert.True(PickupReach.IsWithinRange(
            new Vector3d(1000.0 + PickupReach.Radius - 0.1, 2000.0, 3000.0), pickup));
        Assert.False(PickupReach.IsWithinRange(
            new Vector3d(1000.0 + PickupReach.Radius, 2000.0, 3000.0), pickup));
        Assert.True(PickupReach.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + PickupReach.VerticalTolerance - 0.1), pickup));
        Assert.False(PickupReach.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + PickupReach.VerticalTolerance), pickup));
    }
}
