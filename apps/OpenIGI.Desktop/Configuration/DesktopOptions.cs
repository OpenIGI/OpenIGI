using OpenIGI.Game.Ai;
using OpenIGI.Platform;
using OpenIGI.Rendering;

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
/// <param name="Lockstep">Whether the simulation is driven by a synthetic clock so a run is reproducible.</param>
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
    bool Lockstep,
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

        // Null until -difficulty is seen: the faithful source is the active player profile (the original's
        // sub_406F70 reads sub_406220()'s first field), so the flag is only a test override, not a default.
        AiDifficulty? difficulty = null;

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
        (float X, float Y, float Z)? sunDirection = null;

        // Null until a flight end pose is named. When any of the three is set the free camera is flown
        // from its opening pose to this one over the frame budget, and -screenshot writes one numbered
        // frame per rendered frame instead of a single still -- the only way to see the temporal filter's
        // in-motion behaviour, which a static capture cannot show.
        (double X, double Y, double Z)? cameraEnd = null;
        float? cameraYawEnd = null;
        float? cameraPitchEnd = null;
        int? flightFrames = null;
        float cameraShake = 0f;
        int exitAfterFrames = 0;
        int uiScale = Core.Graphics.UiScale.Automatic;
        bool logCamera = false;
        bool lockstep = false;
        bool startFlying = false;
        bool playCutscenes = true;
        float cameraYaw = 0f;
        float cameraPitch = -0.12f;
        float cameraHeight = 0f;
        int terrainViewDistance = 0;
        float? fieldOfView = null;
        int startScreen = 0;
        int startMission = 0;
        bool vanillaMenu = false;
        string? shellTab = null;
        int entryCamMission = 0;
        int computerCamFrame = 0;
        int missionCycleFrames = 0;
        string? showcasePointDump = null;
        string? benchmarkPath = null;
        bool bakeAll = false;

        List<string> startupCommands = [];

        for (int i = 0; i < args.Length; i++)
        {
            // `+name value...` is a console statement to run once the console exists, the convention
            // every game with a console uses. Its arguments run to the next `+` or `-`, so
            // `+gfx_preset ultra +r_fog_quality 4 -window` reads as two statements and a flag.
            if (args[i].StartsWith('+'))
            {
                List<string> statement = [args[i][1..]];
                while (i + 1 < args.Length && !args[i + 1].StartsWith('+') && !args[i + 1].StartsWith('-'))
                {
                    statement.Add(args[++i]);
                }

                if (statement[0].Length > 0)
                {
                    startupCommands.Add(string.Join(' ', statement));
                }

                continue;
            }

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
            else if (arg.Equals("sundir", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "sundir");
                string[] parts = value.Split(',');
                if (parts.Length != 3
                    || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sx)
                    || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sy)
                    || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sz))
                {
                    throw new ArgumentException($"-sundir expects X,Y,Z pointing towards the sun, got '{value}'.", nameof(args));
                }

                sunDirection = (sx, sy, sz);
            }
            else if (arg.Equals("camend", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "camend");
                string[] parts = value.Split(',');
                if (parts.Length != 3
                    || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ex)
                    || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ey)
                    || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ez))
                {
                    throw new ArgumentException($"-camend expects X,Y,Z in metres, got '{value}'.", nameof(args));
                }

                cameraEnd = (ex, ey, ez);
            }
            else if (arg.Equals("yawend", StringComparison.OrdinalIgnoreCase))
            {
                cameraYawEnd = ParseNumber(Next(args, ref i, "yawend"), "yawend", args);
            }
            else if (arg.Equals("pitchend", StringComparison.OrdinalIgnoreCase))
            {
                cameraPitchEnd = ParseNumber(Next(args, ref i, "pitchend"), "pitchend", args);
            }
            else if (arg.Equals("camshake", StringComparison.OrdinalIgnoreCase))
            {
                cameraShake = ParseNumber(Next(args, ref i, "camshake"), "camshake", args);
                if (cameraShake < 0f)
                {
                    throw new ArgumentException($"-camshake expects a non-negative amplitude in degrees, got '{cameraShake}'.", nameof(args));
                }
            }
            else if (arg.Equals("flightframes", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "flightframes");
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int ff)
                    || ff <= 0)
                {
                    throw new ArgumentException($"-flightframes expects a positive frame count, got '{value}'.", nameof(args));
                }

                flightFrames = ff;
            }
            else if (arg.Equals("fov", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "fov");
                if (!float.TryParse(
                        value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsedFov)
                    || parsedFov <= 1f
                    || parsedFov >= 179f)
                {
                    throw new ArgumentException($"-fov expects horizontal degrees between 1 and 179, got '{value}'.", nameof(args));
                }

                fieldOfView = parsedFov;
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
            else if (arg.Equals("lockstep", StringComparison.OrdinalIgnoreCase))
            {
                lockstep = true;
            }
            else if (arg.Equals("benchmark", StringComparison.OrdinalIgnoreCase))
            {
                benchmarkPath = Next(args, ref i, "benchmark");
                lockstep = true;
                vsync = false;
                exitAfterFrames = BenchmarkSchedule.TotalFrames;
                startupCommands.Add("r_perflog 1");
            }
            else if (arg.Equals("freecam", StringComparison.OrdinalIgnoreCase))
            {
                startFlying = true;
            }
            else if (arg.Equals("cutscene", StringComparison.OrdinalIgnoreCase))
            {
                playCutscenes = true;
            }
            else if (arg.Equals("skipcutscene", StringComparison.OrdinalIgnoreCase))
            {
                playCutscenes = false;
            }
            else if (arg.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
            {
                // The whole remaster track off in one word: the retail menus instead of the cinematic
                // shell, the original's renderer and one-render-per-tick 30 Hz presentation, its mix --
                // 32 voices at 22 kHz with the flat hand-computed pan instead of positioning -- and its
                // canned death animations instead of simulated bodies.
                vanillaMenu = true;
                startupCommands.Add("gfx_preset vanilla");
                startupCommands.Add("snd_preset vanilla");
                startupCommands.Add("g_preset vanilla");
            }
            else if (arg.Equals("vanillamenu", StringComparison.OrdinalIgnoreCase))
            {
                // Just the front end: the byte-for-byte retail menu system, but the modern renderer.
                vanillaMenu = true;
            }
            else if (arg.Equals("poidump", StringComparison.OrdinalIgnoreCase))
            {
                // Walk every mission, find the places its backdrop should circle, write them out and quit.
                // A tool, not a game mode -- the file it writes is then owned and edited by hand.
                showcasePointDump = Next(args, ref i, "poidump");
            }
            else if (arg.Equals("shelltab", StringComparison.OrdinalIgnoreCase))
            {
                // The cinematic shell's counterpart to -screen: open straight on a section rather than on
                // the landing page, so a capture of the campaign or the settings does not need someone to
                // click their way there first.
                shellTab = Next(args, ref i, "shelltab");
            }
            else if (arg.Equals("entrycam", StringComparison.OrdinalIgnoreCase))
            {
                // Boot the shell, wait for the mission's backdrop, then press Play by hand -- so the
                // menu-to-mission entry can be measured and captured without somebody clicking at an
                // unrepeatable moment. A development flag, like -shelltab and -screen, not a game mode.
                string value = Next(args, ref i, "entrycam");
                if (!int.TryParse(value, out entryCamMission) || entryCamMission <= 0)
                {
                    throw new ArgumentException($"-entrycam expects a mission id, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("computercam", StringComparison.OrdinalIgnoreCase))
            {
                // Press C by hand once the mission is up, for the same reason -entrycam presses Play:
                // the computer's transition is a second and a half of camera move and display boot, and
                // there is no other way to capture it starting from a frame anybody can repeat.
                string value = Next(args, ref i, "computercam");
                if (!int.TryParse(value, out computerCamFrame) || computerCamFrame < 0)
                {
                    throw new ArgumentException($"-computercam expects a frame number, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("missioncycle", StringComparison.OrdinalIgnoreCase))
            {
                // Abandon to the campaign and play again, every n frames, for as long as the run lasts.
                // Leaving a mission tears down every renderer the level owns and every structure the
                // traced world accumulated, and doing that by hand is the one path a capture could not
                // reach -- so the cost of it, and whether it survives being done repeatedly, could not
                // be measured. A development flag, like -entrycam and -computercam.
                string value = Next(args, ref i, "missioncycle");
                if (!int.TryParse(value, out missionCycleFrames) || missionCycleFrames <= 0)
                {
                    throw new ArgumentException(
                        $"-missioncycle expects a frame count, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("vanillarender", StringComparison.OrdinalIgnoreCase))
            {
                // Just the renderer: the cinematic shell over the original's render path.
                startupCommands.Add("gfx_preset vanilla");
            }
            else if (arg.Equals("bakeall", StringComparison.OrdinalIgnoreCase))
            {
                // Trace every mission's global-illumination probe field and write it to the cache, then
                // quit. Run once; the .giv files can then be shipped so no player traces a level.
                bakeAll = true;
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

        // A flight is a free-camera move: the walking player never takes an authored path, so naming an
        // end pose is by itself a request to fly, the same way -size is a request for a window.
        if (cameraEnd is not null || cameraYawEnd is not null || cameraPitchEnd is not null || cameraShake > 0f)
        {
            startFlying = true;
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
            lockstep,
            startFlying,
            cameraYaw,
            cameraPitch,
            cameraHeight,
            terrainViewDistance,
            uiScale)
        {
            CameraPosition = cameraPosition,
            SunDirection = sunDirection,
            FieldOfView = fieldOfView,
            CameraEnd = cameraEnd,
            CameraYawEnd = cameraYawEnd,
            CameraPitchEnd = cameraPitchEnd,
            FlightFrames = flightFrames,
            CameraShake = cameraShake,
            Difficulty = difficulty,
            StartupCommands = startupCommands,
            VanillaMenu = vanillaMenu,
            ShellTab = shellTab,
            EntryCamMission = entryCamMission,
            ComputerCamFrame = computerCamFrame,
            MissionCycleFrames = missionCycleFrames,
            ShowcasePointDump = showcasePointDump,
            PlayCutscenes = playCutscenes,
            BenchmarkPath = benchmarkPath,
            BakeAll = bakeAll,
        };
    }

    /// <summary>
    /// Gets the mission whose menu-to-mission entry runs by itself, or <c>0</c> for none. Set by
    /// <c>-entrycam</c>.
    /// </summary>
    /// <remarks>
    /// The shell boots on the campaign with this mission selected, waits for its backdrop to finish
    /// building and arming, and then presses Play as though a player had. A development shortcut, not a
    /// game feature: the entry is the one sequence whose cost and appearance cannot be checked without
    /// somebody clicking at a moment nobody can repeat, and a capture of it has to start on a known frame.
    /// </remarks>
    public int EntryCamMission { get; init; }

    /// <summary>
    /// Gets the frame the map computer opens itself on, or <c>0</c> for never. Set by <c>-computercam</c>.
    /// </summary>
    /// <remarks>
    /// The computer's counterpart to <see cref="EntryCamMission"/>, and a development shortcut for the
    /// same reason: opening it runs a second and a half of camera move and display boot, and a capture of
    /// that has to begin on a frame somebody can ask for again. Paired with <c>-frames</c> and
    /// <c>-screenshot</c> it writes the whole transition as a numbered sequence.
    /// </remarks>
    public int ComputerCamFrame { get; init; }

    /// <summary>
    /// Gets how many frames each leg of an abandon/replay cycle lasts, or <c>0</c> for never. Set by
    /// <c>-missioncycle</c>.
    /// </summary>
    /// <remarks>
    /// A development shortcut in the same family as <see cref="EntryCamMission"/>: leaving a mission is a
    /// teardown of every renderer and every accelerator the level built, and entering one is all of it
    /// again. Repeating the pair on a known cadence is what makes the cost of the transition, and
    /// anything it fails to release, something a run can measure rather than something a player reports.
    /// </remarks>
    public int MissionCycleFrames { get; init; }

    /// <summary>
    /// Gets the cinematic shell's section to open on — <c>campaign</c>, <c>settings</c>, <c>profile</c> or
    /// <c>extras</c> — or <see langword="null"/> for the landing page. Set by <c>-shelltab</c>.
    /// </summary>
    /// <remarks>A development shortcut, the shell's counterpart to <c>-screen</c>; not a game feature.</remarks>
    public string? ShellTab { get; init; }

    /// <summary>
    /// Gets the file every mission's showcase places are written to, or <see langword="null"/> to run the
    /// game. Set by <c>-poidump</c>.
    /// </summary>
    /// <remarks>
    /// A tool: it builds each mission in turn, finds the places its menu backdrop should circle, writes
    /// them out and quits. What it produces is then hand-owned — see <see cref="ShowcasePointFile"/>.
    /// </remarks>
    public string? ShowcasePointDump { get; init; }

    /// <summary>Gets the world position in metres the fly-through camera opens at, or <see langword="null"/> for the level's centre.</summary>
    public (double X, double Y, double Z)? CameraPosition { get; init; }

    /// <summary>Gets a fixed sun direction (pointing towards the sun) that overrides the level's, or <see langword="null"/> to use the level's own. For matching an external reference render.</summary>
    public (float X, float Y, float Z)? SunDirection { get; init; }

    /// <summary>
    /// Gets a fixed horizontal field of view in degrees, or null for the game's own ninety. For lining a
    /// capture up against a reference render whose camera lens differs.
    /// </summary>
    public float? FieldOfView { get; init; }

    /// <summary>
    /// Gets the world position in metres the fly-through camera ends a flight at, or <see langword="null"/>
    /// for no flight. Set by <c>-camend</c>; see <see cref="HasFlight"/>.
    /// </summary>
    public (double X, double Y, double Z)? CameraEnd { get; init; }

    /// <summary>Gets the yaw in radians the flight ends on, or <see langword="null"/> to hold the opening yaw. Set by <c>-yawend</c>.</summary>
    public float? CameraYawEnd { get; init; }

    /// <summary>Gets the pitch in radians the flight ends on, or <see langword="null"/> to hold the opening pitch. Set by <c>-pitchend</c>.</summary>
    public float? CameraPitchEnd { get; init; }

    /// <summary>
    /// Gets how many frames the flight itself takes, or <see langword="null"/> to spread it over the whole
    /// capture. Set by <c>-flightframes</c>.
    /// </summary>
    /// <remarks>
    /// The frames after it hold the end pose, which is what makes a flight able to come to a stop. That
    /// matters for ghosting: a temporal filter drags a trail behind moving content, and a trail is only
    /// measurable once the motion has stopped and the picture is expected to have settled. Comparing a
    /// held frame against the same pose reached standing still measures exactly the smear and nothing else.
    /// </remarks>
    public int? FlightFrames { get; init; }

    /// <summary>
    /// Gets the amplitude in degrees of a per-frame camera shake laid over whatever else the camera is
    /// doing, or zero for none. Set by <c>-camshake</c>.
    /// </summary>
    /// <remarks>
    /// A smooth flight is not what a temporal filter actually has to survive. A player's view is never
    /// still and never moves evenly: it jitters by a pixel or two at a time and changes direction
    /// constantly, which is the case that breaks reprojection — a smooth pan gives every pixel the same
    /// velocity for many frames, so a history that reprojects badly still reprojects consistently. This
    /// lays a deterministic pseudo-random angular offset over the pose, so a run stays reproducible under
    /// <c>-lockstep</c> while the motion is no longer something the filter can settle into.
    /// </remarks>
    public float CameraShake { get; init; }

    /// <summary>
    /// Gets a value indicating whether a scripted free-camera flight is asked for — an end pose the opening
    /// camera is flown to over the frame budget. When set, <c>-screenshot</c> writes one numbered frame per
    /// rendered frame rather than a single still, so the in-motion behaviour of the temporal filters is
    /// visible in the sequence.
    /// </summary>
    public bool HasFlight
        => CameraEnd is not null || CameraYawEnd is not null || CameraPitchEnd is not null || CameraShake > 0f;

    /// <summary>
    /// Gets the difficulty override from <c>-difficulty</c>, or <see langword="null"/> to use the active
    /// player profile's — which is what the original does (<c>sub_406F70</c> reads <c>sub_406220()</c>'s
    /// first field). The flag exists only to force a difficulty for testing without editing a profile.
    /// </summary>
    public AiDifficulty? Difficulty { get; init; }

    /// <summary>
    /// Gets the console statements given as <c>+name value</c> arguments, in the order they appeared.
    /// </summary>
    /// <remarks>
    /// Run after <c>settings.cfg</c> has loaded, so a command line always wins over a saved setting
    /// without overwriting it — closing the game saves whatever the command line put in place, which
    /// is the behaviour every console game has.
    /// </remarks>
    public IReadOnlyList<string> StartupCommands { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the byte-for-byte retail menu system is used instead of the
    /// cinematic shell. Set by <c>-vanilla</c> and <c>-vanillamenu</c>.
    /// </summary>
    /// <remarks>
    /// Independent of the render path: <c>-vanillamenu</c> gives the retail menus over the modern
    /// renderer, while <c>-vanillarender</c> gives the shell over the original's render path.
    /// <c>-vanilla</c> asks for both.
    /// </remarks>
    public bool VanillaMenu { get; init; }

    /// <summary>
    /// Gets a value indicating whether authored mission cinematics play instead of being skipped during
    /// mission start-up. Cinematics are enabled by default; <c>-cutscene</c> remains accepted for command
    /// line compatibility with parity captures made while the system was opt-in.
    /// </summary>
    public bool PlayCutscenes { get; init; }

    /// <summary>Gets the JSON output path for a deterministic 120 Hz/30 Hz benchmark.</summary>
    public string? BenchmarkPath { get; init; }

    /// <summary>Gets a value indicating whether deterministic performance capture is active.</summary>
    public bool IsBenchmark => BenchmarkPath is not null;

    /// <summary>
    /// Gets a value indicating whether every mission's global-illumination probe field is traced and
    /// written to the cache and the game then quits, set by <c>-bakeall</c>.
    /// </summary>
    public bool BakeAll { get; init; }

    /// <summary>Gets the usage text.</summary>
    /// <returns>A short description of every option.</returns>
    public static string Usage =>
        """
        OpenIGI desktop shell.

        usage:
          OpenIGI.Desktop [options]

        options:
          -game <dir>      Original installation to read data from. Defaults to the
                           IGI_GAME_PATH environment variable, and failing that to an
                           installation found beside the executable or in the working
                           directory -- so putting OpenIGI in the game's own folder is
                           enough on its own.
          -borderless      Cover the screen with an undecorated window at the monitor's
                           own resolution and refresh rate. This is the default.
          -window          Run windowed. Implied by -size.
          -fullscreen      Take over the monitor and switch its display mode.
                           -exclusive is the same thing.
          -size WxH        Client size when windowed or exclusive.
                           Default 640x480 windowed, the monitor's own fullscreen.
          -novsync         Do not wait for the vertical blank.
          -opengl          Draw with the legacy OpenGL backend. Vulkan is the default,
                           and -vulkan asks for it explicitly. OPENIGI_BACKEND=opengl
                           does the same as -opengl for a whole shell.
          -screenshot <f>  Write a BMP of the rendered frame and quit. Implies -frames 5.
          -frames <n>      Quit after n rendered frames. 0 runs until closed.
          -lockstep        Drive the simulation from a synthetic clock, one tick per loop pass,
                           instead of the wall clock. A run then depends only on its arguments,
                           which is what makes a capture reproducible. Capture only -- it uncaps
                           the simulation rate and is not how the game is meant to be played.
          -benchmark <f>   Run 120 warm-up and 600 measured frames on a deterministic 120 Hz
                           presentation clock with 30 Hz simulation, then write JSON to <f>.
          -cutscene        Play the mission's authored opening cinematic. Cinematics already play by
                           default; this legacy flag remains accepted for capture-script compatibility.
          -skipcutscene    Skip the authored opening cinematic for scene benchmarks and references.
          -uiscale <n>     Magnify the 640x480-authored menus and HUD by a whole number.
                           0 (the default) picks one from the window size; 1 is the original's
                           own size, which on a modern display is very small.
          -screen <id>     Open on this menu screen instead of the one the script names.
                           mainmenu.qvm has 900 and 904-916.
          -campos X,Y,Z    Open the fly-through camera at this world position in metres.
          -camend X,Y,Z    Fly the free camera from its opening pose to this one over the frame
                           budget (implies -freecam). With -screenshot it writes one numbered
                           frame per rendered frame, for a moving-camera sequence -- the only way
                           to judge the temporal filters (GI, TAA) in motion. Pair with -frames.
          -yawend <rad>    Yaw the flight ends on. Defaults to the opening yaw (-yaw).
          -pitchend <rad>  Pitch the flight ends on. Defaults to the opening pitch (-pitch).
          -flightframes N  Finish the flight in N frames and hold the end pose for the rest of the
                           capture. Defaults to spreading the flight over every frame.
          -camshake <deg>  Lay a handheld-camera shake of this peak amplitude over the pose, drifting
                           the way a hand does rather than jittering randomly. Implies a flight, so it
                           can be used on its own to shake a fixed viewpoint. Try 0.15 for a steady
                           hold, 0.5 for a walk, 1.5 for a run.
          -difficulty <d>  easy, normal or hard - the settings.qsc rows GD_1, GD_2 and GD_3
                           every soldier reads its combat numbers from. Default normal.
          -vanilla         The whole remaster track off: the retail menu system and the original's
                           picture, pacing and mix (implies -vanillamenu, +gfx_preset vanilla and
                           +snd_preset vanilla, including the original's 30 Hz between-tick
                           presentation and its 32-voice flat-panned audio).
          -vanillamenu     The byte-for-byte retail menu system instead of the cinematic shell,
                           over the modern renderer.
          -shelltab <s>    Open the cinematic shell on a section instead of its landing page:
                           campaign, settings, profile or extras. The shell's -screen.
          -entrycam <n>    Boot the shell on mission <n>, wait for its backdrop, then press Play by
                           itself, so the menu-to-mission entry can be measured and captured from a
                           known frame rather than from whenever somebody clicked.
          -missioncycle N  Abandon the mission to the campaign and play it again every N frames, so the
                           cost of leaving a level -- and whether it releases what it built -- can be
                           measured over many cycles instead of clicked through by hand.
          -computercam <n> Open the map computer by itself on frame <n> of a mission, so its camera
                           move and display boot can be captured from a known frame. Pair with
                           -frames and -screenshot for the whole transition as a sequence.
          -poidump <f>     Find the places each mission's menu backdrop should circle, write them
                           to <f> as menu-cameras.json, and quit. Run once; the file it writes is
                           then edited by hand. Without it the places are found at load instead.
          -vanillarender   The cinematic shell over the original's render path
                           (shorthand for +gfx_preset vanilla).
          -bakeall         Trace every mission's global-illumination probe field, write it to the
                           cache and quit. Run once; ship the .giv files so no player traces a level.
          +<command>       Run a console statement at start-up, after settings.cfg has loaded.
                           Arguments run to the next + or -, so
                             +gfx_preset ultra +r_scale 0.8
                           is two statements. Press ` in game for the console, and
                           'cvarlist' or 'find <text>' to see what there is.
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
