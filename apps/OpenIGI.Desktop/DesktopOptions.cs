namespace OpenIGI.Desktop;

/// <summary>
/// The command-line options the desktop shell understands.
/// </summary>
/// <param name="GamePath">The original installation to read data from.</param>
/// <param name="Fullscreen">Whether to open fullscreen.</param>
/// <param name="Width">The window client width in pixels.</param>
/// <param name="Height">The window client height in pixels.</param>
/// <param name="VSync">Whether presentation waits for the vertical blank.</param>
/// <param name="ScreenshotPath">Where to write a capture of the first rendered frames, or <see langword="null"/> to run normally.</param>
/// <param name="ExitAfterFrames">Quit after this many rendered frames, or zero to run until closed.</param>
/// <param name="StartScreen">Open on this menu screen id instead of the one the script names, or zero.</param>
/// <param name="StartMission">Load this mission at boot instead of opening the menus, or zero.</param>
/// <remarks>
/// The original parses its command line with a case-insensitive prefix scan (<c>Win_ParseCmdOption</c>,
/// <c>0x48F360</c>) and understands <c>Window</c>, <c>WinInput</c>, <c>Germany</c> and
/// <c>HardcoreGerman</c>. <c>-window</c> is kept with the same meaning; the rest are additions the
/// original had no need for because it shipped alongside its data.
/// </remarks>
public readonly record struct DesktopOptions(
    string? GamePath,
    bool Fullscreen,
    int Width,
    int Height,
    bool VSync,
    string? ScreenshotPath,
    int ExitAfterFrames,
    int StartScreen,
    int StartMission)
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
        bool fullscreen = false;
        bool vsync = true;
        int width = 640;
        int height = 480;
        string? screenshot = null;
        int exitAfterFrames = 0;
        int startScreen = 0;
        int startMission = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].TrimStart('-', '/');

            if (arg.Equals("window", StringComparison.OrdinalIgnoreCase))
            {
                fullscreen = false;
            }
            else if (arg.Equals("fullscreen", StringComparison.OrdinalIgnoreCase))
            {
                fullscreen = true;
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

        return new DesktopOptions(gamePath, fullscreen, width, height, vsync, screenshot, exitAfterFrames, startScreen, startMission);
    }

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
          -window          Run windowed. This is the default.
          -fullscreen      Run fullscreen.
          -size WxH        Client size when windowed. Default 640x480.
          -novsync         Do not wait for the vertical blank.
          -screenshot <f>  Write a BMP of the rendered frame and quit. Implies -frames 5.
          -frames <n>      Quit after n rendered frames. 0 runs until closed.
          -screen <id>     Open on this menu screen instead of the one the script names.
                           mainmenu.qvm has 900 and 904-916.
        """;

    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"-{option} expects a value.", nameof(args));
        }

        return args[++index];
    }
}
