using OpenIGI.Game.Ai;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Host.Configuration;

/// <summary>
/// The command-line options the desktop shell understands.
/// </summary>
/// <param name="GamePath">The original installation to read data from.</param>
/// <param name="Title">Which game to run, by id, or null to work it out from what is installed.</param>
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
public readonly record struct HostOptions(
    string? GamePath,
    string? Title,
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
    /// <summary>The environment variable that says where the first game is installed.</summary>
    /// <remarks>
    /// Named here and read by the composition root, not by <see cref="Parse"/>. The path is per title --
    /// a machine may well have both games installed -- so which variable to consult is a question only
    /// the place that resolved the title can answer, and <see cref="GamePath"/> is therefore what the
    /// command line said and nothing else.
    /// </remarks>
    public static string GamePathVariable => Core.Titles.GameTitle.Igi1.GamePathVariable;

    /// <summary>The environment variable that names the game to run, when <c>-title</c> does not.</summary>
    /// <remarks>
    /// The path variable is per title and this one is not, because a machine may well have both games
    /// installed and wants to say which to run without retyping where either is.
    /// </remarks>
    public const string TitleVariable = "OPENIGI_TITLE";

    /// <summary>Parses a command line.</summary>
    /// <param name="args">The arguments, without the executable name.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An option is malformed.</exception>
    public static HostOptions Parse(string[] args) => HostOptionsParser.Parse(args);

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

    /// <summary>Gets the time of day <c>-entrycam</c> chooses on the campaign before pressing Play.</summary>
    /// <remarks>
    /// Chosen through the dropdown's own path, a second before Play, so the backdrop is re-lit under the
    /// menu first as it is for a player. Set by <c>-entrytime</c>.
    /// </remarks>
    public MissionTimeOfDay EntryTime { get; init; }

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

    /// <summary>Gets the console statements to run once, each on the frame it names.</summary>
    /// <remarks>
    /// <c>+</c> statements run at start-up, before any level exists, so they cannot reach anything a
    /// mission owns. This is the same idea for a loaded world: a capture can put a bullet mark, a dust
    /// puff or a wound exactly where it wants one and photograph the result on a frame it can ask for
    /// again.
    /// </remarks>
    public IReadOnlyList<(int Frame, string Statement)> DeferredCommands { get; init; } = [];

    /// <summary>Gets the content folders named with <c>-content</c>, highest priority first.</summary>
    /// <remarks>
    /// For development: a folder that mirrors the game's paths, stacked above every layer the content
    /// profile brings, for every title. Repeat the option to stack several; the first named wins.
    /// </remarks>
    public IReadOnlyList<string> ContentDirectories { get; init; } = [];

    /// <summary>Gets the content profile named with <c>-contentprofile</c>, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The launcher's per-game load order. When absent the game falls back to the file named by
    /// <c>OPENIGI_CONTENT_PROFILE</c>, and failing that to the game folder's own <c>overrides</c> alone.
    /// </remarks>
    public string? ContentProfilePath { get; init; }

    /// <summary>Gets the NVIDIA Streamline folder named with <c>-streamline</c>, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Overrides where the optional DLSS runtime is looked for. Without it the launcher's runtime pack is
    /// used (<c>OPENIGI_STREAMLINE_PATH</c>), and failing that a development checkout's
    /// <c>third-party/streamline</c>.
    /// </remarks>
    public string? StreamlineDirectory { get; init; }

    /// <summary>Gets a value indicating whether <c>-nostreamline</c> turned the DLSS runtime off for this run.</summary>
    public bool NoStreamline { get; init; }

    /// <summary>Gets a value indicating whether <c>-streamline-dev</c> asked for NVIDIA's development runtime.</summary>
    /// <remarks>Its debug overlays and JSON configuration; never what a player runs.</remarks>
    public bool StreamlineDevelopment { get; init; }

    /// <summary>Gets the graphics preset a player with no saved settings starts on.</summary>
    /// <remarks>
    /// Enhanced unless the platform says otherwise. Only a first run reads it: once there is a settings
    /// file, the file decides.
    /// </remarks>
    public GraphicsPreset FirstRunPreset { get; init; } = GraphicsPreset.Enhanced;

    /// <summary>
    /// Gets the one graphics preset this build allows, or <see langword="null"/> to let the player choose.
    /// </summary>
    /// <remarks>
    /// A property of the build rather than a command-line option: the browser build is held on
    /// <see cref="GraphicsPreset.Vanilla"/>. It is applied over any saved settings, the menu stops offering
    /// the others and <c>gfx_preset</c> refuses them. When set, <see cref="FirstRunPreset"/> is not read.
    /// </remarks>
    public GraphicsPreset? LockedPreset { get; init; }

    /// <summary>
    /// Gets how many missions, counted along the campaign from its first, this build offers, or
    /// <see langword="null"/> for all of them.
    /// </summary>
    /// <remarks>
    /// A property of the build rather than a command-line option: the browser build offers the first six.
    /// A mission past the limit is not listed, cannot be started from a save or the command line, and
    /// finishing the last one offered completes the campaign.
    /// </remarks>
    public int? CampaignMissionLimit { get; init; }

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

    /// <summary>Gets how many frames a game runs for before the next installed one is switched to.</summary>
    /// <remarks>
    /// Zero for never. Set by <c>-titlecycle</c>, and <see cref="MissionCycleFrames"/> one level up: a
    /// title switch replaces the task loop, the resource registry, the mission table, the content
    /// session and every renderer together, which is the largest teardown a session performs. Until this
    /// it could only be reached by a person choosing another game from the menu, so nothing could
    /// measure it or run it often enough to see what it fails to release.
    /// </remarks>
    public int TitleCycleFrames { get; init; }

    /// <summary>Gets how many frames a menu backdrop is shown for before the next mission is selected.</summary>
    /// <remarks>
    /// <see cref="MissionCycleFrames"/> on the menu's side of Play, set by <c>-backdropcycle</c>. A
    /// backdrop swap tears down and rebuilds every renderer, every mesh and every traced structure a level
    /// owns while the loop keeps presenting, which is a harder case than leaving a mission and had no way
    /// to be run other than clicking down the campaign list.
    /// </remarks>
    public int BackdropCycleFrames { get; init; }

    /// <summary>Gets how many frames each graphics preset is kept before the next is applied.</summary>
    /// <remarks>Set by <c>-presetcycle</c>; walks vanilla, enhanced, high and ultra in turn.</remarks>
    public int PresetCycleFrames { get; init; }

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

    /// <summary>Gets a fixed sun direction (pointing towards the sun) that overrides the level's, or <see langword="null"/> to use the level's own. For matching an external render.</summary>
    public (float X, float Y, float Z)? SunDirection { get; init; }

    /// <summary>
    /// Gets where to write, as JSON, the light the last frame was lit with: the sun or moon, the sky, the
    /// exposure settings and every lamp fixture as the renderer receives it. <see langword="null"/> writes
    /// nothing.
    /// </summary>
    /// <remarks>
    /// A tool for lining an external render up with this one from the same numbers rather than
    /// from a second derivation of them, which would drift the moment either side changed. Written on the
    /// frame a capture exits on, so it describes the picture <c>-screenshot</c> saved beside it.
    /// </remarks>
    public string? LightingDumpPath { get; init; }

    /// <summary>
    /// Gets a fixed horizontal field of view in degrees, or null for the game's own ninety. For lining a
    /// capture up against an external render whose camera lens differs.
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

    /// <summary>Gets how many rendered frames a flight holds its start pose before it moves.</summary>
    /// <remarks>
    /// A flight otherwise moves from the first frame, while the level is still loading and every temporal
    /// history is empty -- so a capture of a walk measured the load, not the walk. Held this long first,
    /// the move starts from a converged frame, the way a player's does.
    /// </remarks>
    public int FlightDelay { get; init; }

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

    /// <summary>Gets how many degrees of yaw are added on every other frame, or zero. Set by <c>-camswing</c>.</summary>
    public float CameraSwing { get; init; }

    /// <summary>
    /// Gets a value indicating whether a scripted free-camera flight is asked for — an end pose the opening
    /// camera is flown to over the frame budget. When set, <c>-screenshot</c> writes one numbered frame per
    /// rendered frame rather than a single still, so the in-motion behaviour of the temporal filters is
    /// visible in the sequence.
    /// </summary>
    public bool HasFlight
        => CameraEnd is not null || CameraYawEnd is not null || CameraPitchEnd is not null || CameraShake > 0f
            || CameraSwing != 0f;

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

    /// <summary>Gets where to pick the game up, when this run follows a switch of renderer.</summary>
    /// <remarks>
    /// Not a command-line option: the composition root sets it for the run it starts after the last one
    /// ended to change renderer. Its mission is <see cref="StartMission"/>; this adds the hour and the pose.
    /// </remarks>
    public RendererSwitch? Resume { get; init; }

    /// <summary>Gets a value indicating whether the command line chose the window's mode or size.</summary>
    /// <remarks>
    /// Set by <c>-window</c>, <c>-borderless</c>, <c>-fullscreen</c> and <c>-size</c>. The run then keeps the
    /// window it was asked for, and <c>r_window_mode</c> and <c>r_resolution</c> wait for the next one.
    /// </remarks>
    public bool WindowFromCommandLine { get; init; }

    /// <summary>Gets the graphics API this run draws over.</summary>
    /// <remarks>
    /// Not a command-line option: the composition root sets it to the backend it built, after any fallback,
    /// so the game can tell whether <c>r_renderer</c> asks for a different one. <see cref="RendererBackend.Auto"/>
    /// where nothing said, which a change of the setting never switches away from.
    /// </remarks>
    public RendererBackend Renderer { get; init; }

    /// <summary>Gets the usage text.</summary>
    /// <returns>A short description of every option.</returns>
    public static string Usage =>
        """
        OpenIGI desktop shell.

        usage:
          OpenIGI.Desktop [options]

        options:
          -title <id>      The game to run: igi1 or igi2. Defaults to OPENIGI_TITLE, and
                           failing that to whichever is installed where the port can see
                           it. I.G.I. 2 is still in development.
          -game <dir>      Installation to read data from. Defaults to the chosen title's
                           own variable -- IGI_GAME_PATH for igi1, IGI2_GAME_PATH for
                           igi2 -- and failing that to an installation found beside the
                           executable or in the working directory, so putting OpenIGI in
                           the game's own folder is enough on its own.
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
          -lockstep        Drive the simulation from a synthetic clock, one tick per loop pass,
                           instead of the wall clock. A run then depends only on its arguments,
                           which is what makes a capture reproducible. Capture only -- it uncaps
                           the simulation rate and is not how the game is meant to be played.
          -content <dir>   Stack a folder of asset overrides on top of the content load order,
                           for every title. Repeatable; the first named wins.
          -contentprofile <file>
                           The per-game content load order the launcher writes. Defaults to
                           OPENIGI_CONTENT_PROFILE. Without either, only the game folder's own
                           overrides folder is used. OPENIGI_OVERRIDES still adds one folder,
                           below any -content and above the profile.
          -streamline <dir>
                           The NVIDIA Streamline runtime (DLSS, ray reconstruction, frame generation,
                           Reflex) to load. Defaults to OPENIGI_STREAMLINE_PATH, which the launcher sets
                           when its runtime pack is installed, then third-party/streamline above the
                           executable. Every DLL in the folder must be NVIDIA-signed or none is loaded.
          -nostreamline    Run without the Streamline runtime even when it is installed.
          -streamline-dev  Load the development runtime from the folder's development subfolder:
                           NVIDIA's overlays and plugin configuration. Never for play.
          -runat <n> <cmd> Run one console statement on frame n, once. Unlike +<command>, which runs
                           before any level exists, this reaches a loaded world -- so a capture can fire
                           impact_test or blood_test and photograph the result.
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
          -camswing <deg>  Add this much yaw on every other frame, so frames N and N+2 show the same view:
                           what differs between them depends on where the camera has looked.
          -camshake <deg>  Lay a handheld-camera shake of this peak amplitude over the pose, drifting
                           the way a hand does rather than jittering randomly. Implies a flight, so it
                           can be used on its own to shake a fixed viewpoint. Try 0.15 for a steady
                           hold, 0.5 for a walk, 1.5 for a run.
          -difficulty <d>  easy, normal or hard - the settings.qsc rows GD_1, GD_2 and GD_3
                           every soldier reads its combat numbers from. Default normal.
          -vanilla         The whole remaster track off: the retail menu system and the original's
                           picture, pacing and mix (implies -vanillamenu, +gfx_preset vanilla and
                           +snd_preset vanilla, including its 32-voice flat-panned audio). The
                           simulation stays at 30 Hz but the display is not held to it; -lockstep
                           is what restores the original's one-render-per-tick schedule.
          -vanillamenu     The byte-for-byte retail menu system instead of the cinematic shell,
                           over the modern renderer.
          -shelltab <s>    Open the cinematic shell on a section instead of its landing page:
                           campaign, settings, profile or extras. The shell's -screen.
          -entrycam <n>    Boot the shell on mission <n>, wait for its backdrop, then press Play by
                           itself, so the menu-to-mission entry can be measured and captured from a
                           known frame rather than from whenever somebody clicked.
          -entrytime <t>   The campaign's Time choice for -entrycam: default, day or night, chosen
                           a second before Play the way the dropdown chooses it.
          -missioncycle N  Abandon the mission to the campaign and play it again every N frames, so the
                           cost of leaving a level -- and whether it releases what it built -- can be
                           measured over many cycles instead of clicked through by hand.
          -titlecycle N    Switch to the next installed game every N frames, the same way choosing one
                           from the menu does. The largest teardown there is: the task loop, the
                           resource registry, the mission table and every renderer, all at once.
          -backdropcycle N Select the next mission in the menu every N frames, so the backdrop swap --
                           a whole level replaced while the loop keeps presenting -- can be run for as
                           long as it takes instead of clicked down the list by hand.
          -presetcycle N   Apply the next graphics preset every N frames, the way choosing one in
                           the settings does.
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
}
