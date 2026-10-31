using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The desktop composition root: it builds the world, the renderers and the developer console, drives
/// the frame loop, and tears everything down. The startup and per-frame body lives in <see cref="Run"/>;
/// mission loading, lighting and the menu shell are in the sibling partials.
/// </summary>
public sealed partial class GameHost : IDisposable
{
    // The camera's clip planes, both measured out of the original's own renderer setup -- see
    // ViewPlanes. The far one is shared with the sky, which sizes its dome to four fifths of it exactly
    // as sub_52CC40 does, and with the first-person layer's shallower projection.
    private const float NearPlane = ViewPlanes.NearMetres;
    private const float FarPlane = ViewPlanes.FarMetres;

    // How long a load will hold for the lighting before giving up and letting the player in anyway. A
    // level that somehow never finishes tracing must not become a game that never starts.
    private const int LightingWaitMilliseconds = 30_000;

    // How long -entrytime lets the backdrop show the chosen hour before -entrycam presses Play: about a
    // second of the menu, as a player choosing it would leave.
    private const int EntryTimeSettleFrames = 60;

    // --- Environment, supplied by the composition root ---
    private readonly IPlatformHost _platform;
    private readonly IWorkScheduler _work;
    private readonly ILoadGate _loadGate;
    private readonly IGameWindow _window;
    private readonly IAudioDeviceFactory _audioFactory;
    // The games this build can run. Held rather than resolved once, because the shell can change which
    // one is running: a switch resolves a different title's four services through the same catalogue the
    // first one came from, so there is no second path that could answer differently.
    private readonly ITitleCatalog _titles;

    // The title running NOW. Every one of these four moves together, and a switch that moved three of
    // them would be one game's world drawn through another's renderers.
    private IGameTitleRuntime _titleRuntime;
    private ITitlePresentationProvider _presentation;

    // How this title turns a loaded level into placements. The host used to call the first game's walk
    // directly, which made it a host only that game could load a mission into.
    private ITitleWorldBuilder _worldBuilder;

    // A factory rather than the catalogue itself, because the scan happens after the log is wrapped by
    // the developer console and the catalogue reports what it found as it goes.
    private Func<IOpenIgiLogger, IMissionCatalogue<MissionDescriptor>> _missionCatalogue;
    /// <summary>Gets the window width, or the authored 320 when a minimize/dock race reports 0x0.</summary>
    private int SafeWindowWidth => _window.Width > 0 ? _window.Width : 320;

    /// <summary>Gets the window height, or the authored 240 when a minimize/dock race reports 0x0.</summary>
    private int SafeWindowHeight => _window.Height > 0 ? _window.Height : 240;

    private readonly IGraphicsDevice _device;
    private readonly VirtualFileSystem _fileSystem;
    private readonly HostOptions _options;

    // Where the RUNNING title is installed. The file system's LOCAL device is re-pointed here on a
    // switch; VirtualFileSystem registers devices by name, so re-registering replaces rather than adds.
    private string _gamePath;
    private readonly IOpenIgiLogger _logger;

    // --- Services built during startup ---
    private ResourceRegistry _resources = null!;
    private ResourceOverrides _overrides = null!;

    // What the last scan of that folder found, reported once the console has started -- until then the
    // log verbosity the player configured has not been applied and an information line goes nowhere
    // anybody can see.
    private ResourceOverrides.ScanResult _overrideScan;

    // Normally the session file system above. A promoted preview brings a registry backed by its own
    // private file system, while long-lived services such as audio stay on the session one. Mission
    // changes therefore bind both; keeping this reference beside the registry makes that split explicit.
    private VirtualFileSystem _resourceFileSystem = null!;

    // The archives that belong to the session rather than to a level, kept as bytes so a preview built on
    // a worker can register them into its own registry without reading through this one.
    private readonly Dictionary<string, byte[]> _sessionArchives = [];

    // What a menu backdrop needs to become the played mission: the session's clock, flow host, render
    // scheduler and archives. See MissionPreviewContext.
    private MissionPreviewContext _previewContext = null!;
    private GameConfig _configuration = null!;
    private HostRenderScheduler _scheduler = null!;

    // Whether the presented motion keeps pace with real time; listening only at Pacing=debug.
    private readonly Diagnostics.PresentationPacingProbe _pacing;

    // The one wall clock every loop reads. A mission promoted out of the menu's backdrop brings its own
    // GameLoop with it, and both loops have to measure milliseconds from the same origin — see
    // GameClock.AdoptScheduleFrom.
    private ITimeSource _timeSource = null!;
    private GameLoop _loop = null!;
    private DistanceFieldCache _sdfCache = null!;
    // The title's own, not this game's. DefineMission takes eleven arguments in one and fourteen in the
    // other, and only one of them names the mission that follows -- so the reader and the campaign order
    // are both the title's to supply.
    private IMissionCatalogue<MissionDescriptor> _missions = null!;
    private TitleSession _title = null!;
    private MaterialLibrary _materials = null!;
    private MaterialDetailPack _detailMaps = null!;

