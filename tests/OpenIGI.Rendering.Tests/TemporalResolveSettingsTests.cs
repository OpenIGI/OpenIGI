using OpenIGI.Rendering.Temporal;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how a frame's temporal settings are chosen, which the RHI renderer takes from one place and the
/// Vulkan device makes inline: the two have to make the same choices or their histories differ.
/// </summary>
public sealed class TemporalResolveSettingsTests
{
    [Fact]
    public void The_resolve_runs_only_where_the_filter_accumulates()
    {
        Assert.True(TemporalResolveSettings.ForFrame(new TemporalAaSettings(Enabled: true), 1280, 1280, DebugView.None).Enabled);
        Assert.False(TemporalResolveSettings.ForFrame(TemporalAaSettings.Off, 1280, 1280, DebugView.None).Enabled);
        Assert.False(TemporalResolveSettings.ForFrame(new TemporalAaSettings(Enabled: true, Feedback: 1f), 1280, 1280, DebugView.None).Enabled);
    }

    [Fact]
    public void A_frame_rendered_at_fewer_pixels_leans_less_on_its_guide()
    {
        // One over one plus sixteen over the square of the render-to-present ratio: a seventeenth at full
        // size, and at half size four times further from the guide in the denominator.
        float full = TemporalResolveSettings.ForFrame(new TemporalAaSettings(Enabled: true), 1280, 1280, DebugView.None).ShadingBlend;
        float half = TemporalResolveSettings.ForFrame(new TemporalAaSettings(Enabled: true), 640, 1280, DebugView.None).ShadingBlend;

        Assert.Equal(1f / 17f, full, 6);
        Assert.Equal(1f / 65f, half, 6);
    }

    [Theory]
    [InlineData(DebugView.None, 0f)]
    [InlineData(DebugView.Motion, 1f)]
    [InlineData(DebugView.UpscaleParallax, 2f)]
    [InlineData(DebugView.UpscaleShading, 3f)]
    [InlineData(DebugView.UpscaleHistory, 4f)]
    [InlineData(DebugView.Albedo, 0f)]
    public void The_resolve_shows_the_views_that_are_its_own(DebugView view, float expected) =>
        Assert.Equal(expected, TemporalResolveSettings.ForFrame(new TemporalAaSettings(Enabled: true), 64, 64, view).DebugView);
}
