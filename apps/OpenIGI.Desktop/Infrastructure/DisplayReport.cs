using OpenIGI.Core.Diagnostics;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;

namespace OpenIGI.Desktop;

/// <summary>
/// Logs what the window actually ended up as, which is not always what was asked for: a borderless window
/// is composited by the desktop, so it presents at the desktop's refresh rate and cannot raise it. Saying
/// so is the difference between "165 Hz was requested" and "60 Hz is what you are seeing".
/// </summary>
internal static class DisplayReport
{
    /// <summary>Reports the realised window mode, resolution and refresh rate, and warns when it is capped.</summary>
    /// <param name="window">The created window whose realised state is reported.</param>
    /// <param name="options">The parsed options, for the requested vsync state.</param>
    /// <param name="logger">The log the report is written to.</param>
    public static void Report(SilkGameWindow window, DesktopOptions options, IIgiLogger logger)
    {
        DesktopDisplay display = window.Display;
        string rate = window.RefreshRate > 0 ? $"{window.RefreshRate} Hz" : "an unreported refresh rate";

        logger.Information(
            "Desktop",
            $"{window.Mode} at {window.Width}x{window.Height} @ {rate}"
                + (display.MaxRefreshRate > 0 ? $" (monitor tops out at {display.MaxRefreshRate} Hz here)" : string.Empty)
                + (options.VSync ? ", vsync on." : ", vsync off."));

        logger.Information(
            "Desktop",
            $"Backend reports {window.BackendState}; monitor origin ({display.Origin.X},{display.Origin.Y}), "
                + $"desktop {display.DesktopSize.X}x{display.DesktopSize.Y} @ {display.DesktopRefreshRate} Hz, "
                + $"work area {display.WorkArea.Size.X}x{display.WorkArea.Size.Y}.");

        if (window.Mode == WindowMode.BorderlessFullscreen
            && display.MaxRefreshRate > 0
            && display.DesktopRefreshRate > 0
            && display.DesktopRefreshRate < display.MaxRefreshRate)
        {
            logger.Warning(
                "Desktop",
                $"The monitor supports {display.MaxRefreshRate} Hz at {display.DesktopSize.X}x{display.DesktopSize.Y} "
                    + $"but the desktop is set to {display.DesktopRefreshRate} Hz. A borderless window presents "
                    + "through the compositor and cannot exceed that -- raise it in Windows display settings, "
                    + "or run with -fullscreen to have the display mode set for you.");
        }
        else if (display.IsBelowMaxRefreshRate)
        {
            logger.Warning(
                "Desktop",
                $"Opened at {display.RefreshRate} Hz although the monitor reports {display.MaxRefreshRate} Hz "
                    + $"at {display.Size.X}x{display.Size.Y}.");
        }
    }
}