    // Whether the renderer is currently holding the pack, against r_hd's request. Tracked rather than
    // read, because acting on it means reloading every texture in the level -- so it has to happen on the
    // frame the variable CHANGES and not on every frame it is false.
    private bool _overridesApplied = true;

    // The run, one frame per step; see Frames. Null before Start and after Stop.
    private IEnumerator<bool>? _frames;
    private bool _stepping;
    private bool _stopRequested;
    private int _exitCode;

    // --- The player's own life, damage and death ---


    /// <summary>The wall clock the third-person move is advanced on; it is a camera move, not a tick.</summary>

    /// <summary>The pull-in ray, kept so a frame does not allocate one; rebuilt when the level's does.</summary>



    // What the level says about its own light, for a title that authors that rather than a sky. Null for
    // Project I.G.I., which keeps the sweep. Set by ApplyLevelConstants, so the menu path gets it too.
    private DeferredLighting? _deferredLighting;


    // What a person has said outright about which models are lamps, which beats anything the detector
    // works out. One file for the whole game; see Assets/lamps.json.
    private LampManifest _lampManifest = LampManifest.Empty;

    // The played mission's registries and runtimes, each null until the system that makes it has run.
    // The first game's mission, its world, and the host's side of it.
    private readonly MissionHostServices _missionHost;
    private readonly IHostedMission _hosted;
    private readonly MissionState _mission;

    // The player the host steers and looks through, when the mission has one.
    private IHostedPlayer? HostedPlayer => _hosted.Player;

    // The console's handle on the content layers.
    private readonly ContentLayerCommands _contentLayers;

    // The console's inspector, over what IInspectedWorld shows it of the host.
    private readonly WorldInspector _inspector;

    // --- The loaded world and its renderers ---
    private WorldScene? _scene;
    private IWorldPresenter? _world;

    private ITerrainPresenter? _terrain;
    private IForestPresenter? _forest;
    private IWaterPresenter? _water;
    // Behind the seam, not a named renderer: which type this is differs by title, and the host asking
    // for one game's by name is what left the other with no sky at all.
    private ISkyPresenter? _sky;

    // The level's own GlobalLight keyframe: the fog the ground and the models are drawn into, and the
    // flat light a model with no lightmap wears. Read once per level, used every frame.
    private GlobalLightSettings _globalLight = GlobalLightSettings.None;
    private ShadowCasterPass? _shadowPass;

    // Makes the backdrop playable a couple of milliseconds at a time while the menu is idle.
    private MissionArming? _arming;

    // The shot that carries the player out of the menu and into their own eyes.
    private readonly MissionEntryCamera _entry = new();

    /// <summary>How many ticks a step may run in a row before it returns to its host without a picture.</summary>
    private const int MaxTicksPerStep = 8;

