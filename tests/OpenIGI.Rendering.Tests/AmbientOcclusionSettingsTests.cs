using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins when the horizon search is worth running.
/// </summary>
/// <remarks>
/// The pass is a full-screen search with an inner loop per direction, so "would this do anything" is
/// not a tidiness question — a configuration that reads as enabled but contributes nothing costs the
/// whole search and returns the frame it started with.
/// </remarks>
public sealed class AmbientOcclusionSettingsTests
{
    [Fact]
    public void NoDirectionsMeansNoPass()
    {
        Assert.False(AmbientOcclusionSettings.Off.Enabled);
        Assert.False(new AmbientOcclusionSettings(Quality: 0, Intensity: 2f).Enabled);
    }

    [Fact]
    public void AZeroRadiusMeansNoPass()
    {
        // Nothing can occlude anything within no distance, so the search would return full visibility
        // for every pixel at the full cost of finding that out.
        Assert.False(new AmbientOcclusionSettings(Quality: 4, Radius: 0f).Enabled);
    }

    [Fact]
    public void ABounceAloneIsStillWorthRunning()
    {
        // Occlusion at zero intensity with the bounce on is a legitimate configuration: it gathers
        // colour between surfaces without darkening anything, which is how the effect is tuned.
        AmbientOcclusionSettings settings = new(
            Quality: 4, Intensity: 0f, BounceQuality: 2, BounceIntensity: 1f);

        Assert.True(settings.Enabled);
        Assert.True(settings.WantsBounce);
    }

    [Fact]
    public void ABounceAtZeroStrengthIsNotGathered()
    {
        // Otherwise every sample would fetch a colour that is then multiplied away, which is the
        // expensive half of the search bought for nothing.
        Assert.False(new AmbientOcclusionSettings(4, BounceQuality: 4, BounceIntensity: 0f).WantsBounce);
        Assert.False(new AmbientOcclusionSettings(4, BounceQuality: 0, BounceIntensity: 1f).WantsBounce);
    }

    [Fact]
    public void OcclusionWithoutABounceIsTheDefaultShape()
    {
        AmbientOcclusionSettings settings = new(Quality: 3);

        Assert.True(settings.Enabled);
        Assert.False(settings.WantsBounce);
    }
}
