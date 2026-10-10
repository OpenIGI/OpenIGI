using OpenIGI.Rendering.D3D12.Sync;
using Silk.NET.Direct3D12;
using Xunit;

namespace OpenIGI.Rendering.D3D12.Tests;

/// <summary>
/// That the barrier tracker puts a barrier exactly where a use makes one due, and none elsewhere.
/// </summary>
/// <remarks>
/// A missing barrier is a race that shows up as a flicker on one vendor's hardware; a needless one is a GPU
/// stall nobody sees in a picture. Neither is caught by drawing a frame, so the decisions are checked here,
/// with no device: the tracker decides from uses alone.
/// </remarks>
public sealed class BarrierTrackerTests
{
    private readonly object _texture = new();
    private readonly object _buffer = new();

    [Fact]
    public void A_first_use_needs_no_barrier_and_hands_its_layout_to_the_submission()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.Sampled);
        tracker.UseBuffer(_buffer, 2, D3D12Use.StorageWrite);

        Assert.Empty(tracker.QueuedTextureBarriers);
        Assert.Empty(tracker.QueuedBufferBarriers);

        D3D12LayoutHandOff handOff = Assert.Single(tracker.HandOffs());
        Assert.Equal(BarrierLayout.DirectQueueShaderResource, handOff.First);
        Assert.Equal(BarrierLayout.DirectQueueShaderResource, handOff.Last);
    }

    [Fact]
    public void Reads_after_reads_need_nothing()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.Sampled);
        tracker.UseTexture(_texture, 1, D3D12Use.Sampled);
        tracker.UseBuffer(_buffer, 2, D3D12Use.Constant);
        tracker.UseBuffer(_buffer, 2, D3D12Use.Sampled);

        Assert.Empty(tracker.QueuedTextureBarriers);
        Assert.Empty(tracker.QueuedBufferBarriers);
    }

    [Fact]
    public void Target_writes_follow_each_other_with_no_barrier()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.RenderTarget);
        tracker.UseTexture(_texture, 1, D3D12Use.RenderTarget);
        tracker.UseTexture(_buffer, 2, D3D12Use.DepthWrite);
        tracker.UseTexture(_buffer, 2, D3D12Use.DepthWrite);

        Assert.Empty(tracker.QueuedTextureBarriers);
    }

    [Fact]
    public void Sampling_what_was_drawn_moves_it_out_of_the_target_layout()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.RenderTarget);
        tracker.UseTexture(_texture, 1, D3D12Use.Sampled);

        TextureBarrier barrier = Assert.Single(tracker.QueuedTextureBarriers);
        Assert.Equal(BarrierLayout.RenderTarget, barrier.LayoutBefore);
        Assert.Equal(BarrierLayout.DirectQueueShaderResource, barrier.LayoutAfter);
        Assert.Equal(BarrierSync.RenderTarget, barrier.SyncBefore);
        Assert.Equal(BarrierAccess.RenderTarget, barrier.AccessBefore);
        Assert.Equal(BarrierAccess.ShaderResource, barrier.AccessAfter);

        // The list leaves it where its last use put it, and needed it first as a target.
        D3D12LayoutHandOff handOff = Assert.Single(tracker.HandOffs());
        Assert.Equal(BarrierLayout.RenderTarget, handOff.First);
        Assert.Equal(BarrierLayout.DirectQueueShaderResource, handOff.Last);
    }

    [Fact]
    public void A_dispatch_reading_what_the_last_one_stored_waits_once()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseBuffer(_buffer, 2, D3D12Use.StorageWrite);
        tracker.UseBuffer(_buffer, 2, D3D12Use.Sampled);
        tracker.UseBuffer(_buffer, 2, D3D12Use.Sampled);

        BufferBarrier barrier = Assert.Single(tracker.QueuedBufferBarriers);
        Assert.Equal(BarrierAccess.UnorderedAccess, barrier.AccessBefore);
        Assert.Equal(BarrierAccess.ShaderResource, barrier.AccessAfter);
    }

    [Fact]
    public void Stores_after_stores_and_after_reads_wait()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseBuffer(_buffer, 2, D3D12Use.StorageWrite);
        tracker.UseBuffer(_buffer, 2, D3D12Use.StorageWrite);
        Assert.Single(tracker.QueuedBufferBarriers);

        object other = new();
        tracker.Discard();
        tracker.UseBuffer(other, 3, D3D12Use.Sampled);
        tracker.UseBuffer(other, 3, D3D12Use.StorageWrite);
        BufferBarrier barrier = Assert.Single(tracker.QueuedBufferBarriers);

        // A write after a read waits for the read, which it names: a barrier may not wait for work it says
        // made no access.
        Assert.Equal(BarrierSync.AllShading, barrier.SyncBefore);
        Assert.Equal(BarrierAccess.ShaderResource, barrier.AccessBefore);
    }

    [Fact]
    public void A_storage_texture_stored_twice_keeps_its_layout_and_waits_between()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.StorageWrite);
        tracker.UseTexture(_texture, 1, D3D12Use.StorageWrite);

        TextureBarrier barrier = Assert.Single(tracker.QueuedTextureBarriers);
        Assert.Equal(barrier.LayoutBefore, barrier.LayoutAfter);
        Assert.Equal(BarrierLayout.DirectQueueUnorderedAccess, barrier.LayoutAfter);
    }

    [Fact]
    public void Read_only_depth_can_be_sampled_in_the_same_pass()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.DepthWrite);
        tracker.UseTexture(_texture, 1, D3D12Use.DepthRead);
        tracker.Discard();

        // Once in the read-only layout, sampling it as well changes nothing a bound depth target needs.
        tracker.UseTexture(_texture, 1, D3D12Use.Sampled);
        Assert.Empty(tracker.QueuedTextureBarriers);
    }

    [Fact]
    public void A_covering_barrier_clears_every_outstanding_write()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseBuffer(_buffer, 2, D3D12Use.StorageWrite);
        tracker.Covered();
        tracker.UseBuffer(_buffer, 2, D3D12Use.Sampled);

        Assert.Empty(tracker.QueuedBufferBarriers);
    }

    [Fact]
    public void The_present_moves_the_buffer_into_plain_common()
    {
        D3D12BarrierTracker tracker = new();
        tracker.UseTexture(_texture, 1, D3D12Use.RenderTarget);
        tracker.UseTexture(_texture, 1, D3D12Use.Present);

        TextureBarrier barrier = Assert.Single(tracker.QueuedTextureBarriers);
        Assert.Equal(BarrierLayout.Common, barrier.LayoutAfter);
        Assert.Equal(BarrierSync.None, barrier.SyncAfter);
        Assert.Equal(BarrierAccess.NoAccess, barrier.AccessAfter);
    }

    [Theory]
    [InlineData(BarrierLayout.DirectQueueCommon, BarrierAccess.UnorderedAccess, true)]
    [InlineData(BarrierLayout.Common, BarrierAccess.UnorderedAccess, false)]
    [InlineData(BarrierLayout.DirectQueueGenericRead, BarrierAccess.DepthStencilRead, true)]
    [InlineData(BarrierLayout.DirectQueueGenericRead, BarrierAccess.ShaderResource, true)]
    [InlineData(BarrierLayout.DirectQueueShaderResource, BarrierAccess.CopySource, false)]
    [InlineData(BarrierLayout.RenderTarget, BarrierAccess.ShaderResource, false)]
    [InlineData(BarrierLayout.DepthStencilWrite, BarrierAccess.DepthStencilRead, true)]
    public void Layouts_allow_the_accesses_the_specification_gives_them(BarrierLayout layout, BarrierAccess access, bool allowed) =>
        Assert.Equal(allowed, D3D12BarrierTracker.Allows(layout, access));
}
