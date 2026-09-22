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

    private static SceneLighting Lighting(float scale, Vector3f? atmosphereSun = null) => new(
        LightingModel.Dynamic,
        new Vector3f(0f, 0f, 1f),
        Sun,
        Vector3f.Zero,
        scale,
        AtmosphereSunColour: atmosphereSun);
}
