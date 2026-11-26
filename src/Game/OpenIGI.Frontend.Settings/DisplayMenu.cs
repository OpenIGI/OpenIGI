namespace OpenIGI.Frontend.Settings;

/// <summary>What the Display page offers: the screen, how frames reach it, and the language of the game's text.</summary>
/// <remarks>
/// <para>
/// The ones every game's display options have, in the order a player looks for them: what draws, the
/// window, its size, then how frames reach it. Captioned here rather than by machine from their
/// variable names, which would turn one of them into "Smooth" — which says nothing.
/// </para>
/// <para>
/// <c>r_resolution</c> and <c>r_frame_limit</c> are not choices their types spell out — one is the
/// display's modes, the other a number — so the page builds their lists itself; see
/// <see cref="FrameRateCaps"/>.
/// </para>
/// </remarks>
public static class DisplayMenu
{
    /// <summary>Gets the page's panels, in the order they are shown.</summary>
    public static IReadOnlyList<SettingsPanel> Panels { get; } =
    [
        new SettingsPanel(
            "Screen",
            [],
            [
                new SettingRow(
                    "r_renderer",
                    "Graphics API",
                    "What the game draws through. Direct3D 12 is the usual choice on Windows and Vulkan elsewhere; "
                        + "WebGPU is the browser version's. OpenGL is for older graphics cards that have neither of the first two, "
                        + "and has no ray tracing or DLSS. Changing it rebuilds the renderer and puts you back where you "
                        + "were, which takes a few seconds.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["d3d12"] = "Direct3D 12",
                        ["vulkan"] = "Vulkan",
                        ["webgpu"] = "WebGPU",
                        ["opengl"] = "OpenGL",
                    }),
                new SettingRow(
                    "r_window_mode",
                    "Window Mode",
                    "Borderless covers the screen at the desktop's resolution and switches away instantly. Fullscreen "
                        + "takes the monitor over at the resolution below. Windowed runs in a window that size.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["windowed"] = "Windowed",
                        ["borderless"] = "Borderless",
                        ["fullscreen"] = "Fullscreen",
                    }),
                new SettingRow(
                    "r_resolution",
                    "Resolution",
                    "The window's size, or the monitor's mode in fullscreen. Desktop is your screen's own. To render "
                        + "fewer pixels at the same size, lower Upscaling on the Graphics page instead."),
            ]),

        new SettingsPanel(
            "Output",
            [],
            [
                new SettingRow(
                    "r_widescreen_fov",
                    "Widescreen View",
                    "On a screen wider than 4:3, keeps the original's view whole and shows more at the sides, with "
                        + "nothing stretched. Off keeps the original's side-to-side angle, which crops the top and bottom "
                        + "and stretches the hands and weapon. A 4:3 screen looks the same either way."),
                new SettingRow(
                    "r_vsync",
                    "Vertical Sync",
                    "Waits for the display to be ready before showing each frame. On removes tearing; off uncaps "
                        + "the frame rate and lowers the delay between mouse and picture."),
                new SettingRow(
                    "r_smooth",
                    "Smooth Motion",
                    "Draws the world at your display's rate rather than the original's fixed thirty steps a "
                        + "second, interpolating between them. Also removes IGI2's mouse filtering and turn-speed cap. "
                        + "Off restores the original cadence and IGI2 mouse response."),
                new SettingRow(
                    "r_frame_limit",
                    "Frame Rate Cap",
                    "The most frames a second the game draws. A cap a little under your display's rate keeps the "
                        + "motion even and the GPU cooler. Held by NVIDIA Reflex where it runs, which keeps the delay "
                        + "low while it does."),
                new SettingRow(
                    "r_exposure",
                    "Brightness",
                    "How bright the picture is overall. The eye still adapts to each room on its own; this "
                        + "nudges the result up or down to suit your display."),
            ]),

        new SettingsPanel(
            "Language",
            [],
            [
                new SettingRow(
                    "language",
                    "Language",
                    "The language of the game's own text -- mission names and briefings, objectives, messages and the "
                        + "mission computer -- read from the game's language folders.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["english"] = "English",
                        ["usa"] = "English (US)",
                        ["german"] = "Deutsch",
                        ["french"] = "Français",
                        ["italian"] = "Italiano",
                        ["spanish"] = "Español",
                    }),
            ]),
    ];

    /// <summary>Gets every row on the page, in the order they are shown.</summary>
    public static IReadOnlyList<SettingRow> Rows { get; } = [.. Panels.SelectMany(panel => panel.Rows)];

    /// <summary>Gets the frame-rate caps the Display page offers, in frames a second; nought is none.</summary>
    /// <remarks>The rates displays commonly run at, and the fractions of them a steady cap is chosen from.</remarks>
    public static IReadOnlyList<int> FrameRateCaps { get; } = [0, 30, 60, 75, 90, 100, 120, 144, 165, 180, 240, 360];
}
