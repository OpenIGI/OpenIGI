using OpenIGI.Input;

namespace OpenIGI.Platform;

/// <summary>
/// The host window: a surface to render into, a message pump, and the input state that pump feeds.
/// </summary>
/// <remarks>
/// <para>
/// The original's window lives in <c>WinMain</c> (<c>0x48F530</c>) and its message loop drives
/// everything: it pumps messages, then calls the root task's update, then repeats. This interface
/// keeps that shape — <see cref="PumpEvents"/> is the message pump, and the caller owns the loop, so
/// the frame arbiter from the original stays in charge of when work happens.
/// </para>
/// <para>
/// All members must be called from the thread that created the window.
/// </para>
/// </remarks>
public interface IGameWindow : IDisposable
{
    /// <summary>Occurs when the client area has changed size.</summary>
    event EventHandler<WindowResizedEventArgs>? Resized;

    /// <summary>Occurs when the user has asked to close the window.</summary>
    /// <remarks>
    /// The original's <c>WM_DESTROY</c> clears its running flag and lets the loop exit on its own
    /// terms rather than tearing down inline; hosts should do the same and treat this as a request.
    /// </remarks>
    event EventHandler? CloseRequested;

    /// <summary>Gets or sets the window title.</summary>
    string Title { get; set; }

    /// <summary>Gets the client area width in pixels.</summary>
    int Width { get; }

    /// <summary>Gets the client area height in pixels.</summary>
    int Height { get; }

    /// <summary>Gets or sets a value indicating whether the window covers the screen.</summary>
    bool IsFullscreen { get; set; }

    /// <summary>Gets or sets a value indicating whether the system cursor is shown over the window.</summary>
    bool CursorVisible { get; set; }

    /// <summary>Gets a value indicating whether the window has input focus.</summary>
    /// <remarks>
    /// The original releases every key when its input device cannot be read, which is what focus loss
    /// looks like from the inside; hosts should clear <see cref="Input"/> the same way rather than
    /// leaving keys latched down.
    /// </remarks>
    bool IsFocused { get; }

    /// <summary>Gets a value indicating whether a close has been requested and the loop should end.</summary>
    bool IsClosing { get; }

    /// <summary>Lists the display modes the monitor this window is on reports.</summary>
    /// <returns>The modes, in whatever order the platform gives them.</returns>
    /// <remarks>
    /// The graphics configuration screen needs real modes to offer; a window that cannot enumerate
    /// them returns only the one it is running at, which is honest rather than empty.
    /// </remarks>
    IReadOnlyList<(int Width, int Height, int RefreshRate)> GetDisplayModes();

    /// <summary>Gets the input state this window's message pump feeds.</summary>
    InputState Input { get; }

    /// <summary>Makes the window visible.</summary>
    void Show();

    /// <summary>Processes pending window messages and updates <see cref="Input"/> for the new frame.</summary>
    void PumpEvents();

    /// <summary>Requests that the window close, as though the user had asked.</summary>
    void Close();
}
