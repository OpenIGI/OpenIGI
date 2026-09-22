using OpenIGI.Rendering.Pipeline.Diagnostics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the profiler hands out the timestamp slots it says it does, and never another frame's.
/// </summary>
public sealed class StageProfilerTests
{
    private static RhiFeatures CanTime => new(
        RayQuery: false,
        TimestampQuery: true,
        Float32Filterable: false,
        DepthClamp: false,
        IndirectFirstInstance: false,
        TextureCompressionBc: false);

    [Fact]
    public void A_device_that_cannot_time_builds_nothing_and_says_so()
    {
        // Every member then does nothing, so nothing above has to ask before timing.
        using RecordingRhiDevice device = new();
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        Assert.False(profiler.Available);
        Assert.Empty(device.Resources.OfType<RecordedQuerySet>());

        profiler.BeginFrame(0);
        Assert.Null(profiler.Time(RenderStage.Lighting));
    }

    [Fact]
    public void Nothing_is_timed_until_timing_is_switched_on()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2);

        Assert.True(profiler.Available);

        profiler.BeginFrame(0);
        Assert.Null(profiler.Time(RenderStage.Lighting));

        profiler.Enabled = true;
        Assert.NotNull(profiler.Time(RenderStage.Lighting));
    }

    [Fact]
    public void Each_timed_pass_takes_the_next_pair_of_slots()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(0);

        RhiTimestampWrites first = profiler.Time(RenderStage.Shadows)!.Value;
        RhiTimestampWrites second = profiler.Time(RenderStage.Lighting)!.Value;

        Assert.Equal(0, first.BeginIndex);
        Assert.Equal(1, first.EndIndex);
        Assert.Equal(2, second.BeginIndex);
        Assert.Equal(3, second.EndIndex);
    }

    [Fact]
    public void Each_frame_in_flight_writes_into_its_own_block()
    {
        // Two frames sharing a block would report one frame's work as the other's.
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(0);
        RhiTimestampWrites first = profiler.Time(RenderStage.Lighting)!.Value;

        profiler.BeginFrame(1);
        RhiTimestampWrites second = profiler.Time(RenderStage.Lighting)!.Value;

        Assert.Equal(0, first.BeginIndex);
        Assert.Equal(StageProfiler.SlotCapacity, second.BeginIndex);
    }

    [Fact]
    public void A_frame_that_wants_more_slots_than_its_block_holds_stops_timing()
    {
        // Rather than writing into the next slot's block, which would report another frame's work.
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(0);

        for (int i = 0; i < StageProfiler.SlotCapacity / 2; i++)
        {
            Assert.NotNull(profiler.Time(RenderStage.Lighting));
        }

        Assert.Null(profiler.Time(RenderStage.Lighting));
    }

    [Fact]
    public void A_frame_starts_its_block_again_rather_than_continuing_it()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(0);
        profiler.Time(RenderStage.Lighting);

        profiler.BeginFrame(0);

        Assert.Equal(0, profiler.Time(RenderStage.Lighting)!.Value.BeginIndex);
    }

    [Fact]
    public void Resolving_copies_only_what_the_frame_wrote()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(1);
        profiler.Time(RenderStage.Shadows);
        profiler.Time(RenderStage.Lighting);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");
        profiler.ResolveFrame(encoder);
        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.CopyBufferToBuffer copy =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.CopyBufferToBuffer>(), c => c.Source == "stages");

        // Four timestamps for two passes.
        Assert.Equal(4UL * sizeof(ulong), copy.SizeInBytes);
    }

    [Fact]
    public void A_frame_that_timed_nothing_resolves_nothing()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.BeginFrame(0);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");
        profiler.ResolveFrame(encoder);
        device.Submit([encoder.Finish()]);

        Assert.Empty(device.CommandsOf<RecordedRhiCommand.CopyBufferToBuffer>());
    }

    [Fact]
    public void Switching_timing_off_and_on_forgets_the_history()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 2) { Enabled = true };

        profiler.Ledger.Observe(RenderStage.Lighting, 9.0);
        profiler.Ledger.CompleteFrame();

        profiler.Enabled = false;

        Assert.Equal(0, profiler.Ledger.CompletedFrames);
    }

    [Fact]
    public void The_query_set_is_big_enough_for_every_frame_in_flight()
    {
        using RecordingRhiDevice device = new(CanTime);
        using StageProfiler profiler = new(device, framesInFlight: 3);

        RecordedQuerySet queries = Assert.Single(device.Resources.OfType<RecordedQuerySet>());

        Assert.Equal(RhiQueryType.Timestamp, queries.Type);
        Assert.Equal(StageProfiler.SlotCapacity * 3, queries.Count);
    }
}
