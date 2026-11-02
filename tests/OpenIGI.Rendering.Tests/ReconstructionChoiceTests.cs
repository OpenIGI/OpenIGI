using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// One switch turns DLSS on: its mode, where off is the engine's own upscaler. Ray reconstruction is a switch of its own
/// that runs inside DLSS, and the old single choice still means what it did when an old settings file sets it.
/// </summary>
[Collection(nameof(ReconstructionChoiceTests))]
public sealed class ReconstructionChoiceTests : IDisposable
{
    public ReconstructionChoiceTests() => ResetAll();

    public void Dispose() => ResetAll();

    [Fact]
    public void Dlss_is_off_until_a_mode_is_chosen()
    {
        Assert.Equal(ReconstructionKind.Engine, ReconstructionCVars.Effective);
    }

    [Fact]
    public void Choosing_a_mode_turns_dlss_on_without_ray_reconstruction()
    {
        ReconstructionCVars.DlssQualityMode.Value = DlssQuality.UltraPerformance;

        Assert.Equal(ReconstructionKind.Dlss, ReconstructionCVars.Effective);
    }

    [Fact]
    public void Ray_reconstruction_runs_inside_dlss()
    {
        ReconstructionCVars.DlssQualityMode.Value = DlssQuality.Performance;
        ReconstructionCVars.RayReconstruction.Value = true;

        Assert.Equal(ReconstructionKind.RayReconstruction, ReconstructionCVars.Effective);
    }

    [Fact]
    public void Ray_reconstruction_does_nothing_with_dlss_off()
    {
        ReconstructionCVars.RayReconstruction.Value = true;

        Assert.Equal(ReconstructionKind.Engine, ReconstructionCVars.Effective);
    }

    [Fact]
    public void An_old_ray_reconstruction_setting_becomes_dlss_quality_with_ray_reconstruction()
    {
        Assert.True(ReconstructionCVars.Reconstruction.TrySetText("rayreconstruction", out _));

        Assert.Equal(DlssQuality.Quality, ReconstructionCVars.DlssQualityMode.Value);
        Assert.True(ReconstructionCVars.RayReconstruction.Value);
        Assert.Equal(ReconstructionKind.RayReconstruction, ReconstructionCVars.Effective);
    }

    [Fact]
    public void An_old_dlss_setting_keeps_the_mode_already_chosen()
    {
        ReconstructionCVars.DlssQualityMode.Value = DlssQuality.Balanced;

        Assert.True(ReconstructionCVars.Reconstruction.TrySetText("dlss", out _));

        Assert.Equal(DlssQuality.Balanced, ReconstructionCVars.DlssQualityMode.Value);
        Assert.Equal(ReconstructionKind.Dlss, ReconstructionCVars.Effective);
    }

    [Fact]
    public void The_old_engine_choice_turns_dlss_off()
    {
        ReconstructionCVars.Reconstruction.Value = ReconstructionKind.Dlss;
        Assert.True(ReconstructionCVars.Reconstruction.TrySetText("engine", out _));

        Assert.Equal(DlssQuality.Off, ReconstructionCVars.DlssQualityMode.Value);
        Assert.Equal(ReconstructionKind.Engine, ReconstructionCVars.Effective);
    }

    private static void ResetAll()
    {
        ReconstructionCVars.Reconstruction.Reset();
        ReconstructionCVars.DlssQualityMode.Reset();
        ReconstructionCVars.RayReconstruction.Reset();
    }
}
