using OpenIGI.Core.Configuration;
using OpenIGI.Platform;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The display settings: the window's mode and size, the API drawing into it, and the frame-rate cap.</summary>
internal sealed partial class GameHost
{
    /// <summary>Puts the saved window settings on the window, and follows them as they change.</summary>
    /// <remarks>
    /// After the settings file and the command line's statements, which is the first moment the variables
    /// hold what this run should use. A window the command line chose is left as it was asked for, and so is a
    /// capture's, whose size is part of what it captures. A browser's page is too: it goes fullscreen only
    /// in answer to a click, so a saved mode is applied there when the player picks it, not at start.
    /// </remarks>
    private void ApplyDisplaySettings()
    {
        if (!_options.WindowFromCommandLine && !_options.Lockstep && !OperatingSystem.IsBrowser())
        {
            ApplyWindowSettings();
        }

        WhenChanged(DisplayCVars.Window, ApplyWindowSettings);
        WhenChanged(DisplayCVars.Resolution, ApplyWindowSettings);
        WhenChanged(DisplayCVars.Renderer, OnRendererSettingChanged);
    }

    /// <summary>Sets the window's mode, then its size for that mode.</summary>
    /// <remarks>
    /// The back buffer follows through the window's resize, as it does for a window dragged larger. Borderless
    /// is the desktop's size whatever the resolution says. Fullscreen with no resolution goes back to the
    /// monitor's largest mode, which is its native one, so leaving a lower one is a choice the player can undo.
    /// </remarks>
    private void ApplyWindowSettings()
    {
        WindowMode mode = DisplayCVars.Window.Value switch
        {
            DisplayWindowMode.Windowed => WindowMode.Windowed,
            DisplayWindowMode.Fullscreen => WindowMode.ExclusiveFullscreen,
            _ => WindowMode.BorderlessFullscreen,
        };
        _window.Mode = mode;

        if (mode == WindowMode.BorderlessFullscreen)
        {
            return;
        }

        if (ResolutionText.TryParse(DisplayCVars.Resolution.Value, out int width, out int height))
        {
            _window.Resize(width, height);
        }
        else if (mode == WindowMode.ExclusiveFullscreen && DisplayResolutions() is [var native, ..])
        {
            _window.Resize(native.Width, native.Height);
        }
    }

    /// <summary>The resolutions the monitor the window is on offers, largest first, each once.</summary>
    /// <returns>The sizes.</returns>
    private IReadOnlyList<(int Width, int Height)> DisplayResolutions() =>
        [
            .. _window.GetDisplayModes()
                .Select(static mode => (mode.Width, mode.Height))
                .Distinct()
                .OrderByDescending(static size => (long)size.Width * size.Height)
                .ThenByDescending(static size => size.Width),
        ];

    /// <summary>The rate the loop holds itself to this frame, in hertz, or nought for as fast as it goes.</summary>
    /// <param name="menuMode">Whether the menu is up.</param>
    /// <returns>The rate.</returns>
    /// <remarks>
    /// <c>r_frame_limit</c> is held by NVIDIA Reflex where its runtime runs, which caps the frame without
    /// adding the queueing a sleeping loop does; everywhere else the loop waits out the remainder itself. The
    /// menu has nothing to gain past the display's rate, so without vertical sync it is held there too.
    /// </remarks>
    private int FrameRateCap(bool menuMode)
    {
        int cap = _device.LowLatencyUnavailable.Length > 0 ? RenderCVars.FrameLimit.Value : 0;
        if (menuMode && !RenderCVars.VSync.Value)
        {
            int display = _window.RefreshRate > 0 ? _window.RefreshRate : 60;
            cap = cap > 0 ? Math.Min(cap, display) : display;
        }

        return cap;
    }

    /// <summary>Why a display setting cannot take effect now, for the settings page; empty when it can.</summary>
    /// <param name="variable">The console variable a settings row drives.</param>
    /// <returns>The reason, or empty.</returns>
    private static string DisplaySettingUnavailable(string variable) => variable switch
    {
        "r_resolution" when DisplayCVars.Window.Value == DisplayWindowMode.Borderless =>
            "borderless covers the desktop at its own resolution; choose Windowed or Fullscreen, "
                + "or lower Upscaling to render fewer pixels",
        _ => string.Empty,
    };
}
