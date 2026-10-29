using System.Globalization;
using OpenIGI.Game.Ai;
using OpenIGI.Platform;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Configuration;

/// <summary>
/// Reads a command line into <see cref="HostOptions"/>.
/// </summary>
/// <remarks>
/// The option walk lives here so the record stays a description of the options rather than the code
/// that finds them. Each option that takes a value goes through one of the typed readers below, which
/// take the next argument, parse it and throw the option's own <see cref="ArgumentException"/> -- the
/// message a player sees above the usage text -- so the wording of every complaint is unchanged.
/// </remarks>
internal static class HostOptionsParser
{
    /// <summary>Parses a command line.</summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An option is malformed.</exception>
    public static HostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Only what -game says. The environment fallback is the composition root's, because it is per
        // title and this parser does not know which title was chosen -- seeding it here pointed an
        // I.G.I. 2 run at whatever IGI_GAME_PATH named and reported the sequel missing from it.
        string? gamePath = null;
        string? titleId = Environment.GetEnvironmentVariable(HostOptions.TitleVariable);

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
        string? lightingDump = null;
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
        int flightDelay = 0;
        int? captureRate = null;
        float cameraShake = 0f;
        float cameraSwing = 0f;
        int exitAfterFrames = 0;
        int uiScale = Core.Graphics.UiScale.Automatic;
        bool logCamera = false;
        bool lockstep = false;
        List<(int Frame, string Statement)> deferredCommands = [];
        List<string> contentDirectories = [];
        string? contentProfile = null;
        string? streamlineDirectory = null;
        bool noStreamline = false;
        bool streamlineDevelopment = false;
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
        MissionTimeOfDay entryTime = MissionTimeOfDay.Default;
        int computerCamFrame = 0;
        int missionCycleFrames = 0;
        int titleCycleFrames = 0;
        int backdropCycleFrames = 0;
        int presetCycleFrames = 0;
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
                (width, height) = NextSize(args, ref i, "size");
            }
            else if (arg.Equals("mission", StringComparison.OrdinalIgnoreCase))
            {
                startMission = NextInt(args, ref i, "mission", 1, "a mission id");
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
                uiScale = NextInt(args, ref i, "uiscale", 0, "0 for automatic or a positive whole number");
            }
            else if (arg.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                titleId = Next(args, ref i, "title");
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
            else if (arg.Equals("dumplighting", StringComparison.OrdinalIgnoreCase))
            {
                lightingDump = Next(args, ref i, "dumplighting");
            }
            else if (arg.Equals("frames", StringComparison.OrdinalIgnoreCase))
            {
                exitAfterFrames = NextInt(args, ref i, "frames", 0, "a non-negative count");
            }
            else if (arg.Equals("yaw", StringComparison.OrdinalIgnoreCase))
            {
                cameraYaw = NextFloat(args, ref i, "yaw", "a number of radians");
            }
            else if (arg.Equals("pitch", StringComparison.OrdinalIgnoreCase))
            {
                cameraPitch = NextFloat(args, ref i, "pitch");
            }
            else if (arg.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                cameraHeight = NextFloat(args, ref i, "up");
            }
            else if (arg.Equals("campos", StringComparison.OrdinalIgnoreCase))
            {
                cameraPosition = NextDoubleTriple(args, ref i, "campos", "X,Y,Z in metres");
            }
            else if (arg.Equals("sundir", StringComparison.OrdinalIgnoreCase))
            {
                sunDirection = NextFloatTriple(args, ref i, "sundir", "X,Y,Z pointing towards the sun");
            }
            else if (arg.Equals("camend", StringComparison.OrdinalIgnoreCase))
            {
                cameraEnd = NextDoubleTriple(args, ref i, "camend", "X,Y,Z in metres");
            }
            else if (arg.Equals("yawend", StringComparison.OrdinalIgnoreCase))
            {
                cameraYawEnd = NextFloat(args, ref i, "yawend");
            }
            else if (arg.Equals("pitchend", StringComparison.OrdinalIgnoreCase))
            {
                cameraPitchEnd = NextFloat(args, ref i, "pitchend");
            }
            else if (arg.Equals("camshake", StringComparison.OrdinalIgnoreCase))
            {
                cameraShake = NextFloat(args, ref i, "camshake");
                if (cameraShake < 0f)
                {
                    throw new ArgumentException($"-camshake expects a non-negative amplitude in degrees, got '{cameraShake}'.", nameof(args));
                }
            }
            else if (arg.Equals("camswing", StringComparison.OrdinalIgnoreCase))
            {
                // Swing the camera's yaw by this many degrees on every other frame, so frame N and N+2 are the
                // same view. Anything that differs between them changed because the camera looked away and
                // back -- the test for a picture that depends on where the view has been. A development flag.
                cameraSwing = NextFloat(args, ref i, "camswing");
            }
            else if (arg.Equals("flightdelay", StringComparison.OrdinalIgnoreCase))
            {
                flightDelay = NextInt(
                    args, ref i, "flightdelay", 0, "a non-negative frame count", CultureInfo.InvariantCulture);
            }
            else if (arg.Equals("capturerate", StringComparison.OrdinalIgnoreCase))
            {
                captureRate = NextInt(
                    args, ref i, "capturerate", 1, "a positive frame rate", CultureInfo.InvariantCulture);
            }
            else if (arg.Equals("flightframes", StringComparison.OrdinalIgnoreCase))
            {
                flightFrames = NextInt(
                    args, ref i, "flightframes", 1, "a positive frame count", CultureInfo.InvariantCulture);
            }
            else if (arg.Equals("fov", StringComparison.OrdinalIgnoreCase))
            {
                string value = Next(args, ref i, "fov");
                if (!float.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
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
                terrainViewDistance = NextInt(args, ref i, "viewdist", 0, "a non-negative number");
            }
            else if (arg.Equals("camlog", StringComparison.OrdinalIgnoreCase))
            {
                logCamera = true;
            }
            else if (arg.Equals("runat", StringComparison.OrdinalIgnoreCase))
            {
                int runFrame = NextInt(args, ref i, "runat", 0, "a non-negative frame");
                deferredCommands.Add((runFrame, Next(args, ref i, "runat")));
            }
            else if (arg.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                contentDirectories.Add(Next(args, ref i, "content"));
            }
            else if (arg.Equals("contentprofile", StringComparison.OrdinalIgnoreCase))
            {
                contentProfile = Next(args, ref i, "contentprofile");
            }
            else if (arg.Equals("streamline", StringComparison.OrdinalIgnoreCase))
            {
                streamlineDirectory = Next(args, ref i, "streamline");
            }
            else if (arg.Equals("nostreamline", StringComparison.OrdinalIgnoreCase))
            {
                noStreamline = true;
            }
            else if (arg.Equals("streamline-dev", StringComparison.OrdinalIgnoreCase))
            {
                streamlineDevelopment = true;
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
                // shell, the original's renderer, its mix -- 32 voices at 22 kHz with the flat
                // hand-computed pan instead of positioning -- and its canned death animations instead
                // of simulated bodies. The one-render-per-tick 30 Hz presentation is NOT among them:
                // that is a frame-rate cap rather than part of the picture, and only -lockstep keeps it.
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
                entryCamMission = NextInt(args, ref i, "entrycam", 1, "a mission id");
            }
            else if (arg.Equals("entrytime", StringComparison.OrdinalIgnoreCase))
            {
                // The campaign's Time choice for -entrycam, picked through the same path the dropdown
                // takes, so a capture reaches a night mission the way a player does: the backdrop re-lit
                // first, then Play.
                string value = Next(args, ref i, "entrytime");
                if (!Enum.TryParse(value, ignoreCase: true, out entryTime) || !Enum.IsDefined(entryTime))
                {
                    throw new ArgumentException($"-entrytime expects default, day or night, got '{value}'.", nameof(args));
                }
            }
            else if (arg.Equals("computercam", StringComparison.OrdinalIgnoreCase))
            {
                // Press C by hand once the mission is up, for the same reason -entrycam presses Play:
                // the computer's transition is a second and a half of camera move and display boot, and
                // there is no other way to capture it starting from a frame anybody can repeat.
                computerCamFrame = NextInt(args, ref i, "computercam", 0, "a frame number");
            }
            else if (arg.Equals("missioncycle", StringComparison.OrdinalIgnoreCase))
            {
                // Abandon to the campaign and play again, every n frames, for as long as the run lasts.
                // Leaving a mission tears down every renderer the level owns and every structure the
                // traced world accumulated, and doing that by hand is the one path a capture could not
                // reach -- so the cost of it, and whether it survives being done repeatedly, could not
                // be measured. A development flag, like -entrycam and -computercam.
                missionCycleFrames = NextInt(args, ref i, "missioncycle", 1, "a frame count");
            }
            else if (arg.Equals("titlecycle", StringComparison.OrdinalIgnoreCase))
            {
                // Change GAME every n frames, for as long as the run lasts. -missioncycle and
                // -backdropcycle's counterpart one level up: a title switch replaces the task loop, the
                // resource registry, the mission table, the content session and every renderer at once,
                // which is the largest teardown the session has, and choosing another game from the menu
                // is the only other way to reach it. A development flag.
                titleCycleFrames = NextInt(args, ref i, "titlecycle", 1, "a frame count");
            }
            else if (arg.Equals("backdropcycle", StringComparison.OrdinalIgnoreCase))
            {
                // Walk the campaign's rows in the menu, every n frames, for as long as the run lasts.
                // -missioncycle's counterpart on the other side of Play: swapping a backdrop replaces a
                // whole level's worth of renderers, meshes and traced structures WITHOUT leaving the menu,
                // and that path had no capture at all -- it could only be reached by clicking down the
                // list by hand, which is exactly how it was found to lose the device. A development flag.
                backdropCycleFrames = NextInt(args, ref i, "backdropcycle", 1, "a frame count");
            }
            else if (arg.Equals("presetcycle", StringComparison.OrdinalIgnoreCase))
            {
                // Apply the next graphics preset every n frames, for as long as the run lasts: the same
                // path as choosing one in the settings, which swaps whole render paths and their targets
                // while the loop keeps presenting. A development flag.
                presetCycleFrames = NextInt(args, ref i, "presetcycle", 1, "a frame count");
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
                startScreen = NextInt(args, ref i, "screen", 0, "a screen id");
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

        return new HostOptions(
            gamePath,
            string.IsNullOrWhiteSpace(titleId) ? null : titleId.Trim(),
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
            LightingDumpPath = lightingDump,
            FieldOfView = fieldOfView,
            CameraEnd = cameraEnd,
            CameraYawEnd = cameraYawEnd,
            CameraPitchEnd = cameraPitchEnd,
            FlightFrames = flightFrames,
            FlightDelay = flightDelay,
            CaptureRate = captureRate,
            CameraShake = cameraShake,
            CameraSwing = cameraSwing,
            Difficulty = difficulty,
            StartupCommands = startupCommands,
            DeferredCommands = deferredCommands,
            ContentDirectories = contentDirectories,
            ContentProfilePath = contentProfile,
            StreamlineDirectory = streamlineDirectory,
            NoStreamline = noStreamline,
            StreamlineDevelopment = streamlineDevelopment,
            VanillaMenu = vanillaMenu,
            ShellTab = shellTab,
            WindowFromCommandLine = mode is not null || width > 0,
            EntryCamMission = entryCamMission,
            EntryTime = entryTime,
            ComputerCamFrame = computerCamFrame,
            MissionCycleFrames = missionCycleFrames,
            TitleCycleFrames = titleCycleFrames,
            BackdropCycleFrames = backdropCycleFrames,
            PresetCycleFrames = presetCycleFrames,
            ShowcasePointDump = showcasePointDump,
            PlayCutscenes = playCutscenes,
            BenchmarkPath = benchmarkPath,
            BakeAll = bakeAll,
        };
    }

    /// <summary>Takes the value that follows an option.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The option is the last argument.</exception>
    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"-{option} expects a value.", nameof(args));
        }

        return args[++index];
    }

    /// <summary>Takes a whole number no smaller than <paramref name="minimum"/>.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <param name="minimum">The smallest value the option accepts.</param>
    /// <param name="expects">What the option wants, as the message words it.</param>
    /// <param name="culture">
    /// The culture to read the digits in; <see langword="null"/> reads them the way a bare
    /// <see cref="int.TryParse(string?, out int)"/> does, in the current culture.
    /// </param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The value is missing, not a number or too small.</exception>
    private static int NextInt(
        string[] args, ref int index, string option, int minimum, string expects, CultureInfo? culture = null)
    {
        string value = Next(args, ref index, option);
        int number;
        bool parsed = culture is null
            ? int.TryParse(value, out number)
            : int.TryParse(value, NumberStyles.Integer, culture, out number);
        if (!parsed || number < minimum)
        {
            throw new ArgumentException($"-{option} expects {expects}, got '{value}'.", nameof(args));
        }

        return number;
    }

    /// <summary>Takes a number in the invariant culture.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <param name="expects">What the option wants, as the message words it.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The value is missing or not a number.</exception>
    private static float NextFloat(string[] args, ref int index, string option, string expects = "a number")
    {
        string value = Next(args, ref index, option);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : throw new ArgumentException($"-{option} expects {expects}, got '{value}'.", nameof(args));
    }

    /// <summary>Takes a <c>WIDTHxHEIGHT</c> size whose sides are both positive.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <returns>The width and height.</returns>
    /// <exception cref="ArgumentException">The value is missing or not a size.</exception>
    private static (int Width, int Height) NextSize(string[] args, ref int index, string option)
    {
        string value = Next(args, ref index, option);
        string[] parts = value.Split('x', 'X');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int width)
            || !int.TryParse(parts[1], out int height)
            || width <= 0
            || height <= 0)
        {
            throw new ArgumentException($"-{option} expects WIDTHxHEIGHT, got '{value}'.", nameof(args));
        }

        return (width, height);
    }

    /// <summary>Takes three comma-separated numbers at double precision, in the invariant culture.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <param name="expects">What the option wants, as the message words it.</param>
    /// <returns>The three numbers.</returns>
    /// <exception cref="ArgumentException">The value is missing or not three numbers.</exception>
    private static (double X, double Y, double Z) NextDoubleTriple(
        string[] args, ref int index, string option, string expects)
    {
        string value = Next(args, ref index, option);
        string[] parts = value.Split(',');
        if (parts.Length != 3
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
        {
            throw new ArgumentException($"-{option} expects {expects}, got '{value}'.", nameof(args));
        }

        return (x, y, z);
    }

    /// <summary>Takes three comma-separated numbers at single precision, in the invariant culture.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="index">The option's index, advanced past the value.</param>
    /// <param name="option">The option's name without its dash, for the message.</param>
    /// <param name="expects">What the option wants, as the message words it.</param>
    /// <returns>The three numbers.</returns>
    /// <exception cref="ArgumentException">The value is missing or not three numbers.</exception>
    private static (float X, float Y, float Z) NextFloatTriple(
        string[] args, ref int index, string option, string expects)
    {
        string value = Next(args, ref index, option);
        string[] parts = value.Split(',');
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            throw new ArgumentException($"-{option} expects {expects}, got '{value}'.", nameof(args));
        }

        return (x, y, z);
    }
}
