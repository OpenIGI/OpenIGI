namespace OpenIGI.Platform;

/// <summary>
/// How a game window relates to the screen it is on.
/// </summary>
/// <remarks>
/// The original only knows two of these: <c>WinMain</c> creates a <c>WS_POPUP</c> window covering the
/// screen unless <c>-window</c> was passed, and the Direct3D device is then created in exclusive mode
///. <see cref="BorderlessFullscreen"/> is an addition —
/// the same screen-covering picture without taking the display away from the desktop compositor, which
/// is what alt-tabbing out of a modern build is expected to feel like.
/// </remarks>
public enum WindowMode
{
    /// <summary>A normal, decorated, resizable window.</summary>
    Windowed,

    /// <summary>
    /// An undecorated window positioned at the monitor's origin and sized to its whole desktop area.
    /// </summary>
    /// <remarks>
    /// The display mode is not changed, so the picture is composited at whatever resolution and refresh
    /// rate the desktop is running at. Switching away is instant and nothing else on the machine is
    /// resized, which is the point of it.
    /// </remarks>
    BorderlessFullscreen,

    /// <summary>The monitor is taken over and switched to the requested display mode.</summary>
    ExclusiveFullscreen,
}
