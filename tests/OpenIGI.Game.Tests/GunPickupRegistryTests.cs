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

        Assert.True(GunPickupRegistry.IsWithinRange(
            new Vector3d(1000.0 + GunPickupRegistry.PickupRadius - 0.1, 2000.0, 3000.0), pickup));
        Assert.False(GunPickupRegistry.IsWithinRange(
            new Vector3d(1000.0 + GunPickupRegistry.PickupRadius, 2000.0, 3000.0), pickup));
        Assert.True(GunPickupRegistry.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + GunPickupRegistry.VerticalTolerance - 0.1), pickup));
        Assert.False(GunPickupRegistry.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + GunPickupRegistry.VerticalTolerance), pickup));
    }
}
