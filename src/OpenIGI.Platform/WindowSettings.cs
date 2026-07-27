namespace OpenIGI.Platform;

/// <summary>
/// How a game window is created.
/// </summary>
/// <param name="Title">The window title.</param>
/// <param name="Width">The client width in pixels.</param>
/// <param name="Height">The client height in pixels.</param>
/// <param name="Fullscreen">Whether the window covers the screen.</param>
/// <param name="CursorVisible">Whether the system cursor is shown over the window.</param>
/// <param name="VSync">Whether presentation waits for the vertical blank.</param>
/// <remarks>
/// The defaults reproduce the original's boot: a 640×480 client area, centred, fullscreen unless
/// <c>-window</c> was passed, with the cursor hidden (<c>WinMain</c> calls <c>ShowCursor(0)</c> right
/// after <c>CreateWindowExA</c>). See the original §boot.
/// </remarks>
public readonly record struct WindowSettings(
    string Title,
    int Width,
    int Height,
    bool Fullscreen,
    bool CursorVisible,
    bool VSync)
{
    /// <summary>Gets the settings the original boots with.</summary>
    public static WindowSettings Default => new("OpenIGI", 640, 480, Fullscreen: false, CursorVisible: false, VSync: true);
}
