using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins what the retail directional light's two angles mean, as both binaries compute them.
/// </summary>
/// <remarks>
/// <para>
/// <c>IGI.exe sub_527320</c> and <c>igi2ed 0x6573C0</c> both build
/// <c>(cos β cos γ, −cos β sin γ, sin β)</c>. <c>Beta</c> is therefore an ELEVATION above the horizon
/// and <c>Gamma</c> a compass bearing, which is the plain reading of a light that is placed by pointing
/// it.
/// </para>
/// <para>
/// <b>This file used to assert the opposite</b> — that <c>Beta</c> was a zenith angle and elevation was
/// <c>90° − Beta</c> — because the port took the forward row of the engine's Euler composition. That row
/// is <c>Row3</c> of <c>RotationZ(γ) · RotationY(β)</c>, and <c>Row3</c> of a Z rotation is
/// <c>(0, 0, 1)</c>, so the product's third row is the Y rotation's alone: <c>(−sin β, 0, cos β)</c>.
/// Gamma never entered the arithmetic.
/// </para>
/// <para>
/// The old reading was defended on the grounds that it put the sun high at noon where "the competing
/// readings put the sun on the horizon in every level". It did — but it also put the sun at azimuth 180°
/// in every level, because the bearing was being discarded, and it left mission 8 with a sun 54.6° UNDER
/// the world that the file itself recorded as unexplained. Both of those go away here: every level keeps
/// the bearing its authors gave it, and mission 8's sun is an ordinary one 35.4° up.
/// </para>
/// <para>
/// A sun pointing the wrong way still renders — it just lights the wrong side of the world — so these
/// assert the arithmetic against the exact values the probe printed rather than against a level loading.
/// </para>
/// </remarks>
public sealed class RetailDirlightTests
{
    [Fact]
    public void BetaIsAnElevationSoTheSunStandsAtBeta()
    {
        // Mission 1's authored keyframe. 0.5236 is pi/6 to five places, which is itself the evidence
        // that a script Angle is radians rather than degrees.
        Vector3f direction = DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;

        Assert.Equal(30f, elevation, 1);
        Assert.Equal(1f, direction.Length, 4);
    }

    /// <summary>Every authored keyframe puts its sun at the elevation it names, above the horizon.</summary>
    /// <remarks>
    /// Low suns, 6° to 42°, and that is what the data says rather than something to be explained away.
    /// The levels are lit by bakes; the directional reaches only what has none, so where it points is a
    /// lighting decision and not an almanac.
    /// </remarks>
    [Theory]
    [InlineData(0.5236f, -4.92183f, 30f)]
    [InlineData(0.41888f, -4.92183f, 24f)]
    [InlineData(0.44888f, 3.97935f, 25.7f)]
    [InlineData(0.10472f, 0f, 6f)]
    [InlineData(0.73304f, -0.31416f, 42f)]
    public void EveryAuthoredMissionPutsItsSunAtTheElevationItNames(
        float beta,
        float gamma,
        float expectedElevation)
    {
        Vector3f direction = DirectionalLightSettings.DirectionFrom(beta, gamma);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;

        Assert.Equal(expectedElevation, elevation, 1);
        Assert.False(direction.Z < 0f, "an authored sun should be above the horizon");
    }

    /// <summary>The bearing reaches the direction, which is the whole of what was wrong before.</summary>
    /// <remarks>
    /// The old composition returned azimuth 180° for every one of these. That is the signature of a
    /// discarded angle: five levels whose authors pointed the light five different ways, all lit from
    /// due south.
    /// </remarks>
    [Theory]
    [InlineData(0.5236f, -4.92183f, -78f)]
    [InlineData(0.44888f, 3.97935f, 132f)]
    [InlineData(0.10472f, 0f, 0f)]
    [InlineData(0.73304f, -0.31416f, 18f)]
    public void TheAuthoredBearingSurvivesIntoTheDirection(float beta, float gamma, float expectedAzimuth)
    {
        Vector3f direction = DirectionalLightSettings.DirectionFrom(beta, gamma);

        float azimuth = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;

        Assert.Equal(expectedAzimuth, azimuth, 1);
    }

    /// <summary>Mission 8's keyframe is an ordinary sun once the angles are read as the binaries read them.</summary>
    /// <remarks>
    /// Beta 144.6°. Under the old composition this came out 54.6° below the world, and the test that
    /// pinned it said so and left it as "the one piece of evidence for whatever explains it". There is
    /// nothing to explain: past 90° the cosine turns the horizontal half around, so this is a sun 35.4°
    /// up on the opposite bearing.
    /// </remarks>
    [Fact]
    public void MissionEightsSunIsAboveTheHorizonOnTheOppositeBearing()
    {
        Vector3f direction = DirectionalLightSettings.DirectionFrom(2.5236f, -0.94248f);

        float elevation = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;
        float azimuth = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;

        Assert.Equal(35.4f, elevation, 1);
        Assert.Equal(-126f, azimuth, 1);
        Assert.Equal(1f, direction.Length, 4);
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
