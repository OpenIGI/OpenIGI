using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>A soldier's heading carried to a co-op client and turned back into the orientation it was taken from.</summary>
public sealed class SoldierPuppetTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(-2.5f)]
    [InlineData(3.1f)]
    public void A_heading_taken_from_an_orientation_rebuilds_it(float gamma)
    {
        Matrix3f orientation = Matrix3f.RotationZ(gamma);
        Matrix3f rebuilt = Matrix3f.RotationZ(SoldierPuppet.FacingOf(orientation));

        Assert.Equal(orientation.M11, rebuilt.M11, 5);
        Assert.Equal(orientation.M12, rebuilt.M12, 5);
        Assert.Equal(orientation.M21, rebuilt.M21, 5);
        Assert.Equal(orientation.M22, rebuilt.M22, 5);
    }
}
