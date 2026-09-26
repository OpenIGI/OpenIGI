using OpenIGI.Desktop;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Which graphics API a run starts on, and which one the switch key moves to.</summary>
public sealed class RendererSwitchOrderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vulkan")]
    [InlineData("Vulkan")]
    public void Vulkan_is_the_default_and_what_vulkan_asks_for(string? value)
    {
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Parse(value));
        Assert.True(RendererSwitchOrder.IsKnown(value));
    }

    [Theory]
    [InlineData("webgpu")]
    [InlineData("WebGPU")]
    public void Webgpu_asks_for_webgpu(string value)
    {
        Assert.Equal(RendererBackend.WebGpu, RendererSwitchOrder.Parse(value));
        Assert.True(RendererSwitchOrder.IsKnown(value));
    }

    [Fact]
    public void The_old_name_for_this_renderer_over_vulkan_still_means_vulkan()
    {
        // A script from before this renderer was the default set OPENIGI_RENDERER=rhi to reach it.
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Parse("rhi"));
        Assert.True(RendererSwitchOrder.IsKnown("rhi"));
    }

    [Fact]
    public void A_name_for_no_renderer_is_not_known_and_draws_over_vulkan()
    {
        Assert.False(RendererSwitchOrder.IsKnown("opengl"));
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Parse("opengl"));
    }

    [Fact]
    public void The_switch_toggles_between_the_two()
    {
        Assert.Equal(RendererBackend.WebGpu, RendererSwitchOrder.Next(RendererBackend.Vulkan));
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Next(RendererBackend.WebGpu));
    }
}