    /// <summary>Milliseconds since the process was started, for the start-up timings.</summary>
    /// <remarks>
    /// From the OS's own record of when the process began rather than a stopwatch this class starts, so
    /// the runtime's own start-up -- assembly loading and the first JIT, which is a good share of the
    /// wait -- is inside the number rather than before it. A browser keeps no such record, so there it
    /// counts from the host's first use: the runtime's own start-up is the page's to report.
    /// </remarks>
    private static double SinceProcessStart() =>
        OperatingSystem.IsBrowser()
            ? HostUptime.Elapsed.TotalMilliseconds
            : (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    private static readonly System.Diagnostics.Stopwatch HostUptime = System.Diagnostics.Stopwatch.StartNew();

    // The level's light by the hour: its authored sun, the cycle's, the frame's, the sky fill and the night sky.
    private readonly TimeOfDayLighting _timeOfDay;

    // The level's lamps as a player meets them: the wall switch, the photocells, their sound.
    private readonly LevelLamps _lamps;

    // Chosen on the campaign screen beside the difficulty and held from there on, so a run started at
    // night stays at night through every mission after it. Session state, never archived -- see
    // MissionConditions.
    private MissionConditions _conditions = MissionConditions.Default;
    private readonly SunGizmo _sunGizmo = new();
    private int _dayNightMilliseconds;
    private FreeCamera? _camera;

    // --- The player, the held weapon, the HUD and the soldiers ---
    private IHeldWeaponPresenter? _heldWeapon;
    private IGunPickupPresenter? _gunPickupRenderer;

    // The over-the-shoulder camera's and the flashlight's wall cast, cached against the collision it is built from.
    private DebugOverlayRenderer? _debugOverlay;
    private StatusMessageRenderer? _messages;
    private readonly HashSet<int> _deferredCommandsFired = [];

    // -missioncycle's bookkeeping: which leg of the abandon/replay cadence has been acted on, and the
    // mission to go back into once the level has been let go of.
    private long _missionCycleLeg;
    private long _titleCycleLeg;
    private int _missionCycleMission;

    private IBulletMarkPresenter? _marks;

    // What the open ground is made of, for the trace above. A terrain leaf carries no material row of
    // its own, so a hit on it reports Air -- which marks nothing at all. This is the row the footsteps
    // and the player's own material lookup already use.

    private ICameraStatusLightPresenter? _cameraStatusLights;
    private IGrenadePresenter? _grenadeRenderer;
    private IMissilePresenter? _missileRenderer;

    // How the level's models give up detail with distance, or none.
    private ModelLodPolicy? _modelLod;

    // The camera the last frame was drawn from, which the ear follows; null before a level has drawn one.
    private Camera3D? _earCamera;

    private LoadedMission? _loadedMissionForWeapon;
    private IViewModelPresenter? _arms;
    private ISoldierBodies? _soldiers;

    // The vehicles' engine sets, gun reports and turret traverse (VehicleSoundPlayer).
    private VehicleSoundPlayer? _vehicleSounds;
    private bool _playerMode;

    // --- Built once and re-registered into every title's namespace ---
    // Locals until the shell could change games. All four outlive a title: the display and the menu host
    // are the seams the menus act through, and the mission state is a native the level scripts call.
    // A switch builds a new GameLoop, and each of them has to be registered into it -- which is why they
    // are here rather than in Run's own scope.
    private RetailMenuDisplay _menuDisplay = null!;

    // The missions the retail menus list: the title's table once its scan has succeeded.
    private IMissionCatalogue<MissionDescriptor>? _menuMissions;

    // The campaigns the running title's mods add beside its own, or null for none.
    private SideCampaignMissionCatalogue? _sideCampaigns;
    private HostFlowHost _flowHost = null!;
    private GameMissionState _missionState = null!;

    // The game the shell has asked for, waiting for a frame boundary. Deferred for the same reason a
    // mission load is: the switch tears down the loop, the registry and every presenter, and doing that
    // from inside a frame would pull them out from under the pass that is drawing.
    private GameTitle? _pendingTitle;

    // --- The menus and the cinematic shell ---
    // The retail menu system's shell, while the -vanilla menus are up.
    private IRetailMenus? _retailMenus;
    private MenuShowcaseCamera? _showcaseCamera;
    private MenuCameraLibrary _menuCameras = MenuCameraLibrary.Empty;

    /// <summary>Wires the game to the window, device, file system and options it runs against.</summary>
    /// <param name="platform">The platform host, for reporting its name.</param>
    /// <param name="window">The game window whose input and frames drive the loop.</param>
    /// <param name="device">The graphics device every renderer draws through.</param>
    /// <param name="audioFactory">Opens the audio backend; re-called whenever the device settings change.</param>
    /// <param name="work">Where work off the frame loop runs: beside it on the desktop, inline in a browser.</param>
    /// <param name="loadGate">Whether a mission's files can be read yet; always, on the desktop.</param>
    /// <param name="titles">The games this build can run, and where each of them is installed.</param>
    /// <param name="startOn">The one to open on.</param>
    /// <param name="fileSystem">The virtual file system the original installation is read through.</param>
    /// <param name="options">The parsed command line.</param>
    /// <param name="gamePath">The starting title's installation directory.</param>
    /// <param name="logger">The log; wrapped by the developer console once it starts.</param>
    /// <exception cref="ArgumentException">The catalogue has no implementation for <paramref name="startOn"/>.</exception>
    /// <remarks>
    /// The catalogue rather than one title's services, because the shell can change games without the
    /// process restarting. Starting in a title and switching to it then run the same code, which is the
    /// only arrangement in which the second one cannot rot: there is no boot path for a switch to
    /// disagree with.
    /// </remarks>
    public GameHost(
        IPlatformHost platform,
        IGameWindow window,
        IGraphicsDevice device,
        IAudioDeviceFactory audioFactory,
        IWorkScheduler work,
        ILoadGate loadGate,
        ITitleCatalog titles,
        GameTitle startOn,
        VirtualFileSystem fileSystem,
        HostOptions options,
        string gamePath,
        IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(startOn);

        if (!titles.TryResolve(startOn, out TitleBinding? binding))
        {
            throw new ArgumentException($"This build cannot run '{startOn.Id}'.", nameof(startOn));
        }

        _platform = platform;
        _window = window;
        _device = device;
        _lampMaps = new LampShadowMaps(device);
        _flashlight = new PlayerFlashlight(device);
        _inspector = new WorldInspector(new InspectedWorld(this));
        _missionHost = new MissionHostServices(this);
        _hosted = titles.CreateHostedMission(_missionHost);
        _mission = _hosted.World;
        _contentLayers = new ContentLayerCommands(
            () => _overrides,
            () => _resources,
            () =>
            {
                ApplyOverrideChange(out string report);
                return report;
            },
            logger);
        _audioFactory = audioFactory;
        _work = work;
        _loadGate = loadGate;
        _titles = titles;
        _titleRuntime = binding.Runtime;
        _presentation = binding.Presentation;
        _worldBuilder = binding.WorldBuilder;
        _gameplay = binding.Gameplay;
        _missionCatalogue = binding.Missions;
        _fileSystem = fileSystem;
        _options = options;
        _gamePath = gamePath;
        _logger = logger;
        _pacing = new Diagnostics.PresentationPacingProbe(logger);
        _memory = new Services.MemoryDiagnostics(device, logger);
        _menuBackdrop = new MenuBackdrop(device, logger);
        _menuFlow = new MenuFlow(device);
        _session = new MissionSession();
        _audioOut = new AudioOutput(audioFactory, fileSystem, logger, MenuSoundArchive);
        _cutsceneSounds = new CutsceneSounds(_audioOut);
        _effects = new WorldEffects(device, logger, _mission.SoundRandom, new WorldEffectsHost(this));
        _timeOfDay = new TimeOfDayLighting(device, options, new TimeOfDayHost(this));
        _lamps = new LevelLamps(device, new LevelLampsHost(this));
        _switch = new RendererSwitchService(device, logger);
        _debugOverlays = new DebugOverlays(device, window, options, logger, new DebugOverlaysHost(this));
        _outcome = new MissionOutcome(_session, logger, new MissionOutcomeHost(this));
    }

    /// <summary>Builds the world, runs the frame loop until the game is asked to quit, then shuts down.</summary>
    /// <returns>The process exit code.</returns>
    /// <remarks>
    /// <see cref="Start"/>, then <see cref="Step"/> until it says the game has finished, then
    /// <see cref="Stop"/>: the same three calls a host that cannot block drives one at a time, from its
    /// own frame callback.
    /// </remarks>
    public int Run()
    {
        Start();

        while (Step())
        {
        }

        Stop();
        return _exitCode;
    }

    /// <summary>Builds the world and everything the first frame needs, and returns before drawing it.</summary>
    /// <exception cref="InvalidOperationException">The host has already been started.</exception>
    public void Start()
    {
        if (_frames is not null)
        {
            throw new InvalidOperationException("The host has already been started.");
        }

        _frames = Frames().GetEnumerator();
        _stepping = _frames.MoveNext();
    }

    /// <summary>Runs one frame: events, simulation, the picture.</summary>
    /// <returns>
    /// <see langword="true"/> while the game goes on; <see langword="false"/> once it has been asked to
    /// quit, by which point it has also shut itself down.
    /// </returns>
    /// <remarks>
    /// Never blocks waiting for the next frame's time. The desktop's own pacing still sleeps inside a
    /// frame where it decides the display has not caught up; a host whose frames are paced for it steps
    /// once per callback.
    /// </remarks>
    public bool Step()
    {
        if (_frames is null || !_stepping)
        {
            return false;
        }

        _stepping = _frames.MoveNext();
        return _stepping;
    }

    /// <summary>Ends the game if it is still running -- saving what it saves on the way out -- and lets it go.</summary>
    public void Stop()
    {
        if (_frames is null)
        {
            return;
        }

        // Asked to stop part way: the loop sees the request at its next test and runs the same shutdown
        // it runs when the player quits, so settings are saved however the host came to an end.
        _stopRequested = true;

        while (_stepping)
        {
            _stepping = _frames.MoveNext();
        }

        _frames.Dispose();
        _frames = null;
    }

    // How long the field takes to replace the ambient it stands in for. Long enough that the eye reads
    // it as light settling rather than as a cut, short enough that nobody waits for it -- and well
    // under the several seconds the bake itself takes, so the two do not read as one long event.
    private const float ProbeFadeMilliseconds = 600f;

    private const int ShowcaseMissionId = 1;

    // How long a mission has to stay selected before its level is built. Every selection re-arms the
    // timer, so running down the list faster than this builds nothing at all. It can be short now that
    // building costs the render thread nothing -- it is a debounce against pointless work, not a shield
    // for the frame rate.
    private const int PreviewSettleMs = 260;

    /// <summary>The whole of the game's run, one frame per step.</summary>
    /// <returns>An element at the top of every frame, before it runs.</returns>
    /// <remarks>
    /// An iterator, so that a step is one pass of the loop. The loop's own state -- its pacers, its timestamps, the
    /// shell and the preview loader, the screen GI's history -- is one <see cref="FrameLoopState"/>, made as the loop
    /// starts and set up in the order it always was.
    /// </remarks>
    private IEnumerable<bool> Frames()
    {
        // What the loop keeps from one frame to the next.
        FrameLoopState state = new();

        // The UI is authored against the original's 640x480 back buffer and is centred, NEVER SCALED, so
        // the projection is the back buffer's own size and MenuLayout.ToScreen does the centring. Passing
        // the authored size here instead scales 640x480 up to fill the window AND then scales the centring
        // offset with it, which puts the menu in the bottom-right corner of anything but a 640x480 window.
        Resize(_window.Width, _window.Height);
        _window.Resized += (_, e) => Resize(e.Width, e.Height);

        // Some window systems fire a degenerate 0x0 resize during creation (minimize/dock races).
        // Setting a zero-sized projection is an immediate renderer fault, so defer until a real size
        // arrives; the next genuine resize re-applies both.
        void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            _logger.Information("Desktop", $"Framebuffer resize event: {width}x{height}.");

            // A minimised window, and the occasional spurious event as the window is created, arrive as a
            // zero dimension. There is nothing to project onto and the back buffer keeps its last real size,
            // so ignore it rather than tear the viewport down to zero (which the projection rejects outright).
            if (width <= 0 || height <= 0)
            {
                return;
            }

            // The back buffer follows the window. Without this only the viewport did: a browser canvas
            // resized by the page -- or by F11 -- kept presenting a surface at its old size, and WebGPU
            // refuses a viewport larger than the target it is drawn into, so every frame after the resize
            // was thrown away and the picture froze.
            DisplayMode mode = _device.CurrentMode;
            if (mode.Width != width || mode.Height != height)
            {
                _device.SetMode(mode with { Width = width, Height = height });
            }

            _device.SetViewport(new Viewport(0, 0, width, height));
            _device.Set2DProjection(width, height);
        }

            // Entry labels are keys into the language archive, so it has to be registered before the menu
            // can resolve any of them. Through the same method a title switch runs, so a game taken up mid
            // session registers exactly what a game started on does. The overrides folder is re-pointed
            // there too, because a different installation has a different one.
            LoadTitleResources();

        // Settings load from the retail config.qvm, and from our own file in preference to it when one
        // exists. Nothing here ever writes into the original installation.
        _configuration = HostConfiguration.Load(_fileSystem, _logger);
        RestoreTitleInputBindings();
        _menuDisplay = new RetailMenuDisplay(
            () => _device.Capabilities.Renderer,
            () => _window.GetDisplayModes().Select(mode => (mode.Width, mode.Height)),
            (w, h) =>
            {
                if (_window.Mode == WindowMode.BorderlessFullscreen)
                {
                    _logger.Information(
                        "Config",
                        $"Resolution {w}x{h} was chosen but the window is borderless fullscreen, whose size "
                            + "is the desktop's. Run with -window or -fullscreen for it to take effect.");
                    return;
                }

                _window.Resize(w, h);
                _logger.Information("Config", $"Resolution set to {w}x{h}.");
            });

        MenuStringTable strings = _menuStrings!;

        // Held so anything outside this method can name a mission the way the player saw it named. A
        // descriptor's own Name is the table KEY -- it reads "Mission 1 Name" -- so a loading panel that
        // showed it would be showing the campaign screen a different title than the one it just listed.
        _menuStrings = strings;
        state.Batch = new(_device);

        // Held so a loading frame can be drawn and swapped from outside this loop, before a mission
        // load blocks it. See GameHost.MissionWarmUp.cs.
        _batch = state.Batch;
        _retailMenus = null;
        state.Running = true;

        // The developer console: the CVar registry, settings.cfg and the on-screen view. Brought up before
        // anything reads a setting, so the saved file and the command line's +statements are already in
        // effect by the time the renderer and the game are built. The process's log is shown in its
        // scrollback, startup included, so opening it shows the log rather than an empty panel.
        using HostConsole console = new(_device, _logger);
        state.Console = console;
        SetUpConsole(state);

        // Audio comes up here, after settings.cfg and the +statements, for the same reason the renderer's
        // settings do: the voice count and the output rate are read once, when the device opens.
        //
        // On a worker, because opening OpenAL and decoding the menu track's ADPCM is a tenth of a second
        // that nothing on this thread depends on, and start-up is the one time the player is waiting for
        // this thread specifically. Everything that touches the audio device does so from the frame loop,
        // which is collected below, before the first line that reads it.
        Task audioReady = _work.Run(OpenAudioDevice);

        BindDeviceVariables(state);

        CreateClocksAndLighting(state);

        // The audio device, collected here: this is the first line that reads it, and everything between
        // it and the Task.Run above -- the scheduler, the console, the cvar wiring -- is work the open
        // ran alongside rather than after.
        audioReady.GetAwaiter().GetResult();

        // The task loop, the mission table and the title's content, through the same method a switch
        // runs. Everything above this line belongs to the process; everything it builds belongs to the
        // game, and changing games rebuilds exactly this much.
        LoadTitleSession();
        GameMusicNatives musicNatives = new(_audioOut.Music!);

        ResetPlayerAndCaptures(state);

        LoadSharedRenderData(state);

        ForgetLevel(state);

        WireMainMenu(state, musicNatives);

        ResetFrontEndState(state);

        BuildFrontEnd(state);

        ShowWindow(state);

        RunOneShotTools(state, strings);

        PrimeLoop(state);

        while (state.Running && !_stopRequested && _loop.IsRunning && !_window.IsClosing)
        {
            // The top of a frame is where a step returns to its host: everything since the last step is
            // one frame, and nothing of the next has started. A host that asked to stop while it was away
            // is answered here, before the frame it would otherwise draw.
            //
            // A frame ends with a pass that drew, or one with nothing due -- not with a tick, which draws
            // nothing. A browser steps once per display refresh, and handing it back a refresh with no
            // picture in it made a clock that had fallen behind catch up at one tick a refresh; a
            // headless browser then throttles a page that has stopped drawing to once a second, and it
            // never caught up again. Bounded, so a long catch-up still returns now and then.
            if (state.LastOutcome != FrameOutcome.Tick || ++state.TicksSinceStep >= MaxTicksPerStep)
            {
                state.TicksSinceStep = 0;
                yield return true;
            }

            // Until this pass runs the loop: one that ends before it has drawn nothing and ticked nothing.
            state.LastOutcome = FrameOutcome.Idle;

            if (_stopRequested)
            {
                break;
            }

            // The whole point of promoting the backdrop instead of loading it is that no frame is ever
            // blocked, and that is a claim about milliseconds rather than a matter of opinion. Every pass
            // is timed, and the worst one seen since Play was pressed is reported when the entry finishes.
            //
            // Measured from the top of the NEXT pass rather than at the bottom of this one, because the
            // loop body has `continue`s in it -- a pass that decides it has nothing to render leaves
            // early -- and a measurement at the bottom silently skips every pass that took one. The
            // promotion happens on a pass that does exactly that, so the first version of this timed
            // everything except the one frame it existed to measure.
            NoteFramePass(state.PassStartedAt);
            FlushWatchedFramePasses();

            // A load whose departure has played out runs here, between frames, where the picture it
            // needs has already been held and nothing is half-drawn.
            RunPendingLoadIfDue();
            RunCommandLineMissionIfDue();

            // And a change of game, at the same boundary and for the same reason -- this one disposes
            // the task loop, the resource registry and every presenter, so it must not happen while a
            // pass is drawing them. The shell is rebuilt around the new title rather than told about it:
            // its game list, its campaign and which entry is current are all the title's.
            if (SwitchTitleIfDue())
            {
                ReopenFrontEndForTitle(state);
            }

            // A change of language, at the same boundary: it swaps the archives the menus are drawing from.
            ApplyLanguageIfDue(state);

            // The frame's input and its world, in the order each one has to see the keys and the clock.
            BeginFramePass(state);
            UpdateShell(state);
            UpdateEntryShot(state);
            RunSessionCycles(state);
            HandleEscape(state);
            RunDeferredCommands(state);
            UpdatePlayerDevices(state);
            UpdateMenuBackdrop(state);
            HandleDeveloperKeys(state);
            AdvanceWorldClocks(state);
            UpdateFlyingView(state);

            // The original's schedule admits ONE render per tick, which caps the whole presentation at
            // 30 Hz however fast the machine is. Smooth mode lifts exactly that latch and nothing else:
            // tick deadlines, the catch-up budget and everything the simulation observes are untouched.
            // Lockstep keeps the original pairing -- its synthetic clock advances per frame and the
            // reference captures depend on the one-to-one schedule.
            // The cinematic shell always renders uncapped so the menu is smooth whatever the render preset's
            // r_smooth is; the orbit and the tweens run on real time and want every frame the display offers.
            // In game the original's one-render-per-tick latch is lifted only by smooth mode.
            // A -capturerate above the tick rate lifts it under lockstep too: its clock advances a fraction of a tick per
            // frame, so with the latch held no frame between two ticks could render and the clock -- counted in rendered
            // frames -- would never reach the next tick.
            _loop.Clock.UncappedRenders = _options.IsBenchmark
                || _menuFlow.MenuMode
                || (!_options.Lockstep && FrameCVars.SmoothPresentation.Value)
                || (_options.Lockstep && _options.CaptureRate > _loop.Clock.TickRate);

            // The level's own half of the pause, and the reason the frozen picture behind the menu really is
            // frozen: with this set the level task forwards nothing to its subtree, so the scripts, the
            // doors and the level clock all stand still. The scheduler is left alone -- ticks keep arriving
            // and finding nothing to do -- which is what stops a menu held open for a minute from being
            // followed by a minute's worth of catch-up.
            // And the level's half of the warm-up: a withheld level must not run. Its intro cut-scene,
            // its patrols and its scripted clocks all start from the frame the player first SEES, not
            // from the frame the load finished -- a cut-scene that played out behind the loading screen
            // is one the player never gets to watch.
            if (_session.LevelRoot is { } pausableLevel)
            {
                pausableLevel.IsPaused = _menuFlow.PauseMenu || PresentationHoldsLevel;
            }

            BeginStage();
            FrameOutcome outcome = _loop.RunFrame();
            state.LastOutcome = outcome;
            EndStageIfSlow("the loop's own frame (present, ticks, task tree)");

            // The number that says how long the player waited for a picture, said once. Everything above
            // it -- the runtime coming up, the window, the device, the swapchain, the first pipelines --
            // is time nobody can do anything during, and it is the only start-up figure that is about
            // what a person experiences rather than about where the CPU went.
            if (!state.FirstFrameReported && outcome != FrameOutcome.Tick)
            {
                state.FirstFrameReported = true;
                _logger.Information(
                    "Desktop",
                    FormattableString.Invariant($"The first frame is on screen {SinceProcessStart():0} ms after the process started."));
            }

            // Retire finished voices, refill streaming buffers and advance any crossfade. This sits
            // ABOVE the pause check on purpose: a paused game still has to feed the music, and a stream
            // that goes unfed for a few frames does not pause, it runs dry and stops.
            AdvanceAudio();

            // The retail in-game menu pauses the game task. Once LevelFlow failure has opened that menu --
            // or the player has opened the pause menu -- do not advance the desktop-owned player/AI
            // simulation until they choose an action.
            // Likewise while the level is withheld: the desktop-owned simulation -- the player, the AI,
            // the cut-scene runtime, the weather -- stands still until the level is revealed.
            if (outcome == FrameOutcome.Tick && (_session.FailedMenu || _menuFlow.PauseMenu || PresentationHoldsLevel))
            {
                // A held co-op level still hears and tells the others: that everyone is there is what lets it go.
                if (PresentationHoldsLevel && TickObserver is Multiplayer.ISessionHold hold)
                {
                    hold.Pump(_hosted);
                }

                continue;
            }

            TickMission(state, outcome);

            if (outcome != FrameOutcome.Render)
            {
                continue;
            }

            state.LastPassRendered = true;

            // Everything the frame simulated has run; recording its commands starts now.
            _device.MarkLatency(LatencyMarker.SimulationEnd);
            _device.MarkLatency(LatencyMarker.RenderSubmitStart);

            DrawPresentation(state);

            FinishFrame(state);

            // The frame's commands are all recorded. Under NVIDIA's latency layer it goes to the GPU and the
            // screen now, rather than at the head of the next pass as the original's cadence has it: a frame
            // submitted only once the next one had begun would put every latency marker on the wrong frame.
            // The scheduled present then finds no frame open and does nothing.
            _device.MarkLatency(LatencyMarker.RenderSubmitEnd);
            if (_device.PresentsOnRecord)
            {
                _device.SwapBuffers();
            }
        }

        WriteBenchmarkSnapshot(state);

        LogStageTimingsAtExit();

        // A bake still tracing at this point is tracing for a level nobody will see again. The worker is a
        // thread-pool thread and cannot hold the process open, so this is not about shutting down cleanly --
        // it is about not spending every core on it while the rest of the shutdown runs.
        _baked.Cancel();

        if (!_options.IsBenchmark)
        {
            HostConfiguration.Save(_configuration, _logger);
            console.SaveSettings();
        }
        state.MissionWarmer?.Dispose();
        state.PreviewLoader?.Dispose();
        _menuFlow.ReleaseHeldFrame();

        // The shell's faces are the device's shared set; the device's teardown takes their textures.
        _logger.Information(
            "Desktop",
            $"Shutting down after {_loop.Clock.TickCount} ticks and {_loop.Clock.FrameCount} frames.");

        _exitCode = 0;
    }

