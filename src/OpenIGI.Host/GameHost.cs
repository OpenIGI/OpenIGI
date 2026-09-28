using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Titles;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Platform;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;

namespace OpenIGI.Host;

/// <summary>
/// The desktop composition root: it builds the world, the renderers and the developer console, drives
/// the frame loop, and tears everything down. The startup and per-frame body lives in <see cref="Run"/>;
/// mission loading, lighting and the menu shell are in the sibling partials.
/// </summary>
internal sealed partial class GameHost : IDisposable
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
    private Func<IIgiLogger, IMissionCatalogue<MissionDescriptor>> _missionCatalogue;
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
    private IIgiLogger _logger;

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

    // Silent until the console has read settings.cfg, so nothing can open a device on stale defaults.
    private IAudioDevice _audio = new NullAudioDevice();

    // The archives that belong to the session rather than to a level, kept as bytes so a preview built on
    // a worker can register them into its own registry without reading through this one.
    private readonly Dictionary<string, byte[]> _sessionArchives = [];

    // What a menu backdrop needs to become the played mission: the session's clock, flow host, render
    // scheduler and archives. See MissionPreviewContext.
    private MissionPreviewContext _previewContext = null!;
    private GameConfig _configuration = null!;
    private HostRenderScheduler _scheduler = null!;

    // The one wall clock every loop reads. A mission promoted out of the menu's backdrop brings its own
    // GameLoop with it, and both loops have to measure milliseconds from the same origin — see
    // GameClock.AdoptScheduleFrom.
    private ITimeSource _timeSource = null!;
    private GameLoop _loop = null!;
    private GiBakeCache _giCache = null!;
    private DistanceFieldCache _sdfCache = null!;
    private LightmapBakeCache _lightmapCache = null!;
    // The title's own, not this game's. DefineMission takes eleven arguments in one and fourteen in the
    // other, and only one of them names the mission that follows -- so the reader and the campaign order
    // are both the title's to supply.
    private IMissionCatalogue<MissionDescriptor> _missions = null!;
    private MagicObjectRegistry? _magic;
    private VehiclePhysicsRegistry? _vehiclePhysics;
    private HumanAiConfigRegistry? _aiConfig;
    private AnimTriggerRegistry? _animationTriggers;
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
    private int _playerDifficulty;
    private float _playerMaximumHealth;
    private float _playerDeltaTranslationScale = HumanMotion.DefaultDeltaTranslationScale;
    private float _electricFenceDose = ElectricFenceContact.DefaultDamageFactor;
    private HumanHealth _playerHealth = null!;
    private bool _playerDead;
    private HumanDamageFlash _damageFlash = null!;
    private FlashbangScreenFlash _flashbangScreenFlash = null!;
    private DeathCamera _deathCamera = null!;

    /// <summary>The over-the-shoulder view and the move between it and the player's own eyes.</summary>
    /// <remarks>
    /// Owned here rather than by <see cref="PlayerCamera"/> because it changes nothing the player camera
    /// is responsible for: the body still walks, aims and fires from exactly where it did, and this only
    /// decides where the frame is rasterised from. See <see cref="ThirdPersonCamera"/>.
    /// </remarks>
    private readonly ThirdPersonCamera _thirdPerson = new();

    /// <summary>The wall clock the third-person move is advanced on; it is a camera move, not a tick.</summary>
    private int _thirdPersonMilliseconds;

    /// <summary>The pull-in ray, kept so a frame does not allocate one; rebuilt when the level's does.</summary>
    private DeathCameraVantage.Blocked? _thirdPersonWalls;
    private ObjectCollisionIndex? _thirdPersonWallsSolids;
    private TerrainData? _thirdPersonWallsTerrain;


    private int _guardRounds;
    private int _guardHits;
    private Random _shotRandom = null!;

    /// <summary>The engine's own generator, for the sound-variant draws <c>0x4638A0</c> makes.</summary>
    private readonly IgiRandom _soundRandom = new();

    /// <summary>The tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    private int _playerHitQuietUntil;
    private int _impactSoundQuietUntil;
    private int _ricochetSoundQuietUntil;
    private int _tracerSkipsLogged;

    private AlarmSpeakerRegistry? _alarmSpeakers;
    private DestructibleObjectRegistry? _destructibles;
    private readonly Dictionary<AlarmSpeaker, AudioVoiceHandle> _sirenVoices = [];
    private readonly HashSet<AlarmSpeaker> _reportedSirenFailures = [];
    private SecurityCameraRegistry? _securityCameras;
    private IPlacementSpin? _spins;
    private readonly Dictionary<SecurityCamera, (string Sound, AudioVoiceHandle Voice)> _cameraBeepVoices = [];
    private readonly Dictionary<SecurityCamera, AudioVoiceHandle> _cameraMoveVoices = [];
    private RotatingObjectRegistry? _rotatingObjects;

    // --- Global illumination, distance fields and the surface cache ---
    private IrradianceVolume? _probeField;
    private IrradianceProbeBake? _probeBake;

    // The newest bounce pass the display is easing towards. Held apart from _probeField (what is on the
    // GPU right now) so a pass does not snap in: a bounce adds a chunk of indirect light, and eight snaps
    // read as the room stepping brighter eight times. _probeField chases this instead.
    private IrradianceVolume? _probeTarget;

    // The cache key an in-flight interactive bake is written under once it finishes -- remembered here
    // because the load no longer blocks for the field, so the save moves to the frame the bake completes.
    // The cast is kept too, so the live re-bake (F11) can trace the field again from the same inputs.
    private int _probeBakeMission;
    private float _probeBakeSpacing;
    private Vector3f _probeBakeSun;
    private WorldRayCast? _bakeCast;

    // The path-traced object-lightmap bake in flight, and the inputs its result is cached under once it
    // lands -- remembered because the load no longer blocks for it, so the save moves to the frame it
    // completes, exactly as the probe field's does.
    private LightmapBake? _lightmapBake;
    private int _lightmapBakeMission;
    private Vector3f _lightmapBakeSun;
    private Vector3f _lightmapBakeSunColour;
    private Vector3f _lightmapBakeSky;

    // One albedo for the whole level, shared by the load-time trace and the live re-bake. IGI's palette is
    // concrete, snow and dirt, so a warm grey stands in for reading each hit's own texture.
    private static readonly Vector3f ProbeBakeAlbedo = new(0.42f, 0.40f, 0.37f);

    // How the displayed field eases towards the newest bounce pass. The rate is an exponential approach in
    // reciprocal seconds -- at 2.0 the field covers ~86% of the gap in one second, which settles well
    // before the next pass (~5 s apart) yet reads as a smooth fill rather than a step. The settle cut-off
    // is the largest per-frame coefficient move below which the ease is treated as arrived and stops
    // re-uploading, so a converging tail does not upload the volume forever.
    private const float ProbeChaseRate = 2.0f;
    private const float ProbeChaseSettle = 0.001f;
    private SurfaceCacheLighting? _cardLighting;
    private SurfaceCacheLookup? _cardLookup;

    // The level's inferred lamp fixtures, kept so the frame loop can submit them to the deferred lighting
    // as real point lights each frame (transformed into view space there), the honest way to light an
    // interior the screen gather cannot reach. Also bounced through the surface cache. Empty until a
    // mission loads one; see EmissiveLightProposer.
    private IReadOnlyList<LightFixture> _lightFixtures = [];

    // Which sub-meshes of this level are lamps, so a shared texture glows on the fixture and not on the
    // furniture that happens to be skinned with the same page. Held here because the world renderer is
    // rebuilt per mission and this outlives the rebuild.
    private LampDetector.Lamps _lamps = LampDetector.Lamps.None;

    // What the level says about its own light, for a title that authors that rather than a sky. Null for
    // Project I.G.I., which keeps the sweep. Set by ApplyLevelConstants, so the menu path gets it too.
    private DeferredLighting? _deferredLighting;

    // The level's sun as the DAY/NIGHT CYCLE has to see it, which is not always as the level wrote it.
    // See BindToCycle.
    private DirectionalLightSettings? _cycleSun;



    // What a person has said outright about which models are lamps, which beats anything the detector
    // works out. One file for the whole game; see Assets/lamps.json.
    private LampManifest _lampManifest = LampManifest.Empty;

    // Which of those lamps are actually lit at this moment, and how far through switching on or out they
    // are. Built with the fixtures, and read by all three of the things a lamp is -- the punctual light
    // below, the emission the world renderer puts on the fixture's own geometry, and the emitters the
    // surface cache bounces off -- so none of them can disagree about a lamp being off. Null until a
    // mission loads fixtures; see LampSwitchboard.
    private LampSwitchboard? _lampSwitchboard;

    // When the surface cache's emitter buffer last matched the switchboard. The bounce is a GPU upload
    // rather than a uniform, so it follows a flickering lamp a few frames behind rather than every frame;
    // the cache's own accumulation is slower than that anyway.
    private int _lampBounceRevision = -1;
    private int _lampMilliseconds;

    // Reused each frame to hand the view-space lamp lights to the device without allocating, and without a
    // stackalloc inside the frame loop. Sized to the shader's cap; only the first N entries are ever sent.
    private readonly DeferredLocalLight[] _localLightScratch = new DeferredLocalLight[DeferredLocalLight.Maximum];

    // The same lamps in the storm's frame -- metres from the eye, world axes -- for the drops falling
    // through their light; how many of them this frame has.
    private readonly StormLamp[] _stormLamps = new StormLamp[DeferredLocalLight.Maximum];
    private int _stormLampCount;

    // Which fixtures this frame submits, and how far outside its own reach the camera stands for each.
    // Held rather than allocated per frame: this runs on every frame of every level that has lamps.
    private readonly int[] _localLightOrder = new int[DeferredLocalLight.Maximum];
    private readonly float[] _localLightRank = new float[DeferredLocalLight.Maximum];
    private int _cardBounceFrame;

    // Stand-down state for the surface-cache retrace. The world-space relight and the amortised bounce
    // re-derive the answer the cache already holds whenever nothing has moved -- and "moved" for a
    // VIEW-INDEPENDENT cache means the distance field scrolled new coverage in (a real walk of metres) or
    // the sun was scrubbed, NOT the camera turning or drifting a few centimetres. Once the cache has settled
    // after the last such change the retrace idles, which is what takes out the periodic multi-millisecond
    // spike that halved the frame rate at rest. It resumes the instant a level scrolls or the sun moves.
    private int _giStaticFrames;
    private bool _giSunValid;
    private Vector3f _lastGiSunDirection;
    private Vector3f _lastGiSunColour;

    // The shortest the retrace keeps running after the last change before it idles. The real window is
    // worked out per frame from the amortisation spread and the temporal frames -- see where retraceGi is
    // decided -- so the cache has converged before it stops.
    private const int GiSettleFrames = 64;

    // The distance field's per-level sizes and ranges, reused frame to frame for LampFieldFor.
    private readonly float[] _lampFieldVoxels = new float[4];
    private readonly float[] _lampFieldRanges = new float[4];

    /// <summary>The level's distance field as a lamp-shadow renderer reads it, or null while there is none.</summary>
    private LampOcclusionField? LampFieldFor(GlobalDistanceField? field)
    {
        if (field is null || !field.Clipmaps.IsValid)
        {
            return null;
        }

        GlobalDistanceFieldSettings settings = field.Settings;
        int levels = Math.Clamp(settings.Levels, 0, Math.Min(4, field.Origins.Count));
        if (levels <= 0)
        {
            return null;
        }

        for (int level = 0; level < 4; level++)
        {
            int from = Math.Min(level, levels - 1);
            _lampFieldVoxels[level] = settings.VoxelSizeOf(from);
            _lampFieldRanges[level] = field.RangeOfLevel(from);
        }

        return new LampOcclusionField(
            field.Clipmaps,
            levels,
            settings.Resolution,
            field.Origins[0],
            field.Origins[Math.Min(1, levels - 1)],
            field.Origins[Math.Min(2, levels - 1)],
            field.Origins[Math.Min(3, levels - 1)],
            _lampFieldVoxels,
            _lampFieldRanges);
    }

    // How far the eye may move between two frames and still count as the same shot for the screen GI's
    // histories. Metres; a sprint covers a fraction of this in a frame, a cut or a teleport covers more.
    private const double GiCutDistance = 2.0;

    // Frames left in the exposure warm-up: for a moment after a level's GI starts converging, the adapted
    // exposure eases fast so it tracks the light filling in rather than over-lifting the dark first frames
    // and then walking visibly back down. Reset when the surface cache is (re)built, counted down per frame.
    private int _giExposureWarmup;

    // How long that warm-up lasts. The radiance cache's running average settles over maxFrames sweeps and
    // one sweep is spread over several frames, so full convergence is a couple of seconds; this covers it.
    private const int GiExposureWarmupFrames = 150;

    // The exposure adaptation rate during the warm-up and in steady play. Fast while the GI converges so the
    // exposure follows it and the picture holds its brightness; slow after, for an unobtrusive eye-adaptation.
    private const float GiExposureWarmupRate = 0.35f;
    private const float ExposureSteadyRate = 0.04f;
    private SurfaceCacheCapture.Result? _capturedCards;
    private SurfaceCacheInstances? _cardInstances;
    private DistanceFieldBake? _sdfBake;
    private SceneDistanceField? _sdfScene;
    private GlobalDistanceField? _sdfClipmaps;
    private bool _sdfValidated;

    // The last frame of the outgoing backdrop, held on the GPU so it can be dissolved off over the
    // incoming one. Sized to the window and re-made when that changes.
    private TextureHandle _dissolveTexture;
    private (int Width, int Height) _dissolveSize;

    // The menu backdrop currently on screen, if one is. Held rather than used: the renderers built from it
    // read models, textures and the sky through ITS resource registry and file system, so it has to outlive
    // the handover. Replaced whole when the next backdrop arrives.
    private MissionPreview? _backdrop;

    // --- The loaded world and its renderers ---
    private Camera3D _inspectorEye = Camera3D.Default;
    private float _inspectorTangent = 1f;
    private float _inspectorAspect = 16f / 9f;
    private WorldScene? _scene;
    private IWorldPresenter? _world;
    // The air the running level is played in, when the title authors one. Null for a game that does
    // not, which is what keeps Project I.G.I. out of this entirely.
    private SceneAtmosphere? _atmosphere;

    /// <summary>Gets the air the world wears when the retail fog is on, preferring the level's own.</summary>
    /// <param name="fromGlobalLight">The fog this band of Project I.G.I.'s <c>GlobalLight</c> publishes.</param>
    /// <returns>The fog to draw into.</returns>
    /// <remarks>
    /// <para>
    /// <c>GlobalLight</c> is the FIRST game's task, and it is the only thing the frame used to ask. So
    /// on the vanilla tier -- the one tier that fogs through <see cref="SceneFog"/> rather than through
    /// the froxel volume -- I.G.I. 2's world was drawn with no fog at all: its own authored air was set
    /// once when the mission loaded and then overwritten by an empty <c>GlobalLight</c> on the first
    /// frame, and every frame after.
    /// </para>
    /// <para>
    /// It also hid the ground's per-pass fog colour, which had nothing to tint. That is worth saying
    /// because the two faults look identical from the outside -- far hills that are not hazed -- and
    /// fixing either alone leaves the other in place.
    /// </para>
    /// <para>
    /// Project I.G.I. cannot reach the new branch: <c>ITitlePresentationProvider.ReadAtmosphere</c>
    /// defaults to null and only the sequel answers it, so <see cref="_atmosphere"/> is null for the
    /// first game and this returns exactly what the frame asked for before.
    /// </para>
    /// </remarks>
    private SceneFog RetailAir(SceneFog fromGlobalLight) =>
        _atmosphere is { FogDensity: > 0f } authored
            ? new SceneFog(authored.FogColour, authored.FogDensity)
            : fromGlobalLight;

    private ITerrainPresenter? _terrain;
    private IForestPresenter? _forest;
    private IWaterPresenter? _water;
    // Behind the seam, not a named renderer: which type this is differs by title, and the host asking
    // for one game's by name is what left the other with no sky at all.
    private ISkyPresenter? _sky;

    // The level's own GlobalLight keyframe: the fog the ground and the models are drawn into, and the
    // flat light a model with no lightmap wears. Read once per level, used every frame.
    private GlobalLightSettings _globalLight = GlobalLightSettings.None;
    private RainRenderer? _rain;
    private StormRenderer? _storm;

    // The least light a rain drop is lit by, as the displayed picture sees it. Rain in an even light is as
    // bright as what is behind it and all but disappears, and a night lit only by its sky is that light;
    // the eye adapted to it still sees the rain as a veil. The drop's own grey, its opacity and the thin,
    // mostly clear streak it is printed with all come on top, so it takes this much for the rain to read:
    // chosen by eye on mission 3's night, where 2.5 barely showed, 5 reads as rain with the dome and the
    // hills clear through it, and the level's daytime fill (about 10) was a wall. See
    // StormRenderer.BuildDrops.
    private const float RainVeilDisplay = 5f;
    private SnowRenderer? _snow;

    // Where the level's roofs are, seen from above. One thing decides whether the sky can reach a point
    // -- the fog, the falling rain, its splashes and the standing water all ask this -- and it is built
    // once when the mission loads, out of the geometry itself rather than out of which tasks happen to
    // be named after buildings.
    private ShelterHeightField _shelterField = ShelterHeightField.Empty;
    private TextureHandle _shelterMap = TextureHandle.None;
    private Vector3d _stormEyeMetres;
    private ShadowCasterPass? _shadowPass;

    // The previewed mission's soldiers, built behind the menu and cast into the shadow map so the driver
    // has already met a skinned vertex layout by the time Play is pressed. Handed straight over to
    // `_soldiers` when the backdrop is promoted, so the played mission never builds them at all.
    private SoldierRenderer? _backdropSoldiers;

    // Makes the backdrop playable a couple of milliseconds at a time while the menu is idle.
    private MissionArming? _arming;

    // The shot that carries the player out of the menu and into their own eyes.
    private readonly MissionEntryCamera _entry = new();

    // Which row -backdropcycle is on, so each is asked for once rather than on every frame past its mark.
    private long _backdropCycleRow = -1;

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
    private DirectionalLightSettings? _sun;

    // The interactive day/night cycle, and the sun it produces for this frame -- the authored sun swept to
    // the current time of day, which everything downstream (shadows, deferred, the surface cache and its
    // radiance cache, the sky fill) lights from in place of the raw authored one. Recomputed each rendered
    // frame; scrubbed on real elapsed time so holding a key sweeps smoothly whatever the frame rate.
    private readonly DayNightCycle _dayNight = new();

    // Chosen on the campaign screen beside the difficulty and held from there on, so a run started at
    // night stays at night through every mission after it. Session state, never archived -- see
    // MissionConditions.
    private MissionConditions _conditions = MissionConditions.Default;
    private readonly SunGizmo _sunGizmo = new();
    private DirectionalLightSettings? _effectiveSun;
    private int _dayNightMilliseconds;
    private FreeCamera? _camera;

    // --- The player, the held weapon, the HUD and the soldiers ---
    private PlayerCamera? _player;
    private IHeldWeaponPresenter? _heldWeapon;
    private IGunPickupPresenter? _gunPickupRenderer;
    private IHudPresenter? _hud;
    private IRemasteredHudPresenter? _remasteredHud;
    private DebugOverlayRenderer? _debugOverlay;
    private IMapComputerPresenter? _mapComputer;
    private MapComputerDeviceRenderer? _mapComputerDevice;
    private ITacticalComputerPresenter? _tacticalComputer;
    private StatusMessageRenderer? _messages;
    private readonly MapComputerCamera _computerFlight = new();
    private readonly BinocularOptics _binocularOptics = new();

    // The interaction marker's own wall clock: which offer is on screen and how far it has faded up.
    // Kept beside the optics for the same reason -- a fade measured in real seconds cannot live on a
    // renderer that is only asked to draw.
    private readonly InteractionPromptAnimation _interactionPrompt = new();
    private int _lastInteractionPromptMilliseconds;
    private int _lastBinocularMilliseconds;
    private int _lastMotionBlurMilliseconds;

    // The performance overlay's managed-allocation sampler. The GPU stage timers see none of this: a frame
    // that allocates megabytes churns Gen0 and, on the large-object heap, triggers the Gen2 collections that
    // stall the render thread -- a cost that shows only as a gap between the frame's wall time and the GPU's.
    private long _perfAllocBytes;
    private long _perfAllocTicks;
    private double _perfAllocRate;
    private long _benchmarkAllocStart;
    private (int, int, int) _benchmarkGcStart;
    private IComputerHighlightCatalogue? _computerHighlights;
    private MissionObjectiveRuntime? _missionObjectives;
    private CutsceneRuntime? _cutscenes;
    private MissionDescriptor? _activeMission;
    private LevelTask? _activeLevelRoot;
    private bool _computerOpen;
    private bool _computerCamFired;
    private readonly HashSet<int> _deferredCommandsFired = [];

    // -missioncycle's bookkeeping: which leg of the abandon/replay cadence has been acted on, and the
    // mission to go back into once the level has been let go of.
    private long _missionCycleLeg;
    private long _titleCycleLeg;
    private int _missionCycleMission;

    /// <summary>How much longer than the original the remaster holds a status message, in seconds.</summary>
    private const float RemasteredMessageHoldSeconds = 3f;

    /// <summary>Gets the tactical interface when the preset asks for it and it built its faces.</summary>
    /// <remarks>
    /// Read per frame rather than latched at mission bring-up, so <c>r_computer_tactical</c> can be
    /// thrown from the console between one frame and the next — which is the only practical way to
    /// compare the redesign against the original it replaces. Both renderers are therefore built for
    /// every mission; each costs an archive or a cached atlas, and neither is worth a restart.
    /// </remarks>
    private ITacticalComputerPresenter? TacticalComputer =>
        RenderCVars.TacticalComputer.Value && _tacticalComputer is { IsReady: true } ready ? ready : null;

    private bool _missionResultHandled;
    private int _missionFailureTicks;
    private bool _missionFailedMenu;
    private IBulletMarkPresenter? _marks;
    private TracerTrailRenderer? _tracers;
    private ImpactParticleRenderer? _impactParticles;
    private RetailImpactRenderer? _retailImpacts;

    // The world the blood is traced against: placed objects AND the terrain, because a droplet lands
    // on the ground as readily as on a wall and the player's own movement caster carries neither the
    // terrain nor a bullet's idea of what is solid.
    private WorldRayCast? _effectWalls;

    // What the open ground is made of, for the trace above. A terrain leaf carries no material row of
    // its own, so a hit on it reports Air -- which marks nothing at all. This is the row the footsteps
    // and the player's own material lookup already use.
    private int? _effectTerrainRow;

    // The original's own impact effects -- the material's GunShotEffect and a body's blood. Simulated
    // on the fixed tick beside the particles and drawn by _retailImpacts.
    private readonly ImpactEffectRuntime _impactEffects = new();
    private ExplosionEffectRenderer? _explosions;
    private SmokeSpriteRenderer? _missileSmoke;
    private ICameraStatusLightPresenter? _cameraStatusLights;
    private IGrenadePresenter? _grenadeRenderer;
    private IMissilePresenter? _missileRenderer;
    private readonly MissileSimulation _vehicleMissiles = new();
    private LoadedMission? _loadedMissionForWeapon;
    private IViewModelPresenter? _arms;
    private SoldierRenderer? _soldiers;
    private TerrainData? _playerTerrain;

    // What the player's feet stand on, which is not always the terrain the rest of the level traces
    // against. Project I.G.I. answers both with one octree and this is simply that; I.G.I. 2's ground is
    // a height field its level declares and nothing can trace through yet, so the two part company.
    private IGroundHeightField? _ground;
    private GameMaterialRegistry? _worldMaterials;
    private AmmoTypeRegistry? _ammoTypes;
    private WeaponTypeRegistry? _weaponTypes;
    private bool _playerMode;
    private ObjectCollisionIndex? _worldSolids;
    private WorldTraceCast? _worldTrace;
    private DoorRegistry? _doors;
    private ElevatorRegistry? _elevators;
    private WorldInteractionRegistry? _interactions;
    private GunPickupRegistry? _gunPickups;
    private GenericPickupRegistry? _genericPickups;

    // --- Built once and re-registered into every title's namespace ---
    // Locals until the shell could change games. All three outlive a title: the config host and the menu
    // host are the seams the menus act through, and the mission state is a native the level scripts call.
    // A switch builds a new GameLoop, and each of them has to be registered into it -- which is why they
    // are here rather than in Run's own scope.
    private HostConfigHost _configHost = null!;
    private MenuHost _menuHost = null!;
    private GameMissionState _missionState = null!;

    // The game the shell has asked for, waiting for a frame boundary. Deferred for the same reason a
    // mission load is: the switch tears down the loop, the registry and every presenter, and doing that
    // from inside a frame would pull them out from under the pass that is drawing.
    private GameTitle? _pendingTitle;

    // --- The menus and the cinematic shell ---
    private MenuRenderer? _renderer;
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
        IIgiLogger logger)
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
        _audioFactory = audioFactory;
        _work = work;
        _loadGate = loadGate;
        _titles = titles;
        _titleRuntime = binding.Runtime;
        _presentation = binding.Presentation;
        _worldBuilder = binding.WorldBuilder;
        _missionCatalogue = binding.Missions;
        _fileSystem = fileSystem;
        _options = options;
        _gamePath = gamePath;
        _logger = logger;
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

    /// <summary>The whole of the game's run, one frame per step.</summary>
    /// <returns>An element at the top of every frame, before it runs.</returns>
    /// <remarks>
    /// An iterator so that the frame loop's own state -- its pacers, its timestamps, the shell and the
    /// preview loader, forty-odd locals the loop reads -- lives exactly where it always did, in the order
    /// it always did, and a step is one pass of the loop. The compiler keeps them between steps.
    /// </remarks>
    private IEnumerable<bool> Frames()
    {
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
        _configHost = new HostConfigHost(_device, _window)
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

        ConfigNatives configNatives = new(_configuration, _configHost);

        MenuStringTable strings = _menuStrings!;
        _configHost.Strings = strings;

        // Held so anything outside this method can name a mission the way the player saw it named. A
        // descriptor's own Name is the table KEY -- it reads "Mission 1 Name" -- so a loading panel that
        // showed it would be showing the campaign screen a different title than the one it just listed.
        _menuStrings = strings;
        SpriteBatch batch = new(_device);

        // Held so a loading frame can be drawn and swapped from outside this loop, before a mission
        // load blocks it. See GameHost.MissionWarmUp.cs.
        _batch = batch;
        _renderer = null;
        bool running = true;

        // The developer console: the CVar registry, settings.cfg and the on-screen view. Brought up before
        // anything reads a setting, so the saved file and the command line's +statements are already in
        // effect by the time the renderer and the game are built. Everything logged from here on is
        // mirrored into its scrollback, so opening it shows the log rather than an empty panel.
        using HostConsole console = new(_device, _logger);
        console.QuitRequested += (_, _) => running = false;

        // The console reaches the system clipboard through the window, which is what owns the handle,
        // rather than taking a platform reference of its own.
        console.View.ReadClipboard = () => _window.ClipboardText;
        console.View.WriteClipboard = text => _window.ClipboardText = text;

        // Attached once rather than per mission: the inspector asks for a fresh snapshot every frame
        // it draws, and the implementation already answers with nothing when no world is loaded.
        console.View.AttachWorld(this);

        // Before Start, so a +lamp_switch on the command line and a settings.cfg that binds one both find
        // the command already there.
        RegisterLampCommands(console.System);
        RegisterImpactCommands(console.System);
        RegisterCameraCommands(console.System);
        RegisterRendererSwitchCommand(console.System);
        RegisterOverrideCommands(console.System);

        console.Start(
            _fileSystem,
            _options.StartupCommands,
            useSavedSettings: !_options.Lockstep,
            _options.FirstRunPreset,
            _options.LockedPreset);
        _logger = console.WrapLogger(_logger);
        // The scan LoadTitleResources ran: taken off the field because the scan now happens wherever the
        // resources are loaded, and that is a method a title switch calls too.
        ReportOverrideScan(_overrideScan);

        WarnAboutSettingsTheBackendIgnores();

        // The keys the game binds for itself, each only where the player has not bound it to something
        // else, so a binding they saved is never stolen. What they are and the rule about what may be in
        // the table is in DefaultBinds.
        foreach ((string key, string statement) in Configuration.DefaultBinds.All)
        {
            if (!console.System.Binds.ContainsKey(key))
            {
                console.System.SetBind(key, statement);
            }
        }

        // -novsync is the older spelling of r_vsync 0. It forces the setting rather than shadowing it, so
        // the console and the flag can never report different things.
        if (!_options.VSync)
        {
            RenderCVars.VSync.Value = false;
        }

        // Audio comes up here, after settings.cfg and the +statements, for the same reason the renderer's
        // settings do: the voice count and the output rate are read once, when the device opens.
        //
        // On a worker, because opening OpenAL and decoding the menu track's ADPCM is a tenth of a second
        // that nothing on this thread depends on, and start-up is the one time the player is waiting for
        // this thread specifically. Everything that touches the audio device does so from the frame loop,
        // which is collected below, before the first line that reads it.
        Task audioReady = _work.Run(OpenAudioDevice);

        // Set before anything uploads a texture: the sampler a texture is created with is the one it
        // keeps, so a level loaded before this would keep the filtering it started with.
        _device.SetTextureQuality(RenderCVars.Anisotropy.Value);
        WhenChanged(RenderCVars.Anisotropy, () => _device.SetTextureQuality(RenderCVars.Anisotropy.Value));

        _device.VSyncEnabled = RenderCVars.VSync.Value;
        WhenChanged(RenderCVars.VSync, () => _device.VSyncEnabled = RenderCVars.VSync.Value);

        WhenChanged(RenderCVars.WeatherEnabled, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherStyle, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherSpeed, ApplyWeatherSettings);
        WhenChanged(RenderCVars.WeatherForce, ApplyWeatherSettings);
        WhenChanged(RenderCVars.Storm, ApplyWeatherSettings);
        WhenChanged(RenderCVars.StormIntensity, ApplyWeatherSettings);

        // Timing is pushed on change rather than read per frame, because switching it off is what discards
        // the history -- the peaks after switching it back on should belong to the run being watched.
        _device.SetStageTiming(RenderCVars.PerformanceLog.Value);
        WhenChanged(RenderCVars.PerformanceLog, () => _device.SetStageTiming(RenderCVars.PerformanceLog.Value));

        _menuHost = new MenuHost(
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
        // Baked global illumination: a mission's probe field is loaded from the shipped cache (Assets/gi)
        // when present, else traced and written to the per-user cache so the next load is fast. This is what
        // turns a level load from a few-seconds lighting trace into an instant read -- see GiBakeCache.
        _giCache = GiBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);

        // The level's distance fields, cached the same way and for a sharper reason. This is the one bake
        // the real-time lighting model WAITS on -- until it lands the frame renders from the artists'
        // lightmaps and then visibly crosses over -- and it is the most cacheable thing in the renderer,
        // depending on nothing but the level's static geometry and one integer. See DistanceFieldCache.
        _sdfCache = DistanceFieldCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "sdf"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "sdf"),
            _logger);

        // The path-traced object lightmaps, cached the same way: baked once, read instantly after. The
        // shipped bakes live beside the probe fields; freshly-baked ones write to the per-user cache.
        _lightmapCache = LightmapBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);

        // The mission state native, held because a switch has to re-register it into the loop it builds.
        _missionState = new GameMissionState();

        // The audio device, collected here: this is the first line that reads it, and everything between
        // it and the Task.Run above -- the scheduler, the console, the cvar wiring -- is work the open
        // ran alongside rather than after.
        audioReady.GetAwaiter().GetResult();

        // The task loop, the mission table and the title's content, through the same method a switch
        // runs. Everything above this line belongs to the process; everything it builds belongs to the
        // game, and changing games rebuilds exactly this much.
        LoadTitleSession();
        GameMusicNatives musicNatives = new(_music!);

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
        _electricFenceDose = ElectricFenceContact.DefaultDamageFactor;
        _playerHealth = new(HumanHitZoneTable.Player, _playerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(_playerDifficulty),
        };
        _playerDead = false;

        // The red wash the screen takes on a hit -- four corner alphas that decay a forty-fifth a tick
        // (sub_411300) and paint a screen-filling red quad (HumanPlayer_DrawDamageFlash). It survives the
        // player's death, so the corpse's-eye view stays red rather than snapping back to a clean picture.
        _damageFlash = new();
        _flashbangScreenFlash = new();

        // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye
        // to a third-person shot of the body (HumanCamera death mode). The body it looks at is the player's
        // own third-person rig, which SoldierRenderer loads and poses with the death fall.
        _deathCamera = new();
        InteractionCamera interactionCamera = new();
        bool climbActive = false;
        bool worldInteractionActive = false;
        int temporalFrame = 0;

        // Test-only authored-cutscene capture harness. Set OPENIGI_CUTSCENE_CAPTURE_DIR to enable;
        // OPENIGI_CUTSCENE_CAPTURE_SCENE selects one scene id (unset/0 captures whichever scene is
        // active), OPENIGI_CUTSCENE_CAPTURE_START_TICK is the first scene tick written and
        // OPENIGI_CUTSCENE_CAPTURE_COUNT the still budget (default 5). One still is written every
        // 90 logic ticks (3 s) while the scene runs, named mission-NN-scene-ID-tick-NNNNNN.bmp.
        string? cutsceneCaptureDirectory = Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_DIR");
        int cutsceneCaptureScene = 0;
        bool captureCutscene = cutsceneCaptureDirectory is not null;
        _ = int.TryParse(Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_SCENE"), out cutsceneCaptureScene);
        int cutsceneCaptureTick = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_START_TICK"),
            out int configuredCaptureTick)
            ? Math.Max(0, configuredCaptureTick)
            : 90;
        int cutsceneCaptureCount = 0;
        int cutsceneCaptureStartTick = cutsceneCaptureTick;
        CutSceneTask? previousCapturedScene = null;
        int cutsceneCaptureLimit = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_COUNT"),
            out int configuredCaptureCount) && configuredCaptureCount > 0
            ? configuredCaptureCount
            : 5;

        // A scripted free-camera flight (-camend), if one was asked for. The opening pose is captured on
        // the first rendered frame and the camera is moved along a straight line to the end pose across the
        // frame budget, so the temporal filters (GI, TAA) can be seen resolving under real motion -- which a
        // static capture cannot show. flightFrame is the rendered-frame index, and the sequence's frame number.
        bool flightStarted = false;
        Vector3d flightStartPos = default;
        float flightStartYaw = 0f;
        float flightStartPitch = 0f;
        int flightFrame = 0;

        _probeField = null;

        // The bake in flight, while it is in flight. Non-null only between the level load starting one and
        // the render thread collecting it; a lockstep capture never sees it, because that path waits.
        _probeBake = null;
        _lightmapBake?.Cancel();
        _lightmapBake = null;

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

        // The gather's raw per-probe estimates, traced by its first pass and pooled by its second.
        TextureHandle giRaw = default;
        int giIndirectWidth = 0;
        int giIndirectHeight = 0;
        int giFrame = 0;
        bool giHasHistory = false;
        Matrix3f giPrevWorldToView = Matrix3f.Identity;
        Vector3f giPrevCameraToCache = default;

        // The full-resolution resolve runs EVERY frame even where the gather is amortised, so it keeps its
        // own ping-pong cursor and its own previous camera. Sharing the gather's was the trap: on a frame
        // the gather skipped, the resolve would have reprojected against a camera two frames old and read
        // back the slot it was about to write.
        int giResolveFrame = 0;
        Matrix3f giResolvePrevWorldToView = Matrix3f.Identity;
        Vector3f giResolvePrevCameraToCache = default;

        // Which accumulation slot the last gather wrote, and the atlas the resolve should read. On a frame
        // the gather skips, both simply stand.
        int giLastWrite = 0;
        int giCadenceCursor = 0;

        // The full-resolution temporal resolve's atlases: the sparse probe atlas upsampled and accumulated
        // per pixel so the indirect is pinned to the world rather than sliding with the sparse reprojection.
        // Ping-ponged like the probe atlases, sized to the full render, and read directly by the deferred fill.
        TextureHandle[] giResolve = [default, default];
        TextureHandle[] giResolvePosition = [default, default];
        int giResolveWidth = 0;
        int giResolveHeight = 0;
        bool giResolveHasHistory = false;

        // What the screen GI's histories were accumulated under, so a cut, a zoom or a new surface cache
        // can throw them away. See where the gather begins.
        bool giContinuityValid = false;
        Vector3d giLastEye = default;
        DepthUnprojection giLastUnprojection = default;
        SurfaceCacheLighting? giLastCards = null;
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

        // The lamps somebody has named by hand. One list for the whole game -- a model is a global
        // asset and binds the same textures in every mission it appears in -- so this is read once for
        // the session rather than per level.
        _lampManifest = LampManifest.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "lamps.json"),
            _logger);

        // The derived surface detail this game never shipped -- normals, roughness, metal. Built by
        // derived here for anything no replacement carries a map for; an installation that has never run a tool
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
            _work,
            _logger,
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "detail"));

        // Set HERE as well as on the frame, and the difference only shows on the path a capture takes.
        // The loop assigns this every pass so that turning r_material_detail up mid-session starts
        // deriving for whatever loads next -- but a level reached with -mission loads its textures BEFORE
        // the first pass, so every one of them saw the default of false and cached the answer "no map".
        // A mission started from the menu never hit it, because the loop is already running by then.
        // Measured: 27 maps derived over 600 frames of -mission, against a level's worth of textures.
        _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;


        // What a menu backdrop needs to be promotable into the played mission rather than thrown away,
        // and what it needs to be LIT the way that mission will be: the lamp manifest its proposal is
        // checked against and the field cache it reads. Built here because every part of it is finished
        // being written by this point and none of it is mutated afterwards, which is what makes it safe
        // to hand to a worker.
            RebuildPreviewContext();


        // Skeleton 0's and skeleton 5's animation sets, read here rather than when a mission starts.
        // They live under common/, so they are the same 2.4 MB of IFF for every mission, and parsing
        // them was the single largest thing standing a player up used to do on the render thread.
        //
        // Started rather than done: at start-up the thing between the player and the first frame is this
        // thread, and the read needs nothing from it. The first mission waits for the result, by when it
        // has been sitting finished for several seconds.
        BeginReadingSharedAnimations();


        // Re-graded for whatever is actually on screen: the mission being played, else the menu's
        // backdrop, else whatever -mission asked for. Reading `_options.StartMission` unconditionally
        // meant changing the table from the console while browsing the campaign re-graded to mission 0.
        WhenChanged(RenderCVars.ColourLookupTable, () =>
            ApplyColourLookup(_activeMission?.Id ?? _backdrop?.MissionId ?? _options.StartMission));

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
        _retailImpacts = null;
        _impactEffects.Clear();
        _explosions = null;
        _missileSmoke = null;
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _loadedMissionForWeapon = null;
        _arms = null;
        _soldiers = null;
        _playerTerrain = null;
        _effectWalls = null;
        _effectTerrainRow = null;
        _ground = null;

        // The material table, shared with the guard-round handler so an AI round's impact can leave the
        // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
        _worldMaterials = null;
        _ammoTypes = null;
        _weaponTypes = null;
        _alarmSpeakers = null;
        _destructibles = null;
        _sirenVoices.Clear();
        _playerMode = false;

        // The debug overlay (F3): the player's state and a crosshair "what am I looking at" readout. The
        // collision index is kept here so the overlay can name the object under the crosshair.
        _worldSolids = null;
        _worldTrace = null;
        bool showDebug = false;

        _menuHost.StartGame = () => StartMission(_missionState.PendingMissionId);

        _menuHost.LoadMainMenu = () =>
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
                _missionState.TryInvokeFromMenu(name, arguments) ?? musicNatives.TryInvokeFromMenu(name, arguments);
            menu.Manager.Initialize(menu, _menuHost, _logger);
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

        // Hoisted so a title switch can rebuild the shell around them. Neither belongs to a game: the
        // host seam calls back into this class, and the save store is the player's, one directory for
        // the session. What the rebuilt shell does get anew is its CONTEXT -- the game list and the
        // mission list, which are the two things that change when the game does.
        ShellHost? shellHost = null;
        SaveSlotStore? saves = null;

        // What the mission-entry shot types over its vantage. Filled when the shot begins.

        // Where the shot's character stands, in engine units, and which way it faces.
        bool menuMode = false;

        // The shell is up over a LIVE mission -- the pause menu -- rather than over the showcase. It always
        // implies menuMode; what it adds is that the backdrop is already the right picture. So nothing is
        // streamed in behind the menu, the showcase orbit does not take the camera off the player, and the
        // world is frozen instead of advanced. Escape opens it and Escape closes it again.
        bool pauseMenu = false;
        bool showcasePending = false;
        bool firstFrameReported = false;

        // -entrycam: Play has not been pressed yet. Set only when the flag asked for it.
        bool entryCamPending = false;
        long entryTimeChosenAt = -1;
        ShellHost? shellHostForEntryCam = null;

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
        bool dissolveRevealPending = false;

        // A field that finished baking for the backdrop, waiting for the frame after the hold to be
        // adopted -- see the collect further down.
        DistanceFieldBake.Result? bakedFieldForBackdrop = null;

        // The menu is held to the display's rate rather than rendering as fast as it can, where the
        // platform leaves the rate to the loop. See IFramePacer.
        IFramePacer menuPacer = _platform.CreateFramePacer();
        bool lastPassRendered = false;

        // How long a mission has to stay selected before its level is built. Every selection re-arms the
        // timer, so running down the list faster than this builds nothing at all. It can be short now that
        // building costs the render thread nothing -- it is a debounce against pointless work, not a shield
        // for the frame rate.
        const int PreviewSettleMs = 260;


        // -vanillamenu reaches the retail menus, which carry their own in-game menu; everything else runs
        // the cinematic shell. -mission still opens straight into a level — what it no longer does is leave
        // the shell unbuilt, because the pause menu IS the shell and Escape has to reach it from a mission
        // the menus were skipped into.
        if (_options.BakeAll)
        {
            // The bake sweep runs after the window is shown (the probe wait pumps the message queue); the
            // shell and menus are skipped entirely.
        }
        else if (_options.VanillaMenu && _options.StartMission <= 0)
        {
            _loop.Flow.Request(FlowEvent.MainMenu);
        }
        else
        {
            _menuCameras = MenuCameraLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);
            missionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);
            previewLoader = new MissionPreviewLoader(
                _gamePath, _missions, _magic, _giCache, _menuCameras, _previewContext, _logger, _work);

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
            saves = new SaveSlotStore(Path.Combine(SettingsPaths.GetUserDirectory(), "saves"));

            shellHost = new ShellHost(
                startMission: (id, difficulty) =>
                {
                    if (_configuration.Active is { } activeProfile)
                    {
                        activeProfile.Difficulty = difficulty;
                    }

                    // A load already under way owns the screen; a second press is nothing.
                    if (_loadSequence.IsActive)
                    {
                        return;
                    }

                    // Reachable from the pause menu as well as from the front end — the campaign is in the
                    // bar either way — so the shell is told it is no longer over a mission it was paused on.
                    // Deferred where a sequence covers the load: the menu keeps drawing under the departure
                    // and is put away on the frame the load actually runs.
                    void LeaveTheMenu()
                    {
                        shell?.ClosePauseMenu();
                        menuMode = false;
                        pauseMenu = false;
                        ApplyGameplayDepthOfField();
                    }

                    // The menu has been drawing this very level for as long as the player has been
                    // reading the briefing, built on a worker and complete. When it is the mission they
                    // picked, the level is PROMOTED rather than loaded a second time — see
                    // PromoteBackdrop — behind the same going-in sequence a restart gets: the picture
                    // falls out of focus under the card, the promotion runs, and the shot opens over the
                    // start. `_backdrop` is null over a played mission, so this cannot fire from the pause
                    // menu. -lockstep is excluded: the sequence runs on wall-clock time, and a reference
                    // capture has to land on the same frame every run.
                    if (_backdrop is { } backdrop && backdrop.MissionId == id && !_options.Lockstep)
                    {
                        BeginWatchingFramePasses($"Entering mission {id}");

                        void Promote()
                        {
                            LeaveTheMenu();
                            if (PromoteBackdrop(backdrop))
                            {
                                _window.CursorVisible = false;
                                ReportWatchedFramePassesIn(2);
                            }
                            else
                            {
                                StartMission(id);
                                _window.CursorVisible = false;
                            }
                        }

                        if (CinematicLoadAvailable)
                        {
                            BeginCinematicLoad(id, Promote);
                        }
                        else
                        {
                            Promote();
                        }

                        return;
                    }

                    void LoadCold()
                    {
                        LeaveTheMenu();
                        BeginWatchingFramePasses($"Loading mission {id}");
                        StartMission(id);
                        ReportWatchedFramePassesIn(5);
                        _window.CursorVisible = false;
                    }

                    if (CinematicLoadAvailable)
                    {
                        BeginCinematicLoad(id, LoadCold);
                    }
                    else
                    {
                        LoadCold();
                    }
                },
                setMissionConditions: conditions =>
                {
                    _conditions = conditions;

                    // One variable carries the hour, because one already means exactly this:
                    // r_time_of_day is "the hour a level opens at, or negative for the level's own", and
                    // both the mission loader and the menu's backdrop already reset the cycle from it. It
                    // is deliberately not archived, which is what makes a campaign choice safe to put in
                    // it -- a night run lasts the session and does not become a preference.
                    RenderCVars.TimeOfDay.Value = conditions.StartHour;

                    // The weather gets no variable of its own. Forcing rain everywhere through
                    // r_weather_force WOULD be archived, so a campaign choice would outlive the campaign
                    // and rain on every mission of the next run; ApplyWeatherSettings reads the choice
                    // straight off _conditions instead.
                    ApplyWeatherSettings();

                    // The level behind the menu is re-lit at once, so choosing night shows night while
                    // the briefing is still being read. Never over a mission being played: the sky is not
                    // to change under a player halfway through a level, and the hour they chose is picked
                    // up by the next level they load.
                    if (menuMode && !pauseMenu)
                    {
                        _dayNight.ResetForLevel(_sun, RenderCVars.TimeOfDay.Value);
                    }
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
                    if (_activeMission is { } mission && !_loadSequence.IsActive)
                    {
                        // The frozen mission under the pause menu is the picture the sequence holds: the
                        // menu leaves, the place falls out of focus, and it comes back reset.
                        void Restart()
                        {
                            shell?.ClosePauseMenu();
                            menuMode = false;
                            pauseMenu = false;
                            ApplyGameplayDepthOfField();
                            StartMission(mission.Id);
                            _window.CursorVisible = false;
                        }

                        if (CinematicLoadAvailable)
                        {
                            BeginCinematicLoad(mission.Id, Restart);
                        }
                        else
                        {
                            Restart();
                        }
                    }
                },
                abandonMission: () =>
                {
                    // Before it was ever shown, possibly. Counting the rest of the warm-up down would
                    // hold a loading panel over the campaign screen.
                    CancelMissionWarmUp();
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
                switchTitle: RequestTitle,
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
                saveProfiles: () => HostConfiguration.Save(_configuration, _logger),
                captureThumbnail: (out captureWidth, out captureHeight) =>
                {
                    captureWidth = _window.Width;
                    captureHeight = _window.Height;
                    var pixels = new Bgra32[captureWidth * captureHeight];
                    _device.CaptureFrame(captureWidth, captureHeight, pixels);
                    return pixels;
                });

            shellHostForEntryCam = shellHost;

            shell = new Shell(BuildShellContext(shellHost, shellTheme, saves));

            // -shelltab is a development shortcut, not a game feature, and the shell's counterpart to
            // -screen: it opens straight on a section so a capture does not need someone to click there.
            if (_options.ShellTab is { Length: > 0 } openOn && !shell.OpenOn(openOn))
            {
                _logger.Warning("Menu", $"-shelltab '{openOn}' is not a section; opening on the main menu.");
            }

            if (_options.StartMission > 0)
            {
                // Straight into the level, with the shell built and waiting behind Escape rather than shown.
                // Once its files can be read: on the desktop that is now, in a browser it is when the page
                // has fetched them -- a level started before that opened with no terrain and no models.
                ResumeConditions();
                StartMissionWhenReadable(_options.StartMission);
            }
            else
            {
                // The showcase level is ASKED FOR here and arrives when it arrives. It used to be
                // requested a couple of frames into the loop, because building it once blocked for
                // seconds and doing that before the window was shown was a blue screen where the menu
                // should be. It has not blocked since the build moved to a worker, and the wait between
                // asking and the backdrop appearing is that worker's -- so asking now rather than two
                // frames later is the whole of it earlier. The menu still draws over its dark backdrop
                // in the meantime, which is what the deferral was protecting.
                menuMode = true;
                int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
                currentShowcaseMissionId = firstShowcase;

                if (previewLoader is { } opening)
                {
                    RequestBackdrop(opening, firstShowcase);
                }
                else
                {
                    showcasePending = true;
                }

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
        _logger.Information(
            "Desktop",
            $"{_platform.Name} up on {_device.Capabilities.Renderer}; the window is on screen "
            + $"{SinceProcessStart():0} ms after the process started.");

        // -poidump: build every mission, find the places its menu backdrop should circle, write them out
        // and quit. A tool run once; what it writes is then owned and edited by hand.
        if (_options.ShowcasePointDump is { Length: > 0 } dumpPath)
        {
            DumpShowcasePoints(dumpPath, strings);
            running = false;
        }

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
                    + $"{Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi")} into apps/OpenIGI.Composition/Assets/gi.");
            running = false;
        }

        // The distance-field shader is compiled here, when the feature is on, rather than when a level
        // first needs it -- a shader that does not compile would otherwise surface a long way into a run
        // as a stage that silently did nothing. Gated so a device with the feature off pays nothing.
        if (RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute)
        {
            GlobalDistanceField.ValidateShader(_device, _logger);
        }

        // Reused every frame rather than reallocated; the fit writes into it in place.
        ShadowCascade[] cascadeStorage = new ShadowCascade[ShadowSettings.MaxCascades];

        int lastCameraMilliseconds = _loop.TimeSource.Milliseconds;
        int lastConsoleMilliseconds = _loop.TimeSource.Milliseconds;
        int lastShellMilliseconds = _loop.TimeSource.Milliseconds;
        int lastMenuCameraMilliseconds = _loop.TimeSource.Milliseconds;
        int lastProbeChaseMilliseconds = _loop.TimeSource.Milliseconds;
        int probeChaseFrame = 0;

        // A capture run is a background process as far as the person at the keyboard is concerned: it must
        // not read the mouse and must never grab the cursor out from under whatever they are actually doing.
        bool captureMode = _options.Lockstep;

        // What the last pass of the loop did, and how many ticks in a row have run without a step
        // returning to its host. See the top of the loop.
        FrameOutcome lastOutcome = FrameOutcome.Idle;
        int ticksSinceStep = 0;

        while (running && !_stopRequested && _loop.IsRunning && !_window.IsClosing)
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
            if (lastOutcome != FrameOutcome.Tick || ++ticksSinceStep >= MaxTicksPerStep)
            {
                ticksSinceStep = 0;
                yield return true;
            }

            // Until this pass runs the loop: one that ends before it has drawn nothing and ticked nothing.
            lastOutcome = FrameOutcome.Idle;

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
            NoteFramePass(passStartedAt);
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
                missionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);

                // Closed rather than dropped: it owns a worker that may be part way through building the
                // outgoing game's backdrop, and the replacement below would otherwise race it.
                previewLoader?.Dispose();
                previewLoader = new MissionPreviewLoader(
                    _gamePath, _missions, _magic, _giCache, _menuCameras, _previewContext, _logger, _work);

                if (shellHost is not null && shellBodyFont is not null && shellHeadingFont is not null && saves is not null)
                {
                    shell = new Shell(BuildShellContext(shellHost, new UiTheme(shellBodyFont, shellHeadingFont), saves));
                    shell.OpenOn("campaign");
                }

                // Back to the front end with no level up: the outgoing game's was released and the
                // incoming one has not been asked for.
                menuMode = true;
                pauseMenu = false;
                showcasePending = true;
                ApplyMenuDepthOfField();
            }

            // The menu has nothing to gain past the display's rate, and a GPU kept at full load turns every
            // small upload between frames into a hitch. Only after a pass that presented, or the pacing
            // would throttle the simulation's ticks as well; never under a capture, whose clock is its own.
            // A level held behind the loading screen while its field bakes is paced harder still: the
            // withheld frames exist to advance the temporal passes, which thirty a second does as well as
            // a hundred, and every core the render thread is not spinning on is one the bake gets.
            if (lastPassRendered && !_options.Lockstep && !_options.IsBenchmark)
            {
                if (IsWarmingUp && _sdfBake is not null)
                {
                    menuPacer.Pace(30);
                }
                else if (menuMode && !RenderCVars.VSync.Value)
                {
                    menuPacer.Pace(_window.RefreshRate > 0 ? _window.RefreshRate : 60);
                }
                else
                {
                    menuPacer.Reset();
                }
            }
            else
            {
                menuPacer.Reset();
            }

            lastPassRendered = false;
            passStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            _window.PumpEvents();

            // Whatever the detail-map workers finished since the last pass, uploaded a few at a time.
            // The setting is read here rather than captured at construction so that turning
            // r_material_detail up mid-session starts deriving for whatever loads next -- it is zero
            // under vanilla and enhanced, and deriving maps the shader multiplies by nothing is waste.
            _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
            _detailMaps.PumpDerived();

            // The probe bake, if one is still tracing. Collected HERE rather than on the worker because
            // what comes back becomes a GL texture, and the context belongs to this thread. TryCollect
            // answers true once, so this costs a completed-flag read per frame until then and nothing after.
            if (_probeBake is { } pendingBake)
            {
                // Each bounce pass becomes the field the display eases towards, not one shown outright:
                // the smooth chase below fills the light in over about a second rather than stepping it.
                if (pendingBake.TryCollectLatest(out IrradianceVolume? streamedField) && streamedField is not null)
                {
                    if (_probeField is null)
                    {
                        // First light: shown at once and faded up from flat ambient by the device's own
                        // blend, so there is a starting field for later passes to ease away from.
                        _probeField = streamedField;
                        _probeTarget = streamedField;
                        _device.SetIrradianceVolume(_probeField);
                        probeArrivedAt = _loop.TimeSource.Milliseconds;
                    }
                    else
                    {
                        _probeTarget = streamedField;
                    }
                }

                // Once the bake is done, the authoritative final field is both what the display settles on
                // and what the cache keeps, so the next load reads it instead of tracing again.
                if (pendingBake.Poll())
                {
                    if (pendingBake.TryCollect(out IrradianceVolume? finalField) && finalField is not null)
                    {
                        _probeTarget = finalField;
                        _giCache.Save(_probeBakeMission, _probeBakeSpacing, _probeBakeSun, finalField);
                    }

                    _probeBake = null;
                }
            }

            // The path-traced lightmap bake, once its worker finishes: swap the pages in and write them to
            // the cache so the next load reads instead of tracing. Collected here for the same reason the
            // probe field is -- the pages become GL textures, and the context belongs to this thread.
            if (_lightmapBake is { } pendingLightmaps && pendingLightmaps.TryCollect(out BakedLightmaps? bakedLightmaps))
            {
                _lightmapBake = null;
                if (bakedLightmaps is not null)
                {
                    _world?.SetBakedLightmaps(bakedLightmaps, RenderCVars.BakedLightmaps.Value >= 2);
                    _lightmapCache.Save(
                        _lightmapBakeMission, _lightmapBakeSun, _lightmapBakeSunColour, _lightmapBakeSky, bakedLightmaps);
                }
            }

            // Ease the displayed field towards the newest baked pass. Framerate-independent, so the fill
            // looks the same however fast frames go out, and it keeps running after the bake handle is gone
            // until the last pass has settled -- at which point the field points straight at the target and
            // the per-frame re-upload stops.
            if (_probeTarget is not null && _probeField is not null && !ReferenceEquals(_probeField, _probeTarget)

                // Every fourth frame, not every frame. The blend walks the whole field on the CPU and the
                // re-upload sends the four probe volumes over again -- together roughly ten milliseconds
                // and half a gigabyte a second while a bake streams. The ease runs over about a second, so
                // a quarter-rate step is invisible; the elapsed-time step below keeps the pace identical.
                && (++probeChaseFrame & 3) == 0)
            {
                int chaseNow = _loop.TimeSource.Milliseconds;
                float chaseSeconds = MathF.Max(0f, (chaseNow - lastProbeChaseMilliseconds) / 1000f);
                lastProbeChaseMilliseconds = chaseNow;

                float step = 1f - MathF.Exp(-ProbeChaseRate * chaseSeconds);
                float moved = _probeField.BlendToward(_probeTarget, step);
                _device.SetIrradianceVolume(_probeField);

                if (moved < ProbeChaseSettle)
                {
                    _probeField = _probeTarget;
                }
            }
            else
            {
                // Keep the clock current while idle, so the first frame of the next ease steps by one
                // frame's worth of time rather than the whole idle gap.
                lastProbeChaseMilliseconds = _loop.TimeSource.Milliseconds;
            }

            // Whatever the real-time lighting model needs and does not have yet. Every frame rather than
            // at load, because the settings menu can ask for that model at any moment and the structures it
            // reads have to be able to arrive afterwards -- see GameHost.RealtimeGi.cs. A no-op once
            // they exist, which is every frame but the handful after the switch.
            EnsureRealtimeGiStructures();

            // Behind the loading screen the traced world takes every mesh offered in the frame it is
            // offered, so the level is whole in it before anything is lit against it; in play the per-frame
            // budget comes back. Said every frame, so no path out of the load can leave it lifted.
            _device.SetTraceAdmission(IsWarmingUp);

            // This frame's share of a convergence in flight, so a level lights up behind the loading
            // screen or under the entry shot's hold rather than in one stall.
            StepGiConvergence();

            // Same handover, same reason: the atlas becomes a GL volume, and the context belongs here.
            // Behind the menu the adoption is deferred to the frame after the picture has been held, so
            // the backdrop crossing over to its real-time lighting happens under the dissolve rather than
            // in front of the player -- the same shape as a backdrop swap.
            if (_sdfBake is { } pendingField && pendingField.TryCollect(out DistanceFieldBake.Result baked))
            {
                _sdfBake = null;
                if (menuMode && !pauseMenu && _backdrop is not null)
                {
                    bakedFieldForBackdrop = baked;
                }
                else
                {
                    AdoptDistanceField(baked.Scene, baked.Atlas, fromCache: false);
                }
            }

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
            void DrainPressQueue(bool executeBinds)
            {
                if (!console.IsOpen && !console.IsolatedFromSavedSettings && !_window.Input.Captured)
                {
                    while (_window.Input.Keyboard.TryDequeuePress(out Input.KeyCode key))
                    {
                        if (executeBinds)
                        {
                            console.System.ExecuteBind(BindKeyNames.NameOf(key));
                        }
                    }
                }
            }

            if (!(menuMode && shell is not null))
            {
                DrainPressQueue(executeBinds: true);
            }

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
                DrainPressQueue(executeBinds: false);
                if (!console.IsOpen)
                {
                    _window.Input.Captured = true;
                }

                // Something wants the picture to change — a new backdrop has finished building, or the
                // showcase camera has held a place long enough to move to the next one. Either way the
                // frame on screen is held first, so the two can be blended rather than cut between.
                // None of it applies to the pause menu, whose picture is a level that is not going anywhere.
                // Not while a load is departing: the held frame is the sequence's picture from here, and
                // a backdrop swapped under a departure would be a different place from the one the card
                // is over.
                bool changeWanted = !pauseMenu
                    && !_loadSequence.IsActive
                    && (previewLoader is { HasResult: true }
                        || _showcaseCamera is { IsCutDue: true }
                        || bakedFieldForBackdrop is not null);
                if (changeWanted && !holdFrameForDissolve && !heldFrameReady)
                {
                    holdFrameForDissolve = true;
                }

                // The held picture stays up until the incoming level has converged its lighting, so what
                // the dissolve reveals is the settled backdrop rather than one still lighting up. A field
                // that is still baking is not waited for -- that is seconds, and it crosses over under its
                // own hold when it lands.
                if (dissolveRevealPending && !IsGiConverging)
                {
                    dissolveRevealPending = false;
                    dissolve.Retarget(0f);
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

                // The hand-over, taken as the travel begins: the fastest, most smeared moment of the shot,
                // where the one frame the promotion still costs is least visible. What it used to cost --
                // several hundred milliseconds -- has moved onto the preview worker; see AdoptPreview.
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

                // The beat holds until the picture under it is final -- the lighting built and converged,
                // the temporal passes settled -- which is what the warm-up decides. The camera keeps
                // drifting while it waits, so a cold first-time bake reads as a longer shot rather than a
                // frozen one.
                if (_entry.Phase == MissionEntryPhase.Hold && !IsWarmingUp && !_loadSequence.IsActive)
                {
                    _entry.NotifyReady();
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
                    if (_entry.ShowPlayerBody && _player is { } standingBody)
                    {
                        // Minus the yaw, for the reason the third-person follow states at length: a
                        // heading is a gamma and turns the other way. This shot has always faced the
                        // character the wrong way about; it went unnoticed because the dive ends inside
                        // their head and the level's own spawn heading is often near zero, where the
                        // mirror shows nothing.
                        bodies.StandPlayerBodyAt(standingBody.Body.Position, -standingBody.Body.Yaw);
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

            // -titlecycle changes GAME on a fixed cadence, taking exactly the path the menu's selector
            // takes. Latched on the frame count for the same reason -missioncycle is, and placed beside
            // it because it is the same kind of flag: the switch is the largest teardown the session
            // performs and a person clicking a dropdown was the only way to reach it.
            if (_options.TitleCycleFrames > 0)
            {
                long titleLeg = _loop.Clock.FrameCount / _options.TitleCycleFrames;
                if (titleLeg != _titleCycleLeg)
                {
                    _titleCycleLeg = titleLeg;
                    RequestNextInstalledTitle();
                }
            }

            // -missioncycle leaves the level and re-enters it on a fixed cadence, taking exactly the two
            // paths the pause menu's Abandon and the campaign's Play take. It is here so it runs against
            // a fully built frame rather than during start-up, and it is latched on the frame count so
            // both legs happen once each rather than every frame past the mark.
            if (_options.MissionCycleFrames > 0 && shell is not null)
            {
                long leg = _loop.Clock.FrameCount / _options.MissionCycleFrames;
                if (leg != _missionCycleLeg)
                {
                    _missionCycleLeg = leg;
                    if (_activeMission is { } cycling)
                    {
                        _logger.Information("Mission", $"-missioncycle: abandoning {cycling.Id}.");
                        _missionCycleMission = cycling.Id;
                        ReleasePlayedMission();
                        shell.ClosePauseMenu();
                        shell.OpenOn("campaign");
                        menuMode = true;
                        pauseMenu = false;
                        showcasePending = true;
                        ApplyMenuDepthOfField();
                    }
                    else if (_missionCycleMission > 0)
                    {
                        _logger.Information("Mission", $"-missioncycle: playing {_missionCycleMission} again.");
                        shell.ClosePauseMenu();
                        menuMode = false;
                        pauseMenu = false;
                        ApplyGameplayDepthOfField();
                        StartMission(_missionCycleMission);
                    }
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

            // -runat fires a console statement once on a named frame, after the world exists. Latched
            // by index so a statement runs exactly once however long the capture goes on.
            if (_options.DeferredCommands.Count > 0)
            {
                for (int i = 0; i < _options.DeferredCommands.Count; i++)
                {
                    (int frame, string statement) = _options.DeferredCommands[i];
                    if (_deferredCommandsFired.Contains(i) || _loop.Clock.FrameCount < frame)
                    {
                        continue;
                    }

                    _deferredCommandsFired.Add(i);
                    _logger.Information("Console", $"-runat {frame}: {statement}");
                    console.System.Execute(statement, echo: true);
                }
            }

            // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
            // freezes player/camera input while the world continues its fixed-tick simulation.
            if (!menuMode && !console.IsOpen && _player is not null
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
                RequestBackdrop(previewLoader, firstShowcase);
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
                // -entrytime: the time chosen first, as the dropdown chooses it, and Play a second later
                // over the backdrop that choice re-lit.
                if (_options.EntryTime != MissionTimeOfDay.Default && entryTimeChosenAt < 0)
                {
                    entryTimeChosenAt = _loop.Clock.FrameCount;
                    shellHostForEntryCam?.SetMissionConditions(_conditions with { TimeOfDay = _options.EntryTime });
                }

                bool settled = entryTimeChosenAt < 0
                    || _loop.Clock.FrameCount - entryTimeChosenAt >= EntryTimeSettleFrames;

                if (settled)
                {
                    entryCamPending = false;
                    _logger.Information("Menu", $"-entrycam is pressing Play on mission {entryBackdrop.MissionId}.");
                    shellHostForEntryCam?.StartMission(
                        entryBackdrop.MissionId,
                        _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal);
                }
            }

            // -backdropcycle walks the campaign's rows on a fixed cadence, taking the same path a player
            // moving down the mission list takes: it asks the preview loader for the next mission and
            // lets the ordinary handover below adopt it. Latched on the frame count so each row is asked
            // for once, and only while the menu is actually up over a backdrop.
            if (_options.BackdropCycleFrames > 0 && menuMode && !pauseMenu && previewLoader is not null)
            {
                long row = _loop.Clock.FrameCount / _options.BackdropCycleFrames;
                if (row != _backdropCycleRow)
                {
                    _backdropCycleRow = row;
                    int[] order = [.. _missions.InCampaignOrder().Select(m => m.Id)];
                    if (order.Length > 0)
                    {
                        int next = order[(int)(row % order.Length)];
                        if (next != currentShowcaseMissionId)
                        {
                            _logger.Information("Menu", $"-backdropcycle: selecting mission {next}.");
                            pendingPreviewMissionId = null;
                            currentShowcaseMissionId = next;
                            RequestBackdrop(previewLoader, next);
                        }
                    }
                }
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
                RequestBackdrop(previewLoader, previewMissionId);
            }

            // A backdrop whose mission's files were still arriving, asked for again until they are there.
            if (menuMode && !pauseMenu && _backdropAwaitingFiles is { } awaitingFiles)
            {
                RequestBackdrop(previewLoader, awaitingFiles);
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
                else if (bakedFieldForBackdrop is { } landedField)
                {
                    // The backdrop's field, baked behind the menu, adopted under the held frame so the
                    // level's cross-over to its real-time lighting is dissolved to rather than watched.
                    bakedFieldForBackdrop = null;
                    AdoptDistanceField(landedField.Scene, landedField.Atlas, fromCache: false);
                    changed = true;
                }

                heldFrameReady = false;

                // The held frame is now the whole picture; it dissolves off to reveal what is behind it,
                // once the lighting behind it has converged.
                if (changed)
                {
                    dissolve.SnapTo(heldFrameIsPicture ? 1f : 0f);
                    dissolveRevealPending = heldFrameIsPicture;
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

            // F11 re-bakes the lighting from scratch, live. It throws away every cached field and the one
            // on screen, then starts tracing again from the same inputs this level loaded with, so the
            // light resolves back in from nothing over the next seconds -- the way to see a change to the
            // bake without reloading the level, and to watch it generate. Ignored where there is nothing to
            // bake (GI off, or the level's scene or cast not yet set up).
            // F8 puts the overrides folder in and out of the frame. Watched as a VARIABLE rather than as
            // a key press, so `r_overrides 0` from the console does the same thing as the key -- the bind
            // sets the variable and this notices, which is one path rather than two that must agree.
            //
            // Nothing happens with an empty folder: the flag is meaningful only against something to
            // compare with, and reloading a level's textures to show the same assets again would be a
            // long hitch for no picture.
            if (_overridesApplied != RenderCVars.OverridesEnabled.Value && _overrides.Count > 0)
            {
                _overridesApplied = RenderCVars.OverridesEnabled.Value;
                _overrides.Bypassed = !_overridesApplied;

                int swapped = _resources.ReapplyOverrides();

                if (swapped > 0)
                {
                    _world?.ReloadTextures();
                    _ = ReloadTerrainTiles();

                    // The card atlas has the diffuse baked into it, so the indirect light is still
                    // bouncing off the old pixels until it is captured again. Discarding it is the
                    // existing invalidation -- the same one a density change uses -- and it re-converges
                    // the GI with it.
                    DiscardSurfaceCache();
                }

                _logger.Information(
                    "Overrides",
                    _overridesApplied
                        ? $"Overrides on (F8); {swapped} resource(s) swapped in."
                        : $"Overrides off (F8); {swapped} resource(s) back to the game's own.");
            }

            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F11)
                && RenderCVars.GlobalIllumination.Value
                && _scene is not null
                && _bakeCast is not null)
            {
                _probeBake?.Cancel();
                int cleared = _giCache.DeleteAll();

                _probeField = null;
                _probeTarget = null;
                _device.SetIrradianceVolume(null);
                probeArrivedAt = -1;
                lastProbeChaseMilliseconds = _loop.TimeSource.Milliseconds;

                _probeBake = IrradianceProbeBake.Start(
                    _scene, _bakeCast, _probeBakeSun, _probeBakeSpacing, ProbeBakeAlbedo, _work, _logger);

                _logger.Information(
                    "GI", $"Re-baking the lighting from scratch (F11); cleared {cleared} cached field(s).");
            }

            // F1 swaps between flying and walking. The player takes the camera's position when it is
            // switched on, so the swap happens where you were looking.
            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && _player is not null && _camera is not null)
            {
                _playerMode = !_playerMode;
                if (_playerMode)
                {
                    if (!_player.Place(_ground, _camera.Position, _camera.Yaw))
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

            // The day/night cycle is advanced on every pass, on real elapsed time, so holding [ or ] sweeps
            // the sun smoothly whatever the frame rate -- the same reasoning the camera below is advanced by.
            // The keyboard's own capture flag keeps the console's typing out of it.
            //
            // The automatic cycle, on top of the scrub, runs only while a mission is actually being played:
            // menuMode covers the front end and the pause menu alike, and neither is time the player is
            // spending in the level. The cycle banks the paused seconds nowhere, so coming back does not
            // release a jump in the sun.
            {
                int dayNightNow = _loop.TimeSource.Milliseconds;
                float dayNightElapsed = Math.Clamp((dayNightNow - _dayNightMilliseconds) / 1000f, 0f, 0.1f);
                _dayNightMilliseconds = dayNightNow;
                bool clockRuns = RenderCVars.DayNightCycle.Value && !menuMode && _activeMission is not null;
                _dayNight.Update(
                    _window.Input.Keyboard,
                    dayNightElapsed,
                    clockRuns ? RenderCVars.DayLength.Value * 60f : 0f);
            }

            // The level's lamps, on every pass and on real elapsed time, for exactly the reasons the sweep
            // above is: a fitting takes a couple of hundred milliseconds to go out and about a second to
            // come back, and a fade advanced once per rendered frame would be a different fade on every
            // machine. Cheap enough to be unconditional -- a level nobody has touched walks an array of
            // steady lamps and returns.
            UpdateLamps(_loop.TimeSource.Milliseconds);

            // G shows the sun gizmo, but only while flying: it takes the cursor to drag its rings, which the
            // walking player needs for looking. Leaving the free camera puts it away.
            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.G) && !_playerMode)
            {
                _sunGizmo.Toggle();
            }

            if (_playerMode)
            {
                _sunGizmo.Hide();
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

                if (console.WantsPointer)
                {
                    // The developer console is a pointer-driven tool: its panels are dragged and
                    // resized. This branch comes first and is repeated every pass for the same reason
                    // the pause menu's is -- the lock below would otherwise be re-asserted a frame
                    // later and the cursor would snap back to the centre under the reader's hand.
                    // HoldStill matters just as much here: taking the cursor back on close warps the
                    // pointer, and without re-arming the settling frame that warp is read as aim.
                    // The system cursor stays hidden: the console draws its own, as the shell does,
                    // so the pointer looks the same everywhere and is drawn over the panels rather
                    // than by the compositor on top of them.
                    _player?.HoldStill();
                    _window.CursorLocked = false;
                }
                else if (MenuOwnsPointer(pauseMenu, _missionFailedMenu))
                {
                    // A paused view does not turn, and the cursor has to be free to reach the menu. The
                    // release is repeated every pass rather than done once on the way in, so nothing can
                    // quietly take the pointer back while the menu is up; it also re-arms the look's
                    // settling frame, so the warp that re-capturing the cursor produces on resume is not
                    // read as a movement the player made.
                    //
                    // The mission-failed screen belongs here for the same reason. It arrives while the
                    // player is dead and the death camera is orbiting, and EVERY branch below this one
                    // takes the pointer: the orbit, and then -- because _playerMode outlives the player --
                    // the look, whose Look() sets IsCapturingMouse and locks. The screen released the
                    // cursor each frame and the branch below re-took it, which is the pointer that
                    // flashed and would not move. Standing every camera branch down is what actually
                    // settles it; excluding them one at a time only moves the fight further down.
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
                else if (_player is { IsOnLadder: true }
                    or { IsInWorldInteraction: true, IsMountedStationaryGun: false })
                {
                    // A mounted gun is not among these: its look turns the barrel, so it falls through
                    // to the ordinary first-person look below.
                    //
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
                else if (_sunGizmo.Active)
                {
                    // The sun gizmo owns the cursor while it is up: free it so its rings can be dragged, and
                    // leave the camera where it is so a drag turns the sun rather than the view. The gizmo is
                    // a free-camera tool, so this only ever runs while flying.
                    _window.CursorLocked = false;
                }
                else
                {
                    _camera.Update(ViewInput, elapsed);

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
            // And the level's half of the warm-up: a withheld level must not run. Its intro cut-scene,
            // its patrols and its scripted clocks all start from the frame the player first SEES, not
            // from the frame the load finished -- a cut-scene that played out behind the loading screen
            // is one the player never gets to watch.
            if (_activeLevelRoot is { } pausableLevel)
            {
                pausableLevel.IsPaused = pauseMenu || PresentationHoldsLevel;
            }

            BeginStage();
            FrameOutcome outcome = _loop.RunFrame();
            lastOutcome = outcome;
            EndStageIfSlow("the loop's own frame (present, ticks, task tree)");

            // The number that says how long the player waited for a picture, said once. Everything above
            // it -- the runtime coming up, the window, the device, the swapchain, the first pipelines --
            // is time nobody can do anything during, and it is the only start-up figure that is about
            // what a person experiences rather than about where the CPU went.
            if (!firstFrameReported && outcome != FrameOutcome.Tick)
            {
                firstFrameReported = true;
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
            if (outcome == FrameOutcome.Tick && (_missionFailedMenu || pauseMenu || PresentationHoldsLevel))
            {
                continue;
            }

            if (outcome == FrameOutcome.Tick)
            {
                // The storm steps here rather than inside the player's own block, where the impact
                // particles step: weather is ambient, and it has to keep falling in the free camera and
                // while the player is down. Its spawn column follows the eye, so it is handed the
                // position the last frame drew from -- one frame stale is nothing to a column forty
                // metres across, and the tick has no camera of its own.
                _storm?.Tick(_stormEyeMetres);
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
            if (outcome is FrameOutcome.Tick && _elevators is { } elevators)
            {
                // Feed the player's current position to the elevators so their start gate can refuse
                // to move an empty car or one with an open door. Provider is read every tick.
                elevators.SetPlayerPositionProvider(_player is { } p ? () => p.Body.Position : null);
                elevators.Tick();
                UpdateElevatorSoundPositions();
                if (_worldSolids is { } elevatorSolids)
                {
                    elevators.ApplyCollision(elevatorSolids);
                }

                if (_playerMode && _player is { } passenger
                    && elevators.TryGetPassengerDisplacement(
                        passenger.Body.Position, _worldSolids, out Vector3d displacement))
                {
                    passenger.Body.Position += displacement;
                }
            }

            // The player cannot jump while standing inside an elevator cab. The elevator registry knows
            // the player's position (fed above), so it can report whether they're inside.
            if (_player is { } playerWithJump)
            {
                playerWithJump.CanJump = _elevators is { } jumpElevators
                    ? (Func<bool>)(() => !jumpElevators.IsPlayerInsideElevator(
                        playerWithJump.Body.Position, out _))
                    : null;
            }

            bool interactionsTickedByPlayer = false;
            if (outcome is FrameOutcome.Tick
                && _cutscenes?.IsPlaying != true
                && _playerMode && _player is not null)
            {
                // A dead player is in locomotion state 47, whose per-tick is not the walking one -- so the
                // body stops being stepped rather than being stepped with no input. Without this a corpse
                // walks around.
                if (!_playerDead && !_missionFailedMenu)
                {
                    _player.InputProfile = _configuration.Active;
                    GrantCheatWeapons(_player);
                    if (_computerOpen)
                    {
                        _player.TickMapComputer();
                    }
                    else
                    {
                        _player.Tick(ViewInput, _ground);
                        // PlayerCamera advances its interaction registry before it resolves this
                        // tick's activate target. Do not advance the same registry again below.
                        interactionsTickedByPlayer = true;
                    }
                    TickVehicleMissiles();

                    // Grenade physics runs inside PlayerCamera.Tick. IGI1 broadcasts the grenade task's
                    // AI events before the HumanAI pass, so deliver the queued records here rather than
                    // after SoldierRenderer.Tick (where they would be delayed and a guard could fire
                    // one extra burst). Effects and sounds are still resolved below, after the AI pass.
                    NotifyGrenadeAiEvents();

                    PlayBinocularTransitionSound();
                    PlayMedipackSound();

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
                    // FUN_0040FB30/0x40FB90 drives medipack animation tasks directly. A medipack is
                    // not a firearm, so never feed its weapon record through WeaponSoundPlayer: some
                    // live rows carry a legacy gun sound and that produced a SPAS-like report. Passing
                    // null also closes an automatic gun loop when the player switches to the syringe.
                    WeaponType? weaponForSound = _player.HeldWeaponType?.Category == WeaponCategory.Medipack
                        ? null
                        : _player.HeldWeaponType;
                    bool m203Launched = _player.M203LaunchedThisTick;
                    _weaponSounds?.Tick(
                        _player.FiredThisTick && !m203Launched && weaponForSound is not null,
                        weaponForSound);
                    if (m203Launched)
                    {
                        // GunM16A2's native callback (0x473510) plays this launch report itself; it is
                        // not the M16A2's primary rifle loop and must remain audible on an alternate shot.
                        // The native call attaches it to the launcher task position, not to the listener.
                        Vector3d launchPosition = _player.Body.Eye;
                        if (_player.Weapons?.M203Shots is { Count: > 0 } m203Shots)
                        {
                            launchPosition = m203Shots[0].Origin;
                        }

                        _sounds?.PlayDefinition("m203_launch_1", launchPosition, priority: 250);
                    }

                    // The grenade's cook tick: a looping sound held while the throw charges, stopped the
                    // moment it is released (0x40F590 starts it, 0x40F640 stops it).
                    TickGrenadeCook();

                    // Reloads and a few other weapon actions name their sounds through BOED animation
                    // events rather than through the weapon record. The rig advanced inside Player.Tick,
                    // so its crossed-event interval is ready to dispatch here.
                    // The native medipack callbacks dispatch their own clips and do not enter the
                    // generic firearm BOED trigger path. Suppress those generic triggers while the
                    // medipack is held, otherwise a shared animation event can sound like reload/fire.
                    if (_player.HeldWeaponType?.Category != WeaponCategory.Medipack)
                    {
                        PlayPlayerAnimationSounds();
                    }

                    // State 28's entry (0x40BBF7) plays jump_1 through 0x4DD750 on the take-off tick.
                    if (_player.Body.TookOffThisTick)
                    {
                        PlayJumpSound();
                    }

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
                    PlayBulletImpactSounds();
                    PlayWeaponDropSounds();

                    // The same impacts, seen: sparks and dust composed from the struck material.
                    // Ticked before spawning so a new burst draws its birth frame unmoved.
                    _impactParticles?.Tick();
                    _explosions?.Tick();
                    _missileSmoke?.Tick();

                    // The original's own effects, on the same rhythm: the dust and chips step, and the
                    // blood droplets in the air are traced against the world, marking whatever they
                    // land on. Ticked before this tick's impacts spawn, so a new puff is drawn where
                    // it was born.
                    TickRetailImpactEffects();

                    SpawnBallisticImpactParticles();
                    SpawnRetailImpactEffects();

                    // The launcher rockets in the air lay their smoke trail, one column of puffs per
                    // segment they crossed this tick.
                    SpawnMissileSmoke();

                    // Whatever grenades ran out of fuse this tick -- the blast, the flash, the noise
                    // and the casualties.
                    ResolveGrenadeDetonations();
                    ResolveGenericTbaExplosions();
                    ResolveMissileDetonations();
                    ResolveM203Detonations();
                    PlayGrenadeBounces();

                    // The alarm speakers, following their own level expressions -- the siren the
                    // room hears when a guard reaches a switch.
                    if (_destructibles is { } destructibles)
                    {
                        destructibles.Tick();
                        foreach (DestructibleObject ready in destructibles.ConsumeReadyExplosions())
                        {
                            HandleDestructibleImpact(ready);
                        }
                        foreach (VehicleBlast blast in destructibles.ConsumeVehicleBlasts())
                        {
                            PresentVehicleBlast(blast);
                        }
                        foreach (DestructibleObject car in destructibles.ConsumeExplodedVehicles())
                        {
                            ForgetVehicleMarks(destructibles, car);
                        }
                    }
                    TickAlarmSpeakers();
                    TickSecurityCameraSounds();

                    // And whether one of this tick's rounds landed ON a speaker, pane or barrel. The
                    // impact list carries both the instant rays and the ballistic arrivals, so either
                    // mode can shoot the siren off the wall.
                    foreach (GunImpact playerImpact in _player.Impacts)
                    {
                        if (playerImpact.HitSomething)
                        {
                            ApplyRoundContact(
                                playerImpact.Position,
                                playerImpact.Owner,
                                playerImpact.Placement,
                                _player.HeldWeaponType?.DamageFactor ?? 1f);
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

            // PlayerCamera owns the normal player tick, including interaction pulses. Other fixed-tick
            // modes (free camera, cutscenes, map computer and a dead player) still need their level
            // interactions to advance, but never more than once per simulation tick.
            if (outcome is FrameOutcome.Tick && !interactionsTickedByPlayer)
            {
                _interactions?.Tick();
            }

            // Lifts advance before perception and AI consume this tick's player position. A passenger
            // standing on the previous platform sample receives the same displacement, which keeps the
            // body, camera and collision contact together while the platform travels.
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
                _flashbangScreenFlash.Tick();
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
                // Mission 14 streams 108 of its 120 building tasks through AreaActivate volumes. During
                // an authored cinematic the visible occupant is the cutscene camera rather than the
                // stationary player body at the gameplay spawn. A diagnostic free camera is different:
                // when a player body exists it must not impersonate that player and satisfy gameplay
                // gates. Mission 4 AreaActivate_156 otherwise starts the extraction/outro merely because
                // the free camera follows Bell 800 onto the warehouse landing.
                Vector3d? cutsceneViewMetres = _cutscenes?.Frame?.Camera.Position;
                Vector3d? freeViewMetres = !_playerMode ? _camera?.Position : null;
                _missionObjectives?.SetSoldierOccupants(_soldiers?.Occupants() ?? []);
                _missionObjectives?.SetVehicleOccupants(_cutscenes?.VehicleOccupants() ?? []);
                TickAreaActivations(
                    _missionObjectives,
                    _player?.Body.Position,
                    _soldiers?.PlayerTeam ?? 0,
                    cutsceneViewMetres,
                    freeViewMetres,
                    playerAlive: !_playerDead);

                // Test-only outro forcing: re-apply the env-specified gate writes every tick so gates
                // whose variables the level scripts reset stay satisfied while the scene plays.
                foreach ((string writeName, string writeValue) in _testOutroTickWrites)
                {
                    _loop.Variables.TryWrite(
                        writeName,
                        QvmValue.FromInt32((int)double.Parse(writeValue, System.Globalization.CultureInfo.InvariantCulture)));
                }
                _cutscenes?.FinishExpressionTick();

                // The level's fans and radar heads, one tick further round. Unconditional and cheap: it
                // is a counter, and the renderer turns the transform from it when it draws.
                _spins?.Advance();
                if (_securityCameras is { } cameras)
                {
                    bool crouching = _playerMode && _player is not null && _player.IsCrouched;

                    // Retail's lenses scan every human and every camera (0x489750): a guard's body or a
                    // destroyed camera in view raises the alarm at once, as the player does after 90 ticks.
                    cameras.Tick(
                        watchedFeet,
                        crouching,
                        targetAlive: !_playerDead,
                        playerOnCamerasSide: (_soldiers?.PlayerTeam ?? 0) == 1,
                        _soldiers?.CameraSubjects() ?? []);
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

                if (_rotatingObjects is { } rotatingObjects)
                {
                    rotatingObjects.Tick();
                    if (_worldSolids is { } rotatingSolids)
                    {
                        rotatingObjects.ApplyCollision(rotatingSolids);
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
                        climbUp, climbDown, climbSlide, _ground!);
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
                                    _player.CompleteLadderTopTransition(_ground!);
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
                                    _player.TickLadderSlide(slideDelta, _ground!);
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

                    if (interacting.IsMountedStationaryGun)
                    {
                        // The gun's sight sits just in front of the gunner's own chest, and retail draws
                        // no body in that view (captured in mission 4: barrel, stand and scenery only).
                        _soldiers?.HidePlayerBodyClimbing();
                    }
                    else if (_soldiers is not null)
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

                    // State 33 is the one interaction that keeps a first-person camera: the gun hands
                    // the view its own sight (0x474420), so no orbit is started behind the body.
                    if (interacting.IsMountedStationaryGun)
                    {
                        interactionCamera.Reset();
                    }
                    else if (!interactionCamera.IsActive)
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

                    if (interactionCamera.IsActive)
                    {
                        interactionCamera.Tick();
                    }
                }

                if ((_player is null || (!_player.IsOnLadder && !_player.IsInWorldInteraction))
                    && (climbActive || worldInteractionActive || interactionCamera.IsActive))
                {
                    _soldiers?.HidePlayerBodyClimbing();
                    interactionCamera.Reset();
                    climbActive = false;
                    worldInteractionActive = false;
                }

                // The character the third-person view looks at, walked with the player on the same tick
                // their own legs are -- see SoldierRenderer.FollowPlayerBody for why it must be the tick
                // and not the frame. Kept running for the whole of the move back as well as out, so the
                // body is already in step whenever the camera is far enough away to show it.
                //
                // The states with a body of their own are left alone: the ladder and the world
                // interactions place and pose the same rig on their own schedule, and the death path owns
                // it outright once the player has fallen.
                if (_soldiers is { } followedBody)
                {
                    // The same conditions the camera itself is eligible under, and deliberately so: the
                    // follow owns the torso clip and the aim bend, and every state excluded here has an
                    // authored full-body clip or a camera of its own that must own the whole character.
                    if (_playerMode
                        && !_playerDead
                        && !_missionFailedMenu
                        && !_computerOpen
                        && !_entry.IsCameraActive
                        && _cutscenes?.IsPlaying != true
                        && (!menuMode || pauseMenu)
                        && _player is { IsOnLadder: false, IsInWorldInteraction: false } walkingBody)

                        // ...and in FIRST person too, unconditionally, which it did not used to be.
                        //
                        // The follow is what POSES the body; the separate visibility latch below is what
                        // draws it. Those were both held shut in first person, so the body did not exist
                        // at all -- and geometry that does not exist cannot be struck by a reflected ray,
                        // whatever the reflection is then able to do with the hit. A window could not
                        // show the player because there was no player to show.
                        //
                        // So it is posed always and drawn as before. What that costs is one character
                        // skinned per frame; what it buys is the player's own reflection and shadow,
                        // through DrawPlayerBody's traced-only path.
                    {
                        followedBody.FollowPlayerBody(
                            walkingBody.Body.Position,

                            // MINUS the yaw. A body's heading is a GAMMA and the look is a YAW, and the
                            // two run opposite ways: PlayerBody.FacingBasis builds the body's own basis
                            // as RotationZ(-yaw), and every other caller in the port converts the same
                            // way (PlayerCamera writes `_body.Yaw = -facing` on entering an interaction
                            // and `_interactionFacing = -_body.Yaw` on leaving one). Passing the yaw
                            // straight through mirrors the character about the world's X axis, which is
                            // a body that faces the camera at exactly the moment it should face away.
                            -walkingBody.Body.Yaw,
                            walkingBody.LocomotionAnimation,
                            walkingBody.LocomotionAnimationLoops,
                            new PlayerBodyPose(
                                walkingBody.UpperBodyAnimation,
                                walkingBody.UpperBodyAnimationLoops,
                                walkingBody.UpperBodyBlendSeconds,
                                walkingBody.UpperBodySequence,
                                walkingBody.AimPitch));
                    }
                    else
                    {
                        followedBody.HidePlayerBodyFollowing();
                    }
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
                    if (!_missionFailedMenu && ++_missionFailureTicks > 151 && shell is not null)
                    {
                        _missionFailedMenu = true;

                        // Said out loud because the level decided this, not the player: a LevelFlow
                        // 'Failed' expression is authored out of message and area state, and when it
                        // turns for a reason nobody in the room understands this line is where the
                        // reading starts.
                        _logger.Information(
                            "Mission",
                            $"The level's LevelFlow has failed the mission; opening the failed page.");
                        _computerOpen = false;
                        _computerFlight.Reset();
                        _player?.SetMapComputerOpen(false);

                        // The result is a shell page, not a drawn card, so its two choices can be clicked
                        // and navigated like every other page and the shell's own cursor is the one on
                        // screen. menuMode hands it the frame's input and draw; pauseMenu holds the
                        // showcase off, because the level this is drawn over is still loaded -- the same
                        // pair the pause menu sets.
                        menuMode = true;
                        pauseMenu = true;
                        shell.OpenMissionFailed();

                        // The platform cursor stays hidden: the shell draws its own, so revealing this one
                        // would put a second, unrelated pointer beside it.
                        _window.CursorVisible = false;
                        _player?.HoldStill();
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
                    // A successor the catalogue does not offer ends the campaign the way the last mission
                    // does -- in a build cut to the opening missions, the last one offered is its finale.
                    int nextMissionId = completedMission.NextMissionId;
                    if (nextMissionId != MissionDescriptor.NextMissionUndefined && _missions.TryGet(nextMissionId, out _))
                    {
                        if (_configuration.Active is { } activeProfile)
                        {
                            MissionProgression.UnlockNext(activeProfile, _missions, nextMissionId);
                            // Retail writes LOCAL:config.qsc after every non-final completion, even when
                            // replaying an earlier mission leaves the frontier unchanged.
                            HostConfiguration.Save(_configuration, _logger);
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

            lastPassRendered = true;

            if (_options.IsBenchmark && _loop.Clock.FrameCount == BenchmarkSchedule.WarmUpFrames)
            {
                framePerformance.Reset();
                _benchmarkAllocStart = GC.GetTotalAllocatedBytes();
                _benchmarkGcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
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

                    // -flightframes shortens the move without shortening the capture, so the frames past it
                    // hold the end pose and the flight comes to a stop. The clamp is what holds it.
                    int span = Math.Max(
                        1,
                        (_options.FlightFrames
                            ?? (_options.IsBenchmark ? BenchmarkSchedule.MeasurementEndFrame : _options.ExitAfterFrames)) - 1);
                    float t = Math.Clamp((float)(flightFrame - _options.FlightDelay) / span, 0f, 1f);

                    Vector3d endPos = _options.CameraEnd is { } e
                        ? new Vector3d(e.X, e.Y, e.Z)
                        : flightStartPos;
                    float endYaw = _options.CameraYawEnd ?? flightStartYaw;
                    float endPitch = _options.CameraPitchEnd ?? flightStartPitch;

                    (float shakeYaw, float shakePitch) = HandheldShake(flightFrame, _options.CameraShake);

                    _camera.MoveTo(
                        new Vector3d(
                            flightStartPos.X + ((endPos.X - flightStartPos.X) * t),
                            flightStartPos.Y + ((endPos.Y - flightStartPos.Y) * t),
                            flightStartPos.Z + ((endPos.Z - flightStartPos.Z) * t)),
                        flightStartYaw + ((endYaw - flightStartYaw) * t) + shakeYaw,
                        flightStartPitch + ((endPitch - flightStartPitch) * t) + shakePitch);
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
                double pacingWall = _loop.Clock.FrameInstantMilliseconds;
                float tickBlend = smoothPresentation
                    ? _loop.Clock.TickFraction(pacingWall)
                    : 1f;

                // Whether the picture's own clock keeps step with the wall clock, behind
                // OPENIGI_PACING_DEBUG=1. Sampled here because this pair -- the tick and the blend --
                // is exactly the moment in the simulation the frame is about to be drawn at.
                Diagnostics.PresentationPacingProbe.Sample(
                    pacingWall, _loop.Clock.TickCount, tickBlend, _loop.Clock.TickRate, smoothPresentation);
                Diagnostics.PresentationPacingProbe.SampleGate(
                    pauseMenu,
                    _cutscenes?.IsPlaying == true,
                    _options.Lockstep,
                    RenderCVars.SmoothPresentation.Value);
                if (_loop.Clock.FrameCount % 600 == 0)
                {
                    Diagnostics.PresentationPacingProbe.Report("vulkan");
                }
                SightZoomFrame sight = _playerMode && _player is not null
                    ? _player.SmoothedSightZoom(tickBlend)
                    : new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

                float aspectRatio = (float)_window.Width / Math.Max(1, _window.Height);
                // A window that reports no size at all -- which some window systems do for a frame
                // during a minimize or a dock -- would divide the projection by zero.
                if (aspectRatio <= 0f)
                {
                    aspectRatio = 1.3333f;
                }

                // A pinned lens for reference comparisons: -fov overrides the game's own ninety-degree
                // horizontal, so a capture can be framed exactly as a reference render whose camera
                // differs. The sight zoom still governs ordinary play.
                float horizontalTangent = _options.FieldOfView is { } fixedFov
                    ? MathF.Tan(fixedFov * (MathF.PI / 360f))
                    : sight.TanX;
                float verticalFieldOfView = VerticalFieldOfView(aspectRatio, horizontalTangent);

                // Kept for the developer inspector, which projects a selected entity to the screen and
                // has no other way to know the lens the frame was actually drawn with.
                _inspectorTangent = horizontalTangent;
                _inspectorAspect = aspectRatio;
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

                // The binocular glass, raised on its own wall clock so the lens fades in at the same
                // rate whatever the frame rate. Gated on the device being open (the fade sits at 0
                // otherwise) and the lens cvar; the map tube above and this glass are never both up.
                // How long this frame took, for the shutter the motion blur measures its smear against.
                int motionNow = _loop.TimeSource.Milliseconds;
                float frameDeltaSeconds =
                    Math.Clamp((motionNow - _lastMotionBlurMilliseconds) / 1000f, 1f / 1000f, 1f / 10f);
                _lastMotionBlurMilliseconds = motionNow;

                int binocularNow = _loop.TimeSource.Milliseconds;
                float binocularElapsed = Math.Clamp((binocularNow - _lastBinocularMilliseconds) / 1000f, 0f, 0.1f);
                _lastBinocularMilliseconds = binocularNow;
                _binocularOptics.Update(_player is { IsBinocularsOpen: true }, binocularElapsed);
                BinocularDisplaySettings binocularDisplay =
                    RenderCVars.BinocularLens.Value ? _binocularOptics.Display : default;
                // A little extra edge fringing while the optics are up, on top of the ordinary value, so
                // the lens carries chromatic aberration that ramps in with the glass.
                float binocularAberration = binocularDisplay.Amount * 2f;
                PostProcessSettings postProcess = new(
                    RenderCVars.ToneMap.Value,

                    // The player's brightness less whatever the night is taking off it; see
                    // RenderCVars.EffectiveExposure. Never r_exposure directly -- the night must not be
                    // able to edit, and then have to put back, the setting the player chose.
                    RenderCVars.EffectiveExposure,
                    RenderCVars.Bloom.Value,
                    RenderCVars.BloomThreshold.Value,
                    RenderCVars.WhitePoint.Value,
                    // The dynamic model now composites in linear space -- albedo decoded before the multiply
                    // -- so its scene buffer holds linear radiance and the resolve tone maps it directly. The
                    // baked and vanilla models keep the original's gamma-space compositing, whose buffer is
                    // display-encoded and has to be decoded before the tone map.
                    SceneIsDisplayEncoded: EffectiveLighting != LightingModel.Dynamic,
                    Contrast: RenderCVars.Contrast.Value,
                    Saturation: RenderCVars.Saturation.Value,
                    Vignette: RenderCVars.Vignette.Value,
                    Grain: RenderCVars.Grain.Value,
                    ChromaticAberration: MathF.Max(RenderCVars.ChromaticAberration.Value, binocularAberration),
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
                    Tactical: computerDisplay,
                    Binocular: binocularDisplay,

                    // Fast while the GI is still filling in after a load, slow once it has settled. Counted
                    // down here so a frame that never reaches the cache update (a menu, a paused load) does
                    // not burn the window; the cache's own reset above is what refills it.
                    ExposureRate: _giExposureWarmup > 0 ? GiExposureWarmupRate : ExposureSteadyRate,

                    // Read off the loop's own time source rather than the wall clock, which is what makes
                    // a -lockstep capture reproducible: that mode swaps in a synthetic clock advanced a
                    // fixed amount per pass, so every captured frame smears by exactly the same length.
                    // Clamped because a frame that stalled on a load should not smear the whole screen.
                    FrameDeltaSeconds: frameDeltaSeconds,

                    // The eye's night vision, only as deep as the night: a day is untouched.
                    NightShift: RenderCVars.NightShift.Value * RenderCVars.Nightfall.Value);

                if (_giExposureWarmup > 0)
                {
                    _giExposureWarmup--;
                }
                if (mapScene is { } mapView)
                {
                    aspectRatio = (float)mapView.Viewport.Width / Math.Max(1, mapView.Viewport.Height);
                    if (aspectRatio <= 0f)
                    {
                        aspectRatio = 1.3333f;
                    }

                    verticalFieldOfView = mapView.FieldOfView;
                }
                // Both planes are the original's own: 0x493580 initialises the near one to 409.6 units,
                // which is 0.1 m, and the far one to 409,600,000, which is 100 km. (Scripts can move the
                // near plane through the SetNearClippingPlane native, 0x497D00.) At 0.2 the near plane
                // reached twice as far as retail's and cut into any wall the sweep lets the body stand
                // 2.5 mm from; at the 4000 m the far plane carried since the first fly-through, every
                // mountain past four kilometres was clipped away. See ViewPlanes.
                Matrix4f unjitteredProjection = Matrix4f.CreatePerspective(
                    verticalFieldOfView,
                    aspectRatio,
                    near: NearPlane,
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

                // The over-the-shoulder view, built off whatever camera the player would have had. It is
                // applied HERE, on the finished first-person eye and before anything reads the eye back,
                // so the capture log, the aim projection below and every pass of the frame all agree on
                // where the picture was taken from.
                //
                // The four states that own the view outright are excluded rather than blended out of: a
                // cutscene, the entry shot, the map computer and the menu orbit each replace the eye
                // below, and running the move underneath one of them would only decide where the camera
                // was when it was handed back.
                //
                // The front end is excluded but the PAUSE is not, and the difference is the whole reason
                // the test is written this way: a paused frame is still the player's own view of the level
                // they are standing in, so the camera stays where it was and simply stops moving, while
                // the menu's showcase orbit is somewhere else entirely and there is nothing to preserve.
                bool thirdPersonEligible = _playerMode
                    && !_playerDead
                    && !_missionFailedMenu
                    && !_computerOpen
                    && !_entry.IsCameraActive
                    && _cutscenes?.IsPlaying != true
                    && mapScene is null
                    && (!menuMode || pauseMenu)
                    && _player is { IsOnLadder: false, IsInWorldInteraction: false };

                if (thirdPersonEligible && _player is { } thirdPersonPlayer)
                {
                    int thirdPersonNow = _loop.TimeSource.Milliseconds;
                    float thirdPersonElapsed =
                        Math.Clamp((thirdPersonNow - _thirdPersonMilliseconds) / 1000f, 0f, 0.1f);
                    _thirdPersonMilliseconds = thirdPersonNow;

                    _thirdPerson.DistanceInMetres = GameplayCVars.ThirdPersonDistance.Value;
                    _thirdPerson.ShoulderInMetres = GameplayCVars.ThirdPersonShoulder.Value;
                    _thirdPerson.HeightInMetres = GameplayCVars.ThirdPersonHeight.Value;
                    _thirdPerson.BlendSeconds = GameplayCVars.ThirdPersonBlend.Value;

                    // Optics are a first-person act. Raising the binoculars or leaning into the Dragunov's
                    // scope replaces the picture with what the eye sees, so the camera comes back to the
                    // eye first -- by the same move, not by a cut -- and goes out again on the way down.
                    bool wantsThirdPerson = GameplayCVars.ThirdPerson.Value
                        && thirdPersonPlayer is { IsBinocularsOpen: false, IsSniperScoped: false };

                    // A paused view does not move. Advancing the blend behind the menu would have the
                    // camera quietly travel while the player was reading, and arrive somewhere else.
                    eye = _thirdPerson.Update(
                        menuMode ? 0f : thirdPersonElapsed,
                        wantsThirdPerson,
                        eye,
                        ThirdPersonWalls());

                    ConvergeThirdPersonAim(eye);
                }
                else
                {
                    // A cut, not a move: the view was taken by something that put the camera somewhere
                    // else entirely, and interpolating back from there draws a frame from a line between
                    // two unrelated places.
                    _thirdPerson.Reset();
                    _thirdPersonMilliseconds = _loop.TimeSource.Milliseconds;
                    if (_player is not null)
                    {
                        _player.AimConvergenceRay = null;
                    }
                }

                // Whether the character is drawn at all, read here rather than at the draw because the
                // SHADOW cascades run before it: deciding at the geometry pass left the body's shadow a
                // frame behind the body on the frame it appeared.
                //
                // TWO gates, and both have to open. The follow above places and animates the body; THIS
                // is what actually draws it, and the third-person camera holds it shut until it has left
                // the head far enough for a body to be a body rather than the inside of a skull filling
                // the screen. r_player_body overrides that for first person deliberately -- see the cvar
                // for what it buys and what it costs.
                bool thirdPersonBody = _thirdPerson.ShowsBody || RenderCVars.PlayerBody.Value;
                if (_soldiers is { } shownBody)
                {
                    shownBody.PlayerBodyFollowVisible = thirdPersonBody;
                }

                // The pose the frame is actually drawn from, whichever camera supplied it, in the form
                // -campos/-yaw/-pitch take. Without this a viewpoint reached by WALKING to it cannot be
                // captured: the free camera opens over the middle of the level, so the two modes can only
                // ever be compared at different places -- which is exactly the confusion that hid a
                // difference between them.
                if (_options.LogCamera)
                {
                    Vector3f eyeForward = eye.Orientation.Row3;
                    float eyePitch = MathF.Asin(Math.Clamp(eyeForward.Z, -1f, 1f));
                    float eyeYaw = MathF.Atan2(eyeForward.X, eyeForward.Y);

                    // The sun rides along, because the pose alone does not reproduce a viewpoint any more.
                    // The day/night keys sweep the sun's elevation AND its bearing, so a scene reached by
                    // holding one of them cannot be captured from -campos/-yaw/-pitch: the harness would
                    // render the authored noon sun and light a different room. With this the whole line
                    // pastes onto a capture command and the frame comes back as it was seen. One frame
                    // stale while the sun is being scrubbed, which does not matter for standing still.
                    Vector3f loggedSun = _effectiveSun?.Direction ?? Vector3f.UnitZ;

                    // Warning rather than Information, which reads wrong and is not: the shell's logger
                    // is built at Warning, so every Information call in the app is discarded and this
                    // flag produced no output at all -- the flag existed and did nothing. It is opt-in
                    // and its whole purpose is to be read, so it has to be emitted at a level that
                    // survives. The same is true of the older mouse-look line just above.
                    _logger.Warning(
                        "Camera",
                        $"eye {(_playerMode ? "player" : "free")} -campos {eye.Position.X:0.###},{eye.Position.Y:0.###},"
                            + $"{eye.Position.Z:0.###} -yaw {eyeYaw:0.#####} -pitch {eyePitch:0.#####}"
                            + $" -sundir {loggedSun.X:0.######},{loggedSun.Y:0.######},{loggedSun.Z:0.######}");
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

                // The eye has settled by here -- every camera that could have claimed the frame has
                // had its say -- so this is where the inspector's projection can read it.
                _inspectorEye = eye;

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
                    GBuffer: (EffectiveLighting != LightingModel.Dynamic
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
                // The authored sun swept to the current time of day. Held for the frame so every consumer
                // -- this deferred light, the shadows, the surface cache's relight and its radiance cache,
                // and the sky fill -- lights from the same swept sun rather than the raw authored one.
                // The sun -- or the moon -- this frame lights with. See CycleSun: the same answer the surface
                // cache is converged under, so the two cannot light the level differently.
                DirectionalLightSettings light = CycleSun();

                // What the night is doing to the picture, published for the exposure. The sweep is the only
                // thing that knows how far under the horizon the sun has gone, and the resolve is a long way
                // from here, so it travels as the one hidden variable rather than as another argument
                // threaded through every pass between. The fixed-sun path leaves it at nought, which is what
                // a reference render wants: a pinned sun and an unmodified exposure.
                RenderCVars.Nightfall.Value = _dayNight.Nightfall;

                // And what the LEVEL says about its own darkness, which the adaptation cannot work out
                // for itself: a scene lit dimly all over reads to it as a correctly-exposed dark subject.
                //
                // The SWEEP's reading, not the level's own constant. It used to be the constant, which
                // pinned the exposure to the hour the level opened at: scrub a night mission to noon and
                // the sun came up while the picture stayed exposed for midnight. A title that authors its
                // own night is bound to the cycle by it, so the cycle's own reading is the level's -- and
                // it moves with the clock, which is the whole point of having one.
                //
                // And ONLY under the dynamic model, because that is the only one it describes. The drop
                // answers an eye adaptation that misreads a scene whose light is COMPUTED at low levels
                // and lifts the whole thing back to mid grey. A model that reproduces the level's own
                // composited picture computes nothing to be misread: the picture is what the artists made,
                // and re-exposing it for a night it already contains is subtracting the same night twice.
                // Fitted for the dynamic model, it crushed the other two -- mission 1's baked ground came
                // out at 0.18 of vanilla where the dynamic one landed on 0.93.
                //
                // The REQUESTED model, not EffectiveLighting. That one reports Baked until the real-time
                // structures finish building, so reading it here would move the exposure partway through a
                // load -- and the adaptation that rides on it is temporal, so the step would be chased
                // rather than taken. What the exposure follows has to be a decision the player made, not a
                // state the loader is in.
                RenderCVars.AuthoredNightfall.Value =
                    _deferredLighting is null || RenderCVars.Lighting.Value != LightingModel.Dynamic
                        ? 0f
                        : _dayNight.Nightfall;

                // And how much a full night of it is worth, which is the title's to say: see
                // DeferredLighting.NightExposureDrop.
                RenderCVars.AuthoredNightDropFor.Value =
                    _deferredLighting?.NightExposureDrop ?? RenderCVars.AuthoredNightDrop;

                _effectiveSun = light;
                Vector3f viewSunDirection = eye.Orientation.Transposed().Transform(light.Direction);

                // The sun gizmo, if it is up: projected against this frame's camera and dragged this frame,
                // so there is no lag between the ring under the cursor and the sun it turns. Its rotation
                // feeds straight back into the cycle, the same knob the numpad keys turn.
                _sunGizmo.Update(
                    eye,
                    DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
                    light.Direction,
                    _window.Width,
                    _window.Height,
                    _window.Input.Mouse.X,
                    _window.Input.Mouse.Y,
                    _window.Input.Mouse.IsDown(Input.MouseButton.Left),
                    (axis, delta) => _dayNight.RotateAxis(axis, delta));

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

                        // The live software/hardware switch: off, a tracing backend leaves the sun to the
                        // cascaded map like everyone else. r_rt_shadows moves the sun's shadow on its own,
                        // leaving every other trace in the frame alone, which is what makes the two shadow
                        // systems comparable in place.
                        TracedSun = RenderCVars.HardwareRayTracing.Value && RenderCVars.TracedSunShadows.Value,

                        // The day/night cycle fades the colour to exactly nought a tenth below the
                        // horizon; from there every sun-shadow mask is multiplied by zero, and the whole
                        // pipeline -- trace, denoise, cascades -- can stand down. Colour, not
                        // IsBelowHorizon: through twilight a dim sun still lights and still shadows.
                        SunLit = light.Colour.X > 0f || light.Colour.Y > 0f || light.Colour.Z > 0f,
                    }
                    : ShadowSettings.Off;

                int cascadeCount = ShadowCascades.Fit(
                    eye,
                    light.Direction,
                    shadowSettings,
                    verticalFieldOfView,
                    aspectRatio,
                    NearPlane,
                    cascadeStorage);

                _device.SetShadows(
                    shadowSettings,
                    cascadeStorage.AsSpan(0, cascadeCount),
                    DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),

                    // View space back into camera-relative world space, which is the frame the cascades
                    // are fitted in. The same transpose, for the same reason.
                    eye.Orientation.Transposed());

                // The medium. Density comes from the level's own FlatSky "Fog Amount" unless overridden:
                // a level that asked for thick fog gets thick fog, so the missions keep their own moods
                // instead of all being handed one number. The colours come from the sun, so a fog bank is
                // lit by the same light as the ground under it.
                // Lighter than it was: the authored FogAmount, read at full strength, laid a haze dense
                // enough to wash the yard white a few tens of metres out -- where the reference keeps its
                // buildings crisp -- and it hung inside rooms besides. This carries the same shape at a
                // fraction of the extinction, so distance still reads but the far wall of a compound stays
                // legible.
                // A title that authors a real extinction gives it here, in the same units, and is
                // believed. I.G.I. 2's GlobalLight carries one per level -- mission 1 asks for 0.00174
                // per metre -- where the curve below is a fit over Project I.G.I.'s FlatSky "Fog Amount",
                // which is a 0-to-1 dial and not a length at all. Run through that fit the sequel's first
                // mission came out at 0.0070 per metre, four times its own figure, and the frame was a
                // blue wash with the buildings barely in it.
                float authoredDensity = _atmosphere is { FogDensity: > 0f } own
                    ? own.FogDensity
                    : _sky is { HasSky: true }
                        ? 0.002f + (Math.Clamp(_sky.FogAmount, 0f, 1f) * 0.010f)
                        : 0.006f;

                // Fog is an outdoor medium, and where it is outdoors is decided a froxel at a time inside
                // the march itself -- see the shelter lookup in the FogScatter shader module. It used to be
                // decided once for the whole frame, standing the march down while the camera was inside a
                // building, because the haze sitting between the eye and a window washed out the yard
                // beyond it: fog marched straight through the wall. Switching the march off answers that
                // by deleting the yard's fog too, so a compound seen from a doorway came up in clear air.
                // The room is now dry and the yard beyond the window is not, which is what is actually
                // out there.
                bool wantsFog = RenderCVars.FogQuality.Value > 0
                    && RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value;

                // The mission actually on screen, as the colour grade reads it. This read -mission's id, so a
                // mission entered from the campaign -- where that is nought -- was drawn in the generic
                // default haze instead of its own tuned fog, and looked different from the same mission
                // started any other way, a renderer switch included.
                int fogMission = _activeMission?.Id ?? _backdrop?.MissionId ?? _options.StartMission;

                VolumetricFogSettings medium = VolumetricFogSettings.Off;
                if (wantsFog)
                {
                    // The sky plane's height is an offset above the ground, not a world Z: a level's
                    // coordinates carry a kilometres-large absolute offset, so it has to be anchored to
                    // the scene's own ground before the march can compare a sample against it.
                    medium = fogPresets.Resolve(
                        fogMission,
                        authoredDensity,
                        (float)(_scene.GroundHeight + (_sky is { HasSky: true } ? _sky.PlaneHeight : 0f)),
                        RenderCVars.FogQuality.Value);

                    // A console override beats the authored preset, so a value can be dialled in live and
                    // then written back into the file.
                    //
                    // "Overridden" is the variable having been moved off its own default, which is why
                    // the graphics presets pin all four of these to their defaults on every tier: a
                    // player choosing Ultra has not asked for one mission's authored atmosphere to be
                    // replaced by a global number, and a preset that assigned one could not be told from
                    // a player who had. Picking a preset therefore CLEARS an override and hands the
                    // missions back their own air, which is the behaviour the settings page implies.
                    // The haze is the colour the level says it is. Without this the medium scatters a
                    // near-white droplet albedo lit by the sun, which for a night mission whose own fog is
                    // a dark blue (0.00, 0.16, 0.29) is a pale daylight haze over a dark scene.
                    if (_atmosphere is { } tint && tint.FogColour.LengthSquared > 0f)
                    {
                        medium = medium with { Albedo = tint.FogColour };
                    }

                    if (RenderCVars.FogDensity.Value > 0f)
                    {
                        medium = medium with { Density = RenderCVars.FogDensity.Value };
                    }

                    if (!RenderCVars.FogHeightFalloff.IsDefault)
                    {
                        medium = medium with { HeightFalloff = RenderCVars.FogHeightFalloff.Value };
                    }

                    if (!RenderCVars.FogAnisotropy.IsDefault)
                    {
                        medium = medium with { Anisotropy = RenderCVars.FogAnisotropy.Value };
                    }

                    if (!RenderCVars.FogDistance.IsDefault)
                    {
                        medium = medium with { Distance = RenderCVars.FogDistance.Value };
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
                    // The real-time GI's compute runs here, before the G-buffer, so this is where it is
                    // timed. Without this bracket the bounce below -- the frame's single most expensive pass
                    // when real-time GI is on -- was measured by nothing, and the overlay's total was a frame
                    // that did not exist.
                    _device.BeginStageTiming(RenderStage.GlobalIllumination);

                    // Scrolls the clipmaps to follow the camera, rebuilding whichever level it walked out
                    // of. How many rebuilt is the retrace's "did the cache's coverage actually change"
                    // signal: nought means the camera only turned or drifted within a cell, and a
                    // view-independent cache needs no retrace for that -- which is what lets it idle at rest.
                    int giRebuilt = liveField.Update(eye.Position);

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
                        && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 }

                        // Not the first qualifying frame. On a backend that records a frame's compute and
                        // submits it at the end, a readback taken on the same frame as the dispatches sees
                        // the atlas as it was before them -- which reads exactly like a bounce that
                        // produced nothing, and cost a long detour to tell apart from one.
                        && _cardBounceFrame >= 8;

                    // Whether the surface cache's inputs actually changed this frame: a level scrolled new
                    // coverage in, the sun was scrubbed, or a dump was asked for. Turning or nudging the
                    // camera changes nothing a view-independent cache holds, so it does not count -- which is
                    // what lets the retrace idle while the player stands roughly still. The sun is fixed
                    // unless [ or ] is held, so a plain per-frame compare catches a scrub and nothing else.
                    bool giSunMoved = !_giSunValid
                        || MathF.Abs(light.Direction.X - _lastGiSunDirection.X) > 1e-4f
                        || MathF.Abs(light.Direction.Y - _lastGiSunDirection.Y) > 1e-4f
                        || MathF.Abs(light.Direction.Z - _lastGiSunDirection.Z) > 1e-4f
                        || MathF.Abs(light.Colour.X - _lastGiSunColour.X) > 1e-4f
                        || MathF.Abs(light.Colour.Y - _lastGiSunColour.Y) > 1e-4f
                        || MathF.Abs(light.Colour.Z - _lastGiSunColour.Z) > 1e-4f;
                    _lastGiSunDirection = light.Direction;
                    _lastGiSunColour = light.Colour;
                    _giSunValid = true;

                    // Reset the settle counter on any real change; otherwise let it climb. The retrace runs
                    // while something is moving and for a settling window after, then idles -- the cache
                    // keeps its converged atlas with none of the ray tracing, so a still scene stops paying
                    // the periodic covering-slice spike that halved its frame rate.
                    bool giChanged = wantDump || giRebuilt > 0 || giSunMoved;
                    // Long enough for the cache to actually converge before it stops: a tile is traced once
                    // per amortisation spread and moves 1/(1+temporal frames) of the way each time, so its
                    // time constant is spread x (1 + frames) -- some eight hundred frames at every modern
                    // preset. A fixed 64 froze it a few traces in, with most of the error still there.
                    int giSettle = Math.Max(
                        GiSettleFrames,
                        2 * Math.Max(RenderCVars.GiAmortize.Value, 1) * (1 + Math.Max(RenderCVars.GiTemporalFrames.Value, 1)));
                    _giStaticFrames = giChanged ? 0 : Math.Min(_giStaticFrames + 1, giSettle + 1);
                    bool retraceGi = _giStaticFrames <= giSettle;

                    if (_cardLighting is { } litCards)
                    {
                        // How many frames the relight and the bounce spread their tiles across, from the cvar
                        // so the per-frame GPU cost can be traded against GI fill-in latency without a rebuild.
                        litCards.BounceFrameSpread = RenderCVars.GiAmortize.Value;

                        // How many frames a tile's estimate accumulates over, the pair to the spread above:
                        // the two together set how much of one retrace is admitted and how often, which is
                        // what governs the slow ripple across interior surfaces. See r_gi_temporal_frames.
                        litCards.TemporalFrames = RenderCVars.GiTemporalFrames.Value;

                        // Where the steady-state refresh is centred and how far it reaches. The cache spans
                        // the whole level and the cursor used to walk all of it every round, so a fixed
                        // share of every frame went on cards in parts of a map the player is nowhere near --
                        // and past the distance field's own reach, where the relight cannot even arrive at a
                        // different answer. Relative to the cache's origin, as everything in its space is,
                        // and differenced in double precision before the cast: the level sits kilometres out.
                        litCards.ActiveCentre = new Vector3f(
                            (float)(eye.Position.X - litCards.Origin.X),
                            (float)(eye.Position.Y - litCards.Origin.Y),
                            (float)(eye.Position.Z - litCards.Origin.Z));
                        litCards.ActiveRadius = RenderCVars.GiCacheRadius.Value;

                        // The direct relight: the whole cache at once for a deterministic dump, otherwise a
                        // slice a frame. The sun is fixed, so the only thing the direct term has to track is
                        // the clipmap scrolling new coverage under a card as the camera moves -- and doing
                        // that over the whole cache on the scroll frame was a seventy-millisecond stall that
                        // read as the game lurching every few steps. Sliced, the new shadows arrive over a
                        // handful of frames with no spike, and the bounce keeps its long-running history
                        // instead of being re-seeded from scratch on every scroll.
                        // Skipped once the cache has settled and nothing is scrolling or scrubbing: the sun
                        // is fixed and no new coverage is arriving, so the slice would re-trace the direct
                        // term it already holds. That idle is the whole fix for the at-rest spike.
                        if (retraceGi)
                        {
                            litCards.Light(
                                light is { IsBelowHorizon: false } shadowSun ? shadowSun.Direction : default,

                                // The SAME effective sun the deferred lighting shades with -- colour times
                                // both scales -- or the bounce is lit by a different key than the screen and
                                // the two disagree wherever indirect light dominates.
                                light.Colour * (RenderCVars.SunScale.Value * RenderCVars.LightScale.Value),
                                SkyFillColour(),
                                liveField,
                                RenderCVars.SurfaceCacheProbe.Value,
                                amortize: !wantDump);
                        }
                    }

                    // The bounce accumulates EVERY frame -- the reference's amortised radiosity. Each frame
                    // reads last frame's final and folds ONE more bounce in, so a still scene keeps filling
                    // with light toward its multi-bounce equilibrium (the "lighting up" over the first
                    // second) instead of holding at the two bounces the load pass seeded. This was gated on
                    // a clipmap rebuild, so a stationary interior never got past its first couple of bounces
                    // -- which is most of why a sunless room read dark: bounces three, four and five are
                    // what fill a room the sun never reaches. The feedback is clamped below one in the
                    // shader, so it converges rather than running away, and the 1/(1+frames) blend gives the
                    // ramp for free. Only when no diagnostic probe owns the direct atlas.
                    // Same stand-down as the relight: once settled with nothing scrolling or scrubbing, the
                    // scene has reached its multi-bounce equilibrium and folding another bounce in re-derives
                    // the same final -- so the bounce, the frame's single most expensive pass, idles too. The
                    // frame counter holds where it was, so the accumulation resumes rather than restarts when
                    // motion or a scrub brings the retrace back.
                    if (_cardLighting is { } bounceCards && RenderCVars.SurfaceCacheProbe.Value == 0 && retraceGi)
                    {
                        bounceCards.Bounce(
                            liveField,
                            boost: RenderCVars.GlobalIlluminationBoost.Value,
                            temporalIndex: wantDump ? 0 : _cardBounceFrame);
                        _cardBounceFrame++;

                        // Dumped after the SHADOWED bounce rather than the load-time one, which runs before
                        // there is any field to trace against and would show the unshadowed answer.
                        if (wantDump && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } shadowInto)
                        {
                            cardLightingDumped = true;
                            SurfaceCacheDebugDumps.CardLighting(
                                _device, bounceCards, shadowInto, _logger, RenderCVars.SurfaceCacheProbe.Value);
                        }
                    }

                    // The world-space radiance cache: a grid of probes tracing the just-lit surface cache
                    // into the same L1-SH field the deferred pass samples, but held in world cells rather
                    // than on the moving screen -- so it stays stable under motion where the screen gather
                    // shimmers. Configured to the baked field's own layout so the two are interchangeable at
                    // the sampler, updated a slab a frame off the freshly bounced cache, and bound in place
                    // of the baked field for the deferred fill. Needs the baked field only for its grid.
                    if (RenderCVars.RadianceCache.Value != 0
                        && _cardLighting is { } cacheCards
                        && _probeField is { } cacheGrid)
                    {
                        cacheCards.ConfigureRadianceCache(
                            new Vector3d(cacheGrid.Origin.X, cacheGrid.Origin.Y, cacheGrid.Origin.Z),
                            cacheGrid.Spacing,
                            cacheGrid.CountX,
                            cacheGrid.CountY,
                            cacheGrid.CountZ);

                        cacheCards.UpdateRadianceCache(
                            liveField,
                            SkyFillColour(),
                            // A short running average while the level is warming up, so the cache climbs to
                            // its converged value in a handful of frames rather than easing there over a
                            // couple of seconds -- which is what read as the scene loading bright and dimming.
                            // A long one after, where a slow average is what keeps the steady field from
                            // shimmering under the per-sweep ray jitter.
                            maxFrames: _giExposureWarmup > 0 ? 6 : 32,
                            scale: RenderCVars.RadianceCacheScale.Value,
                            skyReject: RenderCVars.GlobalIlluminationLeakReject.Value);

                        _device.SetRealtimeProbeVolume(
                            cacheCards.RadianceCacheRed,
                            cacheCards.RadianceCacheGreen,
                            cacheCards.RadianceCacheBlue,
                            cacheCards.RadianceCacheDistance,
                            cacheGrid.Origin,
                            cacheGrid.Spacing,
                            cacheGrid.CountX,
                            cacheGrid.CountY,
                            cacheGrid.CountZ);
                    }
                    else
                    {
                        _device.ClearRealtimeProbeVolume();
                    }

                    _device.EndStageTiming();
                }

                _device.SetDebugView(RenderCVars.Debug.Value);

                // The hashed alpha test needs the temporal resolve to average its dither, so it follows the
                // temporal path: a per-frame seed above zero turns it on, zero leaves the hard cut-out. The
                // seed only has to change each frame for the dither to differ; the jitter counter does that.
                _device.SetAlphaHash(
                    RenderCVars.HashedAlpha.Value && wantsTemporal
                        ? 1f + (float)(temporalFrame & 63)
                        : 0f);

                // The derived detail is a deferred-path feature: it changes the normal and the roughness
                // written into the G-buffer, and without one there is nothing to write them into.
                //
                // r_pbr (F9) multiplies the authored strength by one or zero rather than replacing it, so
                // the maps can be taken out of the frame and put back without disturbing what the preset
                // chose. Read here, every frame, because that is what makes it instant: nothing is
                // unloaded and nothing is rebuilt -- the shader simply stops scaling its slope by it.
                _device.SetMaterialDetail(
                    RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value
                    && RenderCVars.MaterialDetailEnabled.Value
                        ? RenderCVars.MaterialDetail.Value
                        : 0f);
                _device.SetFloorGloss(
                    RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                        ? RenderCVars.FloorGloss.Value
                        : 1f);
                _device.SetReflections(
                    RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                        ? new ReflectionSettings(
                            RenderCVars.Reflections.Value,
                            RenderCVars.ReflectionIntensity.Value,
                            RenderCVars.ReflectionMaxRoughness.Value)
                        : ReflectionSettings.Off);

                // How wet the ground is. Driven by the storm rather than by a number of its own: it is
                // raining or it is not, and a road that stays dry through a downpour is the single thing
                // that most gives away rain as an effect rather than as weather. Off the moment the storm
                // is not drawing, so a level with no rain is not quietly shaded damp.
                bool wetGround = RenderCVars.Wetness.Value
                    && RenderCVars.OffscreenScene.Value
                    && RenderCVars.Deferred.Value
                    && _storm is { IsActive: true };
                _device.SetWetness(
                    wetGround
                        ? WetnessSettings.Storm.AtIntensity(
                            _storm!.Settings.Intensity,
                            _loop.TimeSource.Milliseconds / 1000f)
                        : WetnessSettings.Dry);

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
                    eye.Position,
                    eye.Orientation.Transposed());

                _device.SetVolumetricFog(
                    medium,
                    new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
                    wantsFog ? fogPresets.VolumesFor(fogMission) : ReadOnlySpan<LocalFogVolume>.Empty);

                // Filled BEFORE the scene capture opens: a cascade renders into its own framebuffer, and
                // beginning a capture first would leave the shell holding two render targets at once.
                //
                // And not filled at all on a frame where nothing can read it: with the traced sun the
                // ray mask is the ONLY sun shadow the lighting samples, and with the sun faded to
                // nothing the sun term is nought before the shadow is asked -- either way the cascades
                // are written and never read, raster spent on a map nobody looks at. The maps and their
                // matrices stay allocated and uploaded, so the first frame that needs them again (the
                // r_rt_shadows toggle, or a sunrise scrub) refits and refills BEFORE that frame's
                // lighting reads -- no stall and no frame of wrong shadows.
                bool cascadesRead = shadowSettings.SunLit && !_device.SunShadowTraced;
                BeginStage();
                if (cascadeCount > 0 && _shadowPass is not null && cascadesRead)
                {
                    // The cascades are filled through the title's presentation, not through this
                    // host's renderer fields: the pass draws whichever game is loaded.
                    // Named concretely because this host still knows its title; the seam is
                    // ShadowCasterPass.Render, which takes only the presenter interfaces and so
                    // draws whichever game supplied them. The title becomes injected in phase 1.
                    Igi1Presentation casters = new(_world, shadowCasters);
                    _shadowPass.Render(
                        cascadeStorage, cascadeCount, eye, _scene, casters.World, casters.Characters);
                }
                EndStageIfSlow("the shadow cascades");

                bool captured = RenderCVars.OffscreenScene.Value && _device.BeginSceneCapture(sceneTarget);
                if (captured)
                {
                    // The scheduler cleared the back buffer; the target it did not know about needs it too.
                    _device.Clear(_scheduler.ClearColour);

                    // The live sun, calibrated against the independently-normalised sky by r_sun_scale.
                    // The baked model ignores it (it never adds a live sun), so the scale is a no-op there.
                    Vector3f scaledSun = light.Colour * RenderCVars.SunScale.Value;

                    // The atmospheric sky keeps its own source illuminance. Sharing scaledSun here made
                    // r_sun_scale dim the sky SH together with the direct term, so their ratio could never
                    // be corrected. Ultra's atmosphere remains at its already-measured source intensity.
                    //
                    // Under the moon the feed is knocked down hard rather than handed over at face
                    // value: the atmosphere scatters whatever illuminance it is given, and the moon's
                    // few thousandths of a sun still rendered as a dim but unmistakably blue daytime
                    // sky where a night sky reads near black. The surfaces keep the full moonlight; the
                    // sky keeps only a trace of it.
                    // The same knock-down, for the other way a scene can be at night. The moon case
                    // above exists because the atmosphere scatters whatever illuminance it is given and a
                    // trace of moonlight still came out as a dim blue DAYTIME sky. I.G.I. 2 reaches the
                    // same place by a different road: its night missions leave the sun up and paint it
                    // nearly black, so the model -- which only knows night as a sun below the horizon --
                    // has no way to tell. Told by the level instead, and taken to the same floor.
                    //
                    // Not for a level bound to the cycle by its night end. Its night IS the moon above, at
                    // the hour it is bound to, so the level's constant adds nothing there -- and away from
                    // that hour it is wrong: scrubbed up to noon, a constant night held the sky at the
                    // floor under a risen sun.
                    //
                    // How far down is r_night_sky's to say. Two percent kept the moonlit sky from reading
                    // as a dim day, and left it and the fill projected from it black; with the resolve's
                    // night shift doing the job of making it read as night, a third lets the sky light the
                    // moon's shadows again. See RenderCVars.NightSkyScale.
                    float nightSky = RenderCVars.NightSkyScale.Value;
                    float nightFeed = _deferredLighting is { Night: null } nightly
                        ? 1f + ((nightSky - 1f) * Math.Clamp(nightly.Nightfall, 0f, 1f))
                        : 1f;
                    //
                    // Under the day cycle the sky is handed the sun and the moon as two lights, not
                    // whichever of them holds the direct slot. The sun comes untinted and from where it
                    // really is, under the horizon too: the march reddens it, and the planet's shadow
                    // puts it out a little at a time, which is the twilight. The moon comes as its own
                    // light, at the knocked-down feed above, from the moment it rises. Handing the sky
                    // only the direct light made it scatter a sun already reddened and dimmed -- a
                    // second reddening, the orange-brown dusk -- and then, as the direct light passed to
                    // the moon, a sun that had gone and a moon not yet up: a black sky and a black fill
                    // for the half hour between.
                    float atmosphereScale = RenderCVars.AtmosphereSunScale.Value;
                    Vector3f atmosphereSun;
                    Vector3f? atmosphereSunDirection = null;
                    Vector3f atmosphereMoon = default;
                    if (_options.SunDirection is null)
                    {
                        atmosphereSun = _dayNight.SkySunColour * (atmosphereScale * nightFeed);
                        atmosphereSunDirection = _dayNight.SunDirection;
                        // The moon above the weather, whole: the sky's cloud deck is lit by all of it and
                        // passes on its own share (see SkyView's deck transmission), while the clear air
                        // scatters only r_night_sky's. Knocked down or veiled here as well, the deck took
                        // the cloud's toll twice and a rainy night came out black.
                        atmosphereMoon = _dayNight.MoonColour * (RenderCVars.Moonlight.Value * atmosphereScale);
                    }
                    else
                    {
                        atmosphereSun = light.Colour * (atmosphereScale * nightFeed);
                    }

                    // TEMPORARY (r_ambient_scale): the dynamic model fills every surface with this one flat
                    // ambient, so a sealed room reads as lit as open ground. Scaling it here -- towards 0 --
                    // is how that stand-in is dialled down or removed to confirm what lights enclosed spaces.
                    Vector3f scaledAmbient = light.Ambient * RenderCVars.AmbientScale.Value;

                    _dumpWorldSun = light.Direction;
                    _device.SetLighting(_dumpLighting = new SceneLighting(
                        EffectiveLighting,
                        viewSunDirection,
                        scaledSun,
                        scaledAmbient,
                        RenderCVars.LightScale.Value,

                        // Whether the level's bakes already carry its direct light, so the hybrid model
                        // must not add a sun over them. See DeferredLighting.CompleteBake.
                        _deferredLighting?.CompleteBake ?? false,

                        // The fog scatters the level's OWN ambient, not the r_sky_light-boosted surface
                        // fill: the fog was calibrated against this value and boosting it whites the frame
                        // out. Kept independent of r_ambient_scale so the haze does not collapse when the
                        // flat ambient is dialled to zero to watch the GI carry the surfaces.
                        light.Ambient,

                        // How brightly the sky lights the world, for a backend that models the sky as a
                        // real light source rather than folding it into the flat ambient. Same dial that
                        // scaled the flat stand-in, now driving the thing it stood in for.
                        RenderCVars.SkyLight.Value,

                        // The source that builds both the visible atmosphere and its diffuse SH. Separate
                        // from scaledSun so direct-light calibration does not move the sky with it.
                        atmosphereSun,

                        // What the night sky holds beyond the air: the glow that lights a moonless night,
                        // the cloud cover, the stars and the moon's disc. Nothing by day.
                        FrameNightSky(),
                        atmosphereSunDirection,
                        atmosphereMoon,
                        nightSky));
                    _device.BeginPass(ScenePass.Geometry);

                    // The sky is geometry as far as the G-buffer is concerned, so without this the dynamic
                    // model would multiply the dome by an ambient term and the horizon would go dark. It
                    // is not lit; it IS light.
                    _device.SetMaterialOverride(SurfaceMaterial.Sky);
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
                // The level's own distance fog, which the whole world below is drawn into, and its flat
                // model light. Set before the sky, because the sky renderer puts back whatever it finds
                // here after drawing its unfogged dome. The modern tiers leave the fog off and march the
                // same authored haze through the froxel volume instead, and their models are lit by the
                // G-buffer pass rather than by this one.
                bool retailFog = RenderCVars.RetailFog.Value;
                _device.SetFog(retailFog ? RetailAir(_globalLight.ObjectFog) : SceneFog.None);
                _device.SetModelAmbient(
                    EffectiveLighting == LightingModel.Baked
                        ? _globalLight.ObjectAmbient
                        : GlobalLightSettings.DefaultAmbient);

                if (mapScene is null || _computerFlight.IsFlying)
                {
                    // Warm the sky dome towards the horizon as the sun sinks, so a window shows a golden dusk
                    // sky rather than a flat blue one and the screen-space bounce off it carries that warmth
                    // into the room instead of a cool tint. Untouched with the sun high.
                    _sky?.SetSunTint(SkyWarmthTint((_effectiveSun ?? _sun)?.Direction.Z ?? 1f));

                    // Kept rather than replaced by the atmosphere, and divided by the exposure on the way
                    // in because what it carries is a picture. Only where there IS a resolve to survive:
                    // vanilla draws straight to the back buffer.
                    //
                    // A BRACKET, closed below. The division is the sky's own, and everything drawn after
                    // this is the world: leaving it open divided every emissive surface in the level --
                    // every lamp -- by the exposure too.
                    bool paintedSky = captured && _sky is { IsGameOwnSky: true };
                    if (paintedSky)
                    {
                        _device.SetPaintedSky(true);
                    }

                    _sky?.Draw(new SkyFrame(eye, projection, _loop.TimeSource.Milliseconds / 1000f));

                    if (paintedSky)
                    {
                        _device.SetPaintedSky(false);
                    }
                }

                if (captured)
                {
                    _device.SetMaterialOverride(SurfaceMaterial.Default);
                }

                // The world draws BEFORE the terrain, deliberately: buildings are the pass's best
                // occluders. Indoors the frustum still holds the whole outdoors, and with the ground
                // drawn first every one of those terrain fragments was shaded and then buried under
                // the walls; walls first, and early-Z rejects that fill instead. Outdoors the same
                // holds for the ground behind every building.
                //
                // This is the one place the frame knowingly differs from I.G.I. 2's, which puts terrain
                // in queue 2 and rigid objects in 5 and so draws the ground first. The picture is the
                // same either way -- both passes are opaque and depth-tested, so the order decides only
                // which fragments are shaded and then thrown away -- and the port's way throws away
                // fewer. Where the order does decide the picture, the render group is honoured: the sky
                // is drawn first, the water last, and the world's own objects come out grouped, which is
                // what BuildDrawOrder is for.
                bool cameraIndoors = IsCameraInsideBuilding(eye.Position);
                _world.Draw(_scene, eye, projection, new WorldDrawOptions(OcclusionCulling: RenderCVars.WorldOcclusion.Value));
                EndStageIfSlow("the sky and the world's objects");

                if (captured)
                {
                    // The world leaves its last sub-mesh's surface material in force; the terrain
                    // must not wear it.
                    _device.SetMaterialOverride(SurfaceMaterial.Default);
                }

// The ground is drawn through its own band of the same keyframe. Retail keeps two --
                // "terrain" and "object category 1" -- and publishes them to different globals; every
                // shipping level happens to author them alike, which is not a reason to draw them alike.
                if (retailFog)
                {
                    _device.SetFog(RetailAir(_globalLight.TerrainFog));
                }

                _terrain?.Draw(new TerrainFrame(
                    eye,
                    projection,
                    verticalFieldOfView,
                    aspectRatio,

                    // -viewdist still wins when it is given, so an existing diagnostic command line keeps
                    // meaning what it did; otherwise the setting decides.
                    _options.TerrainViewDistance > 0 ? _options.TerrainViewDistance : RenderCVars.TerrainDetail.Value,
                    GraphicsPresetRegistry.Default.Current != GraphicsPreset.Vanilla,
                    RenderCVars.TerrainOcclusion.Value));
                EndStageIfSlow("the terrain");

if (retailFog)
                {
                    _device.SetFog(RetailAir(_globalLight.ObjectFog));
                }

                // The foliage after the ground it is planted in, so a tree's trunk depth-tests against
                // the hillside rather than against whatever was drawn there before it. Half-tangents
                // rather than the angle, because the cull's planes are built straight out of them.
                _forest?.Draw(new ForestFrame(
                    eye,
                    MathF.Tan(verticalFieldOfView * 0.5f) * aspectRatio,
                    MathF.Tan(verticalFieldOfView * 0.5f)));
                EndStageIfSlow("the forest");

                // And the water last of the opaque world, because it is translucent and has to sort
                // against everything already in the depth buffer -- the ground under it above all.
                _water?.Draw(new WaterFrame(eye, projection, _loop.TimeSource.Milliseconds / 1000.0));
                EndStageIfSlow("the water");

                // With the world and the terrain both in the depth buffer, the world's occlusion
                // queries can be answered honestly: walls hide the outdoors, and hills hide buildings.
                _world.IssueOcclusionQueries(eye);
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
                if (_missileRenderer is not null && _player is not null)
                {
                    _missileRenderer.Draw(_player.Missiles, eye, projection, smoothPresentation ? tickBlend : 1f);
                    _missileRenderer.Draw(_vehicleMissiles, eye, projection, smoothPresentation ? tickBlend : 1f);
                    _missileRenderer.Draw(_player.M203, eye, projection, smoothPresentation ? tickBlend : 1f);
                }

                // The remastered-optics decisions and the binocular detector are resolved here, before
                // the soldiers are drawn, for two reasons: the detector is stateful and must be scanned
                // exactly once per frame, and the through-wall glow pass below has to mark the very
                // contacts it finds while the scene is still being captured. The HUD after the resolve
                // reuses this same target list rather than advancing the detector a second time.
                bool remasterHud = RemasteredHudPresentation.UsesRemasteredHud(RenderCVars.RemasteredHud.Value);
                bool remasteredOptics = RemasteredHudPresentation.UsesRemasteredOptics(
                    TacticalComputer is not null,
                    _remasteredHud is { IsReady: true });
                bool tacticalMessages = RemasteredHudPresentation.UsesTacticalMessages(
                    remasterHud,
                    RenderCVars.TacticalMessages.Value);
                bool showPlayerHud = _playerMode && !_computerOpen && !_entry.IsCameraActive
                    && _cutscenes?.IsPlaying != true
                    && !(_playerDead && _deathCamera.IsActive)
                    && _player is { View: not null, IsMountedStationaryGun: false };

                // On a stand retail draws no HUD at all -- captured in mission 4, the stance figure shown
                // beside the gun is gone the moment its sight takes the view -- hence the last condition.
                bool showFirstPersonHud = showPlayerHud
                    && _player is { IsOnLadder: false, IsInWorldInteraction: false };

                // The arms and the held weapon hang off the eye, so the moment the camera leaves it they
                // are either filling the frame or behind it. When they stand down, and when they come
                // back, is the camera's own call -- see ThirdPersonCamera.ShowsViewModel.
                bool showViewModel = showFirstPersonHud && _thirdPerson.ShowsViewModel;

                // What the interaction resolver is offering, advanced on its own wall clock so the
                // marker fades at the same rate whatever the frame rate. Gated on showPlayerHud rather
                // than showFirstPersonHud on purpose: a timed interaction hides the hands and the
                // weapon, and that is exactly when the ring filling on the object is what the player
                // is reading.
                bool worldInteractionPrompt = InteractionPromptPresentation.UsesWorldPrompt(
                    remasterHud,
                    RenderCVars.InteractionPrompt.Value);
                int interactionPromptNow = _loop.TimeSource.Milliseconds;
                float interactionPromptElapsed =
                    Math.Clamp((interactionPromptNow - _lastInteractionPromptMilliseconds) / 1000f, 0f, 0.1f);
                _lastInteractionPromptMilliseconds = interactionPromptNow;
                _interactionPrompt.Update(
                    worldInteractionPrompt && showPlayerHud
                        ? _player?.InteractionPrompt(smoothPresentation ? tickBlend : 1f)
                        : null,
                    interactionPromptElapsed);

                // The remastered detector's range grows with the zoom: from the base range at the
                // default field of view up to the zoomed range at the tightest magnification, so leaning
                // into the optics resolves a distant figure the wide view does not. The vanilla optics
                // stay retail-uncapped (positive infinity).
                float binocularZoomIn = _player?.BinocularZoomInFraction(sight.TanX) ?? 0f;
                float binocularDetectionRange = remasteredOptics
                    ? RenderCVars.BinocularRange.Value
                        + ((RenderCVars.BinocularRangeZoomed.Value - RenderCVars.BinocularRange.Value) * binocularZoomIn)
                    : float.PositiveInfinity;
                IReadOnlyList<EnemyOverlayBox> binocularTargets =
                    showFirstPersonHud
                    && _player is { IsBinocularsOpen: true }
                    && _soldiers is { } binocularSoldiers
                        ? binocularSoldiers.BinocularTargets(
                            // Use the same presented eye that rendered the world. The detector owns
                            // authoritative slot lifetime, but its screen rectangle must be projected
                            // through the frame's smoothed camera or moving/zooming binoculars make the
                            // box visibly lag behind the soldier it is meant to surround.
                            eye,
                            sight.TanX,
                            _window.Width,
                            _window.Height,
                            // FUN_00471F60/FUN_00471B60 use Sys_GetTickCount (the retail
                            // millisecond counter), not the 30 Hz simulation index. Passing
                            // TickCount here stretched the native 250 ms entry pulse and
                            // 3000 ms retention window by a factor of thirty-three.
                            _loop.Clock.Now,
                            binocularDetectionRange,
                            _loop.Clock.TickCount)
                        : [];

                // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
                // never swaps in the view-model block for them (player.md section 15.3).
                _soldiers?.ObserveView(eye, projection);
                _soldiers?.Draw(eye, projection);

                // The player's own body, once it has fallen -- drawn only under the death camera, so it never
                // appears in front of the first-person view during normal play.
                // ...and, now, whenever the third-person camera has cleared the character's head, which
                // was settled with the camera above so the shadow cascades and this pass agree.
                _soldiers?.DrawPlayerBody(eye, projection);

                // What the character is holding. The first-person rig is not drawn at all from out here,
                // so its weapon has to be hung off the body's own hand -- bone 22, where a guard's rifle
                // rides -- under the WORLD projection rather than the view model's magnified one. Body
                // only, as a soldier's is: the muzzle flame is light, and the deferred pass would shade it
                // like scenery.
                if (thirdPersonBody
                    && _player is { } armedBody
                    && _soldiers is { } handBody
                    && _heldWeapon is { IsReady: true } thirdPersonGun
                    && armedBody.Weapons?.HideFirstPersonWeapon != true)
                {
                    // The cycle changes the model as well as the animation, and out here nothing else is
                    // watching for that: the first-person block that normally does is not running.
                    if (_loadedMissionForWeapon is { } missionForBody)
                    {
                        FollowHeldWeapon(armedBody, _heldWeapon, missionForBody);
                    }

                    if (handBody.PlayerBodyHandAttachment() is { } handAttachment)
                    {
                        thirdPersonGun.Draw(
                            handAttachment,
                            eye,
                            projection,
                            firedThisTick: false,
                            armedBody.ShotSequence,
                            WeaponDrawParts.BodyOnly);
                    }
                }

                EndStageIfSlow("the soldiers");

                // The marks rounds have left, over the world and under the first-person rig.
                if (_marks is not null && _player is not null && RenderCVars.BulletMarks.Value)
                {
                    _marks.Draw(_player.Marks, eye, projection, _world.DrawPoses);
                }

                // L6/L9: the live screen probe gather. Traced here, while the G-buffer the geometry pass just
                // filled is still live and BEFORE the pass closes -- because closing it runs the deferred
                // lighting, which reads the atlas as its diffuse fill. So the gather has to have written it
                // first. Only without a card probe, which fills the cache atlas with a diagnostic rather than
                // light. It replaces the ambient inside the shade rather than compositing over the frame, so
                // there is no ambient to zero and nothing runs after the lighting.
                const int giStep = 8;
                // The probes' running average, capped at the reference's screen-probe temporal limit of
                // ten frames. It was thirty-two, and a history that long outlives every
                // turn of the view -- each frame of motion resamples it through the reprojection and clamps
                // it into the window of one frame's rays, and at one part in thirty-three the bias that
                // leaves takes the better part of a second to wash back out, which is the cloud that
                // follows the camera and settles after it stops.
                const int giMaxAccum = 10;

                // The screen trace tier: how far its rays reach across the frame and their step cap, plus the
                // surface thickness its hit test assumes. Metres, save the step count.
                const int giTraceSteps = 32;
                const float giTraceDistance = 24f;
                const float giTraceThickness = 0.5f;
                // The surface cache, handed to the backend for the reflection pass's traced half: a ray
                // that leaves the screen has no answer in the frame, and the cards are the only record of
                // what stands off it. Independent of whether the screen gather runs below -- the two read
                // the same cards for different questions, and a player who has turned the indirect light
                // off has not turned reflections off. Cleared to nothing while there is no lit cache, so
                // the backend traces nothing rather than against stale handles.
                _device.SetSurfaceCache(
                    _cardLighting is { } reflectionCards && RenderCVars.SurfaceCacheProbe.Value == 0
                        ? reflectionCards.Describe(
                            new Vector3f(
                                (float)(eye.Position.X - reflectionCards.Origin.X),
                                (float)(eye.Position.Y - reflectionCards.Origin.Y),
                                (float)(eye.Position.Z - reflectionCards.Origin.Z)),
                            RenderCVars.GiBounce.Value)
                        : SurfaceCacheBinding.None);

                // And the distance field, for the same passes and by the same rule. Where the adapter can
                // trace, the acceleration structure finds a reflection's hit; where it cannot, this is the
                // only description of the world a shader can walk, and without it glass reflected the sky
                // and nothing else. The boxes are CAMERA-RELATIVE, which is why this is handed over every
                // frame rather than when the level loads: they move with the camera the clipmaps centre on.
                _device.SetDistanceField(DescribeDistanceField(eye.Position));

                float giStrength = RenderCVars.ScreenGather.Value;
                bool giActive = captured
                    && giStrength > 0f
                    && _sdfClipmaps is not null
                    && _cardLighting is not null
                    && RenderCVars.SurfaceCacheProbe.Value == 0;

                if (giActive && _sdfClipmaps is { } liveGiField && _cardLighting is { } liveGiCards)
                {
                    // Timed, at last. This gather is the most expensive thing in a real-time-GI frame and
                    // NOTHING measured it: the bracket the surface-cache relight runs under closes long
                    // before this, and the backend's own geometry range -- ranges cannot nest -- took over
                    // the one the game opens around the world draws. So the overlay and the benchmark both
                    // reported a frame four milliseconds shorter than the one being drawn, and the
                    // difference read as a CPU cost that was never there. GlobalIllumination is the stage
                    // its own documentation already says this belongs to; same-stage ranges in a frame sum,
                    // so it joins the relight rather than displacing it.
                    _device.EndStageTiming();
                    _device.BeginStageTiming(RenderStage.GlobalIllumination);

                    // The gather reads the G-buffer, which under a render scale is smaller than the
                    // window — probes are laid over the pixels that exist, not the ones the window has.
                    int giSourceWidth = sceneTarget.Width;
                    int giSourceHeight = sceneTarget.Height;
                    int wantWidth = (giSourceWidth + giStep - 1) / giStep;
                    int wantHeight = (giSourceHeight + giStep - 1) / giStep;

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

                        if (giRaw.IsValid)
                        {
                            _device.DestroyTexture(giRaw);
                        }

                        giRaw = _device.CreateStorageTexture(
                            wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.raw");

                        giIndirectWidth = wantWidth;
                        giIndirectHeight = wantHeight;
                        giHasHistory = false;
                    }

                    // Both histories describe the scene as the last frames saw it. A camera cut (a cutscene, a
                    // respawn, the death camera, the map taking the eye), a change of projection (a scope's
                    // zoom -- the reprojection assumes last frame's projection is this frame's) or a surface
                    // cache rebuilt from dark all make that description wrong, and the plane tests only catch
                    // part of it: a cut still read one frame of the old view's light.
                    DepthUnprojection giUnprojection = DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane);
                    bool giContinuous = giContinuityValid
                        && ReferenceEquals(giLastCards, liveGiCards)
                        && MathF.Abs(giUnprojection.XScale - giLastUnprojection.XScale) < 1e-4f * giLastUnprojection.XScale
                        && MathF.Abs(giUnprojection.YScale - giLastUnprojection.YScale) < 1e-4f * giLastUnprojection.YScale
                        && (eye.Position - giLastEye).LengthSquared < GiCutDistance * GiCutDistance;
                    if (!giContinuous)
                    {
                        giHasHistory = false;
                        giResolveHasHistory = false;
                    }

                    giContinuityValid = true;
                    giLastEye = eye.Position;
                    giLastUnprojection = giUnprojection;
                    giLastCards = liveGiCards;

                    Vector3f giCameraToCache = new(
                        (float)(eye.Position.X - liveGiCards.Origin.X),
                        (float)(eye.Position.Y - liveGiCards.Origin.Y),
                        (float)(eye.Position.Z - liveGiCards.Origin.Z));

                    // The gather's cadence: it re-traces every Nth frame and the atlas stands in between.
                    // See r_screen_gi_cadence for why that is sound and why it beats cutting the ray count.
                    int giFilterRadius = RenderCVars.ScreenGatherFilter.Value;
                    int giCadence = Math.Max(RenderCVars.ScreenGatherCadence.Value, 1);
                    bool giTraceThisFrame = !giHasHistory || (giCadenceCursor % giCadence) == 0;
                    giCadenceCursor++;

                    int write = giFrame & 1;
                    int read = 1 - write;

                    if (giTraceThisFrame)
                    {
                        liveGiCards.GatherScreenTemporal(
                            liveGiField,
                            _device.SceneDepth,
                            _device.SceneNormal,
                            DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
                            eye.Orientation.Transposed(),
                            giCameraToCache,
                            giSourceWidth,
                            giSourceHeight,
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
                            SkyFillColour(),

                            // The camera the lit target was rendered through: the LAST frame's, which the
                            // resolve carries forward every frame -- unlike the gather's own previous camera,
                            // which only moves on a frame that traced.
                            giResolvePrevWorldToView,
                            giResolvePrevCameraToCache,
                            giRaw);

                        // The spatial denoise (L6c), if on: smooths the accumulation into a separate atlas that
                        // the deferred fill reads, leaving the history the next frame reprojects onto unfiltered.
                        if (giFilterRadius > 0)
                        {
                            liveGiCards.FilterScreen(
                                giAccum[write],
                                giPosition[write],
                                _device.SceneNormal,
                                giFiltered,
                                giIndirectWidth,
                                giIndirectHeight,
                                giFilterRadius);
                        }

                        // What this frame becomes to the NEXT GATHER: only a frame that actually traced moves
                        // the gather's history and its camera on.
                        giLastWrite = write;
                        giPrevWorldToView = eye.Orientation;
                        giPrevCameraToCache = giCameraToCache;
                        giHasHistory = true;
                        giFrame++;
                    }

                    TextureHandle giRead = giFilterRadius > 0 ? giFiltered : giAccum[giLastWrite];

                    // The full-resolution temporal resolve (the reference's ScreenProbeTemporalReprojectionCS):
                    // upsample the sparse atlas once and accumulate it per pixel, reprojecting against each
                    // surface's own world position, so the indirect is pinned to the world instead of sliding
                    // with the coarse sparse-probe reprojection. The deferred fill reads the full-res result.
                    int resolveWrite = giResolveFrame & 1;
                    int resolveRead = 1 - resolveWrite;

                    int resolveW = giSourceWidth;
                    int resolveH = giSourceHeight;
                    if (giResolveWidth != resolveW || giResolveHeight != resolveH)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            if (giResolve[i].IsValid)
                            {
                                _device.DestroyTexture(giResolve[i]);
                            }

                            if (giResolvePosition[i].IsValid)
                            {
                                _device.DestroyTexture(giResolvePosition[i]);
                            }

                            giResolve[i] = _device.CreateStorageTexture(
                                resolveW, resolveH, VolumeFormat.Rgba16F, "gi.resolve");
                            giResolvePosition[i] = _device.CreateStorageTexture(
                                resolveW, resolveH, VolumeFormat.Rgba16F, "gi.resolve.pos");
                        }

                        giResolveWidth = resolveW;
                        giResolveHeight = resolveH;
                        giResolveHasHistory = false;
                    }

                    liveGiCards.ResolveScreenTemporal(
                        giRead,
                        _device.SceneDepth,
                        _device.SceneNormal,
                        DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
                        eye.Orientation.Transposed(),
                        giCameraToCache,
                        giResolvePrevWorldToView,
                        giResolvePrevCameraToCache,
                        giResolve[resolveWrite],
                        giResolvePosition[resolveWrite],
                        giResolve[resolveRead],
                        giResolvePosition[resolveRead],
                        resolveW,
                        resolveH,
                        giResolveHasHistory,
                        16,

                        // The probes' own traced positions and the frame they were traced in -- the last
                        // frame that traced, which is not this one whenever the gather's cadence skipped.
                        giPosition[giLastWrite],
                        giPrevWorldToView,
                        giPrevCameraToCache,
                        giStep);
                    giResolveHasHistory = true;

                    // Recorded for the deferred lighting to fold in when the pass closes. Full-resolution, so
                    // it is read straight rather than upsampled from the sparse atlas a second time.
                    _device.SetScreenIndirect(giResolve[resolveWrite], giStrength, fullResolution: true);

                    // The resolve's own carry-forward, every frame: this camera is what the next frame's
                    // per-pixel reprojection measures its motion against. The gather's equivalent is set
                    // above, inside the branch, because only a frame that traced may move it.
                    giResolvePrevWorldToView = eye.Orientation;
                    giResolvePrevCameraToCache = giCameraToCache;
                    giResolveFrame++;

                    _device.EndStageTiming();
                }
                else
                {
                    // The gather did not run this frame, so next frame has nothing coherent to reproject onto.
                    giHasHistory = false;
                    giResolveHasHistory = false;
                }

                // The level's lamp fixtures, submitted as real point lights for the deferred pass that
                // closes just below -- the same timing as the screen-space fill. Transformed into this
                // frame's view space here, in double precision: the level sits at large world coordinates,
                // so the camera-relative subtract is done before the cast to float. Only under a shading
                // model and only when the scale asks for them; a windowless interior the gather cannot
                // reach is exactly what these light. The shader also guards on the model and the count.
                float localLightScale = RenderCVars.LocalLightScale.Value;
                float localLightRange = RenderCVars.LocalLightRange.Value;
                _stormLampCount = 0;
                if (captured
                    && localLightScale > 0f
                    && EffectiveLighting != LightingModel.Baked
                    && _lightFixtures.Count > 0)
                {
                    Matrix3f worldToView = eye.Orientation.Transposed();

                    // WHICH lamps, before how they light. A level carries well over a hundred fixtures and
                    // the shader's arrays hold thirty-two, so the set has to be chosen every frame rather
                    // than taken off the top of a list -- the first thirty-two are the level's brightest,
                    // which is a fact about the whole level and says nothing about where the player is
                    // standing. Nearest to the camera, measured against each fixture's own reach so that a
                    // long tube is not passed over for a desk lamp a metre closer.
                    int localCount = SelectNearestLamps(eye.Position, localLightRange);

                    for (int i = 0; i < localCount; i++)
                    {
                        LightFixture fixture = _lightFixtures[_localLightOrder[i]];
                        Vector3f cameraRelative = new(
                            (float)(fixture.Position.X - eye.Position.X),
                            (float)(fixture.Position.Y - eye.Position.Y),
                            (float)(fixture.Position.Z - eye.Position.Z));

                        // Clamp the fixture's relative intensity before scaling. Intensity is area x
                        // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                        // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                        // is not sixteen times brighter, only larger. The clamp flattens that to something
                        // like real fixtures; the scale cvar then sets the overall level.
                        //
                        // Then by how much of that the lamp is actually giving out, which is one for
                        // everything nobody has switched. The emission on the fixture's own geometry is
                        // scaled by the same number in the world renderer, so a lamp fading out fades as
                        // one thing rather than as a panel and a pool of light going at their own rates.
                        int lampIndex = _localLightOrder[i];
                        float output = _lampSwitchboard?.LevelOf(lampIndex) ?? 1f;

                        // Clamp the fixture's relative intensity before scaling. Intensity is area x
                        // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                        // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                        // is not sixteen times brighter, only larger. The clamp flattens that to something
                        // like real fixtures; the scale cvar then sets the overall level.
                        float intensity = MathF.Min(fixture.Intensity, 1.5f) * localLightScale * output;

                        // A cooling filament is a colder black body and therefore a redder one, which is
                        // the last thing anybody sees of a light going out. White whenever the lamp is
                        // not on its way out.
                        Vector3f colour = fixture.Colour;

                        if (_lampSwitchboard?.TintOf(lampIndex) is { } tint && tint != Vector3f.One)
                        {
                            colour = new Vector3f(colour.X * tint.X, colour.Y * tint.Y, colour.Z * tint.Z);
                        }

                        // The way the fixture faces, rotated into the same space its position is in. A
                        // rotation and not a transform: this is a direction, so the camera's translation
                        // must not touch it. Zero stays zero, which the shader reads as a point light.
                        Vector3f facing = fixture.IsDirectional
                            ? worldToView.Transform(fixture.Direction)
                            : default;

                        _stormLamps[i] = new StormLamp(
                            cameraRelative,
                            fixture.Radius * localLightRange,
                            colour * intensity,
                            fixture.IsDirectional ? fixture.Direction : default);

                        _localLightScratch[i] = new DeferredLocalLight(
                            worldToView.Transform(cameraRelative),
                            fixture.Radius * localLightRange,
                            colour,
                            intensity,
                            facing,

                            // The fixture's own size, which is what gives its shadow a penumbra. Floored
                            // rather than taken raw: a fixture the extractor measured as a few
                            // centimetres across would cast an edge as hard as a point light's, and no
                            // real fitting is that small.
                            MathF.Max(fixture.Extent, 0.05f),

                            // The rectangle the fitting emits from, where its shadow rays are aimed.
                            fixture.HasRect ? worldToView.Transform(fixture.Axis) * fixture.HalfLength : default,
                            fixture.HasRect ? fixture.HalfWidth : 0f);
                    }

                    // Both backends, through the seam: a windowless interior needs its lamps whichever
                    // renderer is drawing it, and the ultra preset -- the one that discards the shipped
                    // bakes entirely and so has NOTHING else lighting an interior -- is the Vulkan path.
                    _device.SetLocalLights(_localLightScratch.AsSpan(0, localCount));
                    _stormLampCount = localCount;

                    // And the distance field those lamps' shadows are marched through wherever the renderer
                    // cannot trace them -- WebGPU always, Vulkan with the RT switch off. See
                    // LampOcclusionField.
                    _device.SetLampOcclusionField(LampFieldFor(_sdfClipmaps));
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
                    // THE GLAZING IS DRAWN TWICE, and the two draws answer different questions.
                    //
                    // This one records which pane is nearest at each pixel and which way it faces, into a
                    // pass that owns a depth buffer. That pass exists because the question has one answer
                    // per pixel and the translucency pass could not settle it: two windows in a line both
                    // blend, both used to record, and an image store is not ordered between fragments, so
                    // the answer changed every frame and the reflection fizzed. A depth test settles it
                    // and cannot disagree with itself.
                    //
                    // The second draw, below, is the one you can see: the pane's own tint blended over
                    // the finished picture. It writes no depth, because the tracers and the smoke are
                    // drawn after it through the same buffer.
                    _device.BeginPass(ScenePass.GlassLayer);
                    _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true));

                    _device.EndPass();
                    _device.BeginPass(ScenePass.Forward);
                }

                // Retail rotor blades carry graded alpha (Bell: 72/255). The opaque pass's
                // alpha test discards them entirely; blend their phase samples over the lit world.
                if (mapScene is { } rotorMap)
                {
                    float renderScale = captured ? RenderCVars.RenderScale.Value : 1f;
                    _device.SetViewport(new Viewport(
                        (int)(rotorMap.Viewport.X * renderScale),
                        (int)((_window.Height - rotorMap.Viewport.Y - rotorMap.Viewport.Height) * renderScale),
                        Math.Max(1, (int)(rotorMap.Viewport.Width * renderScale)),
                        Math.Max(1, (int)(rotorMap.Viewport.Height * renderScale))));
                }
_world.Draw(_scene, eye, projection, new WorldDrawOptions(RotorsOnly: true));

                // The level's glazing, over a picture the lighting has finished. It has to follow the
                // opaque world for the ordinary reason anything blended does, and it is a pass of its own
                // rather than part of the rotor pass above because the two share nothing: a rotor is a
                // whole placement that blends, and a pane is half of a texture on a placement whose other
                // half was drawn into the G-buffer.
                _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true));
                if (mapScene is not null)
                {
                    _device.SetViewport(new Viewport(0, 0,
                        captured ? sceneTarget.Width : _window.Width,
                        captured ? sceneTarget.Height : _window.Height));
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

                // And the original's own: concrete1.spr dust, the chips it knocked loose, and blood.
                if (RenderCVars.ImpactEffects.Value)
                {
                    _retailImpacts?.Draw(
                        _impactEffects, eye, projection, smoothPresentation ? tickBlend : 1f);
                }

                // The rocket's smoke trail and the blast's grey column, under the fireball.
                _missileSmoke?.Draw(eye, projection, smoothPresentation ? tickBlend : 1f);

                // The retail fireball, over the scene as light: grenade, missile and shot-out-device
                // blasts drawn from the real fireball.spr sprite.
                _explosions?.Draw(eye, projection, smoothPresentation ? tickBlend : 1f);

                // Rain environmental particles for levels featuring rain weather (e.g. RainEffect task).
                // While the camera is inside a building's shell the rain is suppressed, exactly as the
                // reference editor does per frame — buildings keep the weather outside.
                _stormEyeMetres = eye.Position;
                if (_storm is { } liveStorm)
                {
                    // Assigned BEFORE the active test, never inside it. IsActive reads Indoors, so
                    // setting it under `if (IsActive)` latches: the first frame the eye passes through a
                    // building shell turns the storm off, and the assignment that would turn it back on
                    // is inside the branch that no longer runs. That is what stuck the free camera on
                    // the original's rain for the rest of the mission.
                    liveStorm.Indoors = cameraIndoors;

                    // Rain lands on the ground the player is standing on. The free camera has no feet,
                    // so it keeps whatever the player last supplied rather than dropping the splashes.
                    // This is only the fallback now: the trace below is what the storm actually lands on,
                    // and the plane is what it falls back to where the trace has not reached yet.
                    if (_player is { } stormPlayer)
                    {
                        liveStorm.GroundHeightMetres =
                            stormPlayer.Body.Position.Z / WorldScene.UnitsPerMetre;

                        // The grenade caster, which is the one trace in the game that already covers the
                        // terrain AND the placed collision meshes -- the same two things rain lands on.
                        liveStorm.Ground = stormPlayer.GrenadeWalls;
                        liveStorm.UnitsPerMetre = WorldScene.UnitsPerMetre;
                    }
                }

                if (_storm is { IsActive: true } storm)
                {
                    // Direction and colour from the one effective sun the deferred pass shades with, and
                    // the fill from the sky the deferred lighting itself shades with, averaged over every
                    // direction -- not the level's raw ambient triple, which is a chromaticity that says
                    // what colour the fill is and nothing about how much of it there is, and at night is
                    // several times the light the scene gets. The storm multiplies its colours by this,
                    // so handing over more light than the scene has drew every drop brighter than the
                    // ground behind it: a rainy night became a wall of rain with nothing visible through it.
                    DirectionalLightSettings stormSun = _effectiveSun ?? default;
                    Vector3f stormSunLight =
                        stormSun.Colour * RenderCVars.SunScale.Value * RenderCVars.LightScale.Value;

                    // What the reference multiplies by is the frame BEHIND the drop, and outdoors that
                    // is overwhelmingly sky rather than the flat fill the interiors are lit by. The sky
                    // fill alone left daylight rain almost invisible against a bright sky, which is the
                    // one place rain is most obviously there. A fifth of the sun's own illuminance is a
                    // rough stand-in for a daylit sky's radiance, and it goes to nothing at night with
                    // the sun it is taken from.
                    storm.Draw(
                        eye,
                        projection,
                        smoothPresentation ? tickBlend : 1f,
                        _effectiveSun is null ? Vector3f.UnitZ : stormSun.Direction,
                        stormSunLight,
                        AmbientSkyLight() + (stormSunLight * 0.2f),
                        _loop.TimeSource.Milliseconds / 1000f,
                        _stormLamps.AsSpan(0, _stormLampCount),

                        // A display level, taken back through the frame's exposure into the scene's
                        // units, so the drops never sink below a faint grey whatever the night.
                        RainVeilDisplay / MathF.Max(_device.SceneExposure, 1e-3f));
                }
                else if (_storm is not { Enabled: true } && _rain is { IsWeatherEnabled: true } rain)
                {
                    // The original's band, drawn only where the storm is not the renderer in charge.
                    // Enabled is the question, not IsActive: Enabled says which of the two owns the
                    // weather -- the preset's r_storm, which only vanilla turns off -- while IsActive
                    // says whether that owner has anything to draw this frame. Falling back on IsActive
                    // made every reason the storm draws nothing into a reason to draw 1998's rain
                    // instead, so dialling r_storm_intensity to zero on an ultra preset did not stop the
                    // rain, it replaced it with the vanilla band.
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

                // The through-wall glow over the enemies the binocular scan marked, in the same FORWARD
                // pass and for the same reason as the inspector selection below: submitted from the world
                // pass it would be lit and fogged into a smear. Here it composites over the finished
                // scene and is carried into the resolve, so the lens and bloom pick it up. Only while the
                // remastered optics are up.
                if (remasteredOptics
                    && RenderCVars.BinocularOutline.Value
                    && _player is { IsBinocularsOpen: true })
                {
                    _soldiers?.DrawBinocularOutlines(eye, radiusPixels: 8f);
                }

                // The developer inspector's selection, drawn as a real glow around the model rather
                // than a mark beside it. It belongs to the FORWARD pass, with the tracers and the muzzle
                // flames: submitted from the world pass it would be written into the g-buffer as
                // scenery and come back lit and fogged, which is a dull smear rather than a mark. Late
                // in the pass because it carves its silhouette out of the depth buffer to read through
                // walls, and the view model clears depth immediately after.
                DrawInspectorOutline(console, eye);

                // The first-person view goes last, and in 0x4644A0's own order: pose the rig, draw the arms,
                // then ask the human where the weapon is and draw that. Only in player mode, because the
                // original draws it out of the human's own render and the free camera is not a human -- and
                // NOT once the player is dead, when the death camera is watching the body from the outside
                // and a pair of arms hanging in front of it would be exactly wrong.
                // Not while the entry shot is running: the camera is somewhere over the level, and a pair
                // of arms and a HUD hanging in front of it would be exactly wrong. They come up at the
                // moment the view is handed over, which is also the moment the player can use them.
                bool binocularsOpen = _player is { IsBinocularsOpen: true };

                // Scoped, the Dragunov replaces the first-person picture with its scope overlay: retail's
                // 0x473040 latch branch draws snipersight.spr and never poses the rig, so the rifle and the
                // gloves are gone and only the scope picture remains. Hide the whole view model exactly as
                // the binoculars do, or the weapon mesh shows through the aperture.
                bool sniperScoped = _player is { IsSniperScoped: true };
                float? binocularRangeMetres = null;
                if (binocularsOpen && _worldSolids is { } rangeSolids)
                {
                    // FUN_00471480's DISTANCE readout is a 1000 m ray cast from the eye along the aim
                    // direction; its 0.000244140625 scale is 1/4096 = 1/UnitsPerMetre. The range is
                    // whatever the centre crosshair rests on -- terrain or a placed object -- not the
                    // nearest detected soldier, so a bare hillside still reads a distance. A ray that
                    // reaches its 1000 m end unobstructed leaves the readout at the retail "1000+M".
                    Vector3d forward = eye.Orientation.Transform(new Vector3d(0d, 0d, 1d));
                    Vector3d rangeOrigin = new(
                        eye.Position.X * WorldScene.UnitsPerMetre,
                        eye.Position.Y * WorldScene.UnitsPerMetre,
                        eye.Position.Z * WorldScene.UnitsPerMetre);
                    const double RangeSpanUnits = 1000d * WorldScene.UnitsPerMetre;
                    Vector3d rangeEnd = new(
                        rangeOrigin.X + (forward.X * RangeSpanUnits),
                        rangeOrigin.Y + (forward.Y * RangeSpanUnits),
                        rangeOrigin.Z + (forward.Z * RangeSpanUnits));
                    WorldTraceCast rangeTrace = _playerTerrain is { } rangeGround
                        ? WorldTrace.Nearest(rangeSolids.Tracer(), TerrainRayTrace.Caster(rangeGround))
                        : rangeSolids.Tracer();
                    if (rangeTrace(
                            new WorldTraceRequest(rangeOrigin, rangeEnd, WorldTraceMask.World),
                            out WorldTraceHit rangeHit)
                        && double.IsFinite(rangeHit.Distance))
                    {
                        binocularRangeMetres = (float)(rangeHit.Distance / WorldScene.UnitsPerMetre);
                    }
                }
                // Not before the rig has a clip: see HumanView.IsPosed. At spawn that is the whole lowering
                // half of the first weapon's change, so the weapon rises into an empty frame, as in retail.
                if (showViewModel && _player is { View: { IsPosed: true } rig } && !binocularsOpen && !sniperScoped)
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
                    Matrix4f viewModelProjection = ViewModelProjection.Create(sight.ViewModelTanX, FarPlane);

                    // Between-tick smoothing for everything the rig steps at 30 Hz: its origin (the eye
                    // the body carried plus the clip's hold offset), its skin, and the weapon bone. The
                    // LOOK is already per frame; without this the arms and the knife stepped at 30 Hz
                    // against a camera that now glides -- which read as jitter -- and skipped several
                    // poses per drawn frame whenever ticks ran back to back, which read as the animation
                    // racing.
                    Vector3d? rigOriginUnits = null;
                    Matrix3f? rigOrientation = null;
                    BoneAttachment? weaponAttachment = _player.HeldWeapon;
                    BoneAttachment? magazineAttachment = _player.HeldWeaponSecondary;
                    BoneAttachment? movingPartAttachment = _player.HeldWeaponTertiary;
                    float previousPoseShare = 0f;
                    if (smoothPresentation && _player.HasViewTickPair)
                    {
                        (
                            Vector3d smoothedOrigin,
                            Matrix3f smoothedOrientation,
                            BoneAttachment? smoothedWeapon,
                            BoneAttachment? smoothedMagazine,
                            BoneAttachment? smoothedMovingPart) =
                            _player.SmoothedRig(tickBlend);
                        rigOriginUnits = smoothedOrigin;
                        rigOrientation = smoothedOrientation;
                        weaponAttachment = smoothedWeapon ?? weaponAttachment;
                        magazineAttachment = smoothedMagazine ?? magazineAttachment;
                        movingPartAttachment = smoothedMovingPart ?? movingPartAttachment;
                        previousPoseShare = 1f - tickBlend;
                    }

                    // The retail rig is foreground-only: it draws after the world under its own transform
                    // layer, so an already-rendered wall must not be allowed to cut through the hands or
                    // weapon. Resetting only depth keeps normal arm-versus-weapon self-occlusion.
                    ViewModelDepth.Begin(_device);
                    // The arms and the weapon are not the world: the eye adaptation must not meter them
                    // and the indirect gather must not bounce light off them, or holstering changes the
                    // lighting of a room that has not moved.
                    _device.SetFirstPersonGeometry(true);
                    _arms?.Draw(rig, eye, viewModelProjection, rigOriginUnits, previousPoseShare, rigOrientation);

                    // Cycling weapons changes the model as well as the animations, so the renderer has to
                    // follow whatever the cycle put in hand.
                    if (_loadedMissionForWeapon is { } missionForWeapon)
                    {
                        FollowHeldWeapon(_player, _heldWeapon, missionForWeapon);
                    }

                    if (_player.Weapons?.HideFirstPersonWeapon != true
                        && weaponAttachment is { } attachment
                        && _heldWeapon is { IsReady: true })
                    {
                        _heldWeapon.Draw(
                            attachment,
                            eye,
                            viewModelProjection,
                            // 0x473510 is a separate GunM16A2 task: it does not pass through the
                            // ordinary Gun_Fire/GUNFLAME path. Do not show the rifle muzzle flame on
                            // an M203 launch; the launcher task owns its own launch report/effect.
                            _player.FiredThisTick && !_player.M203LaunchedThisTick,
                            _player.ShotSequence,
                            WeaponDrawParts.All,
                            magazineAttachment,
                            movingPartAttachment);
                    }

                    _device.SetFirstPersonGeometry(false);
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
                if (RemasteredHudPresentation.UsesRetailWeaponHud(
                        remasterHud,
                        _player?.IsBinocularsOpen == true,
                        remasteredOptics)
                    && showFirstPersonHud
                    && _player is { Weapons: { } carried }
                    && _hud is { IsReady: true } retailHud)
                {
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    // Scoped, the Dragunov's overlay REPLACES the weapon HUD -- 0x473040's latch branch
                    // never calls the parent handler.
                    if (_player.IsBinocularsOpen)
                    {
                        retailHud.DrawBinocularOverlay(
                            batch,
                            _window.Width,
                            _window.Height,
                            binocularTargets,
                            _player.Yaw,
                            _loop.Clock.TickCount,
                            binocularRangeMetres);
                    }
                    else if (_player.IsSniperScoped)
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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

                    Matrix4f computerViewModelProjection = ViewModelProjection.Create(
                        sight.ViewModelTanX,
                        FarPlane);

                    if (_mapComputerDevice is { IsReady: true } deviceModel)
                    {
                        Viewport deviceViewport = _mapComputer!.DeviceViewport(_window.Width, _window.Height);
                        float horizontalHalfTangent = sight.ViewModelTanX;
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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

                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    batch.DrawQuad(
                        TextureHandle.None,
                        new Vertex2D(0f, 0f, 0f, 0f, Red(_damageFlash.TopLeft)),
                        new Vertex2D(flashWidth, 0f, 1f, 0f, Red(_damageFlash.TopRight)),
                        new Vertex2D(flashWidth, flashHeight, 1f, 1f, Red(_damageFlash.BottomRight)),
                        new Vertex2D(0f, flashHeight, 0f, 1f, Red(_damageFlash.BottomLeft)));
                    batch.End();
                }

                // The native player flash handler writes a uniform white screen field. It is stronger
                // when the eye is inside the grenade radius (2) and still fully white at the first
                // frame of the 25 m visible effect (1); the close field simply keeps the quad opaque
                // for longer. This pass follows the red damage wash so the retail flash wins.
                if (_flashbangScreenFlash.IsActive)
                {
                    byte whiteAlpha = (byte)Math.Clamp(
                        (int)(Math.Min(_flashbangScreenFlash.Strength, 1f) * 255f),
                        0,
                        255);
                    Bgra32 white = new(255, 255, 255, whiteAlpha);
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                // The world-anchored marker carries the same progress on its own ring, at the object,
                // so the centred bar would be the same reading twice.
                if (remasterHud
                    && !worldInteractionPrompt
                    && _player?.InteractionProgress is float interactionProgress
                    && _remasteredHud is { IsReady: true } interactionHud)
                {
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    scopeHud.DrawSniperScope(batch, _window.Width, _window.Height);
                    batch.End();
                }

                if (remasteredOptics
                    && showFirstPersonHud
                    && _player is { IsBinocularsOpen: true }
                    && _player.View is { } binocularView
                    && _remasteredHud is { IsReady: true } binocularHud)
                {
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    binocularHud.DrawBinocularOverlay(
                        batch,
                        _player.Yaw,
                        binocularView.TanX,
                        _window.Width,
                        _window.Height,
                        binocularTargets,
                        _binocularOptics.Seconds,
                        binocularRangeMetres);
                    batch.End();
                }

                if (remasterHud
                    && showFirstPersonHud
                    && _player is { Weapons: { } modernCarried }
                    && !_player.IsBinocularsOpen
                    && _remasteredHud is { IsReady: true } modernHud)
                {
                    CarriedWeapon? inHand = modernCarried.InHand;
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                            _player.IsSniperScoped,
                            worldInteractionPrompt),
                        _window.Width,
                        _window.Height);
                    batch.End();
                }

                // The marker itself, over the finished frame and over the HUD. It is a surface standing
                // in the level rather than an overlay on the picture, so it is handed the same presented
                // eye and the same lens that drew the thing it belongs to, and it projects itself.
                if (worldInteractionPrompt
                    && _interactionPrompt.Visible is { } promptTarget
                    && _remasteredHud is { IsReady: true } promptHud)
                {
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    promptHud.DrawInteractionPrompt(
                        batch,
                        new InteractionPromptView(
                            promptTarget.AnchorUnits / WorldScene.UnitsPerMetre,
                            _interactionPrompt.Opacity,
                            _interactionPrompt.Pop,
                            InteractionPromptPresentation.BadgeLabel(
                                PlayerProfileInputBindings.BoundKeyName(
                                    _configuration.Active,
                                    PlayerInputBindings.ConfigName(PlayerAction.Activate))),
                            InteractionPromptPresentation.Title(promptTarget),
                            InteractionPromptPresentation.Detail(promptTarget),
                            InteractionPromptPresentation.RingProgress(promptTarget)),
                        eye,
                        horizontalTangent,
                        aspectRatio,
                        SafeWindowWidth,
                        SafeWindowHeight);
                    batch.End();
                }

                // The HUD is in the condition, not asserted inside it. Every presenter is nullable so a
                // title under construction draws the passes it has and skips the rest -- and I.G.I. 2 has
                // no HUD at all yet, so the null-forgiving read of it here took the process down as soon
                // as one of its missions had an objective to post.
                if (tacticalMessages
                    && showFirstPersonHud
                    && _remasteredHud is { } messageHud
                    && _missionObjectives is { } modernMessages
                    && _messages is { IsReady: true } modernCards)
                {
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    float topOffset = new LayoutContext(_window.Width, _window.Height)
                        .Px(messageHud.MessageTopInset);
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
                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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

                // The sun gizmo, over everything, whenever it is up -- not gated on the F12 overlay, since it
                // is a tool being used rather than an inspection panel.
                if (_sunGizmo.Active)
                {
                    _device.BeginStageTiming(RenderStage.DebugRendering);
                    batch.Begin();
                    _sunGizmo.Draw(batch);
                    batch.End();
                    _device.EndStageTiming();
                }

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
                    const double ReachU = 300.0 * WorldScene.UnitsPerMetre;
                    Vector3d endU = new(
                        originU.X + (fwd.X * ReachU), originU.Y + (fwd.Y * ReachU), originU.Z + (fwd.Z * ReachU));

                    string target = "target: (nothing / terrain / sky)";
                    string? detect = null;
                    double bestT = double.MaxValue;
                    if (_worldSolids is not null
                        && _worldSolids.TryIdentify(
                            originU,
                            endU,
                            out string objLabel,
                            out WorldPlacement? objPlacement,
                            out WorldRayHit objHit))
                    {
                        bestT = objHit.Distance;

                        // The texture, from the RENDER mesh, because mat= cannot give it. The contact
                        // above is on the collision mesh and names a game-material row -- SoftMetal,
                        // Glass -- which drives bullets and decals and is read by nothing in the
                        // renderer. What the renderer reads is materials.json, keyed by texture name,
                        // and until this line there was no way to learn that name from inside the game:
                        // somebody wanting to say "this pane is glass" had to guess which of a level's
                        // hundred numbered textures they were looking at.
                        string surface =
                            objPlacement is not null
                            && RenderSurfaceProbe.TryFindTexture(
                                objPlacement,
                                originU,
                                new Vector3d(endU.X - originU.X, endU.Y - originU.Y, endU.Z - originU.Z),
                                out string objTexture,
                                out _)
                                ? objTexture
                                : "?";

                        target = I($"target: OBJECT {objLabel} @ {objHit.Distance / WorldScene.UnitsPerMetre:F1}m ({objHit.Position.X:F0}, {objHit.Position.Y:F0}, {objHit.Position.Z:F0}) mat={objHit.Material} tex={surface}");
                    }

                    if (_soldiers is not null
                        && _soldiers.TryPick(originU, fwd, ReachU, out string botName, out double botT, out Vector3d feetU, out int botZone)
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
                        && _soldiers.TryDetectionInfo(originU, fwd, ReachU, watchedEye, out string detectInfo))
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

                    // Which of the level's lamps the crosshair is on, if any -- so the switch keys can be
                    // aimed rather than guessed at, and so a press that appeared to do nothing can be told
                    // apart from a press that landed on a lamp whose light is off screen.
                    if (DescribeAimedLamp() is { } aimedLamp)
                    {
                        lines.Add(aimedLamp);
                    }

                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                    batch.Begin();
                    debugPanelHeight = debugOverlay.Draw(batch, lines, _window.Width, _window.Height);

                    // A centre reticle so the target readout can be aimed — the crosshair the free camera lacks.
                    float cx = _window.Width * 0.5f;
                    float cy = _window.Height * 0.5f;
                    const float Len = 9f;
                    const float Th = 1f;
                    static Bgra32 White() => new(255, 255, 255, 220);
                    void Bar(float x0, float y0, float x1, float y1) => batch.DrawQuad(
                        TextureHandle.None,
                        new Vertex2D(x0, y0, 0f, 0f, White()),
                        new Vertex2D(x1, y0, 1f, 0f, White()),
                        new Vertex2D(x1, y1, 1f, 1f, White()),
                        new Vertex2D(x0, y1, 0f, 1f, White()));
                    Bar(cx - Len, cy - Th, cx + Len, cy + Th);
                    Bar(cx - Th, cy - Len, cx + Th, cy + Len);

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

                        // Which of the two shadow systems is actually drawing, spelled out with the keys
                        // that move it. Reading a shadow against the reference means knowing which one
                        // produced it, and the answer is two cvars deep -- r_rt_shadows does nothing
                        // while r_rt is off, and neither means anything with shadows turned off.
                        I($"shadow   {ShadowModeLabel()}"),
                        I($"texture  {TextureModeLabel()}"),
                        I($"fps {frame.FramesPerSecond:0.0}  frame {frame.AverageMilliseconds:0.00} ms  1% {frame.OnePercentLowFramesPerSecond:0.0}"),

                        // The whole GPU frame measured end to end, against the sum of the stages listed
                        // below it. Everything else on this overlay is built from those stages, so a pass
                        // recorded outside every one of them shows up nowhere -- and a millisecond of lamp
                        // shadow did exactly that for a whole session while this panel read as though the
                        // lamps were free. "lost" is that difference: a few hundredths is the timestamps'
                        // own overhead, anything more is real work with nobody's name on it.
                        MeasuredFrameLine(_device, gpu),

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

                    // The CPU side the GPU timers cannot see. cpu is how much longer the frame's wall time
                    // is than the GPU's -- submission, game logic and GC that did not overlap the GPU. alloc
                    // is the managed allocation rate, eased so it is readable; a high figure is what feeds the
                    // GC collections whose pauses read as the frame's stutter, and the LOH share of it drives
                    // the Gen2 collections that stall hardest. gc shows the Gen0/1/2 collection counts so a
                    // climbing Gen2 is visible while playing rather than only under a profiler.
                    long allocNow = GC.GetTotalAllocatedBytes();
                    long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (_perfAllocTicks != 0)
                    {
                        double seconds = (nowTicks - _perfAllocTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                        if (seconds > 1e-4)
                        {
                            double sample = (allocNow - _perfAllocBytes) / seconds / (1024.0 * 1024.0);
                            _perfAllocRate = (_perfAllocRate * 0.9) + (sample * 0.1);
                        }
                    }

                    _perfAllocBytes = allocNow;
                    _perfAllocTicks = nowTicks;
                    double cpuMilliseconds = Math.Max(0.0, frame.AverageMilliseconds - gpu.Milliseconds);
                    perf.Add(I($"cpu {cpuMilliseconds,5:0.00} ms   alloc {_perfAllocRate,6:0} MB/s   gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));

                    _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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
                    List<string> world = [];
                    _world.Describe(world);
                    _logger.Information("World", $"{string.Join("; ", world)}, from {_camera.Position}.");

                    if (_terrain is not null)
                    {
                        // The presenter formats its own counters: what is worth reporting about a
                        // streamed octree is not what is worth reporting about a grid of tiles.
                        List<string> terrain = [];
                        _terrain.Describe(terrain);
                        _logger.Information("Terrain", string.Join("; ", terrain));
                    }
                }
            }

            // The cinematic shell draws over the resolved showcase scene, in 2D on the back buffer, before
            // the console (which goes over even the shell).
            // The shell keeps being DRAWN through the mission-entry shot even though the menu is over, so
            // that it can fade rather than vanish. It hears nothing while it does.
            if ((menuMode || _entry.IsRunning) && shell is not null)
            {
                // Exactly one layer may draw a pointer. The console is drawn after this and over it,
                // so while it is up it owns the cursor; otherwise the reader sees two arrows.
                shell.Stack.PointerSuppressed = console.IsOpen;

                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                var shellPainter = new UiPainter(batch, new LayoutContext(_window.Width, _window.Height));
                batch.Begin();

                // The going-in sequence's picture: the level exactly as it is drawn, before any of the
                // interface goes over it. Taken here for the same reason the dissolve's hold is.
                CaptureLoadPictureIfWanted();

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
                float shellOpacity = MathF.Min(
                    _entry.IsRunning ? _entry.ShellOpacity : 1f,
                    _loadSequence.ShellOpacity);
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
                    DrawEntryFraming(shellPainter);
                }

                batch.End();
                _device.EndStageTiming();
            }

            // The level, withheld while it settles. Over the HUD and the menus because it stands in for
            // the whole picture, and under the console because the console has to be usable while a level
            // is warming -- that is exactly when somebody is looking into why it is.
            DrawMissionWarmUp(batch);

            // The mission's caption, standing in the level under the shot: over the defocus, so it is the
            // one sharp thing while the lighting settles, and under the console.
            DrawWorldTitle(batch);

            // The console goes over everything, including the menus, so it is the last thing drawn.
            // WantsDraw rather than IsOpen: the watch and performance panels can be pinned to stay up
            // with the console shut. It is false during a capture run whatever the reader pinned, so a
            // pinned overlay can never leak into a reference screenshot and break the vanilla gate.
            if (console.WantsDraw)
            {
                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
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

            // Authored-scene capture: write numbered stills of the requested scene every 90 logic ticks
            // (3 s) up to the still budget. Test-only; driven by environment variables.
            // A chained intro resets its scene-local clock. Do not carry the previous scene's next
            // capture threshold into the next scene and silently miss its early/short shots.
            if (captureCutscene && _cutscenes?.ActiveScene is { } observedScene
                && !ReferenceEquals(previousCapturedScene, observedScene))
            {
                previousCapturedScene = observedScene;
                cutsceneCaptureTick = cutsceneCaptureStartTick;
            }
            if (captureCutscene
                && cutsceneCaptureCount < cutsceneCaptureLimit
                && _cutscenes?.ActiveScene is { } activeCutscene
                && (cutsceneCaptureScene <= 0 || activeCutscene.Id == cutsceneCaptureScene)
                && activeCutscene.Ticks >= cutsceneCaptureTick)
            {
                Directory.CreateDirectory(cutsceneCaptureDirectory!);
                Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
                _device.CaptureFrame(_window.Width, _window.Height, capture);
                int missionId = _activeMission?.Id ?? 0;
                string path = Path.Combine(
                    cutsceneCaptureDirectory!,
                    FormattableString.Invariant(
                        $"mission-{missionId:00}-scene-{activeCutscene.Id}-tick-{activeCutscene.Ticks:000000}.bmp"));
                BitmapWriter.Write(path, _window.Width, _window.Height, capture);
                _logger.Information("CutsceneCapture", $"Wrote {path}.");
                cutsceneCaptureTick += 90;
                cutsceneCaptureCount++;
            }

            AdvanceRendererSwitch();

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

                if (_options.LightingDumpPath is { } lightingDumpPath)
                {
                    WriteLightingDump(lightingDumpPath);
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

            if (_benchmarkAllocStart > 0)
            {
                long allocated = GC.GetTotalAllocatedBytes() - _benchmarkAllocStart;
                int measured = Math.Max(1, frame.SampleCount);

                // Warning, not Information: the shell builds its logger at Warning, so an Information call
                // here is discarded and the benchmark's allocation report -- the one number that says
                // whether a stutter is the collector rather than the GPU -- was written to nothing. The
                // camera log had the same hole for the same reason.
                _logger.Warning(
                    "Benchmark",
                    FormattableString.Invariant(
                        $"Managed alloc over {measured} measured frames: {allocated / (1024.0 * 1024.0):0.0} MB, {allocated / (double)measured / 1024.0:0.0} KB/frame; gc {GC.CollectionCount(0) - _benchmarkGcStart.Item1}/{GC.CollectionCount(1) - _benchmarkGcStart.Item2}/{GC.CollectionCount(2) - _benchmarkGcStart.Item3}."));
            }
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
                _device.GpuMeasuredTiming().Milliseconds,
                gpuStages.ToArray());

            string? benchmarkDirectory = Path.GetDirectoryName(Path.GetFullPath(benchmarkPath));
            if (!string.IsNullOrEmpty(benchmarkDirectory))
            {
                Directory.CreateDirectory(benchmarkDirectory);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(snapshot, RenderPerformanceJsonContext.Default.RenderPerformanceSnapshot);
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
                        $"GPU total avg {gpu.Milliseconds:0.000} ms  p95 {gpu.P95Milliseconds:0.000} ms  peak {gpu.PeakMilliseconds:0.000} ms of the {RenderBudget.FrameMilliseconds:0.00} ms a 60 Hz frame has; {over} stage(s) over budget."));
            }
        }

        // A bake still tracing at this point is tracing for a level nobody will see again. The worker is a
        // thread-pool thread and cannot hold the process open, so this is not about shutting down cleanly --
        // it is about not spending every core on it while the rest of the shutdown runs.
        _probeBake?.Cancel();
        _lightmapBake?.Cancel();

        if (!_options.IsBenchmark)
        {
            HostConfiguration.Save(_configuration, _logger);
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

    private void ApplyWeatherSettings()
    {
        // The campaign screen's choice overrides both the level's own data and the player's
        // r_weather_kind, and it has to FORCE: eleven of the fourteen retail missions author no weather
        // at all, so a choice that only asked politely would do nothing in most of the campaign and read
        // as a broken setting. Default forces nothing and leaves every answer where it was.
        WeatherKind weatherKind = _conditions.WeatherStyle(RenderCVars.WeatherStyle.Value);
        bool forceWeather = RenderCVars.WeatherForce.Value || _conditions.ForcesWeather;

        _rain?.ApplySettings(
            RenderCVars.WeatherEnabled.Value,
            weatherKind,
            RenderCVars.WeatherSpeed.Value,
            forceWeather);
        _snow?.ApplySettings(
            RenderCVars.WeatherEnabled.Value,
            weatherKind,
            RenderCVars.WeatherSpeed.Value,
            forceWeather);

        if (_storm is { } storm)
        {
            // Resolved after the rain renderer, not before it, so this reads the answer the console just
            // produced rather than the one from the last time the level was loaded. The level decides
            // whether it rains at all (unless r_weather_force overrides it); r_storm only decides which
            // renderer draws it.
            bool rains = _rain?.IsWeatherEnabled ?? false;
            storm.Enabled = rains && RenderCVars.Storm.Value;
            storm.Configure(storm.Settings with
            {
                Intensity = RenderCVars.StormIntensity.Value,
                FallSpeedScale = RenderCVars.WeatherSpeed.Value,
            });

            // Reported every time rather than only on a change: the case worth seeing is the one where
            // nothing changed and the storm is still not drawing, and a log that only fires on a
            // transition is silent for exactly that.
            _logger.Information(
                "Storm",
                FormattableString.Invariant(
                    $"enabled={storm.Enabled} active={storm.IsActive} rains={rains} r_storm={RenderCVars.Storm.Value} r_weather_enabled={RenderCVars.WeatherEnabled.Value} kind={weatherKind} force={forceWeather} campaign={_conditions.Weather}/{_conditions.TimeOfDay} intensity={RenderCVars.StormIntensity.Value}"));
        }
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
    /// <summary>Chooses which of the level's lamps this frame submits, nearest first.</summary>
    /// <param name="eye">Where the camera stands, in world metres.</param>
    /// <param name="range">Unused by the ranking; see the remarks on why it must not scale it.</param>
    /// <returns>How many entries of <c>_localLightOrder</c> are valid.</returns>
    /// <remarks>
    /// <para>
    /// A level's lamps outnumber the shader's array several times over, so this is a choice and not a
    /// truncation. Each fixture is ranked by how far OUTSIDE its own reach the camera stands: a lamp the
    /// camera is inside ranks negative and always makes the cut, and beyond that a long fluorescent
    /// batten twenty metres off beats a desk lamp fifteen metres off, which is the right way round
    /// because reach is what decides whether a lamp can light anything the player can see.
    /// </para>
    /// <para>
    /// The camera's own position rather than the frustum: a lamp behind the player still lights the wall
    /// in front of them, and culling to what is on screen would switch that light off as they turned.
    /// </para>
    /// <para>
    /// A running insertion into an array of thirty-two rather than a sort of the whole list. The list is
    /// a few hundred long and this runs every frame; an insertion sort over a fixed, tiny destination is
    /// linear in the input and allocates nothing, where sorting the input is neither.
    /// </para>
    /// </remarks>
    private int SelectNearestLamps(Vector3d eye, float range)
    {
        int kept = 0;

        for (int i = 0; i < _lightFixtures.Count; i++)
        {
            LightFixture fixture = _lightFixtures[i];

            // A lamp that has finished going out is not a dim light, it is not a light: passed over here
            // rather than submitted at zero, because the shader's array is a budget and a dark fixture
            // holding a slot switches a real one at the end of the corridor off instead.
            if (_lampSwitchboard is { } board && board.LevelOf(i) <= 0f)
            {
                continue;
            }

            double dx = fixture.Position.X - eye.X;
            double dy = fixture.Position.Y - eye.Y;
            double dz = fixture.Position.Z - eye.Z;
            // The fixture's OWN size, not its size times the reach multiplier. Scaling this by the
            // multiplier meant raising r_local_light_range -- which people raise to make lamps carry
            // further -- also multiplied how much a fixture's size counted for a slot, so the big
            // battens took the budget and the small near fittings went dark EARLIER than before. Reach
            // decides how far a lamp lights; this decides which lamps are worth submitting, and the two
            // should not move together.
            float rank = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) - fixture.Radius;

            // Nothing to insert: this fixture is further out than every one already held, and the array
            // is full. The common case by far once the level is more than thirty-two lamps deep.
            if (kept == _localLightOrder.Length && rank >= _localLightRank[kept - 1])
            {
                continue;
            }

            int at = Math.Min(kept, _localLightOrder.Length - 1);

            while (at > 0 && _localLightRank[at - 1] > rank)
            {
                _localLightRank[at] = _localLightRank[at - 1];
                _localLightOrder[at] = _localLightOrder[at - 1];
                at--;
            }

            _localLightRank[at] = rank;
            _localLightOrder[at] = i;

            if (kept < _localLightOrder.Length)
            {
                kept++;
            }
        }

        return kept;
    }

    /// <summary>Hands the player the ray their crosshair sits on.</summary>
    /// <param name="drawn">The camera the frame was rasterised from.</param>
    /// <remarks>
    /// <para>
    /// Only the ray. What it is over is settled on the simulation tick, by
    /// <see cref="PlayerCamera.AimConvergenceRay"/>, against the same world trace and the same human
    /// probe a round marches — because a crosshair resolved by one test and a bullet resolved by
    /// another disagree exactly where it matters. The first attempt at this traced here, with the
    /// renderer's own wall query, and looked straight through people: every shot at a guard converged on
    /// whatever stood behind him and landed the camera's own half-metre wide.
    /// </para>
    /// <para>
    /// Cleared the moment the camera is back at the eye, which is what leaves first person firing along
    /// the body's forward with no branch anywhere to say so.
    /// </para>
    /// </remarks>
    private void ConvergeThirdPersonAim(in Camera3D drawn)
    {
        if (_player is null)
        {
            return;
        }

        if (!_thirdPerson.IsEngaged)
        {
            _player.AimConvergenceRay = null;
            return;
        }

        _player.AimConvergenceRay = (
            new Vector3d(
                drawn.Position.X * WorldScene.UnitsPerMetre,
                drawn.Position.Y * WorldScene.UnitsPerMetre,
                drawn.Position.Z * WorldScene.UnitsPerMetre),
            drawn.Orientation.Row3);
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
    /// Cached against the two references it is built from rather than rebuilt per frame: it is called
    /// once a frame and would otherwise allocate a closure every one of them.
    /// </para>
    /// </remarks>
    private DeathCameraVantage.Blocked? ThirdPersonWalls()
    {
        if (_worldSolids is null && _playerTerrain is null)
        {
            _thirdPersonWalls = null;
            _thirdPersonWallsSolids = null;
            _thirdPersonWallsTerrain = null;
            return null;
        }

        if (_thirdPersonWalls is not null
            && ReferenceEquals(_thirdPersonWallsSolids, _worldSolids)
            && ReferenceEquals(_thirdPersonWallsTerrain, _playerTerrain))
        {
            return _thirdPersonWalls;
        }

        _thirdPersonWallsSolids = _worldSolids;
        _thirdPersonWallsTerrain = _playerTerrain;

        WorldRayCast? cast = (_worldSolids, _playerTerrain) switch
        {
            ({ } solids, { } ground) => WorldTrace.AsRayCast(
                WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(ground))),
            ({ } solids, null) => solids.Caster(),
            (null, { } ground) => WorldTrace.AsRayCast(TerrainRayTrace.Caster(ground)),
            _ => null,
        };

        _thirdPersonWalls = cast is null
            ? null
            : (Vector3d from, Vector3d to, out Vector3d hit) =>
            {
                bool blocked = cast(from, to, out WorldRayHit wall);
                hit = blocked ? wall.Position : default;
                return blocked;
            };

        return _thirdPersonWalls;
    }

    private static float VerticalFieldOfView(float aspectRatio, float horizontalTangent) =>
        2f * MathF.Atan(horizontalTangent / MathF.Max(aspectRatio, 0.0001f));

    // The angular offset a hand holding a camera would add on a given frame, in radians.
    //
    // Modelled on a hand rather than on noise, because the two are not interchangeable for what this
    // tests. A hand drifts: slow wandering of about a degree with a faster, much smaller tremor riding on
    // it. Per-frame white noise would instead point somewhere new every frame, which no camera has ever
    // done and which a temporal filter would rightly treat as unreprojectable and discard -- so it would
    // measure the filter's give-up path rather than its reprojection.
    //
    // Summed sines at frequencies with no common multiple, so the pattern does not repeat over any
    // capture worth taking, driven off the FRAME INDEX rather than the clock so -lockstep keeps a run
    // repeatable. Weights sum to one per axis, which is what makes the amplitude mean what it says, and
    // the two axes carry different frequencies and phases so the motion wanders instead of tracking a
    // diagonal.
    private static (float Yaw, float Pitch) HandheldShake(int frame, float degrees)
    {
        if (degrees <= 0f)
        {
            return (0f, 0f);
        }

        // Hand tremor is quoted in Hz, so the frequencies below are meaningless without a rate to read
        // them against. The capture path steps at a fixed sixty under -lockstep.
        const float FramesPerSecond = 60f;
        float t = frame / FramesPerSecond;
        float amplitude = degrees * (MathF.PI / 180f);

        float yaw =
            (0.60f * MathF.Sin((2f * MathF.PI * 0.37f * t) + 0.0f))
            + (0.28f * MathF.Sin((2f * MathF.PI * 0.91f * t) + 1.7f))
            + (0.12f * MathF.Sin((2f * MathF.PI * 7.30f * t) + 4.2f));

        float pitch =
            (0.55f * MathF.Sin((2f * MathF.PI * 0.29f * t) + 2.4f))
            + (0.30f * MathF.Sin((2f * MathF.PI * 1.13f * t) + 5.1f))
            + (0.15f * MathF.Sin((2f * MathF.PI * 6.10f * t) + 0.9f));

        return (amplitude * yaw, amplitude * pitch);
    }

    // Whether the camera sits inside one of the current level's building shells, and so out of the rain.
    // The volumes are the per-mission shells precomputed by RainRenderer.BuildBuildingVolumes.
    /// <summary>Names the shadow system actually drawing this frame, for the performance overlay.</summary>
    /// <returns>The mode, and the keys that move it.</returns>
    /// <remarks>
    /// Reads the same three conditions the renderer does, in the same order, rather than reporting a
    /// cvar: r_rt_shadows means nothing while r_rt is off, r_rt means nothing on a backend that cannot
    /// trace, and neither means anything with shadows turned off. Reporting the cvar rather than the
    /// consequence is how someone ends up comparing two shadows they believe came from different systems.
    /// </remarks>
    /// <summary>The overlay's line comparing the measured GPU frame against the sum of its stages.</summary>
    /// <remarks>
    /// Reports "not measured" rather than a zero on a backend with no frame bracket, because a zero
    /// difference and an absent measurement mean opposite things and the whole point of the line is that
    /// it can be believed.
    /// </remarks>
    private static string MeasuredFrameLine(IGraphicsDevice device, GpuFrameTiming summed)
    {
        GpuFrameTiming measured = device.GpuMeasuredTiming();

        if (measured.Milliseconds <= 0.0)
        {
            return FormattableString.Invariant(
                $"gpu      {summed.Milliseconds:0.00} ms summed   (frame not measured)");
        }

        return FormattableString.Invariant(
            $"gpu      {measured.Milliseconds:0.00} measured   {summed.Milliseconds:0.00} summed   lost {measured.Milliseconds - summed.Milliseconds:0.00}");
    }

    /// <summary>What the remaster's two asset features are doing, and the keys that move them.</summary>
    /// <remarks>
    /// Both are comparison switches, so the thing worth saying is which side of the comparison is on
    /// screen right now — a frame is worth nothing as evidence if you cannot tell which picture it is.
    /// Spelled out with the keys for the same reason <see cref="ShadowModeLabel"/> is: the answer is two
    /// variables deep, and "no overrides present" looks exactly like "overrides turned off" in the picture.
    /// </remarks>
    private string TextureModeLabel()
    {
        string upscaled = _overrides.Count == 0
            ? "original (no overrides folder)"
            : RenderCVars.OverridesEnabled.Value
                ? $"{_resources.OverriddenCount} override(s) [F8 for the originals]"
                : $"original [F8 for the {_overrides.Count} override(s)]";

        // Zero at every preset but high, so "off" here is usually the authored answer rather than a key
        // somebody pressed -- worth distinguishing, because the maps look identical to absent when the
        // strength is nothing.
        string detail = RenderCVars.MaterialDetail.Value <= 0f
            ? "no detail (r_material_detail is 0)"
            : RenderCVars.MaterialDetailEnabled.Value
                ? $"detail {RenderCVars.MaterialDetail.Value:0.##} [F9 off]"
                : $"detail off [F9 back to {RenderCVars.MaterialDetail.Value:0.##}]";

        return $"{upscaled}, {detail}";
    }

    private string ShadowModeLabel()
    {
        bool enabled = RenderCVars.Shadows.Value > 0
            && RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value;

        if (!enabled)
        {
            return "off (r_shadows 0)";
        }

        if (!_device.Capabilities.SupportsRayQuery)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value} (no ray tracing on this backend)");
        }

        if (!RenderCVars.HardwareRayTracing.Value)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value}  [F6 traces everything]");
        }

        if (!RenderCVars.TracedSunShadows.Value)
        {
            return FormattableString.Invariant(
                $"cascaded map, {RenderCVars.ShadowCascades.Value} cascades at {RenderCVars.Shadows.Value}  [F7 traces the sun; rest of the frame is traced]");
        }

        return FormattableString.Invariant(
            $"ray query, sun disc {RenderCVars.SunAngle.Value:0.0} deg  [F7 back to the map, F6 back to software]");
    }

    /// <summary>Describes the level's distance field for the backend, relative to the camera.</summary>
    /// <param name="eye">Where the camera is, which every box is measured from.</param>
    /// <returns>The clipmaps and their boxes, or nothing when there is no field to march.</returns>
    /// <remarks>
    /// CAMERA-RELATIVE, and that is not a convenience. These levels sit tens of kilometres from the world
    /// origin, where a float has metres of precision left; the march that reads them is a loop of steps a
    /// fraction of a voxel long, so it has to work in a space centred on the camera. Every other traced
    /// pass in the renderer already does.
    /// </remarks>
    private DistanceFieldBinding DescribeDistanceField(Vector3d eye)
    {
        if (_sdfClipmaps is not { } field || !field.Clipmaps.IsValid)
        {
            return DistanceFieldBinding.None;
        }

        GlobalDistanceFieldSettings settings = field.Settings;
        int levels = Math.Clamp(settings.Levels, 0, DistanceFieldBinding.MaxLevels);
        if (levels <= 0)
        {
            return DistanceFieldBinding.None;
        }

        Span<DistanceFieldLevel> described = stackalloc DistanceFieldLevel[DistanceFieldBinding.MaxLevels];
        for (int level = 0; level < levels; level++)
        {
            Vector3d corner = field.Origins[level];
            described[level] = new DistanceFieldLevel(
                new Vector3f(
                    (float)(corner.X - eye.X),
                    (float)(corner.Y - eye.Y),
                    (float)(corner.Z - eye.Z)),
                settings.VoxelSizeOf(level),
                field.RangeOfLevel(level));
        }

        return new DistanceFieldBinding(
            field.Clipmaps,
            levels,
            settings.Resolution,
            described[0],
            described[1],
            described[2],
            described[3]);
    }

    /// <summary>Reports whether the camera has a roof over it, so the frame is drawn as an interior.</summary>
    /// <param name="position">Where the camera stands, in world metres.</param>
    /// <remarks>
    /// This used to be "inside the bounding box of a task named <c>Building_</c>", and that box is not
    /// the building: one standing at an angle to the world axes fills barely a third of it, so a quarter
    /// of the ground the test called indoors on mission one has open sky over it. That is what stood the
    /// fog down and stopped the rain over the middle of an open apron. What decides it now is whether
    /// anything solid is actually overhead — see <see cref="ShelterHeightField.IsUnderCover"/>, which
    /// takes a small vote around the eye so a pipe passing over does not read as a ceiling.
    /// </remarks>
    private bool IsCameraInsideBuilding(Vector3d position) => _shelterField.IsUnderCover(position);

    /// <summary>Determines whether an in-game menu holds the pointer, standing the cameras down.</summary>
    /// <remarks>
    /// Both of these screens release the cursor every frame so it can be used, and every camera branch
    /// below them takes it back: the death orbit, the ladder camera, the look. Whichever runs last in
    /// the frame wins, so a menu that does not stop the chain here ends up fighting one of them and the
    /// pointer flashes between locked and free without ever moving.
    /// </remarks>
    internal static bool MenuOwnsPointer(bool pauseMenu, bool missionFailedMenu) =>
        pauseMenu || missionFailedMenu;

    /// <summary>Converts an authored EditCamera lens through the retail 4:3 composition aperture.</summary>
    internal static float CutsceneVerticalFieldOfView(float horizontalTangent) =>
        VerticalFieldOfView(4f / 3f, horizontalTangent);

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
        _retailImpacts?.Dispose();
        _explosions?.Dispose();
        _missileSmoke?.Dispose();
        _cameraStatusLights?.Dispose();
        _grenadeRenderer?.Dispose();
        _missileRenderer?.Dispose();
        _renderer?.Dispose();
        _arms?.Dispose();
        _cardLighting?.Dispose();
        _cardLookup?.Dispose();
        _sdfClipmaps?.Dispose();
        _detailMaps?.Dispose();

        // Before the device: the bed's taps are streams the device owns, and stopping them while it is
        // still alive is what gives the voices back rather than leaving them to the device's teardown.
        _rainAmbience?.Dispose();
        _audio.Dispose();
    }
}
