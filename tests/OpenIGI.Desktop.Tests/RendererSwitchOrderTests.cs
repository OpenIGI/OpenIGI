using OpenIGI.Desktop;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Which graphics API a run starts on, and which one the switch key moves to.</summary>
public sealed class RendererSwitchOrderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_named_starts_on_the_platform_default(string? value)
    {
        Assert.Equal(RendererSwitchOrder.Default, RendererSwitchOrder.Parse(value));
        Assert.True(RendererSwitchOrder.IsKnown(value));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("Auto")]
    public void Auto_is_the_platform_default(string value)
    {
        Assert.Equal(RendererSwitchOrder.Default, RendererSwitchOrder.Parse(value));
        Assert.True(RendererSwitchOrder.IsKnown(value));
        Assert.Equal(RendererSwitchOrder.Default, RendererBackends.Resolve(RendererBackend.Auto));
    }

    [Fact]
    public void Direct3d12_is_the_default_on_windows_and_vulkan_elsewhere() =>
        Assert.Equal(
            OperatingSystem.IsWindows() ? RendererBackend.D3D12 : RendererBackend.Vulkan,
            RendererSwitchOrder.Default);

    [Theory]
    [InlineData("vulkan")]
    [InlineData("Vulkan")]
    public void Vulkan_is_what_vulkan_asks_for(string value)
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
    public void A_name_for_no_renderer_is_not_known_and_draws_over_the_default()
    {
        Assert.False(RendererSwitchOrder.IsKnown("opengl"));
        Assert.Equal(RendererSwitchOrder.Default, RendererSwitchOrder.Parse("opengl"));
    }

    [Theory]
    [InlineData("d3d12")]
    [InlineData("DX12")]
    [InlineData("Direct3D12")]
    public void Direct3d12_names_are_known_and_ask_for_it_on_windows(string value)
    {
        Assert.True(RendererSwitchOrder.IsKnown(value));
        Assert.Equal(
            OperatingSystem.IsWindows() ? RendererBackend.D3D12 : RendererSwitchOrder.Default,
            RendererSwitchOrder.Parse(value));
    }

    [Fact]
    public void The_switch_steps_through_every_backend_on_windows()
    {
        Assert.Equal(RendererBackend.WebGpu, RendererSwitchOrder.Next(RendererBackend.Vulkan, hasD3D12: true));
        Assert.Equal(RendererBackend.D3D12, RendererSwitchOrder.Next(RendererBackend.WebGpu, hasD3D12: true));
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Next(RendererBackend.D3D12, hasD3D12: true));
    }

    [Fact]
    public void The_switch_toggles_between_the_two_elsewhere()
    {
        Assert.Equal(RendererBackend.WebGpu, RendererSwitchOrder.Next(RendererBackend.Vulkan, hasD3D12: false));
        Assert.Equal(RendererBackend.Vulkan, RendererSwitchOrder.Next(RendererBackend.WebGpu, hasD3D12: false));
    }
}
