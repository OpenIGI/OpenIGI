using OpenIGI.Core.Threading;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering.WebGpu;

namespace OpenIGI.Desktop;

/// <summary>The WebGPU device a desktop window presents through, for A/B against Vulkan.</summary>
/// <remarks>
/// The browser makes its surface from a canvas; the desktop makes it from the window's own native handles,
/// which the platform layer states in its own terms so that it names no graphics backend. This is where
/// the two vocabularies meet.
/// </remarks>
internal static class WebGpuWindow
{
    /// <summary>Makes a WebGPU device presenting to a window.</summary>
    /// <param name="window">The window.</param>
    /// <param name="width">The width to configure the surface at.</param>
    /// <param name="height">The height to configure it at.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <returns>The device, with its surface configured.</returns>
    /// <exception cref="PlatformException">The window has no handles WebGPU can make a surface from.</exception>
    public static WebGpuRhiDevice Create(SilkGameWindow window, int width, int height, bool vsync)
    {
        ArgumentNullException.ThrowIfNull(window);

        DesktopWindowHandles handles = window.NativeHandles
            ?? throw new PlatformException(
                "This window system gives no native handles WebGPU can make a surface from; "
                + "run with the Vulkan renderer instead.");

        WebGpuWindowSystem system = handles.System switch
        {
            DesktopWindowSystem.Win32 => WebGpuWindowSystem.Win32,
            DesktopWindowSystem.Xlib => WebGpuWindowSystem.Xlib,
            DesktopWindowSystem.Wayland => WebGpuWindowSystem.Wayland,
            _ => throw new PlatformException($"WebGPU has no surface for a {handles.System} window."),
        };

        return WebGpuRhiDevice.CreateForWindow(
            new WebGpuWindowHandles(system, handles.Display, handles.Window),
            width,
            height,
            vsync,
            finishing: ThreadPoolWorkScheduler.Instance);
    }
}
