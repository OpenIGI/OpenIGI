using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The window's side of the Vulkan seam: the instance extensions a presentable surface needs, and the
/// creation of that surface once the instance exists.
/// </summary>
/// <remarks>
/// <para>
/// The backend never references a windowing library, exactly as the OpenGL backend takes a
/// <c>GL</c> and a present delegate rather than the window itself. A surface is the one thing Vulkan
/// cannot create without the platform — it is the bridge between the swapchain and the operating
/// system's window — so the platform hands over just that: which extensions to enable, and how to make
/// the surface. On desktop this is implemented against Silk.NET's <c>IVkSurface</c>; a test or a
/// headless host can implement it however it needs to.
/// </para>
/// <para>
/// The required-extensions call happens <em>before</em> the instance is created, because the instance
/// must be created with them already enabled; the surface call happens after, because a surface is
/// created against a live instance.
/// </para>
/// </remarks>
public interface IVulkanSurfaceProvider
{
    /// <summary>Gets the instance extensions a surface for this window requires.</summary>
    /// <returns>
    /// The platform surface extensions — on Win32, <c>VK_KHR_surface</c> and
    /// <c>VK_KHR_win32_surface</c>; the exact set varies by platform and is whatever the windowing
    /// layer reports. The backend adds its own (debug utils, and later the ray-tracing set) on top.
    /// </returns>
    IReadOnlyList<string> GetRequiredInstanceExtensions();

    /// <summary>Creates the presentable surface for this window.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="instance">The instance the surface is created against.</param>
    /// <param name="khrSurface">The surface extension, already loaded for <paramref name="instance"/>.</param>
    /// <returns>The created surface.</returns>
    SurfaceKHR CreateSurface(Vk vk, Instance instance, KhrSurface khrSurface);
}