    /// <summary>
    /// Says out loud which of the settings in force this backend does not act on yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for settings moved off their default: a variable sitting where it shipped is not asking for
    /// anything, and warning about it every launch would bury the ones that are. A variable that has been
    /// turned up and then read past is the case worth a line, because nothing else in the run reports it —
    /// the value persists, the options screen shows it, and the picture never changes, so it reads as a
    /// broken effect rather than an absent one. It also silently divides a comparison: two renders of
    /// "the same settings" are not the same picture if one backend honours a setting and the other drops it.
    /// </para>
    /// </remarks>
    // Console key bindings fire with the console shut. The bind table, the `bind` command and
    // their tests have existed since the console was written, but nothing ever asked the table
    // whether a key had been pressed -- so a binding could be declared and saved and would
    // never once fire. Skipped during a capture run, whose input must stay exactly scripted.
    //
    // In menu mode the shell runs first (below) so a listening rebind row sees the press
    // queue before anything consumes it (issue #230). The shell then owns the frame — it sets
    // Captured right below — so game binds must not execute against the sim beneath a menu:
    // what the shell leaves queued is discarded, never fired. Menu navigation is unaffected
    // either way because it reads WasPressed shadow edges, not the queue.
    private void DrainPressQueue(FrameLoopState state, bool executeBinds)
    {
        if (!state.Console.IsOpen && !state.Console.IsolatedFromSavedSettings && !_window.Input.Captured)
        {
            while (_window.Input.Keyboard.TryDequeuePress(out Input.KeyCode key))
            {
                if (executeBinds)
                {
                    state.Console.System.ExecuteBind(BindKeyNames.NameOf(key));
                }
            }
        }
    }

