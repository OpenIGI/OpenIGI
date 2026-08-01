using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins the scene-capture contract: the order passes may be opened and closed in, and what a caller
/// that ignores the order is told.
/// </summary>
/// <remarks>
/// The sequencing matters more than it looks. A capture left open at the end of a frame strands the
/// framebuffer binding and makes the <em>next</em> frame throw, which reads as a bug somewhere else
/// entirely — so the rules are asserted here rather than trusted.
/// </remarks>
public sealed class SceneCaptureTests
{
    private static readonly SceneTargetDescription Target = new(1280, 720);

    [Fact]
    public void AFrameRunsCaptureThenPassesThenResolve()
    {
        RecordingGraphicsDevice device = new();

        Assert.True(device.BeginSceneCapture(Target));
        device.BeginPass(ScenePass.Geometry);
        device.EndPass();
        device.BeginPass(ScenePass.Forward);
        device.EndPass();
        device.EndSceneCapture();
        device.ResolveScene(PostProcessSettings.Identity);

        Assert.Equal([Target], device.SceneCaptures);
        Assert.Equal([ScenePass.Geometry, ScenePass.Forward], device.Passes);
        Assert.Equal(PostProcessSettings.Identity, Assert.Single(device.Resolves));
        Assert.Equal(ScenePass.None, device.CurrentPass);
        Assert.False(device.IsCapturing);
    }

    [Fact]
    public void ABackendWithoutTargetsRefusesAndTheCallerDrawsDirect()
    {
        RecordingGraphicsDevice device = new() { RefuseSceneCapture = true };

        // Refusing is a supported outcome, not an error: the frame is then drawn exactly as the
        // original draws it.
        Assert.False(device.BeginSceneCapture(Target));
        Assert.False(device.IsCapturing);
        Assert.Empty(device.SceneCaptures);
    }

    [Fact]
    public void APassCannotBeOpenedOutsideACapture()
    {
        RecordingGraphicsDevice device = new();

        Assert.Throws<GraphicsDeviceException>(() => device.BeginPass(ScenePass.Geometry));
    }

    [Fact]
    public void PassesCannotNest()
    {
        RecordingGraphicsDevice device = new();
        device.BeginSceneCapture(Target);
        device.BeginPass(ScenePass.Geometry);

        Assert.Throws<GraphicsDeviceException>(() => device.BeginPass(ScenePass.Forward));
    }

    [Fact]
    public void ACaptureCannotBeEndedWithAPassStillOpen()
    {
        RecordingGraphicsDevice device = new();
        device.BeginSceneCapture(Target);
        device.BeginPass(ScenePass.Geometry);

        Assert.Throws<GraphicsDeviceException>(device.EndSceneCapture);
    }

    [Fact]
    public void CapturesCannotNest()
    {
        RecordingGraphicsDevice device = new();
        device.BeginSceneCapture(Target);

        Assert.Throws<GraphicsDeviceException>(() => device.BeginSceneCapture(Target));
    }

    [Fact]
    public void ResolvingBeforeTheCaptureIsEndedIsRefused()
    {
        RecordingGraphicsDevice device = new();
        device.BeginSceneCapture(Target);

        Assert.Throws<GraphicsDeviceException>(() => device.ResolveScene(PostProcessSettings.Identity));
    }

    [Fact]
    public void ResolvingWithNothingCapturedIsRefused()
    {
        RecordingGraphicsDevice device = new();

        Assert.Throws<GraphicsDeviceException>(() => device.ResolveScene(PostProcessSettings.Identity));
    }

    [Fact]
    public void IdentityIsWhatTheVanillaPresetAsksFor()
    {
        // The regression gate for the whole remaster track rests on this being a copy.
        Assert.True(PostProcessSettings.Identity.IsIdentity);
        Assert.False(PostProcessSettings.Identity.WantsBloom);
        Assert.Equal(ToneMapOperator.None, PostProcessSettings.Identity.ToneMap);
        Assert.Equal(1.0f, PostProcessSettings.Identity.Exposure);
    }

    [Theory]
    [InlineData(ToneMapOperator.Neutral, 1.0f, true)]
    [InlineData(ToneMapOperator.None, 2.0f, true)]
    [InlineData(ToneMapOperator.Aces, 2.0f, true)]
    [InlineData(ToneMapOperator.None, 1.0f, false)]
    public void OnlyTheUntouchedSettingsCountAsIdentity(ToneMapOperator toneMap, float exposure, bool changed)
    {
        PostProcessSettings settings = new(toneMap, exposure);

        Assert.Equal(!changed, settings.IsIdentity);
    }

    [Theory]
    [InlineData(1280, 720, 1, true)]
    [InlineData(1280, 720, 4, true)]
    [InlineData(0, 720, 1, false)]
    [InlineData(1280, -1, 1, false)]
    [InlineData(1280, 720, 0, false)]
    public void ATargetIsOnlyValidWhenItNamesUsableDimensions(int width, int height, int samples, bool valid)
    {
        SceneTargetDescription description = new(width, height, samples);

        Assert.Equal(valid, description.IsValid);
        Assert.Equal(samples > 1, description.IsMultisampled);
    }
}
