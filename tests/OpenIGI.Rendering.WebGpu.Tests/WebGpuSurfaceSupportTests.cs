using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Choosing a format and a way of waiting for the display, from what an adapter offers.
/// </summary>
/// <remarks>
/// Both choices are made from a list that differs by machine, so they are tested against lists written
/// here rather than against whatever this one reports. The interesting cases are the adapters that offer
/// less: one with no tear-free mode, one that offers only what WebGPU requires, one that names a format
/// this renderer has never heard of.
/// </remarks>
public sealed class WebGpuSurfaceSupportTests
{
    [Fact]
    public void Waiting_for_the_display_is_always_fifo()
    {
        // The one mode every WebGPU implementation must offer, so it is what waiting means whatever else
        // is on the list.
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8Unorm],
            [WGPUPresentMode.Fifo, WGPUPresentMode.Immediate, WGPUPresentMode.Mailbox],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(WGPUPresentMode.Fifo, support.PresentMode(vsync: true));
    }

    [Fact]
    public void Not_waiting_asks_for_immediate()
    {
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8Unorm],
            [WGPUPresentMode.Fifo, WGPUPresentMode.Immediate, WGPUPresentMode.Mailbox],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(WGPUPresentMode.Immediate, support.PresentMode(vsync: false));
    }

    [Fact]
    public void Not_waiting_settles_for_mailbox_where_there_is_no_immediate()
    {
        // Mailbox tears less and is the better of the two, so an adapter with only it still honours a
        // request not to wait rather than quietly waiting anyway.
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8Unorm],
            [WGPUPresentMode.Fifo, WGPUPresentMode.Mailbox],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(WGPUPresentMode.Mailbox, support.PresentMode(vsync: false));
    }

    [Fact]
    public void An_adapter_with_only_fifo_waits_whatever_was_asked()
    {
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8Unorm],
            [WGPUPresentMode.Fifo],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(WGPUPresentMode.Fifo, support.PresentMode(vsync: false));
        Assert.Equal(WGPUPresentMode.Fifo, support.PresentMode(vsync: true));
    }

    [Fact]
    public void The_format_is_the_adapters_own_first_choice()
    {
        // Its first choice is the one its composition path is cheapest for, and taking it keeps the
        // present free of a conversion nobody asked for.
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8Unorm, RhiTextureFormat.Rgba8Unorm],
            [WGPUPresentMode.Fifo],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(RhiTextureFormat.Bgra8Unorm, support.PreferredFormat);
    }

    [Fact]
    public void A_format_that_stores_what_it_is_given_is_preferred_to_an_encoding_one()
    {
        // The interface blends at the display encoding, as the original did; an sRGB target blends in
        // linear space and turns a dark panel over the scene grey.
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8UnormSrgb, RhiTextureFormat.Bgra8Unorm],
            [WGPUPresentMode.Fifo],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(RhiTextureFormat.Bgra8Unorm, support.PreferredFormat);
    }

    [Fact]
    public void An_adapter_offering_only_encoding_formats_is_still_presented_to()
    {
        WebGpuSurfaceSupport support = new(
            [RhiTextureFormat.Bgra8UnormSrgb],
            [WGPUPresentMode.Fifo],
            WGPUCompositeAlphaMode.Opaque);

        Assert.Equal(RhiTextureFormat.Bgra8UnormSrgb, support.PreferredFormat);
    }

    [Fact]
    public void An_adapter_offering_nothing_this_renderer_knows_has_no_preferred_format()
    {
        // Not a crash and not a guess: the formats the surface reported were all ones this has no name
        // for, so there is nothing to present in and the configure says so.
        WebGpuSurfaceSupport support = new([], [WGPUPresentMode.Fifo], WGPUCompositeAlphaMode.Opaque);

        Assert.Null(support.PreferredFormat);
    }

    [Fact]
    public void A_surface_nothing_described_yet_has_no_preference()
    {
        // The default value, which is what a headless device holds. Reaching for a format on one of
        // these has to be a null rather than an index out of range.
        WebGpuSurfaceSupport none = default;

        Assert.Null(none.PreferredFormat);
        Assert.Equal(WGPUPresentMode.Fifo, none.PresentMode(vsync: false));
    }
}
