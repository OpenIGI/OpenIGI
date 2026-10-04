using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That every target a frame is built out of is the size and format it is supposed to be.
/// </summary>
public sealed class SceneTargetSetTests
{
    [Fact]
    public void Every_target_of_the_layout_is_allocated()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720);

        foreach (SceneTargetSpec spec in SceneTargetSet.Layout)
        {
            IRhiTexture target = targets[spec.Id];

            Assert.Equal(spec.Format, target.Format);
            Assert.Equal(spec.Usage, target.Usage);
        }

        // A history pair is two textures for one target, so the count is what the layout asks for rather
        // than how many entries it has.
        int expected = SceneTargetSet.Layout.Sum(
            spec => spec.History is SceneTargetHistory.PingPong ? 2 : 1);

        Assert.Equal(expected, device.Resources.OfType<RecordedTexture>().Count());
    }

    [Fact]
    public void A_history_pair_changes_places_each_frame()
    {
        // What a temporal filter is: this frame's answer is built from the last one's. One image cannot
        // do it, because a pass reading and writing the same texels sees its own output for half of them.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);

        IRhiTexture written = targets.Current(SceneTargetId.ResolveHistoryColour);
        IRhiTexture read = targets.Previous(SceneTargetId.ResolveHistoryColour);

        Assert.NotSame(written, read);

        targets.AdvanceFrame();

        // What this frame wrote is what the next one reads.
        Assert.Same(written, targets.Previous(SceneTargetId.ResolveHistoryColour));
        Assert.Same(read, targets.Current(SceneTargetId.ResolveHistoryColour));
    }

    [Fact]
    public void Every_pair_turns_over_together()
    {
        // A pass reading two of them would otherwise see one frame's output beside another frame's.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);

        List<IRhiTexture> before =
        [
            .. SceneTargetSet.Layout
                .Where(spec => spec.History is SceneTargetHistory.PingPong)
                .Select(spec => targets.Current(spec.Id)),
        ];

        targets.AdvanceFrame();

        List<IRhiTexture> after =
        [
            .. SceneTargetSet.Layout
                .Where(spec => spec.History is SceneTargetHistory.PingPong)
                .Select(spec => targets.Previous(spec.Id)),
        ];

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_target_with_no_pair_reads_back_what_it_wrote()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);

        Assert.Same(
            targets.Current(SceneTargetId.BaseColour),
            targets.Previous(SceneTargetId.BaseColour));

        targets.AdvanceFrame();

        Assert.Same(
            targets.Current(SceneTargetId.BaseColour),
            targets.Previous(SceneTargetId.BaseColour));
    }

    [Theory]
    [InlineData(SceneTargetId.BaseColour, 1280, 720)]
    [InlineData(SceneTargetId.Depth, 1280, 720)]
    [InlineData(SceneTargetId.Occlusion, 640, 360)]
    [InlineData(SceneTargetId.ResolveHistoryColour, 2560, 1440)]
    public void A_target_takes_its_size_from_the_frame(SceneTargetId id, int width, int height)
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720);

        Assert.Equal(width, targets[id].Width);
        Assert.Equal(height, targets[id].Height);
    }

    [Fact]
    public void A_half_size_target_rounds_up_so_it_still_covers_an_odd_frame()
    {
        // Rounding down leaves the last row and column reading a texel nothing wrote, which at half
        // resolution is a two-pixel band down two edges of the picture.
        Assert.Equal((5, 4), SceneTargetSet.SizeOf(SceneTargetScale.Half, 9, 7));
        Assert.Equal((1, 1), SceneTargetSet.SizeOf(SceneTargetScale.Half, 1, 1));
    }

    [Fact]
    public void No_target_is_ever_smaller_than_a_texel()
    {
        foreach (SceneTargetScale scale in Enum.GetValues<SceneTargetScale>())
        {
            (int width, int height) = SceneTargetSet.SizeOf(scale, 1, 1);

            Assert.True(width >= 1);
            Assert.True(height >= 1);
        }
    }

    [Fact]
    public void A_resize_rebuilds_everything_at_the_new_size()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720);

        RecordedTexture before = (RecordedTexture)targets[SceneTargetId.Lit];

        targets.Resize(1920, 1080);

        Assert.True(before.IsDisposed);
        Assert.Equal(1920, targets[SceneTargetId.Lit].Width);
        Assert.Equal(960, targets[SceneTargetId.Occlusion].Width);
        Assert.Equal(3840, targets[SceneTargetId.ResolveHistoryColour].Width);
    }

    [Fact]
    public void A_resize_retires_the_old_targets_until_the_frames_in_flight_are_done()
    {
        // The frame before the resize may still be running on the GPU and reading every one of them;
        // freeing them at once lost the device whenever a preset changed the render scale.
        using RecordingRhiDevice device = new();
        using RetiredResources retired = new(framesInFlight: 2);
        using SceneTargetSet targets = new(device, 1280, 720, retired: retired);

        RecordedTexture before = (RecordedTexture)targets[SceneTargetId.Lit];

        retired.Frame = 10;
        targets.Resize(1088, 612, 1280, 720);

        Assert.False(before.IsDisposed);

        retired.ReleaseBefore(11);
        Assert.False(before.IsDisposed);

        retired.ReleaseBefore(12);
        Assert.True(before.IsDisposed);
    }

    [Fact]
    public void A_history_at_one_scale_is_the_frame_size_and_a_change_of_scale_rebuilds()
    {
        // At native resolution the doubled history buys nothing it can show, so it may be kept at the
        // frame's own size; switching between the two is a rebuild like any change of size.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720, historyScale: 1);

        Assert.Equal(1, targets.HistoryScale);
        Assert.Equal(1280, targets[SceneTargetId.ResolveHistoryColour].Width);
        Assert.Equal(720, targets[SceneTargetId.ResolveHistoryMetadata].Height);
        Assert.False(targets.Matches(1280, 720, historyScale: 2));

        targets.Resize(1280, 720, historyScale: 2);

        Assert.Equal(2560, targets[SceneTargetId.ResolveHistoryColour].Width);
    }

    [Fact]
    public void Under_a_render_scale_the_resolve_output_is_at_the_presented_size()
    {
        // The temporal resolve reconstructs to the window's size; everything before it runs at the
        // frame's own, and the history stays twice the frame rather than twice the output.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720, 1920, 1080);

        Assert.Equal((1920, 1080), (targets.OutputWidth, targets.OutputHeight));
        Assert.Equal(1920, targets[SceneTargetId.ResolveOutput].Width);
        Assert.Equal(1080, targets[SceneTargetId.ResolveOutput].Height);
        Assert.Equal(1280, targets[SceneTargetId.Lit].Width);
        Assert.Equal(2560, targets[SceneTargetId.ResolveHistoryColour].Width);
    }

    [Fact]
    public void A_change_of_output_size_alone_rebuilds_the_set()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720, 1920, 1080);

        Assert.True(targets.Matches(1280, 720, 1920, 1080));
        Assert.False(targets.Matches(1280, 720, 2560, 1440));

        targets.Resize(1280, 720, 2560, 1440);

        Assert.Equal(2560, targets[SceneTargetId.ResolveOutput].Width);
    }

    [Fact]
    public void Without_an_output_size_the_output_is_the_frame()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720);

        Assert.Equal((1280, 720), (targets.OutputWidth, targets.OutputHeight));
        Assert.Equal(1280, targets[SceneTargetId.ResolveOutput].Width);
    }

    [Fact]
    public void A_resize_to_the_size_it_already_is_changes_nothing()
    {
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 1280, 720);

        IRhiTexture before = targets[SceneTargetId.Lit];

        targets.Resize(1280, 720);

        Assert.Same(before, targets[SceneTargetId.Lit]);
    }

    [Fact]
    public void Every_target_goes_when_the_set_does()
    {
        RecordingRhiDevice device = new();
        SceneTargetSet targets = new(device, 320, 240);

        List<RecordedTexture> textures = [.. device.Resources.OfType<RecordedTexture>()];

        targets.Dispose();

        Assert.All(textures, texture => Assert.True(texture.IsDisposed));

        device.Dispose();
    }

    [Fact]
    public void The_geometry_targets_can_be_attached_and_the_computed_ones_cannot()
    {
        // The difference is not decoration: a texture the driver was not told would be an attachment
        // cannot be one, and the failure arrives when the pass begins rather than when it was created.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);

        foreach (SceneTargetId id in (ReadOnlySpan<SceneTargetId>)
            [SceneTargetId.BaseColour, SceneTargetId.Normal, SceneTargetId.Material, SceneTargetId.Baked, SceneTargetId.Depth])
        {
            Assert.True(targets[id].Usage.HasFlag(RhiTextureUsage.RenderTarget));
        }

        foreach (SceneTargetId id in (ReadOnlySpan<SceneTargetId>)
            [SceneTargetId.Occlusion, SceneTargetId.DilatedVelocity, SceneTargetId.ResolveOutput])
        {
            Assert.True(targets[id].Usage.HasFlag(RhiTextureUsage.Storage));
        }
    }

    [Fact]
    public void The_velocity_target_is_both_written_by_a_draw_and_by_a_dispatch()
    {
        // The field is computed, and then each animated mesh's own motion is rasterised over it.
        using RecordingRhiDevice device = new();
        using SceneTargetSet targets = new(device, 64, 64);

        RhiTextureUsage usage = targets[SceneTargetId.Velocity].Usage;

        Assert.True(usage.HasFlag(RhiTextureUsage.RenderTarget));
        Assert.True(usage.HasFlag(RhiTextureUsage.Storage));
    }

    [Fact]
    public void A_frame_of_no_size_is_refused()
    {
        using RecordingRhiDevice device = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneTargetSet(device, 0, 720));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneTargetSet(device, 1280, 0));
    }

    [Fact]
    public void The_layout_names_each_target_once()
    {
        Assert.Equal(
            SceneTargetSet.Layout.Count,
            SceneTargetSet.Layout.Select(spec => spec.Id).Distinct().Count());
    }
}
