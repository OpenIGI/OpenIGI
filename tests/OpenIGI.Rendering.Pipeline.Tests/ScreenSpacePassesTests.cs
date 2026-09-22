using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the motion field and the occlusion walk what they are supposed to walk.
/// </summary>
public sealed class ScreenSpacePassesTests
{
    [Fact]
    public void The_motion_field_is_computed_over_the_whole_frame()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using ScreenSpacePasses passes = new(device, framesInFlight: 2);

        Record(device, pass => passes.RecordVelocity(pass, targets, State(targets), 0));

        Assert.Equal(
            new RecordedRhiCommand.Dispatch(8, 8, 1),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.Dispatch>()));
    }

    [Fact]
    public void The_occlusion_walks_its_own_half_size_target_rather_than_the_frame()
    {
        // Dispatching over the frame would run four invocations for every texel there is to write.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using ScreenSpacePasses passes = new(device, framesInFlight: 2);

        Record(device, pass => passes.RecordOcclusion(
            pass,
            targets,
            State(targets),
            new AmbientOcclusionSettings(),
            frameIndex: 0,
            frameInFlight: 0));

        Assert.Equal(32, targets[SceneTargetId.Occlusion].Width);

        Assert.Equal(
            new RecordedRhiCommand.Dispatch(4, 4, 1),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.Dispatch>()));
    }

    [Fact]
    public void The_occlusion_reads_the_previous_frames_and_writes_this_one()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using ScreenSpacePasses passes = new(device, framesInFlight: 2);

        string written = targets.Current(SceneTargetId.Occlusion).Label;
        string read = targets.Previous(SceneTargetId.Occlusion).Label;

        Assert.NotEqual(written, read);

        Record(device, pass => passes.RecordOcclusion(
            pass,
            targets,
            State(targets),
            new AmbientOcclusionSettings(),
            frameIndex: 0,
            frameInFlight: 0));

        RecordedBindGroup group = device.Resources
            .OfType<RecordedBindGroup>()
            .Single(g => g.Label.StartsWith("occlusion", StringComparison.Ordinal));

        List<string> bound =
        [
            .. group.Entries
                .Where(entry => entry.TextureView is not null)
                .Select(entry => entry.TextureView!.Label),
        ];

        Assert.Contains(written, bound);
        Assert.Contains(read, bound);
    }

    [Fact]
    public void The_noise_advances_with_the_frame_and_wraps()
    {
        // A short cycle shows as a standing pattern in the shadow; an unbounded one loses its precision.
        Span<float> first = stackalloc float[AmbientOcclusionParameters.Floats];
        Span<float> wrapped = stackalloc float[AmbientOcclusionParameters.Floats];

        Write(first, 1);
        Write(wrapped, AmbientOcclusionParameters.NoiseCycle + 1);

        Assert.Equal(1f, first[51]);
        Assert.Equal(first[51], wrapped[51]);
    }

    [Fact]
    public void A_frame_with_no_history_says_so()
    {
        // The first frame after a load: what is in that texture belongs to a different scene.
        Span<float> block = stackalloc float[AmbientOcclusionParameters.Floats];

        Write(block, 0, hasHistory: false);
        Assert.Equal(0f, block[52]);

        Write(block, 0, hasHistory: true);
        Assert.Equal(1f, block[52]);
    }

    [Fact]
    public void A_block_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            float[] block = new float[8];

            AmbientOcclusionParameters.Write(
                block,
                State(),
                new AmbientOcclusionSettings(),
                32,
                32,
                0,
                hasHistory: true);
        });
    }

    private static void Write(Span<float> block, long frameIndex, bool hasHistory = true) =>
        AmbientOcclusionParameters.Write(
            block,
            State(),
            new AmbientOcclusionSettings(),
            32,
            32,
            frameIndex,
            hasHistory);

    private static void Record(RecordingRhiDevice device, Action<IRhiComputePassEncoder> run)
    {
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass())
        {
            run(pass);
        }

        device.Submit([encoder.Finish()]);
    }

    private static TemporalFrameState State(SceneTargetSet? targets = null) => new(
        Matrix3f.Identity,
        Matrix3f.Identity,
        Vector3f.Zero,
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        JitterX: 0f,
        JitterY: 0f,
        PreviousJitterX: 0f,
        PreviousJitterY: 0f,
        targets?.Width ?? 64,
        targets?.Height ?? 64);
}
