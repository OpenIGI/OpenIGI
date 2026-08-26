using System.Runtime.InteropServices;
using OpenIGI.Rendering.Vulkan;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Desktop;

/// <summary>
/// Bridges Silk.NET's windowing surface to the Vulkan backend's <see cref="IVulkanSurfaceProvider"/>.
/// </summary>
/// <remarks>
/// The backend never references a windowing library; the desktop app, which owns the window, adapts its
/// <see cref="IVkSurface"/> — the required instance extensions before the instance exists, and the
/// surface creation once it does. This is the Vulkan counterpart to handing the OpenGL device the
/// window's GL context.
/// </remarks>
internal sealed unsafe class VulkanSurfaceProvider : IVulkanSurfaceProvider
{
    private readonly IVkSurface _surface;

    /// <summary>Initialises a new instance of the <see cref="VulkanSurfaceProvider"/> class.</summary>
    /// <param name="surface">The window's Silk.NET surface source.</param>
    public VulkanSurfaceProvider(IVkSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetRequiredInstanceExtensions()
    {
        byte** extensions = _surface.GetRequiredExtensions(out uint count);
        List<string> names = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            names.Add(Marshal.PtrToStringAnsi((nint)extensions[i]) ?? string.Empty);
        }

        return names;
    }

    /// <inheritdoc/>
    public SurfaceKHR CreateSurface(Vk vk, Instance instance, KhrSurface khrSurface)
    {
        VkNonDispatchableHandle handle = _surface.Create<nint>(new VkHandle(instance.Handle), null);
        return new SurfaceKHR(handle.Handle);
    }
}
