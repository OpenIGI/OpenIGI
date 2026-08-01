using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the readings the original took off the retail directional light.
/// </summary>
/// <remarks>
/// These are the conclusions that would be quietest to get wrong — a sun pointing the wrong way still
/// renders, it just lights the wrong side of the world — so the arithmetic is asserted against the
/// exact values the probe printed rather than against a level being loaded.
/// </remarks>
public sealed class RetailDirlightTests
{
    [Fact]
    public void BetaIsAZenithAngleSoElevationIsNinetyLessBeta()
    {
        // Mission 1's authored keyframe. 0.5236 is pi/6 to five places, which is itself the evidence
        // that a script Angle is radians rather than degrees.
        Vector3f direction = DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;

        Assert.Equal(60f, elevation, 1);
        Assert.Equal(1f, direction.Length, 4);
    }

    [Theory]
    [InlineData(0.5236f, -4.92183f, 60f)]
    [InlineData(0.41888f, -4.92183f, 66f)]
    [InlineData(0.44888f, 3.97935f, 64.3f)]
    [InlineData(0.10472f, 0f, 84f)]
    [InlineData(0.73304f, -0.31416f, 48f)]
    public void EveryAuthoredMissionPutsItsSunHighAtNoon(float beta, float gamma, float expectedElevation)
    {
        // A Time of 12 should mean a high sun, and it does: 48 to 84 degrees. The competing readings
        // of these same numbers put the sun within 6 degrees of the horizon in every level.
        Vector3f direction = DirectionalLightSettings.DirectionFrom(beta, gamma);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;

        Assert.Equal(expectedElevation, elevation, 1);
        Assert.False(direction.Z < 0f, "an authored noon sun should be above the horizon");
    }

    [Fact]
    public void MissionEightsSunIsBelowTheHorizonAndIsReportedRatherThanClamped()
    {
        // Beta 144.6 degrees. Left exactly as measured: this is the one piece of evidence for whatever
        // explains it, and quietly flipping it would destroy that.
        Vector3f direction = DirectionalLightSettings.DirectionFrom(2.5236f, -0.94248f);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;

        Assert.Equal(-54.6f, elevation, 1);
    }

    [Fact]
    public void GammaIsNotWrappedAndBothTurnsGiveTheSameDirection()
    {
        // -4.92183 and its wrap to +1.36 must agree; retail authors both signs and values past a full
        // turn, so anything that assumed a range would be reading a different sun.
        Vector3f asAuthored = DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f);
        Vector3f wrapped = DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f + (2f * MathF.PI));

        Assert.Equal(asAuthored.X, wrapped.X, 4);
        Assert.Equal(asAuthored.Y, wrapped.Y, 4);
        Assert.Equal(asAuthored.Z, wrapped.Z, 4);
    }

    [Fact]
    public void TheTemplateKeyframeIsOutsideAnHourOfTheDay()
    {
        // The Time = 100 row is identical across all fourteen missions and carries a green sun; it is
        // an editor default. The reader picks by this bound, so the bound is what the test pins.
        Assert.True(100f > DirectionalLightSettings.LatestHour);
        Assert.True(12f <= DirectionalLightSettings.LatestHour);
    }
}
