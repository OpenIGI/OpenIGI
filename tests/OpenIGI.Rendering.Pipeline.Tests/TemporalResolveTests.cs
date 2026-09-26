using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;
using OpenIGI.TestSupport.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the temporal chain runs in the order it has to, over the targets it has to.
/// </summary>
/// <remarks>
/// The order is not a preference: every pass reads what the one before it wrote. And the two that read
/// the previous frame have to read the OTHER half of a history pair, not what this frame has already
/// put there — which is the mistake that turns a temporal filter into a feedback loop.
/// </remarks>
public sealed class TemporalResolveTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_pass_of_the_chain_runs_once()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        Record(device, resolve, targets);

        IReadOnlyList<RecordedRhiCommand.Dispatch> dispatches =
            device.CommandsOf<RecordedRhiCommand.Dispatch>();

        output.WriteLine($"{dispatches.Count} dispatch(es)");

        Assert.Equal(8, dispatches.Count);
        Assert.Equal(8, device.CommandsOf<RecordedRhiCommand.SetPipeline>().Count);
    }

    [Fact]
    public void The_occluder_scatter_is_emptied_before_the_dilation_builds_it()
    {
        // The dilation builds it with an atomic. Left as the last frame had it, every frame's scatter took
        // in every frame before, the decimation found an occluder in front of every history texel, and the
        // resolve passed the raw frame straight through -- its parallax view came out fully inverted.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        Record(device, resolve, targets);

        IReadOnlyList<RecordedRhiCommand> commands = device.Commands;
        int cleared = commands
            .Select((command, index) => (command, index))
            .Where(entry => entry.command is RecordedRhiCommand.WriteBuffer clear && clear.Label == "ClosestOccluder")
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        int dilated = commands
            .Select((command, index) => (command, index))
            .Where(entry => entry.command is RecordedRhiCommand.SetPipeline set
                && set.Label.Contains("DilateVelocity", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();

        Assert.True(cleared >= 0, "The occluder scatter was never cleared.");
        Assert.True(dilated > cleared, "The dilation ran before the occluder scatter was cleared.");
    }

    [Fact]
    public void The_history_update_walks_the_history_rather_than_the_frame()
    {
        // The history is twice the frame in each axis, so a dispatch sized from the frame would leave
        // three quarters of it unwritten.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        Record(device, resolve, targets);

        IReadOnlyList<RecordedRhiCommand.Dispatch> dispatches =
            device.CommandsOf<RecordedRhiCommand.Dispatch>();

        // Eight-wide workgroups: the frame is eight groups across, the history sixteen.
        // Counted from the end, which is where the update and the resolve always are.
        Assert.Equal(new RecordedRhiCommand.Dispatch(16, 16, 1), dispatches[^2]);
        Assert.Equal(new RecordedRhiCommand.Dispatch(8, 8, 1), dispatches[^1]);
    }

    [Fact]
    public void The_passes_that_read_last_frame_read_the_other_half_of_the_pair()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        string written = targets.Current(SceneTargetId.ResolveHistoryColour).Label;
        string read = targets.Previous(SceneTargetId.ResolveHistoryColour).Label;

        Assert.NotEqual(written, read);

        Record(device, resolve, targets);

        // The update binds both halves: the one it reads and the one it writes.
        RecordedBindGroup update = device.Resources
            .OfType<RecordedBindGroup>()
            .Single(group => group.Label.StartsWith("upscale.update", StringComparison.Ordinal));

        List<string> bound =
        [
            .. update.Entries
                .Where(entry => entry.TextureView is not null)
                .Select(entry => entry.TextureView!.Label),
        ];

        Assert.Contains(written, bound);
        Assert.Contains(read, bound);
    }

    [Fact]
    public void A_second_frame_binds_the_pair_the_other_way_round()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        Record(device, resolve, targets);

        int built = device.Resources.OfType<RecordedBindGroup>().Count();

        targets.AdvanceFrame();
        device.ClearCommands();

        Record(device, resolve, targets, frameInFlight: 1);

        // Every group naming a history pair has to be rebuilt; one that was not would read this frame's
        // own output as the history.
        Assert.True(
            device.Resources.OfType<RecordedBindGroup>().Count() > built,
            "No group was rebuilt, so the pairs did not change places.");
    }

    [Fact]
    public void Each_frame_in_flight_writes_its_own_parameters()
    {
        // A single buffer would give the older frame the newer frame's camera, which reprojects
        // everything to the wrong place for exactly one frame and reads as a flicker.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);
        using TemporalResolve resolve = new(device, framesInFlight: 2);

        Record(device, resolve, targets, frameInFlight: 0);
        Record(device, resolve, targets, frameInFlight: 1);

        List<string> written =
        [
            .. device.CommandsOf<RecordedRhiCommand.WriteBuffer>()
                .Select(write => write.Label)
                .Where(label => label.StartsWith("upscale.reprojection", StringComparison.Ordinal))
                .Distinct(),
        ];

        Assert.Equal(2, written.Count);
    }

    /// <summary>Runs the chain once, the way a frame runs it.</summary>
    private static void Record(
        RecordingRhiDevice device,
        TemporalResolve resolve,
        SceneTargetSet targets,
        int frameInFlight = 0)
    {
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");
        resolve.Record(encoder, targets, State(targets), TemporalResolveSettings.Default, frameInFlight);
        device.Submit([encoder.Finish()]);
    }

    private static TemporalFrameState State(SceneTargetSet targets) => new(
        Matrix3f.Identity,
        Matrix3f.Identity,
        Vector3f.Zero,
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        JitterX: 0f,
        JitterY: 0f,
        PreviousJitterX: 0f,
        PreviousJitterY: 0f,
        targets.Width,
        targets.Height);
}