    private void WarnAboutSettingsTheBackendIgnores()
    {
        IReadOnlyList<string> ignored = _device.Capabilities.UnimplementedSettings;
        if (ignored.Count == 0)
        {
            return;
        }

        foreach (string name in ignored)
        {
            if (CVarRegistry.Default.TryGet(name, out ICVar? variable) && !variable.IsDefault)
            {
                _logger.Warning(
                    "Render",
                    $"{name} is {variable.ValueText}, but {_device.Capabilities.Renderer} does not implement it yet — "
                        + $"the value is kept and does nothing until that pass lands (default {variable.DefaultText}).");
            }
        }
    }

    /// <summary>The ray the third-person camera keeps itself out of walls with.</summary>
    /// <returns>The test, or <see langword="null"/> when the level has nothing to be blocked by yet.</returns>
    /// <remarks>
    /// <para>
    /// Placed objects <b>and</b> the terrain, unlike the ladder camera's, which tests objects alone. That
    /// is defensible for a camera that only ever exists on a ladder; a camera that follows the player
    /// across a level spends most of its time outdoors, where the thing it is about to be pushed into is
    /// a hillside.
    /// </para>
    /// <para>
    /// The level's collision makes it once (<see cref="LevelCollision.Walls"/>), as it is asked every frame.
    /// </para>
    /// </remarks>
    private WorldWalls.Blocked? ThirdPersonWalls() => _mission.Collision.Walls;

