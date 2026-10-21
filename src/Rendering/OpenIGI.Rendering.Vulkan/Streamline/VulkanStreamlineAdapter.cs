using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;
using OpenIGI.Rendering.Vulkan.Rhi;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>A Streamline session's native side on a Vulkan device created through the interposer.</summary>
/// <remarks>
/// Everything Streamline needs that the RHI does not say -- the <c>VkImage</c>, its view, its format, its
/// usage, the layout it is in, the command buffer -- is read off the backend's own objects here. Everything
/// else is <see cref="StreamlineSession"/>'s.
/// </remarks>
internal sealed class VulkanStreamlineAdapter : IStreamlineGraphicsAdapter
{
    private readonly VulkanRhiDevice _device;

    /// <summary>Initialises a new instance of the <see cref="VulkanStreamlineAdapter"/> class.</summary>
    /// <param name="device">The RHI device.</param>
    /// <param name="physicalDevice">The adapter, for support queries.</param>
    public VulkanStreamlineAdapter(VulkanRhiDevice device, PhysicalDevice physicalDevice)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        Adapter = SlAdapterInfo.ForVulkan(physicalDevice.Handle);
    }

    /// <inheritdoc/>
    public SlAdapterInfo Adapter { get; }

    /// <inheritdoc/>
    public nint CommandList(IRhiCommandEncoder encoder) => ((VulkanRhiCommandEncoder)encoder).Command.Handle;

    /// <inheritdoc/>
    /// <remarks>
    /// Everything the RHI holds rests in General between passes already; the transition is the guarantee
    /// rather than the hope, and General is the layout the description names.
    /// </remarks>
    public SlResource PrepareTag(IRhiTexture texture, IRhiCommandEncoder encoder)
    {
        VulkanRhiTexture vulkan = (VulkanRhiTexture)texture;
        _device.Transition(((VulkanRhiCommandEncoder)encoder).Command, vulkan, ImageLayout.General);
        return Describe(vulkan);
    }

    /// <inheritdoc/>
    public void ExternalBarrier(IRhiCommandEncoder encoder) => ((VulkanRhiCommandEncoder)encoder).ExternalBarrier();

    /// <inheritdoc/>
    public void RebuildSwapchain(Action between) => _device.RebuildSwapchain(between);

    /// <inheritdoc/>
    public void AddFrameWait(nint fence, ulong value) => _device.AddFrameWait(fence, value);

    /// <inheritdoc/>
    public void WaitIdle() => _device.WaitIdle();

    /// <summary>Puts a texture tagged until the present back into the layout its tag named.</summary>
    /// <param name="command">The command buffer recorded last before the present.</param>
    /// <param name="texture">The texture.</param>
    internal void RestoreForPresent(CommandBuffer command, IRhiTexture texture) =>
        _device.Transition(command, (VulkanRhiTexture)texture, ImageLayout.General);

    private static SlResource Describe(VulkanRhiTexture texture)
    {
        SlResource resource = SlResource.Image2D(
            (nint)texture.Image.Handle,
            (nint)((VulkanRhiTextureView)texture.View).Handle.Handle,
            (uint)ImageLayout.General,
            (uint)VulkanRhiTranslation.TextureFormat(texture.Format),
            (uint)texture.Width,
            (uint)texture.Height,
            (uint)VulkanRhiTranslation.TextureUsage(texture.Usage, texture.Format.IsDepth()));
        resource.MipLevels = (uint)texture.MipLevelCount;
        resource.ArrayLayers = (uint)texture.Layers;
        return resource;
    }
}
