using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a mission's authored atmosphere layers over the defaults, and what happens when it does
/// not exist.
/// </summary>
/// <remarks>
/// The layering is the part worth guarding. A preset file where each mission had to restate every
/// field would be one where changing a default silently reached none of them — the same failure the
/// settings file avoids by recording a diff rather than a snapshot.
/// </remarks>
public sealed class FogPresetTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "openigi-fog-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private FogPresets Write(string json)
    {
        File.WriteAllText(_path, json);
        return FogPresets.Load(_path);
    }

    [Fact]
    public void AMissionTakesTheDefaultForEverythingItDoesNotSay()
    {
        FogPresets presets = Write("""
            {
              "default": { "heightFalloff": 30.0, "anisotropy": 0.6, "distance": 400.0 },
              "missions": { "3": { "density": 0.02 } }
            }
            """);

        VolumetricFogSettings fog = presets.Resolve(3, derivedDensity: 0.5f, baseHeight: 4f, steps: 32);

        Assert.Equal(0.02f, fog.Density, 5);
        Assert.Equal(30f, fog.HeightFalloff, 3);
        Assert.Equal(0.6f, fog.Anisotropy, 3);
        Assert.Equal(400f, fog.Distance, 3);
    }

    [Fact]
    public void AMissionWithNoEntryDerivesItsDensityFromItsOwnSky()
    {
        // The point of the fallback: every level gets a distinct mood from its own FlatSky data
        // without anyone having to author fourteen entries.
        FogPresets presets = Write("""{ "default": { "heightFalloff": 22.0 } }""");

        VolumetricFogSettings fog = presets.Resolve(11, derivedDensity: 0.017f, baseHeight: 6f, steps: 24);

        Assert.Equal(0.017f, fog.Density, 5);
        Assert.Equal(22f, fog.HeightFalloff, 3);
        Assert.Equal(6f, fog.BaseHeight, 3);
        Assert.Equal(24, fog.Steps);
    }

    [Fact]
    public void ADensityOfZeroMeansDeriveRatherThanNoFog()
    {
        FogPresets presets = Write("""
            { "default": { "density": 0.0 }, "missions": { "2": { "anisotropy": 0.8 } } }
            """);

        VolumetricFogSettings fog = presets.Resolve(2, derivedDensity: 0.012f, baseHeight: 0f, steps: 32);

        Assert.Equal(0.012f, fog.Density, 5);
        Assert.Equal(0.8f, fog.Anisotropy, 3);
    }

    [Fact]
    public void AMissionCanSwitchTheSunOffEntirely()
    {
        // Mission 8's authored sun is below the horizon, so its fog leans on ambient instead of
        // pretending to be lit by something that is underground.
        FogPresets presets = Write("""
            { "missions": { "8": { "sunIntensity": 0.0, "ambientIntensity": 1.6 } } }
            """);

        VolumetricFogSettings fog = presets.Resolve(8, 0.016f, 0f, 32);

        Assert.Equal(0f, fog.SunIntensity, 4);
        Assert.Equal(1.6f, fog.AmbientIntensity, 3);
    }

    [Fact]
    public void VolumesAreReadWithTheirShapeAndScale()
    {
        FogPresets presets = Write("""
            {
              "volumes": {
                "1": [
                  { "shape": "box", "centre": [10, 20, 30], "extent": [4, 5, 6], "densityScale": 3.0 },
                  { "shape": "sphere", "centre": [0, 0, 0], "extent": [12, 12, 12], "densityScale": 0.2 }
                ]
              }
            }
            """);

        ReadOnlySpan<LocalFogVolume> volumes = presets.VolumesFor(1);

        Assert.Equal(2, volumes.Length);
        Assert.Equal(FogVolumeShape.Box, volumes[0].Shape);
        Assert.Equal(30f, volumes[0].Centre.Z, 3);
        Assert.Equal(3f, volumes[0].DensityScale, 3);
        Assert.Equal(FogVolumeShape.Sphere, volumes[1].Shape);

        // Below one thins the medium instead of thickening it, which is how an interior is kept clear
        // of an outdoor fog bank.
        Assert.Equal(0.2f, volumes[1].DensityScale, 3);
        Assert.True(volumes[1].IsMeaningful);
    }

    [Fact]
    public void MoreVolumesThanTheShaderHoldsAreTruncatedRatherThanOverflowing()
    {
        string many = string.Join(",", Enumerable.Range(0, 20).Select(i =>
            $$"""{ "centre": [{{i}}, 0, 0], "extent": [1, 1, 1], "densityScale": 2.0 }"""));

        FogPresets presets = Write($$"""{ "volumes": { "4": [{{many}}] } }""");

        Assert.Equal(LocalFogVolume.MaxPerLevel, presets.VolumesFor(4).Length);
    }

    [Fact]
    public void AMissionWithNoVolumesGetsNone() =>
        Assert.True(Write("""{ "default": {} }""").VolumesFor(7).IsEmpty);

    [Fact]
    public void AMalformedFileFallsBackToTheLevelsOwnFogRatherThanFailing()
    {
        // A broken preset file must never stop the game: without it every level still has the fog its
        // own FlatSky task asks for, which is the behaviour from before this file existed.
        FogPresets presets = Write("{ this is not json");

        VolumetricFogSettings fog = presets.Resolve(1, derivedDensity: 0.013f, baseHeight: 2f, steps: 32);

        Assert.Equal(0.013f, fog.Density, 5);
        Assert.True(presets.VolumesFor(1).IsEmpty);
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        FogPresets presets = FogPresets.Load(Path.Combine(Path.GetTempPath(), "no-such-fog-presets.json"));

        Assert.Equal(0.02f, presets.Resolve(1, 0.02f, 0f, 32).Density, 5);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        // The shipped file documents itself with $comment keys and is edited by hand, so it has to
        // survive being edited by hand.
        FogPresets presets = Write("""
            {
              // why this level is thick
              "missions": { "5": { "density": 0.03, } },
            }
            """);

        Assert.Equal(0.03f, presets.Resolve(5, 0.01f, 0f, 32).Density, 5);
    }
}
