using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The two retail rules for an undrawn soldier's eye, against what the probe read on mission 4.</summary>
public sealed class SoldierEyeTests
{
    [Fact]
    public void AnUndrawnSoldierLooksAlongTheRestEyeTurnedByItsPlacedHeading()
    {
        // Guard 409's body matrix as placed (Human+120) and the eye matrix the sweep tested it with (ai+14896+24).
        Matrix3f placed = new(-0.10452f, 0.99452f, 0f, -0.99452f, -0.10452f, 0f, 0f, 0f, 1f);
        Matrix3f retailEye = new(-0.0126f, 0.9969f, 0.0772f, -0.9998f, -0.0114f, -0.0157f, -0.0148f, -0.0774f, 0.9968f);

        Matrix3f eye = placed * SoldierRenderer.RestEyeOrientation;

        float[] got = [eye.M11, eye.M12, eye.M13, eye.M21, eye.M22, eye.M23, eye.M31, eye.M32, eye.M33];
        float[] want =
        [
            retailEye.M11, retailEye.M12, retailEye.M13, retailEye.M21, retailEye.M22, retailEye.M23,
            retailEye.M31, retailEye.M32, retailEye.M33,
        ];
        // The probe printed four decimals; B * R matched every soldier to 2e-4.
        for (int i = 0; i < 9; i++)
        {
            Assert.InRange(Math.Abs(want[i] - got[i]), 0f, 3e-4f);
        }
    }

    [Fact]
    public void ASightRayRefreshesTheBystanderItPassesWithinItsBound()
    {
        // Tick 4544: squad member 404's ray to guard 411 refreshed bystander 403, 0.85 m off the line.
        Vector3d from = new(112955743, -55707999, 175293878);
        Vector3d to = new(113073990, -55784000, 175298356);
        Vector3d bystander = new(112969800, -55712900, 175293800);
        double bound = 0.94 * 4096.0;

        Assert.True(SoldierRenderer.SegmentMeetsSphere(from, to, bystander, bound));

        // The same man 1.5 m off the line -- where the port's 403 stood when it engaged early -- is not met.
        Vector3d offLine = new(bystander.X + 1440, bystander.Y + 2240, bystander.Z);
        Assert.False(SoldierRenderer.SegmentMeetsSphere(from, to, offLine, bound));

        // Nor is one level with the line but beyond its far end.
        Assert.False(SoldierRenderer.SegmentMeetsSphere(from, to, new Vector3d(113100000, -55800000, 175298000), bound));
    }
}
