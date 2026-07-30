using OpenIGI.Game.Ai;
using OpenIGI.Platform;

namespace OpenIGI.Desktop;

/// <summary>
/// The command-line options the desktop shell understands.
/// </summary>
/// <param name="GamePath">The original installation to read data from.</param>
/// <param name="Mode">How the window relates to the screen.</param>
/// <param name="Width">The window client width in pixels, or zero for the monitor's own.</param>
/// <param name="Height">The window client height in pixels, or zero for the monitor's own.</param>
/// <param name="VSync">Whether presentation waits for the vertical blank.</param>
/// <param name="ScreenshotPath">Where to write a capture of the first rendered frames, or <see langword="null"/> to run normally.</param>
/// <param name="ExitAfterFrames">Quit after this many rendered frames, or zero to run until closed.</param>
/// <param name="StartScreen">Open on this menu screen id instead of the one the script names, or zero.</param>
/// <param name="StartMission">Load this mission at boot instead of opening the menus, or zero.</param>
/// <param name="LogCamera">Whether to print the fly-through camera's input and orientation every frame.</param>
/// <param name="StartFlying">Whether to open in the free camera instead of standing on the ground.</param>
/// <param name="CameraYaw">The yaw in radians the fly-through camera opens on.</param>
/// <param name="CameraPitch">The pitch in radians the fly-through camera opens on.</param>
/// <param name="CameraHeight">Extra metres above the level to open at.</param>
/// <param name="UiScale">How much to magnify the 640x480-authored interface; 0 derives it from the window.</param>
/// <param name="TerrainViewDistance">Override the terrain level-of-detail distance, or zero for the derived one.</param>
/// <remarks>
/// The original parses its command line with a case-insensitive prefix scan (<c>Win_ParseCmdOption</c>,
/// <c>0x48F360</c>) and understands <c>Window</c>, <c>WinInput</c>, <c>Germany</c> and
/// <c>HardcoreGerman</c>. <c>-window</c> is kept with the same meaning; the rest are additions the
/// original had no need for because it shipped alongside its data.
/// </remarks>
public readonly record struct DesktopOptions(
    string? GamePath,
    WindowMode Mode,
    int Width,
    int Height,
    bool VSync,
    string? ScreenshotPath,
    int ExitAfterFrames,
    int StartScreen,
    int StartMission,
    bool LogCamera,
    bool StartFlying,
    float CameraYaw,
    float CameraPitch,
    float CameraHeight,
    int TerrainViewDistance,
    int UiScale)
{
    /// <summary>Gets the environment variable consulted when no path is given on the command line.</summary>
    public const string GamePathVariable = "IGI_GAME_PATH";

    /// <summary>Parses a command line.</summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An option is malformed.</exception>
    public static DesktopOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? gamePath = Environment.GetEnvironmentVariable(GamePathVariable);
        AiDifficulty difficulty = AiDifficulty.Normal;

        // Null until a mode flag is seen, so -size can imply windowed without overriding an explicit
        // -borderless that came before or after it.
        WindowMode? mode = null;
        bool vsync = true;

        // Zero, not 640x480: the default mode is borderless, whose size is the monitor's, and a size
        // only becomes meaningful once -window or -fullscreen has been asked for.
        int width = 0;
        int height = 0;
        string? screenshot = null;
        (double X, double Y, double Z)? cameraPosition = null;
        int exitAfterFrames = 0;
        int uiScale = OpenIGI.Core.Graphics.UiScale.Automatic;
        bool logCamera = false;
        bool startFlying = false;
        float cameraYaw = 0f;
        float cameraPitch = -0.12f;
        float cameraHeight = 0f;
        int terrainViewDistance = 0;
        int startScreen = 0;
        int startMission = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].TrimStart('-', '/');

            if (arg.Equals("window", StringComparison.OrdinalIgnoreCase))
            {
                mode = WindowMode.Windowed;
            }
            else if (arg.Equals("borderless", StringComparison.OrdinalIgnoreCase))
            {
                mode = WindowMode.BorderlessFullscreen;
            }
            else if (arg.Equals("fullscreen", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("exclusive", StringComparison.OrdinalIgnoreCase))
            {
                mode = WindowMode.ExclusiveFullscreen;
            }
            else if (arg.Equals("novsync", StringComparison.OrdinalIgnoreCase))
            {
                vsync = false;
            }
            else if (arg.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                gamePath = Next(args, ref i, "game");
            }
            else if (arg.Equals("size", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "size");
                string[] parts = value.Split('x', 'X');
                if (parts.Length != 2
                    || !int.TryParse(parts[0], out width)
                    || !int.TryParse(parts[1], out height)
                    || width <= 0
                    || height <= 0)
                {
                    throw new ArgumentException($"-size expects WIDTHxHEIGHT, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("mission", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "mission");
                if (!int.TryParse(value, out startMission) || startMission <= 0)
                {
                    throw new ArgumentException($"-mission expects a mission id, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("difficulty", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "difficulty");
                difficulty = value.ToLowerInvariant() switch
                {
                    "easy" or "gd_1" or "1" => AiDifficulty.Easy,
                    "normal" or "gd_2" or "2" => AiDifficulty.Normal,
                    "hard" or "gd_3" or "3" => AiDifficulty.Hard,
                    _ => throw new ArgumentException(
                        $"-difficulty expects easy, normal or hard, got '{value}'.", nameof(args)),
                };
            }
            else if (arg.Equals("uiscale", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "uiscale");
                if (!int.TryParse(value, out uiScale) || uiScale < 0)
                {
                    throw new ArgumentException(
                        $"-uiscale expects 0 for automatic or a positive whole number, got '{value}'.",
                        nameof(args));
                }
            }
            else if (arg.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
            {
                screenshot = Next(args, ref i, "screenshot");
                if (exitAfterFrames == 0)
                {
                    // A few frames in, so the capture is of a steady frame rather than the first one
                    // the driver produces.
                    exitAfterFrames = 5;
                }
            }
            else if (arg.Equals("frames", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "frames");
                if (!int.TryParse(value, out exitAfterFrames) || exitAfterFrames < 0)
                {
                    throw new ArgumentException($"-frames expects a non-negative count, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("yaw", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "yaw");
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cameraYaw))
                {
                    throw new ArgumentException($"-yaw expects a number of radians, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("pitch", StringComparison.OrdinalIgnoreCase))
            {
                cameraPitch = ParseNumber(Next(args, ref i, "pitch"), "pitch", args);
            }
            else if (arg.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                cameraHeight = ParseNumber(Next(args, ref i, "up"), "up", args);
            }
            else if (arg.Equals("campos", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "campos");
                string[] parts = value.Split(',');
                if (parts.Length != 3
                    || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double px)
                    || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double py)
                    || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pz))
                {
                    throw new ArgumentException($"-campos expects X,Y,Z in metres, got '{value}'.", nameof(args));
                }

                cameraPosition = (px, py, pz);
            }
            else if (arg.Equals("viewdist", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "viewdist");
                if (!int.TryParse(value, out terrainViewDistance) || terrainViewDistance < 0)
                {
                    throw new ArgumentException($"-viewdist expects a non-negative number, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("camlog", StringComparison.OrdinalIgnoreCase))
            {
                logCamera = true;
            }
            else if (arg.Equals("freecam", StringComparison.OrdinalIgnoreCase))
            {
                startFlying = true;
            }
            else if (arg.Equals("player", StringComparison.OrdinalIgnoreCase))
            {
                // Walking is the default now, so this is kept only so an existing command line does
                // not fail. It asks for what already happens.
                startFlying = false;
            }
            else if (arg.Equals("screen", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "screen");
                if (!int.TryParse(value, out startScreen) || startScreen < 0)
                {
                    throw new ArgumentException($"-screen expects a screen id, got '{value}'.", nameof(args));
                }
            }
            else
            {
                throw new ArgumentException($"Unknown option '{args[i]}'.", nameof(args));
            }
        }

        // Borderless unless told otherwise. Naming a size is by itself a request for a window, because
        // a borderless one ignores it -- honouring the flag and dropping the size would be the more
        // surprising of the two readings.
        WindowMode resolvedMode = mode ?? (width > 0 ? WindowMode.Windowed : WindowMode.BorderlessFullscreen);

        // A window with no size asked for is the original's 640x480 boot, not the monitor's resolution:
        // a decorated window the size of the whole desktop is nobody's intent. Fullscreen is the other
        // way round, where no size means the monitor's own.
        if (resolvedMode == WindowMode.Windowed && width <= 0)
        {
            width = 640;
            height = 480;
        }

        return new DesktopOptions(
            gamePath,
            resolvedMode,
            width,
            height,
            vsync,
            screenshot,
            exitAfterFrames,
            startScreen,
            startMission,
            logCamera,
            startFlying,
            cameraYaw,
            cameraPitch,
            cameraHeight,
            terrainViewDistance,
            uiScale)
        {
            CameraPosition = cameraPosition,
            Difficulty = difficulty,
        };
    }

    /// <summary>Gets the world position in metres the fly-through camera opens at, or <see langword="null"/> for the level's centre.</summary>
    public (double X, double Y, double Z)? CameraPosition { get; init; }

    /// <summary>Gets the game difficulty every soldier reads its combat row at.</summary>
    /// <remarks>
    /// The original takes it from the active player profile; this shell has no profiles yet, so it is a
    /// command-line option defaulting to <see cref="AiDifficulty.Normal"/> — the script's <c>GD_2</c>.
    /// </remarks>
    public AiDifficulty Difficulty { get; init; }

    /// <summary>Gets the usage text.</summary>
    /// <returns>A short description of every option.</returns>
    public static string Usage =>
        """
        OpenIGI desktop shell.

        usage:
          OpenIGI.Desktop [options]

        options:
          -game <dir>      Original installation to read data from.
                           Defaults to the IGI_GAME_PATH environment variable.
          -borderless      Cover the screen with an undecorated window at the monitor's
                           own resolution and refresh rate. This is the default.
          -window          Run windowed. Implied by -size.
          -fullscreen      Take over the monitor and switch its display mode.
                           -exclusive is the same thing.
          -size WxH        Client size when windowed or exclusive.
                           Default 640x480 windowed, the monitor's own fullscreen.
          -novsync         Do not wait for the vertical blank.
          -screenshot <f>  Write a BMP of the rendered frame and quit. Implies -frames 5.
          -frames <n>      Quit after n rendered frames. 0 runs until closed.
          -uiscale <n>     Magnify the 640x480-authored menus and HUD by a whole number.
                           0 (the default) picks one from the window size; 1 is the original's
                           own size, which on a modern display is very small.
          -screen <id>     Open on this menu screen instead of the one the script names.
                           mainmenu.qvm has 900 and 904-916.
          -campos X,Y,Z    Open the fly-through camera at this world position in metres.
          -difficulty <d>  easy, normal or hard - the settings.qsc rows GD_1, GD_2 and GD_3
                           every soldier reads its combat numbers from. Default normal.
        """;

    private static float ParseNumber(string value, string option, string[] args) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number)
            ? number
            : throw new ArgumentException($"-{option} expects a number, got '{value}'.", nameof(args));

    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"-{option} expects a value.", nameof(args));
        }

        return args[++index];
    }
}
