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
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Config;
using OpenIGI.Game.Igi1.Menus;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Rendering.Presets;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

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

    private Random _shotRandom = null!;

    /// <summary>The engine's own generator, for the sound-variant draws <c>0x4638A0</c> makes.</summary>
    private readonly IgiRandom _soundRandom = new();

    /// <summary>The tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    private int _playerHitQuietUntil;
    private int _impactSoundQuietUntil;
    private int _ricochetSoundQuietUntil;
    private int _tracerSkipsLogged;

    private readonly Dictionary<AlarmSpeaker, AudioVoiceHandle> _sirenVoices = [];
    private readonly HashSet<AlarmSpeaker> _reportedSirenFailures = [];
    private IPlacementSpin? _spins;
    private readonly Dictionary<SecurityCamera, (string Sound, AudioVoiceHandle Voice)> _cameraBeepVoices = [];
    private readonly Dictionary<SecurityCamera, AudioVoiceHandle> _cameraMoveVoices = [];

    // What the level says about its own light, for a title that authors that rather than a sky. Null for
    // Project I.G.I., which keeps the sweep. Set by ApplyLevelConstants, so the menu path gets it too.
    private DeferredLighting? _deferredLighting;

    // The level's sun as the DAY/NIGHT CYCLE has to see it, which is not always as the level wrote it.
    // See BindToCycle.
    private DirectionalLightSettings? _cycleSun;

    // What a person has said outright about which models are lamps, which beats anything the detector
    // works out. One file for the whole game; see Assets/lamps.json.
    private LampManifest _lampManifest = LampManifest.Empty;

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

    // The played mission's registries and runtimes, each null until the system that makes it has run.
    private readonly MissionWorld _mission = new();

    // Which lamps hold shadow maps and the surface cache's coarse lamp maps, and drawing them.
    private readonly LampShadowMaps _lampMaps;

    // The console's handle on the content layers.
    private readonly ContentLayerCommands _contentLayers;

    // The console's inspector, over what IInspectedWorld shows it of the host.
    private readonly WorldInspector _inspector;

    // The player's flashlight, and what its lens is kept out of.
    private readonly PlayerFlashlight _flashlight;
    private Func<DeathCameraVantage.Blocked?>? _flashlightWalls;

    // Where a new level's lamps are ordered from for their coarse maps, nearest first.
    private Vector3d LampOrderEye => _camera?.Position ?? default;

    // Where everyone who casts in a lamp's map stands, in metres: the player and the living soldiers.
    private void LampMapPeople(List<Vector3d> people)
    {
        if (_player is { } player)
        {
            people.Add(WorldScene.ToMetres(player.Body.Position));
        }

        if (_soldiers is { } soldiers)
        {
            foreach (HumanOccupant occupant in soldiers.Simulation.LiveOccupants())
            {
                if (!occupant.IsDead)
                {
                    people.Add(WorldScene.ToMetres(occupant.Position));
                }
            }
        }
    }
    private readonly float[] _localLightRank = new float[DeferredLocalLight.Maximum];

    // Stand-down state for the surface-cache retrace. The world-space relight and the amortised bounce
    // re-derive the answer the cache already holds whenever nothing has moved -- and "moved" for a
    // VIEW-INDEPENDENT cache means the distance field scrolled new coverage in (a real walk of metres) or
    // the sun was scrubbed, NOT the camera turning or drifting a few centimetres. Once the cache has settled
    // after the last such change the retrace idles, which is what takes out the periodic multi-millisecond
    // spike that halved the frame rate at rest. It resumes the instant a level scrolls or the sun moves.
    private int _giStaticFrames;

    /// <summary>How many frames the near relight is spread over while following a moving sun.</summary>
    private const int GiSunFollowSpread = 4;

    /// <summary>How many frames a card's estimate averages over while following a moving sun.</summary>
    /// <remarks>
    /// Short enough to keep up with a scrub, long enough that a card's single noisy bounce does not show:
    /// at one, every relight replaced the last outright and the walls clouded over while the key was held.
    /// </remarks>
    private const int GiSunFollowTemporalFrames = 4;
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

    // How far the projection's scale may change between two frames and still count as the same shot: a tenth.
    // A cinematic zoom moves it a ten-thousandth a frame and is reprojected through; a scope snaps it by far
    // more than this in one, and its history is the unzoomed view's.
    private const float GiCutZoom = 0.1f;

    // The exposure adaptation rate during the warm-up and in steady play. Fast while the GI converges so the
    // exposure follows it and the picture holds its brightness; slow after, for an unobtrusive eye-adaptation.
    private const float GiExposureWarmupRate = 0.35f;
    private const float ExposureSteadyRate = 0.04f;

    // The last frame of the outgoing backdrop, held on the GPU so it can be dissolved off over the
    // incoming one. Sized to the window and re-made when that changes.
    private TextureHandle _dissolveTexture;
    private (int Width, int Height) _dissolveSize;

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

    // Makes the backdrop playable a couple of milliseconds at a time while the menu is idle.
    private MissionArming? _arming;

    // The shot that carries the player out of the menu and into their own eyes.
    private readonly MissionEntryCamera _entry = new();

    // Which row -backdropcycle is on, so each is asked for once rather than on every frame past its mark.
    private long _backdropCycleRow = -1;

    // Which step -presetcycle is on, latched the same way.
    private long _presetCycleStep = -1;

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

    // The world the blood is traced against: placed objects AND the terrain, because a droplet lands
    // on the ground as readily as on a wall and the player's own movement caster carries neither the
    // terrain nor a bullet's idea of what is solid.
    private WorldRayCast? _effectWalls;

    // What the open ground is made of, for the trace above. A terrain leaf carries no material row of
    // its own, so a hit on it reports Air -- which marks nothing at all. This is the row the footsteps
    // and the player's own material lookup already use.
    private int? _effectTerrainRow;

    private ICameraStatusLightPresenter? _cameraStatusLights;
    private IGrenadePresenter? _grenadeRenderer;
    private IMissilePresenter? _missileRenderer;
    private readonly MissileSimulation _vehicleMissiles = new();
    private LoadedMission? _loadedMissionForWeapon;
    private IViewModelPresenter? _arms;
    private ISoldierPresenter? _soldiers;

    // Whether the soldiers and the player update inside the level walk (AttachActorsToLevelWalk), whether the
    // player's update has run this tick, and whether it advanced the interaction registry itself.
    private bool _soldiersInWalk;
    private bool _playerInWalk;
    private bool _playerTicked;
    private bool _interactionsTickedByPlayer;
    private Func<SoldierEnemy>? _soldierEnemy;

    // The vehicles' engine sets, gun reports and turret traverse (VehicleSoundPlayer).
    private VehicleSoundPlayer? _vehicleSounds;
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

    // --- Built once and re-registered into every title's namespace ---
    // Locals until the shell could change games. All three outlive a title: the config host and the menu
    // host are the seams the menus act through, and the mission state is a native the level scripts call.
    // A switch builds a new GameLoop, and each of them has to be registered into it -- which is why they
    // are here rather than in Run's own scope.
    private HostConfigHost _configHost = null!;
    private HostFlowHost _flowHost = null!;
    private GameMissionState _missionState = null!;

    // The game the shell has asked for, waiting for a frame boundary. Deferred for the same reason a
    // mission load is: the switch tears down the loop, the registry and every presenter, and doing that
    // from inside a frame would pull them out from under the pass that is drawing.
    private GameTitle? _pendingTitle;

    // --- The menus and the cinematic shell ---
    // The retail menu system's shell, while the -vanilla menus are up.
    private IRetailMenuPresenter? _retailMenus;
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
        _audioOut = new AudioOutput(audioFactory, fileSystem, logger, MenuSoundArchive);
        _cutsceneSounds = new CutsceneSounds(_audioOut);
        _effects = new WorldEffects(device, logger, _soundRandom, new WorldEffectsHost(this));
        _switch = new RendererSwitchService(device, logger);
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
        if (_logger is LogRouter router)
        {
            console.AttachLog(router);
        }

        console.QuitRequested += (_, _) => state.Running = false;

        // The console reaches the system clipboard through the window, which is what owns the handle,
        // rather than taking a platform reference of its own.
        console.View.ReadClipboard = () => _window.ClipboardText;
        console.View.WriteClipboard = text => _window.ClipboardText = text;

        // Attached once rather than per mission: the inspector asks for a fresh snapshot every frame
        // it draws, and the implementation already answers with nothing when no world is loaded.
        console.View.AttachWorld(_inspector);

        // Before Start, so a +lamp_switch on the command line and a settings.cfg that binds one both find
        // the command already there.
        RegisterLampCommands(console.System);
        RegisterTimeOfDayCommands(console.System);
        _flashlight.RegisterCommands(console.System);
        RegisterTitlePlayerCommands(console.System);
        _effects.Register(console.System);
        RegisterCameraCommands(console.System);
        RegisterRendererSwitchCommand(console.System);
        _contentLayers.Register(console.System);
        _memory.Register(console.System);

        console.Start(
            _fileSystem,
            _options.StartupCommands,
            useSavedSettings: !_options.Lockstep,
            _options.FirstRunPreset,
            _options.LockedPreset);
        // The scan LoadTitleResources ran: taken off the field because the scan now happens wherever the
        // resources are loaded, and that is a method a title switch calls too.
        _contentLayers.Report(_overrideScan);

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

        _flowHost = new HostFlowHost(_logger);

        // The flow state machine owns the frame, and the menu is a task beneath it — so the frame walk
        // updates and renders the menu rather than this loop doing either by hand.
        _scheduler = new(_device, _pacing);

        // -lockstep replaces the wall clock with a synthetic one advanced a fixed amount per loop pass,
        // so how long a frame took to draw stops deciding how many ticks it covered. Without it a capture
        // is not reproducible -- two identical command lines land on different tick counts and so on
        // different soldier and animation states, which measured at 9% of pixels differing. Everything
        // else in the simulation is already deterministic (IgiRandom is seeded, not time-seeded), so this
        // is the whole of what a reference capture needs.
        state.LockstepClock = _options.Lockstep ? new ManualTimeSource() : null;

        // Held as a field, and built here rather than left to GameLoop's own default, because a second
        // loop is created for every mission the menu previews and one of those eventually takes the
        // session over. Two loops that each defaulted to their own SystemTimeSource would read
        // milliseconds from different origins, and every `last*Milliseconds` delta in the frame loop
        // would jump the moment the swap happened. One source, shared, keeps the handover invisible.
        _timeSource = state.LockstepClock is not null ? state.LockstepClock : new SystemTimeSource();
        // Baked global illumination: a mission's probe field is loaded from the shipped cache (Assets/gi)
        // when present, else traced and written to the per-user cache so the next load is fast. This is what
        // turns a level load from a few-seconds lighting trace into an instant read -- see GiBakeCache.
        GiBakeCache probeCache = GiBakeCache.Create(
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
        _gi = new GlobalIllumination(_device, _logger, _work, _options.Lockstep, new GlobalIlluminationHost(this));

        // The path-traced object lightmaps, cached the same way: baked once, read instantly after. The
        // shipped bakes live beside the probe fields; freshly-baked ones write to the per-user cache.
        LightmapBakeCache lightmapCache = LightmapBakeCache.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "gi"),
            Path.Combine(SettingsPaths.GetUserDirectory(), "cache", "gi"),
            _logger);
        _baked = new BakedLighting(_device, _logger, _work, _options.Lockstep, probeCache, lightmapCache);

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
        GameMusicNatives musicNatives = new(_audioOut.Music!);

        state.ReportedWorld = false;
        state.ReportedShot = false;

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
        _flashlight.Reset();

        // The red wash the screen takes on a hit -- four corner alphas that decay a forty-fifth a tick
        // (sub_411300) and paint a screen-filling red quad (HumanPlayer_DrawDamageFlash). It survives the
        // player's death, so the corpse's-eye view stays red rather than snapping back to a clean picture.
        _damageFlash = new();
        _flashbangScreenFlash = new();

        // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye
        // to a third-person shot of the body (HumanCamera death mode). The body it looks at is the player's
        // own third-person rig, which SoldierRenderer loads and poses with the death fall.
        _deathCamera = new();
        state.InteractionCamera = new();
        state.ClimbActive = false;
        state.WorldInteractionActive = false;
        state.TemporalFrame = 0;

        // Test-only authored-cutscene capture harness. Set OPENIGI_CUTSCENE_CAPTURE_DIR to enable;
        // OPENIGI_CUTSCENE_CAPTURE_SCENE selects one scene id (unset/0 captures whichever scene is
        // active), OPENIGI_CUTSCENE_CAPTURE_START_TICK is the first scene tick written and
        // OPENIGI_CUTSCENE_CAPTURE_COUNT the still budget (default 5). One still is written every
        // 90 logic ticks (3 s) while the scene runs, named mission-NN-scene-ID-tick-NNNNNN.bmp;
        // OPENIGI_CUTSCENE_CAPTURE_EVERY changes the interval, and 0 writes every rendered frame (the
        // name then carries the frame too), for flicker that changes between frames of one tick.
        state.CutsceneCaptureDirectory = Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_DIR");
        state.CutsceneCaptureScene = 0;
        state.CaptureCutscene = state.CutsceneCaptureDirectory is not null;
        _ = int.TryParse(Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_SCENE"), out int captureScene);
        state.CutsceneCaptureScene = captureScene;
        state.CutsceneCaptureTick = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_START_TICK"),
            out int configuredCaptureTick)
            ? Math.Max(0, configuredCaptureTick)
            : 90;
        state.CutsceneCaptureEvery = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_EVERY"),
            out int configuredCaptureEvery)
            ? Math.Max(0, configuredCaptureEvery)
            : 90;
        state.CutsceneCaptureCount = 0;
        state.CutsceneCaptureStartTick = state.CutsceneCaptureTick;
        state.PreviousCapturedScene = null;
        state.CutsceneCaptureLimit = int.TryParse(
            Environment.GetEnvironmentVariable("OPENIGI_CUTSCENE_CAPTURE_COUNT"),
            out int configuredCaptureCount) && configuredCaptureCount > 0
            ? configuredCaptureCount
            : 5;

        // A scripted free-camera flight (-camend), if one was asked for. The opening pose is captured on
        // the first rendered frame and the camera is moved along a straight line to the end pose across the
        // frame budget, so the temporal filters (GI, TAA) can be seen resolving under real motion -- which a
        // static capture cannot show. state.FlightFrame is the rendered-frame index, and the sequence's frame number.
        state.FlightStarted = false;
        state.FlightStartPos = default;
        state.FlightStartYaw = 0f;
        state.FlightStartPitch = 0f;
        state.FlightFrame = 0;

        _baked.Forget();

        // The level's signed distance fields, and the clipmaps that resample them around the camera. Baked
        // on a worker for the same reason the probe field is; the clipmaps themselves are GL and are built
        // on the render thread once the bake lands.
        _gi.Forget();
        state.CardLightingDumped = false;

        // How many times the bounce has been re-lit this mission. Zero converges in one shot; each relight
        // after that accumulates over frames. Reset when a mission loads.

        // One screen-space indirect dump per run, to verify the gather reads the G-buffer and reconstructs
        // world positions correctly before it feeds the frame.
        state.ScreenGiDumped = false;

        // What the screen-space GI keeps from one frame to the next.
        state.ScreenGi = new();

        state.ClimbSlideArmed = false;
        _shotRandom = new(20000101);
        state.ReportedHumanHits = 0;
        _scene = null;
        _world = null;
        _terrain = null;
        _sky = null;
        _shadowPass = null;

        // Authored atmosphere. Levels with no entry fall back to what their own FlatSky task asks for.
        state.FogPresets = FogPresets.Load(
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
        // A mission started from the menu never hit it, because the loop is already state.Running by then.
        // Measured: 27 maps derived over 600 frames of -mission, against a level's worth of textures.
        _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
        _detailMaps.Enabled = _detailMaps.DeriveMissing;

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
            ApplyColourLookup(_activeMission?.Id ?? _menuBackdrop.MissionId ?? _options.StartMission));

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
        _effects.Forget();
        _cameraStatusLights = null;
        _grenadeRenderer = null;
        _missileRenderer = null;
        _loadedMissionForWeapon = null;
        _arms = null;
        _soldiers = null;
        _soldiersInWalk = false;
        _playerInWalk = false;
        _playerTerrain = null;
        _effectWalls = null;
        _effectTerrainRow = null;
        _ground = null;

        // The material table, shared with the guard-round handler so an AI round's impact can leave the
        // same mark a player's does -- retail rounds are physical and decal whatever they meet (10.4).
        _worldMaterials = null;
        _ammoTypes = null;
        _weaponTypes = null;
        _mission.AlarmSpeakers = null;
        _mission.Destructibles = null;
        _sirenVoices.Clear();
        _playerMode = false;

        // The debug overlay (F3): the player's state and a crosshair "what am I looking at" readout. The
        // collision index is kept here so the overlay can name the object under the crosshair.
        _worldSolids = null;
        _worldTrace = null;
        state.ShowDebug = false;

        _flowHost.LoadMainMenu = () =>
        {
            MenuSystem menu = MenuSystem.Load(
                _fileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
                _loop,
                _logger);

            // The mission briefing images are in their own archive beside the menu's. Leaving with StartGame starts
            // the chosen mission; leaving any other way quits.
            _retailMenus?.Dispose();
            _retailMenus = _presentation.Presenters.Create<RetailMenuPresentationRequest, IRetailMenuPresenter>(
                new RetailMenuPresentationRequest(
                    _device,
                    _fileSystem,
                    menu.Manager.ResourcePath,
                    strings,
                    ["LOCAL:menusystem/missionsprites.res"],
                    _window.Input,
                    state.Batch,
                    () => _window.Width,
                    () => _window.Height,
                    // The menu and its hit-test have to agree about the magnification. See UiScale for why the
                    // interface is scaled at all.
                    _options.UiScale,
                    _audioOut.Sounds,
                    leaving =>
                    {
                        if (leaving == MenuManagerState.StartGame)
                        {
                            StartMission(_missionState.PendingMissionId);
                        }
                        else
                        {
                            state.Running = false;
                        }
                    },
                    _logger));
            if (_retailMenus is null)
            {
                _logger.Warning("Menu", "This title draws no retail menus; the menu script is not started.");
                return;
            }

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
            menu.Manager.Initialize(menu, _retailMenus, _logger);
        };

        // R11: the cinematic state.Shell (OpenIGI.Frontend) is the default front end; -vanilla and -vanillamenu
        // reach the byte-for-byte retail menus instead. -mission still skips the menus straight into a level.
        state.Shell = null;
        _showcaseCamera = null;
        state.MissionWarmer = null;
        state.PreviewLoader = null;
        _menuCameras = MenuCameraLibrary.Empty;
        state.ShellTypography = null;

        // Hoisted so a title switch can rebuild the state.Shell around them. Neither belongs to a game: the
        // host seam calls back into this class, and the save store is the player's, one directory for
        // the session. What the rebuilt state.Shell does get anew is its CONTEXT -- the game list and the
        // mission list, which are the two things that change when the game does.
        state.ShellHost = null;
        state.Saves = null;

        // What the mission-entry shot types over its vantage. Filled when the shot begins.

        // Where the shot's character stands, in engine units, and which way it faces.
        state.MenuMode = false;

        // The state.Shell is up over a LIVE mission -- the pause menu -- rather than over the showcase. It always
        // implies state.MenuMode; what it adds is that the backdrop is already the right picture. So nothing is
        // streamed in behind the menu, the showcase orbit does not take the camera off the player, and the
        // world is frozen instead of advanced. Escape opens it and Escape closes it again.
        state.PauseMenu = false;
        state.ShowcasePending = false;
        state.FirstFrameReported = false;

        // -entrycam: Play has not been pressed yet. Set only when the flag asked for it.
        state.EntryCamPending = false;
        state.EntryTimeChosenAt = -1;
        state.ShellHostForEntryCam = null;

        // When the mission-entry shot last read the clock.
        state.LastEntryMilliseconds = 0;
        state.LastComputerMilliseconds = 0;
        state.CurrentShowcaseMissionId = ShowcaseMissionId;
        int? pendingPreviewMissionId = null;
        state.PreviewRequestedAtMs = 0;

        // The backdrop cross-dissolve. 1 is the outgoing level's held frame fully covering the incoming
        // one, 0 is the incoming level alone; it runs on an eased tween on real elapsed time. There is no
        // dip through black any more: the two pictures blend directly into each other.
        var dissolve = new Tween(0f, duration: 0.42f, Ease.InOutQuad);

        // Set on the frame the backdrop is to be held, cleared once the hold has happened; the swap then
        // runs on the frame after, so what was captured is the OLD level. The very first backdrop has no
        // outgoing level to hold — the menu is over its dark clear — so there is nothing to dissolve from
        // and it simply appears.
        state.HoldFrameForDissolve = false;
        state.HeldFrameReady = false;
        state.MissionsSwept = false;
        state.BackdropSwaps = 0;
        state.HeldFrameIsPicture = false;
        state.DissolveRevealPending = false;

        // A field that finished baking for the backdrop, waiting for the frame after the hold to be
        // adopted -- see the collect further down.
        state.BakedFieldForBackdrop = null;

        // The menu is held to the display's rate rather than rendering as fast as it can, where the
        // platform leaves the rate to the loop. See IFramePacer.
        state.MenuPacer = _platform.CreateFramePacer();
        state.LastPassRendered = false;

        // -vanillamenu reaches the retail menus, which carry their own in-game menu; everything else runs
        // the cinematic state.Shell. -mission still opens straight into a level — what it no longer does is leave
        // the state.Shell unbuilt, because the pause menu IS the state.Shell and Escape has to reach it from a mission
        // the menus were skipped into.
        if (_options.BakeAll)
        {
            // The bake sweep runs after the window is shown (the probe wait pumps the message queue); the
            // state.Shell and menus are skipped entirely.
        }
        else if (_options.VanillaMenu && _options.StartMission <= 0)
        {
            _loop.Flow.Request(FlowEvent.MainMenu);
        }
        else
        {
            _menuCameras = MenuCameraLibrary.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);
            state.MissionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);
            state.PreviewLoader = new MissionPreviewLoader(
                _gamePath, _missions, _magic, _baked.ProbeCache, _menuCameras, _previewContext, _logger, _work);

            // The design's four faces — Barlow, Barlow Condensed in two weights, JetBrains Mono — the same
            // set the launcher draws with, uploaded once and shared by every page of the state.Shell.
            state.ShellTypography = UiFontLibrary.Typography(_device);
            UiTheme shellTheme = state.ShellTypography.CreateTheme();

            // Held so the mission-entry shot can typeset its title with the same faces the state.Shell uses.
            _shellBodyFont = state.ShellTypography.Body;
            _shellHeadingFont = state.ShellTypography.Display;
            _shellType = state.ShellTypography;

            // The real DefineMission data, resolved to display strings through the language table, in the
            // order the CAMPAIGN CHAIN gives — each descriptor names its successor. The registry's own
            // order is the startup scan's, which is a directory walk and therefore alphabetical, so it
            // listed level 10 between level 1 and level 2. The retail menu walks the chain (0x405660) and
            // so does this.
            state.Saves = new SaveSlotStore(Path.Combine(SettingsPaths.GetUserDirectory(), "state.Saves"));

            state.ShellHost = new ShellHost(
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
                    // bar either way — so the state.Shell is told it is no longer over a mission it was paused on.
                    // Deferred where a sequence covers the load: the menu keeps drawing under the departure
                    // and is put away on the frame the load actually runs.
                    void LeaveTheMenu()
                    {
                        state.Shell?.ClosePauseMenu();
                        state.MenuMode = false;
                        state.PauseMenu = false;
                        ApplyGameplayMotionBlur();
                    }

                    // The menu has been drawing this very level for as long as the player has been
                    // reading the briefing, built on a worker and complete. When it is the mission they
                    // picked, the level is PROMOTED rather than loaded a second time — see
                    // PromoteBackdrop — behind the same going-in sequence a restart gets: the picture
                    // falls out of focus under the card, the promotion runs, and the shot opens over the
                    // start. The backdrop is gone over a played mission, so this cannot fire from the pause
                    // menu. -lockstep is excluded: the sequence runs on wall-clock time, and a reference
                    // capture has to land on the same frame every run.
                    if (_menuBackdrop.Preview is { } backdrop && backdrop.MissionId == id && !_options.Lockstep)
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
                    if (state.MenuMode && !state.PauseMenu)
                    {
                        _dayNight.ResetForLevel(_sun, RenderCVars.TimeOfDay.Value);
                    }
                },
                resumeMission: () =>
                {
                    // Nothing is reloaded and nobody is placed again: the level was never torn down, only
                    // left un-ticked, so resuming is this loop putting the menu away.
                    state.Shell?.ClosePauseMenu();
                    state.MenuMode = false;
                    state.PauseMenu = false;
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
                            state.Shell?.ClosePauseMenu();
                            state.MenuMode = false;
                            state.PauseMenu = false;
                            ApplyGameplayMotionBlur();
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
                    state.Shell?.ClosePauseMenu();
                    state.Shell?.OpenOn("campaign");
                    state.MenuMode = true;

                    // The state.Shell is over the showcase again from here, so the backdrop has to be streamed:
                    // the level it was drawing over has just been released, and it is framed as a
                    // backdrop again rather than as something being played through.
                    state.PauseMenu = false;
                    state.ShowcasePending = true;
                    _window.CursorVisible = true;
                },
                quit: () => state.Running = false,
                switchTitle: RequestTitle,
                previewMission: id =>
                {
                    // Never over a live mission. Adopting a preview disposes the world renderer and drops
                    // the player, which from the pause menu would be the state.Shell demolishing the mission it
                    // is drawn over. The screens already know not to ask (ShellContext.InGame); this is the
                    // guarantee rather than the manners.
                    if (state.PauseMenu)
                    {
                        return;
                    }

                    int wanted = id ?? ShowcaseMissionId;

                    // -entrycam holds its own mission: the campaign opens on its first row and previews
                    // it, which would replace the backdrop the flag is waiting to press Play over.
                    if (_options.EntryCamMission > 0)
                    {
                        wanted = _options.EntryCamMission;
                    }

                    if (wanted != state.CurrentShowcaseMissionId)
                    {
                        // Warm the level's files on a worker now, and note the moment: once the selection
                        // has held for the settle window the loop asks the preview loader to build it, also
                        // on a worker. Nothing here touches the render thread, so a selection that keeps
                        // moving simply keeps re-arming this.
                        pendingPreviewMissionId = wanted;
                        state.PreviewRequestedAtMs = _loop.TimeSource.Milliseconds;
                        state.MissionWarmer?.Warm(wanted);
                    }
                    else
                    {
                        pendingPreviewMissionId = null;
                    }
                },
                saveSettings: () => console.SaveSettings(),
                saveProfiles: () =>
                {
                    RestoreTitleInputBindings();
                    HostConfiguration.Save(_configuration, _logger);
                },
                captureThumbnail: (out captureWidth, out captureHeight) =>
                {
                    captureWidth = _window.Width;
                    captureHeight = _window.Height;
                    var pixels = new Bgra32[captureWidth * captureHeight];
                    _device.CaptureFrame(captureWidth, captureHeight, pixels);
                    return pixels;
                },
                settingUnavailable: SettingUnavailable,
                missionSaves: () => TitlePlayer?.SaveGames,
                inputBindingLabel: action => TitleInputBindings.Label(action, _gameplay?.InputBindings ?? []),
                resetInputBindings: profile =>
                {
                    TitleInputBindings.Reset(profile, _gameplay?.InputBindings ?? []);
                });

            state.ShellHostForEntryCam = state.ShellHost;

            state.Shell = new Shell(BuildShellContext(state.ShellHost, shellTheme, state.Saves));

            // -shelltab is a development shortcut, not a game feature, and the state.Shell's counterpart to
            // -screen: it opens straight on a section so a capture does not need someone to click there.
            if (_options.ShellTab is { Length: > 0 } openOn && !state.Shell.OpenOn(openOn))
            {
                _logger.Warning("Menu", $"-shelltab '{openOn}' is not a section; opening on the main menu.");
            }

            if (_options.StartMission > 0)
            {
                // Straight into the level, with the state.Shell built and waiting behind Escape rather than shown.
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
                state.MenuMode = true;
                int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
                state.CurrentShowcaseMissionId = firstShowcase;

                if (state.PreviewLoader is { } opening)
                {
                    RequestBackdrop(opening, firstShowcase);
                }
                else
                {
                    state.ShowcasePending = true;
                }

                // -entrycam: open on the campaign and remember that Play is to be pressed as soon as this
                // mission's backdrop is up. The backdrop itself is requested through the ordinary showcase
                // path below, so what gets measured is the real thing rather than a special case.
                if (_options.EntryCamMission > 0)
                {
                    state.Shell.OpenOn("campaign");
                    state.EntryCamPending = true;
                }
            }
        }

        // Watch every pass from here on, so a hitch is attributed to the frame it happened on rather than
        // only reported as a run-wide peak. The stage profiler's peaks say a stage was once expensive
        // without saying when, and "when" is the whole question: a cost paid behind the menu is free and
        // the same cost paid on the frame Play is pressed is the bug.
        BeginWatchingFramePasses("Session");
        state.PassStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        state.FramePerformance = new();

        _logger.Information(
            "Render",
            FormattableString.Invariant(
                $"Camera effects: r_motion_blur {RenderCVars.MotionBlur.Value:0.###}, r_hdr {RenderCVars.OffscreenScene.Value}, r_deferred {RenderCVars.Deferred.Value}, r_taa {RenderCVars.TemporalFeedback.Value:0.###}, r_smooth {RenderCVars.SmoothPresentation.Value}, r_scale {RenderCVars.RenderScale.Value:0.###}."));

        _window.CloseRequested += (_, _) => state.Running = false;
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
            state.Running = false;
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
            state.Running = false;
        }

        // The distance-field shader is compiled here, when the feature is on, rather than when a level
        // first needs it -- a shader that does not compile would otherwise surface a long way into a run
        // as a stage that silently did nothing. Gated so a device with the feature off pays nothing.
        if (RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute)
        {
            GlobalDistanceField.ValidateShader(_device, _logger);
        }

        // Reused every frame rather than reallocated; the fit writes into it in place.
        state.CascadeStorage = new ShadowCascade[ShadowSettings.MaxCascades];

        state.LastCameraMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastConsoleMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastShellMilliseconds = _loop.TimeSource.Milliseconds;
        state.LastMenuCameraMilliseconds = _loop.TimeSource.Milliseconds;
        _baked.StartChaseClock(_loop.TimeSource.Milliseconds);

        // A capture run is a background process as far as the person at the keyboard is concerned: it must
        // not read the mouse and must never grab the cursor out from under whatever they are actually doing.
        state.CaptureMode = _options.Lockstep;

        // What the last pass of the loop did, and how many ticks in a row have run without a step
        // returning to its host. See the top of the loop.
        state.LastOutcome = FrameOutcome.Idle;
        state.TicksSinceStep = 0;

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
            // pass is drawing them. The state.Shell is rebuilt around the new title rather than told about it:
            // its game list, its campaign and which entry is current are all the title's.
            if (SwitchTitleIfDue())
            {
                // The outgoing title's sweep stops with it, and the new title's missions are swept once its
                // first backdrop is up.
                state.MissionWarmer?.Dispose();
                state.MissionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);
                state.MissionsSwept = false;

                // Closed rather than dropped: it owns a worker that may be part way through building the
                // outgoing game's backdrop, and the replacement below would otherwise race it.
                state.PreviewLoader?.Dispose();
                state.PreviewLoader = new MissionPreviewLoader(
                    _gamePath, _missions, _magic, _baked.ProbeCache, _menuCameras, _previewContext, _logger, _work);

                if (state.ShellHost is not null && state.ShellTypography is not null && state.Saves is not null)
                {
                    state.Shell = new Shell(BuildShellContext(state.ShellHost, state.ShellTypography.CreateTheme(), state.Saves));
                    state.Shell.OpenOn("campaign");
                }

                // Back to the front end with no level up: the outgoing game's was released and the
                // incoming one has not been asked for.
                state.MenuMode = true;
                state.PauseMenu = false;
                state.ShowcasePending = true;
            }

            // The menu has nothing to gain past the display's rate, and a GPU kept at full load turns every
            // small upload between frames into a hitch. Only after a pass that presented, or the pacing
            // would throttle the simulation's ticks as well; never under a capture, whose clock is its own.
            // A level held behind the loading screen while its field bakes is paced harder still: the
            // withheld frames exist to advance the temporal passes, which thirty a second does as well as
            // a hundred, and every core the render thread is not spinning on is one the bake gets.
            if (state.LastPassRendered && !_options.Lockstep && !_options.IsBenchmark)
            {
                if (IsWarmingUp && _gi.FieldBake is not null)
                {
                    state.MenuPacer.Pace(30);
                }
                else if (state.MenuMode && !RenderCVars.VSync.Value)
                {
                    state.MenuPacer.Pace(_window.RefreshRate > 0 ? _window.RefreshRate : 60);
                }
                else
                {
                    state.MenuPacer.Reset();
                }
            }
            else
            {
                state.MenuPacer.Reset();
            }

            state.LastPassRendered = false;
            state.PassStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            // NVIDIA Reflex, where its runtime runs: its mode, and the frame it paces. The frame begins only
            // once the last one has been presented, so the passes that only tick neither sleep nor count, and
            // it begins BEFORE the input below is read -- sleeping first is how the frame's input comes fresher.
            // Frame generation needs Reflex on and turns it on whatever the setting says.
            _device.SetLowLatency(EffectiveLowLatency(), RenderCVars.FrameLimit.Value);

            // Frame generation interpolates along the motion of play; a menu, a pause, the warm-up hold or a
            // cutscene has none to follow, so it stands down for those and resumes with play. Asked here, at
            // the top of the pass, because loading or unloading it rebuilds the swapchain between frames.
            _device.SetFrameGeneration(
                RenderCVars.FrameGeneration.Value,
                gameplay: !state.MenuMode
                    && !state.PauseMenu
                    && _activeMission is not null
                    && !PresentationHoldsLevel
                    && _mission.Cutscenes?.IsPlaying != true);
            _device.BeginLatencyFrame();

            _window.PumpEvents();

            // Whatever the detail-map workers finished since the last pass, uploaded a few at a time.
            // The setting is read here rather than captured at construction so that turning
            // r_material_detail up mid-session starts deriving for whatever loads next -- it is zero
            // under vanilla and enhanced, and deriving maps the shader multiplies by nothing is waste.
            _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
            _detailMaps.Enabled = _detailMaps.DeriveMissing;
            _detailMaps.PumpDerived();

            // The baked lighting's share of the frame: what its workers finished, and the probe field eased
            // towards the newest pass. Collected here because what comes back becomes GPU textures.
            _baked.Step(_world, _loop.TimeSource.Milliseconds);

            // Whatever the real-time lighting model needs and does not have yet. Every frame rather than
            // at load, because the settings menu can ask for that model at any moment and the structures it
            // reads have to be able to arrive afterwards -- see GameHost.RealtimeGi.cs. A no-op once
            // they exist, which is every frame but the handful after the switch.
            _gi.EnsureStructures();

            // Behind the loading screen the traced world takes every mesh offered in the frame it is
            // offered, so the level is whole in it before anything is lit against it; in play the per-frame
            // budget comes back. Said every frame, so no path out of the load can leave it lifted.
            _device.SetTraceAdmission(IsWarmingUp);

            // This frame's share of a convergence in flight, so a level lights up behind the loading
            // screen or under the entry shot's hold rather than in one stall.
            _gi.StepConvergence();

            // Same handover, same reason: the atlas becomes a GL volume, and the context belongs here.
            // Behind the menu the adoption is deferred to the frame after the picture has been held, so
            // the backdrop crossing over to its real-time lighting happens under the dissolve rather than
            // in front of the player -- the same shape as a backdrop swap.
            if (_gi.TryCollectFieldBake(out DistanceFieldBake.Result baked))
            {
                if (state.MenuMode && !state.PauseMenu && _menuBackdrop.Preview is not null)
                {
                    state.BakedFieldForBackdrop = baked;
                }
                else
                {
                    _gi.AdoptDistanceField(baked.Scene, baked.Atlas, fromCache: false);
                }
            }

            // One tick's worth of synthetic time per RENDERED FRAME, from an absolute target rather than
            // an accumulated step so the schedule cannot drift. Per loop pass would be wrong: the loop
            // pumps many times per frame, so the clock would jump a whole tick each pump and the catch-up
            // cap would fire every pass -- measured at 200 ticks for 20 frames, a world state.Running ten times
            // too fast. Deterministic either way, but a capture should show the moment it claims to.
            if (state.LockstepClock is not null)
            {
                state.LockstepClock.Milliseconds = _options.IsBenchmark
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
            console.Update(_window.Input, Math.Clamp((consoleNow - state.LastConsoleMilliseconds) / 1000f, 0f, 0.25f));
            state.LastConsoleMilliseconds = consoleNow;

            // Console key bindings fire with the console shut. The bind table, the `bind` command and
            // their tests have existed since the console was written, but nothing ever asked the table
            // whether a key had been pressed -- so a binding could be declared and saved and would
            // never once fire. Skipped during a capture run, whose input must stay exactly scripted.
            //
            // In menu mode the state.Shell runs first (below) so a listening rebind row sees the press
            // queue before anything consumes it (issue #230). The state.Shell then owns the frame — it sets
            // Captured right below — so game binds must not execute against the sim beneath a menu:
            // what the state.Shell leaves queued is discarded, never fired. Menu navigation is unaffected
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

            if (!(state.MenuMode && state.Shell is not null))
            {
                DrainPressQueue(executeBinds: true);
            }

            // The cinematic state.Shell reads the frame's input after the console and, like it, marks the frame
            // captured so the free camera and the game beneath see an idle keyboard and pad. When the console
            // is open it owns the frame and the state.Shell only advances its animation.
            if (state.MenuMode && state.Shell is not null)
            {
                int shellNow = _loop.TimeSource.Milliseconds;
                float shellElapsed = Math.Clamp((shellNow - state.LastShellMilliseconds) / 1000f, 0f, 0.1f);
                state.LastShellMilliseconds = shellNow;

                var shellFrame = new LayoutContext(_window.Width, _window.Height);
                state.Shell.Update(shellFrame, _window.Input, shellElapsed);
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
                bool changeWanted = !state.PauseMenu
                    && !_loadSequence.IsActive
                    && (state.PreviewLoader is { HasResult: true }
                        || _showcaseCamera is { IsCutDue: true }
                        || state.BakedFieldForBackdrop is not null);
                if (changeWanted && !state.HoldFrameForDissolve && !state.HeldFrameReady)
                {
                    state.HoldFrameForDissolve = true;
                }

                // The held picture stays up until the incoming level has converged its lighting, so what
                // the dissolve reveals is the settled backdrop rather than one still lighting up. A field
                // that is still baking is not waited for -- that is seconds, and it crosses over under its
                // own hold when it lands.
                if (state.DissolveRevealPending && !_gi.IsConverging)
                {
                    state.DissolveRevealPending = false;
                    dissolve.Retarget(0f);
                }

                dissolve.Update(shellElapsed);
            }

            // The mission-entry shot. It runs on real elapsed time, like the menu's orbit and the
            // interface's own animation, because it is presentation: it must look the same whatever rate
            // the display is state.Running at, and it must not be tied to the simulation's fixed tick.
            if (_entry.IsRunning)
            {
                int entryNow = _loop.TimeSource.Milliseconds;
                float entryElapsed = Math.Clamp((entryNow - state.LastEntryMilliseconds) / 1000f, 0f, 0.1f);
                state.LastEntryMilliseconds = entryNow;

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
                    if (_menuBackdrop.Preview is { } entering && PromoteBackdrop(entering))
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

                // Nothing takes input for the length of the shot -- not the state.Shell, not the player, not
                // the free camera. The state.Shell still ANIMATES, so its fade plays out; it just hears nothing.
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
                        bodies.Simulation.HidePlayerBody();
                    }
                }

                // The shot's own smear, from its own movement; handed back to the player's setting after.
                ApplyEntryMotionBlur();

                if (_entry.Phase == MissionEntryPhase.Done)
                {
                    _entry.Reset();
                    _soldiers?.Simulation.HidePlayerBody();
                    ApplyGameplayMotionBlur();
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
            if (_options.MissionCycleFrames > 0 && state.Shell is not null)
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
                        state.Shell.ClosePauseMenu();
                        state.Shell.OpenOn("campaign");
                        state.MenuMode = true;
                        state.PauseMenu = false;
                        state.ShowcasePending = true;
                    }
                    else if (_missionCycleMission > 0)
                    {
                        _logger.Information("Mission", $"-missioncycle: playing {_missionCycleMission} again.");
                        state.Shell.ClosePauseMenu();
                        state.MenuMode = false;
                        state.PauseMenu = false;
                        ApplyGameplayMotionBlur();
                        StartMission(_missionCycleMission);
                    }
                }
            }

            // Escape brings the state.Shell up over the level as a pause menu. It goes AFTER the state.Shell's own
            // update, so the frame that opens the menu is not also the frame the menu reads a back press
            // on -- and after it the state.Shell has marked the frame captured, which is what stops the Escape
            // that closed the menu from opening it again on the same pass.
            //
            // It also goes BEFORE the handheld computer below, which owns Escape while it is open: closing
            // the computer clears the flag, so a check placed after it would see a closed computer and a
            // still-pressed Escape and pause the game in the same breath.
            if (!state.MenuMode
                && !console.IsOpen
                && _mission.Cutscenes?.IsPlaying == true
                && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
            {
                _mission.Cutscenes.RequestBreak();
                _cutsceneSounds.StopAll();
                _mission.Objectives?.ClearCutsceneMessages();
            }

            if (!state.MenuMode
                && !console.IsOpen
                && !_computerOpen
                && !_missionFailedMenu
                && _mission.Cutscenes?.IsPlaying != true
                && TitlePlayer is not { IsCutscenePlaying: true } and not { IsMenuEnabled: false }
                && state.Shell is not null
                && _activeMission is not null
                && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
            {
                state.MenuMode = true;
                state.PauseMenu = true;
                state.Shell.OpenPauseMenu();

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

            // The remaster's flashlight, before the map computer's key: it is read only while the player's
            // own view is on screen, which the computer opening this frame has not yet changed.
            UpdateFlashlightSwitch(state.MenuMode, console.IsOpen);

            // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
            // freezes player/camera input while the world continues its fixed-tick simulation.
            if (!state.MenuMode && !console.IsOpen && _player is not null
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
                    // the same wall-clock second whatever the frame rate, and it must keep state.Running while
                    // the simulation is paused behind it.
                    int computerNow = _loop.TimeSource.Milliseconds;
                    float computerElapsed = Math.Clamp((computerNow - state.LastComputerMilliseconds) / 1000f, 0f, 0.1f);
                    state.LastComputerMilliseconds = computerNow;

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

            // The showcase's first backdrop, asked for a couple of menu frames in so the state.Shell is already on
            // screen. It is built on a worker like every other one; until it arrives the menu sits over its
            // dark backdrop rather than over a blank blue clear.
            if (state.MenuMode && !state.PauseMenu && state.ShowcasePending && _loop.Clock.FrameCount >= 2)
            {
                state.ShowcasePending = false;

                // -entrycam's mission stands in for the default showcase, so the backdrop it presses Play
                // over is built through exactly the path a player's selection would have built it through.
                int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
                state.CurrentShowcaseMissionId = firstShowcase;
                RequestBackdrop(state.PreviewLoader, firstShowcase);
            }

            // The backdrop is made playable here, on the frames where nothing is happening but a menu.
            // A couple of milliseconds is invisible at the uncapped rate the menu runs at, and the work
            // is finished long before anybody has finished reading a briefing.
            if (state.MenuMode && !state.PauseMenu && _arming is { IsArmed: false })
            {
                _arming.Step(IdleArmingBudgetMilliseconds);

                // The level is resident: the decoded pictures nobody took are dropped, and the staging
                // the uploads went through is given back rather than held for the next level.
                if (_arming.IsArmed)
                {
                    _memory.ReleaseLoadLeftovers(_world, _detailMaps);
                }
            }

            // -entrycam presses Play the moment the backdrop it was pointed at is armed, which is the
            // state a player's press would find it in after a second of reading. Waiting for the arming
            // rather than only for the backdrop is what keeps the measurement honest.
            if (state.EntryCamPending && state.MenuMode && !state.PauseMenu && _menuBackdrop.Preview is { } entryBackdrop
                && entryBackdrop.MissionId == _options.EntryCamMission
                && _arming is { IsArmed: true })
            {
                // -entrytime: the time chosen first, as the dropdown chooses it, and Play a second later
                // over the backdrop that choice re-lit.
                if (_options.EntryTime != MissionTimeOfDay.Default && state.EntryTimeChosenAt < 0)
                {
                    state.EntryTimeChosenAt = _loop.Clock.FrameCount;
                    state.ShellHostForEntryCam?.SetMissionConditions(_conditions with { TimeOfDay = _options.EntryTime });
                }

                bool settled = state.EntryTimeChosenAt < 0
                    || _loop.Clock.FrameCount - state.EntryTimeChosenAt >= EntryTimeSettleFrames;

                if (settled)
                {
                    state.EntryCamPending = false;
                    _logger.Information("Menu", $"-entrycam is pressing Play on mission {entryBackdrop.MissionId}.");
                    state.ShellHostForEntryCam?.StartMission(
                        entryBackdrop.MissionId,
                        _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal);
                }
            }

            // -backdropcycle walks the campaign's rows on a fixed cadence, taking the same path a player
            // moving down the mission list takes: it asks the preview loader for the next mission and
            // lets the ordinary handover below adopt it. Latched on the frame count so each row is asked
            // for once, and only while the menu is actually up over a backdrop.
            if (_options.BackdropCycleFrames > 0 && state.MenuMode && !state.PauseMenu && state.PreviewLoader is not null)
            {
                long row = _loop.Clock.FrameCount / _options.BackdropCycleFrames;
                if (row != _backdropCycleRow)
                {
                    _backdropCycleRow = row;
                    int[] order = [.. _missions.InCampaignOrder().Select(m => m.Id)];
                    if (order.Length > 0)
                    {
                        int next = order[(int)(row % order.Length)];
                        if (next != state.CurrentShowcaseMissionId)
                        {
                            _logger.Information("Menu", $"-backdropcycle: selecting mission {next}.");
                            pendingPreviewMissionId = null;
                            state.CurrentShowcaseMissionId = next;
                            RequestBackdrop(state.PreviewLoader, next);
                        }
                    }
                }
            }

            // -presetcycle applies the next graphics preset on a fixed cadence, the way the settings page does.
            if (_options.PresetCycleFrames > 0)
            {
                long step = _loop.Clock.FrameCount / _options.PresetCycleFrames;
                if (step != _presetCycleStep)
                {
                    _presetCycleStep = step;
                    GraphicsPreset[] presets = [GraphicsPreset.Ultra, GraphicsPreset.Vanilla, GraphicsPreset.High, GraphicsPreset.Enhanced];
                    GraphicsPreset preset = presets[(int)(step % presets.Length)];
                    if (GraphicsPresetRegistry.Default.IsAllowed(preset))
                    {
                        _logger.Information("Menu", $"-presetcycle: applying {preset}.");
                        GraphicsPresetRegistry.Default.Apply(preset);
                    }
                }
            }

            // The build request, once the campaign selection has held still for a moment. The debounce is
            // only there to stop a run down the list queueing fourteen builds; it costs the render thread
            // nothing either way, because the build happens on a worker.
            if (state.MenuMode
                && !state.PauseMenu
                && pendingPreviewMissionId is { } previewMissionId
                && _loop.TimeSource.Milliseconds - state.PreviewRequestedAtMs > PreviewSettleMs)
            {
                pendingPreviewMissionId = null;
                RequestBackdrop(state.PreviewLoader, previewMissionId);
            }

            // A backdrop whose mission's files were still arriving, asked for again until they are there.
            if (state.MenuMode && !state.PauseMenu && _backdropAwaitingFiles is { } awaitingFiles)
            {
                RequestBackdrop(state.PreviewLoader, awaitingFiles);
            }

            // The handover. Everything a backdrop is made of was built off the render thread; what is left
            // is the part that cannot be — a GL texture belongs to the context that made it — so the
            // renderers are constructed here, on the frame after the outgoing picture was held. Same shape
            // as the probe and distance-field bakes above.
            if (state.MenuMode && !state.PauseMenu && state.HeldFrameReady)
            {
                bool changed = false;
                if (state.PreviewLoader is { } loader
                    && loader.TryCollect(out MissionPreview? readyPreview) && readyPreview is not null)
                {
                    state.CurrentShowcaseMissionId = readyPreview.MissionId;
                    AdoptPreview(readyPreview);
                    changed = true;

                    // Under the held frame, where the pause is not seen: the outgoing level and the
                    // incoming one's decoding have just been through the heap.
                    _memory.TrimAfterLoad();

                    // A soak reports where memory stands every tenth swap, so growth shows as a trend.
                    if (_options.BackdropCycleFrames > 0 && ++state.BackdropSwaps % 10 == 0)
                    {
                        _memory.Report($"After {state.BackdropSwaps} backdrop swap(s)");
                    }

                    // Once the first backdrop is up, every other mission's files are read through in the
                    // background, so whichever the player settles on next builds from memory.
                    if (!state.MissionsSwept)
                    {
                        state.MissionsSwept = true;
                        state.MissionWarmer?.WarmAll(_missions.InCampaignOrder().Select(mission => mission.Id));
                    }
                }
                else if (_showcaseCamera is { IsCutDue: true } showcase)
                {
                    // No new level, just the next place in this one. The camera does not take the cut
                    // itself, so that it happens here — after the outgoing frame has been held.
                    showcase.CommitCut();
                    changed = true;
                }
                else if (state.BakedFieldForBackdrop is { } landedField)
                {
                    // The backdrop's field, baked behind the menu, adopted under the held frame so the
                    // level's cross-over to its real-time lighting is dissolved to rather than watched.
                    state.BakedFieldForBackdrop = null;
                    _gi.AdoptDistanceField(landedField.Scene, landedField.Atlas, fromCache: false);
                    changed = true;
                }

                state.HeldFrameReady = false;

                // The held frame is now the whole picture; it dissolves off to reveal what is behind it,
                // once the lighting behind it has converged.
                if (changed)
                {
                    dissolve.SnapTo(state.HeldFrameIsPicture ? 1f : 0f);
                    state.DissolveRevealPending = state.HeldFrameIsPicture;
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
                state.ShowDebug = !state.ShowDebug;
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
            // Any layer with files counts, switched on or not: with every layer off there is still
            // something the switch is the difference between once one comes back on.
            if (_overridesApplied != RenderCVars.OverridesEnabled.Value && _overrides.HasContent)
            {
                _overridesApplied = RenderCVars.OverridesEnabled.Value;
                _overrides.Bypassed = !_overridesApplied;

                int swapped = ApplyOverrideChange(out _);

                _logger.Information(
                    "Overrides",
                    _overridesApplied
                        ? $"Overrides on (F8); {swapped} resource(s) swapped in."
                        : $"Overrides off (F8); {swapped} resource(s) back to the game's own.");
            }

            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F11)
                && RenderCVars.GlobalIllumination.Value
                && _scene is not null
                && _baked.CanRebake)
            {
                _baked.Rebake(_scene, _loop.TimeSource.Milliseconds);
            }

            // F1 swaps between flying and walking. The player takes the camera's position when it is
            // switched on, so the swap happens where you were looking.
            if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && TitlePlayer is not null)
            {
                ToggleTitlePlayer();
            }
            else if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && _player is not null && _camera is not null)
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
            // state.MenuMode covers the front end and the pause menu alike, and neither is time the player is
            // spending in the level. The cycle banks the paused seconds nowhere, so coming back does not
            // release a jump in the sun.
            {
                int dayNightNow = _loop.TimeSource.Milliseconds;
                float dayNightElapsed = Math.Clamp((dayNightNow - _dayNightMilliseconds) / 1000f, 0f, 0.1f);
                _dayNightMilliseconds = dayNightNow;
                bool clockRuns = RenderCVars.DayNightCycle.Value && !state.MenuMode && _activeMission is not null;
                _dayNight.Update(
                    _window.Input.Keyboard,
                    dayNightElapsed,
                    clockRuns ? RenderCVars.DayLength.Value * 60f : 0f);
            }

            // The level's lamps, on every pass and on real elapsed time, for exactly the reasons the sweep
            // above is: a fitting takes a couple of hundred milliseconds to go out and about a second to
            // come back, and a fade advanced once per rendered frame would be a different fade on every
            // machine. Cheap enough to be unconditional -- a level nobody has touched walks an array of
            // steady lamps and returns. The daylight's photocells ride on the same pass, reading the sun
            // the sweep above has just moved.
            UpdateLamps(_loop.TimeSource.Milliseconds, audible: !state.MenuMode && _activeMission is not null);

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
                float elapsed = Math.Clamp((nowMilliseconds - state.LastCameraMilliseconds) / 1000f, 0f, 0.1f);
                state.LastCameraMilliseconds = nowMilliseconds;

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
                state.InteractionCamera.LookSettings = look;

                if (console.WantsPointer)
                {
                    // The developer console is a pointer-driven tool: its panels are dragged and
                    // resized. This branch comes first and is repeated every pass for the same reason
                    // the pause menu's is -- the lock below would otherwise be re-asserted a frame
                    // later and the cursor would snap back to the centre under the reader's hand.
                    // HoldStill matters just as much here: taking the cursor back on close warps the
                    // pointer, and without re-arming the settling frame that warp is read as aim.
                    // The system cursor stays hidden: the console draws its own, as the state.Shell does,
                    // so the pointer looks the same everywhere and is drawn over the panels rather
                    // than by the compositor on top of them.
                    _player?.HoldStill();
                    _window.CursorLocked = false;
                }
                else if (MenuOwnsPointer(state.PauseMenu, _missionFailedMenu))
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
                    _window.CursorLocked = !state.CaptureMode;

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
                    state.InteractionCamera.Control(rawX, rawY);
                    _window.CursorLocked = !state.CaptureMode;
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
                else if (_mission.Cutscenes?.IsPlaying == true)
                {
                    _player?.HoldStill();
                    _window.CursorLocked = false;
                }
                else if (_playerMode && TitlePlayer is not null)
                {
                    // A title's own player takes its look on the tick, as the original does; the frame only
                    // gathers what the mouse and the keys did since the last one.
                    GatherTitleInput();
                    _window.CursorLocked = !state.CaptureMode;
                }
                else if (_playerMode && _player is not null)
                {
                    // Looking is per frame so the view is as smooth as the display allows; the body itself
                    // moves on the fixed tick below, so it steps identically whatever the frame rate.
                    _player.Look(_window.Input, elapsed);
                    _window.CursorLocked = !state.CaptureMode && _player.IsCapturingMouse;
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
                    _window.CursorLocked = !state.CaptureMode && _camera.IsCapturingMouse;
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
            // The cinematic state.Shell always renders uncapped so the menu is smooth whatever the render preset's
            // r_smooth is; the orbit and the tweens run on real time and want every frame the display offers.
            // In game the original's one-render-per-tick latch is lifted only by smooth mode.
            _loop.Clock.UncappedRenders = _options.IsBenchmark
                || state.MenuMode
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
                pausableLevel.IsPaused = state.PauseMenu || PresentationHoldsLevel;
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
            if (outcome == FrameOutcome.Tick && (_missionFailedMenu || state.PauseMenu || PresentationHoldsLevel))
            {
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

            if (_options.IsBenchmark && _loop.Clock.FrameCount == BenchmarkSchedule.WarmUpFrames)
            {
                state.FramePerformance.Reset();
                _benchmarkAllocStart = GC.GetTotalAllocatedBytes();
                _benchmarkGcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            }
            if (!_options.IsBenchmark || _loop.Clock.FrameCount <= BenchmarkSchedule.MeasurementEndFrame)
            {
                state.FramePerformance.NoteFrame();
            }

            if (_scene is not null && _world is not null && _camera is not null)
            {
                RenderWorld(state, console);
            }

            // The cinematic state.Shell draws over the resolved showcase scene, in 2D on the back buffer, before
            // the console (which goes over even the state.Shell).
            // The state.Shell keeps being DRAWN through the mission-entry shot even though the menu is over, so
            // that it can fade rather than vanish. It hears nothing while it does.
            if ((state.MenuMode || _entry.IsRunning) && state.Shell is not null)
            {
                // Exactly one layer may draw a pointer. The console is drawn after this and over it,
                // so while it is up it owns the cursor; otherwise the reader sees two arrows.
                state.Shell.Stack.PointerSuppressed = console.IsOpen;

                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                var shellPainter = UiPainter.Create(state.Batch, new LayoutContext(_window.Width, _window.Height));
                state.Batch.Begin();

                // The going-in sequence's picture: the level exactly as it is drawn, before any of the
                // interface goes over it. Taken here for the same reason the dissolve's hold is.
                CaptureLoadPictureIfWanted();

                // None of the backdrop machinery applies to the pause menu: its backdrop is the mission,
                // already drawn on this very frame, and there is no swap for it to dissolve between.
                if (!state.PauseMenu)
                {
                    // Before the showcase level has loaded there is no scene to draw, so fill a dark backdrop
                    // rather than leaving the frame's blue clear behind the menu.
                    if (_scene is null)
                    {
                        shellPainter.FillRect(new UiRect(0f, 0f, _window.Width, _window.Height), Bgra32.FromRgb(12, 14, 18));

                        // Nothing on screen to dissolve from, so the hold is a no-op and the first backdrop is
                        // simply revealed. Without this the swap would wait forever on a hold that never came.
                        if (state.HoldFrameForDissolve)
                        {
                            state.HoldFrameForDissolve = false;
                            state.HeldFrameReady = true;
                            state.HeldFrameIsPicture = false;
                        }
                    }
                    else
                    {
                        // The outgoing level, caught here: after the 3D pass, before any of the menu is drawn
                        // over it, so what is held is the picture alone.
                        if (state.HoldFrameForDissolve)
                        {
                            HoldFrameForDissolve();
                            state.HoldFrameForDissolve = false;
                            state.HeldFrameReady = true;
                            state.HeldFrameIsPicture = true;
                        }

                        // The dissolve, over the resolved scene and UNDER the state.Shell: the two levels blend into
                        // each other while the menu stays lit and responsive on top of both.
                        DrawDissolve(shellPainter, dissolve.Value);
                    }
                }

                // The whole interface behind one opacity. It has to be pushed HERE rather than left to
                // each screen's own transition, because a screen deliberately draws its header and footer
                // outside that fade and the stack draws the pointer outside every screen — so only a push
                // around the whole draw takes all of it, chrome and cursor included.
                float shellOpacity = MathF.Min(
                    _entry.IsRunning ? _entry.ShellOpacity : 1f,
                    _loadSequence.ShellOpacity);
                if (shellOpacity > 0.001f)
                {
                    // The frame so far — the level, the dissolve over it — blurred into the levels the
                    // state.Shell's backdrop and every glass panel sample. The blur closes the 2D pass, so the
                    // state.Batch is flushed around it and the state.Shell draws on a painter that knows the levels.
                    state.Batch.End();
                    shellPainter = UiPainter.Create(state.Batch, shellPainter.Context, _menuBackdrop.Glass(_device.BlurFrameForBackdrop(), SafeWindowWidth, SafeWindowHeight));
                    state.Batch.Begin();

                    shellPainter.PushOpacity(shellOpacity);
                    state.Shell.DrawBackdrop(shellPainter);
                    state.Shell.Draw(shellPainter);
                    shellPainter.PopOpacity();
                }

                // The shot's own framing goes over the interface at full strength: the bars and the title
                // belong to the cinematic, not to the menu, and they must not fade out with it.
                if (_entry.IsRunning)
                {
                    DrawEntryFraming(shellPainter);
                }

                state.Batch.End();
                _device.EndStageTiming();
            }

            // The level, withheld while it settles. Over the HUD and the menus because it stands in for
            // the whole picture, and under the console because the console has to be usable while a level
            // is warming -- that is exactly when somebody is looking into why it is.
            DrawMissionWarmUp(state.Batch);

            // The mission's caption, standing in the level under the shot: over the defocus, so it is the
            // one sharp thing while the lighting settles, and under the console.
            DrawWorldTitle(state.Batch);

            // The console goes over everything, including the menus, so it is the last thing drawn.
            // WantsDraw rather than IsOpen: the watch and performance panels can be pinned to stay up
            // with the console shut. It is false during a capture run whatever the reader pinned, so a
            // pinned overlay can never leak into a reference screenshot and break the vanilla gate.
            if (console.WantsDraw)
            {
                _device.BeginStageTiming(RenderStage.UserInterface);
                _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
                state.Batch.Begin();
                console.Draw(state.Batch, _window.Width, _window.Height, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
                state.Batch.End();
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
                // Not while a flight still waits out its -flightdelay: those frames are the opening still, the
                // same picture hundreds of times over, eight megabytes each at 1080p -- a capture that held the
                // view for six hundred frames before moving filled the disk with them.
                if (!_options.HasFlight || state.FlightFrame >= _options.FlightDelay)
                {
                    Bgra32[] frame = new Bgra32[_window.Width * _window.Height];
                    _device.CaptureFrame(_window.Width, _window.Height, frame);

                    string directory = Path.GetDirectoryName(flightPath) ?? string.Empty;
                    string stem = Path.GetFileNameWithoutExtension(flightPath);
                    string extension = Path.GetExtension(flightPath);
                    string numbered = Path.Combine(directory, FormattableString.Invariant($"{stem}.{state.FlightFrame:0000}{extension}"));
                    BitmapWriter.Write(numbered, _window.Width, _window.Height, frame);
                }

                state.FlightFrame++;
            }

            // Authored-scene capture: write numbered stills of the requested scene every 90 logic ticks
            // (3 s) up to the still budget. Test-only; driven by environment variables.
            // A chained intro resets its scene-local clock. Do not carry the previous scene's next
            // capture threshold into the next scene and silently miss its early/short shots.
            if (state.CaptureCutscene && _mission.Cutscenes?.ActiveScene is { } observedScene
                && !ReferenceEquals(state.PreviousCapturedScene, observedScene))
            {
                state.PreviousCapturedScene = observedScene;
                state.CutsceneCaptureTick = state.CutsceneCaptureStartTick;
            }
            if (state.CaptureCutscene
                && state.CutsceneCaptureCount < state.CutsceneCaptureLimit
                && _mission.Cutscenes?.ActiveScene is { } activeCutscene
                && (state.CutsceneCaptureScene <= 0 || activeCutscene.Id == state.CutsceneCaptureScene)
                && activeCutscene.Ticks >= state.CutsceneCaptureTick)
            {
                Directory.CreateDirectory(state.CutsceneCaptureDirectory!);
                Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
                _device.CaptureFrame(_window.Width, _window.Height, capture);
                int missionId = _activeMission?.Id ?? 0;
                string path = Path.Combine(
                    state.CutsceneCaptureDirectory!,
                    state.CutsceneCaptureEvery > 0
                        ? FormattableString.Invariant($"mission-{missionId:00}-scene-{activeCutscene.Id}-tick-{activeCutscene.Ticks:000000}.bmp")
                        : FormattableString.Invariant($"mission-{missionId:00}-scene-{activeCutscene.Id}-tick-{activeCutscene.Ticks:000000}-frame-{_loop.Clock.FrameCount:000000}.bmp"));
                BitmapWriter.Write(path, _window.Width, _window.Height, capture);
                _logger.Information("CutsceneCapture", $"Wrote {path}.");
                state.CutsceneCaptureTick += state.CutsceneCaptureEvery;
                state.CutsceneCaptureCount++;
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
                        $"Wrote {state.FlightFrame} flight frame(s) at {_window.Width}x{_window.Height} beside {_options.ScreenshotPath}.");
                }

                if (_options.LightingDumpPath is { } lightingDumpPath)
                {
                    WriteLightingDump(lightingDumpPath);
                }

                _soldiers?.Simulation.LogAiSummary();
                state.Running = false;
            }

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

        if (_options.BenchmarkPath is { } benchmarkPath)
        {
            IReadOnlyList<RenderStageTiming> gpuStages = _device.StageTimings();
            FramePerformanceStatistics frame = state.FramePerformance.Statistics();
            GpuFrameTiming gpu = _device.GpuTiming();

            if (_benchmarkAllocStart > 0)
            {
                long allocated = GC.GetTotalAllocatedBytes() - _benchmarkAllocStart;
                int measured = Math.Max(1, frame.SampleCount);

                // Warning, not Information: the state.Shell builds its logger at Warning, so an Information call
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

                foreach (RenderPassTiming pass in _device.PassTimings())
                {
                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant(
                            $"  pass {pass.Label,-32} avg {pass.Milliseconds,6:0.000} ms  peak {pass.PeakMilliseconds,7:0.000} ms  ran {pass.FramesRun} frame(s)  [{pass.Stage}]"));
                }

                // The 1% low, taken apart: what each pass cost in the slowest frames, beside its overall average.
                IReadOnlyList<RenderPassTiming> worst = _device.WorstFramePassTimings(0.01, out int worstFrames, out double worstAverage);
                if (worstFrames > 0)
                {
                    Dictionary<string, double> overall = [];
                    foreach (RenderPassTiming pass in _device.PassTimings())
                    {
                        overall[pass.Label] = pass.Milliseconds;
                    }

                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant($"Slowest 1% ({worstFrames} frame(s)): summed GPU avg {worstAverage:0.000} ms."));

                    foreach (RenderPassTiming pass in worst)
                    {
                        double usual = overall.GetValueOrDefault(pass.Label);
                        _logger.Information(
                            "Perf",
                            FormattableString.Invariant(
                                $"  worst {pass.Label,-32} {pass.Milliseconds,7:0.000} ms  (usually {usual,6:0.000}, {pass.Milliseconds - usual,+7:0.000})  ran in {pass.FramesRun}/{worstFrames}"));
                    }
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
        _baked.Cancel();

        if (!_options.IsBenchmark)
        {
            HostConfiguration.Save(_configuration, _logger);
            console.SaveSettings();
        }
        state.MissionWarmer?.Dispose();
        state.PreviewLoader?.Dispose();
        if (_dissolveTexture.IsValid)
        {
            _device.DestroyTexture(_dissolveTexture);
        }

        // The state.Shell's faces are the device's shared set; the device's teardown takes their textures.
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

        for (int i = 0; i < _gi.LightFixtures.Count; i++)
        {
            LightFixture fixture = _gi.LightFixtures[i];

            // A lamp that has finished going out is not a dim light, it is not a light: passed over here
            // rather than submitted at zero, because the shader's array is a budget and a dark fixture
            // holding a slot switches a real one at the end of the corridor off instead.
            if (_gi.LampSwitchboard is { } board && board.LevelOf(i) <= 0f)
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
        int layers = _overrides.Layers.Count(layer => layer.Enabled && layer.Wins > 0);
        string upscaled = _overrides.Count == 0
            ? "original (no content)"
            : RenderCVars.OverridesEnabled.Value
                ? $"{_resources.OverriddenCount} override(s) from {layers} layer(s) [F8 for the originals]"
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
        if (_gi.DistanceField is not { } field || !field.Clipmaps.IsValid)
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
        _menuBackdrop.Dispose();
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
