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

        WebGpuWindowHandles surface = handles.System switch
        {
            DesktopWindowSystem.Win32 => new(WebGpuWindowSystem.Win32, handles.Display, handles.Window),
            DesktopWindowSystem.Xlib => new(WebGpuWindowSystem.Xlib, handles.Display, handles.Window),
            DesktopWindowSystem.Wayland => new(WebGpuWindowSystem.Wayland, handles.Display, handles.Window),

            // A Cocoa window's surface is made from a Metal layer on its view, which nothing has attached yet.
            DesktopWindowSystem.Cocoa when OperatingSystem.IsMacOS() =>
                new(WebGpuWindowSystem.Metal, 0, CocoaMetalLayer.Attach(handles.Window)),
            _ => throw new PlatformException($"WebGPU has no surface for a {handles.System} window."),
        };

        return WebGpuRhiDevice.CreateForWindow(
            surface,
            width,
            height,
            vsync,
            finishing: ThreadPoolWorkScheduler.Instance);
    }
}
