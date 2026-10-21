using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A line heard going by, as <c>GunShot_FindHearableIntersections</c> hears one bullet.</summary>
public sealed class Igi2GunShotHearingTests
{
    [Fact]
    public void A_line_is_heard_within_two_metres_of_the_listener()
    {
        Vector3d from = new(0, 0, 0);
        Vector3d to = new(100000, 0, 0);

        Assert.True(Igi2GunShot.Hears(from, to, new Vector3d(50000, 4096, 0), out Vector3d nearest));
        Assert.Equal(50000.0, nearest.X, 1.0);
        Assert.Equal(0.0, nearest.Y, 1.0);

        Assert.False(Igi2GunShot.Hears(from, to, new Vector3d(50000, 12288, 0), out _));
        Assert.False(Igi2GunShot.Hears(from, to, new Vector3d(-20000, 0, 0), out _));

        // Its muzzle, then its end, are heard before the line between.
        Assert.True(Igi2GunShot.Hears(from, to, new Vector3d(100, 0, 0), out Vector3d muzzle));
        Assert.Equal(from, muzzle);
        Assert.True(Igi2GunShot.Hears(from, to, new Vector3d(99900, 0, 0), out Vector3d end));
        Assert.Equal(to, end);
    }
}
