namespace OpenIGI.Platform;

/// <summary>
/// How a game window is created.
/// </summary>
/// <param name="Title">The window title.</param>
/// <param name="Width">The client width in pixels, or zero for the monitor's own.</param>
/// <param name="Height">The client height in pixels, or zero for the monitor's own.</param>
/// <param name="Mode">How the window relates to the screen.</param>
/// <param name="CursorVisible">Whether the system cursor is shown over the window.</param>
/// <param name="VSync">Whether presentation waits for the vertical blank.</param>
/// <param name="RefreshRate">
/// The refresh rate in hertz to ask the monitor for, or zero for the highest it reports at the chosen
/// resolution.
/// </param>
/// <param name="Graphics">
/// Whether the window carries an OpenGL context. A window is made with one or without one and cannot change, so
/// the OpenGL renderer needs a window of its own; every other renderer presents through a window without.
/// </param>
/// <remarks>
/// <para>
/// The original's boot is a 640×480 client area, centred, fullscreen unless <c>-window</c> was passed,
/// with the cursor hidden (<c>WinMain</c> calls <c>ShowCursor(0)</c> right after <c>CreateWindowExA</c>).
/// </para>
/// <para>
/// <see cref="Width"/> and <see cref="Height"/> are ignored under
/// <see cref="WindowMode.BorderlessFullscreen"/>: a borderless window that is not exactly the size of
/// the desktop is not fullscreen, it is a window with no title bar. <see cref="RefreshRate"/> is
/// likewise ignored there, because the desktop's mode is what the picture is composited at.
/// </para>
/// </remarks>
public readonly record struct WindowSettings(
    string Title,
    int Width,
    int Height,
    WindowMode Mode,
    bool CursorVisible,
    bool VSync,
    int RefreshRate = 0,
    WindowGraphics Graphics = WindowGraphics.Native)
{
    /// <summary>Gets the settings the original boots with.</summary>
    public static WindowSettings Default =>
        new("OpenIGI", 640, 480, WindowMode.Windowed, CursorVisible: false, VSync: true);
}

/// <summary>What a window is made ready for a renderer to draw into.</summary>
public enum WindowGraphics
{
    /// <summary>No context of its own: Vulkan, Direct3D 12 and WebGPU each make their own swapchain on it.</summary>
    Native,

    /// <summary>An OpenGL 4.3 core context, for the OpenGL renderer.</summary>
    OpenGL,

    /// <summary>An OpenGL 4.3 core debug context, which reports everything the driver finds wrong.</summary>
    OpenGLDebug,
}
