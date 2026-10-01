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

    /// <summary>Gets or sets how the window relates to the screen it is on.</summary>
    /// <remarks>
    /// Assigning this switches modes in place; the framebuffer follows through <see cref="Resized"/>,
    /// so a caller does not have to rebuild the device.
    /// </remarks>
    WindowMode Mode { get; set; }

    /// <summary>Gets a value indicating whether the window covers the screen.</summary>
    bool IsFullscreen => Mode != WindowMode.Windowed;

    /// <summary>Gets the refresh rate the window is presenting at, in hertz, or zero if unknown.</summary>
    /// <remarks>
    /// Under <see cref="WindowMode.BorderlessFullscreen"/> this is the desktop's rate, which is the only
    /// one a composited window can be presented at — a borderless window cannot drive the display faster
    /// than the desktop it sits on.
    /// </remarks>
    int RefreshRate { get; }

    /// <summary>Gets or sets a value indicating whether the system cursor is shown over the window.</summary>
    bool CursorVisible { get; set; }

    /// <summary>Gets or sets a value indicating whether the cursor is held inside the window.</summary>
    /// <remarks>
    /// A locked cursor is hidden and cannot leave, so mouse motion keeps arriving however far the user
    /// drags. That is what a look-around control needs: without it the pointer reaches the edge of the
    /// screen and the view stops turning. Merely hiding the cursor is not enough, which is why this is
    /// separate from <see cref="CursorVisible"/>.
    /// </remarks>
    bool CursorLocked { get; set; }

    /// <summary>Gets or sets the system clipboard's text.</summary>
    /// <remarks>
    /// <para>
    /// On the window because the clipboard belongs to the windowing system, not to the input devices:
    /// the platform backend already owns the handle that reaches it, and putting it here keeps every
    /// layer above free of a platform reference. A host that cannot reach a clipboard returns an empty
    /// string and discards what is set, so a caller never has to ask whether one exists.
    /// </para>
    /// <para>
    /// Reading is a synchronous call into the window system and is not free; read it when a paste is
    /// actually asked for rather than polling it.
    /// </para>
    /// </remarks>
    string ClipboardText { get; set; }

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

    /// <summary>Resizes the window's client area.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <remarks>
    /// What the graphics screen's resolution list acts on. The back buffer follows through the
    /// <see cref="Resized"/> event, so a caller does not have to rebuild the device. It has no effect
    /// under <see cref="WindowMode.BorderlessFullscreen"/>, whose size is the desktop's by definition.
    /// </remarks>
    void Resize(int width, int height);

    /// <summary>Gets the input state this window's message pump feeds.</summary>
    InputState Input { get; }

    /// <summary>Makes the window visible.</summary>
    void Show();

    /// <summary>Processes pending window messages and updates <see cref="Input"/> for the new frame.</summary>
    void PumpEvents();

    /// <summary>Requests that the window close, as though the user had asked.</summary>
    void Close();
}
