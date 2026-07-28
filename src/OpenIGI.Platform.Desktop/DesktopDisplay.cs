using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace OpenIGI.Platform.Desktop;

/// <summary>
/// What a monitor reports, and the size and refresh rate chosen from it for a window.
/// </summary>
/// <param name="Origin">The monitor's top-left corner in desktop coordinates.</param>
/// <param name="WorkArea">
/// The part of the monitor left over once the taskbar and other appbars are subtracted, as the backend
/// reports it. Kept only so a window that comes up the wrong size can be diagnosed against it.
/// </param>
/// <param name="DesktopSize">The monitor's current desktop resolution in pixels.</param>
/// <param name="DesktopRefreshRate">The refresh rate the desktop is running at, in hertz, or zero if unreported.</param>
/// <param name="Size">The client size chosen for the window.</param>
/// <param name="RefreshRate">The refresh rate chosen for the window, in hertz, or zero if unreported.</param>
/// <param name="MaxRefreshRate">The highest rate the monitor reports at <paramref name="Size"/>, or zero.</param>
/// <remarks>
/// The distinction between <paramref name="RefreshRate"/> and <paramref name="DesktopRefreshRate"/> is
/// the whole reason this exists. A mode change is something only an exclusive-fullscreen window gets to
/// make; a borderless one is composited by the desktop and therefore presents at the desktop's rate, so
/// asking for 165 Hz on a desktop set to 60 Hz silently gets 60. Reporting both lets the caller say so
/// rather than claim a rate it is not achieving.
/// </remarks>
public readonly record struct DesktopDisplay(
    Vector2D<int> Origin,
    Rectangle<int> WorkArea,
    Vector2D<int> DesktopSize,
    int DesktopRefreshRate,
    Vector2D<int> Size,
    int RefreshRate,
    int MaxRefreshRate)
{
    /// <summary>Gets a value indicating whether the chosen rate is below what the monitor can do.</summary>
    /// <remarks>
    /// True only when the shortfall is real and known: an unreported rate is zero on both sides and is
    /// not something to warn about.
    /// </remarks>
    public bool IsBelowMaxRefreshRate => MaxRefreshRate > 0 && RefreshRate > 0 && RefreshRate < MaxRefreshRate;

    /// <summary>Chooses the size and refresh rate a window should open with.</summary>
    /// <param name="monitor">The monitor the window will be on.</param>
    /// <param name="settings">What the caller asked for.</param>
    /// <returns>The resolved display description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="monitor"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Borderless takes the monitor's own resolution, because that is what "covers the screen" means.
    /// The other two take the requested size, falling back to the monitor's when none was given. In
    /// every case the rate is the highest the monitor reports <em>at that resolution</em> unless one was
    /// named — a monitor's fastest mode is often only offered at its lowest resolution, so picking the
    /// fastest mode overall would quietly hand back a smaller picture.
    /// </remarks>
    public static DesktopDisplay Resolve(IMonitor monitor, in WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        int desktopRefreshRate = monitor.VideoMode.RefreshRate ?? 0;

        // The current video mode, NOT Bounds: the backend reports the work area there, which is the
        // screen minus the taskbar. Sizing a borderless window to that leaves the taskbar on top of the
        // picture and is the single most common way this feature is got wrong.
        Vector2D<int> desktopSize = monitor.VideoMode.Resolution ?? monitor.Bounds.Size;

        // A monitor that reports neither would give a window with no client area, so fall back to the
        // original's own size rather than to nothing.
        if (desktopSize.X <= 0 || desktopSize.Y <= 0)
        {
            desktopSize = new Vector2D<int>(640, 480);
        }

        Vector2D<int> size = settings.Mode == WindowMode.BorderlessFullscreen
            ? desktopSize
            : settings.Width > 0 && settings.Height > 0
                ? new Vector2D<int>(settings.Width, settings.Height)
                : desktopSize;

        int maxRefreshRate = HighestRefreshRateAt(monitor, size);

        // Borderless never gets a mode of its own, so reporting anything but the desktop's rate here
        // would be a number the picture does not run at.
        int refreshRate = settings.Mode == WindowMode.BorderlessFullscreen
            ? desktopRefreshRate
            : settings.RefreshRate > 0
                ? settings.RefreshRate
                : maxRefreshRate > 0 ? maxRefreshRate : desktopRefreshRate;

        return new DesktopDisplay(
            monitor.Bounds.Origin,
            monitor.Bounds,
            desktopSize,
            desktopRefreshRate,
            size,
            refreshRate,
            settings.Mode == WindowMode.BorderlessFullscreen
                ? HighestRefreshRateAt(monitor, desktopSize)
                : maxRefreshRate);
    }

    private static int HighestRefreshRateAt(IMonitor monitor, Vector2D<int> size)
    {
        int highest = 0;

        foreach (VideoMode mode in monitor.GetAllVideoModes())
        {
            if (mode.Resolution == size && mode.RefreshRate is { } rate && rate > highest)
            {
                highest = rate;
            }
        }

        return highest;
    }
}
