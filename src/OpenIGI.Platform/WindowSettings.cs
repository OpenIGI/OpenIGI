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
/// <param name="Backend">Which graphics API the window is created for. Defaults to OpenGL.</param>
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
    GraphicsBackend Backend = GraphicsBackend.OpenGL)
{
    /// <summary>Gets the settings the original boots with.</summary>
    public static WindowSettings Default =>
        new("OpenIGI", 640, 480, WindowMode.Windowed, CursorVisible: false, VSync: true);
}
