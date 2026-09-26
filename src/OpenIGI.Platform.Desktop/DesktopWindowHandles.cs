namespace OpenIGI.Platform.Desktop;

/// <summary>Which window system a desktop window's native handles belong to.</summary>
public enum DesktopWindowSystem
{
    /// <summary>A Win32 window: a module instance and a window handle.</summary>
    Win32,

    /// <summary>An X11 window through Xlib: a display and a window identifier.</summary>
    Xlib,

    /// <summary>A Wayland surface: a display and a surface.</summary>
    Wayland,
}

/// <summary>A desktop window's native handles, as its own window system names them.</summary>
/// <param name="System">Which window system they belong to.</param>
/// <param name="Display">The Win32 module instance, the Xlib display, or the Wayland display.</param>
/// <param name="Window">The Win32 window handle, the X11 window identifier, or the Wayland surface.</param>
/// <remarks>
/// For a graphics API that makes its surface from the window itself rather than from a Vulkan surface
/// the window hands over. Named here rather than in that API's terms so the platform layer names no
/// graphics backend; the composition root translates.
/// </remarks>
public readonly record struct DesktopWindowHandles(DesktopWindowSystem System, nint Display, nint Window);
