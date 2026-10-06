using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins how the render size, the jitter length and the mip bias are decided together from one plan.
/// </summary>
/// <remarks>
/// The DLSS figures are NVIDIA's own (DLSS Programming Guide §3.5 and §3.7.1.1): eight phases times the
/// area ratio, and a mip bias of the log of the ratio less one. The engine's figures are what the renderer
/// has always used, so that turning nothing on changes nothing.
/// </remarks>
public sealed class RenderResolutionPolicyTests
{
    [Fact]
    public void Engine_follows_the_render_scale_and_keeps_sixteen_phases()
    {
        RenderResolution resolution = RenderResolutionPolicy.Choose(1920, 1080, 0.5f, temporal: true, ReconstructionPlan.Engine());

        Assert.Equal(960, resolution.RenderWidth);
        Assert.Equal(540, resolution.RenderHeight);
        Assert.Equal(1920, resolution.OutputWidth);
        Assert.Equal(1080, resolution.OutputHeight);
        Assert.Equal(16, resolution.JitterPhases);
    }

    [Fact]
    public void Engine_at_native_does_not_ask_for_an_output_size()
    {
        RenderResolution resolution = RenderResolutionPolicy.Choose(1920, 1080, 1f, temporal: true, ReconstructionPlan.Engine());

        Assert.Equal(0, resolution.OutputWidth);
        Assert.Equal(0, resolution.OutputHeight);
    }

    [Theory]
    [InlineData(1920, 1080, 8, -1f)]      // DLAA
    [InlineData(1280, 720, 18, -1.585f)]   // Quality, 1.5x
    [InlineData(960, 540, 32, -2f)]        // Performance, 2x
    [InlineData(640, 360, 72, -2.585f)]    // Ultra performance, 3x
    public void Dlss_uses_nvidias_phase_count_and_mip_bias(int renderWidth, int renderHeight, int phases, float bias)
    {
        ReconstructionPlan plan = new(ReconstructionKind.Dlss, renderWidth, renderHeight, string.Empty);

        RenderResolution resolution = RenderResolutionPolicy.Choose(1920, 1080, 0.7f, temporal: true, plan);

        Assert.Equal(renderWidth, resolution.RenderWidth);
        Assert.Equal(renderHeight, resolution.RenderHeight);
        Assert.Equal(1920, resolution.OutputWidth);
        Assert.Equal(1080, resolution.OutputHeight);
        Assert.Equal(phases, resolution.JitterPhases);
        Assert.Equal(bias, resolution.MipBias, 0.01f);
    }

    [Fact]
    public void Ray_reconstruction_never_has_fewer_than_thirty_two_phases()
    {
        ReconstructionPlan plan = new(ReconstructionKind.RayReconstruction, 1920, 1080, string.Empty);

        RenderResolution resolution = RenderResolutionPolicy.Choose(1920, 1080, 1f, temporal: true, plan);

        Assert.Equal(32, resolution.JitterPhases);
    }

    [Fact]
    public void Without_temporal_reconstruction_the_plan_is_ignored()
    {
        ReconstructionPlan plan = new(ReconstructionKind.Dlss, 960, 540, string.Empty);

        RenderResolution resolution = RenderResolutionPolicy.Choose(1920, 1080, 1f, temporal: false, plan);

        Assert.Equal(1920, resolution.RenderWidth);
        Assert.Equal(0, resolution.OutputWidth);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(72)]
    public void A_longer_jitter_sequence_stays_within_half_a_pixel_and_repeats_at_its_length(int phases)
    {
        HashSet<(float, float)> seen = [];
        for (int frame = 0; frame < phases; frame++)
        {
            (float x, float y) = TemporalJitter.At(frame, phases);
            Assert.InRange(x, -0.5f, 0.5f);
            Assert.InRange(y, -0.5f, 0.5f);
            Assert.True(seen.Add((x, y)), $"phase {frame} repeated before the sequence's length");
        }

        Assert.Equal(TemporalJitter.At(0, phases), TemporalJitter.At(phases, phases));
    }

    [Fact]
    public void The_default_jitter_is_the_sixteen_phase_sequence()
    {
        for (int frame = 0; frame < 40; frame++)
        {
            Assert.Equal(TemporalJitter.At(frame, TemporalJitter.Length), TemporalJitter.At(frame));
        }
    }
}
