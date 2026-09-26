using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Sky;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a frame's lighting becomes the conditions the atmosphere is built for, which both
/// renderers now take from one place rather than each keeping its own copy.
/// </summary>
public sealed class SkyConditionsTests
{
    private static readonly Vector3f Sun = new(1f, 0.5f, 0.25f);

    [Fact]
    public void TheAtmosphereTakesItsOwnSunWhenOneIsNamed()
    {
        SceneLighting lighting = Lighting(scale: 2f, atmosphereSun: new Vector3f(3f, 4f, 5f));

        SkyConditions sky = SkyConditions.ForScene(lighting, Matrix3f.Identity, altitudeMetres: 2f);

        Assert.Equal(new Vector3f(6f, 8f, 10f), sky.SunIlluminance);
    }

    [Fact]
    public void WithoutItsOwnSunTheAtmosphereSharesTheDirectOne()
    {
        SkyConditions sky = SkyConditions.ForScene(Lighting(scale: 2f), Matrix3f.Identity, altitudeMetres: 2f);

        Assert.Equal(new Vector3f(2f, 1f, 0.5f), sky.SunIlluminance);
    }

    [Fact]
    public void TheSunIsTurnedFromViewSpaceIntoTheWorld()
    {
        // A quarter turn about the vertical: the view's X axis is the world's Y.
        Matrix3f viewToWorld = new(
            0f, -1f, 0f,
            1f, 0f, 0f,
            0f, 0f, 1f);

        SceneLighting lighting = Lighting(scale: 1f) with { SunDirection = new Vector3f(1f, 0f, 0f) };

        Vector3f world = SkyConditions.ForScene(lighting, viewToWorld, altitudeMetres: 2f).SunDirection;

        Assert.Equal(0f, world.X, 5);
        Assert.Equal(1f, world.Y, 5);
        Assert.Equal(0f, world.Z, 5);
    }

    [Fact]
    public void ASunWithNoDirectionIsTakenAsStraightUp()
    {
        SceneLighting lighting = Lighting(scale: 1f) with { SunDirection = Vector3f.Zero };

        Assert.Equal(
            new Vector3f(0f, 0f, 1f),
            SkyConditions.ForScene(lighting, Matrix3f.Identity, altitudeMetres: 2f).SunDirection);
    }

    [Fact]
    public void TheSkyNeverLightsTheWorldNegatively()
    {
        SceneLighting lighting = Lighting(scale: 1f) with { SkyLightIntensity = -1f };

        Assert.Equal(0f, SkyConditions.ForScene(lighting, Matrix3f.Identity, altitudeMetres: 2f).SkyLightIntensity);
    }

    [Fact]
    public void TheEyeIsPlacedAtTheAltitudeItIsGiven() =>
        Assert.Equal(
            SkyConditions.ViewHeightFor(2f),
            SkyConditions.ForScene(Lighting(scale: 1f), Matrix3f.Identity, altitudeMetres: 2f).ViewHeightKm);

    [Fact]
    public void TheNightGlowIsScaledWithTheSourceItIsDrawnBeside()
    {
        SceneLighting lighting = Lighting(scale: 2f) with
        {
            Night = new NightSky(Airglow: 0.004f, CloudCover: 0.5f, StarRadiance: 0f, Vector3f.UnitZ, MoonRadiance: 0f),
        };

        SkyConditions sky = SkyConditions.ForScene(lighting, Matrix3f.Identity, altitudeMetres: 2f);

        Assert.Equal(0.008f, sky.Airglow, 6);
        Assert.Equal(0.5f, sky.CloudCover, 6);
    }

    [Fact]
    public void ADayCarriesNoNightSky()
    {
        SkyConditions sky = SkyConditions.ForScene(Lighting(scale: 1f), Matrix3f.Identity, altitudeMetres: 2f);

        Assert.Equal(0f, sky.Airglow);
        Assert.Equal(0f, sky.CloudCover);
    }

    [Fact]
    public void CloudCoverIsHeldBetweenClearAndOvercast()
    {
        SceneLighting lighting = Lighting(scale: 1f) with
        {
            Night = new NightSky(0f, CloudCover: 3f, 0f, Vector3f.UnitZ, 0f),
        };

        Assert.Equal(1f, SkyConditions.ForScene(lighting, Matrix3f.Identity, altitudeMetres: 2f).CloudCover);
    }

    [Fact]
    public void ACloudingSkyIsRebuilt()
    {
        SkyConditions clear = SkyConditions.ForScene(Lighting(scale: 1f), Matrix3f.Identity, altitudeMetres: 2f);
        SkyConditions overcast = clear with { CloudCover = 0.8f };
        SkyConditions glowing = clear with { Airglow = 0.004f };

        Assert.True(clear.LooksLike(clear));
        Assert.False(clear.LooksLike(overcast));
        Assert.False(clear.LooksLike(glowing));
    }

    [Fact]
    public void TheNightSkyRidesInTheBlocksLastTwoSlots()
    {
        SkyConditions conditions = SkyConditions.ForScene(Lighting(scale: 1f), Matrix3f.Identity, 2f)
            with { Airglow = 0.004f, CloudCover = 0.75f };
        float[] block = new float[SkyParameters.Floats];

        SkyParameters.Write(block, 192, 104, conditions);

        Assert.Equal(0.004f, block[14]);
        Assert.Equal(0.75f, block[15]);
    }

    private static SceneLighting Lighting(float scale, Vector3f? atmosphereSun = null) => new(
        LightingModel.Dynamic,
        new Vector3f(0f, 0f, 1f),
        Sun,
        Vector3f.Zero,
        scale,
        AtmosphereSunColour: atmosphereSun);
}
