using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Profiles;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
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
    // The camera's far plane -- the original's HumanView far distance. Shared with the sky, which sizes
    // its dome from it, and with the terrain traversal, which culls against the same frustum.
    private const float FarPlane = 4000f;

    // How long a load will hold for the lighting before giving up and letting the player in anyway. A
    // level that somehow never finishes tracing must not become a game that never starts.
    private const int LightingWaitMilliseconds = 30_000;

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
    private GameConfig _configuration = null!;
    private DesktopRenderScheduler _scheduler = null!;
    private GameLoop _loop = null!;
    private GiBakeCache _giCache = null!;
    private MissionRegistry _missions = null!;
    private MagicObjectRegistry _magic = null!;
    private HumanAiConfigRegistry _aiConfig = null!;
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

    // --- Global illumination, distance fields and the surface cache ---
    private IrradianceVolume? _probeField;
    private IrradianceProbeBake? _probeBake;
    private SurfaceCacheLighting? _cardLighting;
    private SurfaceCacheLookup? _cardLookup;
    private int _cardBounceFrame;
    private SurfaceCacheCapture.Result? _capturedCards;
    private SurfaceCacheInstances? _cardInstances;
    private DistanceFieldBake? _sdfBake;
    private SceneDistanceField? _sdfScene;
    private GlobalDistanceField? _sdfClipmaps;
    private bool _sdfValidated;

    // --- The loaded world and its renderers ---
    private WorldScene? _scene;
    private WorldRenderer? _world;
    private TerrainRenderer? _terrain;
    private SkyRenderer? _sky;
    private ShadowCasterPass? _shadowPass;
    private DirectionalLightSettings? _sun;
    private FreeCamera? _camera;

    // --- The player, the held weapon, the HUD and the soldiers ---
    private PlayerCamera? _player;
    private HeldWeaponRenderer? _heldWeapon;
    private StatusScreenRenderer? _hud;
    private BulletMarkRenderer? _marks;
    private LoadedMission? _loadedMissionForWeapon;
    private ViewModelRenderer? _arms;
    private SoldierRenderer? _soldiers;
    private TerrainData? _playerTerrain;
    private GameMaterialRegistry? _worldMaterials;
    private bool _playerMode;
    private ObjectCollisionIndex? _worldSolids;

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
            _device.SetViewport(new Viewport(0, 0, width, height));
            _device.Set2DProjection(width, height);
        }

        // Entry labels are keys into the language archive, so it has to be registered before the menu can
        // resolve any of them.
        _resources = new(_fileSystem, _logger);

        // menusystem.res holds the menu's own labels; missions.res holds the mission names and briefings,
        // because a descriptor's "Mission 1 Name" is a key into it rather than the title itself.
        foreach (string archive in new[] { MenuStringTable.GetArchiveName(), MenuStringTable.GetMissionArchiveName() })
        {
            if (_fileSystem.Exists(archive))
            {
                _resources.Register(archive, _fileSystem.ReadAllBytes(archive));
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

        // Set before anything uploads a texture: mip generation happens at upload, so a level loaded
        // before this would keep the unfiltered tiles it started with.
        _device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);
        RenderCVars.Anisotropy.Changed += (_, _) =>
            _device.SetTextureQuality(RenderCVars.Anisotropy.Value, RenderCVars.GenerateMipmaps.Value);

        _device.VSyncEnabled = RenderCVars.VSync.Value;
        RenderCVars.VSync.Changed += (_, _) => _device.VSyncEnabled = RenderCVars.VSync.Value;

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
        _loop = new(time: lockstepClock, host: menuHost, renderer: _scheduler, logger: _logger);

        // Baked global illumination: a mission's probe field is loaded from the shipped cache (Assets/gi)
        // when present, else traced and written to the per-user cache so the next load is fast. This is what
        // turns a level load from a few-seconds lighting trace into an instant read -- see GiBakeCache.
        _giCache = GiBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);

        // The mission table is built the way the original builds it: walk LOCAL:missions and run every
        // mission.qvm, each of which publishes one descriptor through DefineMission. It has to happen
        // before the Select Mission screen asks for the list.
        _missions = new(_logger);
        _missions.Register(_loop.Natives);
        GameMissionState missionState = new();
        missionState.Register(_loop.Natives);

        // The magic-object table is what turns a level's few hundred scripted objects into its full
        // contents: every model's attachments name an entry here. Loading it is a startup step, like the
        // mission scan, because a level's own scripts never mention it.
        _magic = new(_logger);

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

        try
        {
            _missions.Scan(_fileSystem, _resources, _loop.Natives);
            _magic.Load(_resources, _loop.Natives);
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

        // The player's maximum health is humanplayer.qvm's ARGUMENT 7 = 3.0, written over Human+600 by the
        // constructor (0x410370 at 0x4104E1) AFTER 0x489C30 set the universal 1.0 -- the player is worth
        // three soldiers. Measured live off the retail game: [phit] max=3.000, dead at exactly 3.0
        // accumulated. The value is refreshed from the parsed script when the mission loads.
        _playerMaximumHealth = 3f;
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
        int temporalFrame = 0;

        // The player eye's last two TICK positions, for drawing the walk between them. Orientation is
        // already per-frame (mouse-look runs on every loop pass); it is the POSITION that stepped at
        // 30 Hz, which read as the whole world juddering whenever the player moved.
        Vector3d playerEyePrevTick = default;
        Vector3d playerEyeTick = default;
        bool playerEyeCaptured = false;
        _probeField = null;

        // The bake in flight, while it is in flight. Non-null only between the level load starting one and
        // the render thread collecting it; a lockstep capture never sees it, because that path waits.
        _probeBake = null;

        // When the probe field landed, in the loop's own milliseconds, or -1 for "no fade owed". The field
        // is traced on a worker and arrives several seconds into a level; until it does, every surface is
        // lit by one flat ambient colour, and swapping the two in a single frame relights the entire world
        // at once. That reads as a fault rather than as a load finishing.
        //
        // A lockstep capture never fades: it waits for the field at load, so there is nothing to blend from
        // and nothing time-dependent for a reference image to disagree about.
        long probeArrivedAt = -1;

        // How long the field takes to replace the ambient it stands in for. Long enough that the eye reads
        // it as light settling rather than as a cut, short enough that nobody waits for it -- and well
        // under the several seconds the bake itself takes, so the two do not read as one long event.
        const float ProbeFadeMilliseconds = 600f;


        // The level's signed distance fields, and the clipmaps that resample them around the camera. Baked
        // on a worker for the same reason the probe field is; the clipmaps themselves are GL and are built
        // on the render thread once the bake lands.
        _cardLighting = null;
        _cardLookup = null;
        bool cardLightingDumped = false;

        // How many times the bounce has been re-lit this mission. Zero converges in one shot; each relight
        // after that accumulates over frames. Reset when a mission loads.
        _cardBounceFrame = 0;

        // One screen-space indirect dump per run, to verify the gather reads the G-buffer and reconstructs
        // world positions correctly before it feeds the frame.
        bool screenGiDumped = false;

        // The live screen probe gather's atlases, sized to the frame divided by the probe step and reused
        // across frames. Ping-ponged: each frame reads one pair and writes the other, so the accumulation
        // reprojects onto its own past. giAccum holds the accumulated irradiance the deferred lighting reads;
        // giPosition holds the world position each probe belongs to, for the reprojection's disocclusion test.
        // Grown when the window changes, which drops the history with it.
        TextureHandle[] giAccum = [default, default];
        TextureHandle[] giPosition = [default, default];
        TextureHandle giFiltered = default;
        int giIndirectWidth = 0;
        int giIndirectHeight = 0;
        int giFrame = 0;
        bool giHasHistory = false;
        Matrix3f giPrevWorldToView = Matrix3f.Identity;
        Vector3f giPrevCameraToCache = default;
        _capturedCards = null;
        _cardInstances = null;
        _sdfBake = null;
        _sdfScene = null;
        _sdfClipmaps = null;
        _sdfValidated = false;
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
        _detailMaps = new(_device, RenderCVars.MaterialPack.Value, _logger);


        RenderCVars.ColourLookupTable.Changed += (_, _) => ApplyColourLookup(_options.StartMission);

        // The level's own sun, read from its Dirlight task -- section 12.
        // Null until a level is loaded, and for a level that declares none.
        _sun = null;
        _camera = null;

        // The walking player. F1 swaps between it and the free camera: the camera is how a level is
        // inspected, the player is how it is stood in. Both draw through the same Camera3D.
        _player = null;
        _heldWeapon = null;
        _hud = null;
        _marks = null;
        _loadedMissionForWeapon = null;
        _arms = null;
        _soldiers = null;
        _playerTerrain = null;

        // The material table, shared with the guard-round handler so an AI round's impact can leave the
        // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
        _worldMaterials = null;
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
            menu.Manager.ExternalNatives = missionState.TryInvokeFromMenu;
            menu.Manager.Initialize(menu, menuHost, _logger);
        };

        // R11: the cinematic shell (OpenIGI.Frontend) is the default front end; -vanilla and -vanillamenu
        // reach the byte-for-byte retail menus instead. -mission still skips the menus straight into a level.
        Shell? shell = null;
        _showcaseCamera = null;
        MissionWarmer? missionWarmer = null;
        _menuCameras = MenuCameraLibrary.Empty;
        UiFont? shellBodyFont = null;
        UiFont? shellHeadingFont = null;
        bool menuMode = false;
        bool showcasePending = false;
        const int ShowcaseMissionId = 1;
        int currentShowcaseMissionId = ShowcaseMissionId;
        int? pendingPreviewMissionId = null;
        int previewRequestedAtMs = 0;


        if (_options.BakeAll)
        {
            // The bake sweep runs after the window is shown (the probe wait pumps the message queue); the
            // shell and menus are skipped entirely.
        }
        else if (_options.StartMission > 0)
        {
            StartMission(_options.StartMission);
        }
        else if (_options.VanillaMenu)
        {
            _loop.Flow.Request(FlowEvent.MainMenu);
        }
        else
        {
            _menuCameras = MenuCameraLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);
            missionWarmer = new MissionWarmer(_gamePath, _missions, _logger);

            // Saira for body, Saira Condensed as a distance field for the headings the shell sets large.
            shellBodyFont = UiFontLibrary.Load(_device, UiFontLibrary.Body, 48f);
            shellHeadingFont = UiFontLibrary.Load(_device, UiFontLibrary.Heading, 64f, FontAtlasKind.SignedDistanceField);
            var shellTheme = new UiTheme(shellBodyFont, shellHeadingFont);

            // The real DefineMission data, resolved to display strings through the language table.
            var shellMissions = new List<MissionInfo>();
            int missionNumber = 1;
            foreach (MissionDescriptor descriptor in _missions.Missions)
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

                    menuMode = false;
                    StartMission(id);
                },
                quit: () => running = false,
                previewMission: id =>
                {
                    int wanted = id ?? ShowcaseMissionId;
                    if (wanted != currentShowcaseMissionId)
                    {
                        // Warm the level's files on a worker now, and queue the visible swap for once the
                        // selection has settled (see the loop) so scrubbing the carousel does not reload
                        // every step.
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
                captureThumbnail: (out captureWidth, out captureHeight) =>
                {
                    captureWidth = _window.Width;
                    captureHeight = _window.Height;
                    var pixels = new Bgra32[captureWidth * captureHeight];
                    _device.CaptureFrame(captureWidth, captureHeight, pixels);
                    return pixels;
                });

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

            // The showcase level is loaded a couple of frames into the loop, not here: loading it now would
            // block for a few seconds before the window ever showed the menu (the "blue screen" that is just
            // the frame's clear). Deferring it lets the menu draw first over a dark backdrop, then the
            // showcase streams in behind it.
            menuMode = true;
            showcasePending = true;

            // Heavy depth of field behind the UI, on the deferred path the shell's preset leaves on.
            RenderCVars.FocalDistance.Value = 40f;
            RenderCVars.FocalRange.Value = 18f;
            RenderCVars.FocalBlur.Value = 1f;
        }

        _window.CloseRequested += (_, _) => running = false;
        _window.Show();
        _logger.Information("Desktop", $"{_platform.Name} up on {_device.Capabilities.Renderer}.");

        // -bakeall: trace every mission's probe field into the cache, then quit. Each StartMission loads the
        // level and writes its .giv (the load path caches a fresh bake); the window is up so the probe wait
        // can pump. Run once and ship the results.
        if (_options.BakeAll)
        {
            IReadOnlyList<MissionDescriptor> toBake = _missions.Missions;
            _logger.Information("Bake", $"Baking global illumination for {toBake.Count} mission(s).");
            int baked = 0;
            foreach (MissionDescriptor descriptor in toBake)
            {
                if (_window.IsClosing)
                {
                    break;
                }

                _logger.Information("Bake", $"[{++baked}/{toBake.Count}] mission {descriptor.Id} '{strings.Get(descriptor.Name)}'.");
                StartMission(descriptor.Id);
            }

            _logger.Information(
                "Bake",
                $"Baked {baked} mission(s). To ship them, copy the .giv files from "
                    + $"{Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi")} into apps/OpenIGI.Desktop/Assets/gi.");
            running = false;
        }

        // Proved once, here, rather than discovered several stages later as a wrong picture. Everything
        // from the distance fields onwards is compute, and none of it draws anything until a good deal of
        // it is in place -- so a driver that mis-binds a buffer or ignores a barrier would first show up
        // as lighting that looks subtly wrong, a very long way from its cause.
        if (GLComputeSelfTest.Run(_device, _logger))
        {
            // Compiled here rather than when a level first needs it, which is a long way into a run. A
            // shader that does not compile would surface there as a stage that silently did nothing.
            GlobalDistanceField.ValidateShader(_device, _logger);
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
            _window.PumpEvents();

            // The probe bake, if one is still tracing. Collected HERE rather than on the worker because
            // what comes back becomes a GL texture, and the context belongs to this thread. TryCollect
            // answers true once, so this costs a completed-flag read per frame until then and nothing after.
            if (_probeBake is { } pendingBake && pendingBake.TryCollect(out IrradianceVolume? bakedField))
            {
                _probeField = bakedField;
                _probeBake = null;
                probeArrivedAt = _loop.TimeSource.Milliseconds;
                _device.SetIrradianceVolume(_probeField);
            }

            // Same handover, same reason: the atlas becomes a GL volume, and the context belongs here.
            if (_sdfBake is { } pendingField && pendingField.TryCollect(out DistanceFieldBake.Result baked))
            {
                _sdfBake = null;
                _sdfScene = baked.Scene;
                _sdfClipmaps = GlobalDistanceField.Create(
                    _device,
                    baked.Scene,
                    baked.Atlas,
                    GlobalDistanceFieldSettings.Default,
                    _logger);
            }

            // One tick's worth of synthetic time per RENDERED FRAME, from an absolute target rather than
            // an accumulated step so the schedule cannot drift. Per loop pass would be wrong: the loop
            // pumps many times per frame, so the clock would jump a whole tick each pump and the catch-up
            // cap would fire every pass -- measured at 200 ticks for 20 frames, a world running ten times
            // too fast. Deterministic either way, but a capture should show the moment it claims to.
            if (lockstepClock is not null)
            {
                lockstepClock.Milliseconds = (int)(((_loop.Clock.FrameCount + 1L) * 1000L) / GameClock.DefaultTickRate);

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
            }

            // The deferred showcase load: after a couple of menu frames have shown, load the level behind the
            // menu. Blocks briefly, but the menu is already on screen (over its dark backdrop) rather than a
            // blank blue clear.
            if (menuMode && showcasePending && _loop.Clock.FrameCount >= 2)
            {
                showcasePending = false;
                StartMission(ShowcaseMissionId);
                _playerMode = false;
                ApplyShowcaseCamera(ShowcaseMissionId);
            }

            // The queued preview swap: once the campaign selection has settled for a moment, load that mission
            // behind the menu so the showcase actually changes. Synchronous — it hitches briefly, softened by
            // the worker warm-up — because the renderer keeps one live world; a second one is out of scope.
            if (menuMode
                && pendingPreviewMissionId is { } previewMissionId
                && _loop.TimeSource.Milliseconds - previewRequestedAtMs > 350)
            {
                pendingPreviewMissionId = null;
                currentShowcaseMissionId = previewMissionId;
                StartMission(previewMissionId);
                _playerMode = false;
                ApplyShowcaseCamera(previewMissionId);
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
                        // A placement is a teleport; the eye pair must not be interpolated across it.
                        playerEyeCaptured = false;
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

                if (_playerDead && _deathCamera.IsActive)
                {
                    // Dead: the mouse orbits the death camera around the body rather than turning a head that
                    // is no longer taking input -- HumanCamera_DeathCameraControlInput. The cursor stays locked
                    // so the orbit keeps turning however far the mouse is moved.
                    _deathCamera.Control(rawX, rawY);
                    _window.CursorLocked = !captureMode;
                }
                else if (_player is { IsOnLadder: true })
                {
                    // On a ladder the mouse orbits the third-person interaction camera around the climbing
                    // body, as the original's mode-3 camera does -- HumanCamera_ThirdPersonControlInput -- so
                    // you can look around while you climb rather than being locked behind the character.
                    interactionCamera.Control(rawX, rawY);
                    _window.CursorLocked = !captureMode;
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
            _loop.Clock.UncappedRenders = menuMode || (!_options.Lockstep && RenderCVars.SmoothPresentation.Value);

            FrameOutcome outcome = _loop.RunFrame();

            // The body advances on the simulation tick and ONLY on it. Tick and Render are mutually
            // exclusive outcomes - a render is not a tick - so including Render here stepped the player
            // once per drawn frame as well, and at an uncapped frame rate that is several times 30 Hz.
            // The player then moved as fast as the display was quick, which is exactly what it looked like.
            if (outcome is FrameOutcome.Tick
                && _playerMode && _player is not null && _playerTerrain is not null)
            {
                // A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the
                // body stops being stepped rather than being stepped with no input. Without this a corpse
                // walks around.
                if (!_playerDead)
                {
                    _player.Tick(_window.Input, _playerTerrain);

                    // The eye's tick pair, for drawing frames between ticks from a position that glides
                    // instead of stepping. Captured AFTER the body moved, so the pair is this tick's and
                    // the one before's.
                    playerEyePrevTick = playerEyeCaptured ? playerEyeTick : _player.Camera.Position;
                    playerEyeTick = _player.Camera.Position;
                    playerEyeCaptured = true;
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
                _soldiers?.Tick(
                    watched,
                    enemyAlive: !_playerDead,
                    enemyFeet: watchedFeet,
                    enemyCrouching: _playerMode && _player is not null && _player.IsCrouched);

                // The player's own body advances its death fall on the tick, the same as a soldier's corpse --
                // it does nothing until the player has died and the body was posed.
                _soldiers?.TickPlayerBody();

                // On a ladder the same third-person body climbs (locomotion state 37, animation 168): it is
                // placed on the rungs and its climb clip advances while up or down is held, so the player sees
                // their character climb rather than a first-person slide. Hidden again on leaving the ladder.
                if (_player is not null && _player.IsOnLadder)
                {
                    // Rung-by-rung climbing (state 37). Input is taken only at a rung boundary, so a rung once
                    // begun plays to completion (you cannot cancel mid-step); holding up or down begins the next
                    // rung the instant the last one's clip ends -- one rung per clip cycle, alternating hands.
                    Input.KeyboardState climbKeys = _window.Input.Keyboard;
                    bool climbUp = climbKeys.IsDown(Input.KeyCode.W);
                    bool climbDown = climbKeys.IsDown(Input.KeyCode.S);
                    bool climbUse = climbKeys.IsDown(Input.KeyCode.E);

                    // On mounting: start the climb clip as one continuous loop and arm the slide only after the
                    // mount key (also E) is released, so mounting does not immediately slide.
                    if (!climbActive)
                    {
                        climbActive = true;
                        climbSlideArmed = false;
                        _soldiers?.StartPlayerClimbLoop(PlayerCamera.LadderClimbAnimation);
                    }

                    if (!climbUse)
                    {
                        climbSlideArmed = true;
                    }

                    bool climbSlide = climbUse && climbSlideArmed;

                    // Input is taken only at a rung boundary; a rung once begun runs to the clip's next wrap.
                    if (_player.LadderAtBoundary)
                    {
                        _player.DecideLadderStep(climbUp, climbDown, climbSlide);
                    }

                    if (_player.IsOnLadder && !_player.LadderAtBoundary && _soldiers is not null)
                    {
                        // Mid-rung: move the body by the climb clip's OWN root motion -- the same way a walking
                        // guard is moved -- so it sways and climbs exactly as the retail game does (read from the
                        // Ladder_ClimbTick probe). The rung commits when the clip completes a cycle.
                        _soldiers.AdvancePlayerClimbStep(_player.LadderFacing, out Vector3d rootDelta, out _);
                        if (_player.MoveLadder(rootDelta))
                        {
                            _player.CompleteLadderStep();
                        }
                    }

                    // The third-person camera takes over on mounting and follows the body up the rungs; the
                    // body is placed at this tick's feet (the clip advance above set the pose).
                    if (_player.IsOnLadder)
                    {
                        _soldiers?.PlacePlayerBodyClimbing(_player.LadderFeet, _player.LadderFacing);
                        if (!interactionCamera.IsActive)
                        {
                            interactionCamera.Begin(_player.LadderFeet, _player.LadderFacing);
                        }
                        else
                        {
                            interactionCamera.Follow(_player.LadderFeet);
                        }
                    }
                }

                if ((_player is null || !_player.IsOnLadder) && (climbActive || interactionCamera.IsActive))
                {
                    _soldiers?.HidePlayerBodyClimbing();
                    interactionCamera.Reset();
                    climbActive = false;
                }
            }

            if (outcome != FrameOutcome.Render)
            {
                continue;
            }

            if (_scene is not null && _world is not null && _camera is not null)
            {
                float aspectRatio = (float)_window.Width / Math.Max(1, _window.Height);
                float verticalFieldOfView = VerticalFieldOfView(aspectRatio);
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
                // How far this frame sits between the last two simulation ticks. Everything the
                // simulation stepped -- the player's own position, the soldiers, their poses -- is drawn
                // blended between its last two tick states by this amount, which is what makes an
                // uncapped frame rate look uncapped while the simulation stays at exactly 30 Hz.
                bool smoothPresentation = !_options.Lockstep && RenderCVars.SmoothPresentation.Value;
                float tickBlend = smoothPresentation ? _loop.Clock.TickFraction(_loop.TimeSource.Milliseconds) : 1f;

                Camera3D eye = _playerDead && _deathCamera.IsActive
                    ? _deathCamera.Camera
                    : _player is { IsOnLadder: true } && interactionCamera.IsActive
                        ? interactionCamera.Camera
                        : _playerMode && _player is not null ? _player.Camera : _camera.Camera;

                // The walking eye glides between its tick pair, and so does the recoil kick inside its
                // orientation; the mouse half of the look is this frame's own. The fly, death and ladder
                // cameras move per frame on their own and need nothing.
                if (smoothPresentation
                    && _playerMode
                    && !_playerDead
                    && playerEyeCaptured
                    && _player is { IsOnLadder: false })
                {
                    eye = new Camera3D(
                        new Vector3d(
                            playerEyePrevTick.X + ((playerEyeTick.X - playerEyePrevTick.X) * tickBlend),
                            playerEyePrevTick.Y + ((playerEyeTick.Y - playerEyePrevTick.Y) * tickBlend),
                            playerEyePrevTick.Z + ((playerEyeTick.Z - playerEyePrevTick.Z) * tickBlend)),
                        _player.SmoothedLookRotation(tickBlend));
                }

                // Behind the cinematic shell the eye is the slow menu orbit, on real elapsed time, whatever
                // camera the scene would otherwise have used.
                if (menuMode && _showcaseCamera is not null)
                {
                    int menuCameraNow = _loop.TimeSource.Milliseconds;
                    float menuCameraElapsed = Math.Clamp((menuCameraNow - lastMenuCameraMilliseconds) / 1000f, 0f, 0.1f);
                    lastMenuCameraMilliseconds = menuCameraNow;
                    eye = _showcaseCamera.Update(menuCameraElapsed);
                }

                // Everything soldier-shaped resolves its own frame state once, here, so the shadow
                // cascades and the geometry pass draw identical bodies in identical places.
                _soldiers?.BeginFrame(tickBlend, smoothPresentation);

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
                    OutputHeight: wantsTemporal && RenderCVars.RenderScale.Value < 1f ? _window.Height : 0);

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

                // The bake stored how much sky reaches each probe, never what colour it was, so the
                // mission's own sky is applied here -- and a level whose sun sits below the horizon leans
                // on its ambient exactly as the fog does.
                // The clipmaps follow the camera. Each level is rebuilt only when the camera has left it,
                // which for the outer levels is almost never -- I.G.I.'s geometry is static, so a clipmap
                // only becomes wrong by being walked out of.
                if (_sdfClipmaps is { } liveField)
                {
                    int rebuiltLevels = liveField.Update(eye.Position);

                    // Once, after the first build, and only when asked. A wrong distance field produces a
                    // picture that is merely subtly wrong several stages later, so the only way to know it
                    // is right is to compare it against the exact answer at the same points.
                    if (!_sdfValidated && RenderCVars.DistanceFieldValidate.Value && _sdfScene is { } exact)
                    {
                        _sdfValidated = true;
                        // Every level, not just the innermost. A trace falls through to the outer
                        // clipmaps as soon as it leaves the fine one, so an unbuilt level 2 is invisible to
                        // a check that only ever looked at level 0 -- and it shadows everything.
                        for (int level = 0; level < liveField.Settings.Levels; level++)
                        {
                            DistanceFieldValidation.Compare(_device, liveField, exact, level, _logger);
                        }
                    }

                    // Re-lit whenever a clipmap level moved, and only then. The clipmaps follow the camera
                    // and reach a few hundred metres, so a page on the far side of the level has no field
                    // to trace against -- re-lighting as they move is what brings each page's shadow in as
                    // the player approaches it. Doing it every frame regardless would be tens of thousands
                    // of work groups spent re-deriving an answer that had not changed.
                    // The second clause is what makes a dump deterministic. Re-lighting is otherwise driven
                    // by a clipmap having moved, which is a race against the field bake landing on its
                    // worker: the same command produced two dumps on one run and one on the next, and when
                    // it produced one the file held the UNSHADOWED load-time pass while looking exactly
                    // like the shadowed one. That is how a probe came back reporting nothing at all.
                    bool wantDump = !cardLightingDumped
                        && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 };

                    if ((rebuiltLevels > 0 || wantDump) && _cardLighting is { } litCards)
                    {
                        litCards.Light(
                            _sun is { IsBelowHorizon: false } shadowSun ? shadowSun.Direction : default,
                            _sun?.Colour ?? default,
                            _sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f),
                            liveField,
                            RenderCVars.SurfaceCacheProbe.Value);

                        // The bounce, once the direct term it gathers from is in place. Only when no probe
                        // is running: a probe writes a diagnostic into the direct atlas rather than light,
                        // and gathering from that would report on the diagnostic.
                        //
                        // The frame index converges the first relight in one shot and accumulates every one
                        // after, so a shadow that comes in as a clipmap moves settles rather than popping. A
                        // dump forces the deterministic first pass, so it is never blended.
                        if (RenderCVars.SurfaceCacheProbe.Value == 0)
                        {
                            litCards.Bounce(liveField, temporalIndex: wantDump ? 0 : _cardBounceFrame);
                            _cardBounceFrame++;
                        }

                        // Dumped after the SHADOWED pass rather than the one at load, which runs before
                        // there is any field to trace against and would show the unshadowed answer.
                        if (!cardLightingDumped
                            && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } shadowInto)
                        {
                            cardLightingDumped = true;
                            SurfaceCacheDebugDumps.CardLighting(
                                _device, litCards, shadowInto, _logger, RenderCVars.SurfaceCacheProbe.Value);
                        }
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
                // Eased rather than linear. A linear ramp starts and stops abruptly, and the eye reads the
                // stop as a second, smaller event -- which is the thing this exists to remove.
                float probeFade = probeArrivedAt < 0
                    ? 1f
                    : Math.Clamp((_loop.TimeSource.Milliseconds - probeArrivedAt) / ProbeFadeMilliseconds, 0f, 1f);
                probeFade = probeFade * probeFade * (3f - (2f * probeFade));

                _device.SetIrradianceVolumeSettings(
                    _probeField is not null && RenderCVars.GlobalIllumination.Value
                        ? new IrradianceVolumeSettings(
                            true,
                            RenderCVars.GlobalIlluminationIntensity.Value,
                            light.Ambient,
                            probeFade)
                        : IrradianceVolumeSettings.Off);

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
                if (cascadeCount > 0 && _shadowPass is not null)
                {
                    _shadowPass.Render(cascadeStorage, cascadeCount, eye, _scene, _world, _soldiers);
                }

                bool captured = RenderCVars.OffscreenScene.Value && _device.BeginSceneCapture(sceneTarget);
                if (captured)
                {
                    // The scheduler cleared the back buffer; the target it did not know about needs it too.
                    _device.Clear(_scheduler.ClearColour);

                    _device.SetLighting(new SceneLighting(
                        RenderCVars.Lighting.Value,
                        viewSunDirection,
                        light.Colour,
                        light.Ambient,
                        RenderCVars.LightScale.Value));
                    _device.BeginPass(ScenePass.Geometry);

                    // The sky is geometry as far as the G-buffer is concerned, so without this the dynamic
                    // model would multiply the dome by an ambient term and the horizon would go dark. It
                    // is not lit; it IS light.
                    _device.SetMaterialOverride(new SurfaceMaterial(Emissive: 1f));
                }

                _sky?.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);

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
                    _options.TerrainViewDistance > 0 ? _options.TerrainViewDistance : RenderCVars.TerrainDetail.Value);
                _world.Draw(_scene, eye, projection);

                // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
                // never swaps in the view-model block for them (player.md section 15.3).
                _soldiers?.Draw(eye, projection);

                // The player's own body, once it has fallen -- drawn only under the death camera, so it never
                // appears in front of the first-person view during normal play.
                _soldiers?.DrawPlayerBody(eye, projection);

                // The marks rounds have left, over the world and under the first-person rig.
                if (_marks is not null && _player is not null)
                {
                    _marks.Draw(_player.Marks, eye, projection);
                }

                // L6/L9: the live screen probe gather. Traced here, while the G-buffer the geometry pass just
                // filled is still live and BEFORE the pass closes -- because closing it runs the deferred
                // lighting, which reads the atlas as its diffuse fill. So the gather has to have written it
                // first. Only without a card probe, which fills the cache atlas with a diagnostic rather than
                // light. It replaces the ambient inside the shade rather than compositing over the frame, so
                // there is no ambient to zero and nothing runs after the lighting.
                const int giStep = 8;
                const int giMaxAccum = 32;

                // The screen trace tier: how far its rays reach across the frame and their step cap, plus the
                // surface thickness its hit test assumes. Metres, save the step count.
                const int giTraceSteps = 32;
                const float giTraceDistance = 24f;
                const float giTraceThickness = 0.5f;
                float giStrength = RenderCVars.ScreenGather.Value;
                bool giActive = captured
                    && giStrength > 0f
                    && _sdfClipmaps is not null
                    && _cardLighting is not null
                    && RenderCVars.SurfaceCacheProbe.Value == 0;

                if (giActive && _sdfClipmaps is { } liveGiField && _cardLighting is { } liveGiCards)
                {
                    int wantWidth = (_window.Width + giStep - 1) / giStep;
                    int wantHeight = (_window.Height + giStep - 1) / giStep;

                    if (giIndirectWidth != wantWidth || giIndirectHeight != wantHeight)
                    {
                        // A resize drops the history: the atlases are a different shape, so nothing in them
                        // reprojects onto the new frame.
                        for (int i = 0; i < 2; i++)
                        {
                            if (giAccum[i].IsValid)
                            {
                                _device.DestroyTexture(giAccum[i]);
                            }

                            if (giPosition[i].IsValid)
                            {
                                _device.DestroyTexture(giPosition[i]);
                            }

                            giAccum[i] = _device.CreateStorageTexture(
                                wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum");
                            giPosition[i] = _device.CreateStorageTexture(
                                wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum.pos");
                        }

                        // The spatial denoise's output, regenerated each frame from the accumulation, so a
                        // single buffer rather than a ping-pong pair.
                        if (giFiltered.IsValid)
                        {
                            _device.DestroyTexture(giFiltered);
                        }

                        giFiltered = _device.CreateStorageTexture(
                            wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.filtered");

                        giIndirectWidth = wantWidth;
                        giIndirectHeight = wantHeight;
                        giHasHistory = false;
                    }

                    Vector3f giCameraToCache = new(
                        (float)(eye.Position.X - liveGiCards.Origin.X),
                        (float)(eye.Position.Y - liveGiCards.Origin.Y),
                        (float)(eye.Position.Z - liveGiCards.Origin.Z));

                    int write = giFrame & 1;
                    int read = 1 - write;

                    liveGiCards.GatherScreenTemporal(
                        liveGiField,
                        _device.SceneDepth,
                        _device.SceneNormal,
                        DepthUnprojection.From(unjitteredProjection, 0.1f, FarPlane),
                        eye.Orientation.Transposed(),
                        giCameraToCache,
                        _window.Width,
                        _window.Height,
                        giStep,
                        giAccum[write],
                        giPosition[write],
                        giAccum[read],
                        giPosition[read],
                        giPrevWorldToView,
                        giPrevCameraToCache,
                        giFrame,
                        giMaxAccum,
                        giHasHistory,

                        // The screen trace tier (L5b): the previous frame's lit scene, read at on-screen hits
                        // before the ray falls through to the surface cache. device.SceneColour holds last
                        // frame's linear scene here -- this runs before the current frame is lit.
                        _device.SceneColour,
                        RenderCVars.ScreenGatherTrace.Value ? giTraceSteps : 0,
                        giTraceDistance,
                        giTraceThickness,

                        // The sky-light fill (L9): a ray that escapes returns the mission's sky, the same sky
                        // the surface cache lights its cards with, so the indirect that replaces the ambient
                        // carries sky light -- with its occlusion, since a ray toward geometry hits it instead.
                        _sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f));

                    // The spatial denoise (L6c), if on: smooths the accumulation into a separate atlas that
                    // the deferred fill reads, leaving the history the next frame reprojects onto unfiltered.
                    int giFilterRadius = RenderCVars.ScreenGatherFilter.Value;
                    TextureHandle giRead = giAccum[write];

                    if (giFilterRadius > 0)
                    {
                        liveGiCards.FilterScreen(
                            giAccum[write], giPosition[write], giFiltered, giIndirectWidth, giIndirectHeight, giFilterRadius);
                        giRead = giFiltered;
                    }

                    // Recorded for the deferred lighting to fold in when the pass closes, just below.
                    _device.SetScreenIndirect(giRead, giStrength);

                    // What this frame becomes to the next: its camera, so the next reprojection can find where
                    // each surface sat, and the flag that says there is now a frame to reproject onto. The
                    // world-to-view rotation is the orientation itself -- the inverse of the view-to-world the
                    // gather traced with.
                    giPrevWorldToView = eye.Orientation;
                    giPrevCameraToCache = giCameraToCache;
                    giHasHistory = true;
                    giFrame++;
                }
                else
                {
                    // The gather did not run this frame, so next frame has nothing coherent to reproject onto.
                    giHasHistory = false;
                }

                // The view model and anything translucent belong to the forward pass: it is drawn with its
                // own narrower projection, so its depth must not end up in the buffer the fog and the
                // reflections read.
                if (captured)
                {
                    _device.EndPass();
                    _device.BeginPass(ScenePass.Forward);
                }

                // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
                // then ask the human where the weapon is and draw that. Only in player mode, because the
                // original draws it out of the human's own render and the free camera is not a human -- and
                // NOT once the player is dead, when the death camera is watching the body from the outside
                // and a pair of arms hanging in front of it would be exactly wrong.
                if (_playerMode && !(_playerDead && _deathCamera.IsActive) && _player is { IsOnLadder: false, View: { } rig })
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
                    float viewModelFieldOfView = 2f * MathF.Atan(0.75f * rig.ViewModelTanX);
                    Matrix4f viewModelProjection = Matrix4f.CreatePerspective(
                        viewModelFieldOfView,
                        aspectRatio,
                        near: 0.1f,
                        far: FarPlane);

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

                    _arms?.Draw(rig, eye, viewModelProjection, rigOriginUnits, previousPoseShare, rigOrientation);

                    // Cycling weapons changes the model as well as the animations, so the renderer has to
                    // follow whatever the cycle put in hand.
                    if (_loadedMissionForWeapon is { } missionForWeapon)
                    {
                        FollowHeldWeapon(_player, _heldWeapon, missionForWeapon);
                    }

                    if (weaponAttachment is { } attachment && _heldWeapon is { IsReady: true })
                    {
                        // The weapon in hand is the one object on screen at all times, so it is the one
                        // worth giving a material to first. Steel: fully metallic, and smooth enough for
                        // the blade to catch a highlight as it moves.
                        if (captured)
                        {
                            _device.SetMaterialOverride(new SurfaceMaterial(Roughness: 0.28f, Metallic: 1f));
                        }

                        _heldWeapon.Draw(attachment, eye, viewModelProjection);

                        if (captured)
                        {
                            _device.SetMaterialOverride(SurfaceMaterial.Default);
                        }
                    }

                    // The HUD goes over everything, in 2D. StatusScreen's render sends one message to the
                    // weapon in hand and the weapon draws its own readout -- so what appears is the roster
                    // record's own SightDisplayType and AmmoDisplayType, not a choice made here.
                    if (_hud is { IsReady: true } && _player.Weapons is { } carried)
                    {
                        _device.Set2DProjection(_window.Width, _window.Height);
                        batch.Begin();
                        _hud.Draw(
                            batch,
                            carried.InHand?.Weapon,
                            carried.InHand?.Gun.RoundsInClip ?? 0,
                            carried.InHand is { } inHand ? inHand.Gun.Reserve(carried.Ammunition) : 0,
                            _window.Width,
                            _window.Height,
                            UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                        batch.End();
                    }

                    // The interaction prompt, when something is in reach — the original's "use" hint.
                    if (_hud is { IsReady: true } && _player.ShowInteractPrompt)
                    {
                        _device.Set2DProjection(_window.Width, _window.Height);
                        batch.Begin();
                        _hud.DrawPrompt(
                            batch,
                            _player.InteractPromptText,
                            _window.Width,
                            _window.Height,
                            UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                        batch.End();
                    }
                }

                // L6a slice 2: gather screen-space indirect from the surface cache and dump it, once, while
                // the G-buffer is still live inside the capture. Verifies the cross-layer bridge and the
                // depth-to-world reconstruction before the gather feeds the frame. Only when no card probe is
                // running, since that fills the cache atlas with a diagnostic rather than light.
                if (captured
                    && !screenGiDumped
                    && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } giInto
                    && _sdfClipmaps is { } giField
                    && _cardLighting is { } giCards
                    && RenderCVars.SurfaceCacheProbe.Value == 0)
                {
                    screenGiDumped = true;
                    SurfaceCacheDebugDumps.ScreenGather(
                        _device, giCards, giField, eye, unjitteredProjection, FarPlane,
                        _window.Width, _window.Height, giInto, _logger);
                }

                // Everything 2D belongs AFTER the resolve, on the back buffer: the HUD, the damage wash,
                // the menus and the console are authored in display colours and must never be tone mapped.
                // Unconditional, and outside the player-mode block on purpose -- a free-camera frame draws
                // no view model, and leaving the capture open would strand it until the next frame threw.
                if (captured)
                {
                    _device.EndPass();
                    _device.EndSceneCapture();
                    _device.ResolveScene(new PostProcessSettings(
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
                        MotionBlur: RenderCVars.MotionBlur.Value,
                        CameraTaps: RenderCVars.CameraTaps.Value,
                        SpatialAa: RenderCVars.SpatialAa.Value,
                        SpatialAaThreshold: RenderCVars.SpatialAaThreshold.Value,
                        ColourLookup: RenderCVars.ColourLookup.Value,
                        AutoExposure: RenderCVars.AutoExposure.Value));
                    captured = false;
                }

                // The damage wash goes over everything, in its own 2D pass: a screen-filling red quad whose
                // four vertex alphas are the four corners sub_411300 fades (HumanPlayer_DrawDamageFlash,
                // 0x4bb770). Outside the player-mode block on purpose -- it has to survive the player's death,
                // when the arms and the HUD are gone but the last thing the screen showed is still red.
                if (_damageFlash.IsActive)
                {
                    float flashWidth = _window.Width;
                    float flashHeight = _window.Height;
                    static Bgra32 Red(float alpha) =>
                        new(0, 0, 255, (byte)Math.Clamp((int)(alpha * 255f), 0, 255));

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

                // The F12 debug overlay and crosshair. Works in both the walking player and the free (flying)
                // camera: the state lines come from whichever is active, the crosshair readout and the reticle
                // are cast from the eye that is actually drawing.
                if (showDebug && _hud is { IsReady: true })
                {
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

                        target = I($"target: SOLDIER {botName} zone={HumanDamage.ZoneName(botZone)} feetZ={feetU.Z:F0} floorZ={floor}");
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
                    _hud.DrawDebug(batch, lines, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));

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
                }

                // r_perflog, its own overlay rather than a section of F12's: it answers a different question
                // and is read while the game is being played rather than while it is being inspected. Top
                // right, so the two can be up together.
                if (RenderCVars.PerformanceLog.Value && _hud is { IsReady: true })
                {
                    static string I(FormattableString s) => FormattableString.Invariant(s);

                    IReadOnlyList<RenderStageTiming> timings = _device.StageTimings();
                    List<string> perf = ["gpu ms   now   peak"];
                    double total = 0.0;

                    foreach (RenderStageTiming timing in timings)
                    {
                        total += timing.Milliseconds;

                        // The budget beside the measurement, because a number without the number it was
                        // supposed to beat is a number nobody acts on.
                        string budget = RenderBudget.For(timing.Stage) is { } allowed
                            ? I($" /{allowed:0.0}{(timing.Milliseconds > allowed ? " OVER" : string.Empty)}")
                            : string.Empty;

                        perf.Add(I($"{timing.Stage,-12} {timing.Milliseconds,5:0.00} {timing.PeakMilliseconds,6:0.00}{budget}"));
                    }

                    perf.Add(timings.Count > 0
                        ? I($"{"gpu total",-12} {total,5:0.00}")
                        : "measuring...");

                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    _hud.DrawDebugTopRight(
                        batch,
                        perf,
                        _window.Width - (8f * UiScale.Resolve(_options.UiScale, _window.Width, _window.Height)),
                        8f * UiScale.Resolve(_options.UiScale, _window.Width, _window.Height),
                        UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                    batch.End();
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
                            $"Drew {_terrain.LastChunkCount} chunk(s) in {_terrain.LastPassCount} material pass(es), "
                                + $"{_terrain.LastTriangleCount} triangles, from {_terrain.LastVisitedCount} "
                                + $"octree node(s) visited, over {_terrain.TileCount} ground tile(s).");
                    }
                }
            }

            // The cinematic shell draws over the resolved showcase scene, in 2D on the back buffer, before
            // the console (which goes over even the shell).
            if (menuMode && shell is not null)
            {
                _device.Set2DProjection(_window.Width, _window.Height);
                var shellPainter = new UiPainter(batch, new LayoutContext(_window.Width, _window.Height));
                batch.Begin();

                // Before the showcase level has loaded there is no scene to draw, so fill a dark backdrop
                // rather than leaving the frame's blue clear behind the menu.
                if (_scene is null)
                {
                    shellPainter.FillRect(new UiRect(0f, 0f, _window.Width, _window.Height), Bgra32.FromRgb(12, 14, 18));
                }

                shell.Draw(shellPainter);
                batch.End();
            }

            // The console goes over everything, including the menus, so it is the last thing drawn.
            if (console.IsOpen)
            {
                _device.Set2DProjection(_window.Width, _window.Height);
                batch.Begin();
                console.Draw(batch, _window.Width, _window.Height, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                batch.End();
            }

            if (_options.ExitAfterFrames > 0 && _loop.Clock.FrameCount >= _options.ExitAfterFrames)
            {
                if (_options.ScreenshotPath is { } screenshotPath)
                {
                    Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
                    _device.CaptureFrame(_window.Width, _window.Height, capture);
                    BitmapWriter.Write(screenshotPath, _window.Width, _window.Height, capture);
                    _logger.Information("Desktop", $"Wrote {_window.Width}x{_window.Height} capture to {screenshotPath}.");
                }

                _soldiers?.LogAiSummary();
                running = false;
            }
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
                double total = 0.0;
                int over = 0;

                foreach (RenderStageTiming timing in finalTimings)
                {
                    total += timing.Milliseconds;
                    double? budget = RenderBudget.For(timing.Stage);
                    bool exceeded = budget is { } allowed && timing.Milliseconds > allowed;
                    over += exceeded ? 1 : 0;

                    string against = budget is { } allowance
                        ? FormattableString.Invariant($"  budget {allowance:0.0} ms{(exceeded ? "  OVER" : string.Empty)}")
                        : "  (unbudgeted)";

                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant(
                            $"{timing.Stage,-16} {timing.Milliseconds,6:0.000} ms  peak {timing.PeakMilliseconds,6:0.000} ms{against}"));
                }

                _logger.Information(
                    "Perf",
                    FormattableString.Invariant(
                        $"GPU total {total:0.000} ms of the {RenderBudget.FrameMilliseconds:0.0} ms a 60 Hz frame has; {over} stage(s) over budget."));
            }
        }

        // A bake still tracing at this point is tracing for a level nobody will see again. The worker is a
        // thread-pool thread and cannot hold the process open, so this is not about shutting down cleanly --
        // it is about not spending every core on it while the rest of the shutdown runs.
        _probeBake?.Cancel();

        DesktopConfiguration.Save(_gamePath, _configuration, _logger);
        console.SaveSettings();
        missionWarmer?.Dispose();
        shellBodyFont?.Dispose();
        shellHeadingFont?.Dispose();
        _logger.Information(
            "Desktop",
            $"Shutting down after {_loop.Clock.TickCount} ticks and {_loop.Clock.FrameCount} frames.");

        return 0;
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
    private static float VerticalFieldOfView(float aspectRatio) =>
        2f * MathF.Atan(HumanView.DefaultTanX / MathF.Max(aspectRatio, 0.0001f));

    /// <summary>
    /// Releases the GPU-backed resources the game owns. The window, device and developer console are owned
    /// by the composition root and are disposed there; everything a level built is released here.
    /// </summary>
    public void Dispose()
    {
        _sky?.Dispose();
        _terrain?.Dispose();
        _world?.Dispose();
        _soldiers?.Dispose();
        _heldWeapon?.Dispose();
        _hud?.Dispose();
        _marks?.Dispose();
        _renderer?.Dispose();
        _arms?.Dispose();
        _cardLighting?.Dispose();
        _cardLookup?.Dispose();
        _sdfClipmaps?.Dispose();
        _detailMaps?.Dispose();
    }
}
