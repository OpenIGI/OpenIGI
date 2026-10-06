using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Binds Silk's Vulkan API to Streamline's interposer instead of the system loader.</summary>
/// <remarks>
/// <para>
/// Streamline has to see a handful of Vulkan calls go past — instance and device creation, so it can
/// add the extensions, features and queues its plugins need, and the swapchain and present calls,
/// which is what makes frame generation possible at all. There are two ways to arrange that. The
/// manual route hooks each of those entry points by hand and hands Streamline the device afterwards
/// through <c>slSetVulkanInfo</c>. The other is to load <c>sl.interposer.dll</c> in place of
/// <c>vulkan-1.dll</c> and let it forward everything it does not care about.
/// </para>
/// <para>
/// The second is taken here, and it is barely any code: the interposer exports the whole Vulkan
/// 1.0-1.3 core plus the surface and swapchain entry points, so it is a drop-in replacement for the
/// loader, and Silk resolves extension functions through <c>vkGetDeviceProcAddr</c> — which it now
/// gets from the interposer, so those come back proxied too without anything asking for it. Nothing
/// in the rest of the backend changes, and there is no list of hooked functions to keep in step with
/// a future SDK.
/// </para>
/// <para>
/// The one thing this does not cover is the window surface. Silk creates that through GLFW, which
/// loads Vulkan itself, so <c>vkCreateWin32SurfaceKHR</c> goes past Streamline unseen. Nothing in the
/// production plugins hooks surface creation — frame generation and Reflex hook the swapchain, which
/// does come through here — so the only thing missed is the development overlay's own window hook.
/// </para>
/// </remarks>
internal static class StreamlineVulkanLoader
{
    /// <summary>Builds a Vulkan API bound to the Streamline interposer.</summary>
    /// <param name="interposerPath">The absolute path to <c>sl.interposer.dll</c>.</param>
    /// <returns>The API, resolving core entry points through Streamline.</returns>
    /// <remarks>
    /// This mirrors Silk's own <c>Vk.GetApi</c> exactly, and deliberately so — the only difference is
    /// which library the first context loads. The second context is Silk's fallback that resolves a
    /// name through <c>vkGetDeviceProcAddr</c> and then <c>vkGetInstanceProcAddr</c>, and it has to
    /// refuse the two <c>ProcAddr</c> functions themselves or it would recurse into itself looking
    /// for them.
    /// </remarks>
    public static Vk CreateApi(string interposerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interposerPath);

        MultiNativeContext context = new(new DefaultNativeContext(interposerPath), null);
        Vk vk = new(context);

        context.Contexts[1] = new LamdaNativeContext(name =>
        {
            if (name.EndsWith("ProcAddr", StringComparison.Ordinal))
            {
                return default;
            }

            nint address = vk.GetDeviceProcAddr(vk.CurrentDevice.GetValueOrDefault(), name);
            return address != default
                ? address
                : vk.GetInstanceProcAddr(vk.CurrentInstance.GetValueOrDefault(), name);
        });

        return vk;
    }
}