    /// <summary>Determines whether an in-game menu holds the pointer, standing the cameras down.</summary>
    /// <remarks>
    /// Both of these screens release the cursor every frame so it can be used, and every camera branch
    /// below them takes it back: the death orbit, the ladder camera, the look. Whichever runs last in
    /// the frame wins, so a menu that does not stop the chain here ends up fighting one of them and the
    /// pointer flashes between locked and free without ever moving.
    /// </remarks>
    internal static bool MenuOwnsPointer(bool pauseMenu, bool missionFailedMenu) =>
        pauseMenu || missionFailedMenu;

    /// <summary>
    /// Releases the GPU-backed resources the game owns. The window, device and developer console are owned
    /// by the composition root and are disposed there; everything a level built is released here.
    /// </summary>
    public void Dispose()
    {
        // First, so nothing below can be reached through a setting changed after this game has gone.
        ReleaseSettingHandlers();

        _sky?.Dispose();
        _rain?.Dispose();
        _storm?.Dispose();
        _terrain?.Dispose();
        _forest?.Dispose();
        _forest = null;
        _water?.Dispose();
        _water = null;
        _retailWeather?.Dispose();
        _retailWeather = null;
        _world?.Dispose();
        _soldiers?.Dispose();
        _menuBackdrop.Dispose();
        _menuFlow.Dispose();
        _heldWeapon?.Dispose();
        _gunPickupRenderer?.Dispose();
        _hud?.Dispose();
        _remasteredHud?.Dispose();
        _debugOverlay?.Dispose();
        _mapComputer?.Dispose();
        _mapComputerDevice?.Dispose();
        _tacticalComputer?.Dispose();
        _messages?.Dispose();
        _marks?.Dispose();
        _effects.Dispose();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _missileRenderer?.Dispose();
        _retailMenus?.Dispose();
        _arms?.Dispose();
        _titleArms?.Dispose();
        _titleHud?.Dispose();
        _gi?.Dispose();
        _detailMaps?.Dispose();

        // Before the device: the bed's taps are streams the device owns, and stopping them while it is
        // still alive is what gives the voices back rather than leaving them to the device's teardown.
        _rainAmbience?.Dispose();
        _audioOut.Dispose();
    }
}
