using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins when a target is asking to be upsampled rather than stretched.
/// </summary>
/// <remarks>
/// The distinction decides how much memory the backend allocates and which pass produces the final
/// pixels, so getting it wrong is not a slightly different picture — it is a second full-resolution
/// buffer set that nothing reads, or a stretch where an upsample was asked for.
/// </remarks>
public sealed class SceneTargetUpsamplingTests
{
    [Fact]
    public void AnOutputSizeOfZeroMeansPresentAtTheRenderSize()
    {
        SceneTargetDescription target = new(1280, 720);

        Assert.Equal(1280, target.PresentWidth);
        Assert.Equal(720, target.PresentHeight);
        Assert.False(target.IsUpsampling);
    }

    [Fact]
    public void ALargerOutputIsAnUpsample()
    {
        SceneTargetDescription target = new(896, 504, OutputWidth: 1280, OutputHeight: 720);

        Assert.True(target.IsUpsampling);
        Assert.Equal(1280, target.PresentWidth);
        Assert.Equal(720, target.PresentHeight);
    }

    [Fact]
    public void AnOutputEqualToTheRenderSizeIsNotAnUpsample()
    {
        // The shell passes the window size whenever the accumulation is on, so the equal case is the
        // common one at a render scale of 1 — and allocating a second set of buffers for it would cost
        // memory to copy an image onto itself.
        SceneTargetDescription target = new(1280, 720, OutputWidth: 1280, OutputHeight: 720);

        Assert.False(target.IsUpsampling);
    }

    [Fact]
    public void ATargetIsStillValidAndComparableWithAnOutputSize()
    {
        SceneTargetDescription target = new(896, 504, OutputWidth: 1280, OutputHeight: 720);

        Assert.True(target.IsValid);

        // The backend rebuilds whenever the description changes, so the output size has to take part in
        // that comparison or a change of render scale would keep the old buffers.
        Assert.NotEqual(target, target with { OutputWidth = 1920 });
    }

    [Fact]
    public void AccumulationIsOffWhenTheFeedbackTakesTheWholeFrame()
    {
        // 1 is how the setting is turned off from the console, and it has to mean "do not run the
        // pass" rather than "run it and blend nothing", which would still cost the history bandwidth.
        Assert.False(new TemporalAaSettings(Enabled: true, Feedback: 1f).Accumulates);
        Assert.True(new TemporalAaSettings(Enabled: true, Feedback: 0.1f).Accumulates);
        Assert.False(new TemporalAaSettings(Enabled: false, Feedback: 0.1f).Accumulates);
        Assert.False(TemporalAaSettings.Off.Accumulates);
    }

    [Fact]
    public void TheIdentityResolveAsksForNothing()
    {
        // The vanilla preset's whole claim: with no tone map, unit exposure and no bloom, going through
        // the offscreen target is a copy, and a backend may say so.
        Assert.True(PostProcessSettings.Identity.IsIdentity);
        Assert.False((PostProcessSettings.Identity with { Exposure = 1.4f }).IsIdentity);
    }
}
