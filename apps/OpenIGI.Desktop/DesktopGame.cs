using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Desktop.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Profiles;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering;
using OpenIGI.Rendering.OpenGL;

namespace OpenIGI.Desktop;

/// <summary>
/// The desktop composition root: it builds the world, the renderers and the developer console, drives
/// the frame loop, and tears everything down. The startup and per-frame body lives in <see cref="Run"/>;
/// mission loading, lighting and the menu shell are in the sibling partials.
/// </summary>
internal sealed partial class DesktopGame : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions BenchmarkJsonOptions = new()
    {
        WriteIndented = true,
    };

    // The camera's far plane -- the original's HumanView far distance. Shared with the sky, which sizes
    // its dome from it, and with the terrain traversal, which culls against the same frustum.
    private const float FarPlane = 4000f;

    // --- Environment, supplied by the composition root ---
    private readonly DesktopPlatformHost _platform;
    private readonly SilkGameWindow _window;
    private readonly GLGraphicsDevice _device;
    private readonly VirtualFileSystem _fileSystem;
    private readonly DesktopOptions _options;
    private readonly string _gamePath;
    private IIgiLogger _logger;

    // --- Services built during startup ---
    private ResourceRegistry _resources = null!;

    // Normally the session file system above. A promoted preview brings a registry backed by its own
    // private file system, while long-lived services such as audio stay on the session one. Mission
    // changes therefore bind both; keeping this reference beside the registry makes that split explicit.
    private VirtualFileSystem _resourceFileSystem = null!;

    // Silent until the console has read settings.cfg, so nothing can open a device on stale defaults.
    private IAudioDevice _audio = new NullAudioDevice();

    // The archives that belong to the session rather than to a level, kept as bytes so a preview built on
    // a worker can register them into its own registry without reading through this one.
    private readonly Dictionary<string, byte[]> _sessionArchives = [];

    // What a menu backdrop needs to become the played mission: the session's clock, flow host, render
    // scheduler and archives. See MissionPreviewContext.
    private MissionPreviewContext _previewContext = null!;
    private GameConfig _configuration = null!;
    private DesktopRenderScheduler _scheduler = null!;

    // The one wall clock every loop reads. A mission promoted out of the menu's backdrop brings its own
    // GameLoop with it, and both loops have to measure milliseconds from the same origin — see
    // GameClock.AdoptScheduleFrom.
    private ITimeSource _timeSource = null!;
    private GameLoop _loop = null!;
    private MissionRegistry _missions = null!;
    private MagicObjectRegistry _magic = null!;
    private VehiclePhysicsRegistry _vehiclePhysics = null!;
    private HumanAiConfigRegistry _aiConfig = null!;
    private AnimTriggerRegistry? _animationTriggers;
    private MaterialLibrary _materials = null!;
    private MaterialDetailPack _detailMaps = null!;

    // --- The player's own life, damage and death ---
    private int _playerDifficulty;
    private float _playerMaximumHealth;
    private HumanHealth _playerHealth = null!;
    private bool _playerDead;
    private HumanDamageFlash _damageFlash = null!;
    private DeathCamera _deathCamera = null!;
    private int _guardRounds;
    private int _guardHits;
    private Random _shotRandom = null!;

    /// <summary>The engine's own generator, for the sound-variant draws <c>0x4638A0</c> makes.</summary>
    private readonly IgiRandom _soundRandom = new();

    /// <summary>The tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    private int _playerHitQuietUntil;
    private int _impactSoundQuietUntil;
    private int _tracerSkipsLogged;

    private AlarmSpeakerRegistry? _alarmSpeakers;
    private readonly Dictionary<AlarmSpeaker, AudioVoiceHandle> _sirenVoices = [];
    private readonly HashSet<AlarmSpeaker> _reportedSirenFailures = [];
    private SecurityCameraRegistry? _securityCameras;
    private readonly Dictionary<SecurityCamera, (string Sound, AudioVoiceHandle Voice)> _cameraBeepVoices = [];
    private readonly Dictionary<SecurityCamera, AudioVoiceHandle> _cameraMoveVoices = [];

    /// <summary>The flashbang's whiteout, 0–2, decaying towards zero every tick.</summary>
    /// <remarks>
    /// The retail handler writes the two strengths (<see cref="Flashbang"/>) into fields whose decay
    /// is unread; a 45-tick fall per unit of strength — a second and a half seen, three blinded — is
    /// this port's chosen curve.
    /// </remarks>
    private float _flashbangStrength;

    private const float FlashbangDecayPerTick = 1f / 45f;


    // The last frame of the outgoing backdrop, held on the GPU so it can be dissolved off over the
    // incoming one. Sized to the window and re-made when that changes.
    private TextureHandle _dissolveTexture;
    private (int Width, int Height) _dissolveSize;

    // The menu backdrop currently on screen, if one is. Held rather than used: the renderers built from it
    // read models, textures and the sky through ITS resource registry and file system, so it has to outlive
    // the handover. Replaced whole when the next backdrop arrives.
    private MissionPreview? _backdrop;

    // --- The loaded world and its renderers ---
    private WorldScene? _scene;
    private WorldRenderer? _world;
    private TerrainRenderer? _terrain;
    private SkyRenderer? _sky;
    private RainRenderer? _rain;
    private SnowRenderer? _snow;
    private IReadOnlyList<RainRenderer.BuildingVolume> _rainBuildingVolumes = [];
    private ShadowCasterPass? _shadowPass;

    // The previewed mission's soldiers, built behind the menu and cast into the shadow map so the driver
    // has already met a skinned vertex layout by the time Play is pressed. Handed straight over to
    // `_soldiers` when the backdrop is promoted, so the played mission never builds them at all.
    private SoldierRenderer? _backdropSoldiers;

    // Makes the backdrop playable a couple of milliseconds at a time while the menu is idle.
    private MissionArming? _arming;

    // The shot that carries the player out of the menu and into their own eyes.
    private readonly MissionEntryCamera _entry = new();
    private DirectionalLightSettings? _sun;
    private FreeCamera? _camera;

    // --- The player, the held weapon, the HUD and the soldiers ---
    private PlayerCamera? _player;
    private HeldWeaponRenderer? _heldWeapon;
    private GunPickupRenderer? _gunPickupRenderer;
    private StatusScreenRenderer? _hud;
    private RemasteredHudRenderer? _remasteredHud;
    private DebugOverlayRenderer? _debugOverlay;
    private MapComputerRenderer? _mapComputer;
    private MapComputerDeviceRenderer? _mapComputerDevice;
    private TacticalComputerRenderer? _tacticalComputer;
    private StatusMessageRenderer? _messages;
    private readonly MapComputerCamera _computerFlight = new();
    private ComputerHighlightCatalog? _computerHighlights;
    private MissionObjectiveRuntime? _missionObjectives;
    private CutsceneRuntime? _cutscenes;
    private MissionDescriptor? _activeMission;
    private LevelTask? _activeLevelRoot;
    private bool _computerOpen;
    private bool _computerCamFired;

    /// <summary>How much longer than the original the remaster holds a status message, in seconds.</summary>
    private const float RemasteredMessageHoldSeconds = 3f;

    /// <summary>Gets the tactical interface when the preset asks for it and it built its faces.</summary>
    /// <remarks>
    /// Read per frame rather than latched at mission bring-up, so <c>r_computer_tactical</c> can be
    /// thrown from the console between one frame and the next — which is the only practical way to
    /// compare the redesign against the original it replaces. Both renderers are therefore built for
    /// every mission; each costs an archive or a cached atlas, and neither is worth a restart.
    /// </remarks>
    private TacticalComputerRenderer? TacticalComputer =>
        RenderCVars.TacticalComputer.Value && _tacticalComputer is { IsReady: true } ready ? ready : null;

    private bool _missionResultHandled;
    private int _missionFailureTicks;
    private bool _missionFailedMenu;
    private BulletMarkRenderer? _marks;
    private TracerTrailRenderer? _tracers;
    private ImpactParticleRenderer? _impactParticles;
    private CameraStatusLightRenderer? _cameraStatusLights;
    private GrenadeRenderer? _grenadeRenderer;
    private LoadedMission? _loadedMissionForWeapon;
    private ViewModelRenderer? _arms;
    private SoldierRenderer? _soldiers;
    private TerrainData? _playerTerrain;
    private GameMaterialRegistry? _worldMaterials;
    private AmmoTypeRegistry? _ammoTypes;
    private bool _playerMode;
    private ObjectCollisionIndex? _worldSolids;
    private DoorRegistry? _doors;
    private WorldInteractionRegistry? _interactions;
    private GunPickupRegistry? _gunPickups;
    private GenericPickupRegistry? _genericPickups;

    // --- The menus and the cinematic shell ---
    private MenuRenderer? _renderer;
    private MenuShowcaseCamera? _showcaseCamera;
    private MenuCameraLibrary _menuCameras = MenuCameraLibrary.Empty;

    /// <summary>Wires the game to the window, device, file system and options it runs against.</summary>
    /// <param name="platform">The desktop platform host, for reporting its name.</param>
    /// <param name="window">The game window whose input and frames drive the loop.</param>
    /// <param name="device">The graphics device every renderer draws through.</param>
    /// <param name="fileSystem">The virtual file system the original installation is read through.</param>
    /// <param name="options">The parsed command line.</param>
    /// <param name="gamePath">The original installation directory.</param>
    /// <param name="logger">The log; wrapped by the developer console once it starts.</param>
    public DesktopGame(
        DesktopPlatformHost platform,
        SilkGameWindow window,
        GLGraphicsDevice device,
        VirtualFileSystem fileSystem,
        DesktopOptions options,
        string gamePath,
        IIgiLogger logger)
    {
        _platform = platform;
        _window = window;
        _device = device;
        _fileSystem = fileSystem;
        _options = options;
        _gamePath = gamePath;
        _logger = logger;
    }

    /// <summary>Builds the world, runs the frame loop until the game is asked to quit, then shuts down.</summary>
    /// <returns>The process exit code.</returns>
    public int Run()
    {
        // The UI is authored against the original's 640x480 back buffer and is centred, NEVER SCALED, so
        // the projection is the back buffer's own size and MenuLayout.ToScreen does the centring. Passing
        // the authored size here instead scales 640x480 up to fill the window AND then scales the centring
        // offset with it, which puts the menu in the bottom-right corner of anything but a 640x480 window.
        Resize(_window.Width, _window.Height);
        _window.Resized += (_, e) => Resize(e.Width, e.Height);

        void Resize(int width, int height)
        {
            _logger.Information("Desktop", $"Framebuffer resize event: {width}x{height}.");
            _device.SetViewport(new Viewport(0, 0, width, height));
            _device.Set2DProjection(width, height);
        }

        // Entry labels are keys into the language archive, so it has to be registered before the menu can
        // resolve any of them.
        _resourceFileSystem = _fileSystem;
        _resources = new(_resourceFileSystem, _logger);

        // menusystem.res holds the menu's own labels; missions.res holds the mission names and briefings,
        // because a descriptor's "Mission 1 Name" is a key into it rather than the title itself.
        //
        // Kept as bytes as well as registered, because these are the session's archives rather than any
        // one level's: a mission previewed for the menu builds into its own private ResourceRegistry, and
        // that registry becomes the game's own if the preview is promoted into the played mission. Without
        // the bytes to hand, the promoted registry would be missing every string the menus, the mission
        // briefings and the handheld computer resolve through. Bytes rather than a shared registry read,
        // so a worker never touches this one.
        foreach (string archive in new[]
        {
            MenuStringTable.GetArchiveName(),
            MenuStringTable.GetMissionArchiveName(),
            MapComputerRenderer.ComputerStringsArchive,
            MapComputerRenderer.ObjectiveStringsArchive,
            MapComputerRenderer.MessageStringsArchive,
        })
        {
            if (_fileSystem.Exists(archive))
            {
                byte[] bytes = _fileSystem.ReadAllBytes(archive);
                _sessionArchives[archive] = bytes;
                _resources.Register(archive, bytes);
            }
        }

        // Settings load from the retail config.qvm, and from our own file in preference to it when one
        // exists. Nothing here ever writes into the original installation.
        _configuration = DesktopConfiguration.Load(_fileSystem, _logger);
        DesktopConfigHost configHost = new(_device, _window)
        {
            ApplyResolution = (w, h) =>
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
            },
        };

        ConfigNatives configNatives = new(_configuration, configHost);

        MenuStringTable strings = new(_resources);
        configHost.Strings = strings;
        SpriteBatch batch = new(_device);
        _renderer = null;
        bool running = true;

        // The developer console: the CVar registry, settings.cfg and the on-screen view. Brought up before
        // anything reads a setting, so the saved file and the command line's +statements are already in
        // effect by the time the renderer and the game are built. Everything logged from here on is
        // mirrored into its scrollback, so opening it shows the log rather than an empty panel.
        using DesktopConsole console = new(_device, _logger);
        console.QuitRequested += (_, _) => running = false;
        console.Start(_fileSystem, _options.StartupCommands, useSavedSettings: !_options.Lockstep);
        _logger = console.WrapLogger(_logger);

        // -novsync is the older spelling of r_vsync 0. It forces the setting rather than shadowing it, so
        // the console and the flag can never report different things.
        if (!_options.VSync)
        {
            RenderCVars.VSync.Value = false;
        }

        // Audio comes up here, after settings.cfg and the +statements, for the same reason the renderer's
        // settings do: the voice count and the output rate are read once, when the device opens.
        OpenAudioDevice();

        // Set before anything uploads a texture: mip generation happens at upload, so a level loaded
        // before this would keep the unfiltered tiles it started with.
        _device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);
        RenderCVars.Anisotropy.Changed += (_, _) =>
            _device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);

        _device.VSyncEnabled = RenderCVars.VSync.Value;
        RenderCVars.VSync.Changed += (_, _) => _device.VSyncEnabled = RenderCVars.VSync.Value;

        RenderCVars.WeatherEnabled.Changed += (_, _) => ApplyWeatherSettings();
        RenderCVars.WeatherStyle.Changed += (_, _) => ApplyWeatherSettings();
        RenderCVars.WeatherSpeed.Changed += (_, _) => ApplyWeatherSettings();

        // Timing is pushed on change rather than read per frame, because switching it off is what discards
        // the history -- the peaks after switching it back on should belong to the run being watched.
        _device.SetStageTiming(RenderCVars.PerformanceLog.Value);
        RenderCVars.PerformanceLog.Changed += (_, _) => _device.SetStageTiming(RenderCVars.PerformanceLog.Value);

        DesktopMenuHost menuHost = new(
            _window.Input,
            batch,
            () => _renderer,
            () => _window.Width,
            () => _window.Height,
            () => running = false,
            _logger)
        {
            // The menu and its hit-test have to agree about the magnification -- this is the inverse of
            // what MenuRenderer draws with. See UiScale for why the interface is scaled at all.
            Scale = _options.UiScale,

            // Opened above, so the menu's own archive is already mounted by the time a screen appears.
            Sounds = _sounds,
        };

        // The flow state machine owns the frame, and the menu is a task beneath it — so the frame walk
        // updates and renders the menu rather than this loop doing either by hand.
        _scheduler = new(_device);

        // -lockstep replaces the wall clock with a synthetic one advanced a fixed amount per loop pass,
        // so how long a frame took to draw stops deciding how many ticks it covered. Without it a capture
        // is not reproducible -- two identical command lines land on different tick counts and so on
        // different soldier and animation states, which measured at 9% of pixels differing. Everything
        // else in the simulation is already deterministic (IgiRandom is seeded, not time-seeded), so this
        // is the whole of what a reference capture needs.
        ManualTimeSource? lockstepClock = _options.Lockstep ? new ManualTimeSource() : null;

        // Held as a field, and built here rather than left to GameLoop's own default, because a second
        // loop is created for every mission the menu previews and one of those eventually takes the
        // session over. Two loops that each defaulted to their own SystemTimeSource would read
        // milliseconds from different origins, and every `last*Milliseconds` delta in the frame loop
        // would jump the moment the swap happened. One source, shared, keeps the handover invisible.
        _timeSource = lockstepClock is not null ? lockstepClock : new SystemTimeSource();
        _loop = new(time: _timeSource, host: menuHost, renderer: _scheduler, logger: _logger);

        // The mission table is built the way the original builds it: walk LOCAL:missions and run every
        // mission.qvm, each of which publishes one descriptor through DefineMission. It has to happen
        // before the Select Mission screen asks for the list.
        _missions = new(_logger);
        _missions.Register(_loop.Natives);
        GameMissionState missionState = new();
        missionState.Register(_loop.Natives);

        // Game_EnableMusic / Game_DisableMusic / Game_SetMusicVolume. Without these the options menu's
        // music slider -- whose script is literally Game_SetMusicVolume(this.vValue) -- moves and does
        // nothing.
        GameMusicNatives musicNatives = new(_music!);
        musicNatives.Register(_loop.Natives);

        // What a menu backdrop needs to be promotable into the played mission rather than thrown away.
        // Built here because every part of it is finished being written by this point and none of it is
        // mutated afterwards, which is what makes it safe to hand to a worker.
        _previewContext = new MissionPreviewContext(
            _timeSource, menuHost, _scheduler, missionState, _sessionArchives);

        // The magic-object table is what turns a level's few hundred scripted objects into its full
        // contents: every model's attachments name an entry here. Loading it is a startup step, like the
        // mission scan, because a level's own scripts never mention it.
        _magic = new(_logger);
        _vehiclePhysics = new(_logger);
        _animationTriggers = new(_logger);

        // Without these the level's own HumanPlayer task is never built, and the spawn the mission
        // declares cannot be found - the tree loads perfectly well without it and simply has no player.
        PlayerTaskTypes.RegisterAll(_loop.Types);

        // Without these, HumanAI/PatrolPath/PatrolPathCommand load as untyped DeclaredTasks: the soldier's
        // brain cannot be found, so no soldier gets its own MISSION:AI/<taskid> script, so nothing ever names a
        // patrol path and every guard falls back to the archetype and stands still.
        AiTaskTypes.RegisterAll(_loop.Types);

        // A level's CutScene tasks export isRun/isFinished/nTick, which the mission AI scripts read in their
        // idle branch. Without the type registered the symbols never bind and guard.qsc faults every idle tick
        // on CutScene_1200.nTick, cutting its patrol handling short. Its fourteen parameters match the level's
        // Task_DeclareParameters("CutScene", ...) declaration so the registry's verification passes.
        _loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);

        // A level parks its intro and outro cut-scene actors inside ConditionalContainer tasks gated on
        // cut-scene / mission-progress state that is false during play. Without the type registered the port
        // built each as a plain container and rendered its children, so those actors -- including a second
        // David Jones on the player's own model, the outro "Jones drive truck" -- stood in the level at
        // mission start where the original never registers them. Registering the type lets the gate below
        // evaluate each container's condition and hide the subtrees the original leaves unregistered.
        _loop.Types.Register<ConditionalContainerTask>(
            ConditionalContainerTask.TypeName);

        // Every soldier's combat numbers - 29 AI types x 3 difficulties out of common/ai/settings.qsc.
        // Loaded before any level, as sub_406DF0 is: the table has to exist before a HumanAI is configured.
        HumanAiConfigRegistry.RegisterTypes(_loop.Types);
        _aiConfig = new(_logger);
        _magic.Register(_loop.Natives);
        _animationTriggers.Register(_loop.Natives);

        try
        {
            _missions.Scan(_fileSystem, _resources, _loop.Natives);
            _magic.Load(_resources, _loop.Natives);
            _vehiclePhysics.Load(_resources);
            _animationTriggers.Load(_resources, _loop.Natives);
            _aiConfig.Load(_resources, _loop.Natives, _loop.Tasks);
            configHost.Missions = _missions;
        }
        catch (EngineFatalException ex)
        {
            _logger.Warning("Mission", $"The mission scan failed, so no missions are selectable: {ex.Message}");
        }

        bool reportedWorld = false;
        bool reportedShot = false;
        _guardRounds = 0;
        _guardHits = 0;

        // The player's own damage. Its zone table is the player's, whose head is worth 1.0 rather than the
        // soldiers' 100 -- humanplayer.qvm overrides the executable's default (weapons.md 11). The player's
        // own hit handler (0x412440) additionally scales every hit by the profile difficulty: x0.75 easy,
        // x1.0 normal, x1.25 hard -- a scale that applies to the PLAYER only, never to a soldier.
        _playerDifficulty = (int)(_options.Difficulty
            ?? (AiDifficulty)Math.Clamp(_configuration.Active?.Difficulty ?? 1, 0, 2));

        // 0x489C30 supplies the universal base value first. Mission loading replaces it with argument 7
        // from humanplayer.qvm, just as HumanPlayer's constructor overwrites Human+600 at 0x4104E1. Do
        // not bake in the shipped script's 3.0: an externally retuned humanplayer.qvm owns this range.
        _playerMaximumHealth = HumanHitZoneTable.MaximumHealth;
        _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
        };
        _playerDead = false;

        // The red wash the screen takes on a hit -- four corner alphas that decay a forty-fifth a tick
        // (sub_411300) and paint a screen-filling red quad (HumanPlayer_DrawDamageFlash). It survives the
        // player's death, so the corpse's-eye view stays red rather than snapping back to a clean picture.
        _damageFlash = new();

        // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye
        // to a third-person shot of the body (HumanCamera death mode). The body it looks at is the player's
        // own third-person rig, which SoldierRenderer loads and poses with the death fall.
        _deathCamera = new();
        InteractionCamera interactionCamera = new();
        bool climbActive = false;
        bool worldInteractionActive = false;
        int temporalFrame = 0;

        // A scripted free-camera flight (-camend), if one was asked for. The opening pose is captured on
        // the first rendered frame and the camera is moved along a straight line to the end pose across the
        // frame budget, so the temporal filters (GI, TAA) can be seen resolving under real motion -- which a
        // static capture cannot show. flightFrame is the rendered-frame index, and the sequence's frame number.
        bool flightStarted = false;
        Vector3d flightStartPos = default;
        float flightStartYaw = 0f;
        float flightStartPitch = 0f;
        int flightFrame = 0;

        bool climbSlideArmed = false;
        _shotRandom = new(20000101);
        int reportedHumanHits = 0;
        _scene = null;
        _world = null;
        _terrain = null;
        _sky = null;
        _shadowPass = null;

        // Authored atmosphere. Levels with no entry fall back to what their own FlatSky task asks for.
        FogPresets fogPresets = FogPresets.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "fog-presets.json"),
            _logger);

        _materials = MaterialLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "materials.json"),
            _logger);

        // The derived surface detail this game never shipped -- normals, roughness, metal. Built by
        // `ResExtract maps` and pointed at by r_material_pack; an installation that has never run the tool
        // has none, and renders exactly as it did before any of this existed.
        //
        // Constructed once for the whole session rather than per level, because a texture pack is shared
        // across missions and the maps are the textures' own property rather than a level's.
        //
        // The cache is what makes the tool optional. A texture with no map derives one on a worker the
        // first time it is seen and the result is written here, so a player who never runs ResExtract
        // still gets the maps -- from their OWN installation's art, which is the only place this
        // repository can get it from. Under %APPDATA% rather than in the checkout because it is derived
        // per-user data, and because the game's art with a filter over it is not ours to redistribute.
        _detailMaps = new(
            _device,
            RenderCVars.MaterialPack.Value,
            _logger,
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "detail"));

        // Skeleton 0's and skeleton 5's animation sets, read here rather than when a mission starts.
        // They live under common/, so they are the same 2.4 MB of IFF for every mission, and parsing
        // them was the single largest thing standing a player up used to do on the render thread. Doing
        // it at start-up costs a moment nobody is looking at instead of a moment everybody is.
        ReadSharedAnimations();


        // Re-graded for whatever is actually on screen: the mission being played, else the menu's
        // backdrop, else whatever -mission asked for. Reading `_options.StartMission` unconditionally
        // meant changing the table from the console while browsing the campaign re-graded to mission 0.
        RenderCVars.ColourLookupTable.Changed += (_, _) =>
            ApplyColourLookup(_activeMission?.Id ?? _backdrop?.MissionId ?? _options.StartMission);

        // The level's own sun, read from its Dirlight task -- section 12.
        // Null until a level is loaded, and for a level that declares none.
        _sun = null;
        _camera = null;

        // The walking player. F1 swaps between it and the free camera: the camera is how a level is
        // inspected, the player is how it is stood in. Both draw through the same Camera3D.
        _player = null;
        _heldWeapon = null;
        _hud = null;
        _debugOverlay = null;
        _marks = null;
        _tracers = null;
        _impactParticles = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _loadedMissionForWeapon = null;
        _arms = null;
        _soldiers = null;
        _playerTerrain = null;

        // The material table, shared with the guard-round handler so an AI round's impact can leave the
        // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
        _worldMaterials = null;
        _ammoTypes = null;
        _alarmSpeakers = null;
        _sirenVoices.Clear();
        _playerMode = false;

        // The debug overlay (F3): the player's state and a crosshair "what am I looking at" readout. The
        // collision index is kept here so the overlay can name the object under the crosshair.
        _worldSolids = null;
        bool showDebug = false;

        menuHost.StartGame = () => StartMission(missionState.PendingMissionId);

        menuHost.LoadMainMenu = () =>
        {
            MenuSystem menu = MenuSystem.Load(
                _fileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
                _loop,
                _logger);

            // The mission briefing images are in their own archive beside the menu's.
            _renderer = new MenuRenderer(
                _device,
                _fileSystem,
                menu.Manager.ResourcePath,
                strings,
                ["LOCAL:menusystem/missionsprites.res"])
            {
                Scale = _options.UiScale,
            };

            // -screen is a development shortcut, not a game feature: the script decides where the menu
            // opens, and this overwrites that one parameter before the manager reads it.
            if (_options.StartScreen > 0)
            {
                menu.Manager.CurrentMenuScreen = _options.StartScreen;
            }

            menu.Manager.Configuration = configNatives;

            // Game_SetMission belongs to the game, not the menu system, and a menu expression is evaluated
            // by the menu's own evaluator rather than by the script VM — so registering it with the VM is
            // not enough to make the Select Mission screen's OK button work.
            // Both of these are game natives rather than menu-system ones, and a menu expression is
            // evaluated by the menu's own evaluator rather than by the script VM -- so registering them
            // with the VM is not enough to make the Select Mission button or the music slider work.
            menu.Manager.ExternalNatives = (name, arguments) =>
                missionState.TryInvokeFromMenu(name, arguments) ?? musicNatives.TryInvokeFromMenu(name, arguments);
            menu.Manager.Initialize(menu, menuHost, _logger);
        };

        // R11: the cinematic shell (OpenIGI.Frontend) is the default front end; -vanilla and -vanillamenu
        // reach the byte-for-byte retail menus instead. -mission still skips the menus straight into a level.
        Shell? shell = null;
        _showcaseCamera = null;
        MissionWarmer? missionWarmer = null;
        MissionPreviewLoader? previewLoader = null;
        _menuCameras = MenuCameraLibrary.Empty;
        UiFont? shellBodyFont = null;
        UiFont? shellHeadingFont = null;

        // What the mission-entry shot types over its vantage. Filled when the shot begins.
        string entryTitle = string.Empty;
        string entrySubtitle = string.Empty;

        // Where the shot's character stands, in engine units, and which way it faces.
        Vector3d entrySpawnPosition = default;
        float entrySpawnGamma = 0f;
        bool menuMode = false;

        // The shell is up over a LIVE mission -- the pause menu -- rather than over the showcase. It always
        // implies menuMode; what it adds is that the backdrop is already the right picture. So nothing is
        // streamed in behind the menu, the showcase orbit does not take the camera off the player, and the
        // world is frozen instead of advanced. Escape opens it and Escape closes it again.
        bool pauseMenu = false;
        bool showcasePending = false;

        // -entrycam: Play has not been pressed yet. Set only when the flag asked for it.
        bool entryCamPending = false;
        DesktopShellHost? shellHostForEntryCam = null;

        // When the mission-entry shot last read the clock.
        int lastEntryMilliseconds = 0;
        int lastComputerMilliseconds = 0;
        const int ShowcaseMissionId = 1;
        int currentShowcaseMissionId = ShowcaseMissionId;
        int? pendingPreviewMissionId = null;
        int previewRequestedAtMs = 0;

        // The backdrop cross-dissolve. 1 is the outgoing level's held frame fully covering the incoming
        // one, 0 is the incoming level alone; it runs on an eased tween on real elapsed time. There is no
        // dip through black any more: the two pictures blend directly into each other.
        var dissolve = new Tween(0f, duration: 0.42f, Ease.InOutQuad);

        // Set on the frame the backdrop is to be held, cleared once the hold has happened; the swap then
        // runs on the frame after, so what was captured is the OLD level. The very first backdrop has no
        // outgoing level to hold — the menu is over its dark clear — so there is nothing to dissolve from
        // and it simply appears.
        bool holdFrameForDissolve = false;
        bool heldFrameReady = false;
        bool heldFrameIsPicture = false;

        // How long a mission has to stay selected before its level is built. Every selection re-arms the
        // timer, so running down the list faster than this builds nothing at all. It can be short now that
        // building costs the render thread nothing -- it is a debounce against pointless work, not a shield
        // for the frame rate.
        const int PreviewSettleMs = 260;


        // -vanillamenu reaches the retail menus, which carry their own in-game menu; everything else runs
        // the cinematic shell. -mission still opens straight into a level — what it no longer does is leave
        // the shell unbuilt, because the pause menu IS the shell and Escape has to reach it from a mission
        // the menus were skipped into.
        if (_options.VanillaMenu && _options.StartMission <= 0)
        {
            _loop.Flow.Request(FlowEvent.MainMenu);
        }
        else
        {
            _menuCameras = MenuCameraLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);
            missionWarmer = new MissionWarmer(_gamePath, _missions, _logger);
            previewLoader = new MissionPreviewLoader(
                _gamePath, _missions, _magic, _menuCameras, _previewContext, _logger);

            // Saira for body, Saira Condensed as a distance field for the headings the shell sets large.
            shellBodyFont = UiFontLibrary.Load(_device, UiFontLibrary.Body, 48f);
            shellHeadingFont = UiFontLibrary.Load(_device, UiFontLibrary.Heading, 64f, FontAtlasKind.SignedDistanceField);
            var shellTheme = new UiTheme(shellBodyFont, shellHeadingFont);

            // Held so the mission-entry shot can typeset its title with the same faces the shell uses.
            _shellBodyFont = shellBodyFont;
            _shellHeadingFont = shellHeadingFont;

            // The real DefineMission data, resolved to display strings through the language table, in the
            // order the CAMPAIGN CHAIN gives — each descriptor names its successor. The registry's own
            // order is the startup scan's, which is a directory walk and therefore alphabetical, so it
            // listed level 10 between level 1 and level 2. The retail menu walks the chain (0x405660) and
            // so does this.
            var shellMissions = new List<MissionInfo>();
            int missionNumber = 1;
            foreach (MissionDescriptor descriptor in _missions.InCampaignOrder())
            {
                shellMissions.Add(new MissionInfo(
                    descriptor.Id,
                    missionNumber++,
                    strings.Get(descriptor.Name),
                    strings.Get(descriptor.Description),
                    descriptor.BriefingSprite));
            }

            var saves = new SaveSlotStore(Path.Combine(SettingsPaths.GetUserDirectory(), "saves"));

            var shellHost = new DesktopShellHost(
                startMission: (id, difficulty) =>
                {
                    if (_configuration.Active is { } activeProfile)
                    {
                        activeProfile.Difficulty = difficulty;
                    }

                    // Reachable from the pause menu as well as from the front end — the campaign is in the
                    // bar either way — so the shell is told it is no longer over a mission it was paused on.
                    shell?.ClosePauseMenu();
                    menuMode = false;
                    pauseMenu = false;
                    ApplyGameplayDepthOfField();

                    // The menu has been drawing this very level for as long as the player has been
                    // reading the briefing, built on a worker and complete. When it is the mission they
                    // picked, the shot begins here and the level is PROMOTED partway through it rather
                    // than loaded a second time — see MissionEntryCamera and PromoteBackdrop. `_backdrop`
                    // is null over a played mission, so this cannot fire from the pause menu.
                    //
                    // -lockstep is excluded: the shot runs on wall-clock time, and a reference capture
                    // has to land on the same frame every run.
                    if (_backdrop is { } backdrop
                        && backdrop.MissionId == id
                        && !_options.Lockstep
                        && _showcaseCamera is { } orbit
                        && PlayerSpawn.TryFindGameplay(backdrop.LevelRoot, out PlayerSpawnPoint entrySpawn))
                    {
                        BeginWatchingFramePasses($"Entering mission {id}");
                        // An explicitly requested authored intro owns the mission from its first frame,
                        // so promote immediately and let its EditCamera take over. In the normal (no
                        // -cutscene) path the cinematic is skipped during promotion; keep the shell's
                        // continuous rise/travel/descent shot instead of cutting straight to gameplay.
                        if (_options.PlayCutscenes && HasAuthoredCutscene(backdrop.LevelRoot))
                        {
                            if (PromoteBackdrop(backdrop))
                            {
                                _window.CursorVisible = false;
                                ReportWatchedFramePassesIn(2);
                                return;
                            }
                        }

                        lastEntryMilliseconds = _loop.TimeSource.Milliseconds;
                        entrySpawnPosition = entrySpawn.Position;
                        entrySpawnGamma = entrySpawn.Gamma;

                        MissionInfo? entering = shellMissions.FirstOrDefault(m => m.Id == id);
                        entryTitle = entering is null
                            ? string.Empty
                            : $"{entering.Number:00}  {entering.Name}".ToUpperInvariant();
                        // The player's word for it, not the settings.qsc row key -- NameOf answers with
                        // GD_2, which is what this caption used to put on screen.
                        entrySubtitle = AiDifficultyNames.Of(difficulty).ToUpperInvariant();

                        _entry.Begin(
                            CameraPose.FromCamera(orbit.Update(0f)),
                            entrySpawn.Position,
                            backdrop.Scene.GroundHeight);
                        _window.CursorVisible = false;
                        return;
                    }

                    BeginWatchingFramePasses($"Loading mission {id}");
                    StartMission(id);
                    ReportWatchedFramePassesIn(5);
                },
                resumeMission: () =>
                {
                    // Nothing is reloaded and nobody is placed again: the level was never torn down, only
                    // left un-ticked, so resuming is this loop putting the menu away.
                    shell?.ClosePauseMenu();
                    menuMode = false;
                    pauseMenu = false;
                    _window.CursorVisible = false;
                },
                restartMission: () =>
                {
                    if (_activeMission is { } mission)
                    {
                        shell?.ClosePauseMenu();
                        menuMode = false;
                        pauseMenu = false;
                        ApplyGameplayDepthOfField();
                        StartMission(mission.Id);
                        _window.CursorVisible = false;
                    }
                },
                abandonMission: () =>
                {
                    ReleasePlayedMission();
                    shell?.ClosePauseMenu();
                    shell?.OpenOn("campaign");
                    menuMode = true;

                    // The shell is over the showcase again from here, so the backdrop has to be streamed:
                    // the level it was drawing over has just been released, and it is framed as a
                    // backdrop again rather than as something being played through.
                    pauseMenu = false;
                    showcasePending = true;
                    ApplyMenuDepthOfField();
                    _window.CursorVisible = true;
                },
                quit: () => running = false,
                previewMission: id =>
                {
                    // Never over a live mission. Adopting a preview disposes the world renderer and drops
                    // the player, which from the pause menu would be the shell demolishing the mission it
                    // is drawn over. The screens already know not to ask (ShellContext.InGame); this is the
                    // guarantee rather than the manners.
                    if (pauseMenu)
                    {
                        return;
                    }

                    int wanted = id ?? ShowcaseMissionId;
                    if (wanted != currentShowcaseMissionId)
                    {
                        // Warm the level's files on a worker now, and note the moment: once the selection
                        // has held for the settle window the loop asks the preview loader to build it, also
                        // on a worker. Nothing here touches the render thread, so a selection that keeps
                        // moving simply keeps re-arming this.
                        pendingPreviewMissionId = wanted;
                        previewRequestedAtMs = _loop.TimeSource.Milliseconds;
                        missionWarmer?.Warm(wanted);
                    }
                    else
                    {
                        pendingPreviewMissionId = null;
                    }
                },
                saveSettings: () => console.SaveSettings(),
                saveProfiles: () => DesktopConfiguration.Save(_configuration, _logger),
                captureThumbnail: (out captureWidth, out captureHeight) =>
                {
                    captureWidth = _window.Width;
                    captureHeight = _window.Height;
                    var pixels = new Bgra32[captureWidth * captureHeight];
                    _device.CaptureFrame(captureWidth, captureHeight, pixels);
                    return pixels;
                });

            shellHostForEntryCam = shellHost;

            shell = new Shell(new ShellContext(
                shellTheme,
                shellHost,
                new ScreenStack(),
                GameProfileResolver.BuildCatalogue(_gamePath, null, null),
                shellMissions,
                CVarRegistry.Default,
                GraphicsPresetRegistry.Default,
                _configuration,
                saves));

            // -shelltab is a development shortcut, not a game feature, and the shell's counterpart to
            // -screen: it opens straight on a section so a capture does not need someone to click there.
            if (_options.ShellTab is { Length: > 0 } openOn && !shell.OpenOn(openOn))
            {
                _logger.Warning("Menu", $"-shelltab '{openOn}' is not a section; opening on the main menu.");
            }

            if (_options.StartMission > 0)
            {
                // Straight into the level, with the shell built and waiting behind Escape rather than shown.
                StartMission(_options.StartMission);
            }
            else
            {
                // The showcase level is loaded a couple of frames into the loop, not here: loading it now
                // would block for a few seconds before the window ever showed the menu (the "blue screen"
                // that is just the frame's clear). Deferring it lets the menu draw first over a dark
                // backdrop, then the showcase streams in behind it.
                menuMode = true;
                showcasePending = true;

                // Heavy depth of field behind the UI, on the deferred path the shell's preset leaves on.
                // Through a helper because what it displaces has to be given back: these are Archive
                // variables, so leaving the menu's framing in place meant the first save wrote it out as
                // the player's own setting.
                ApplyMenuDepthOfField();

                // -entrycam: open on the campaign and remember that Play is to be pressed as soon as this
                // mission's backdrop is up. The backdrop itself is requested through the ordinary showcase
                // path below, so what gets measured is the real thing rather than a special case.
                if (_options.EntryCamMission > 0)
                {
                    shell.OpenOn("campaign");
                    entryCamPending = true;
                }
            }
        }

        // Watch every pass from here on, so a hitch is attributed to the frame it happened on rather than
        // only reported as a run-wide peak. The stage profiler's peaks say a stage was once expensive
        // without saying when, and "when" is the whole question: a cost paid behind the menu is free and
        // the same cost paid on the frame Play is pressed is the bug.
        BeginWatchingFramePasses("Session");
        long passStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        FramePerformanceTracker framePerformance = new();

        _logger.Information(
            "Render",
            FormattableString.Invariant(
                $"Camera effects: r_motion_blur {RenderCVars.MotionBlur.Value:0.###}, r_camera_taps {RenderCVars.CameraTaps.Value}, r_dof {RenderCVars.FocalDistance.Value:0.#}/{RenderCVars.FocalRange.Value:0.#}, r_dof_blur {RenderCVars.FocalBlur.Value:0.#}, r_hdr {RenderCVars.OffscreenScene.Value}, r_deferred {RenderCVars.Deferred.Value}, r_taa {RenderCVars.TemporalFeedback.Value:0.###}, r_smooth {RenderCVars.SmoothPresentation.Value}, r_scale {RenderCVars.RenderScale.Value:0.###}."));

        _window.CloseRequested += (_, _) => running = false;
        _window.Show();
        _logger.Information("Desktop", $"{_platform.Name} up on {_device.Capabilities.Renderer}.");

        // -poidump: build every mission, find the places its menu backdrop should circle, write them out
        // and quit. A tool run once; what it writes is then owned and edited by hand.
        if (_options.ShowcasePointDump is { Length: > 0 } dumpPath)
        {
            DumpShowcasePoints(dumpPath, strings);
            running = false;
        }

        // Kept as an opt-in diagnostic rather than a player-facing boot step. It compiles temporary
        // shaders and reads their results back, so on a cold driver it can make an otherwise ready window
        // appear to hang for seconds. A developer can still ask for the exact same proof with
        // +r_compute_selftest 1.
        if (RenderCVars.ComputeSelfTest.Value)
        {
            GLComputeSelfTest.Run(_device, _logger);
        }

        // Used only for a level that declares no Dirlight at all. The angles are mission 1's, which the
        // probe showed put the sun 60 degrees up at noon, with the warm sun and cool ambient every retail
        // level pairs.
        DirectionalLightSettings defaultSun = new(
            DirectionalLightSettings.DirectionFrom(0.5236f, -4.92183f),
            new Vector3f(1.0f, 0.96f, 0.88f),
            new Vector3f(0.27f, 0.29f, 0.33f),
            Beta: 0.5236f,
            Gamma: -4.92183f,
            TimeOfDay: 12f,
            Intensity: 18f,
            AffectsTerrain: true,
            AffectsObjects: true);

        // Reused every frame rather than reallocated; the fit writes into it in place.
        ShadowCascade[] cascadeStorage = new ShadowCascade[ShadowSettings.MaxCascades];

        int lastCameraMilliseconds = _loop.TimeSource.Milliseconds;
        int lastConsoleMilliseconds = _loop.TimeSource.Milliseconds;
        int lastShellMilliseconds = _loop.TimeSource.Milliseconds;
        int lastMenuCameraMilliseconds = _loop.TimeSource.Milliseconds;

        // A capture run is a background process as far as the person at the keyboard is concerned: it must
        // not read the mouse and must never grab the cursor out from under whatever they are actually doing.
        bool captureMode = _options.Lockstep;

        while (running && _loop.IsRunning && !_window.IsClosing)
        {
            // The whole point of promoting the backdrop instead of loading it is that no frame is ever
            // blocked, and that is a claim about milliseconds rather than a matter of opinion. Every pass
            // is timed, and the worst one seen since Play was pressed is reported when the entry finishes.
            //
            // Measured from the top of the NEXT pass rather than at the bottom of this one, because the
            // loop body has `continue`s in it -- a pass that decides it has nothing to render leaves
            // early -- and a measurement at the bottom silently skips every pass that took one. The
            // promotion happens on a pass that does exactly that, so the first version of this timed
            // everything except the one frame it existed to measure.
            NoteFramePass(passStartedAt);
            FlushWatchedFramePasses();
            passStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            _window.PumpEvents();

            // Whatever the detail-map workers finished since the last pass, uploaded a few at a time.
            // The setting is read here rather than captured at construction so that turning
            // r_material_detail up mid-session starts deriving for whatever loads next -- it is zero
            // under vanilla and enhanced, and deriving maps the shader multiplies by nothing is waste.
            _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
            _detailMaps.PumpDerived();

            // One tick's worth of synthetic time per RENDERED FRAME, from an absolute target rather than
            // an accumulated step so the schedule cannot drift. Per loop pass would be wrong: the loop
            // pumps many times per frame, so the clock would jump a whole tick each pump and the catch-up
            // cap would fire every pass -- measured at 200 ticks for 20 frames, a world running ten times
            // too fast. Deterministic either way, but a capture should show the moment it claims to.
            if (lockstepClock is not null)
            {
                lockstepClock.Milliseconds = _options.IsBenchmark
                    ? BenchmarkSchedule.MillisecondsForFrame(_loop.Clock.FrameCount)
                    : (int)(((_loop.Clock.FrameCount + 1L) * 1000L) / GameClock.DefaultTickRate);

                // A capture must not be able to see the mouse. The window grabs the cursor in player mode,
                // so moving the mouse in another application changed what got captured -- which is exactly
                // how the first reference set came out non-reproducible for a reason that had nothing to
                // do with the clock.
                _window.Input.Captured = true;
            }

            // The console reads the frame's input first. If it is open it then marks the frame captured,
            // and every other reader below -- the debug keys, the cameras, the player, the menu -- sees an
            // idle keyboard and mouse. That is why none of them had to learn the console exists.
            int consoleNow = _loop.TimeSource.Milliseconds;
            console.Update(_window.Input, Math.Clamp((consoleNow - lastConsoleMilliseconds) / 1000f, 0f, 0.25f));
            lastConsoleMilliseconds = consoleNow;

            // The cinematic shell reads the frame's input after the console and, like it, marks the frame
            // captured so the free camera and the game beneath see an idle keyboard and pad. When the console
            // is open it owns the frame and the shell only advances its animation.
            if (menuMode && shell is not null)
            {
                int shellNow = _loop.TimeSource.Milliseconds;
                float shellElapsed = Math.Clamp((shellNow - lastShellMilliseconds) / 1000f, 0f, 0.1f);
                lastShellMilliseconds = shellNow;

                var shellFrame = new LayoutContext(_window.Width, _window.Height);
                shell.Update(shellFrame, _window.Input, shellElapsed);
                if (!console.IsOpen)
                {
                    _window.Input.Captured = true;
                }

                // Something wants the picture to change — a new backdrop has finished building, or the
                // showcase camera has held a place long enough to move to the next one. Either way the
                // frame on screen is held first, so the two can be blended rather than cut between.
                // None of it applies to the pause menu, whose picture is a level that is not going anywhere.
                bool changeWanted = !pauseMenu
                    && (previewLoader is { HasResult: true } || _showcaseCamera is { IsCutDue: true });
                if (changeWanted && !holdFrameForDissolve && !heldFrameReady)
                {
                    holdFrameForDissolve = true;
                }

                dissolve.Update(shellElapsed);
            }

            // The mission-entry shot. It runs on real elapsed time, like the menu's orbit and the
            // interface's own animation, because it is presentation: it must look the same whatever rate
            // the display is running at, and it must not be tied to the simulation's fixed tick.
            if (_entry.IsRunning)
            {
                int entryNow = _loop.TimeSource.Milliseconds;
                float entryElapsed = Math.Clamp((entryNow - lastEntryMilliseconds) / 1000f, 0f, 0.1f);
                lastEntryMilliseconds = entryNow;

                // The orbit keeps turning under the start of the shot so the rise can leave at the speed
                // the menu was already moving at, rather than from a standstill.
                CameraPose liveOrbit = _showcaseCamera is { } liveShowcase
                    ? CameraPose.FromCamera(liveShowcase.Update(entryElapsed))
                    : _entry.Pose;

                _entry.Update(
                    entryElapsed,
                    liveOrbit,
                    _player is { } standing ? CameraPose.FromCamera(standing.Camera) : null);

                // The hand-over, taken at the beat: the one moment in the shot where the camera is not
                // travelling, so whatever it costs is spent against a picture that was holding anyway.
                if (_entry.WantsPromotion)
                {
                    if (_backdrop is { } entering && PromoteBackdrop(entering))
                    {
                        _entry.NotifyPromoted();
                    }
                    else
                    {
                        // Nothing to promote. Rather than hold forever, let the shot finish and land on
                        // whatever the view already is.
                        _entry.NotifyPromoted();
                        _logger.Warning("Menu", "The mission-entry shot had no backdrop left to promote.");
                    }
                }

                // Nothing takes input for the length of the shot -- not the shell, not the player, not
                // the free camera. The shell still ANIMATES, so its fade plays out; it just hears nothing.
                if (_entry.IsCameraActive && !console.IsOpen)
                {
                    _window.Input.Captured = true;
                }

                // The character standing at the spawn, visible for the dive and taken away once the
                // camera is close enough that its head fills the frame. Placed and re-skinned every frame
                // it is shown rather than merely switched on: the body is uploaded as a rest mesh and has
                // no pose until something puts one in it.
                if (_soldiers is { } bodies)
                {
                    if (_entry.ShowPlayerBody)
                    {
                        bodies.StandPlayerBodyAt(entrySpawnPosition, entrySpawnGamma);
                    }
                    else
                    {
                        bodies.HidePlayerBody();
                    }
                }

                // The shot's own framing: focused on what it is looking at rather than on the menu's fixed
                // forty metres, and smeared by its own movement. Both hand back to the player's settings.
                ApplyEntryFraming();

                if (_entry.Phase == MissionEntryPhase.Done)
                {
                    _entry.Reset();
                    _soldiers?.HidePlayerBody();
                    ApplyGameplayDepthOfField();
                    ReportWatchedFramePassesIn(1);

                    // The eye is drawn between its last two ticks, and it has just been somewhere else
                    // entirely; without dropping the pair the first gameplay frame interpolates across
                    // the whole descent.
                    _logger.Information("Menu", "The mission-entry shot has handed over.");
                }
            }

            // Escape brings the shell up over the level as a pause menu. It goes AFTER the shell's own
            // update, so the frame that opens the menu is not also the frame the menu reads a back press
            // on -- and after it the shell has marked the frame captured, which is what stops the Escape
            // that closed the menu from opening it again on the same pass.
            //
            // It also goes BEFORE the handheld computer below, which owns Escape while it is open: closing
            // the computer clears the flag, so a check placed after it would see a closed computer and a
            // still-pressed Escape and pause the game in the same breath.
            if (!menuMode
                && !console.IsOpen
                && _cutscenes?.IsPlaying == true
                && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
            {
                _cutscenes.RequestBreak();
                StopCutsceneSounds();
                _missionObjectives?.ClearCutsceneMessages();
            }

            if (!menuMode
                && !console.IsOpen
                && !_computerOpen
                && !_missionFailedMenu
                && _cutscenes?.IsPlaying != true
                && shell is not null
                && _activeMission is not null
                && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
            {
                menuMode = true;
                pauseMenu = true;
                shell.OpenPauseMenu();

                // The pointer belongs to the menu now. The look is what holds the cursor captive, so the
                // player is told to let go of it rather than the lock being fought over each frame.
                _player?.HoldStill();
                _window.CursorLocked = false;

                // The eye is drawn between its last two ticks and neither of them moves again until the
                // menu closes, so the pair is dropped: without this the frozen picture rocked back and
                // forth between two positions a tick apart as the blend swept on.
                _logger.Information("Menu", "Paused.");
            }

            // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
            // freezes player/camera input while the world continues its fixed-tick simulation.
            if (_missionFailedMenu && !menuMode && !console.IsOpen && _activeMission is { } failedMission)
            {
                if (_window.Input.Keyboard.WasPressed(Input.KeyCode.Return)
                    || _window.Input.Keyboard.WasPressed(Input.KeyCode.NumpadEnter))
                {
                    StartMission(failedMission.Id);
                    _window.CursorVisible = false;
                }
                else if (_window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
                {
                    ReleasePlayedMission();
                    if (shell is not null)
                    {
                        shell.OpenOn("campaign");
                        menuMode = true;
                        showcasePending = true;
                        ApplyMenuDepthOfField();
                    }
                    _window.CursorVisible = true;
                }
                else
                {
                    _window.CursorVisible = true;
                    _window.CursorLocked = false;
                }
                _window.Input.Captured = true;
            }
            else if (!menuMode && !console.IsOpen && _player is not null
                && (TacticalComputer is not null || _mapComputer is { IsReady: true }))
            {
                // -computercam presses C on a named frame, so the transition can be captured from a
                // moment somebody can ask for again. Latched, not held: it must open the computer once
                // and then leave the key alone, or every frame past it would toggle.
                bool automaticOpen = _options.ComputerCamFrame > 0
                    && !_computerCamFired
                    && _loop.Clock.FrameCount >= _options.ComputerCamFrame;
                if (automaticOpen)
                {
                    _computerCamFired = true;
                }

                _player.InputProfile = _configuration.Active;
                bool toggleComputer = _player.WasActionPressed(_window.Input, PlayerAction.MapComputer) || automaticOpen;
                bool closeComputer = _computerOpen && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape);

                if (TacticalComputer is { } tacticalComputer)
                {
                    // Real elapsed time, on its own clock. The transition is presentation: it must take
                    // the same wall-clock second whatever the frame rate, and it must keep running while
                    // the simulation is paused behind it.
                    int computerNow = _loop.TimeSource.Milliseconds;
                    float computerElapsed = Math.Clamp((computerNow - lastComputerMilliseconds) / 1000f, 0f, 0.1f);
                    lastComputerMilliseconds = computerNow;

                    CameraPose eye = CameraPose.FromCamera(_player.Camera);

                    // The field of view gameplay is being drawn at, which is where the climb leaves from.
                    // Taken at the default rather than from the live sight: a player who opens the map
                    // down a scope is the one case it differs, and inheriting a scoped field of view
                    // there would start the shot already halfway zoomed in.
                    float gameplayFieldOfView = VerticalFieldOfView(
                        (float)_window.Width / Math.Max(1, _window.Height),
                        HumanView.DefaultTanX);

                    if (!_computerOpen && toggleComputer)
                    {
                        _computerOpen = true;
                        tacticalComputer.Open(_player.Body.Position);
                        _player.SetMapComputerOpen(true);
                        _computerFlight.BeginOpen(
                            eye,
                            gameplayFieldOfView,
                            tacticalComputer.Vantage(),
                            tacticalComputer.FieldOfView);

                        // The interface draws its own pointer, so the platform's would be a second,
                        // unrelated cursor over it.
                        _window.CursorVisible = false;
                        _window.CursorLocked = false;
                    }
                    else if (_computerOpen && (toggleComputer || closeComputer) && _computerFlight.CanClose)
                    {
                        _computerFlight.BeginClose(
                            tacticalComputer.Vantage(),
                            tacticalComputer.FieldOfView,
                            eye,
                            gameplayFieldOfView);
                    }

                    if (_computerOpen)
                    {
                        _computerFlight.Update(
                            computerElapsed,
                            eye,
                            tacticalComputer.Vantage(),
                            tacticalComputer.FieldOfView);

                        // Only once the display has settled. A drag or a page change that landed during
                        // the climb would move a map the player cannot read yet.
                        if (_computerFlight.IsInteractive)
                        {
                            tacticalComputer.Update(
                                _window.Input,
                                computerElapsed,
                                _player.Body.Position,
                                _window.Width,
                                _window.Height);
                        }

                        // The computer keeps the view — and the player's input — until the fall has
                        // finished landing, so the last frame of the shot is the first frame of play.
                        if (_computerFlight.Phase == MapComputerPhase.Done)
                        {
                            _computerOpen = false;
                            _computerFlight.Reset();
                            _player.SetMapComputerOpen(false);
                        }

                        _window.Input.Captured = true;
                    }
                }
                else if (_mapComputer is { IsReady: true } retailComputer)
                {
                    if (toggleComputer || closeComputer)
                    {
                        _computerOpen = toggleComputer ? !_computerOpen : false;
                        if (_computerOpen)
                        {
                            retailComputer.Open(_player.Body.Position);
                        }
                        _player.SetMapComputerOpen(_computerOpen);
                        // Retail draws COMPUTER:pointer.spr. Leaving the platform cursor visible put a
                        // second, unrelated pointer over it.
                        _window.CursorVisible = false;
                        _window.CursorLocked = false;
                    }

                    if (_computerOpen)
                    {
                        retailComputer.Update(_window.Input, _window.Width, _window.Height);
                        _window.Input.Captured = true;
                    }
                }
            }

            // The showcase's first backdrop, asked for a couple of menu frames in so the shell is already on
            // screen. It is built on a worker like every other one; until it arrives the menu sits over its
            // dark backdrop rather than over a blank blue clear.
            if (menuMode && !pauseMenu && showcasePending && _loop.Clock.FrameCount >= 2)
            {
                showcasePending = false;

                // -entrycam's mission stands in for the default showcase, so the backdrop it presses Play
                // over is built through exactly the path a player's selection would have built it through.
                int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
                currentShowcaseMissionId = firstShowcase;
                previewLoader?.Request(firstShowcase);
            }

            // The backdrop is made playable here, on the frames where nothing is happening but a menu.
            // A couple of milliseconds is invisible at the uncapped rate the menu runs at, and the work
            // is finished long before anybody has finished reading a briefing.
            if (menuMode && !pauseMenu && _arming is { IsArmed: false })
            {
                _arming.Step(IdleArmingBudgetMilliseconds);
            }

            // -entrycam presses Play the moment the backdrop it was pointed at is armed, which is the
            // state a player's press would find it in after a second of reading. Waiting for the arming
            // rather than only for the backdrop is what keeps the measurement honest.
            if (entryCamPending && menuMode && !pauseMenu && _backdrop is { } entryBackdrop
                && entryBackdrop.MissionId == _options.EntryCamMission
                && _arming is { IsArmed: true })
            {
                entryCamPending = false;
                _logger.Information("Menu", $"-entrycam is pressing Play on mission {entryBackdrop.MissionId}.");
                shellHostForEntryCam?.StartMission(
                    entryBackdrop.MissionId,
                    _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal);
            }

            // The build request, once the campaign selection has held still for a moment. The debounce is
            // only there to stop a run down the list queueing fourteen builds; it costs the render thread
            // nothing either way, because the build happens on a worker.
            if (menuMode
                && !pauseMenu
                && pendingPreviewMissionId is { } previewMissionId
                && _loop.TimeSource.Milliseconds - previewRequestedAtMs > PreviewSettleMs)
            {
                pendingPreviewMissionId = null;
                previewLoader?.Request(previewMissionId);
            }

            // The handover. Everything a backdrop is made of was built off the render thread; what is left
            // is the part that cannot be — a GL texture belongs to the context that made it — so the
            // renderers are constructed here, on the frame after the outgoing picture was held. Same shape
            // as the probe and distance-field bakes above.
            if (menuMode && !pauseMenu && heldFrameReady)
            {
                bool changed = false;
                if (previewLoader is { } loader
                    && loader.TryCollect(out MissionPreview? readyPreview) && readyPreview is not null)
                {
                    currentShowcaseMissionId = readyPreview.MissionId;
                    AdoptPreview(readyPreview);
                    changed = true;
                }
                else if (_showcaseCamera is { IsCutDue: true } showcase)
                {
                    // No new level, just the next place in this one. The camera does not take the cut
                    // itself, so that it happens here — after the outgoing frame has been held.
                    showcase.CommitCut();
                    changed = true;
                }

                heldFrameReady = false;

                // The held frame is now the whole picture; it dissolves off to reveal what is behind it.
                if (changed)
                {
                    dissolve.SnapTo(heldFrameIsPicture ? 1f : 0f);
                    if (heldFrameIsPicture)
                    {
                        dissolve.Retarget(0f);
                    }
                }
            }

            // The camera is advanced on every pass of the loop, not only on the passes that render. The
            // pump clears the mouse's motion accumulator, and the loop pumps several times per rendered
            // frame — so consuming it only on a render threw most of the mouse movement away and made the
            // view lurch. It also runs on real elapsed time: a fixed step tied flying speed to the frame
            // rate, which is not what a person moving a mouse expects.
            // F12 toggles the debug overlay: the player's position and angle, and a crosshair readout of
            // whatever is under it — an object (with its task), a soldier, or nothing.
            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F12))
            {
                showDebug = !showDebug;
            }

            // F1 swaps between flying and walking. The player takes the camera's position when it is
            // switched on, so the swap happens where you were looking.
            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && _player is not null && _camera is not null)
            {
                _playerMode = !_playerMode;
                if (_playerMode)
                {
                    if (!_player.Place(_playerTerrain!, _camera.Position, _camera.Yaw))
                    {
                        _playerMode = false;
                        _logger.Warning("Player", "Nothing to stand on here; staying with the free camera.");
                    }
                    else
                    {
                        // Place() drops the player's presentation pair because this is a teleport.
                    }
                }
                else
                {
                    _camera.MoveTo(_player.Camera.Position, _player.Body.Yaw, _player.Body.Pitch);
                }

                _logger.Information("Player", _playerMode ? "Walking." : "Flying.");
            }

            if (_camera is not null)
            {
                int nowMilliseconds = _loop.TimeSource.Milliseconds;
                float elapsed = Math.Clamp((nowMilliseconds - lastCameraMilliseconds) / 1000f, 0f, 0.1f);
                lastCameraMilliseconds = nowMilliseconds;

                float rawX = _window.Input.Mouse.DeltaX;
                float rawY = _window.Input.Mouse.DeltaY;
                bool right = _window.Input.Mouse.IsDown(Input.MouseButton.Right);

                // Resolved per frame rather than cached, so the options menu's sensitivity slider takes
                // effect on the next frame instead of the next launch. It is two multiplies and a Pow.
                MouseLookSettings look = MouseLookSettings.FromProfile(_configuration.Active);
                if (_player is not null)
                {
                    _player.LookSettings = look;
                }

                _deathCamera.LookSettings = look;
                interactionCamera.LookSettings = look;

                if (pauseMenu)
                {
                    // A paused view does not turn, and the cursor has to be free to reach the menu. The
                    // release is repeated every pass rather than done once on the way in, so nothing can
                    // quietly take the pointer back while the menu is up; it also re-arms the look's
                    // settling frame, so the warp that re-capturing the cursor produces on resume is not
                    // read as a movement the player made.
                    _player?.HoldStill();
                    _window.CursorLocked = false;
                }
                else if (_playerDead && _deathCamera.IsActive)
                {
                    // Dead: the mouse orbits the death camera around the body rather than turning a head that
                    // is no longer taking input -- HumanCamera_DeathCameraControlInput. The cursor stays locked
                    // so the orbit keeps turning however far the mouse is moved.
                    _deathCamera.Control(rawX, rawY);
                    _window.CursorLocked = !captureMode;

                    // The look is not driving the view this frame, so it must not consider itself
                    // capturing: whenever the orbit hands control back, the first frame of look is a
                    // settling one and the re-lock's re-centring is absorbed rather than aimed with.
                    _player?.ReleaseLook();
                }
                else if (_player is { IsOnLadder: true } or { IsInWorldInteraction: true })
                {
                    // On a ladder the mouse orbits the third-person interaction camera around the climbing
                    // body, as the original's mode-3 camera does -- HumanCamera_ThirdPersonControlInput -- so
                    // you can look around while you climb rather than being locked behind the character.
                    interactionCamera.Control(rawX, rawY);
                    _window.CursorLocked = !captureMode;
                    _player?.ReleaseLook();
                }
                else if (_computerOpen)
                {
                    // The computer takes the pointer outright. Re-arming the look here is what stops
                    // closing it from throwing the view: dropping the lock restores the desktop pointer,
                    // and taking it back re-centres the backend's virtual one.
                    _window.CursorLocked = false;
                    _player?.ReleaseLook();
                }
                else if (_cutscenes?.IsPlaying == true)
                {
                    _player?.HoldStill();
                    _window.CursorLocked = false;
                }
                else if (_playerMode && _player is not null)
                {
                    // Looking is per frame so the view is as smooth as the display allows; the body itself
                    // moves on the fixed tick below, so it steps identically whatever the frame rate.
                    _player.Look(_window.Input, elapsed);
                    _window.CursorLocked = !captureMode && _player.IsCapturingMouse;
                }
                else
                {
                    _camera.Update(_window.Input, elapsed);

                    // The cursor is locked away while a mouse button is held, the way a scene editor's is,
                    // so the view keeps turning however far the mouse is dragged. It comes back on release.
                    _window.CursorLocked = !captureMode && _camera.IsCapturingMouse;
                }

                if (_options.LogCamera && (right || rawX != 0f || rawY != 0f))
                {
                    _logger.Information(
                        "Camera",
                        $"rmb={(right ? 1 : 0)} raw=({rawX:0.##},{rawY:0.##}) pos=({_camera.Position.X:0.##},"
                            + $"{_camera.Position.Y:0.##},{_camera.Position.Z:0.##}) fwd={_camera.Camera.Orientation.Row3} "
                            + $"right={_camera.Camera.Orientation.Row1} dt={elapsed:0.####}");
                }
            }

            // The original's schedule admits ONE render per tick, which caps the whole presentation at
            // 30 Hz however fast the machine is. Smooth mode lifts exactly that latch and nothing else:
            // tick deadlines, the catch-up budget and everything the simulation observes are untouched.
            // Lockstep keeps the original pairing -- its synthetic clock advances per frame and the
            // reference captures depend on the one-to-one schedule.
            // The cinematic shell always renders uncapped so the menu is smooth whatever the render preset's
            // r_smooth is; the orbit and the tweens run on real time and want every frame the display offers.
            // In game the original's one-render-per-tick latch is lifted only by smooth mode.
            _loop.Clock.UncappedRenders = _options.IsBenchmark
                || menuMode
                || (!_options.Lockstep && RenderCVars.SmoothPresentation.Value);

            // The level's own half of the pause, and the reason the frozen picture behind the menu really is
            // frozen: with this set the level task forwards nothing to its subtree, so the scripts, the
            // doors and the level clock all stand still. The scheduler is left alone -- ticks keep arriving
            // and finding nothing to do -- which is what stops a menu held open for a minute from being
            // followed by a minute's worth of catch-up.
            if (_activeLevelRoot is { } pausableLevel)
            {
                pausableLevel.IsPaused = pauseMenu;
            }

            BeginStage();
            FrameOutcome outcome = _loop.RunFrame();
            EndStageIfSlow("the loop's own frame (present, ticks, task tree)");

            // Retire finished voices, refill streaming buffers and advance any crossfade. This sits
            // ABOVE the pause check on purpose: a paused game still has to feed the music, and a stream
            // that goes unfed for a few frames does not pause, it runs dry and stops.
            AdvanceAudio();

            // The retail in-game menu pauses the game task. Once LevelFlow failure has opened that menu --
            // or the player has opened the pause menu -- do not advance the desktop-owned player/AI
            // simulation until they choose an action.
            if (outcome == FrameOutcome.Tick && (_missionFailedMenu || pauseMenu))
            {
                continue;
            }

            if (outcome == FrameOutcome.Tick)
            {
                _cutscenes?.Tick();
                if (_cutscenes is { } liveCutscenes && _scene is { } cinematicScene && _worldSolids is { } cinematicSolids)
                {
                    foreach (WorldPlacement placement in cinematicScene.Placements)
                    {
                        if (liveCutscenes.TryGetPlacementDrawTransform(
                            placement.Owner,
                            placement.Position,
                            placement.Orientation,
                            out Vector3d position,
                            out Matrix3f orientation))
                        {
                            cinematicSolids.UpdateTransform(placement, position, orientation);
                        }
                    }
                }
            }

            // The body advances on the simulation tick and ONLY on it. Tick and Render are mutually
            // exclusive outcomes - a render is not a tick - so including Render here stepped the player
            // once per drawn frame as well, and at an uncapped frame rate that is several times 30 Hz.
            // The player then moved as fast as the display was quick, which is exactly what it looked like.
            if (outcome is FrameOutcome.Tick
                && _cutscenes?.IsPlaying != true
                && _playerMode && _player is not null && _playerTerrain is not null)
            {
                // A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the
                // body stops being stepped rather than being stepped with no input. Without this a corpse
                // walks around.
                if (!_playerDead && !_missionFailedMenu)
                {
                    _player.InputProfile = _configuration.Active;
                    if (_computerOpen)
                    {
                        _player.TickMapComputer();
                    }
                    else
                    {
                        _player.Tick(_window.Input, _playerTerrain);
                    }

                    // Hearing is emitted once per round, not once per projectile intersection. The weapon
                    // row carries the exact retail radius (for example 4096 for suppressed fire and
                    // 122880 for an ordinary rifle), so shotguns do not post duplicate noise events and
                    // silenced weapons no longer wake the whole 30-metre neighbourhood.
                    if (_player.FiredThisTick && _player.HeldWeaponType is { } audibleWeapon)
                    {
                        _soldiers?.NotifyGunshot(_player.Body.Eye, audibleWeapon.AiDetectionRange);
                    }

                    // And the sound of it. Driven every tick rather than only on the ones that fire,
                    // because an automatic's loop has to be told when the trigger was RELEASED as much
                    // as when it was pulled -- see WeaponSoundPlayer.
                    _weaponSounds?.Tick(_player.FiredThisTick, _player.HeldWeaponType);

                    // Reloads and a few other weapon actions name their sounds through BOED animation
                    // events rather than through the weapon record. The rig advanced inside Player.Tick,
                    // so its crossed-event interval is ready to dispatch here.
                    PlayPlayerAnimationSounds();

                    // And the player's own feet. Positional like anyone else's, and placed where the
                    // foot landed rather than on the listener, because the sound belongs to the ground.
                    if (_player.TakeFootstep() is { } step)
                    {
                        PlayFootstep(step);

                        // The step is also a NOISE the AI hears: 0x463920's tail posts AIEVENT_WALK
                        // (type 10, filter 0x46) with the ground material's own radius, unless the
                        // walker is crouching -- sneaking really is silent to the AI. guard.qvm's WALK
                        // branch is AIAction_SetCombat, so a guard that hears footsteps it cannot see
                        // starts hunting.
                        if (!_player.IsCrouched)
                        {
                            _soldiers?.NotifyWalk(
                                step.PositionInUnits, _player.Body.Position, step.HearingRadiusInUnits);
                        }
                    }

                    // What this tick's ballistic rounds landed on -- dirt, steel or flesh, out of the
                    // material table, where the round arrived rather than where it was fired.
                    PlayBallisticImpactSounds();

                    // The same impacts, seen: sparks and dust composed from the struck material.
                    // Ticked before spawning so a new burst draws its birth frame unmoved.
                    _impactParticles?.Tick();
                    SpawnBallisticImpactParticles();

                    // Whatever grenades ran out of fuse this tick -- the blast, the flash, the noise
                    // and the casualties.
                    ResolveGrenadeDetonations();
                    _flashbangStrength = Math.Max(0f, _flashbangStrength - FlashbangDecayPerTick);

                    // The alarm speakers, following their own level expressions -- the siren the
                    // room hears when a guard reaches a switch.
                    TickAlarmSpeakers();
                    TickSecurityCameraSounds();

                    // And whether one of this tick's rounds landed ON a speaker. The impact list
                    // carries both the instant rays and the ballistic arrivals, so either mode can
                    // shoot the siren off the wall.
                    foreach (GunImpact playerImpact in _player.Impacts)
                    {
                        if (playerImpact.HitSomething)
                        {
                            DamageAlarmSpeakers(playerImpact.Position, owner: playerImpact.Owner);
                        }
                    }

                    // The level's ambience, which is keyed off where the ear is rather than where the
                    // body stands: an area is a box you listen from the inside of.
                    TickAmbience(_player.EyeUnits);

                }

                // The first round of a mission, reported once — the same one-shot diagnostic the world
                // draw uses. It is what says the whole chain arrived: the trigger reached the state
                // machine, the machine reached the gun, the gun's clock let a round out, and the round
                // went somewhere.
                if (!reportedShot && _player.FiredThisTick && _player.HeldWeaponType is { } fired)
                {
                    reportedShot = true;
                    string where = _player.Impacts.Count == 0
                        ? "nowhere"
                        : _player.Impacts[0].HitSomething
                            ? $"{_player.Impacts[0].Position}"
                            : $"nothing inside {fired.EffectiveRange} m";

                    _logger.Information(
                        "Weapon",
                        $"Fired {fired.ScriptId} — {GunTiming.TicksPerRound(fired.RoundsPerMinute)} tick(s) "
                            + $"a round at {fired.RoundsPerMinute} rpm, "
                            + $"{_player.Weapons?.InHand?.Gun.RoundsInClip} left in the magazine, into {where}.");
                }

                // Every round that lands on a guard, reported once each so the zone and the kill are
                // visible without a per-shot log.
                if (_player.HumanHits != reportedHumanHits)
                {
                    reportedHumanHits = _player.HumanHits;
                    _logger.Information(
                        "Weapon",
                        $"Hit a guard in zone {_player.LastHumanZone} "
                            + $"({reportedHumanHits} hit(s), {_soldiers?.Killed ?? 0} killed).");
                }
            }

            // The soldiers' rigs advance on the tick, not on the render, so their animation runs at the
            // engine's fixed 30 Hz whatever the frame rate is - the determinism contract's whole point.
            //
            // Tick and Render are SEPARATE outcomes and one logic tick produces one of each, so accepting both
            // here ran the AI and its rigs TWICE per tick: guards walked and animated at exactly double speed.
            // Measured against the original, which covers 160-185 units a tick, this port was covering ~357.
            if (outcome is FrameOutcome.Tick)
            {
                // The damage wash fades a forty-fifth a tick whatever the frame rate -- decayed here, on the
                // fixed tick, for the same determinism reason the bodies are.
                _damageFlash.Tick();
                _hud?.Tick();

                // The player's eye is what the guards' view cones are tested against -- and in the
                // fly-through it is the CAMERA's, not the body's. The body stops being stepped the moment
                // you leave walking mode, so feeding its eye meant every guard in the level was watching an
                // immobile figure at the spawn point while you flew past them: they never saw you because
                // there was nothing of you to see. A shell affordance, not engine behaviour.
                Vector3d watched = _playerMode && _player is not null
                    ? _player.Body.Eye
                    : _camera?.Position ?? _player?.Body.Eye ?? default;

                // A dead player is no longer a target: the guards lose it and stop firing at the corpse, the
                // way the retail detection scan (0x489750) unregisters a human that has died.
                //
                // The eye is what vision watches; the FEET are what combat aims at (sub_451380 raises the
                // target task's position 0.5 m, 0.25 m crouched) -- in the fly-through there is no body, so
                // the camera stands in for both.
                Vector3d watchedFeet = _playerMode && _player is not null
                    ? _player.Body.Position
                    : watched;

                // Retail's EditVariable tasks run before the AlarmControls that consume them. Mission 1's
                // alarm latch depends on that order: reversing it sets and clears the latch during the
                // same multi-tick switch pulse, cutting the siren off almost as soon as it starts.
                _missionObjectives?.TickLevelState(_player?.Body.Position);
                _cutscenes?.FinishExpressionTick();
                if (_securityCameras is { } cameras)
                {
                    bool crouching = _playerMode && _player is not null && _player.IsCrouched;
                    cameras.Tick(watchedFeet, crouching, targetAlive: !_playerDead);
                    if (_worldSolids is { } cameraSolids)
                    {
                        foreach (SecurityCamera camera in cameras.Cameras)
                        {
                            Vector3d headMetres = new(
                                camera.HeadPositionUnits.X / WorldScene.UnitsPerMetre,
                                camera.HeadPositionUnits.Y / WorldScene.UnitsPerMetre,
                                camera.HeadPositionUnits.Z / WorldScene.UnitsPerMetre);
                            cameraSolids.UpdateTransform(
                                camera.Task,
                                camera.HeadModelName,
                                headMetres,
                                camera.Orientation);
                        }
                    }
                }

                _soldiers?.Tick(
                    watched,
                    enemyAlive: !_playerDead && _cutscenes?.IsPlaying != true,
                    enemyFeet: watchedFeet,
                    enemyCrouching: _playerMode && _player is not null && _player.IsCrouched);

                // The player's own body advances its death fall on the tick, the same as a soldier's corpse --
                // it does nothing until the player has died and the body was posed.
                _soldiers?.TickPlayerBody();

                // A simulated corpse decides for itself where it ends up, so the death camera has to
                // follow it. Retail needs no such thing: its fall is authored, and aiming at the spot
                // you were shot frames the whole of it.
                if (_playerDead && _soldiers?.PlayerRagdollCentre is { } corpseCentre)
                {
                    _deathCamera.Follow(corpseCentre);
                }

                // The complete IGI1 ladder graph: 37 pauses animation 168 at authored event 8, 38/39 play
                // animation 170 backwards/forwards at the top, and 40 loops 169 while its damped velocity
                // falls to a ground collision. Every advance happens here, on the fixed 30 Hz tick.
                if (_player is not null && _player.IsOnLadder)
                {
                    _player.InputProfile = _configuration.Active;
                    bool climbUp = _player.IsActionDown(_window.Input, PlayerAction.MoveUp);
                    bool climbDown = _player.IsActionDown(_window.Input, PlayerAction.MoveDown);
                    bool climbUse = _player.IsActionDown(_window.Input, PlayerAction.Activate);

                    // Mount and use share E. The release gate is a shell detail that prevents the mounting press
                    // from becoming state 40 on the immediately following tick.
                    if (!climbActive)
                    {
                        climbActive = true;
                        climbSlideArmed = false;
                        if (_player.LadderPhase == LadderTraversalPhase.GettingOnTop)
                        {
                            _soldiers?.StartPlayerLadderAnimation(
                                PlayerCamera.LadderTopAnimation, loops: false, backwards: true);
                        }
                        else
                        {
                            _soldiers?.StartPlayerLadderAnimation(
                                PlayerCamera.LadderClimbAnimation, loops: true, paused: true);
                        }
                    }

                    if (!climbUse)
                    {
                        climbSlideArmed = true;
                    }

                    bool climbSlide = climbUse && climbSlideArmed;
                    LadderStepResult decision = _player.DecideLadderStep(
                        climbUp, climbDown, climbSlide, _playerTerrain!);
                    switch (decision)
                    {
                        case LadderStepResult.SteppingUp:
                        case LadderStepResult.SteppingDown:
                            _soldiers?.SetPlayerLadderPlaybackDirection(_player.LadderDirection);
                            break;

                        case LadderStepResult.ReachedTop:
                            _soldiers?.StartPlayerLadderAnimation(
                                PlayerCamera.LadderTopAnimation, loops: false);
                            break;

                        case LadderStepResult.Sliding:
                            _soldiers?.StartPlayerLadderAnimation(
                                PlayerCamera.LadderSlideAnimation, loops: true);
                            break;
                    }

                    if (_player.IsOnLadder && _soldiers is not null)
                    {
                        switch (_player.LadderPhase)
                        {
                            case LadderTraversalPhase.Climbing when _player.LadderDirection != 0:
                                _soldiers.AdvancePlayerLadderAnimation(
                                    _player.LadderFacing,
                                    out Vector3d climbDelta,
                                    out bool rungBoundary,
                                    out _);
                                _player.MoveLadder(climbDelta);
                                if (rungBoundary)
                                {
                                    _player.CompleteLadderStep();
                                    _soldiers.SetPlayerLadderPlaybackDirection(0);
                                }

                                break;

                            case LadderTraversalPhase.GettingOnTop:
                            case LadderTraversalPhase.GettingOffTop:
                                _soldiers.AdvancePlayerLadderAnimation(
                                    _player.LadderFacing,
                                    out Vector3d topDelta,
                                    out _,
                                    out bool topAnimationEnded);
                                _player.MoveLadder(topDelta);
                                if (topAnimationEnded)
                                {
                                    _player.CompleteLadderTopTransition(_playerTerrain!);
                                    if (_player.LadderPhase == LadderTraversalPhase.Climbing)
                                    {
                                        _soldiers.StartPlayerLadderAnimation(
                                            PlayerCamera.LadderClimbAnimation, loops: true, paused: true);
                                    }
                                }

                                break;

                            case LadderTraversalPhase.SlidingDown:
                                _soldiers.AdvancePlayerLadderAnimation(
                                    _player.LadderFacing,
                                    out Vector3d slideDelta,
                                    out _,
                                    out _);
                                // State 40's entry only installs animation 169 and the sound. Its damped
                                // velocity function first runs on the following tick.
                                if (decision != LadderStepResult.Sliding)
                                {
                                    _player.TickLadderSlide(slideDelta, _playerTerrain!);
                                }

                                break;
                        }
                    }

                    // All four states stay in third person until their actual deinitialization signal.
                    if (_player.IsOnLadder)
                    {
                        _soldiers?.PlacePlayerBodyClimbing(_player.LadderFeet, _player.LadderFacing);
                        if (!interactionCamera.IsActive)
                        {
                            WorldRayCast? cameraCast = _worldSolids?.Caster();
                            interactionCamera.Begin(
                                _player.LadderFeet,
                                _player.LadderFacing,
                                cameraCast is null
                                    ? null
                                    : (Vector3d from, Vector3d to, out Vector3d hit) =>
                                    {
                                        bool blocked = cameraCast(from, to, out WorldRayHit wall);
                                        hit = blocked ? wall.Position : default;
                                        return blocked;
                                    });
                        }
                        else
                        {
                            interactionCamera.Follow(_player.LadderFeet);
                        }

                        interactionCamera.Tick();
                    }
                }

                if (_player is { IsInWorldInteraction: true } interacting)
                {
                    if (!worldInteractionActive)
                    {
                        worldInteractionActive = true;
                        if (interacting.InteractionAnimation >= 0)
                        {
                            _soldiers?.StartPlayerInteractionAnimation(
                                interacting.InteractionAnimation,
                                loops: interacting.InteractionAnimationLoops);
                        }
                    }

                    if (_soldiers is not null)
                    {
                        _soldiers.AdvancePlayerLadderAnimation(
                            interacting.InteractionFacing,
                            out Vector3d interactionRootDelta,
                            out _,
                            out _);
                        interacting.ApplyInteractionRootMotion(interactionRootDelta);
                        _soldiers.PlacePlayerBodyClimbing(
                            interacting.InteractionFeet, interacting.InteractionFacing);
                    }

                    Vector3d interactionCameraFeet = _soldiers?.PlayerInteractionCameraFeet(
                        interacting.InteractionFeet) ?? interacting.InteractionFeet;

                    if (!interactionCamera.IsActive)
                    {
                        WorldRayCast? cameraCast = _worldSolids?.Caster();
                        interactionCamera.Begin(
                            interactionCameraFeet,
                            interacting.InteractionFacing,
                            cameraCast is null
                                ? null
                                : (Vector3d from, Vector3d to, out Vector3d hit) =>
                                {
                                    bool blocked = cameraCast(from, to, out WorldRayHit wall);
                                    hit = blocked ? wall.Position : default;
                                    return blocked;
                                });
                    }
                    else
                    {
                        interactionCamera.Follow(interactionCameraFeet);
                    }
                    interactionCamera.Tick();
                }

                if ((_player is null || (!_player.IsOnLadder && !_player.IsInWorldInteraction))
                    && (climbActive || worldInteractionActive || interactionCamera.IsActive))
                {
                    _soldiers?.HidePlayerBodyClimbing();
                    interactionCamera.Reset();
                    climbActive = false;
                    worldInteractionActive = false;
                }

                if (_missionObjectives is { } objectiveTick)
                {
                    // The remaster holds a message three seconds longer than the original does, because
                    // the authored durations are short and the hold only starts once the last character
                    // has been typed. The retail HUD always retains the original timing and message block,
                    // irrespective of the independently archived tactical-message preference.
                    objectiveTick.ExtraMessageHoldSeconds =
                        RemasteredHudPresentation.UsesTacticalMessages(
                            RenderCVars.RemasteredHud.Value,
                            RenderCVars.TacticalMessages.Value)
                        && _cutscenes?.IsPlaying != true
                            ? RemasteredMessageHoldSeconds
                            : 0f;
                }

                _missionObjectives?.TickAfterLevelState();
                if (_missionObjectives is { IsFailed: true })
                {
                    // MainMenu state 9 polls LevelFlow_LevelFailed and opens the in-game menu after
                    // the counter passes 150 (0x4181E5..0x41821A). Keep that exact grace period.
                    if (!_missionFailedMenu && ++_missionFailureTicks > 151)
                    {
                        _missionFailedMenu = true;
                        _computerOpen = false;
                        _computerFlight.Reset();
                        _player?.SetMapComputerOpen(false);
                        _window.CursorVisible = true;
                        _window.CursorLocked = false;
                    }
                }
                else
                {
                    _missionFailureTicks = 0;
                }
                if (!_missionResultHandled
                    && _missionObjectives is { IsComplete: true }
                    && _activeMission is { } completedMission)
                {
                    _missionResultHandled = true;
                    int nextMissionId = completedMission.NextMissionId;
                    if (nextMissionId != MissionDescriptor.NextMissionUndefined)
                    {
                        if (_configuration.Active is { } activeProfile)
                        {
                            MissionProgression.UnlockNext(activeProfile, _missions, nextMissionId);
                            // Retail writes LOCAL:config.qsc after every non-final completion, even when
                            // replaying an earlier mission leaves the frontier unchanged.
                            DesktopConfiguration.Save(_configuration, _logger);
                        }

                        _logger.Information("Mission", $"Completed mission {completedMission.Id}; loading {nextMissionId}.");
                        StartMission(nextMissionId);
                    }
                    else
                    {
                        _logger.Information("Mission", $"Completed final mission {completedMission.Id}; campaign complete.");
                        ReleasePlayedMission();
                        if (shell is not null)
                        {
                            // Event 7 is the retail outro-screen path. The authored final cutscene has
                            // completed before LevelFlow reaches here, so the shell now continues to its
                            // campaign-complete destination.
                            shell.OpenOn("extras");
                            menuMode = true;
                            showcasePending = true;
                        }
                    }
                }
            }

            if (outcome != FrameOutcome.Render)
            {
                continue;
            }

            if (_options.IsBenchmark && _loop.Clock.FrameCount == BenchmarkSchedule.WarmUpFrames)
            {
                framePerformance.Reset();
            }
            if (!_options.IsBenchmark || _loop.Clock.FrameCount <= BenchmarkSchedule.MeasurementEndFrame)
            {
                framePerformance.NoteFrame();
            }

            if (_scene is not null && _world is not null && _camera is not null)
            {
                // The scripted flight, driven off the rendered-frame index so the motion is exactly
                // reproducible under lockstep. The opening pose is captured once; each subsequent frame
                // moves the camera a straight fraction of the way to the end pose. An unset end component
                // holds the opening value, so -camend alone dollies without turning and -yawend alone pans
                // in place. Only in the free camera -- a flight forces -freecam, so _playerMode is false.
                if (_options.HasFlight && !_playerMode)
                {
                    if (!flightStarted)
                    {
                        flightStartPos = _camera.Position;
                        flightStartYaw = _camera.Yaw;
                        flightStartPitch = _camera.Pitch;
                        flightStarted = true;
                    }

                    int span = Math.Max(
                        1,
                        (_options.IsBenchmark ? BenchmarkSchedule.MeasurementEndFrame : _options.ExitAfterFrames) - 1);
                    float t = Math.Clamp((float)flightFrame / span, 0f, 1f);

                    Vector3d endPos = _options.CameraEnd is { } e
                        ? new Vector3d(e.X, e.Y, e.Z)
                        : flightStartPos;
                    float endYaw = _options.CameraYawEnd ?? flightStartYaw;
                    float endPitch = _options.CameraPitchEnd ?? flightStartPitch;

                    _camera.MoveTo(
                        new Vector3d(
                            flightStartPos.X + ((endPos.X - flightStartPos.X) * t),
                            flightStartPos.Y + ((endPos.Y - flightStartPos.Y) * t),
                            flightStartPos.Z + ((endPos.Z - flightStartPos.Z) * t)),
                        flightStartYaw + ((endYaw - flightStartYaw) * t),
                        flightStartPitch + ((endPitch - flightStartPitch) * t));
                }

                // How far this frame sits between the last two simulation ticks. Compute it before
                // either projection: MP5 sight zoom changes the world and view-model FOV on those ticks,
                // and both need the same presentation blend to remain welded together at uncapped rates.
                //
                // Off entirely while the pause menu is up, and this is what makes a paused frame actually
                // still. Nothing is stepping, so the last two ticks stay where they are — while the blend
                // between them keeps sweeping 0 to 1 with real time, which rocked the eye, the arms and
                // every guard back and forth between two poses a thirtieth of a second apart. At blend 1
                // every one of those readings is simply the tick that was last run.
                // Cutscene camera and vehicle transforms are authored and committed on the 30 Hz task
                // tick. Blending only the SoldierRenderer between its previous/current records while the
                // camera and its parent vehicle stay on the current record makes a seated actor sweep
                // backwards after every tick, then glide forwards again. Retail presents the cinematic
                // transform set atomically, so keep the actor on that same current tick as its plane/train.
                bool smoothPresentation = !pauseMenu
                    && _cutscenes?.IsPlaying != true
                    && !_options.Lockstep
                    && RenderCVars.SmoothPresentation.Value;
                float tickBlend = smoothPresentation
                    ? _loop.Clock.TickFraction(_loop.TimeSource.PreciseMilliseconds)
                    : 1f;
                SightZoomFrame sight = _playerMode && _player is not null
                    ? _player.SmoothedSightZoom(tickBlend)
                    : new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

                float aspectRatio = (float)_window.Width / Math.Max(1, _window.Height);
                float horizontalTangent = sight.TanX;
                float verticalFieldOfView = VerticalFieldOfView(aspectRatio, horizontalTangent);
                if (_cutscenes?.Frame is { FieldOfView: > 0f } cutsceneLens)
                {
                    // EditCamera.FOV is the horizontal half-angle tangent submitted at 0x4F604A,
                    // not an angle in radians. A value of 1 is retail's 90-degree horizontal lens.
                    // The authored framing was composed through retail's fixed 4:3 viewport. Keep that
                    // vertical aperture at modern aspects and let the wider projection reveal the sides;
                    // deriving it from a 16:9 window crops the top and bottom by 25 percent, which cut
                    // Anya's upper body out of the Mission 1 briefing.
                    verticalFieldOfView = CutsceneVerticalFieldOfView(cutsceneLens.FieldOfView);
                }
                // The map's own view of the level, when the computer owns the frame. Under the tactical
                // interface the transition owns it for as long as it is flying — same viewport, but its
                // camera and its field of view, which is what makes the climb and the fall a move of the
                // player's own eye rather than a cut to a second one.
                MapSceneView? mapScene = null;
                if (_computerOpen && _player is { } mapPlayer)
                {
                    if (TacticalComputer is { } tacticalView)
                    {
                        MapSceneView steady = tacticalView.SceneView(
                            mapPlayer.Body.Position, _window.Width, _window.Height);
                        mapScene = _computerFlight.IsFlying
                            ? steady with
                            {
                                Camera = _computerFlight.Camera,
                                FieldOfView = _computerFlight.FieldOfView,
                            }
                            : steady;
                    }
                    else if (_mapComputer is { IsReady: true, ShowsMap: true } computerView)
                    {
                        mapScene = computerView.SceneView(
                            mapPlayer.Body.Position, _window.Width, _window.Height);
                    }
                }

                // What the tube is doing this frame, and how hard the shot is smearing. Read here, next
                // to the camera they belong to, rather than at the resolve five hundred lines below,
                // where the transition would be a name with no visible source.
                bool tacticalUp = _computerOpen && TacticalComputer is not null;
                TacticalDisplaySettings computerDisplay = tacticalUp ? _computerFlight.Display : default;
                float computerFlightBlur = tacticalUp ? _computerFlight.MotionBlur : 0f;
                PostProcessSettings postProcess = new(
                    RenderCVars.ToneMap.Value,
                    RenderCVars.Exposure.Value,
                    RenderCVars.Bloom.Value,
                    RenderCVars.BloomThreshold.Value,
                    RenderCVars.WhitePoint.Value,
                    SceneIsDisplayEncoded: true,
                    Contrast: RenderCVars.Contrast.Value,
                    Saturation: RenderCVars.Saturation.Value,
                    Vignette: RenderCVars.Vignette.Value,
                    Grain: RenderCVars.Grain.Value,
                    ChromaticAberration: RenderCVars.ChromaticAberration.Value,
                    Sharpen: RenderCVars.Sharpen.Value,
                    FocalDistance: RenderCVars.FocalDistance.Value,
                    FocalRange: RenderCVars.FocalRange.Value,
                    FocalBlur: RenderCVars.FocalBlur.Value,
                    MotionBlur: MathF.Max(RenderCVars.MotionBlur.Value, computerFlightBlur),
                    CameraTaps: RenderCVars.CameraTaps.Value,
                    SpatialAa: RenderCVars.SpatialAa.Value,
                    SpatialAaThreshold: RenderCVars.SpatialAaThreshold.Value,
                    ColourLookup: RenderCVars.ColourLookup.Value,
                    AutoExposure: RenderCVars.AutoExposure.Value,
                    GreenPhosphor: mapScene is not null && TacticalComputer is null,
                    Tactical: computerDisplay);
                if (mapScene is { } mapView)
                {
                    aspectRatio = (float)mapView.Viewport.Width / Math.Max(1, mapView.Viewport.Height);
                    verticalFieldOfView = mapView.FieldOfView;
                }
                // The near plane is the original's default: 0x493580 initialises it to 409.6 units, which
                // is 0.1 m. (Scripts can move it through the SetNearClippingPlane native, 0x497D00.) At
                // 0.2 the plane reached twice as far as retail's and cut into any wall the sweep lets the
                // body stand 2.5 mm from.
                Matrix4f unjitteredProjection = Matrix4f.CreatePerspective(
                    verticalFieldOfView,
                    aspectRatio,
                    near: 0.1f,
                    far: FarPlane);

                // Temporal accumulation works by sampling a different point inside each pixel every frame
                // and averaging what comes back, so the jitter is not a refinement of the technique -- it
                // is the technique. Without it every frame samples the same pixel centre and the history
                // is an average of identical samples, which is a blur and nothing else.
                //
                // It is a translation in clip space, which is exactly a sub-pixel shift of the whole
                // frustum: clip.xy += offset * clip.w, applied uniformly at every depth. Moving the camera
                // instead would shift the near plane more than the far one and reproject wrongly.
                bool wantsTemporal = RenderCVars.TemporalFeedback.Value < 1f
                    && RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value;

                Matrix4f projection = unjitteredProjection;
                float jitterX = 0f;
                float jitterY = 0f;

                if (wantsTemporal)
                {
                    (jitterX, jitterY) = TemporalJitter.At(temporalFrame);

                    // In RENDER pixels, not window pixels: the offset has to be half a pixel of the buffer
                    // being rasterised into, or at a render scale below one it moves the sample by less
                    // than the pixel it is meant to be exploring and the upsample has nothing to gather.
                    projection = Matrix4f.CreateTranslation(
                        jitterX * 2f / Math.Max(1, (int)(_window.Width * RenderCVars.RenderScale.Value)),
                        jitterY * 2f / Math.Max(1, (int)(_window.Height * RenderCVars.RenderScale.Value)),
                        0f) * unjitteredProjection;
                    temporalFrame++;
                }

                // The sky is behind everything and writes no depth, so it goes first; then terrain, which
                // is opaque and covers most of the screen, so drawing it before the objects gives the
                // depth test the most to reject.
                //
                // A dead player watches from the death camera -- pulled back off the eye to a third-person
                // shot of the body -- rather than from the frozen first-person view.
                // On a ladder the eye pulls back to a third-person interaction view (camera mode 3), so you
                // see your character on the rungs rather than the back of your hands.
                // The ladder and world-interaction view is a third-person orbit whose ANGLES are already
                // this frame's own -- Control runs per frame -- but whose pivot follows the climbing body
                // and whose distance eases out, both on the 30 Hz tick. The body itself is drawn
                // interpolated between its tick pair, so leaving the camera on the tick made a smoothly
                // climbing Jones judder against a camera that stepped thirty times a second. This is the
                // other half of that smoothing, and it must run BEFORE the eye is read below.
                if (interactionCamera.IsActive)
                {
                    interactionCamera.Present(smoothPresentation ? tickBlend : 1f);
                }
                if (_deathCamera.IsActive)
                {
                    _deathCamera.Present(smoothPresentation ? tickBlend : 1f);
                }

                Camera3D eye = _playerDead && _deathCamera.IsActive
                    ? _deathCamera.Camera
                    : (_player is { IsOnLadder: true } or { IsInWorldInteraction: true })
                        && interactionCamera.IsActive
                        ? interactionCamera.Camera
                        : _playerMode && _player is not null ? _player.Camera : _camera.Camera;

                // The walking eye glides between its tick pair, and so does the recoil kick inside its
                // orientation; the mouse half of the look is this frame's own. The fly and death cameras
                // move per frame on their own and need nothing.
                if (smoothPresentation
                    && _playerMode
                    && !_playerDead
                    && _player is { HasViewTickPair: true, IsOnLadder: false, IsInWorldInteraction: false })
                {
                    eye = _player.SmoothedCamera(tickBlend);
                }

                // Behind the cinematic shell the eye is the slow menu orbit, on real elapsed time, whatever
                // camera the scene would otherwise have used. Not behind the pause menu: the camera the
                // scene would otherwise have used is the player's own, and orbiting away from where they
                // were standing is the one thing a pause must not do. The showcase camera outlives the menu
                // it was built for -- starting a mission does not clear it -- so this has to say so.
                if (menuMode && !pauseMenu && _showcaseCamera is not null)
                {
                    int menuCameraNow = _loop.TimeSource.Milliseconds;
                    float menuCameraElapsed = Math.Clamp((menuCameraNow - lastMenuCameraMilliseconds) / 1000f, 0f, 0.1f);
                    lastMenuCameraMilliseconds = menuCameraNow;
                    eye = _showcaseCamera.Update(menuCameraElapsed);
                }
                else if (mapScene is { } liveMap)
                {
                    eye = liveMap.Camera;
                }

                // The mission-entry shot owns the view outright while it runs, over the menu's orbit and
                // over the player's own eye alike -- it begins at one and ends exactly at the other.
                if (_entry.IsCameraActive)
                {
                    eye = _entry.Camera;
                }
                else if (_cutscenes?.Frame is { } cutsceneView)
                {
                    eye = cutsceneView.Camera;
                }

                // Everything soldier-shaped resolves its own frame state once, here, so the shadow
                // cascades and the geometry pass draw identical bodies in identical places.
                // The backdrop's soldiers resolve their frame state too, because the shadow pass below is
                // about to read their poses. Only one of the two is ever live: `_soldiers` belongs to a
                // played mission, `_backdropSoldiers` to the menu's.
                SoldierRenderer? shadowCasters = _soldiers ?? _backdropSoldiers;
                _world.BeginFrame(tickBlend, smoothPresentation);
                shadowCasters?.BeginFrame(tickBlend, smoothPresentation);
                _gunPickupRenderer?.BeginFrame(tickBlend, smoothPresentation);

                // The modern path renders the scene offscreen so that it still exists, with its depth, once
                // it has been drawn -- which is what every effect from stage R3 on needs. With r_hdr off,
                // or on a backend that cannot offer a target, nothing here happens and the frame is drawn
                // straight to the back buffer exactly as the original draws it.
                SceneTargetDescription sceneTarget = new(
                    Math.Max(1, (int)(_window.Width * RenderCVars.RenderScale.Value)),
                    Math.Max(1, (int)(_window.Height * RenderCVars.RenderScale.Value)),
                    RenderCVars.OffscreenScene.Value ? Math.Max(1, RenderCVars.MultiSampling.Value) : 1,
                    Deferred: RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value,

                    // An 8-bit target under the vanilla preset: with no tone map and unit exposure the whole
                    // offscreen detour is then a copy, which is what keeps that preset identical to the
                    // build that had no offscreen path at all.
                    HighDynamicRange: RenderCVars.ToneMap.Value != ToneMapOperator.None,

                    // Asked for only when the accumulation is running, because it is the accumulation that
                    // does the upsampling: without it these are two buffers holding a stretched copy.
                    OutputWidth: wantsTemporal && RenderCVars.RenderScale.Value < 1f ? _window.Width : 0,
                    OutputHeight: wantsTemporal && RenderCVars.RenderScale.Value < 1f ? _window.Height : 0,
                    GBuffer: (RenderCVars.Lighting.Value != LightingModel.Dynamic
                            ? GBufferLayout.BakedLighting
                            : GBufferLayout.Compact)
                        | (wantsTemporal || RenderCVars.MotionBlur.Value > 0f
                            ? GBufferLayout.MotionVectors
                            : GBufferLayout.Compact));

                // The sun arrives in world space and the G-buffer's normals are in view space, so it is
                // rotated by the camera here. Transposed() is not decoration: the rows of the orientation
                // ARE the view axes, and Transform() applies the engine's row-vector convention which dots
                // the columns -- the inverse rotation. Getting it wrong lights the world from a direction
                // that swings as the player turns.
                DirectionalLightSettings light = _sun ?? defaultSun;
                Vector3f viewSunDirection = eye.Orientation.Transposed().Transform(light.Direction);

                bool wantsShadows = RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value
                    && RenderCVars.Shadows.Value > 0;

                ShadowSettings shadowSettings = wantsShadows
                    ? new ShadowSettings(
                        RenderCVars.ShadowCascades.Value,
                        RenderCVars.Shadows.Value,
                        RenderCVars.ShadowDistance.Value)
                    {
                        ContactReach = RenderCVars.ContactShadows.Value,
                        FilterRadius = RenderCVars.ShadowSoftness.Value,
                    }
                    : ShadowSettings.Off;

                int cascadeCount = ShadowCascades.Fit(
                    eye,
                    light.Direction,
                    shadowSettings,
                    verticalFieldOfView,
                    aspectRatio,
                    0.1f,
                    cascadeStorage);

                _device.SetShadows(
                    shadowSettings,
                    cascadeStorage.AsSpan(0, cascadeCount),
                    DepthUnprojection.From(unjitteredProjection, 0.1f, FarPlane),

                    // View space back into camera-relative world space, which is the frame the cascades
                    // are fitted in. The same transpose, for the same reason.
                    eye.Orientation.Transposed());

                // The medium. Density comes from the level's own FlatSky "Fog Amount" unless overridden:
                // a level that asked for thick fog gets thick fog, so the missions keep their own moods
                // instead of all being handed one number. The colours come from the sun, so a fog bank is
                // lit by the same light as the ground under it.
                float authoredDensity = _sky is { HasSky: true }
                    ? 0.004f + (Math.Clamp(_sky.FogAmount, 0f, 1f) * 0.03f)
                    : 0.014f;

                bool wantsFog = RenderCVars.FogQuality.Value > 0
                    && RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value;

                VolumetricFogSettings medium = VolumetricFogSettings.Off;
                if (wantsFog)
                {
                    // The sky plane's height is an offset above the ground, not a world Z: a level's
                    // coordinates carry a kilometres-large absolute offset, so it has to be anchored to
                    // the scene's own ground before the march can compare a sample against it.
                    medium = fogPresets.Resolve(
                        _options.StartMission,
                        authoredDensity,
                        (float)(_scene.GroundHeight + (_sky is { HasSky: true } ? _sky.PlaneHeight : 0f)),
                        RenderCVars.FogQuality.Value);

                    // A console override beats the authored preset, so a value can be dialled in live and
                    // then written back into the file.
                    if (RenderCVars.FogDensity.Value > 0f)
                    {
                        medium = medium with { Density = RenderCVars.FogDensity.Value };
                    }
                }

                _device.SetDebugView(RenderCVars.Debug.Value);

                // The derived detail is a deferred-path feature: it changes the normal and the roughness
                // written into the G-buffer, and without one there is nothing to write them into.
                _device.SetMaterialDetail(
                    RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                        ? RenderCVars.MaterialDetail.Value
                        : 0f);
                _device.SetReflections(
                    RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                        ? new ReflectionSettings(
                            RenderCVars.Reflections.Value,
                            RenderCVars.ReflectionIntensity.Value,
                            RenderCVars.ReflectionMaxRoughness.Value)
                        : ReflectionSettings.Off);

                // Ambient occlusion needs the G-buffer's normals and depth, so it is offered only when
                // there is a G-buffer; without one the lighting has no ambient term of its own to shade.
                _device.SetAmbientOcclusion(
                    RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                        ? new AmbientOcclusionSettings(
                            RenderCVars.AmbientOcclusion.Value,
                            RenderCVars.AmbientOcclusionRadius.Value,
                            RenderCVars.AmbientOcclusionIntensity.Value,
                            RenderCVars.ScreenSpaceGi.Value,
                            RenderCVars.ScreenSpaceGiIntensity.Value)
                        : AmbientOcclusionSettings.Off);

                // Reprojection compares two frames, and the jitter is precisely what differs between them,
                // so the matrix handed over is the one without it.
                _device.SetTemporalAntiAliasing(
                    new TemporalAaSettings(
                        wantsTemporal,
                        RenderCVars.TemporalFeedback.Value,
                        jitterX,
                        jitterY,
                        RenderCVars.TemporalSharpening.Value),
                    unjitteredProjection,
                    new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
                    eye.Orientation.Transposed());

                _device.SetVolumetricFog(
                    medium,
                    new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
                    wantsFog ? fogPresets.VolumesFor(_options.StartMission) : ReadOnlySpan<LocalFogVolume>.Empty);

                // Filled BEFORE the scene capture opens: a cascade renders into its own framebuffer, and
                // beginning a capture first would leave the shell holding two render targets at once.
                BeginStage();
                if (cascadeCount > 0 && _shadowPass is not null)
                {
                    _shadowPass.Render(cascadeStorage, cascadeCount, eye, _scene, _world, shadowCasters);
                }
                EndStageIfSlow("the shadow cascades");

                bool captured = RenderCVars.OffscreenScene.Value && _device.BeginSceneCapture(sceneTarget);
                if (captured)
                {
                    // The scheduler cleared the back buffer; the target it did not know about needs it too.
                    _device.Clear(_scheduler.ClearColour);

                    // The live sun, lifted by r_sun_scale so a sunlit surface reads as sunlit rather than
                    // as flat daylight -- the bakes carry the sun at full radiosity intensity, the dynamic
                    // model adds it live at magnitude one, and only this makes up the difference. The baked
                    // model ignores it (it never adds a live sun), so the scale is a no-op below ultra.
                    Vector3f scaledSun = light.Colour * RenderCVars.SunScale.Value;

                    _device.SetLighting(new SceneLighting(
                        RenderCVars.Lighting.Value,
                        viewSunDirection,
                        scaledSun,
                        light.Ambient,
                        RenderCVars.LightScale.Value));
                    _device.BeginPass(ScenePass.Geometry);

                    // The sky is geometry as far as the G-buffer is concerned, so without this the dynamic
                    // model would multiply the dome by an ambient term and the horizon would go dark. It
                    // is not lit; it IS light.
                    _device.SetMaterialOverride(new SurfaceMaterial(Emissive: 1f));
                }
                else
                {
                    // Vanilla draws opaque geometry directly to the back buffer, so it has no backend
                    // BeginPass bracket. Give that path the same complete GPU timing as deferred geometry.
                    _device.BeginStageTiming(RenderStage.Geometry);
                }

                if (mapScene is { } insetMap)
                {
                    float renderScale = captured ? RenderCVars.RenderScale.Value : 1f;
                    _device.SetViewport(new Viewport(
                        (int)(insetMap.Viewport.X * renderScale),
                        (int)((_window.Height - insetMap.Viewport.Y - insetMap.Viewport.Height) * renderScale),
                        Math.Max(1, (int)(insetMap.Viewport.Width * renderScale)),
                        Math.Max(1, (int)(insetMap.Viewport.Height * renderScale))));
                }

                // The SIMOTECH map is the live terrain/object pass, not a view of the sky dome from
                // directly above. The green CRT wash is applied in the 2D computer pass below.
                //
                // The exception is the tactical transition. Its climb starts at eye height with the
                // horizon across the middle of the frame and only reaches straight down at the top, so
                // dropping the sky for the whole of it would leave the first half of the shot rising out
                // of a black void.
                if (mapScene is null || _computerFlight.IsFlying)
                {
                    _sky?.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);
                }

                if (captured)
                {
                    _device.SetMaterialOverride(SurfaceMaterial.Default);
                }
                _terrain?.Draw(
                    eye,
                    projection,
                    verticalFieldOfView,
                    aspectRatio,

                    // -viewdist still wins when it is given, so an existing diagnostic command line keeps
                    // meaning what it did; otherwise the setting decides.
                    _options.TerrainViewDistance > 0 ? _options.TerrainViewDistance : RenderCVars.TerrainDetail.Value,
                    GraphicsPresetRegistry.Default.Current != GraphicsPreset.Vanilla,
                    RenderCVars.TerrainOcclusion.Value);
                EndStageIfSlow("the sky and the terrain");
                bool cameraIndoors = IsCameraInsideBuilding(eye.Position);
                _world.Draw(_scene, eye, projection);
                EndStageIfSlow("the world's objects");
                _gunPickupRenderer?.Draw(eye, projection);

                // The grenades in the air, solid geometry among the world's own.
                if (_grenadeRenderer is not null && _player is not null)
                {
                    _grenadeRenderer.Draw(
                        _player.Grenades,
                        eye,
                        projection,
                        smoothPresentation ? tickBlend : 1f);
                }

                // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
                // never swaps in the view-model block for them (player.md section 15.3).
                _soldiers?.Draw(eye, projection);

                // The player's own body, once it has fallen -- drawn only under the death camera, so it never
                // appears in front of the first-person view during normal play.
                _soldiers?.DrawPlayerBody(eye, projection);
                EndStageIfSlow("the soldiers");

                // The marks rounds have left, over the world and under the first-person rig.
                if (_marks is not null && _player is not null && RenderCVars.BulletMarks.Value)
                {
                    _marks.Draw(_player.Marks, eye, projection);
                }

                if (mapScene is not null)
                {
                    _device.SetViewport(new Viewport(
                        0,
                        0,
                        captured ? sceneTarget.Width : _window.Width,
                        captured ? sceneTarget.Height : _window.Height));
                }

                if (!captured)
                {
                    _device.EndStageTiming();
                    _device.BeginStageTiming(RenderStage.Forward);
                }


                // The view model and anything translucent belong to the forward pass: it is drawn with its
                // own narrower projection, so its depth must not end up in the buffer the fog and the
                // reflections read.
                EndStageIfSlow("the deferred lighting, occlusion, reflections and fog");

                if (captured)
                {
                    _device.EndPass();
                    _device.BeginPass(ScenePass.Forward);
                }

                // The ballistic streaks are LIGHT, and light belongs to the forward pass -- drawn in
                // the geometry pass they are written into the g-buffer as scenery and come out lit
                // and fogged (a dim grey square was exactly what that looked like). Here they draw
                // additively over the finished scene, emissive like the muzzle flame, testing the
                // world's depth but writing none.
                if (_tracers is not null && _player is not null)
                {
                    _tracers.Draw(
                        _player.Ballistics,
                        eye,
                        projection,
                        smoothPresentation ? tickBlend : 1f);

                    // The finite guard's confession, rate-limited: a growing count is a NaN reaching
                    // the tracer geometry -- the exact stuff one-frame black squares were made of.
                    if (_tracers.SkippedQuads > _tracerSkipsLogged
                        && (_tracerSkipsLogged == 0 || _tracers.SkippedQuads - _tracerSkipsLogged >= 100))
                    {
                        _tracerSkipsLogged = _tracers.SkippedQuads;
                        _logger.Warning(
                            "Tracer",
                            $"{_tracers.SkippedQuads} tracer quad(s) carried a non-finite value and were not drawn.");
                    }
                }

                // The soldiers' muzzle flames, for the same reason: drawn from the soldier pass they
                // landed in the G-buffer and shaded as scenery -- the one-frame black square at a
                // firing guard's muzzle. Light draws over the lit scene.
                (_soldiers ?? _backdropSoldiers)?.DrawMuzzleFlashes(eye, projection);

                // The impact bursts: dust hazes over the scene, spark light over the haze.
                _impactParticles?.Draw(eye, projection, smoothPresentation ? tickBlend : 1f);

                // Rain environmental particles for levels featuring rain weather (e.g. RainEffect task).
                // While the camera is inside a building's shell the rain is suppressed, exactly as the
                // reference editor does per frame — buildings keep the weather outside.
                if (_rain is { IsWeatherEnabled: true } rain)
                {
                    rain.Indoors = cameraIndoors;
                    rain.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);
                }

                // Snow uses the same authored building shells and weather band, but renders small,
                // drifting flakes instead of rain droplets.
                if (_snow is { IsWeatherEnabled: true } snow)
                {
                    snow.Indoors = cameraIndoors;
                    snow.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);
                }

                // SCameraLens draws its own alarmlight.spr in the forward pass: green when clear,
                // red while confirming/detecting, and attached to the head as it pans.
                if (_securityCameras is { } statusCameras)
                {
                    _cameraStatusLights?.Draw(
                        statusCameras.Cameras,
                        eye,
                        projection,
                        tickBlend,
                        smoothPresentation);
                }

                // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
                // then ask the human where the weapon is and draw that. Only in player mode, because the
                // original draws it out of the human's own render and the free camera is not a human -- and
                // NOT once the player is dead, when the death camera is watching the body from the outside
                // and a pair of arms hanging in front of it would be exactly wrong.
                // Not while the entry shot is running: the camera is somewhere over the level, and a pair
                // of arms and a HUD hanging in front of it would be exactly wrong. They come up at the
                // moment the view is handed over, which is also the moment the player can use them.
                bool remasterHud = RemasteredHudPresentation.UsesRemasteredHud(RenderCVars.RemasteredHud.Value);
                bool tacticalMessages = RemasteredHudPresentation.UsesTacticalMessages(
                    remasterHud,
                    RenderCVars.TacticalMessages.Value);
                bool showPlayerHud = _playerMode && !_computerOpen && !_entry.IsCameraActive
                    && _cutscenes?.IsPlaying != true
                    && !(_playerDead && _deathCamera.IsActive)
                    && _player is { View: not null };
                bool showFirstPersonHud = showPlayerHud
                    && _player is { IsOnLadder: false, IsInWorldInteraction: false };
                if (showFirstPersonHud && _player is { View: { } rig })
                {
                    // Only an advancing clip changes the skin, so the re-skin is keyed to the tick rather
                    // than the frame.
                    if (_player.PoseChanged)
                    {
                        _arms?.Invalidate();
                        _player.AcknowledgePose();
                    }

                    // The view model gets its OWN projection: 0x49F700 saves the current view block,
                    // rebuilds [16..19] from dword_B81700 -- HumanCamera+220, filled from HumanView+488 =
                    // tan(30 degrees) -- and restores it afterwards, for any bone model with no LOD chain,
                    // which the HumanView is (0x464660 leaves +212 at zero). The rebuild is sub_497D70's
                    // own algebra, the SAME build every camera pass uses: [16] is the plain half-angle
                    // tangent, [18] = halfWidth / [16] and [19] = Aspect43 * [18] are the pixel scales the
                    // projection divides through (sub_50FE60 publishes them as the per-vertex xScale and
                    // yScale). So against the world's tan(45), the arms and the weapon really are drawn
                    // tan45/tan30 = 1.732x magnified about the screen centre -- which moves a hand held
                    // below centre DOWN and out of frame. That is what holds the glove below the bottom
                    // edge in retail. (An earlier pass refuted this as "larger is the wrong direction";
                    // larger is exactly what hides it.) section 14.7.
                    //
                    // The vertical tangent is 3/4 of the horizontal one and does NOT follow the window's
                    // aspect: [19] = Aspect43() * [18] works out to yScale = halfHeight / (0.75 * tan) at
                    // EVERY aspect, so retail's vertical framing -- the glove at the bottom edge -- is
                    // aspect-invariant. Dividing the tangent by the aspect instead (the world's widescreen
                    // rule) shrinks the view model's vertical reach at 16:9 and pushes the hand off the
                    // bottom of the frame. At 4:3 the two rules coincide, tanY = 0.433 and tanX = tan(30).
                    float viewModelFieldOfView = 2f * MathF.Atan(0.75f * sight.ViewModelTanX);
                    Matrix4f viewModelProjection = ViewModelProjection.Create(
                        viewModelFieldOfView,
                        aspectRatio,
                        FarPlane);

                    // Between-tick smoothing for everything the rig steps at 30 Hz: its origin (the eye
                    // the body carried plus the clip's hold offset), its skin, and the weapon bone. The
                    // LOOK is already per frame; without this the arms and the knife stepped at 30 Hz
                    // against a camera that now glides -- which read as jitter -- and skipped several
                    // poses per drawn frame whenever ticks ran back to back, which read as the animation
                    // racing.
                    Vector3d? rigOriginUnits = null;
                    Matrix3f? rigOrientation = null;
                    BoneAttachment? weaponAttachment = _player.HeldWeapon;
                    float previousPoseShare = 0f;
                    if (smoothPresentation && _player.HasViewTickPair)
                    {
                        (Vector3d smoothedOrigin, Matrix3f smoothedOrientation, BoneAttachment? smoothedWeapon) =
                            _player.SmoothedRig(tickBlend);
                        rigOriginUnits = smoothedOrigin;
                        rigOrientation = smoothedOrientation;
                        weaponAttachment = smoothedWeapon ?? weaponAttachment;
                        previousPoseShare = 1f - tickBlend;
                    }

                    // The retail rig is foreground-only: it draws after the world under its own transform
                    // layer, so an already-rendered wall must not be allowed to cut through the hands or
                    // weapon. Resetting only depth keeps normal arm-versus-weapon self-occlusion.
                    ViewModelDepth.Begin(_device);
                    _arms?.Draw(rig, eye, viewModelProjection, rigOriginUnits, previousPoseShare, rigOrientation);

                    // Cycling weapons changes the model as well as the animations, so the renderer has to
                    // follow whatever the cycle put in hand.
                    if (_loadedMissionForWeapon is { } missionForWeapon)
                    {
                        FollowHeldWeapon(_player, _heldWeapon, missionForWeapon);
                    }

                    if (weaponAttachment is { } attachment && _heldWeapon is { IsReady: true })
                    {
                        _heldWeapon.Draw(
                            attachment,
                            eye,
                            viewModelProjection,
                            _player.FiredThisTick,
                            _player.ShotSequence);
                    }
                }

                // Everything 2D belongs AFTER the resolve, on the back buffer: the HUD, the damage wash,
                // the menus and the console are authored in display colours and must never be tone mapped.
                // Unconditional, and outside the player-mode block on purpose -- a free-camera frame draws
                // no view model, and leaving the capture open would strand it until the next frame threw.
                if (!captured)
                {
                    _device.EndStageTiming();
                }

                if (captured)
                {
                    _device.EndPass();
                    _device.EndSceneCapture();
                    _device.ResolveScene(postProcess);
                    captured = false;
                    EndStageIfSlow("the resolve (tone map, bloom, depth of field, temporal)");
                }

                _device.BeginStageTiming(RenderStage.UserInterface);

                // StatusScreen is a display-space layer. Keep every retail HUD component out of the
                // captured scene so camera motion blur, depth of field, bloom and tone mapping can only
                // affect the world and first-person rig. The damage and flash washes intentionally remain
                // later in the draw order and still cover the HUD exactly as they did before this separation.
                if (!remasterHud
                    && showFirstPersonHud
                    && _player is { Weapons: { } carried }
                    && _hud is { IsReady: true } retailHud)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    // Scoped, the Dragunov's overlay REPLACES the weapon HUD -- 0x473040's latch branch
                    // never calls the parent handler.
                    if (_player.IsSniperScoped)
                    {
                        retailHud.DrawSniperScope(batch, _window.Width, _window.Height);
                    }
                    else
                    {
                        CarriedWeapon? held = carried.InHand;
                        int m203Reserve = held is { Weapon.ScriptId: "WEAPON_ID_M16A2" }
                            && _ammoTypes?.Types.FirstOrDefault(
                                ammo => ammo.ScriptId == "AMMO_ID_M203") is { } m203
                            ? carried.Ammunition.Count(m203.AmmoId)
                            : 0;
                        retailHud.Draw(
                            batch,
                            held?.Weapon,
                            held?.Gun.RoundsInClip ?? 0,
                            held is { } primary ? primary.Gun.Reserve(carried.Ammunition) : 0,
                            held?.Gun.State.SecondSubCounter ?? 0,
                            m203Reserve,
                            _window.Width,
                            _window.Height,
                            UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                    }

                    batch.End();
                }

                // Retail action sprites are deliberately retained for the faithful UI. The remaster's
                // corresponding context label is part of the single modern HUD draw below.
                if (!remasterHud
                    && showFirstPersonHud
                    && _hud is { IsReady: true } retailActions
                    && _player?.ActionSprite is { } actionSprite)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    retailActions.DrawAction(
                        batch,
                        actionSprite,
                        _window.Width,
                        _window.Height,
                        UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                    batch.End();
                }

                // The mission's status messages. Kept off screen while the computer is up, as the rest
                // of the HUD is: the map is a full-screen readout of the same mission and a comms card
                // over it would be the same information twice.
                if (!tacticalMessages
                    && showFirstPersonHud
                    && _missionObjectives is { } messageRuntime
                    && _hud is { IsReady: true } retailMessages)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    retailMessages.DrawStatusMessages(
                        batch,
                        messageRuntime.DisplayedMessages.Where(static message => !message.IsCutscene),
                        _window.Width,
                        _window.Height,
                        UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                    batch.End();
                }

                // The human-owned retail display remains alive when a full-body interaction hides the
                // hands and weapon: that is precisely when HumanPlayer's timed-use bar is needed. In the
                // original dispatch order this follows the weapon handler and precedes StatusScreen's timer.
                if (!remasterHud
                    && showPlayerHud
                    && _player is { } retailPlayer
                    && _hud is { IsReady: true } retailStatus)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    retailStatus.DrawPlayerStatus(
                        batch,
                        _playerHealth.RemainingFraction,
                        retailPlayer.InteractionProgress,
                        _missionObjectives?.InterfaceTimerRemainingTicks,
                        _window.Width,
                        _window.Height,
                        UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                    batch.End();
                }

                if (_cutscenes?.Frame is { } cinematicFraming)
                {
                    DrawCutsceneFraming(batch, cinematicFraming);
                }

                // ComputerMap renders the terrain through its own top-down camera and turns only that
                // captured view into green phosphor. The SIMOTECH casing is a separate retail panel that
                // is drawn after the resolve, in its authored colours, before the HUD fills its aperture.
                //
                // The tactical interface has no casing at all — that is the point of it — so this whole
                // block is the retail path's alone.
                if (_computerOpen
                    && TacticalComputer is null
                    && _playerMode
                    && !(_playerDead && _deathCamera.IsActive)
                    && _player is { IsOnLadder: false, IsInWorldInteraction: false })
                {
                    Camera3D computerEye = _player.Camera;
                    if (smoothPresentation && _player.HasViewTickPair)
                    {
                        computerEye = _player.SmoothedCamera(tickBlend);
                    }

                    float computerViewModelFov = 2f * MathF.Atan(0.75f * sight.ViewModelTanX);
                    Matrix4f computerViewModelProjection = ViewModelProjection.Create(
                        computerViewModelFov,
                        (float)_window.Width / Math.Max(1, _window.Height),
                        FarPlane);

                    if (_mapComputerDevice is { IsReady: true } deviceModel)
                    {
                        Viewport deviceViewport = MapComputerRenderer.DeviceViewport(_window.Width, _window.Height);
                        float horizontalHalfTangent = MathF.Tan(computerViewModelFov * 0.5f)
                            * _window.Width / Math.Max(1f, _window.Height);
                        // The mapcomputer mesh is a close foreground prop. Its housing extends past the
                        // view just as it does in retail; 2.50 is the retail asset's aperture-to-frame
                        // calibration against the map task's 91% screen viewport. The casing is intentionally
                        // cropped at the display edges: leaving its outside black behind the four corners
                        // makes the computer look set back from the camera rather than held in the foreground.
                        float horizontalCoverage = deviceViewport.Width / (float)Math.Max(1, _window.Width) * 2.50f;
                        deviceModel.Draw(computerEye, computerViewModelProjection, horizontalHalfTangent, horizontalCoverage);
                    }

                    _device.SetDepthState(DepthState.Disabled);
                }

                if (_computerOpen
                    && _missionObjectives is { } objectiveRuntime
                    && _player is { } computerPlayer)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    if (TacticalComputer is { } tacticalChrome)
                    {
                        tacticalChrome.Draw(
                            batch,
                            objectiveRuntime,
                            computerPlayer.Body.Position,
                            _soldiers?.MapContacts() ?? [],
                            _computerHighlights?.Visible() ?? [],
                            _window.Width,
                            _window.Height,
                            _computerFlight.ChromeOpacity);
                    }
                    else if (_mapComputer is { IsReady: true } computer)
                    {
                        computer.Draw(
                            batch,
                            objectiveRuntime,
                            computerPlayer.Body.Position,
                            _soldiers?.MapContacts() ?? [],
                            _computerHighlights?.Visible() ?? [],
                            _window.Width,
                            _window.Height);
                    }

                    batch.End();
                }

                if (_missionFailedMenu)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    if (TacticalComputer is { } tacticalFailed)
                    {
                        tacticalFailed.DrawMissionFailed(batch, _window.Width, _window.Height);
                    }
                    else if (_mapComputer is { IsReady: true } failedComputer)
                    {
                        failedComputer.DrawMissionFailed(batch, _window.Width, _window.Height);
                    }

                    batch.End();
                }

                // The damage wash goes over everything, in its own 2D pass: a screen-filling red quad whose
                // four vertex alphas are the four corners sub_411300 fades (HumanPlayer_DrawDamageFlash,
                // 0x4bb770). Outside the player-mode block on purpose -- it has to survive the player's death,
                // when the arms and the HUD are gone but the last thing the screen showed is still red.
                float flashStrength = Math.Clamp(RenderCVars.DamageFlash.Value, 0f, 1f);
                if (_damageFlash.IsActive && flashStrength > 0f)
                {
                    float flashWidth = _window.Width;
                    float flashHeight = _window.Height;

                    // The strength scales the alpha only. The four corners still carry their own values
                    // and still decay on the original's schedule, so a hit from the left is still redder
                    // on the left and still fades over the same number of ticks -- it is the peak that
                    // comes down, not the cue.
                    Bgra32 Red(float alpha) =>
                        new(0, 0, 255, (byte)Math.Clamp((int)(alpha * flashStrength * 255f), 0, 255));

                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    batch.DrawQuad(
                        TextureHandle.None,
                        new Vertex2D(0f, 0f, 0f, 0f, Red(_damageFlash.TopLeft)),
                        new Vertex2D(flashWidth, 0f, 1f, 0f, Red(_damageFlash.TopRight)),
                        new Vertex2D(flashWidth, flashHeight, 1f, 1f, Red(_damageFlash.BottomRight)),
                        new Vertex2D(0f, flashHeight, 0f, 1f, Red(_damageFlash.BottomLeft)));
                    batch.End();
                }

                // The flashbang's whiteout, over even the damage wash: at strength 2 the screen is
                // solid white until the fall brings it under one; a merely-seen flash starts at one
                // and thins from the first frame. Uniform, unlike the wash -- light has no corner.
                if (_flashbangStrength > 0f)
                {
                    byte whiteAlpha = (byte)Math.Clamp((int)(Math.Min(_flashbangStrength, 1f) * 255f), 0, 255);
                    Bgra32 white = new(255, 255, 255, whiteAlpha);
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    batch.DrawQuad(
                        TextureHandle.None,
                        new Vertex2D(0f, 0f, 0f, 0f, white),
                        new Vertex2D(_window.Width, 0f, 1f, 0f, white),
                        new Vertex2D(_window.Width, _window.Height, 1f, 1f, white),
                        new Vertex2D(0f, _window.Height, 0f, 1f, white));
                    batch.End();
                }

                // HumanPlayer_DrawStatus keeps drawing states 30/31/32/35/36 in third person and adds
                // the current/total bar at 0x413120. It must therefore sit outside the first-person arms
                // block, which interactions deliberately suppress.
                if (remasterHud
                    && _player?.InteractionProgress is float interactionProgress
                    && _remasteredHud is { IsReady: true } interactionHud)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    interactionHud.DrawInteractionProgress(
                        batch, interactionProgress, _window.Width, _window.Height);
                    batch.End();
                }

                // The modern HUD is intentionally the last gameplay overlay. Rendering it only after the
                // scene resolve, close foreground device and damage wash means no hand, weapon, world mesh
                // or post-process pass can cover it.
                // The scope overlay under the modern HUD: the retail sprite machinery draws it in
                // both HUD flavours, so the scope is the scope whatever the player's UI taste.
                if (remasterHud
                    && showFirstPersonHud
                    && _player is { IsSniperScoped: true }
                    && _hud is { IsReady: true } scopeHud)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    scopeHud.DrawSniperScope(batch, _window.Width, _window.Height);
                    batch.End();
                }

                if (remasterHud
                    && showFirstPersonHud
                    && _player is { Weapons: { } modernCarried }
                    && _remasteredHud is { IsReady: true } modernHud)
                {
                    CarriedWeapon? inHand = modernCarried.InHand;
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    modernHud.Draw(
                        batch,
                        new RemasteredHudSnapshot(
                            _playerHealth.RemainingFraction,
                            _player.Yaw,
                            inHand?.Weapon,
                            inHand?.Gun.RoundsInClip ?? 0,
                            inHand is { } held ? held.Gun.Reserve(modernCarried.Ammunition) : 0,
                            modernCarried.State,
                            _player.ActionSprite,
                            _missionObjectives?.Objectives ?? [],
                            _player.IsSniperScoped),
                        _window.Width,
                        _window.Height);
                    batch.End();
                }

                if (tacticalMessages
                    && showFirstPersonHud
                    && _missionObjectives is { } modernMessages
                    && _messages is { IsReady: true } modernCards)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    float topOffset = new LayoutContext(_window.Width, _window.Height)
                        .Px(RemasteredHudRenderer.MessageTopInsetDesign);
                    modernCards.Draw(
                        batch,
                        modernMessages.DisplayedMessages.Where(static message => !message.IsCutscene),
                        _window.Width,
                        _window.Height,
                        topOffset);
                    batch.End();
                }

                // Cutscene StatusMessages are subtitles, not HUD. They remain visible while the authored
                // camera owns the frame even though the arms, weapon and ordinary mission UI are hidden.
                if (_cutscenes?.IsPlaying == true && _missionObjectives is { } subtitleRuntime)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    // Retail cutscenes always use StatusMessageScreen's shipped bitmap face and its
                    // left-aligned 620/640-width line. TacticalMessages only replaces the in-game feed;
                    // letting it replace dialogue produced a centred modern subtitle unlike the original.
                    if (_hud is { IsReady: true } subtitleHud)
                    {
                        subtitleHud.DrawStatusMessages(
                            batch,
                            subtitleRuntime.DisplayedMessages,
                            _window.Width,
                            _window.Height,
                            // Retail scales this full-width normalized subtitle face with framebuffer
                            // height. Keeping the HUD's integer auto scale here made a 938px capture use
                            // the unscaled 480p glyphs, less than half the size of the original.
                            // The retail text objects use the shipped face at their 0.9 presentation
                            // scale; retain that calibration while growing it with framebuffer height.
                            Math.Max(1f, (_window.Height / (float)UiScale.AuthoredHeight) * 0.87f));
                    }
                    else if (_messages is { IsReady: true } subtitleCards)
                    {
                        subtitleCards.Draw(batch, subtitleRuntime.DisplayedMessages, _window.Width, _window.Height);
                    }
                    batch.End();
                }

                float debugPanelHeight = 0f;

                _device.EndStageTiming();

                // The F12 debug overlay and crosshair. Works in both the walking player and the free (flying)
                // camera: the state lines come from whichever is active, the crosshair readout and the reticle
                // are cast from the eye that is actually drawing.
                if (showDebug && _debugOverlay is { IsReady: true } debugOverlay)
                {
                    _device.BeginStageTiming(RenderStage.DebugRendering);
                    static string I(FormattableString s) => FormattableString.Invariant(s);

                    Vector3d originU = new(
                        eye.Position.X * WorldScene.UnitsPerMetre,
                        eye.Position.Y * WorldScene.UnitsPerMetre,
                        eye.Position.Z * WorldScene.UnitsPerMetre);
                    Matrix3f orient = eye.Orientation;
                    Vector3d fwd = new(orient.M31, orient.M32, orient.M33);
                    const double reachU = 300.0 * WorldScene.UnitsPerMetre;
                    Vector3d endU = new(
                        originU.X + (fwd.X * reachU), originU.Y + (fwd.Y * reachU), originU.Z + (fwd.Z * reachU));

                    string target = "target: (nothing / terrain / sky)";
                    string? detect = null;
                    double bestT = double.MaxValue;
                    if (_worldSolids is not null
                        && _worldSolids.TryIdentify(originU, endU, out string objLabel, out WorldRayHit objHit))
                    {
                        bestT = objHit.Distance;
                        target = I($"target: OBJECT {objLabel} @ {objHit.Distance / WorldScene.UnitsPerMetre:F1}m ({objHit.Position.X:F0}, {objHit.Position.Y:F0}, {objHit.Position.Z:F0}) mat={objHit.Material}");
                    }

                    if (_soldiers is not null
                        && _soldiers.TryPick(originU, fwd, reachU, out string botName, out double botT, out Vector3d feetU, out int botZone)
                        && botT < bestT)
                    {
                        // The floor the game's collision puts under this soldier's feet — so a sunk guard shows
                        // its feet Z below the floor Z, and a correct one shows them level.
                        string floor = "?";
                        if (_worldSolids is not null)
                        {
                            Vector3d fTop = new(feetU.X, feetU.Y, feetU.Z + 2048.0);
                            Vector3d fBot = new(feetU.X, feetU.Y, feetU.Z - (10.0 * WorldScene.UnitsPerMetre));
                            if (_worldSolids.TryIdentify(fTop, fBot, out string floorLbl, out WorldRayHit floorHit))
                            {
                                floor = I($"{floorHit.Position.Z:F0} ({floorLbl} mat{floorHit.Material}) Δ={(feetU.Z - floorHit.Position.Z) / WorldScene.UnitsPerMetre:F2}m");
                            }
                        }

                        string describedBot = ModelNameRegistry.DescribeModel(botName);
                        target = I($"target: SOLDIER {describedBot} zone={HumanDamage.ZoneName(botZone)} feetZ={feetU.Z:F0} floorZ={floor}");
                    }

                    // Whatever soldier the crosshair is on, ask its brain which detection gate is (not) passing
                    // against the point the guards actually test -- the player's own eye in engine units, the same
                    // point soldiers.Tick is fed.
                    Vector3d watchedEye = _playerMode && _player is not null
                        ? _player.Body.Eye
                        : _camera.Position;
                    if (_soldiers is not null
                        && _soldiers.TryDetectionInfo(originU, fwd, reachU, watchedEye, out string detectInfo))
                    {
                        detect = detectInfo;
                    }

                    List<string> lines;
                    if (_playerMode && _player is not null)
                    {
                        Vector3d pu = _player.PositionUnits;
                        Vector3d pm = _player.Position;
                        lines =
                        [
                            "mode: WALK",
                            I($"pos units ({pu.X:F0}, {pu.Y:F0}, {pu.Z:F0})"),
                            I($"pos m     ({pm.X:F2}, {pm.Y:F2}, {pm.Z:F2})"),
                            I($"yaw {_player.Yaw * 180f / MathF.PI:F1}  pitch {_player.Pitch * 180f / MathF.PI:F1}"),
                            I($"grounded {_player.IsGrounded}  crouch {_player.IsCrouched}  wall {_player.TouchedWall}"),
                            target,
                        ];
                    }
                    else
                    {
                        Vector3d cm = _camera.Position;
                        lines =
                        [
                            "mode: FREE CAM",
                            I($"pos units ({cm.X * WorldScene.UnitsPerMetre:F0}, {cm.Y * WorldScene.UnitsPerMetre:F0}, {cm.Z * WorldScene.UnitsPerMetre:F0})"),
                            I($"pos m     ({cm.X:F2}, {cm.Y:F2}, {cm.Z:F2})"),
                            I($"yaw {_camera.Yaw * 180f / MathF.PI:F1}  pitch {_camera.Pitch * 180f / MathF.PI:F1}"),
                            target,
                        ];
                    }

                    if (detect is not null)
                    {
                        lines.Add(detect);
                    }

                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    debugPanelHeight = debugOverlay.Draw(batch, lines, _window.Width, _window.Height);

                    // A centre reticle so the target readout can be aimed — the crosshair the free camera lacks.
                    float cx = _window.Width * 0.5f;
                    float cy = _window.Height * 0.5f;
                    const float len = 9f;
                    const float th = 1f;
                    static Bgra32 White() => new(255, 255, 255, 220);
                    void Bar(float x0, float y0, float x1, float y1) => batch.DrawQuad(
                        TextureHandle.None,
                        new Vertex2D(x0, y0, 0f, 0f, White()),
                        new Vertex2D(x1, y0, 1f, 0f, White()),
                        new Vertex2D(x1, y1, 1f, 1f, White()),
                        new Vertex2D(x0, y1, 0f, 1f, White()));
                    Bar(cx - len, cy - th, cx + len, cy + th);
                    Bar(cx - th, cy - len, cx + th, cy + len);

                    batch.End();
                    _device.EndStageTiming();
                }

                // r_perflog, its own overlay rather than a section of F12's: it answers a different question
                // and is read while the game is being played rather than while it is being inspected. Top
                // right, so the two can be up together. When F12 is also enabled, place this panel below
                // the measured F12 panel instead of letting both panels claim the same top-right corner.
                if (RenderCVars.PerformanceLog.Value
                    && !_options.IsBenchmark
                    && _debugOverlay is { IsReady: true } performanceOverlay)
                {
                    _device.BeginStageTiming(RenderStage.DebugRendering);
                    static string I(FormattableString s) => FormattableString.Invariant(s);

                    IReadOnlyList<RenderStageTiming> timings = _device.StageTimings();
                    GpuFrameTiming gpu = _device.GpuTiming();
                    FramePerformanceStatistics frame = framePerformance.Statistics();
                    GraphicsPreset preset = GraphicsPresetRegistry.Default.Current;
                    List<string> perf =
                    [
                        I($"{preset}  {_window.Width}x{_window.Height}  vsync {(_device.VSyncEnabled ? "on" : "off")}  indoor {(cameraIndoors ? "yes" : "no")}"),
                        I($"fps {frame.FramesPerSecond:0.0}  frame {frame.AverageMilliseconds:0.00} ms  1% {frame.OnePercentLowFramesPerSecond:0.0}"),
                        "gpu ms   avg    p95   peak  budget",
                    ];
                    foreach (RenderStageTiming timing in timings)
                    {
                        if (!timing.Active)
                        {
                            perf.Add($"{timing.Stage,-24} off");
                            continue;
                        }

                        // The budget beside the measurement, because a number without the number it was
                        // supposed to beat is a number nobody acts on.
                        string budget = RenderBudget.For(timing.Stage) is { } allowed
                            ? I($" /{allowed:0.0}{(timing.Milliseconds > allowed ? " OVER" : string.Empty)}")
                            : string.Empty;

                        perf.Add(I($"{timing.Stage,-24} {timing.Milliseconds,5:0.00} {timing.P95Milliseconds,6:0.00} {timing.PeakMilliseconds,6:0.00}{budget}"));
                    }

                    perf.Add(gpu.SampleCount > 0
                        ? I($"{"gpu total",-24} {gpu.Milliseconds,5:0.00} {gpu.P95Milliseconds,6:0.00} {gpu.PeakMilliseconds,6:0.00}")
                        : "measuring...");

                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    float performanceTopOffset = debugPanelHeight > 0f
                        ? debugPanelHeight + DebugOverlayRenderer.PanelGapPixels(_window.Width, _window.Height)
                        : 0f;
                    performanceOverlay.DrawTopRight(
                        batch,
                        perf,
                        _window.Width,
                        _window.Height,
                        performanceTopOffset);
                    batch.End();
                    _device.EndStageTiming();
                }

                if (!reportedWorld)
                {
                    reportedWorld = true;
                    _logger.Information(
                        "World",
                        $"Drew {_world.LastDrawCount} sub-mesh(es), {_world.LastLightmappedCount} lightmapped, {_world.LastTriangleCount} triangles from {_camera.Position}.");

                    if (_terrain is not null)
                    {
                        _logger.Information(
                            "Terrain",
                            $"Drew {_terrain.LastChunkCount} chunk(s) in {_terrain.LastPassCount} material pass(es) "
                                + $"and {_terrain.LastBatchCount} multi-draw batch(es); "
                                + $"{_terrain.LastTriangleCount} triangles, from {_terrain.LastVisitedCount} "
                                + $"octree node(s) visited, {_terrain.LastOccludedChunkCount} chunk(s) occlusion-culled "
                                + $"by {_terrain.LastOcclusionQueryCount} query box(es), over {_terrain.TileCount} ground tile(s).");
                    }
                }
            }

            // The cinematic shell draws over the resolved showcase scene, in 2D on the back buffer, before
            // the console (which goes over even the shell).
            // The shell keeps being DRAWN through the mission-entry shot even though the menu is over, so
            // that it can fade rather than vanish. It hears nothing while it does.
            if ((menuMode || _entry.IsRunning) && shell is not null)
            {
                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(_window.Width, _window.Height);
                var shellPainter = new UiPainter(batch, new LayoutContext(_window.Width, _window.Height));
                batch.Begin();

                // None of the backdrop machinery applies to the pause menu: its backdrop is the mission,
                // already drawn on this very frame, and there is no swap for it to dissolve between.
                if (!pauseMenu)
                {
                    // Before the showcase level has loaded there is no scene to draw, so fill a dark backdrop
                    // rather than leaving the frame's blue clear behind the menu.
                    if (_scene is null)
                    {
                        shellPainter.FillRect(new UiRect(0f, 0f, _window.Width, _window.Height), Bgra32.FromRgb(12, 14, 18));

                        // Nothing on screen to dissolve from, so the hold is a no-op and the first backdrop is
                        // simply revealed. Without this the swap would wait forever on a hold that never came.
                        if (holdFrameForDissolve)
                        {
                            holdFrameForDissolve = false;
                            heldFrameReady = true;
                            heldFrameIsPicture = false;
                        }
                    }
                    else
                    {
                        // The outgoing level, caught here: after the 3D pass, before any of the menu is drawn
                        // over it, so what is held is the picture alone.
                        if (holdFrameForDissolve)
                        {
                            HoldFrameForDissolve();
                            holdFrameForDissolve = false;
                            heldFrameReady = true;
                            heldFrameIsPicture = true;
                        }

                        // The dissolve, over the resolved scene and UNDER the shell: the two levels blend into
                        // each other while the menu stays lit and responsive on top of both.
                        DrawDissolve(shellPainter, dissolve.Value);
                    }
                }

                // The whole interface behind one opacity. It has to be pushed HERE rather than left to
                // each screen's own transition, because a screen deliberately draws its top bar outside
                // that fade and the stack draws the pointer outside every screen — so only a push around
                // the whole draw takes all of it, chrome and cursor included.
                float shellOpacity = _entry.IsRunning ? _entry.ShellOpacity : 1f;
                if (shellOpacity > 0.001f)
                {
                    shellPainter.PushOpacity(shellOpacity);
                    shell.Draw(shellPainter);
                    shellPainter.PopOpacity();
                }

                // The shot's own framing goes over the interface at full strength: the bars and the title
                // belong to the cinematic, not to the menu, and they must not fade out with it.
                if (_entry.IsRunning)
                {
                    DrawEntryFraming(shellPainter, entryTitle, entrySubtitle);
                }

                batch.End();
                _device.EndStageTiming();
            }

            // The console goes over everything, including the menus, so it is the last thing drawn.
            if (console.IsOpen)
            {
                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(_window.Width, _window.Height);
                batch.Begin();
                console.Draw(batch, _window.Width, _window.Height, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                batch.End();
                _device.EndStageTiming();
            }

            // A flight writes one numbered frame per rendered frame -- stem.NNNN.ext beside the given
            // screenshot path -- so the sequence shows the temporal filters resolving under motion. Written
            // here, after everything 2D is on the back buffer, exactly where the single still is captured.
            // -entrycam writes its frames the same way, so the shot can be flipped through afterwards.
            // A camera move is the one thing a single still cannot show, and the entry is the one camera
            // move nobody can trigger at a repeatable moment by hand.
            if ((_options.HasFlight || (_options.EntryCamMission > 0 && _entry.IsRunning))
                && _options.ScreenshotPath is { } flightPath
                && _scene is not null)
            {
                Bgra32[] frame = new Bgra32[_window.Width * _window.Height];
                _device.CaptureFrame(_window.Width, _window.Height, frame);

                string directory = Path.GetDirectoryName(flightPath) ?? string.Empty;
                string stem = Path.GetFileNameWithoutExtension(flightPath);
                string extension = Path.GetExtension(flightPath);
                string numbered = Path.Combine(directory, FormattableString.Invariant($"{stem}.{flightFrame:0000}{extension}"));
                BitmapWriter.Write(numbered, _window.Width, _window.Height, frame);
                flightFrame++;
            }

            if (_options.ExitAfterFrames > 0 && _loop.Clock.FrameCount >= _options.ExitAfterFrames)
            {
                // A flight has already written its frames one by one; only a single-still capture writes here.
                if (!_options.HasFlight && _options.ScreenshotPath is { } screenshotPath)
                {
                    Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
                    _device.CaptureFrame(_window.Width, _window.Height, capture);
                    BitmapWriter.Write(screenshotPath, _window.Width, _window.Height, capture);
                    _logger.Information("Desktop", $"Wrote {_window.Width}x{_window.Height} capture to {screenshotPath}.");
                }
                else if (_options.HasFlight)
                {
                    _logger.Information(
                        "Desktop",
                        $"Wrote {flightFrame} flight frame(s) at {_window.Width}x{_window.Height} beside {_options.ScreenshotPath}.");
                }

                _soldiers?.LogAiSummary();
                running = false;
            }

        }

        if (_options.BenchmarkPath is { } benchmarkPath)
        {
            IReadOnlyList<RenderStageTiming> gpuStages = _device.StageTimings();
            FramePerformanceStatistics frame = framePerformance.Statistics();
            GpuFrameTiming gpu = _device.GpuTiming();
            RenderPerformanceSnapshot snapshot = new(
                GraphicsPresetRegistry.Default.Current.ToString(),
                _window.Width,
                _window.Height,
                _device.VSyncEnabled,
                frame.FramesPerSecond,
                frame.AverageMilliseconds,
                frame.OnePercentLowFramesPerSecond,
                frame.P95Milliseconds,
                frame.PeakMilliseconds,
                frame.SampleCount,
                gpu.Milliseconds,
                gpu.P95Milliseconds,
                gpuStages.ToArray());

            string? benchmarkDirectory = Path.GetDirectoryName(Path.GetFullPath(benchmarkPath));
            if (!string.IsNullOrEmpty(benchmarkDirectory))
            {
                Directory.CreateDirectory(benchmarkDirectory);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(snapshot, BenchmarkJsonOptions);
            File.WriteAllText(benchmarkPath, json);
            _logger.Information("Perf", $"Wrote benchmark snapshot to {Path.GetFullPath(benchmarkPath)}.");
        }

        // The measurement, written down. The overlay answers "is it fast right now" for somebody watching
        // it; this is what a capture run leaves behind, and it is the only form the acceptance criterion
        // can actually be checked in -- nobody reads an overlay in a thirty-frame headless capture.
        if (RenderCVars.PerformanceLog.Value)
        {
            IReadOnlyList<RenderStageTiming> finalTimings = _device.StageTimings();

            if (finalTimings.Count == 0)
            {
                _logger.Information("Perf", "No stage timings were collected; the run was too short to resolve a query.");
            }
            else
            {
                int over = 0;

                foreach (RenderStageTiming timing in finalTimings)
                {
                    if (!timing.Active)
                    {
                        _logger.Information("Perf", $"{timing.Stage,-28} off");
                        continue;
                    }

                    double? budget = RenderBudget.For(timing.Stage);
                    bool exceeded = budget is { } allowed && timing.Milliseconds > allowed;
                    over += exceeded ? 1 : 0;

                    string against = budget is { } allowance
                        ? FormattableString.Invariant($"  budget {allowance:0.0} ms{(exceeded ? "  OVER" : string.Empty)}")
                        : "  (unbudgeted)";

                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant(
                            $"{timing.Stage,-28} avg {timing.Milliseconds,6:0.000} ms  p95 {timing.P95Milliseconds,6:0.000} ms  peak {timing.PeakMilliseconds,6:0.000} ms{against}"));
                }

                GpuFrameTiming gpu = _device.GpuTiming();
                _logger.Information(
                    "Perf",
                    FormattableString.Invariant(
                        $"GPU total avg {gpu.Milliseconds:0.000} ms  p95 {gpu.P95Milliseconds:0.000} ms  peak {gpu.PeakMilliseconds:0.000} ms of the {RenderBudget.FrameMilliseconds:0.00} ms a 120 Hz frame has; {over} stage(s) over budget."));
            }
        }

        if (!_options.IsBenchmark)
        {
            DesktopConfiguration.Save(_configuration, _logger);
            console.SaveSettings();
        }
        missionWarmer?.Dispose();
        previewLoader?.Dispose();
        if (_dissolveTexture.IsValid)
        {
            _device.DestroyTexture(_dissolveTexture);
        }

        shellBodyFont?.Dispose();
        shellHeadingFont?.Dispose();
        _logger.Information(
            "Desktop",
            $"Shutting down after {_loop.Clock.TickCount} ticks and {_loop.Clock.FrameCount} frames.");

        return 0;
    }

    private void ApplyWeatherSettings()
    {
        _rain?.ApplySettings(
            RenderCVars.WeatherEnabled.Value,
            RenderCVars.WeatherStyle.Value,
            RenderCVars.WeatherSpeed.Value);
        _snow?.ApplySettings(
            RenderCVars.WeatherEnabled.Value,
            RenderCVars.WeatherStyle.Value,
            RenderCVars.WeatherSpeed.Value);
    }

    // The camera's projection, in one place: the terrain traversal needs the same field of view the
    // projection matrix is built from, or it would cull against a frustum the picture does not have.
    // The far plane is shared with the sky, which sizes its dome from it.
    //
    // The field of view is the original's, not a chosen one. HumanView's constructor (0x4643A0) sets
    // its half-tangent to tan(45 degrees) = 1.0, the camera copies it to HumanCamera+96 and submits it
    // to the view (0x4D9870) -- and it is HORIZONTAL, which the terrain traversal's own
    // viewDistance/sqrt(tanX) independently confirms. So the horizontal field of view is 90 degrees and the
    // vertical one follows from the window's aspect, rather than the other way round.
    private static float VerticalFieldOfView(float aspectRatio, float horizontalTangent) =>
        2f * MathF.Atan(horizontalTangent / MathF.Max(aspectRatio, 0.0001f));

    // Whether the camera sits inside one of the current level's building shells, and so out of the rain.
    // The volumes are the per-mission shells precomputed by RainRenderer.BuildBuildingVolumes.
    private bool IsCameraInsideBuilding(Vector3d position)
    {
        foreach (RainRenderer.BuildingVolume volume in _rainBuildingVolumes)
        {
            if (volume.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Converts an authored EditCamera lens through the retail 4:3 composition aperture.</summary>
    internal static float CutsceneVerticalFieldOfView(float horizontalTangent) =>
        VerticalFieldOfView(4f / 3f, horizontalTangent);

    /// <summary>
    /// Releases the GPU-backed resources the game owns. The window, device and developer console are owned
    /// by the composition root and are disposed there; everything a level built is released here.
    /// </summary>
    public void Dispose()
    {
        _sky?.Dispose();
        _rain?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _backdropSoldiers?.Dispose();
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
        _tracers?.Dispose();
        _impactParticles?.Dispose();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _renderer?.Dispose();
        _arms?.Dispose();
        _detailMaps?.Dispose();
        _audio.Dispose();
    }
}
