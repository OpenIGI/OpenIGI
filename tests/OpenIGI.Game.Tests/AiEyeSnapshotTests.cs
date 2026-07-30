using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the cone test the detection cascade actually runs — <c>0x489960</c> against the eye snapshot at
/// <c>HumanAI+14896</c> — and the three ways it differs from <c>0x4502F0</c>.
/// </summary>
public sealed class AiEyeSnapshotTests
{
    // The identity eye: X right, Y forward, Z up.
    private static readonly Matrix3f LookingAlongY = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    private static readonly Vector3d Origin = new(0.0, 0.0, 0.0);

    /// <summary>The alert latch picks each cone's length when the snapshot is taken, not when it is tested.</summary>
    [Fact]
    public void TheSnapshotResolvesEachConesLengthOnce()
    {
        AiVision vision = new()
        {
            Primary = new AiViewCone(1.0, 1.0, 10.0 * AiViewCone.UnitsPerMetre, 40.0 * AiViewCone.UnitsPerMetre),
            Secondary = new AiViewCone(1.0, 1.0, 5.0 * AiViewCone.UnitsPerMetre, 5.0 * AiViewCone.UnitsPerMetre),
        };

        AiEyeSnapshot calm = vision.Capture(Origin, LookingAlongY, alerted: false);
        AiEyeSnapshot alerted = vision.Capture(Origin, LookingAlongY, alerted: true);

        Assert.Equal(10.0 * AiViewCone.UnitsPerMetre, calm.Primary.Length, 6);
        Assert.Equal(40.0 * AiViewCone.UnitsPerMetre, alerted.Primary.Length, 6);

        // Twenty metres straight ahead: outside the calm snapshot, inside the alerted one. The snapshot
        // holds the choice, so the same target tests differently against the two without either being
        // re-picked at test time.
        Vector3d target = new(0.0, 20.0 * AiViewCone.UnitsPerMetre, 0.0);
        Assert.False(calm.IsVisible(target));
        Assert.True(alerted.IsVisible(target));
    }

    /// <summary>
    /// The gate byte at <c>+112</c> turns the secondary cone off entirely — which is what an
    /// <c>SCamera</c>'s snapshot reads back in the live game, and what <c>0x4502F0</c> has no equivalent of.
    /// </summary>
    [Fact]
    public void TheGateByteSwitchesOffThePeripheralCone()
    {
        AiVision vision = new()
        {
            // A narrow primary and the retail-wide secondary: a target 70 degrees off-axis is only ever
            // seen through the peripheral cone.
            Primary = new AiViewCone(
                45.0 * AiViewCone.DegreesToRadians,
                10.0 * AiViewCone.DegreesToRadians,
                20.0 * AiViewCone.UnitsPerMetre,
                20.0 * AiViewCone.UnitsPerMetre),
            Secondary = AiViewCone.DefaultSecondary,
        };

        Vector3d beside = Bearing(70.0, 5.0 * AiViewCone.UnitsPerMetre);

        Assert.True(vision.Capture(Origin, LookingAlongY, alerted: false).IsVisible(beside));

        vision.SecondaryEnabled = false;
        Assert.False(vision.Capture(Origin, LookingAlongY, alerted: false).IsVisible(beside));
    }

    /// <summary>
    /// <c>0x489960</c> has no <c>local.y &lt; 0</c> early-out, and it does not need one: with every
    /// configured half-angle under 90°, a target behind the eye fails on the azimuth instead.
    /// </summary>
    /// <remarks>
    /// Asserted over the whole retail cone vocabulary — the two constructor defaults and both archetype
    /// primaries — so the equivalence is a property of the shipped parameters, stated as such rather than
    /// assumed from one case.
    /// </remarks>
    [Theory]
    [InlineData(45.0, 50.0)]    // 0x44F060's primary
    [InlineData(45.0, 80.0)]    // 0x44F060's secondary, the widest cone in the game
    [InlineData(50.0, 45.0)]    // guard / patrol / civilian
    [InlineData(30.0, 15.0)]    // sniper
    [InlineData(45.0, 27.0)]    // the SCamera
    public void NothingBehindTheEyeIsEverSeen(double alphaDegrees, double gammaDegrees)
    {
        AiViewCone cone = new(
            alphaDegrees * AiViewCone.DegreesToRadians,
            gammaDegrees * AiViewCone.DegreesToRadians,
            100.0 * AiViewCone.UnitsPerMetre,
            100.0 * AiViewCone.UnitsPerMetre);

        AiVision vision = new() { Primary = cone, Secondary = cone };
        AiEyeSnapshot snapshot = vision.Capture(Origin, LookingAlongY, alerted: false);

        for (double bearing = 90.0; bearing <= 270.0; bearing += 5.0)
        {
            Vector3d behind = Bearing(bearing, 1.0 * AiViewCone.UnitsPerMetre);
            Assert.False(snapshot.IsVisible(behind));
        }
    }

    /// <summary>Range is <c>&lt;=</c> and the rim is <c>&lt;</c>, exactly as <c>0x4502F0</c> has it.</summary>
    [Fact]
    public void MaximumRangeIsSeenAndTheRimIsNot()
    {
        AiViewCone cone = new(
            45.0 * AiViewCone.DegreesToRadians,
            45.0 * AiViewCone.DegreesToRadians,
            10.0 * AiViewCone.UnitsPerMetre,
            10.0 * AiViewCone.UnitsPerMetre);

        AiVision vision = new() { Primary = cone, Secondary = cone, SecondaryEnabled = false };
        AiEyeSnapshot snapshot = vision.Capture(Origin, LookingAlongY, alerted: false);

        Assert.True(snapshot.IsVisible(new Vector3d(0.0, 10.0 * AiViewCone.UnitsPerMetre, 0.0)));
        Assert.False(snapshot.IsVisible(Bearing(45.0, 1.0 * AiViewCone.UnitsPerMetre)));
        Assert.True(snapshot.IsVisible(Bearing(44.5, 1.0 * AiViewCone.UnitsPerMetre)));
    }

    /// <summary>
    /// On retail parameters the snapshot test and <c>0x4502F0</c> agree everywhere, which is why swapping
    /// them changed nothing on screen — and why the reason for swapping them is what the snapshot
    /// <em>carries</em>, not what it answers.
    /// </summary>
    [Fact]
    public void TheTwoConeTestsAgreeOnEveryRetailParameterSet()
    {
        AiVision vision = new()
        {
            Primary = AiViewCone.DefaultPrimary
                .WithAlphaInDegrees(50.0).WithGammaInDegrees(45.0)
                .WithLengthInMetres(40.0).WithAlarmLengthInMetres(50.0),
            Secondary = AiViewCone.DefaultSecondary,
        };

        foreach (bool alerted in new[] { false, true })
        {
            AiEyeSnapshot snapshot = vision.Capture(Origin, LookingAlongY, alerted);
            for (double bearing = -180.0; bearing < 180.0; bearing += 3.0)
            {
                for (double metres = 1.0; metres <= 60.0; metres += 1.0)
                {
                    Vector3d target = Bearing(bearing, metres * AiViewCone.UnitsPerMetre);
                    Assert.Equal(
                        vision.IsVisible(Origin, LookingAlongY, target, alerted),
                        snapshot.IsVisible(target));
                }
            }
        }
    }

    /// <summary>A point at a horizontal bearing from forward, at a given range.</summary>
    private static Vector3d Bearing(double degrees, double range)
    {
        double radians = degrees * Math.PI / 180.0;
        return new Vector3d(range * Math.Sin(radians), range * Math.Cos(radians), 0.0);
    }
}
