using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Runs the authored <c>CutScene</c>, <c>EditCamera</c>, timer, container and animation timelines.</summary>
/// <remarks>
/// Timing and interpolation reproduce the retail handlers: a 30 Hz integer clock, depth-first camera
/// flattening, duration scaling, non-uniform cubic Hermite interpolation, per-shot smooth flags, and
/// transition-latched link/target tracking. The host only supplies presentation and the few game natives
/// referenced by level expressions.
/// </remarks>
public sealed class CutsceneRuntime
{
    /// <summary>The fixed update frequency used by retail cutscenes and animation programmes.</summary>
    public const int Frequency = 30;

    /// <summary>The narrow vertical band in which an airframe has touched supporting geometry.</summary>
    /// <remarks>
    /// Contact is resolved after the native-style integration step. This small band catches the exact
    /// rest case as well as the following tick's shallow penetration without affecting a genuinely
    /// airborne path.
    /// </remarks>
    private const double GroundContactToleranceUnits = 64.0;

    private static readonly AsyncLocal<string?> AirframePathLogLocal = new();

    /// <summary>[AIRFRAME-PATH] Per-test hook (AsyncLocal) so parallel suites do not share one file.</summary>
    public static string? AirframePathLog
    {
        get => AirframePathLogLocal.Value;
        set => AirframePathLogLocal.Value = value;
    }

    private readonly GameTask _root;
    private readonly ScriptVarRegistry _variables;
    private readonly LevelExpressionRuntime _expressions;
    private readonly List<ConditionalContainerTask> _containers = [];
    private readonly List<SceneState> _scenes = [];
    private readonly List<TimerState> _timers = [];
    private readonly List<AnimationState> _animations = [];
    private readonly List<SoundState> _sounds = [];
    private readonly List<CarAiState> _carAis = [];
    private readonly List<DeclaredTask> _binocularOverlayHolders = [];
    private readonly Dictionary<int, TransformState> _transforms = [];
    private readonly Dictionary<GameTask, TransformState> _taskTransforms = [];
    private readonly Dictionary<GameTask, int> _taskOrder = [];
    private readonly Dictionary<GameTask, AttachmentState> _attachments = [];
    private readonly Dictionary<GameTask, CarWheel[]> _carWheels = [];
    private readonly Dictionary<WorldPlacement, int> _carWheelIndices = [];
    private readonly Dictionary<WorldPlacement, (GameTask Vehicle, int Index)> _gunPlacements = [];
    private readonly Dictionary<GameTask, float> _trainTailOffsets = [];
    private readonly Func<string, ScriptArguments, QvmValue?>? _hostNative;
    private readonly Action<CutsceneSoundEvent>? _soundChanged;
    private readonly Func<string, float>? _modelLength;
    private readonly Func<string, float>? _modelPivot;
    private readonly VehiclePhysicsRegistry? _vehiclePhysics;
    private IRetailCarWorld? _retailCarWorld;
    private Func<string, OpenIGI.Formats.World.MefModel?>? _retailCarModels;
    private readonly List<RetailRayModel> _retailVehicleModels = [];
    private readonly LevelUpdateWalker _walker = new();
    private readonly Dictionary<GameTask, int> _lastVehicleTick = [];
    private Func<string, MagicObjectDefinition?>? _retailMagic;
    private readonly Dictionary<GameTask, RetailRayModel?> _standingCars = [];
    private readonly Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? _vehicleObstacleHit;
    private readonly Func<GameTask, bool>? _vehicleObstacleReleased;
    private readonly TerrainData? _terrain;
    private readonly IIgiLogger _logger;
    private ObjectCollisionIndex? _collisionIndex;
    private DestructibleObjectRegistry? _destructibles;
    private Func<int, CutsceneTargetPose?>? _targetPoseResolver;
    private readonly Dictionary<GameTask, VehicleArmament> _armaments = [];
    private readonly Dictionary<GameTask, VehicleVision> _visions = new(ReferenceEqualityComparer.Instance);
    private int _visionCounter;
    private readonly Dictionary<GameTask, DeathZoneState> _deathZones = [];
    private readonly Random _gunRandom = new(0x46F390);

    /// <summary>CarAI's arrival radius squared (<c>dbl_533648</c>: three metres).</summary>
    private const double CarAiArrivalSquared = 150_994_944d;

    /// <summary>Within ten metres of a node the route speed looks one node further (<c>0x4255A0</c>).</summary>
    private const double CarAiLookAheadSquared = 1_677_721_600d;

    /// <summary>The chase looks ahead within fourteen metres (<c>0x425900</c>).</summary>
    private const double CarAiChaseLookAheadSquared = 3_288_334_336d;
    private bool _breakRequested;
    private readonly List<string> _diagnostics = [];

    /// <summary>Gets or sets the solid-object index used to find a supporting surface for vehicles.</summary>
    /// <remarks>
    /// <para>
    /// Wired by the host when a level has placed pads, crates, garage floors or rooftops that a
    /// vehicle stands on. The terrain half mirrors retail's <c>0x4CBE40</c>; the object sweep
    /// mirrors retail's placed-model sweep (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>). Main thread only.
    /// </para>
    /// <para>
    /// Every mission wants this: a car's suspension probes it for the surface under each wheel, and
    /// a great deal of what a vehicle stands on is placed geometry rather than terrain -- the
    /// Mission 1 outro truck starts on a garage floor a few centimetres above the dirt, and without
    /// the objects its wheels settle through the floor onto the terrain. Setting it no longer rests
    /// the helicopters as a side effect; call <see cref="RestHelicoptersOnSupport"/> for that.
    /// </para>
    /// </remarks>
    public ObjectCollisionIndex? CollisionIndex
    {
        get => _collisionIndex;
        set => _collisionIndex = value;
    }

    /// <summary>Gets or sets the destruction state whose vehicle origins follow live CarAI transforms.</summary>
    public DestructibleObjectRegistry? Destructibles
    {
        get => _destructibles;
        set => _destructibles = value;
    }

    /// <summary>Supplies the living player eye used by firing vehicles.</summary>
    public Func<Vector3d?>? VehicleCombatTarget { get; set; }

    /// <summary>
    /// Gets or sets where a vehicle's eye tests the same target: the point the human reports to a detection sweep
    /// (message <c>0x489620</c>), which the soldiers' sweep uses too. Falls back to <see cref="VehicleCombatTarget"/>.
    /// </summary>
    public Func<Vector3d?>? VehicleSightTarget { get; set; }

    /// <summary>Tests the firing vehicle's muzzle-to-player sight segment.</summary>
    public Func<GameTask, Vector3d, Vector3d, bool>? VehicleCombatLineOfSight { get; set; }

    /// <summary>Receives a vehicle missile only after sight, retention and gun cadence allow it.</summary>
    public Action<VehicleCombatShot>? VehicleCombatShotFired { get; set; }

    /// <summary>Binds one authored firing vehicle to a single retail weapon row, fired from its captured muzzle.</summary>
    /// <remarks>
    /// The Level 6 APC capture's model: the vehicle fires the row's cadence at whatever it retains,
    /// from a fixed muzzle, without turning a gun. Vehicles whose model guns are known use
    /// <see cref="ConfigureVehicleGuns"/> instead.
    /// </remarks>
    public void ConfigureVehicleCombat(GameTask vehicle, WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(weapon);
        _armaments[vehicle] = new VehicleArmament(weapon, []);
    }

    /// <summary>Arms one authored vehicle with the guns its model carries.</summary>
    /// <param name="vehicle">The Car or Heli task.</param>
    /// <param name="guns">Every gun its model's attachments define, each turning and firing on its own.</param>
    /// <remarks>
    /// The vehicle sees; each gun asks it what it sees and turns to it (<c>0x46F390</c>). A tank's
    /// cannon and coaxial gun therefore fire independently at their own rows' cadences.
    /// </remarks>
    public void ConfigureVehicleGuns(GameTask vehicle, IReadOnlyList<VehicleGun> guns)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(guns);
        if (guns.Count == 0)
        {
            _armaments.Remove(vehicle);
            return;
        }

        _armaments[vehicle] = new VehicleArmament(
            null,
            [.. guns.Select(static gun => new VehicleGunState(gun))]);
    }

    /// <summary>Every live vehicle's sound inputs, as its update left them this tick.</summary>
    /// <returns>One entry per active, undestroyed Car or Heli.</returns>
    public IEnumerable<VehicleSoundSource> VehicleSoundSources()
    {
        foreach ((GameTask task, TransformState state) in _taskTransforms)
        {
            if (task is not DeclaredTask vehicle
                || vehicle.Type.Name is not ("Car" or "Heli")
                || !ConditionalContainerGate.IsTaskActive(vehicle)
                || _destructibles?.IsDestroyed(vehicle) == true)
            {
                continue;
            }

            bool helicopter = vehicle.Type.Name == "Heli";
            string set = helicopter
                ? "helicopter_1"
                : _vehiclePhysics?.TryGetCar(Text(vehicle, WorldScene.ModelParameter), out CarPhysicsDefinition car) == true
                    ? car.EngineSoundSet
                    : string.Empty;
            string gate = Text(vehicle, "Play sound");
            bool on = string.IsNullOrWhiteSpace(gate) || _expressions.EvaluateCondition(gate, emptyValue: true, vehicle);
            float throttle = helicopter ? state.RetailHeli?.Collective ?? 0f : state.RetailBody?.Throttle ?? 0f;
            float speed = helicopter ? 0f : Math.Min((state.RetailBody?.Speed ?? 0f) * 0.001f, 1f);
            yield return new VehicleSoundSource(
                vehicle,
                set,
                state.Position,
                throttle,
                throttle > 0.01f ? 1f : 0f,
                speed,
                on);
        }
    }

    /// <summary>Every armed live vehicle's guns, as this tick's updates left them.</summary>
    /// <returns>One entry per gun of an active, undestroyed vehicle.</returns>
    public IEnumerable<VehicleGunSound> VehicleGunSounds()
    {
        foreach ((GameTask vehicle, VehicleArmament armament) in _armaments)
        {
            if (!ConditionalContainerGate.IsTaskActive(vehicle)
                || _destructibles?.IsDestroyed(vehicle) == true
                || !_taskTransforms.TryGetValue(vehicle, out TransformState? transform))
            {
                continue;
            }

            for (int index = 0; index < armament.Guns.Count; index++)
            {
                VehicleGunState gun = armament.Guns[index];

                // Gun_Fire plays its reports at the weapon object (+32), where the seat put it.
                yield return new VehicleGunSound(
                    vehicle,
                    index,
                    armament.Seated
                        ? gun.WeaponPosition
                        : transform.Position + transform.Orientation.Transposed().Transform(gun.Gun.Offset),
                    gun.Hold,
                    gun.RoundsFiredThisTick,
                    gun.Turret.Turning,
                    gun.Gun.Weapon,
                    gun.Gun.Mount.TurnSound);
            }
        }
    }

    /// <summary>Gives one authored vehicle the death zones its model carries.</summary>
    /// <param name="vehicle">The Car or Heli task.</param>
    /// <param name="zones">The zones' volumes in the vehicle's frame.</param>
    public void ConfigureDeathZones(GameTask vehicle, IReadOnlyList<VehicleDeathZone> zones)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(zones);
        if (zones.Count == 0)
        {
            _deathZones.Remove(vehicle);
            return;
        }

        _deathZones[vehicle] = new DeathZoneState(zones);
    }

    /// <summary>Finds a moving vehicle whose death zone a human stands in (<c>0x45FA80</c>).</summary>
    /// <param name="humanOrigin">The human's model origin in world units -- about a metre above the feet.</param>
    /// <param name="vehicle">Receives the vehicle that runs the human over.</param>
    /// <returns>
    /// <see langword="true"/> when the human is inside a zone of a live vehicle that moved at least
    /// <see cref="VehicleDeathZone.LethalSpeedUnitsPerTick"/> on the last tick.
    /// </returns>
    public bool TryRunOver(Vector3d humanOrigin, out GameTask? vehicle)
    {
        foreach ((GameTask candidate, DeathZoneState state) in _deathZones)
        {
            if (state.SpeedUnitsPerTick < VehicleDeathZone.LethalSpeedUnitsPerTick
                || !ConditionalContainerGate.IsTaskActive(candidate)
                || _destructibles?.IsDestroyed(candidate) == true
                || !_taskTransforms.TryGetValue(candidate, out TransformState? transform))
            {
                continue;
            }

            Vector3d local = transform.Orientation.Transform(humanOrigin - transform.Position);
            foreach (VehicleDeathZone zone in state.Zones)
            {
                if (zone.Contains(local))
                {
                    vehicle = candidate;
                    return true;
                }
            }
        }

        vehicle = null;
        return false;
    }

    /// <summary>Gets the turret of one gun a vehicle was armed with, for presentation and tests.</summary>
    public VehicleGunTurret? GunTurret(GameTask vehicle, int index) =>
        _armaments.TryGetValue(vehicle, out VehicleArmament? armament) && index >= 0 && index < armament.Guns.Count
            ? armament.Guns[index].Turret
            : null;

    /// <summary>Settles every collision-enabled helicopter onto whatever is under it.</summary>
    /// <remarks>
    /// Separate from <see cref="CollisionIndex"/> because it is a one-shot placement pass, not a
    /// consequence of the index being available: the index is what every vehicle's ground query
    /// reads, and only a helicopter authored with collision wants to be re-seated on it.
    /// </remarks>
    public void RestHelicoptersOnSupport() => RestCollisionHelisOnSupport();

    /// <summary>Builds the live cutscene state for a loaded level.</summary>
    public CutsceneRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? native = null,
        Action<CutsceneSoundEvent>? soundChanged = null,
        Func<string, float>? modelLength = null,
        Func<string, float>? modelPivot = null,
        VehiclePhysicsRegistry? vehiclePhysics = null,
        Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? vehicleObstacleHit = null,
        Func<GameTask, bool>? vehicleObstacleReleased = null,
        TerrainData? terrain = null,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        _root = levelRoot;
        _variables = variables;
        _hostNative = native;
        _soundChanged = soundChanged;
        _modelLength = modelLength;
        _modelPivot = modelPivot;
        _vehiclePhysics = vehiclePhysics;
        _vehicleObstacleHit = vehicleObstacleHit;
        _vehicleObstacleReleased = vehicleObstacleReleased;
        _terrain = terrain;
        _logger = logger ?? NullLogger.Instance;
        _expressions = new LevelExpressionRuntime(variables, InvokeNative);
        List<CutSceneTask> cutscenes = [];

        // Every detection record takes the next value of the global round-robin counter as it initialises: the camera
        // lenses at construction, then the vehicles in tree order at post-load (VehicleVision).
        _visionCounter = Walk(levelRoot).Count(static task => string.Equals(task.Type.Name, "SCamera", StringComparison.Ordinal));
        int taskOrder = 0;
        foreach (GameTask task in Walk(levelRoot))
        {
            int order = taskOrder++;
            _taskOrder[task] = order;
            if (task.Type.Name is "Car" or "Heli")
            {
                _visions[task] = new VehicleVision(NextVisionPhase());
            }
            if (task is ConditionalContainerTask container)
            {
                _containers.Add(container);
            }

            if (task is CutSceneTask cutscene)
            {
                cutscene.IsRun = cutscene.InitialRun;
                cutscene.IsFinished = false;
                cutscene.Ticks = Math.Max(0, (int)(cutscene.StartTime * Frequency));
                cutscenes.Add(cutscene);
            }

            CaptureTransform(task);

            if (task is DeclaredTask declared)
            {
                if (string.Equals(task.Type.Name, "LevelTimer", StringComparison.Ordinal))
                {
                    TimerState timer = new(
                        declared,
                        Text(declared, "On"),
                        Text(declared, "Reset"),
                        Bool(declared, "Initial run"));
                    _timers.Add(timer);
                    variables.BindBool(declared, "isRun", () => timer.IsRun, value => timer.IsRun = value);
                    variables.BindInt(declared, "nTick", () => timer.Tick, value => timer.Tick = value);
                }
                else if (string.Equals(task.Type.Name, "AnimTask", StringComparison.Ordinal)
                    && declared.TryGetValue("AnimData", out object? data)
                    && data is IReadOnlyList<AnimDataEntry> entries)
                {
                    int target = Int(declared, "TargetID", Int(declared, "Target ID", -1));
                    AnimationState animation = new(
                        declared,
                        target,
                        order,
                        Text(declared, "Run"),
                        Bool(declared, "Initial run"),
                        new AnimDataProgram(entries));
                    _animations.Add(animation);

                    // Retail exports AnimTask's running tick counter so level expressions such as
                    // container conditions (e.g. mission 4's ConditionalContainer_300 waiting on
                    // AnimTask_850.nTick > 66 * GAME_FREQUENCY) can time off it. Without this export
                    // those conditions never resolve and the gated tasks stay dormant forever.
                    variables.BindInt(declared, "nTick", () => animation.Tick, value => animation.Tick = value);

                    // And its run byte (+131316, 0x4F3BA0), which its update rewrites from Run each tick. Level
                    // timers count on it -- mission 7's LevelTimer_2500 runs while AnimTask_300.isRun, and the
                    // escape truck's own Run stops it at that timer's 2650 and 4330 -- and a helicopter's Run
                    // latches itself with "|| this.isRun".
                    variables.BindBool(declared, "isRun", () => animation.IsRun, value => animation.IsRun = value);
                }
                else if (string.Equals(task.Type.Name, "ConditionalSound", StringComparison.Ordinal))
                {
                    _sounds.Add(new SoundState(
                        declared,
                        FirstTextNamed(declared, "condition"),
                        FirstTextNamed(declared, "sound"),
                        Vector(declared, "Position"),
                        Bool(declared, "Simple"),
                        Bool(declared, "OneShot"),
                        Bool(declared, "Relative to microphone")));
                }
                else if (string.Equals(task.Type.Name, "BinocularOverlayHolder", StringComparison.Ordinal))
                {
                    _binocularOverlayHolders.Add(declared);
                }
            }
        }

        foreach (TimerState timer in _timers)
        {
            _walker.Register(timer.Task, _ => TickTimer(timer));
        }

        foreach (AnimationState animation in _animations)
        {
            _walker.Register(animation.Task, _ => TickAnimation(animation));
        }

        foreach (SoundState sound in _sounds)
        {
            _walker.Register(sound.Task, _ => TickSound(sound));
            _walker.RegisterDeactivation(sound.Task, _ => SetSoundRun(sound, run: false));
        }

        foreach (GameTask task in Walk(levelRoot))
        {
            if (string.Equals(task.Type.Name, "Car", StringComparison.Ordinal))
            {
                _walker.Register(task, TickCar);
            }
            else if (string.Equals(task.Type.Name, "Heli", StringComparison.Ordinal))
            {
                _walker.Register(task, TickHeli);
            }
        }

        _walker.ContainerTransition = (container, started) =>
            _expressions.Execute(started ? container.RunAtStart : container.RunAtStop, container);
        _walker.HoldContainer = RetainsLevelFourOutro;

        // Capture the whole authored hierarchy before parking a vehicle. Moving a
        // parent first makes its still-authored child positions acquire the parking
        // displacement as a permanent local offset (Level 3's pilot floats above it).
        RestCollisionHelisOnSupport();

        // EditCamera caches every Link task's initial world transform during retail
        // UpdateInternalData.  Build those keys only after all task transforms exist: Train tasks do
        // not author a world Position at all, and their initial frame must first be evaluated from the
        // railroad.  Reading the keys inline above silently cached (0,0,0)/identity for linked cars.
        foreach (CutSceneTask cutscene in cutscenes)
        {
            SceneState scene = new(cutscene, ReadShots(cutscene));
            _scenes.Add(scene);
            _walker.Register(cutscene, _ => TickScene(scene));
            _walker.RegisterDeactivation(cutscene, _ => DeactivateScene(scene));
        }

        // Sibling humans acquire a moving parent from their live collision contact.
        // Proximity at construction can capture an inactive vehicle's parked pose,
        // baking its height difference into the actor before the scene registers.

        // The gate is prepared by the mission loader before this runtime is created. Refreshing here
        // replaces its former steady-game snapshot with the authored initial registration state.
        RefreshContainers(executeTransitions: true);
        foreach (SceneState state in _scenes)
        {
            if (!ConditionalContainerGate.IsTaskActive(state.Task))
            {
                continue;
            }

            bool run = string.IsNullOrWhiteSpace(state.Task.Run)
                ? state.Task.InitialRun
                : _expressions.EvaluateCondition(
                    state.Task.Run, emptyValue: state.Task.InitialRun, state.Task);
            state.Task.IsRun = run;
            if (run)
            {
                ExecuteSceneExpression(state.Task, state.Task.StartExpression);
            }
        }

        // Retail dispatches every registered AnimTask twice at tick zero before the first positive
        // 30 Hz update. The first packet initializes the receiver and lets vehicle handlers install
        // hold sentinels into the aliased channel block. The scheduler then spends one tick of the
        // programme's first wait and dispatches tick zero again. Mission 1's captured helicopter trace
        // is the useful proof: its initial Wait(10) produces the first cyclic edge at tick 9, not 10.
        foreach (AnimationState animation in _animations)
        {
            if (!ConditionalContainerGate.IsTaskActive(animation.Task))
            {
                continue;
            }
            animation.Program.SeekTo(0);
            ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
            animation.Program.Step();
            ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
        }
        UpdateFrame();
    }

    /// <summary>Gets the number of <see cref="Tick"/> calls applied since construction.</summary>
    public int SimulationTick { get; private set; }

    /// <summary>
    /// Ride / rest / attach notes for tests and playtest logs (task id + tick). Capped.
    /// </summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Gets whether an authored cutscene currently owns the camera.</summary>
    public bool IsPlaying => ActiveScene is not null;

    /// <summary>Gets the current authored camera and presentation settings.</summary>
    public CutsceneFrame? Frame { get; private set; }

    /// <summary>Supplies live poses for targets owned by another simulation, such as animated humans.</summary>
    public void SetTargetPoseResolver(Func<int, CutsceneTargetPose?>? resolver) => _targetPoseResolver = resolver;

    /// <summary>Runs every car on the original's rigid body, in the world its wheel rays are cast into.</summary>
    /// <param name="world">The world the cars' rays query.</param>
    /// <param name="models">Resolves a car's model by name, for its body points and radius.</param>
    /// <remarks>
    /// Each car gets a <see cref="RetailCarBody"/> at its controller's set-up packet -- the reset
    /// <c>0x42EA10</c> performs -- and from then on every physics packet steps it through <c>Car_Tick</c>
    /// (<c>0x42ECA0</c>) instead of the planar <see cref="CarDynamics"/> model. A car whose model or type is
    /// missing stays on the planar model.
    /// </remarks>
    /// <param name="magic">Resolves a magic object by name, for a helicopter's skids and rotors; optional.</param>
    public void ConfigureRetailCars(
        IRetailCarWorld world,
        Func<string, OpenIGI.Formats.World.MefModel?> models,
        Func<string, MagicObjectDefinition?>? magic = null)
    {
        _retailCarWorld = world ?? throw new ArgumentNullException(nameof(world));
        _retailCarModels = models ?? throw new ArgumentNullException(nameof(models));
        _retailMagic = magic;
    }

    /// <summary>Supplies the loaded graph corpus to authored vehicle patrol tasks.</summary>
    /// <remarks>
    /// CarAI is a normal level task, not a cutscene animation. Retail's callback at 0x425DE0 starts
    /// its PatrolPath, uses the graph route table to select each next node, and applies its vehicle-only
    /// speed command on the fixed game tick. The desktop loader owns graph-file I/O, so it passes the
    /// already offset world graphs here after they have parsed.
    /// </remarks>
    public void ConfigureCarAi(IReadOnlyDictionary<int, GraphFile> graphs)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        _carAis.Clear();
        AiPatrolPathRegistry paths = AiPatrolPathRegistry.Build(_root);
        foreach (GameTask task in Walk(_root))
        {
            if (task is not DeclaredTask declared
                || !string.Equals(task.Type.Name, "CarAI", StringComparison.Ordinal)
                || !declared.TryGetValue("Car ID", out object? rawCarId)
                || !declared.TryGetValue("Graph ID", out object? rawGraphId)
                || !declared.TryGetValue("Route ID", out object? rawRouteId)
                || rawCarId is not int carId
                || rawGraphId is not int graphId
                || rawRouteId is not int routeId
                || graphId < 0
                || routeId < 0
                || _root.Tree.FromId(carId) is not DeclaredTask car
                || !_taskTransforms.TryGetValue(car, out TransformState? transform)
                || !graphs.TryGetValue(graphId, out GraphFile? graph)
                || paths.Find(routeId) is not { } path)
            {
                continue;
            }

            CarAiState state = new(declared, car, transform, graph, AiPatrolRoute.Start(path));
            _carAis.Add(state);
            _walker.Register(declared, _ => TickCarAi(state));
        }
    }

    /// <summary>Gets the graph node a car's CarAI is heading for (<c>+104</c>), for diagnostics and tests.</summary>
    public int? CarAiTargetNode(GameTask car) =>
        _carAis.Find(state => ReferenceEquals(state.Car, car))?.TargetNode?.Id;

    /// <summary>Gets every vehicle in the world at its current origin, as CRITERIA_VEHICLE counts them.</summary>
    /// <remarks>
    /// <c>0x4F4560</c> walks the global vehicle list (<c>dword_A76C40</c>) and tests each vehicle's origin (<c>+32</c>):
    /// every Car and Heli, whatever drives it. Mission 7's escape truck is an AnimTask's, and the areas it drives
    /// through -- the timer that releases it past the checkpoint and the two gates that open for it -- only ever
    /// counted the CarAI cars.
    /// <para>
    /// A vehicle joins the list when it is created (<c>0x42E180</c>) and leaves it only when it is destroyed
    /// (<c>0x42E940</c>); deactivation (<c>0x4F44A0</c>) clears its active flag and leaves it listed. So a vehicle
    /// behind a closed ConditionalContainer still counts where it stands: mission 7's ending hides the truck under
    /// CutScene 1200, and the truck in AreaActivate 177 is what keeps that scene's container open.
    /// </para>
    /// </remarks>
    public IReadOnlyList<VehicleOccupant> VehicleOccupants()
    {
        List<VehicleOccupant> occupants = [];
        foreach ((GameTask task, TransformState state) in _taskTransforms)
        {
            if (task.Type.Name is "Car" or "Heli")
            {
                occupants.Add(new VehicleOccupant(task.Id, state.Position));
            }
        }

        return occupants;
    }

    /// <summary>Runs retail's registered CarAI callback (<c>0x425DE0</c>) once per game tick.</summary>
    /// <remarks>
    /// <para>
    /// CarAI does not move its car. It decides steering, throttle and brake and sends them to the car
    /// as a channel packet (<c>0x426510</c>: ch0 steering, ch1 throttle, ch3 brake), which the car's own
    /// receiver (<c>0x42EA10</c>) turns into physics -- the same path an AnimTask-driven car takes. The
    /// car therefore pulls away as its smoothed throttle rises: probed on Mission 7, the checkpoint APC
    /// crawls at 1-4 km/h for six seconds and reaches its cruise near 35 km/h after eleven.
    /// </para>
    /// <para>
    /// While the car's vision holds a target (<c>0x426000</c>: retained 600 ticks) CarAI leaves its
    /// route and drives at 20 km/h towards the graph node nearest the target, stopping within three
    /// metres of it and turning in place to face the target (<c>0x4262F0</c>).
    /// </para>
    /// </remarks>
    private void TickCarAi(CarAiState state)
    {
        Vector3d? target = VehicleCombatTarget?.Invoke();
        {
            if (_destructibles?.IsDestroyed(state.Car) == true)
            {
                state.Stopped = true;
                return;
            }

            if (!ConditionalContainerGate.IsTaskActive(state.Task) || state.Route is null)
            {
                return;
            }

            if (ChaseTarget(state, target) is { } chased)
            {
                GraphNode? goal = NearestNode(state.Graph, chased);
                if (goal is null || CarAiArrived(state, goal))
                {
                    state.CommandSpeedKmh = 0f;
                    state.Halt = 1f;
                }
                else
                {
                    state.TargetNode = CarAiNextHop(state, state.Transform.Position, goal.Id);
                    state.CommandSpeedKmh = 20f;
                    state.Halt = 0f;
                }

                SteerCarAiChase(state, chased);
            }
            else if (!state.Stopped && state.Route.Current is { } command)
            {
                switch (command.Kind)
                {
                    case AiPatrolCommandKind.WalkTo:
                    case AiPatrolCommandKind.RunTo:
                        if (state.Graph.NodeAt(command.Operand) is not { } destination || CarAiArrived(state, destination))
                        {
                            state.Route.Advance();
                        }
                        else
                        {
                            state.TargetNode = CarAiNextHop(state, state.Transform.Position, destination.Id);
                            state.CommandSpeedKmh = state.RouteSpeedKmh;
                            state.Halt = 0f;
                            SteerCarAiRoute(state);
                        }

                        break;

                    case AiPatrolCommandKind.Delay:
                    {
                        // 0x425DE0 counts the wait at +124 and moves on once it has passed the operand.
                        state.CommandSpeedKmh = 0f;
                        state.Halt = 1f;
                        int waited = state.WaitTicks++;
                        if (waited > command.Operand)
                        {
                            state.Route.Advance();
                            state.WaitTicks = 0;
                        }

                        break;
                    }

                    case AiPatrolCommandKind.Quit:
                    case AiPatrolCommandKind.End:
                        // Arms 6 and 7 stop the car and pull its brake; the cursor stays where it is.
                        state.CommandSpeedKmh = 0f;
                        state.Halt = 1f;
                        state.Brake = 1f;
                        state.DesiredSpeedKmh = 0f;
                        state.Steering = 0f;
                        break;

                    case AiPatrolCommandKind.SetSpeed:
                        state.RouteSpeedKmh = command.Operand;
                        state.Halt = 0f;
                        state.Route.Advance();
                        break;

                    default:
                        // Retail's default arm logs "Command not supported by CARAI" and advances.
                        state.Route.Advance();
                        break;
                }
            }
            else
            {
                state.Halt = 1f;
                state.Brake = 1f;
                state.Steering = 0f;
            }

            // 0x426510: the packet goes out every tick; its first word is the call counter, and a
            // zero there is the receiver's set-up packet rather than a physics step.
            if (state.Packets++ == 0)
            {
                state.Transform.Thrust = state.Transform.OriginalThrust;
                state.Transform.TargetThrust = state.Transform.OriginalThrust;
                state.Transform.CarMotion.Reset();
                ResetRetailBody(state.Car, state.Transform);
                return;
            }

            DriveCar(
                state.Car,
                state.Transform,
                simulationOrder: 0,
                state.Steering,
                state.Throttle,
                state.Brake == 0f ? -1f : state.Brake);

            // The car missiles, bullets and the player collide with is the same placed body: keep it
            // where the car is, or shots pass through the visible vehicle and never reach its
            // direct-hit destruction.
            _collisionIndex?.UpdateTransform(
                state.Car,
                state.Transform.Position / WorldScene.UnitsPerMetre,
                state.Transform.Orientation);
            _destructibles?.UpdatePosition(state.Car, state.Transform.Position, state.Transform.Orientation);
        }
    }

    /// <summary>What the car's vision holds, retained for 600 ticks (<c>0x426000</c>), or nothing.</summary>
    private Vector3d? ChaseTarget(CarAiState state, Vector3d? target)
    {
        // A car whose "Can fire" is off has no vision and chases nothing; a retained target that died is dropped.
        if (!_visions.TryGetValue(state.Car, out VehicleVision? vision) || !vision.CanFire || target is not { } seen)
        {
            state.ChaseHeld = false;
            state.ChaseRetention = 0;
            return null;
        }

        // 0x426000 takes the first slot the vision marks visible and holds it for 600 ticks, counting down while
        // nothing is visible; CarAI reads the held human's live position (+32) every tick.
        if (vision.Sees)
        {
            state.ChaseHeld = true;
            state.ChaseRetention = AiVehicleCombat.TargetRetentionTicks;
        }
        else if (state.ChaseRetention > 0)
        {
            state.ChaseRetention--;
        }
        else
        {
            state.ChaseHeld = false;
        }

        return state.ChaseHeld ? seen : null;
    }

    /// <summary>Retail's "π" (<c>0x533618</c>): three ulps above <see cref="Math.PI"/>.</summary>
    private static readonly double RetailPi = BitConverter.Int64BitsToDouble(0x400921FB54442D1B);

    /// <summary>Retail's "1/π" (<c>0x533610</c>): three ulps below <c>1 / Math.PI</c>.</summary>
    private static readonly double RetailInversePi = BitConverter.Int64BitsToDouble(0x3FD45F306DC9C880);

    /// <summary>170° as the chase eases by it (<c>0x533630</c>).</summary>
    private static readonly double Retail170Degrees = BitConverter.Int64BitsToDouble(0x4007BC89CEEED7C6);

    /// <summary>1/170° (<c>0x533628</c>).</summary>
    private static readonly double RetailInverse170Degrees = BitConverter.Int64BitsToDouble(0x3FD591F70B6D4D3F);

    /// <summary>10°, the chase's turn threshold (<c>0x533638</c>).</summary>
    private static readonly double Retail10Degrees = BitConverter.Int64BitsToDouble(0x3FC6571855555555);

    /// <summary>1/(10 m)², the final-stop ramp (<c>0x533600</c>).</summary>
    private const double CarAiRampScale = 5.960464477539063e-10;

    /// <summary>Whether the car is within three metres of a node (<c>0x4260C0</c>: 150994944 squared units).</summary>
    private static bool CarAiArrived(CarAiState state, GraphNode node)
    {
        Vector3d p = state.Transform.Position;
        X87 dx = (X87)node.Position.X - p.X, dy = (X87)node.Position.Y - p.Y, dz = (X87)node.Position.Z - p.Z;
        return (dx * dx + dy * dy + dz * dz).Value < CarAiArrivalSquared;
    }

    /// <summary>The next node from a point towards a destination (<c>0x425C10</c>).</summary>
    /// <remarks>
    /// When the route table has no link, retail logs "CarAI: Error in graph" and keeps the nearest node; a
    /// step to an empty slot is returned as the empty slot.
    /// </remarks>
    private static GraphNode? CarAiNextHop(CarAiState state, Vector3d from, int destinationId)
    {
        GraphNode? nearest = NearestNode(state.Graph, from);
        if (nearest is null || nearest.Id == destinationId)
        {
            return nearest;
        }

        return state.Graph.RouteTable?.TryGetStep(nearest.Id, destinationId, out int step, out _) == true
            ? state.Graph.NodeAt(step)
            : nearest;
    }

    /// <summary>
    /// <c>0x425B80</c>: from a point, the next position toward a destination -- the destination's own when the
    /// point is nearest it, the route step's otherwise, and the point unchanged when there is no link.
    /// </summary>
    private static Vector3d CarAiResolveToward(CarAiState state, Vector3d from, int destinationId)
    {
        GraphNode? nearest = NearestNode(state.Graph, from);
        if (nearest is null)
        {
            return from;
        }

        if (nearest.Id == destinationId)
        {
            return nearest.Position;
        }

        return state.Graph.RouteTable?.TryGetStep(nearest.Id, destinationId, out int step, out _) == true
            && state.Graph.NodeAt(step) is { } next
            ? next.Position
            : from;
    }

    private static GraphNode? NearestNode(GraphFile graph, Vector3d at)
    {
        GraphNearestNode.Find(graph, at, out GraphNode? nearest, out _);
        return nearest;
    }

    /// <summary>
    /// The heading of a world offset in the car's frame (<c>0x426140</c>'s rotation, then <c>0x414950</c>),
    /// signed positive to the left, kept at full width as retail keeps it in a register.
    /// </summary>
    /// <param name="state">The CarAI.</param>
    /// <param name="dx">The offset's X, already rounded as the caller's subtraction rounds it.</param>
    /// <param name="dy">The offset's Y.</param>
    /// <param name="dz">The offset's Z.</param>
    private static double CarAiHeading(CarAiState state, double dx, double dy, double dz)
    {
        // Mᵀ·d with the z term first (M6, M3, M0 for x; M7, M4, M1 for y), each stored as a double.
        Matrix3f m = state.Transform.Orientation;
        double lx = ((X87)m.M31 * dz + (X87)m.M21 * dy + (X87)m.M11 * dx).Value;
        double ly = ((X87)m.M32 * dz + (X87)m.M22 * dy + (X87)m.M12 * dx).Value;

        // 0x414950: z is cleared, the offset normalised by a reciprocal, its cosine to +Y through acos.
        if (lx == 0d && ly == 0d)
        {
            return 0d;
        }

        X87 inverse = (X87)1.0 / X87.Sqrt((X87)lx * lx + (X87)ly * ly + (X87)0.0 * 0.0);
        double nx = (inverse * lx).Value;
        double ny = (inverse * ly).Value;
        ny = ny < -1d ? -1d : ny > 1d ? 1d : ny;
        double heading = RetailAcos(ny);
        return nx > 0d ? -heading : heading;
    }

    /// <summary>The heading to a point, from the car (<c>d = point − P</c>, rounded toward zero).</summary>
    private static double CarAiHeadingTo(CarAiState state, Vector3d point)
    {
        Vector3d p = state.Transform.Position;
        return CarAiHeading(
            state,
            X87.Add(point.X, -p.X),
            X87.Add(point.Y, -p.Y),
            X87.Add(point.Z, -p.Z));
    }

    /// <summary>
    /// The CRT's <c>acos</c> (<c>0x516810</c>): it switches to round-to-nearest, takes
    /// <c>√((1 + c)(1 − c))</c> as a plain double and hands both to <c>fpatan</c>.
    /// </summary>
    /// <remarks><c>fpatan</c> keeps a 64-bit significand; <see cref="Math.Atan2(double, double)"/> is within a double ulp of it.</remarks>
    private static double RetailAcos(double c) =>
        c >= 1d ? 0d
        : c <= -1d ? Math.PI
        : Math.Atan2(Math.Sqrt((1d + c) * (1d - c)), c);

    /// <summary>Steering from a heading (<c>0x426140</c>, <c>0x4262F0</c>): the positive limit tests the full-width value, the rest the float.</summary>
    private static float CarAiSteering(double heading)
    {
        float hf = X87.ToFloat(heading);
        return heading > 0.17453294f ? -1f
            : hf < -0.17453294f ? 1f
            : ((X87)hf * -1.9098593f).F;
    }

    /// <summary>Route steering, throttle and brake (<c>0x426140</c>).</summary>
    private static void SteerCarAiRoute(CarAiState state)
    {
        state.DesiredSpeedKmh = X87.ToFloat(CarAiRouteSpeed(state));
        CarAiThrottle(state);
        double heading = state.TargetNode is { } node ? CarAiHeadingTo(state, node.Position) : 0d;

        // The wheel eases off as the throttle opens: full lock only at a standstill.
        state.Steering = (((((X87)1f - state.Throttle) * 0.95f) + 0.05f) * CarAiSteering(heading)).F;
    }

    /// <summary>Chase steering, throttle and brake (<c>0x4262F0</c>).</summary>
    private static void SteerCarAiChase(CarAiState state, Vector3d chased)
    {
        bool stopped = state.CommandSpeedKmh == 0f;
        Vector3d toward = !stopped && state.TargetNode is { } node ? node.Position : chased;
        double heading = CarAiHeadingTo(state, toward);
        state.Steering = CarAiSteering(heading);
        float magnitude = MathF.Abs(X87.ToFloat(heading));
        state.DesiredSpeedKmh = !stopped
            ? X87.ToFloat(CarAiChaseSpeed(state, chased))
            : magnitude > 0.17453294f ? 5f : ((X87)magnitude * 28.647888f).F;
        CarAiThrottle(state);

        // A stopped car that still has to turn keeps its brake off so it can crawl round.
        state.Brake = stopped && magnitude > 0.17453294f ? 0f : state.Halt;
    }

    /// <summary>Opens or closes the throttle towards the desired speed (<c>0x426260</c>).</summary>
    /// <remarks>
    /// The car's speed is read in world units a tick and converted to km/h (× 108/4096). The throttle
    /// moves one hundredth of the shortfall from the car's own smoothed throttle -- all in one register,
    /// truncated once -- and the brake comes on when told to halt or when the car is more than 5 km/h too fast.
    /// </remarks>
    private static void CarAiThrottle(CarAiState state)
    {
        X87 shortfall = (X87)state.DesiredSpeedKmh - ((X87)state.Transform.Speed * 0.0263671875f);
        X87 throttle = (X87)state.Transform.Thrust + (shortfall * 0.0099999998f);
        state.Throttle = throttle.Value < 0.0f ? 0f : throttle.Value > 1.0f ? 1f : throttle.F;
        state.Brake = state.Halt != 0f || shortfall.Value < -5.0 ? 1f : 0f;
    }

    /// <summary>The route speed, eased for the heading and for the turn at the node (<c>0x4255A0</c>).</summary>
    /// <remarks>
    /// Beyond ten metres of the current node the speed falls with the heading error to it. Within ten
    /// metres: mid-leg the look-ahead resolves back to the current node, so there is no turn; at a leg's last
    /// node the turn is the angle at the node between the way back to the car and the way on to the next
    /// leg's destination, so carrying straight on slows to about 5 km/h; before a Quit or End the speed runs
    /// down with the square of the distance. This is what the original computes, dead look-ahead included.
    /// </remarks>
    private static double CarAiRouteSpeed(CarAiState state)
    {
        float command = state.CommandSpeedKmh;
        if (state.TargetNode is not { } node)
        {
            return command;
        }

        Vector3d p = state.Transform.Position;
        Vector3d n = node.Position;
        double ax = X87.Add(p.X, -n.X), ay = X87.Add(p.Y, -n.Y), az = X87.Add(p.Z, -n.Z);
        double distanceSquared = ((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        if (distanceSquared > CarAiLookAheadSquared)
        {
            return CarAiEase(command, Math.Abs(CarAiHeading(state, -ax, -ay, -az)));
        }

        if (state.Route?.Current is not { } current || state.Graph.NodeAt(current.Operand) is not { } destination)
        {
            return command;
        }

        double turn;
        if (destination.Id != node.Id)
        {
            GraphNode? hop = NearestNode(state.Graph, destination.Position) is { } nearest
                ? CarAiNextHop(state, p, nearest.Id)
                : null;
            if (hop is null)
            {
                return command;
            }

            turn = CarAiAngleBetween(ax, ay, az, CarAiResolveToward(state, hop.Position, hop.Id), n);
        }
        else if (state.Route.Next is not { } next)
        {
            return command;
        }
        else if (next.Kind is AiPatrolCommandKind.WalkTo or AiPatrolCommandKind.RunTo)
        {
            if (state.Graph.NodeAt(next.Operand) is not { } following)
            {
                return command;
            }

            turn = CarAiAngleBetween(ax, ay, az, CarAiResolveToward(state, following.Position, following.Id), n);
        }
        else if (next.Kind is AiPatrolCommandKind.Quit or AiPatrolCommandKind.End)
        {
            return (((X87)command * distanceSquared) * CarAiRampScale).Value;
        }
        else
        {
            return command;
        }

        return command < 5f ? command : CarAiEase(command, turn);
    }

    /// <summary>The chase speed towards the node nearest the target (<c>0x425900</c>).</summary>
    private static double CarAiChaseSpeed(CarAiState state, Vector3d chased)
    {
        float command = state.CommandSpeedKmh;
        if (state.TargetNode is not { } node)
        {
            return command;
        }

        Vector3d p = state.Transform.Position;
        Vector3d n = node.Position;
        double ax = X87.Add(p.X, -n.X), ay = X87.Add(p.Y, -n.Y), az = X87.Add(p.Z, -n.Z);
        double distanceSquared = ((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        if (distanceSquared > CarAiChaseLookAheadSquared)
        {
            return CarAiEase(command, Math.Abs(CarAiHeading(state, -ax, -ay, -az)));
        }

        if (NearestNode(state.Graph, chased) is not { } goal)
        {
            return command;
        }

        if (goal.Id == node.Id)
        {
            return (((X87)command * distanceSquared) * CarAiRampScale).Value;
        }

        // Straight from +104 to the goal, with no nearest-node step; no link steers at the goal itself.
        Vector3d after = state.Graph.RouteTable?.TryGetStep(node.Id, goal.Id, out int step, out _) == true
            && state.Graph.NodeAt(step) is { } stepNode
            ? stepNode.Position
            : goal.Position;
        double turn = CarAiAngleBetween(ax, ay, az, after, n);
        if (command < 5f)
        {
            return command;
        }

        return turn < Retail10Degrees
            ? ((((X87)command - 5f) * ((X87)Retail170Degrees - turn)) * RetailInverse170Degrees + 5.0).Value
            : 5f;
    }

    /// <summary>The eased speed: 5 km/h at a full reversal, the command (less an ulp: retail's π) straight on.</summary>
    private static double CarAiEase(float command, double turn) =>
        ((((X87)command - 5f) * ((X87)RetailPi - turn)) * RetailInversePi + 5.0).Value;

    /// <summary>
    /// The angle at a node between the way back to the car (<c>P − N</c>, given) and the way on to
    /// <paramref name="beyond"/> (<c>0x4B31F0</c>, both vectors rounded toward zero).
    /// </summary>
    private static double CarAiAngleBetween(double ax, double ay, double az, Vector3d beyond, Vector3d node)
    {
        double bx = X87.Add(beyond.X, -node.X), by = X87.Add(beyond.Y, -node.Y), bz = X87.Add(beyond.Z, -node.Z);
        if ((ax == 0d && ay == 0d && az == 0d) || (bx == 0d && by == 0d && bz == 0d))
        {
            return 0d;
        }

        double dot = ((X87)az * bz + (X87)ay * by + (X87)ax * bx).Value;
        double la = X87.Sqrt((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        double c = ((X87)dot / (X87.Sqrt((X87)bx * bx + (X87)by * by + (X87)bz * bz) * la)).Value;
        if (!(c < 1d))
        {
            return 0d;
        }

        return c <= -1d ? RetailPi : RetailAcos(c);
    }

    private void TickVehicleCombat(GameTask vehicle)
    {
        Vector3d? target = VehicleCombatTarget?.Invoke();
        if (!_armaments.TryGetValue(vehicle, out VehicleArmament? armament))
        {
            return;
        }

        {
            if (!_taskTransforms.TryGetValue(vehicle, out TransformState? transform)
                || vehicle is not DeclaredTask declared)
            {
                return;
            }

            // A car's "Can fire" is an expression: 0x42E870 evaluates it every tick and registers the
            // car's vision while it holds, removing it again when it stops.
            bool canFire = _expressions.EvaluateCondition(Text(declared, "Can fire"), emptyValue: false, vehicle);
            CarAiState? controller = _carAis.Find(state => ReferenceEquals(state.Car, vehicle));
            bool active = ConditionalContainerGate.IsTaskActive(vehicle)
                && (controller is null || ConditionalContainerGate.IsTaskActive(controller.Task))
                && _destructibles?.IsDestroyed(vehicle) != true;

            if (armament.LegacyWeapon is { } weapon)
            {
                TickLegacyVehicleGun(declared, transform, armament, weapon, target, active, canFire);
                return;
            }

            // Each gun walks its vehicle's record for the first slot marked visible and aims at that human's
            // position (0x46F390); its own 90-run hold covers a target it loses.
            Vector3d? aim = active
                && _visions.TryGetValue(vehicle, out VehicleVision? vision)
                && vision.Sees
                && target is { } candidate
                    ? candidate
                    : null;

            // A car's update (0x42E320) runs its guns once, then re-seats them and runs every
            // AIStationaryGun a second time unless +11980 says its physics tick already did, so a car's
            // gun turns and meters its fire twice a tick. Probed on Mission 7 for both kinds of car: the
            // parked T80 354 (T80 rounds 75 ticks apart, the coaxial M2HB one a tick) and the CarAI-driven
            // APC 352 (APC rounds 75 ticks apart), where the rows' own cadences are 150, 2 and 150.
            //
            // A helicopter's update (0x4316D0) never runs its model's attachments; its gun turns only while
            // its AnimTask flies it, once a tick. Probed on Mission 7's heli 353: nothing while parked, and
            // on its flight 55 MIL rounds over 162 ticks -- the row's own three-tick cadence.
            bool helicopter = string.Equals(vehicle.Type.Name, "Heli", StringComparison.Ordinal);
            int runs = !helicopter
                ? 2
                : _animations.Exists(animation => animation.TargetId == vehicle.Id && animation.RanThisTick) ? 1 : 0;

            // A gun aims from its mount's position (+32) and fires from its weapon object, both as the last re-seat
            // left them: nothing moves them while the guns run. The spawn seats them first (0x46EC90).
            if (!armament.Seated)
            {
                SeatVehicleGuns(armament, transform);
            }

            foreach (VehicleGunState gun in armament.Guns)
            {
                gun.RoundsFiredThisTick = 0;
            }

            for (int run = 0; run < runs; run++)
            {
                // A car re-seats its guns between its two runs (0x46FBA0 through op 46), from where it stands now and
                // with the traverse the first run left; a helicopter's AnimTask moves it before its one run.
                if (run == 1 || helicopter)
                {
                    SeatVehicleGuns(armament, transform);
                }

                // The turret runs before the gun it carries (0x46F390 walks its children last).
                foreach (VehicleGunState gun in armament.Guns)
                {
                    Vector3d? local = aim is { } point ? transform.Orientation.Transform(point - gun.MountPosition) : null;
                    if (FireVehicleGun(vehicle, gun, local, aim) is { } shot)
                    {
                        gun.RoundsFiredThisTick++;
                        gun.ShotSequence++;
                        VehicleCombatShotFired?.Invoke(shot);
                    }

                    // The Gun's own update (0x478640) counts its cooldown down after Gun_Fire has run.
                    if (gun.Cooldown > 0)
                    {
                        gun.Cooldown--;
                    }
                }
            }
        }
    }

    /// <summary>Measures how far each zone-carrying vehicle moved this tick, the speed 0x45FA80 tests.</summary>
    private void TickDeathZones(GameTask vehicle)
    {
        if (!_deathZones.TryGetValue(vehicle, out DeathZoneState? state))
        {
            return;
        }

        if (!_taskTransforms.TryGetValue(vehicle, out TransformState? transform))
        {
            state.SpeedUnitsPerTick = 0d;
            return;
        }

        state.SpeedUnitsPerTick = state.LastPosition is { } last ? (transform.Position - last).Length : 0d;
        state.LastPosition = transform.Position;
    }

    /// <summary>One run of a vehicle gun: turn (0x46F390), then let Gun_Fire (0x478BA0) take the trigger.</summary>
    private VehicleCombatShot? FireVehicleGun(
        GameTask vehicle,
        VehicleGunState gun,
        Vector3d? local,
        Vector3d? aim)
    {
        bool trigger = gun.Turret.Tick(local);
        int burst = gun.Gun.Weapon.MaximumRoundsPerBurst;

        // The hold of Gun_Fire decides its sounds: the trigger, the pull's burst unspent and the magazine not empty.
        // The cooldown between rounds is not part of it (0x478BA0).
        gun.Hold = trigger && !(burst > 0 && gun.RoundsThisPull >= burst) && gun.Ammunition != 0;
        if (!trigger)
        {
            // Gun_Fire resets the pull's round count (+320) when the trigger drops.
            gun.RoundsThisPull = 0;
            return null;
        }

        // A row that fires a fixed burst per pull (+1612) stops at it until the trigger is released.
        if ((burst > 0 && gun.RoundsThisPull >= burst)
            || gun.Cooldown > 0
            || gun.Ammunition == 0
            || aim is not { } shotTarget)
        {
            return null;
        }

        // Gun_Fire restarts the cooldown at (60 / rpm) * 30 ticks, truncated.
        gun.RoundsThisPull++;
        gun.Cooldown = Math.Max(1, GunTiming.TicksPerRound(gun.Gun.Weapon.RoundsPerMinute));
        if (gun.Ammunition > 0)
        {
            gun.Ammunition--;
        }

        // With no origin in its message, Gun_Fire starts the round at the weapon's magic vertex 2 and sends it down the
        // weapon's middle column (0x478BA0) -- the weapon object as the last re-seat posed it, which swings a gun on a
        // gun round with the gun it rides on.
        Vector3f muzzle = gun.Gun.MuzzleVertex;
        Vector3d origin = gun.WeaponPosition
            + gun.WeaponOrientation.Transposed().Transform(new Vector3d(muzzle.X, muzzle.Y, muzzle.Z));
        Vector3f barrel = gun.WeaponOrientation.Transposed().Transform(Vector3f.UnitY).Normalized();
        WeaponType row = gun.Gun.Weapon;

        // Gun_Fire sends the round down the barrel, not at the target, turned inside the row's cone
        // (0x478BA0: +1636 for a pull's first three rounds, +1640 after; radius three draws deep).
        float cone = GunFiring.ConeAngle(row, gun.RoundsThisPull);
        if (cone > 0f)
        {
            Span<float> draws = stackalloc float[GunFiring.ConeRadiusSamples + 1];
            for (int draw = 0; draw < draws.Length; draw++)
            {
                draws[draw] = _gunRandom.NextSingle();
            }

            (float radius, float bearing) = GunFiring.SampleCone(cone, draws);
            Vector3f side = Vector3f.Cross(barrel, Vector3f.UnitZ);
            side = side.Length > 1e-6f ? side.Normalized() : Vector3f.UnitX;
            Vector3f up = Vector3f.Cross(side, barrel).Normalized();
            barrel = GunFiring.OffsetInCone(barrel, side, up, radius, bearing);
        }

        double range = (shotTarget - origin).Length;
        Vector3d along = origin + new Vector3d(barrel.X * range, barrel.Y * range, barrel.Z * range);
        return new VehicleCombatShot(
            origin,
            along,
            barrel,
            MissileFlight.TypeForLauncher(row),
            MissileFlight.LaunchSpeedMetresPerSecond(row),
            string.IsNullOrWhiteSpace(row.Projectile.Text) ? "110_02_1" : row.Projectile.Text)
        {
            Owner = vehicle,
            WeaponScriptId = row.ScriptId,
            IsHitscan = gun.Gun.IsHitscan,
        };
    }

    /// <summary>The Level 6 capture's single fixed gun: fire the row's cadence at whatever is retained.</summary>
    private void TickLegacyVehicleGun(
        DeclaredTask vehicle,
        TransformState transform,
        VehicleArmament armament,
        WeaponType weapon,
        Vector3d? target,
        bool active,
        bool canFire)
    {
        if (target is not { } seen)
        {
            return;
        }

        Vector3f forward = transform.Orientation.Column2;
        Vector3d sightOrigin = transform.Position + new Vector3d(
            forward.X * AiVehicleCombat.CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            forward.Y * AiVehicleCombat.CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            (forward.Z * AiVehicleCombat.CapturedMuzzleForwardMetres
                + AiVehicleCombat.CapturedMuzzleHeightMetres) * WorldScene.UnitsPerMetre);
        bool visible = VehicleCombatLineOfSight?.Invoke(vehicle, sightOrigin, seen) ?? true;
        VehicleCombatShot? shot = armament.Sight.Tick(new AiVehicleCombatInput(
            transform.Position,
            transform.Orientation,
            seen,
            active,
            canFire,
            Single(vehicle, "ViewCone Alpha (degrees)", 180f),
            Single(vehicle, "ViewCone Gamma (degrees)", 360f),
            Single(vehicle, "ViewCone Length (meter)", 0f),
            visible,
            weapon.MuzzleVelocity,
            weapon.RoundsPerMinute));
        if (shot is { } fired)
        {
            VehicleCombatShotFired?.Invoke(fired with
            {
                Owner = vehicle,
                WeaponScriptId = weapon.ScriptId,
                MissileType = MissileFlight.TypeForLauncher(weapon),
                MuzzleVelocityMetresPerSecond = MissileFlight.LaunchSpeedMetresPerSecond(weapon),
                ModelName = string.IsNullOrWhiteSpace(weapon.Projectile.Text)
                    ? fired.ModelName
                    : weapon.Projectile.Text,
            });
        }
    }

    /// <summary>Marks an explicitly configured helicopter for terminal terrain landing.</summary>
    /// <param name="task">The helicopter task whose first terrain contact is terminal.</param>
    /// <remarks>
    /// Mission setup supplies this state for authored one-shot landings. The generic cutscene runtime
    /// does not infer terminal behavior from a task id, so unrelated helicopters continue their own
    /// animation after touching terrain.
    /// </remarks>
    public void EnableTerminalTerrainLanding(GameTask task)
    {
        TransformState state = TerminalLandingState(task);
        state.TerminalLandingOnTerrain = true;
    }

    /// <summary>Marks a helicopter for terminal landing at a separately authored final pose.</summary>
    /// <param name="task">The helicopter task whose first terrain contact is terminal.</param>
    /// <param name="authoredTouchdown">The retail task whose authored position is the landing target.</param>
    public void EnableTerminalTerrainLanding(GameTask task, GameTask authoredTouchdown)
    {
        TransformState state = TerminalLandingState(task);
        ArgumentNullException.ThrowIfNull(authoredTouchdown);
        if (!_taskTransforms.TryGetValue(authoredTouchdown, out TransformState? touchdown))
        {
            throw new ArgumentException(
                "Terminal terrain landing requires a captured touchdown task.",
                nameof(authoredTouchdown));
        }

        state.TerminalLandingOnTerrain = true;
        state.TerminalLandingTarget = touchdown.OriginPosition;
    }

    private TransformState TerminalLandingState(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!_taskTransforms.TryGetValue(task, out TransformState? state)
            || !string.Equals(task.Type?.Name, "Heli", StringComparison.Ordinal))
        {
            throw new ArgumentException("Terminal terrain landing requires a captured Heli task.", nameof(task));
        }

        return state;
    }

    /// <summary>Gets the currently running scene, if any.</summary>
    public CutSceneTask? ActiveScene => _scenes.LastOrDefault(
        static state => state.Task.IsRun && ConditionalContainerGate.IsTaskActive(state.Task))?.Task;

    /// <summary>Queues the retail break-cutscene key for the next logic tick.</summary>
    public void RequestBreak() => _breakRequested = true;

    /// <summary>Advances all cutscene-owned tasks by one fixed logic tick.</summary>
    public void Tick()
    {
        SimulationTick++;
        foreach (AnimationState animation in _animations)
        {
            animation.RanThisTick = false;
        }

        // One walk of the level tree in authored order: each task runs its own update where the tree puts it
        //, not grouped by type.
        _walker.Walk(_root);
        UpdateFrame();
    }

    /// <summary>
    /// Gets the level's update walk. Other level systems register the tasks they own into it, so that one walk
    /// in authored order ticks them all.
    /// </summary>
    public LevelUpdateWalker Walker => _walker;

    /// <summary>A LevelTimer's update (<c>0x51BE10</c>): children first, then On, Reset and the tick count.</summary>
    private void TickTimer(TimerState timer)
    {
        _walker.WalkChildren(timer.Task);
        if (_expressions.EvaluateCondition(timer.Reset, emptyValue: false, timer.Task))
        {
            timer.Tick = 0;
        }

        timer.IsRun = string.IsNullOrWhiteSpace(timer.Run)
            ? timer.InitialRun
            : _expressions.EvaluateCondition(timer.Run, emptyValue: timer.InitialRun, timer.Task);
        if (timer.IsRun)
        {
            timer.Tick++;
        }
    }

    /// <summary>An AnimTask's update (<c>0x4F3B40</c>): the run expression, then the next record step's packet.</summary>
    private void TickAnimation(AnimationState animation)
    {
        // 0x4F3B40: a Run expression rewrites the run byte every tick; without one the byte keeps whatever it holds
        // -- its Initial run, or what a script wrote to isRun. The task steps while the byte is set.
        if (!string.IsNullOrWhiteSpace(animation.Run))
        {
            animation.IsRun = _expressions.EvaluateCondition(animation.Run, emptyValue: animation.IsRun, animation.Task);
        }

        bool run = animation.IsRun;
        animation.RanThisTick = run;
        if (!run)
        {
            return;
        }

        int previousTick = animation.Program.Tick;
        animation.Tick++;
        animation.Program.SeekTo(previousTick + 1);
        ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
        if (previousTick < 0)
        {
            // A task that was out of the world at load has its two tick-zero dispatches on its first run.
            animation.Program.Step();
            ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
        }
    }

    /// <summary>A CutScene's update (<c>0x4F51D0</c>): its children (its AnimTasks) first, then its own run.</summary>
    private void TickScene(SceneState state)
    {
        CutSceneTask scene = state.Task;
        _walker.WalkChildren(scene);
        bool wasRun = scene.IsRun;
        if (_expressions.EvaluateCondition(scene.Reset, emptyValue: false, scene))
        {
            scene.Ticks = 0;
            scene.IsFinished = false;
        }

        scene.IsRun = string.IsNullOrWhiteSpace(scene.Run)
            ? scene.InitialRun && !scene.IsFinished
            : _expressions.EvaluateCondition(scene.Run, emptyValue: false, scene);

        if (!wasRun && scene.IsRun)
        {
            ExecuteSceneExpression(scene, scene.StartExpression);
        }

        if (scene.IsRun)
        {
            // CutScene_SetCamera runs with the *current* nTick and marks isFinished only when that old tick is
            // beyond the authored duration; Run stays latched this frame and nTick is incremented afterwards.
            // The Run expression observes isFinished on the following frame -- Mission 1's 2.2-second landing
            // scene publishes train ticks 0..67, exactly as the retail probe records.
            double total = state.TotalTime(scene.TimeScale);
            if ((scene.Ticks / (double)Frequency) > total)
            {
                scene.Ticks = Math.Max(0, (int)(Frequency * total));
                scene.IsFinished = true;
            }

            int delta = string.IsNullOrWhiteSpace(scene.TimeDelta)
                ? 1
                : (int)(_expressions.EvaluateSingle(scene.TimeDelta, owner: scene) * Frequency);
            scene.Ticks = Math.Max(0, scene.Ticks + delta);
        }

        if (wasRun && !scene.IsRun)
        {
            ExecuteSceneExpression(scene, scene.StopExpression);
        }
    }

    /// <summary>A scene taken out of the world stops.</summary>
    private void DeactivateScene(SceneState state)
    {
        if (state.Task.IsRun)
        {
            state.Task.IsRun = false;
            ExecuteSceneExpression(state.Task, state.Task.StopExpression);
        }
    }

    /// <summary>A ConditionalSound's update (<c>0x4250D0</c>): its condition's edges, then its children.</summary>
    private void TickSound(SoundState sound)
    {
        bool run = _expressions.EvaluateCondition(sound.Condition, emptyValue: false, sound.Task);
        SetSoundRun(sound, run);
        _walker.WalkChildren(sound.Task);
    }

    private void SetSoundRun(SoundState sound, bool run)
    {
        if (run == sound.IsRun)
        {
            return;
        }

        sound.IsRun = run;
        if (run)
        {
            if (!sound.OneShot || !sound.HasPlayed)
            {
                sound.HasPlayed = true;
                _soundChanged?.Invoke(new CutsceneSoundEvent(
                    sound.Task,
                    sound.Sound,
                    sound.Simple || sound.RelativeToMicrophone ? null : sound.Position,
                    Playing: true,
                    Simple: sound.Simple));
            }
        }
        else if (!sound.Simple && sound.HasPlayed)
        {
            // A non-simple ConditionalSound owns its voice for the condition's lifetime. A simple sound is
            // fire-and-forget and deliberately receives no falling-edge stop.
            _soundChanged?.Invoke(new CutsceneSoundEvent(
                sound.Task,
                sound.Sound,
                sound.RelativeToMicrophone ? null : sound.Position,
                Playing: false,
                Simple: false));
        }
    }

    /// <summary>
    /// A Car's update (<c>0x42E320</c>): at most once a tick, its children -- the CarAI or AnimTask whose packet
    /// steps its physics -- then its guns and its death zones.
    /// </summary>
    private void TickCar(GameTask car)
    {
        if (_lastVehicleTick.TryGetValue(car, out int last) && last == SimulationTick)
        {
            return;
        }

        _lastVehicleTick[car] = SimulationTick;
        TickVehicleVision(car);
        _walker.WalkChildren(car);
        TickVehicleCombat(car);
        TickDeathZones(car);
    }

    /// <summary>A Heli's update (<c>0x4316D0</c>): its children -- its AnimTask flies it -- then its gun.</summary>
    private void TickHeli(GameTask heli)
    {
        TickVehicleVision(heli);
        _walker.WalkChildren(heli);
        TickVehicleCombat(heli);
        TickDeathZones(heli);
    }

    /// <summary>
    /// The vehicle's eye: a car's <c>0x42E870</c> (eye 8192 units over its origin, its own orientation), a helicopter's
    /// <c>+1960</c> block of <c>0x4316D0</c> (eye at its origin, looking down: its orientation times RotX(-90°)).
    /// </summary>
    private void TickVehicleVision(GameTask vehicle)
    {
        if (!_visions.TryGetValue(vehicle, out VehicleVision? vision)
            || vehicle is not DeclaredTask declared
            || !_taskTransforms.TryGetValue(vehicle, out TransformState? transform)
            || _destructibles?.IsDestroyed(vehicle) == true)
        {
            return;
        }

        bool helicopter = string.Equals(vehicle.Type.Name, "Heli", StringComparison.Ordinal);
        bool canFire = _expressions.EvaluateCondition(Text(declared, "Can fire"), emptyValue: false, vehicle);
        Vector3d eyePosition = helicopter
            ? transform.Position
            : transform.Position + new Vector3d(0d, 0d, VehicleVision.CarEyeHeightUnits);
        Matrix3f orientation = helicopter ? transform.Orientation * HeliEyeTilt : transform.Orientation;
        AiEyeSnapshot eye = new(
            eyePosition,
            orientation,
            new AiSnapshotCone(
                Single(declared, "ViewCone Alpha (degrees)", 180f) * 0.017453292,
                Single(declared, "ViewCone Gamma (degrees)", 360f) * 0.017453292,
                Single(declared, "ViewCone Length (meter)", 0f) * 4096.0),
            default,
            secondaryEnabled: false);
        Vector3d? target = VehicleSightTarget?.Invoke() ?? VehicleCombatTarget?.Invoke();
        // The sweep compares its phase against the flow tick count (0x4028B0), which the walk increments after it runs:
        // the level's first walk is flow tick 0. Probed on mission 7: the T80's sweeps fall on flow ticks 3, 7, 11, ...
        vision.Tick(
            SimulationTick - 1,
            canFire,
            eye,
            target,
            point => VehicleCombatLineOfSight?.Invoke(vehicle, eyePosition, point) ?? true,
            NextVisionPhase);
    }

    /// <summary>RotX(-1.5707964) as <c>0x4B3B60</c> builds it, which <c>0x4316D0</c> turns a helicopter's eye by.</summary>
    private static readonly Matrix3f HeliEyeTilt = HeliTilt();

    private static Matrix3f HeliTilt()
    {
        float angle = -1.5707964f;
        float sine = (float)Math.Sin(angle), cosine = (float)Math.Cos(angle);
        return new Matrix3f(1f, 0f, 0f, 0f, cosine, -sine, 0f, sine, cosine);
    }

    private int NextVisionPhase()
    {
        int phase = _visionCounter & 3;
        _visionCounter = (_visionCounter + 1) & 3;
        return phase;
    }

    /// <summary>Serves cutscene-specific level-expression natives to sibling mission runtimes.</summary>
    public QvmValue? TryInvokeNative(string name, ScriptArguments arguments) => InvokeNative(name, arguments);

    /// <summary>Evaluates one scene's camera frame at an arbitrary scene time in seconds.</summary>
    /// <remarks>
    /// Uses exactly the production interpolation path (same knot selection, Hermite basis, link/
    /// target remap and presentation fields) so tests can compare the emitted camera against the
    /// authored EditCamera data without driving the whole level simulation. Callers must accumulate
    /// shot start times exactly like <see cref="SceneState.Timed"/> (double += float duration) so
    /// knot times match bit-for-bit and the current-shot scan selects the asserted shot.
    /// </remarks>
    internal CutsceneFrame? EvaluateSceneFrame(CutSceneTask scene, double time)
    {
        SceneState? state = _scenes.FirstOrDefault(item => ReferenceEquals(item.Task, scene));
        return state is null ? null : BuildFrame(state, time);
    }

    /// <summary>Clears one-tick cutscene input after every level expression has had a chance to read it.</summary>
    public void FinishExpressionTick() => _breakRequested = false;

    /// <summary>Resolves an animation-driven task transform in render units.</summary>
    public bool TryGetDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (TryEvaluateAttachment(task, out Vector3d attachedPosition, out orientation))
        {
            position = attachedPosition / WorldScene.UnitsPerMetre;
            return true;
        }

        if (_taskTransforms.TryGetValue(task, out TransformState? state))
        {
            UpdateDerivedPose(state);

            position = state.Position / WorldScene.UnitsPerMetre;
            orientation = state.Orientation;
            // Child Train tasks have no authored world position: even before the engine moves, their
            // derived consist placement must replace WorldScene's position-zero fallback.
            // Collision-parked helis also publish the rested pose so the hull is not left at the
            // authored float height while its container is still closed.
            return state.IsDynamic || state.TrainRoot is not null || state.CollisionEnabled;
        }

        position = default;
        orientation = Matrix3f.Identity;
        return false;
    }

    /// <summary>Reads the latest integrated velocity of a dynamic cutscene vehicle.</summary>
    public bool TryGetDynamicVelocity(GameTask task, out Vector3d velocity)
    {
        if (_taskTransforms.TryGetValue(task, out TransformState? state) && state.IsDynamic)
        {
            velocity = state.Velocity / WorldScene.UnitsPerMetre;
            return true;
        }

        velocity = default;
        return false;
    }

    /// <summary>The magic-object task type retail gives a car's rolling wheels.</summary>
    private const string WheelTaskTypeName = "TASKTYPE_WHEEL";
    private const string GunMountTaskTypeName = "TASKTYPE_AISTATIONARYGUN";

    /// <summary>The two heights the vehicle obstacle probe sweeps, in metres above the body origin.</summary>
    private static readonly double[] CarProbeHeightsMetres = [0.75d, 1.95d];

    /// <summary>How far past the proposed step the obstacle probe reaches, in world units.</summary>
    private const double CarProbeMarginUnits = 128d;

    /// <summary>
    /// The record a car falls back to when its model has no <c>physicsobj</c> entry: the shipped
    /// truck's mass and dimensions with a single driven, unsteered wheel at the origin.
    /// </summary>
    private static readonly CarPhysicsDefinition DefaultCarPhysics = new(
        3_000f,
        new Vector3f(2.2f, 7.8f, 3f),
        0,
        [],
        0.95f,
        5_000f,
        0.95f,
        0,
        [new CarWheelDefinition(false, true, 0f, 0, (float)WorldScene.UnitsPerMetre * 0.3f, 0.5f)],
        string.Empty,
        []);

    /// <summary>
    /// Binds every car's drawn wheels to its physics record, in the order both are authored in.
    /// </summary>
    /// <param name="scene">The built world whose placements carry the wheel attachments.</param>
    /// <remarks>
    /// A car's wheels arrive as <c>TASKTYPE_WHEEL</c> magic-object attachments, one placement each,
    /// in the same order as the record's wheel entries -- on the Mission 1 truck that pairing puts
    /// both steered, driven entries exactly on the two front wheels. Their offsets from the body
    /// origin are what the drive force and the tyre torque are applied at, so the physics reads them
    /// from the model rather than guessing an axle layout.
    /// </remarks>
    /// <param name="modelRadius">
    /// Resolves a wheel model's radius in world units, for the rolling animation. Optional: without
    /// it the wheels still turn, just at the suspension travel rather than their real size.
    /// </param>
    public void BindVehicleWheels(WorldScene scene, Func<string, float>? modelRadius = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _carWheels.Clear();
        _carWheelIndices.Clear();
        Dictionary<GameTask, List<WorldPlacement>> byOwner = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!string.Equals(placement.MagicTaskTypeName, WheelTaskTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!byOwner.TryGetValue(placement.Owner, out List<WorldPlacement>? list))
            {
                list = [];
                byOwner[placement.Owner] = list;
            }

            list.Add(placement);
        }

        foreach ((GameTask owner, List<WorldPlacement> placements) in byOwner)
        {
            if (!_taskTransforms.TryGetValue(owner, out TransformState? state)
                || state.CarPhysics is not { } physics)
            {
                continue;
            }

            // Where each drawn wheel sits in the body's own axes.
            Vector3d[] drawn = new Vector3d[placements.Count];
            double acrossTotal = 0d;
            for (int i = 0; i < placements.Count; i++)
            {
                Vector3d placementUnits = placements[i].Position * WorldScene.UnitsPerMetre;
                drawn[i] = state.AuthoredOrientation.Transform(placementUnits - state.OriginPosition);
                acrossTotal += Math.Abs(drawn[i].X);
            }

            double acrossMagnitude = placements.Count > 0 ? acrossTotal / placements.Count : 0d;

            // Forces are applied at the WHEEL BODIES, of which there are fewer than drawn wheels: a
            // tandem rear pair shares one. Each body sits at the outermost drawn wheel that refers
            // to it, and an axle is symmetric about the body's centreline, so the across offset is
            // the mean of the drawn magnitudes rather than the wheel model's own origin. Reading a
            // running game back confirms both: retail's four bodies on the Mission 1 truck are at
            // (-0.79, +2.81), (-0.79, -2.29), (+0.85, +2.81) and (+0.85, -2.29) metres, which this
            // reproduces to within five centimetres.
            Dictionary<int, Vector3d> bodyOffsets = [];
            for (int i = 0; i < placements.Count && i < physics.Wheels.Count; i++)
            {
                CarWheelDefinition entry = physics.Wheels[i];
                int slot = entry.BodyIndex >= 0 ? entry.BodyIndex : -10 - entry.BodyIndex;
                Vector3d candidate = new(
                    Math.Sign(drawn[i].X) * acrossMagnitude,
                    drawn[i].Y,
                    drawn[i].Z);
                if (!bodyOffsets.TryGetValue(slot, out Vector3d held)
                    || Math.Abs(candidate.Y) > Math.Abs(held.Y))
                {
                    bodyOffsets[slot] = candidate;
                }
            }

            int contacts = 0;
            for (int i = 0; i < physics.Wheels.Count; i++)
            {
                if (physics.Wheels[i].HasBody)
                {
                    contacts++;
                }
            }

            CarWheel[] wheels = new CarWheel[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                CarWheelDefinition definition = i < physics.Wheels.Count
                    ? physics.Wheels[i]
                    : new CarWheelDefinition(false, false, 0f, -1, 0f, 0f);
                int slot = definition.BodyIndex >= 0 ? definition.BodyIndex : -10 - definition.BodyIndex;
                Vector3d offset = bodyOffsets.TryGetValue(slot, out Vector3d bodyOffset)
                    ? bodyOffset
                    : drawn[i];

                // Where the spring is probed from. Retail keeps that point on the wheel body, which
                // is model data the port has no reader for, so it is placed by what it has to
                // achieve: the spring settles with its origin a compression UNDER the surface, and
                // the tyre has to end up ON the surface, so the origin sits a compression below the
                // contact patch. Measuring the running game: its Mission 2 truck rests 0.037 m over
                // the terrain, which puts the bottom of a 0.464 m wheel 0.023 m clear -- touching.
                double radius = Math.Max(0d, modelRadius?.Invoke(placements[i].Instance.Name) ?? 0d);
                double travel = definition.SuspensionLengthUnits;
                double stiffness = definition.SpringStiffness;
                double settled = contacts > 0 && stiffness > 0d
                    ? Math.Min(travel, travel / (contacts * stiffness))
                    : 0d;
                offset = new Vector3d(offset.X, offset.Y, drawn[i].Z - radius - settled);
                CarBodyDefinition? body = null;
                for (int b = 0; b < physics.Bodies.Count; b++)
                {
                    if (physics.Bodies[b].Index == slot)
                    {
                        body = physics.Bodies[b];
                        break;
                    }
                }

                wheels[i] = new CarWheel(
                    definition,
                    offset,
                    body?.LateralFriction ?? 0.95f,
                    body?.LongitudinalFriction ?? 0.05f,
                    (float)radius);
            }

            _carWheels[owner] = wheels;
            for (int i = 0; i < placements.Count; i++)
            {
                _carWheelIndices[placements[i]] = i;
            }
        }
    }

    /// <summary>Clamps a record retention into the range the native blend assumes.</summary>
    private static float CarRetention(float? retention) =>
        retention is { } value && float.IsFinite(value) && value is >= 0f and <= 1f ? value : 0.95f;

    /// <summary>Drops an axis into the ground plane, falling back when it is degenerate.</summary>
    private static Vector3d FlattenAxis(Vector3f axis, Vector3d fallback)
    {
        Vector3d flat = new(axis.X, axis.Y, 0d);
        double length = flat.Length;
        return length > 1e-6d ? flat / length : fallback;
    }

    /// <summary>The wheels bound to a car, or one driven wheel at the origin when none are bound.</summary>
    private CarWheel[] CarWheelsFor(GameTask? task, in CarPhysicsDefinition physics)
    {
        if (task is not null && _carWheels.TryGetValue(task, out CarWheel[]? wheels) && wheels.Length > 0)
        {
            return wheels;
        }

        // Without the drawn attachments the record's own entries still say how many wheels drive and
        // steer; place them on the authored dimension box so the body still moves and turns.
        CarWheel[] fallback = new CarWheel[Math.Max(1, physics.Wheels.Count)];
        double halfLength = physics.Dimensions.Y * 0.25d * WorldScene.UnitsPerMetre;
        double halfWidth = physics.Dimensions.X * 0.5d * WorldScene.UnitsPerMetre;
        for (int i = 0; i < fallback.Length; i++)
        {
            CarWheelDefinition definition = i < physics.Wheels.Count
                ? physics.Wheels[i]
                : new CarWheelDefinition(false, true, 0f, 0, 0f, 0f);
            fallback[i] = new CarWheel(
                definition,
                new Vector3d(
                    (i % 2) == 0 ? halfWidth : -halfWidth,
                    definition.Steerable ? halfLength : -halfLength,
                    0d),
                0.95f,
                0.05f);
        }

        return fallback;
    }

    /// <summary>Applies a moving task's rigid transform to one of its authored model placements.</summary>
    /// <remarks>
    /// A retail vehicle task expands into a root model plus all of its magic-object attachments. Those
    /// placements share an owner but not a transform. Replacing each placement with the task origin
    /// collapses rotors, wheels and cabin fittings into one point; this preserves each placement's local
    /// offset and orientation while the owning task moves.
    /// </remarks>
    public bool TryGetPlacementDrawTransform(
        GameTask task,
        Vector3d placementPosition,
        Matrix3f placementOrientation,
        out Vector3d position,
        out Matrix3f orientation)
    {
        Vector3d originPosition;
        Matrix3f originOrientation;
        Vector3d currentPosition;
        Matrix3f currentOrientation;

        if (_attachments.TryGetValue(task, out AttachmentState? attachment))
        {
            if (!TryEvaluateAttachment(task, out currentPosition, out currentOrientation))
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
            originPosition = attachment.Position;
            originOrientation = attachment.Orientation;
        }
        else if (_taskTransforms.TryGetValue(task, out TransformState? state))
        {
            UpdateDerivedPose(state);
            if (!state.IsDynamic && state.TrainRoot is null && !state.CollisionEnabled)
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
            originPosition = state.OriginPosition;
            originOrientation = state.AuthoredOrientation;
            currentPosition = state.Position;
            currentOrientation = state.Orientation;
        }
        else
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        Vector3d placementUnits = placementPosition * WorldScene.UnitsPerMetre;
        Vector3d relative = originOrientation.Transform(placementUnits - originPosition);
        Vector3d moved = currentPosition + currentOrientation.Transposed().Transform(relative);
        position = moved / WorldScene.UnitsPerMetre;
        orientation = currentOrientation * originOrientation.Transposed() * placementOrientation;
        return true;
    }

    /// <summary>Applies vehicle motion and the first Rotor draw sample to an expanded placement.</summary>
    public bool TryGetPlacementDrawTransform(
        WorldPlacement placement,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (TryGetRotorDrawState(placement, out RotorDrawState rotorDraw))
        {
            position = rotorDraw.Position;
            orientation = rotorDraw.GetOrientation(0);
            return true;
        }

        if (_gunPlacements.TryGetValue(placement, out (GameTask Vehicle, int Index) mount)
            && _armaments.TryGetValue(mount.Vehicle, out VehicleArmament? armament)
            && TryGetVehicleGunMount(mount.Vehicle, armament, mount.Index, out Vector3d mountUnits, out orientation))
        {
            position = mountUnits / WorldScene.UnitsPerMetre;
            return true;
        }

        if (!TryGetPlacementDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            out position,
            out orientation))
        {
            return false;
        }

        if (TryGetWheelDraw(placement, out Matrix3f spun))
        {
            orientation *= spun;
        }

        return true;
    }

    /// <summary>Binds each armed vehicle's drawn gun mounts to the guns that turn them.</summary>
    /// <param name="scene">The built world whose placements carry the vehicles' attachments.</param>
    /// <remarks>
    /// A vehicle's <c>TASKTYPE_AISTATIONARYGUN</c> attachments arrive in the same depth-first order its guns were
    /// collected in (<see cref="VehicleGun.FromModel"/>); each placement takes the next gun whose mount model it is.
    /// Call it after <see cref="ConfigureVehicleGuns"/>.
    /// </remarks>
    public void BindVehicleGunPlacements(WorldScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _gunPlacements.Clear();
        Dictionary<GameTask, int> next = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!string.Equals(placement.MagicTaskTypeName, GunMountTaskTypeName, StringComparison.Ordinal)
                || !_armaments.TryGetValue(placement.Owner, out VehicleArmament? armament))
            {
                continue;
            }

            for (int index = next.GetValueOrDefault(placement.Owner); index < armament.Guns.Count; index++)
            {
                if (string.Equals(armament.Guns[index].Gun.Model, placement.Instance.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _gunPlacements[placement] = (placement.Owner, index);
                    next[placement.Owner] = index + 1;
                    break;
                }
            }
        }
    }

    /// <summary>Every armed live vehicle's weapon models, as this tick's updates posed them.</summary>
    /// <returns>One entry per gun of an active, undestroyed vehicle.</returns>
    public IEnumerable<VehicleGunVisual> VehicleGunVisuals()
    {
        foreach ((GameTask vehicle, VehicleArmament armament) in _armaments)
        {
            if (!ConditionalContainerGate.IsTaskActive(vehicle) || _destructibles?.IsDestroyed(vehicle) == true)
            {
                continue;
            }

            for (int index = 0; index < armament.Guns.Count; index++)
            {
                if (!TryGetVehicleGunWeapon(vehicle, armament, index, out Vector3d position, out Matrix3f orientation))
                {
                    continue;
                }

                VehicleGunState gun = armament.Guns[index];
                yield return new VehicleGunVisual(
                    vehicle,
                    index,
                    gun.Gun.Mount.WeaponModel,
                    position,
                    orientation,
                    gun.RoundsFiredThisTick > 0,
                    gun.ShotSequence);
            }
        }
    }

    /// <summary>Where a vehicle gun's mount stands and faces, re-seated on its parent (<c>0x46FBA0</c>).</summary>
    /// <remarks>
    /// The mount sits at its attachment offset in its parent's frame and turns by its traverse about its own up
    /// axis: parent · attachment · Z(traverse), rows dotted into columns. A gun on a gun (a T80's coaxial M2HB on
    /// its turret) is re-seated on the turned mount, so it swings round with the turret.
    /// </remarks>
    private bool TryGetVehicleGunMount(
        GameTask vehicle,
        VehicleArmament armament,
        int index,
        out Vector3d position,
        out Matrix3f orientation)
    {
        VehicleGun gun = armament.Guns[index].Gun;
        Vector3d parentPosition;
        Matrix3f parentOrientation;
        if (gun.Parent >= 0 && gun.Parent < index)
        {
            if (!TryGetVehicleGunMount(vehicle, armament, gun.Parent, out parentPosition, out parentOrientation))
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
        }
        else if (_taskTransforms.TryGetValue(vehicle, out TransformState? state))
        {
            UpdateDerivedPose(state);
            parentPosition = state.Position;
            parentOrientation = state.Orientation;
        }
        else
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        (position, orientation) = PoseVehicleGunMount(armament.Guns[index], parentPosition, parentOrientation);
        return true;
    }

    /// <summary>Re-seats every gun of a vehicle where it stands now (<c>0x46FBA0</c>, then <c>0x470050</c>).</summary>
    /// <remarks>A gun's parent comes before it in the list, so each gun is seated on its parent's new pose.</remarks>
    private static void SeatVehicleGuns(VehicleArmament armament, TransformState transform)
    {
        for (int index = 0; index < armament.Guns.Count; index++)
        {
            VehicleGunState gun = armament.Guns[index];
            int parent = gun.Gun.Parent;
            (Vector3d mountPosition, Matrix3f mountOrientation) = parent >= 0 && parent < index
                ? PoseVehicleGunMount(gun, armament.Guns[parent].MountPosition, armament.Guns[parent].MountOrientation)
                : PoseVehicleGunMount(gun, transform.Position, transform.Orientation);
            gun.MountPosition = mountPosition;
            gun.MountOrientation = mountOrientation;
            (gun.WeaponPosition, gun.WeaponOrientation) =
                PoseVehicleGunWeapon(gun, mountPosition, mountOrientation, transform.Orientation);
        }

        armament.Seated = true;
    }

    /// <summary>A mount's pose on its parent: parent · attachment · Z(traverse) (<c>0x46FBA0</c>).</summary>
    private static (Vector3d Position, Matrix3f Orientation) PoseVehicleGunMount(
        VehicleGunState gun,
        Vector3d parentPosition,
        Matrix3f parentOrientation)
    {
        Vector3f local = gun.Gun.AttachmentPosition;
        return (
            parentPosition + parentOrientation.Transposed().Transform(new Vector3d(local.X, local.Y, local.Z)),
            parentOrientation * gun.Gun.AttachmentOrientation * Matrix3f.RotationZ(gun.Turret.Yaw));
    }

    /// <summary>A weapon object's pose on its mount (<c>0x470050</c>).</summary>
    private static (Vector3d Position, Matrix3f Orientation) PoseVehicleGunWeapon(
        VehicleGunState gun,
        Vector3d mountPosition,
        Matrix3f mountOrientation,
        Matrix3f vehicleOrientation)
    {
        Vector3f vertex = gun.Gun.WeaponVertex;
        Matrix3f orientation = vehicleOrientation * Matrix3f.FromEngineEulerAngles(gun.Turret.Pitch, 0f, gun.Turret.Yaw);
        if (gun.Turret.Spin != 0f)
        {
            orientation *= Matrix3f.RotationY(gun.Turret.Spin);
        }

        return (mountPosition + mountOrientation.Transposed().Transform(new Vector3d(vertex.X, vertex.Y, vertex.Z)), orientation);
    }

    /// <summary>Where a vehicle gun's weapon object sits and faces (<c>0x470050</c>).</summary>
    /// <remarks>
    /// On the mount's weapon vertex, turned with the mount; faced by the vehicle's own orientation times
    /// <c>0x4B38E0(elevation, 0, traverse)</c>, then by Y(spin) once the barrels have spun. The traverse and
    /// elevation are measured in the vehicle's frame, so the mount's own attachment turn plays no part.
    /// </remarks>
    private bool TryGetVehicleGunWeapon(
        GameTask vehicle,
        VehicleArmament armament,
        int index,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (!TryGetVehicleGunMount(vehicle, armament, index, out Vector3d mountPosition, out Matrix3f mountOrientation)
            || !_taskTransforms.TryGetValue(vehicle, out TransformState? state))
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        (position, orientation) = PoseVehicleGunWeapon(armament.Guns[index], mountPosition, mountOrientation, state.Orientation);
        return true;
    }

    /// <summary>Builds the roll and steer a car's drawn wheel carries this tick.</summary>
    /// <param name="placement">The candidate placement.</param>
    /// <param name="rotation">Receives the wheel's own rotation, in its placement frame.</param>
    /// <returns><see langword="false"/> when the placement is not a car wheel.</returns>
    /// <remarks>
    /// A wheel rolls about its across axis by the distance the body has covered over the wheel
    /// radius, and a steered wheel turns about the body's up axis by
    /// <c>MaximumSteerDegrees * pi/180 * steering</c> -- the same angle <c>0x430D10</c> builds the
    /// drive direction with. Without this the wheels are welded to the hull and the truck slides
    /// along on frozen tyres.
    /// </remarks>
    public bool TryGetWheelDraw(WorldPlacement placement, out Matrix3f rotation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        rotation = Matrix3f.Identity;
        if (!string.Equals(placement.MagicTaskTypeName, WheelTaskTypeName, StringComparison.Ordinal)
            || !_carWheels.TryGetValue(placement.Owner, out CarWheel[]? wheels)
            || !_taskTransforms.TryGetValue(placement.Owner, out TransformState? state))
        {
            return false;
        }

        bool steerable = _carWheelIndices.TryGetValue(placement, out int index)
            && index < wheels.Length
            && wheels[index].Definition.Steerable;
        // Retail's accumulator counts DOWN as the car goes forward (Wheel::Tick subtracts the spin
        // rate), and it is applied against the wheel's own axle axis, which points the opposite way
        // from the placement frame's X here. Rolling by the raw accumulator spins the tyres
        // backwards while the truck drives forwards.
        rotation = Matrix3f.RotationX((float)-state.CarMotion.WheelRoll);
        if (steerable && state.CarMotion.WheelSteer != 0d)
        {
            rotation = Matrix3f.RotationZ((float)state.CarMotion.WheelSteer) * rotation;
        }

        return true;
    }

    /// <summary>Builds the repeated orientations emitted by retail <c>RotorPrimitive::Draw</c>.</summary>
    public bool TryGetRotorDrawState(WorldPlacement placement, out RotorDrawState draw)
    {
        if (!placement.IsRotor
            || _vehiclePhysics?.TryGetRotor(placement.Instance.Name, out RotorPhysicsDefinition rotor) != true
            || !_taskTransforms.TryGetValue(placement.Owner, out TransformState? state)
            || !TryGetPlacementDrawTransform(
                placement.Owner,
                placement.Position,
                placement.Orientation,
                out Vector3d position,
                out Matrix3f baseOrientation))
        {
            draw = default;
            return false;
        }

        // Rotor::Tick (0x42D440) advances its private phase by
        // BladeSamples * PhaseStep * collective. RotorPrimitive::Draw (0x42C630) then converts that
        // accumulator with the engine's 21.362831 factor and emits BladeSamples copies, separated by
        // PhaseStep * collective. The repeated blur meshes are why the main and tail rotors look like
        // discs in retail instead of the single frozen blade that the initial renderer produced.
        // A terminal landing parks the physical airframe and clears its live thrust/phase. The
        // authored helicopter still has its visible rotor presentation in retail/editor previews,
        // though: keep a draw-only phase running from the original collective while the body remains
        // stationary. This does not feed lift or torque back into the landed physics state.
        bool previewOnly = !state.IsDynamic || state.TerminalLandingLatched;
        float visualCollective = previewOnly && state.OriginalThrust > 0f
            ? Math.Clamp(state.OriginalThrust, 0f, 1f)
            : state.Thrust;
        float firstAngle;
        if (previewOnly && visualCollective > 0f)
        {
            // The editor preview advances at the visible retail rates (15 rad/s for the main disc,
            // 25 rad/s in the opposite direction for a tail disc). A parked/terminal Heli has no
            // live physics phase to consume, so use the runtime tick as the presentation clock.
            float angularSpeed = rotor.IsTailRotor ? -25f : 15f;
            firstAngle = (SimulationTick / 30f) * angularSpeed * visualCollective;
        }
        else
        {
            // Rotor tasks keep ticking when the body's AnimTask stops emitting channels.
            // Extend the last physics phase through those idle ticks without moving the body.
            float phase = state.TerminalLandingLatched
                ? state.PhysicsTicks * visualCollective
                : state.RotorPhase + (Math.Max(0, SimulationTick - state.RotorPhaseTick) * visualCollective);
            float accumulatedPhase = phase * rotor.PhasePerCollectiveTick;
            firstAngle = accumulatedPhase * 21.362831f;
        }
        float sampleStep = rotor.PhaseStep * visualCollective;

        float maximumTilt = rotor.MaximumTilt;
        float tiltCollective = previewOnly ? visualCollective : state.Thrust;
        float tilt = -Math.Clamp(1f - tiltCollective, -0.1f, 1f) * maximumTilt;

        // The second half of the flap is the airframe's AXIAL speed: the component of the body
        // velocity along its own vertical. 0x42C630 reads that vertical out of the Heli's live
        // matrix at config +120/+132/+144 — the third column of the +112 basis, the same body-up
        // vector Rotor::Tick already uses for lift. Column1 is the body's sideways axis, so the
        // flap followed lateral drift instead of climb and descent.
        //
        // 0.026367188 is 108/4096: it turns the velocity's units-per-tick into km/h, the unit the
        // record's maximum speed is authored in. Retail divides by that speed times 0.4 (80 km/h
        // for both shipped families), so the blade needs a sustained climb to reach the authored
        // five degrees. Dividing by the live collective instead made the term hundreds of times
        // too large: any movement pinned the disc at the full deflection and flipped its sign with
        // the drift, which is what turned the blur samples into an umbrella and then inverted it.
        float referenceSpeed = state.HelicopterPhysics?.MaximumSpeed ?? 0f;
        if (maximumTilt != 0f && referenceSpeed != 0f)
        {
            Vector3f vertical = state.Orientation.Column3;
            float axialVelocity =
                (vertical.X * (float)state.Velocity.X)
                + (vertical.Y * (float)state.Velocity.Y)
                + (vertical.Z * (float)state.Velocity.Z);
            tilt -= axialVelocity * 0.026367188f * maximumTilt / (referenceSpeed * 0.4f);
        }

        tilt = Math.Clamp(tilt, -maximumTilt, maximumTilt);

        draw = new RotorDrawState(
            position,
            baseOrientation,
            Math.Max(0, rotor.BladeSamples),
            firstAngle,
            sampleStep,
            tilt);
        return true;
    }

    /// <summary>Resolves a cinematic actor staged inside a moving vehicle.</summary>
    public bool TryGetAttachedDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (!TryEvaluateAttachment(task, out Vector3d attachedPosition, out orientation))
        {
            position = default;
            return false;
        }

        position = attachedPosition / WorldScene.UnitsPerMetre;
        return true;
    }

    /// <summary>Parents a cinematic Human to the moving vehicle surface it has just landed on.</summary>
    /// <param name="task">The Human task whose live transform is being parented.</param>
    /// <param name="parent">The exact placed vehicle task returned by the ground collision.</param>
    /// <param name="position">The Human's current world position, in engine units.</param>
    /// <param name="orientation">The Human's current world orientation.</param>
    /// <returns><see langword="true"/> when the parent has a live cutscene transform.</returns>
    /// <remarks>
    /// Retail's Human ground pass tests the struck collision task's moving-platform flag and parents
    /// the Human to that exact task. The aligned native helper at <c>0x463310</c> writes the parent
    /// pointer and the Human's local position/orientation frame; its real one-block boundary is
    /// confirmed in both Ghidra and r2.
    /// The paired detach helper at <c>0x463550</c> clears <c>Human+0x76c</c> when the ground pass has
    /// no valid moving contact. The task-tree ancestry of the Human is unrelated: Mission 1 actor
    /// 1666 is a sibling of train car 1888, not its child.
    /// </remarks>
    public bool TryAttachActorToTask(
        GameTask task,
        GameTask parent,
        Vector3d position,
        Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(parent);

        if (!IsVehicle(parent)
            || !_taskTransforms.TryGetValue(parent, out TransformState? parentTransform))
        {
            return false;
        }

        if (_attachments.TryGetValue(task, out AttachmentState? existing)
            && ReferenceEquals(existing.Parent, parent))
        {
            return true;
        }

        if (!_taskTransforms.TryGetValue(task, out TransformState? state))
        {
            state = new TransformState(position, orientation);
            Remember(task, state);
        }
        else
        {
            state.Position = position;
            state.Orientation = orientation;
        }

        return CaptureAttachment(task, state, parent, parentTransform);
    }

    /// <summary>Reads the moving parent a cinematic Human is currently attached to, if any.</summary>
    /// <remarks>
    /// Observation only: it never selects or changes the parent. Regressions use it to assert
    /// the live contact selection without supplying an expected owner first.
    /// </remarks>
    public bool TryGetAttachmentParent(GameTask task, out GameTask? parent)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_attachments.TryGetValue(task, out AttachmentState? attachment))
        {
            parent = attachment.ParentTask;
            return true;
        }

        parent = null;
        return false;
    }

    /// <summary>
    /// Clears a cinematic Human's moving-parent slot when its contacted owner has been unregistered.
    /// </summary>
    /// <remarks>
    /// Native <c>Human_GroundCollision</c> calls the aligned detach helper at <c>0x463550</c> when a
    /// candidate owner is no longer active; an empty query only clears support fields and deliberately
    /// retains an existing parent. The latter distinction matters during the Level 1 helicopter-to-train
    /// handoff, so this method checks only the parent's registration state and never guesses a replacement.
    /// </remarks>
    public bool TryDetachInactiveAttachment(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!_attachments.TryGetValue(task, out AttachmentState? attachment)
            || ConditionalContainerGate.IsTaskActive(attachment.ParentTask))
        {
            return false;
        }

        _attachments.Remove(task);
        return true;
    }

    /// <summary>Consumes one Human root-motion delta while the Human is standing on a moving parent.</summary>
    /// <remarks>
    /// Every retail Human state first rotates <c>Human+1312</c> through the Human orientation (for
    /// example <c>0x4551A4</c>). <c>Human_AttachedMovement</c> (<c>0x462CF0</c>) then converts that
    /// world-space delta into the parent's local frame and accumulates it at <c>Human+1904</c>.
    /// Keeping both transforms in this order is what makes an actor follow its own facing while staying
    /// welded to a moving vehicle.
    /// </remarks>
    public bool AdvanceAttachedActor(GameTask task, Vector3f rootMotion)
    {
        if (!_attachments.TryGetValue(task, out AttachmentState? attachment))
        {
            return false;
        }

        if (rootMotion.X == 0f && rootMotion.Y == 0f && rootMotion.Z == 0f)
        {
            return true;
        }

        TryEvaluateAttachment(task, out _, out Matrix3f humanOrientation);
        EvaluateAttachmentParentPose(task, attachment, out _, out Matrix3f parentOrientation);
        // Human's state tick (IGI.exe 0x4550B0) first turns the clip delta through Human+120,
        // producing Human+1312 in world axes. Human_AttachedMovement (0x462CF0, named Human_Attach
        // in the IGI2 symbols) then turns it through the parent's Human+112 frame into Human+1904.
        Vector3d worldDelta = humanOrientation.Transposed().Transform(new Vector3d(
            rootMotion.X, rootMotion.Y, rootMotion.Z));
        Vector3d localDelta = parentOrientation.Transform(worldDelta);
        attachment.RelativePosition += localDelta;
        return true;
    }

    private bool TryEvaluateAttachment(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (!_attachments.TryGetValue(task, out AttachmentState? attachment))
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        EvaluateAttachmentParentPose(
            task,
            attachment,
            out Vector3d parentPosition,
            out Matrix3f parentOrientation);
        position = parentPosition
            + parentOrientation.Transposed().Transform(attachment.RelativePosition);
        orientation = parentOrientation
            * attachment.ParentOriginOrientation.Transposed()
            * attachment.Orientation;
        if (_taskTransforms.TryGetValue(task, out TransformState? state))
        {
            state.Position = position;
            state.Orientation = orientation;
        }
        return true;
    }

    private void EvaluateAttachmentParentPose(
        GameTask task,
        AttachmentState attachment,
        out Vector3d position,
        out Matrix3f orientation)
    {
        // Human_AttachedMovement reads its parent's pose at the Human's own task-tree slot. Our grouped
        // scheduler advances every AnimTask first, so reconstruct that slot: a Human preceding the
        // controlling AnimTask sees the saved pre-update pose, while one following it sees current.
        // Mission 1 contains both source-of-truth cases: wagon Human 2021 precedes train-1999's AnimTask;
        // helicopter Human 1505 follows heli-1502's AnimTask.
        TransformState dynamicParent = attachment.Parent.TrainRoot ?? attachment.Parent;
        bool usePreviousPose = task is HumanSoldierTask
            && DynamicRevision(attachment.Parent) > attachment.ParentRevisionAtCapture
            && _taskOrder.TryGetValue(task, out int humanOrder)
            && humanOrder < dynamicParent.LastSimulationOrder;
        EvaluateDynamicPose(attachment.Parent, usePreviousPose, out position, out orientation);
    }

    private void RefreshContainers(bool executeTransitions)
    {
        foreach (ConditionalContainerTask container in _containers)
        {
            // Mission 4's exact retail gate is AreaActivate_156.nActive && !HumanPlayer_0.isDead. When a
            // friendly is its sole occupant, opening container 3902 unregisters gameplay container 3901
            // (and that friendly) on the next tick. Scene 2222 already owns the camera at that point, so
            // retain this one authored gate until its Run state releases the scene. Keep the exception
            // deliberately exact: other levels combine areas with nested conditions that must be re-run.
            if (RetainsLevelFourOutro(container))
            {
                continue;
            }

            bool changed = container.Refresh(out bool started);
            container.IsActive = container.IsRun;
            if (!changed || !executeTransitions)
            {
                continue;
            }

            _expressions.Execute(started ? container.RunAtStart : container.RunAtStop, container);
        }
    }

    private bool RetainsLevelFourOutro(ConditionalContainerTask container)
    {
        if (_breakRequested
            || !container.IsRun
            || container.Id != 3902
            || !string.Equals(
                container.Condition.Trim(),
                "AreaActivate_156.nActive && !HumanPlayer_0.isDead",
                StringComparison.Ordinal)
            || !_expressions.EvaluateCondition("!HumanPlayer_0.isDead", emptyValue: true))
        {
            return false;
        }

        foreach (SceneState scene in _scenes)
        {
            // IsRun deliberately remains true on the completion-boundary frame. Retaining the parent for
            // that frame keeps the final authored camera available; the following scene tick clears IsRun.
            if (scene.Task.Id != 2222 || !scene.Task.IsRun)
            {
                continue;
            }

            for (GameTask? ancestor = scene.Task.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, container))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ExecuteSceneExpression(CutSceneTask scene, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        _expressions.Execute(expression, scene);
    }

    private QvmValue? InvokeNative(string name, ScriptArguments arguments)
    {
        if (string.Equals(name, "LevelFlow_GetBreakCutSceneKey", StringComparison.Ordinal))
        {
            return QvmValue.FromInt32(_breakRequested ? 1 : 0);
        }

        return _hostNative?.Invoke(name, arguments) ?? name switch
        {
            // These native calls are side effects owned by the desktop host. Returning success keeps
            // boolean expression chains faithful even in a headless runtime.
            "Game_CutsceneDelete" or "MenuManager_SetEnabled" or "Game_DisableMusic" or "Game_EnableMusic"
                => QvmValue.FromInt32(1),
            _ => null,
        };
    }

    private void UpdateFrame()
    {
        SceneState? state = _scenes.LastOrDefault(
            static item => item.Task.IsRun && ConditionalContainerGate.IsTaskActive(item.Task));
        if (state is null || state.Shots.Count == 0)
        {
            Frame = null;
            return;
        }

        CutsceneFrame? frame = BuildFrame(state, state.Task.Ticks / (double)Frequency);
        Frame = frame is null
            ? null
            : frame with { BinocularOverlayVisible = IsBinocularOverlayVisible() };
    }

    private bool IsBinocularOverlayVisible()
    {
        foreach (DeclaredTask holder in _binocularOverlayHolders)
        {
            if (ConditionalContainerGate.IsTaskActive(holder)
                && _expressions.EvaluateCondition(
                    Text(holder, "Visible expression"), emptyValue: false, holder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Interpolates one scene's authored camera at an arbitrary scene time.</summary>
    private CutsceneFrame? BuildFrame(SceneState state, double time)
    {
        CutSceneTask scene = state.Task;
        List<TimedShot> timed = state.Timed(scene.TimeScale);
        // IGI1 0x4F4880 retains every authored knot, including duration zero.
        // 0x4F5400 selects the last start <= the scene time without a duration filter.
        // Scene completion/presentation must not be repaired by changing knot ownership.
        int current = 0;
        for (int i = 1; i < timed.Count; i++)
        {
            if (timed[i].Start > time)
            {
                break;
            }
            current = i;
        }

        int i0 = Math.Max(0, current - 1);
        int i1 = current;
        int i2 = Math.Min(timed.Count - 1, current + 1);
        int i3 = Math.Min(timed.Count - 1, current + 2);

        TimedShot p0 = timed[i0];
        TimedShot p1 = timed[i1];
        TimedShot p2 = timed[i2];
        TimedShot p3 = timed[i3];
        if (!p0.Shot.SmoothToNext) { p0 = p1; }
        if (!p1.Shot.SmoothToNext) { p2 = p1; }
        if (!p2.Shot.SmoothToNext) { p3 = p2; }

        Vector3d position = new(
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.X, p1.Shot.Position.X, p2.Shot.Position.X, p3.Shot.Position.X),
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.Y, p1.Shot.Position.Y, p2.Shot.Position.Y, p3.Shot.Position.Y),
            Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Position.Z, p1.Shot.Position.Z, p2.Shot.Position.Z, p3.Shot.Position.Z));
        float alpha = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Alpha, p1.Shot.Alpha, p2.Shot.Alpha, p3.Shot.Alpha);
        float beta = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Beta, p1.Shot.Beta, p2.Shot.Beta, p3.Shot.Beta);
        float gamma = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Gamma, p1.Shot.Gamma, p2.Shot.Gamma, p3.Shot.Gamma);
        float fov = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.FieldOfView, p1.Shot.FieldOfView, p2.Shot.FieldOfView, p3.Shot.FieldOfView);
        float shake = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start, p0.Shot.Shake, p1.Shot.Shake, p2.Shot.Shake, p3.Shot.Shake);
        float noise = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start,
            p0.Shot.Noise, p1.Shot.Noise, p2.Shot.Noise, p3.Shot.Noise);
        static float ShotTimeOfDay(EditCameraShot shot, CutSceneTask owner) =>
            shot.TimeOfDay < 0f ? owner.TimeOfDay : shot.TimeOfDay;
        float timeOfDay = (float)Hermite(time, p0.Start, p1.Start, p2.Start, p3.Start,
            ShotTimeOfDay(p0.Shot, scene), ShotTimeOfDay(p1.Shot, scene),
            ShotTimeOfDay(p2.Shot, scene), ShotTimeOfDay(p3.Shot, scene));
        Matrix3f orientation = Matrix3f.FromEngineEulerAngles(alpha, beta, gamma);

        EditCameraShot active = timed[current].Shot;
        bool shotChanged = state.LastShotIndex != current;
        if (active.LinkTaskId >= 0 && TryPose(active.LinkTaskId, out TransformState? link))
        {
            if (shotChanged || active.UpdateLink || state.LinkedPosition is null)
            {
                // Retail sub_508940 first maps the authored world offset through the cached link
                // orientation, then maps that local offset through the transpose of the live link.
                // Matrix3f.Transform is the engine's row-vector multiply, so both transposes here are
                // significant. Reversing them makes a linked cinematic camera orbit on the wrong axes.
                Vector3d relative = active.LinkOriginOrientation.Transform(position - active.LinkOrigin);
                position = link!.Position + link.Orientation.Transposed().Transform(relative);
                orientation = link.Orientation * active.LinkOriginOrientation.Transposed() * orientation;
                state.LinkedPosition = position;
                state.LinkedOrientation = orientation;
            }
            else
            {
                position = state.LinkedPosition.Value;
                orientation = state.LinkedOrientation ?? orientation;
            }
        }
        if (active.TargetTaskId >= 0 && TryPose(active.TargetTaskId, out TransformState? target))
        {
            if (shotChanged || active.UpdateTarget || state.TargetOrientation is null)
            {
                orientation = LookAt(position, target!.Position);
                state.TargetOrientation = orientation;
            }
            else
            {
                orientation = state.TargetOrientation.Value;
            }
        }
        state.LastShotIndex = current;

        // Retail uses its deterministic RNG here. A tick-derived hash gives the same important property:
        // every replay of a scene shakes identically and rendering the same frame never mutates logic.
        if (shake != 0f)
        {
            uint seed = unchecked((uint)((scene.Id * 397) ^ scene.Ticks));
            position += new Vector3d(Noise(ref seed), Noise(ref seed), Noise(ref seed)) * (shake * WorldScene.UnitsPerMetre);
            orientation = Matrix3f.FromEngineEulerAngles(
                Noise(ref seed) * shake * 0.53407079f,
                Noise(ref seed) * shake * 0.53407079f,
                Noise(ref seed) * shake * 0.53407079f) * orientation;
        }

        double total = state.TotalTime(scene.TimeScale);
        float fadeIn = scene.ViewportFadeIn > 0f ? Math.Clamp((float)(time / scene.ViewportFadeIn), 0f, 1f) : 1f;
        float fadeOut = scene.ViewportFadeOut > 0f ? Math.Clamp((float)((total - time) / scene.ViewportFadeOut), 0f, 1f) : 1f;
        float fade = Math.Min(fadeIn, fadeOut);
        float viewportHeight = 1f + ((scene.ViewportHeightFactor - 1f) * fade);
        float local = p2.Start > p1.Start ? Math.Clamp((float)((time - p1.Start) / (p2.Start - p1.Start)), 0f, 1f) : 0f;
        float colorLocal = local;
        if (noise == 0f && IsBandFilter(active.Filter) && p2.Start - p1.Start > 0.5)
        {
            // DrawBandFilter completes its colour transition half a second before a long zero-noise
            // segment ends (0x4F5E20), leaving the final half-second as a settled hold.
            colorLocal = Math.Clamp(
                (float)((time - p1.Start) / ((p2.Start - p1.Start) - 0.5)), 0f, 1f);
        }

        return new CutsceneFrame(
            // EditCamera keeps a proper engine rotation (determinant +1), whereas Camera3D's left-handed
            // right/up/forward view basis has determinant -1. Its columns become our rows and the up row
            // changes sign at that boundary. A transpose alone points the cinematic camera's up axis down,
            // putting the landscape overhead and exposing the terrain skirts seen in the broken intro.
            new Camera3D(position / WorldScene.UnitsPerMetre, ToCameraOrientation(orientation)),
            fov,
            Math.Clamp(viewportHeight, 0f, 1f),
            active.Filter,
            noise,
            Lerp(active.FilterColor, timed[i2].Shot.FilterColor, colorLocal),
            timeOfDay,
            scene,
            current);
    }

    private void CaptureTransform(GameTask task)
    {
        if (task is HumanSoldierTask soldier)
        {
            // Cabin copies ship with HumanSoldier id -1; the mind
            // id is on the child HumanAI. Skipping NoId left them unparented so they froze
            // while the airframe left. Remember by task reference and attach via QVM parent.
            Matrix3f orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma);
            TransformState humanState = new(soldier.Position, orientation);
            Remember(task, humanState);
            CaptureAttachment(task, humanState);
            return;
        }

        if (task is not DeclaredTask declared)
        {
            return;
        }

        if (string.Equals(task.Type.Name, "Train", StringComparison.Ordinal)
            && RailroadPath.FromTrain(declared) is { } railroad)
        {
            if (task.Parent is DeclaredTask parent
                && string.Equals(parent.Type.Name, "Train", StringComparison.Ordinal)
                && _taskTransforms.TryGetValue(parent, out TransformState? parentTrain)
                && parentTrain.TrainRoot is { Railroad: { } rootRailroad } root)
            {
                float parentLength = Math.Max(0f, _modelLength?.Invoke(Text(parent, "Model")) ?? 0f);
                float parentPivot = Math.Max(0f, _modelPivot?.Invoke(Text(parent, "Model")) ?? 0f);
                // Child Train records leave Flip Direction at their default.  The consist is one
                // railroad body, so spacing and orientation inherit the root locomotive's frame.
                float consistDirection = rootRailroad.Flipped ? 1f : -1f;
                float frontOffset = _trainTailOffsets.TryGetValue(parent, out float tail)
                    ? tail
                    : parentTrain.TrainOffset + (consistDirection * parentLength * (1f - parentPivot));
                float childLength = Math.Max(0f, _modelLength?.Invoke(Text(declared, "Model")) ?? 0f);
                float childPivot = Math.Max(0f, _modelPivot?.Invoke(Text(declared, "Model")) ?? 0f);
                // Retail's Train::UpdateConsist (0x42A2C0/0x42A7C0) asks the railroad solver for
                // the two coupler samples bounding each car, then places the model origin at
                // lerp(front, rear, abs(magicVertex0.x) / couplerDistance).  TrainOffset therefore
                // identifies the model pivot, while the tail map keeps the next coupler boundary.
                float offset = frontOffset + (consistDirection * childLength * childPivot);
                rootRailroad.Evaluate(root.TrackPosition + offset, out Vector3d childPosition, out Matrix3f childOrientation);
                TransformState childState = new(childPosition, childOrientation)
                {
                    Railroad = rootRailroad,
                    TrainRoot = root,
                    TrainOffset = offset,
                };
                Remember(task, childState);
                float tailOffset = frontOffset + (consistDirection * childLength);
                _trainTailOffsets[parent] = tailOffset;
                _trainTailOffsets[task] = tailOffset;
                return;
            }

            float rootLength = Math.Max(0f, _modelLength?.Invoke(Text(declared, "Model")) ?? 0f);
            float rootPivot = Math.Max(0f, _modelPivot?.Invoke(Text(declared, "Model")) ?? 0f);
            float direction = railroad.Flipped ? 1f : -1f;
            float rootOffset = direction * rootLength * rootPivot;
            railroad.Evaluate(railroad.InitialPosition + rootOffset, out Vector3d railPosition, out Matrix3f railOrientation);
            TransformState trainState = new(railPosition, railOrientation)
            {
                Railroad = railroad,
                TrackPosition = railroad.InitialPosition,
                PreviousTrackPosition = railroad.InitialPosition,
                TrainOffset = rootOffset,
                Thrust = RealNamed(declared, "Original thrust"),
                TargetThrust = RealNamed(declared, "Original thrust"),
                OriginalThrust = RealNamed(declared, "Original thrust"),
                ThrustStep = MathF.Abs(RealNamed(declared, "ThrustStep")),
                MaxSpeed = RealNamed(declared, "MaxSpeed"),
            };
            trainState.TrainRoot = trainState;
            Remember(task, trainState);
            _trainTailOffsets[task] = direction * rootLength;
            return;
        }

        Vector3d position = Vector(declared, "Position");
        Vector3f angles = Vector32(declared, "Orientation");
        TransformState state = new(position, Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z))
        {
            // Vehicle AnimTask channels are deltas/controls. Preserve the authored starting attitude
            // when the first wait entry advances; beginning this accumulator at zero snapped linked
            // helicopters and planes to the identity matrix on cutscene tick one.
            Euler = angles,
            Heading = angles.Z,
        };
        if (string.Equals(task.Type.Name, "Car", StringComparison.Ordinal)
            && _vehiclePhysics?.TryGetCar(Text(declared, WorldScene.ModelParameter), out CarPhysicsDefinition car) == true)
        {
            state.CarPhysics = car;
            state.Steering = 0f;
            state.TargetSteering = 0f;

            // Car::ReadChannels restores both the live throttle and its target from this authored
            // value on animation tick zero, the way the Heli and Train receivers do. Mission 2's
            // truck opens its cutscene already carrying 0.6 and coasts down from it.
            state.OriginalThrust = RealNamed(declared, "Original thrust");
            state.Thrust = state.OriginalThrust;
            state.TargetThrust = state.OriginalThrust;
        }
        if (string.Equals(task.Type.Name, "Plane", StringComparison.Ordinal)
            && _vehiclePhysics?.TryGetPlane(Text(declared, WorldScene.ModelParameter), out PlanePhysicsDefinition plane) == true)
        {
            state.PlanePhysics = plane;
        }
        if (string.Equals(task.Type.Name, "Heli", StringComparison.Ordinal))
        {
            // Heli::ReadChannels restores both live and target collective from this authored value on
            // animation tick zero. It then writes -1 into channel three so the value remains latched.
            state.Thrust = RealNamed(declared, "Original Thrust");
            state.TargetThrust = state.Thrust;
            state.OriginalThrust = state.Thrust;
            string model = Text(declared, WorldScene.ModelParameter);
            if (_vehiclePhysics?.TryGetHelicopter(model, out HelicopterPhysicsDefinition physics) == true)
            {
                state.HelicopterPhysics = physics;
            }

            // The retail Heli live-update callback begins at 0x431B70 and invokes the validated
            // force helper 0x431E30. The authored collision flag is carried into the live body;
            // the contact solver below uses it only to prevent terrain penetration. The QVM
            // collective and cyclic controls remain free to lift and steer the airframe.
            state.CollisionEnabled = Bool(declared, "Collision detection");
        }
        bool attached = CaptureAttachment(task, state);
        if (task.Id != GameTask.NoId || attached)
        {
            Remember(task, state);
        }
    }

    private bool CaptureAttachment(GameTask task, TransformState state)
    {
        for (GameTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (IsVehicle(parent)
                && _taskTransforms.TryGetValue(parent, out TransformState? parentTransform))
            {
                return CaptureAttachment(task, state, parent, parentTransform);
            }
        }
        return false;
    }

    private bool CaptureAttachment(
        GameTask task,
        TransformState state,
        GameTask parent,
        TransformState parentTransform)
    {
        UpdateDerivedPose(parentTransform);
        AttachmentState attachment = new(
            parent,
            parentTransform,
            DynamicRevision(parentTransform),
            parentTransform.Position,
            parentTransform.Orientation,
            state.Position,
            state.Orientation);
        _attachments[task] = attachment;
        if (task is DeclaredTask attached
            && _vehiclePhysics?.TryGetRotor(
                Text(attached, WorldScene.ModelParameter), out RotorPhysicsDefinition rotor) == true)
        {
            if (rotor.ProducesLift)
            {
                parentTransform.RotorAxialCoefficient += 2.5f;
            }
            if (rotor.IsTailRotor)
            {
                parentTransform.RotorAxialCoefficient -= 2.5f;
            }
        }
        return true;
    }

    private static bool IsVehicle(GameTask task) => CinematicActor.IsVehicle(task);

    private void Remember(GameTask task, TransformState state)
    {
        _taskTransforms[task] = state;
        if (task.Id != GameTask.NoId)
        {
            _transforms[task.Id] = state;
        }
    }

    /// <summary>Creates or resets a car's retail body at its placement -- the set-up packet's reset (<c>0x42EA10</c> mode 0).</summary>
    private void ResetRetailBody(GameTask? task, TransformState state)
    {
        if (_retailCarWorld is null
            || task is not DeclaredTask declared
            || state.CarPhysics is not { } physics)
        {
            return;
        }

        int wheelAttachments = _carWheels.TryGetValue(task, out CarWheel[]? wheels) ? wheels.Length : 0;
        RetailCarPlacement placement = RetailCarSpawn.Placement(declared, wheelAttachments);
        if (state.RetailBody is { } body)
        {
            RetailCarSpawn.Reset(body, placement);
        }
        else if (_retailCarModels?.Invoke(Text(declared, WorldScene.ModelParameter)) is { } model)
        {
            RetailCarBody created = RetailCarSpawn.Create(physics, model, placement);
            created.Owner = task;
            if (OpenIGI.Formats.World.MefCollisionMesh.TryRead(model.Container, meshIndex: 0, out OpenIGI.Formats.World.MefCollisionMesh? mesh)
                && mesh is { Faces.Length: > 0 })
            {
                created.RayModel = new RetailRayModel(task, new RetailRayMesh(mesh), model.NativeMarkerBound);
            }

            state.RetailBody = created;
        }
        else
        {
            return;
        }

        CopyRetailPose(state, state.RetailBody);
        state.PreviousPosition = state.Position;
        state.PreviousOrientation = state.Orientation;
    }

    /// <summary>One physics packet on the retail body: the channels as <c>0x42EA10</c> latches them, then <c>Car_Tick</c>.</summary>
    private void DriveRetailCar(
        GameTask? target,
        TransformState transform,
        RetailCarBody body,
        IRetailCarWorld world,
        float channel0,
        float channel1,
        float channel3)
    {
        body.SteeringTarget = channel0;
        if (channel1 != -1f)
        {
            body.ThrottleTarget = channel1;
        }

        body.Handbrake = channel3 != -1f;
        body.Tick(world);

        CopyRetailPose(transform, body);
        transform.TargetSteering = body.SteeringTarget;
        transform.TargetThrust = body.ThrottleTarget;
        transform.Steering = body.Steering;
        transform.Thrust = body.Throttle;
        transform.ParkingBrake = body.Handbrake;
        transform.Speed = body.Speed;
        transform.Velocity = transform.Position - transform.PreviousPosition;
        transform.CarMotion.Velocity = new Vector3d(body.Velocity[0], body.Velocity[1], body.Velocity[2]);
        if (transform.CarPhysics is { } physics)
        {
            CarDynamics.AnimateWheels(
                transform.CarMotion,
                physics,
                CarWheelsFor(target, physics),
                transform.Velocity.Length,
                body.Steering,
                body.Handbrake);
        }

        transform.IsDynamic = true;
    }

    /// <summary>
    /// The vehicles rays can strike, each at its body's current pose: every car whose retail body exists and whose
    /// task is in the world.
    /// </summary>
    /// <returns>The vehicles' ray models.</returns>
    public IReadOnlyList<RetailRayModel> RetailVehicleModels()
    {
        _retailVehicleModels.Clear();
        foreach ((GameTask task, TransformState state) in _taskTransforms)
        {
            if (state.CarPhysics is null || !ConditionalContainerGate.IsTaskActive(task))
            {
                continue;
            }

            if (state.RetailBody is { RayModel: { } model } body)
            {
                Array.Copy(body.Position, model.Position, 3);
                Array.Copy(body.M, model.M, 9);
                _retailVehicleModels.Add(model);
            }
            else if (state.RetailBody is null && StandingCarModel(task) is { } standing)
            {
                // A car no controller has reset yet -- or never will, like mission 7's stand-in T80 361 -- stands
                // in the object tree where the level placed it.
                _retailVehicleModels.Add(standing);
            }
        }

        return _retailVehicleModels;
    }

    private RetailRayModel? StandingCarModel(GameTask task)
    {
        if (_standingCars.TryGetValue(task, out RetailRayModel? cached))
        {
            return cached;
        }

        RetailRayModel? model = null;
        if (task is DeclaredTask declared
            && _retailCarModels?.Invoke(Text(declared, WorldScene.ModelParameter)) is { } mef
            && OpenIGI.Formats.World.MefCollisionMesh.TryRead(mef.Container, meshIndex: 0, out OpenIGI.Formats.World.MefCollisionMesh? mesh)
            && mesh is { Faces.Length: > 0 })
        {
            RetailCarPlacement placement = RetailCarSpawn.Placement(declared, 0);
            model = new RetailRayModel(task, new RetailRayMesh(mesh), mef.NativeMarkerBound);
            model.Position[0] = placement.Position.X;
            model.Position[1] = placement.Position.Y;
            model.Position[2] = placement.Position.Z;
            RetailSplineSegment.AnglesToMatrix(placement.Orientation.X, placement.Orientation.Y, placement.Orientation.Z)
                .CopyTo(model.M, 0);
        }

        _standingCars[task] = model;
        return model;
    }

    /// <summary>A plane's retail body, created from its type and model the first time its AnimTask speaks.</summary>
    private RetailPlaneBody? RetailPlaneFor(DeclaredTask task, TransformState state, in PlanePhysicsDefinition physics)
    {
        if (state.RetailPlane is { } existing)
        {
            return existing;
        }

        if (_retailCarModels is not { } models
            || models(Text(task, WorldScene.ModelParameter)) is not { } model
            || _retailMagic is null)
        {
            return null;
        }

        RetailPlaneBody plane = RetailPlaneSpawn.Create(
            physics,
            model,
            RetailPlaneSpawn.Placement(task),
            _retailMagic,
            name => models(name));
        plane.Owner = task;
        state.RetailPlane = plane;
        return plane;
    }

    /// <summary>A helicopter's retail body, created from its type and model the first time its AnimTask speaks.</summary>
    private RetailHeliBody? RetailHeliFor(DeclaredTask task, TransformState state, in HelicopterPhysicsDefinition physics)
    {
        if (state.RetailHeli is { } existing)
        {
            return existing;
        }

        if (_retailCarModels?.Invoke(Text(task, WorldScene.ModelParameter)) is not { } model || _retailMagic is null)
        {
            return null;
        }

        VehiclePhysicsRegistry? registry = _vehiclePhysics;
        RetailHeliBody heli = RetailHeliSpawn.Create(
            physics,
            model,
            RetailHeliSpawn.Placement(task),
            _retailMagic,
            name => registry?.TryGetRotor(name, out RotorPhysicsDefinition rotor) == true ? rotor : null);
        heli.Owner = task;
        state.RetailHeli = heli;
        return heli;
    }

    private static void CopyRetailPose(TransformState state, RetailRigidBody body)
    {
        float[] m = body.M;
        state.Position = new Vector3d(body.Position[0], body.Position[1], body.Position[2]);
        state.Orientation = new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        state.Heading = MathF.Atan2(m[3], m[0]);
    }

    /// <summary>Drives one car for a tick from its three control channels (<c>0x42EA10</c>'s physics branch).</summary>
    /// <param name="target">The Car task, or <see langword="null"/>.</param>
    /// <param name="transform">The car's live transform.</param>
    /// <param name="simulationOrder">The dispatch order the pose snapshot is keyed by.</param>
    /// <param name="channel0">The steering target.</param>
    /// <param name="channel1">The throttle target, or -1 to hold the last one.</param>
    /// <param name="channel3">The parking brake: anything but -1 pulls it.</param>
    /// <remarks>An AnimTask's packet and CarAI's own (<c>0x426510</c>) arrive through the same receiver.</remarks>
    private void DriveCar(
        GameTask? target,
        TransformState transform,
        int simulationOrder,
        float channel0,
        float channel1,
        float channel3)
    {
        SnapshotDynamicPose(transform, simulationOrder);
        if (transform.RetailBody is { } retailBody && _retailCarWorld is { } retailWorld)
        {
            DriveRetailCar(target, transform, retailBody, retailWorld, channel0, channel1, channel3);
            return;
        }

        // Car::ReadChannels (0x42EA10) copies ch0 to the steering target and latches ch1 as
        // throttle only when it is not -1.  ch3 is the parking-brake pulse.  The former generic
        // mover had those first two channels reversed and advanced yaw even while stationary,
        // which sends the mission-one outro truck sideways out of its garage.
        transform.TargetSteering = channel0;
        if (channel1 != -1f)
        {
            transform.TargetThrust = channel1;
        }
        transform.ParkingBrake = channel3 != -1f;

        // Car::Tick (0x42ECA0) blends each live control from the model record's retention:
        //     current = current * retention + target * (1 - retention)
        // Retention is record +24 for steering and +32 for throttle; every shipped car publishes
        // a value between 0.9 and 0.998 there.
        CarPhysicsDefinition? carPhysics = transform.CarPhysics;
        float steeringRetention = CarRetention(carPhysics?.SteeringRetention);
        float throttleRetention = CarRetention(carPhysics?.ThrottleRetention);
        transform.Steering = (transform.Steering * steeringRetention)
            + (transform.TargetSteering * (1f - steeringRetention));
        transform.Thrust = (transform.Thrust * throttleRetention)
            + (transform.TargetThrust * (1f - throttleRetention));
        // Car::Tick zeroes a near-zero smoothed throttle at a zero target (0x42ECA0: if
        // +0x730 == 0 and |+0x720| < 0.01, +0x720 = 0). Without it a commanded stop decays
        // asymptotically and the truck freezes mid-route holding residual thrust.
        if (transform.TargetThrust == 0f && MathF.Abs(transform.Thrust) < 0.01f)
        {
            transform.Thrust = 0f;
        }

        float previousHeading = transform.Heading;
        Matrix3f previousOrientation = transform.Orientation;

        // The orientation's second column is the body's forward axis and the first its across
        // axis, which is how 0x430D10 reads them when it builds the drive direction.
        Vector3f forwardAxis = transform.Orientation.Column2;
        Vector3f lateralAxis = transform.Orientation.Column1;
        Vector3d forward = FlattenAxis(forwardAxis, new Vector3d(0d, 1d, 0d));
        Vector3d lateral = FlattenAxis(lateralAxis, new Vector3d(1d, 0d, 0d));

        CarPhysicsDefinition physics = carPhysics ?? DefaultCarPhysics;
        (Vector3d carTranslation, double carYaw) = CarDynamics.Step(
            transform.CarMotion,
            physics,
            CarWheelsFor(target, physics),
            transform.Position,
            forward,
            lateral,
            transform.Steering,
            transform.Thrust,
            transform.ParkingBrake,
            at => TryFindSupportHeight(at, target, allowDeepRecovery: false, out double height, out _)
                ? height
                : double.NaN);

        transform.Heading += (float)carYaw;
        transform.Orientation = Matrix3f.FromEngineEulerAngles(
            transform.Euler.X,
            transform.Euler.Y,
            transform.Heading);
        transform.Speed = (float)transform.CarMotion.Velocity.Length;
        Vector3d proposedPosition = transform.Position + carTranslation;

        // The obstacle probe is the body's own front, taken from the authored dimensions rather
        // than from a per-level table: half the length ahead of the origin, swept at the two
        // heights that bracket a car's bumper.
        double halfLengthUnits = physics.Dimensions.Y * 0.5d * WorldScene.UnitsPerMetre;
        bool blocked = false;
        GameTask? obstacle = null;
        if (target is not null && _vehicleObstacleHit is not null && carTranslation.Length > 0d)
        {
            Vector3d heading3 = carTranslation / carTranslation.Length;
            for (int heightIndex = 0; heightIndex < CarProbeHeightsMetres.Length && !blocked; heightIndex++)
            {
                Vector3d offset = new(
                    0d,
                    0d,
                    CarProbeHeightsMetres[heightIndex] * WorldScene.UnitsPerMetre);
                Vector3d probeStart = transform.Position + (heading3 * halfLengthUnits) + offset;
                Vector3d probeEnd = proposedPosition
                    + (heading3 * (halfLengthUnits + CarProbeMarginUnits))
                    + offset;
                if (_vehicleObstacleHit.Invoke(target, probeStart, probeEnd) is { } contact)
                {
                    obstacle = contact.Owner;
                    blocked = true;
                }
            }
        }

        if (_vehicleObstacleReleased is not null
            && obstacle is not null
            && string.Equals(obstacle.Type.Name, "Door", StringComparison.Ordinal))
        {
            transform.VehicleLatchedObstacle = obstacle;
        }

        if (transform.VehicleLatchedObstacle is { } latchedDoor)
        {
            if (_vehicleObstacleReleased?.Invoke(latchedDoor) == true)
            {
                transform.VehicleLatchedObstacle = null;
                transform.VehicleGateReleased = true;
                if (ReferenceEquals(obstacle, latchedDoor))
                {
                    blocked = false;
                    obstacle = null;
                }
            }
            else
            {
                blocked = true;
                obstacle ??= latchedDoor;
            }
        }

        if (blocked)
        {
            // Retail resolves a contact by cancelling the motion into the obstacle and letting
            // the body keep whatever is left, so a car that clips scenery slides along it and
            // recovers. Latching the speed to nought instead parks the vehicle for good, which
            // is what stranded the Mission 1 truck against the perimeter fence. The springs are
            // not part of that contact, so the body still rides them.
            transform.Heading = previousHeading;
            transform.Orientation = previousOrientation;
            transform.CarMotion.Velocity = default;
            transform.CarMotion.YawRate = 0d;
            transform.Speed = 0f;
            transform.Position += new Vector3d(0d, 0d, carTranslation.Z);
        }
        else
        {
            transform.Position = proposedPosition;
            transform.Velocity = transform.Position - transform.PreviousPosition;
        }
        transform.IsDynamic = true;
    }

    private void ApplyAnimation(int targetId, int simulationOrder, AnimDataProgram program)
    {
        if (targetId < 0 || !_transforms.TryGetValue(targetId, out TransformState? transform))
        {
            return;
        }

        GameTask? target = _root.Tree.FromId(targetId);
        string type = target?.Type.Name ?? string.Empty;

        // Retail dispatch is table-driven (handler table slot*384 + typeId @ 0xA96AE0) and only
        // Train/Car/Heli/Plane register ReadChannels handlers (ANIMTASK-RE.md §5-6). Human's
        // registration sub_45E450 registers none, so AnimProgram_End (0x4F4060) skips delivery:
        // an AnimTask targeting a human delivers NOTHING — no translation, no speed, no animation
        // id. Humans animate through their own state machine (state tick 0x4550B0, playback rate
        // engineDelta * 1.5 per Human_GetCutSceneDeltaTime 0x45EAE0), and their pose reaches the
        // cutscene via SetTargetPoseResolver / attachment parenting. Reproduce that here: drop
        // the packet instead of the former thrust-and-slide fallback.
        if (target is not DeclaredTask
            || !(type is "Train" or "Car" or "Heli" or "Plane"))
        {
            return;
        }

        ReadOnlySpan<float> channels = program.Channels;

        if (string.Equals(type, "Train", StringComparison.Ordinal)
            && transform.Railroad is { } trainRailroad)
        {
            // Train::ReadChannels (IGI.exe 0x42B040) treats tick zero as receiver setup, not a
            // physics update. It installs the channel-one hold sentinel and restores the authored
            // rail state, then returns without changing TrackPosition. The AnimTask sends tick zero
            // twice; advancing on either packet shifts every car before Jones has landed.
            if (program.Tick == 0)
            {
                program.WriteChannel(1, -2f);
                transform.Thrust = transform.OriginalThrust;
                transform.TargetThrust = transform.OriginalThrust;
                transform.Speed = transform.MaxSpeed * transform.Thrust * (4_096_000f / 108_000f);
                transform.IsDynamic = true;
                return;
            }

            SnapshotDynamicPose(transform, simulationOrder);

            // Train::ReadChannels (0x42B040): channel zero is a latched thrust target.  The live
            // thrust approaches it by the authored step, then MaxSpeed converts km/h to engine
            // units per 30 Hz tick (4096000 / 108000).  Flip negates the scalar rail advance; the
            // path evaluator turns that signed distance back into the correct tangent direction.
            if (channels[0] != -1f)
            {
                transform.TargetThrust = channels[0];
            }

            transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, transform.ThrustStep);
            transform.Speed = transform.MaxSpeed * transform.Thrust * (4_096_000f / 108_000f);
            transform.TrackPosition += trainRailroad.Flipped ? -transform.Speed : transform.Speed;
            trainRailroad.Evaluate(
                transform.TrackPosition + transform.TrainOffset,
                out Vector3d railPosition,
                out Matrix3f railOrientation);
            transform.Position = railPosition;
            transform.Orientation = railOrientation;
            transform.IsDynamic = true;
            return;
        }

        float yaw = MathF.Atan2(transform.Orientation.M21, transform.Orientation.M11);
        if (string.Equals(type, "Car", StringComparison.Ordinal))
        {
            // Car::ReadChannels (IGI.exe 0x42EA10) treats the first packet as receiver setup,
            // not a physics step. Restore the authored body and install the channel hold
            // sentinels before the positive-tick callback starts smoothing controls. Running the
            // generic integration on tick zero advances a truck before its cutscene clock has
            // elapsed and leaves the receiver's channel-two state live for the next packet.
            if (program.Tick == 0)
            {
                transform.Position = transform.OriginPosition;
                transform.Orientation = transform.AuthoredOrientation;
                transform.PreviousPosition = transform.Position;
                transform.PreviousOrientation = transform.Orientation;
                transform.Velocity = default;
                transform.Speed = 0f;
                transform.Steering = 0f;
                transform.TargetSteering = 0f;

                // Car::ReadChannels seeds BOTH the live throttle and its target from the task's
                // authored thrust (+1824 and +1840 from +1856), the way Train::ReadChannels does.
                // Mission 2's truck opens its cutscene already carrying 0.6 and coasts down from
                // it; starting from nought leaves it standing there, because the authored channel
                // only ever sends the -1 hold afterwards.
                transform.Thrust = transform.OriginalThrust;
                transform.TargetThrust = transform.OriginalThrust;
                transform.ParkingBrake = false;
                transform.Heading = transform.Euler.Z;
                transform.VehicleLatchedObstacle = null;
                transform.VehicleGateReleased = false;
                transform.CarMotion.Reset();
                ResetRetailBody(target, transform);
                program.WriteChannel(1, -1f);
                program.WriteChannel(2, -2f);
                transform.IsDynamic = true;
                return;
            }

            DriveCar(target, transform, simulationOrder, channels[0], channels[1], channels[3]);
            return;
        }
        else if (string.Equals(type, "Heli", StringComparison.Ordinal))
        {
            HelicopterPhysicsDefinition physics = transform.HelicopterPhysics
                ?? new HelicopterPhysicsDefinition(
                    3_000f,
                    new Vector3f(2.2f, 7.8f, 3f),
                    new Vector3f(50f, 50f, 50f),
                    new Vector3f(0.4f, 0.4f, 0.4f),
                    0.027f,
                    0.003f,
                    200f,
                    [
                        new(20f, Vector3f.UnitX, Vector3f.Zero),
                        new(3f, Vector3f.UnitY, Vector3f.Zero),
                        new(50f, Vector3f.UnitZ, Vector3f.Zero),
                    ]);
            if (_retailCarWorld is { } retailWorld
                && target is DeclaredTask retailHeliTask
                && transform.HelicopterPhysics is { } retailHeliPhysics
                && RetailHeliFor(retailHeliTask, transform, retailHeliPhysics) is { } retailHeli)
            {
                // 0x431B70: every packet latches the channels; mode 0 resets, a later packet steps Heli_Tick.
                retailHeli.Receive(program.Channels);
                if (program.Tick == 0)
                {
                    RetailHeliSpawn.Reset(retailHeli, retailHeliPhysics, RetailHeliSpawn.Placement(retailHeliTask));


                    program.WriteChannel(0, 0f);
                    program.WriteChannel(1, 0f);
                    program.WriteChannel(2, 0f);
                    program.WriteChannel(3, -1f);
                    program.WriteChannel(4, -2f);
                    CopyRetailPose(transform, retailHeli);
                    transform.PreviousPosition = transform.Position;
                    transform.PreviousOrientation = transform.Orientation;
                    transform.Velocity = default;
                    transform.Thrust = retailHeli.Collective;
                    transform.TargetThrust = retailHeli.CollectiveTarget;
                    transform.RotorPhase = 0f;
                    transform.RotorPhaseTick = SimulationTick;
                    transform.IsDynamic = true;
                    return;
                }

                SnapshotDynamicPose(transform, simulationOrder);

                // 0x4F4660 ticks the attachments -- the rotors push their lift and yaw -- right before Heli_Tick.
                retailHeli.RotorTick();
                retailHeli.Tick(retailWorld);
                CopyRetailPose(transform, retailHeli);
                transform.Thrust = retailHeli.Collective;
                transform.TargetThrust = retailHeli.CollectiveTarget;
                transform.Speed = retailHeli.Speed;
                transform.Velocity = transform.Position - transform.PreviousPosition;
                transform.RotorPhase += transform.Thrust;
                transform.RotorPhaseTick = SimulationTick;
                transform.PhysicsTicks++;
                transform.IsDynamic = true;
                LogAirframePath(targetId, "Heli", target, transform);
                return;
            }

            if (program.Tick == 0)
            {
                // The retail Heli live callback (0x431B70) is bidirectional: its tick-zero reset restores the
                // authored pose/collective, clears motion and controls, and overwrites the shared
                // AnimTask block with its hold sentinels. Without the channel-three write, the zeroed
                // programme block cancels Original Thrust on tick one and the opening helicopter never
                // reaches the later target-tracked flyby shot.
                transform.Position = transform.OriginPosition;
                transform.Orientation = transform.AuthoredOrientation;
                transform.Velocity = default;
                transform.Control = default;
                transform.Heading = 0f;
                transform.RotorPhase = 0f;
                transform.RotorPhaseTick = SimulationTick;
                transform.AngularMomentum = default;
                transform.QueuedRotorForce = default;
                transform.QueuedRotorTorque = default;
                transform.PhysicsTicks = 0;
                transform.GroundContact = false;
                transform.GroundContactEventLogged = false;
                transform.GroundContactLogged = false;
                transform.TerminalLandingLatched = false;
                transform.TerminalLandingEventLogged = false;
                transform.TerminalLandingPosition = transform.OriginPosition;
                transform.TargetThrust = transform.Thrust;
                // A landed helicopter rests with its skids on the terrain. Retail's Heli collision
                // flag keeps the airframe from sinking through the surface while its collective ramps
                // below hover. Prefer a measurement from an authored pose that is already on a
                // support, but never measure an arbitrary in-flight pose and freeze that gap as the
                // rest height: an authored flight can reach an elevated surface while still above its
                // roof, and measuring that in-flight gap would make it stop in the air when its collective reached zero.
                double measuredRest = MeasureGroundRestOffset(transform.OriginPosition, target);
                if (IsPlausibleGroundRestOffset(measuredRest, physics))
                {
                    transform.GroundRestOffset = Math.Max(measuredRest, DefaultGroundRestOffset(physics));
                }
                else if (double.IsNaN(transform.GroundRestOffset))
                {
                    transform.GroundRestOffset = DefaultGroundRestOffset(physics);
                    Note(
                        $"heli-rest-fallback id={targetId} measured={measuredRest:R} "
                        + $"offset={transform.GroundRestOffset:R}");
                }
                // Cache the authored near-ground offset for contact recovery. Do not zero collective
                // here: both Mission 4 Bells follow their AnimTask controls and must still lift.
                if (!double.IsNaN(transform.GroundRestOffset))
                {
                    Note($"heli-park-rest id={targetId} offset={transform.GroundRestOffset:R}");
                }

                program.WriteChannel(0, 0f);
                program.WriteChannel(1, 0f);
                program.WriteChannel(2, 0f);
                program.WriteChannel(3, -1f);
                program.WriteChannel(4, -2f);
                // Rotor tasks run after their parent Heli task. Even though the tick-zero channel
                // packet only resets the body, the child rotor still queues lift for tick one.
                QueueRetailRotorForces(transform, transform.Orientation, physics);
                transform.IsDynamic = true;
                return;
            }

            // An explicitly configured terminal landing has reached its touchdown. Keep the body
            // parked at the first terrain contact instead of feeding the next AnimTask sample back
            // through the rigid-flight integrator. Without this latch the terminal wait clears the
            // collective, but the horizontal velocity carries the airframe onto the next elevated surface.
            if (transform.TerminalLandingLatched)
            {
                transform.Position = transform.TerminalLandingPosition;
                transform.PreviousPosition = transform.Position;
                transform.Velocity = default;
                transform.Speed = 0f;
                transform.Thrust = 0f;
                transform.TargetThrust = 0f;
                transform.Control = default;
                transform.AngularMomentum = default;
                transform.QueuedRotorForce = default;
                transform.QueuedRotorTorque = default;
                transform.IsDynamic = true;
                transform.PhysicsTicks++;
                LogAirframePath(targetId, "Heli", target, transform);
                return;
            }

            SnapshotDynamicPose(transform, simulationOrder);

            channels = program.Channels;
            // Heli::ReadChannels stores normalized cyclic/yaw controls; it does not integrate them as
            // Euler radians.  The rigid flight step smooths those controls, applies torque and actively
            // levels the airframe.  Treating a held 1 as one radian/second accumulated several full
            // turns during the intro and rolled the linked camera underneath the terrain.
            Vector3f targetControl = new(channels[0], channels[1], channels[2]);
            transform.Control = new Vector3f(
                ((1f - physics.Smoothing.X) * targetControl.X) + (physics.Smoothing.X * transform.Control.X),
                ((1f - physics.Smoothing.Y) * targetControl.Y) + (physics.Smoothing.Y * transform.Control.Y),
                ((1f - physics.Smoothing.Z) * targetControl.Z) + (physics.Smoothing.Z * transform.Control.Z));
            if (channels[3] != -1f)
            {
                transform.TargetThrust = channels[3];
            }

            float collectiveStep = transform.Thrust >= 0.8f
                ? physics.HighCollectiveStep
                : physics.LowCollectiveStep;
            transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, collectiveStep);
            if (transform.Thrust < 0.001f && transform.TargetThrust == 0f)
            {
                transform.Thrust = 0f;
            }
            transform.RotorPhase += transform.Thrust;
            transform.RotorPhaseTick = SimulationTick;
            if (target is DeclaredTask liveHeli)
            {
                transform.CollisionEnabled = Bool(liveHeli, "Collision detection");
            }

            ApplyRetailHelicopterPhysics(transform, physics);
            transform.Position += transform.Velocity;
            if (target is not null)
            {
                KeepHelicopterAboveGround(target, transform);
            }
            transform.PhysicsTicks++;
            transform.IsDynamic = true;
            LogAirframePath(targetId, "Heli", target, transform);
            return;
        }
        else if (string.Equals(type, "Plane", StringComparison.Ordinal))
        {
            if (_retailCarWorld is { } planeWorld
                && target is DeclaredTask retailPlaneTask
                && transform.PlanePhysics is { } retailPlanePhysics
                && RetailPlaneFor(retailPlaneTask, transform, retailPlanePhysics) is { } retailPlane)
            {
                // 0x4337E0: every packet latches the channels; mode 0 resets, a later packet steps Plane_Physics
                // (0x433B50) and the gear legs (0x4F4660).
                retailPlane.Receive(program.Channels);
                if (program.Tick == 0)
                {
                    RetailPlaneSpawn.Reset(retailPlane, retailPlanePhysics, RetailPlaneSpawn.Placement(retailPlaneTask));
                    CopyRetailPose(transform, retailPlane);
                    transform.PreviousPosition = transform.Position;
                    transform.PreviousOrientation = transform.Orientation;
                    transform.Velocity = default;
                    transform.Thrust = retailPlane.Thrust;
                    transform.TargetThrust = retailPlane.ThrustTarget;
                    transform.IsDynamic = true;
                    return;
                }

                SnapshotDynamicPose(transform, simulationOrder);
                retailPlane.Tick(planeWorld);
                CopyRetailPose(transform, retailPlane);
                transform.Thrust = retailPlane.Thrust;
                transform.TargetThrust = retailPlane.ThrustTarget;
                transform.Speed = retailPlane.Speed;
                transform.Velocity = transform.Position - transform.PreviousPosition;
                transform.PhysicsTicks++;
                transform.IsDynamic = true;
                LogAirframePath(targetId, "Plane", target, transform);
                return;
            }

            if (program.Tick == 0)
            {
                // The receiver's two tick-zero packets restore the authored rigid-body pose and
                // publish no movement. Its positive-tick collision/body integration is Plane-owned:
                // 0x433B50 calls the contact path at 0x434310 and shared body step at 0x4FC0E0.
                // A Heli terrain-rest clamp would therefore turn a fly-by into a terrain follower.
                transform.Position = transform.OriginPosition;
                transform.Orientation = transform.AuthoredOrientation;
                transform.Velocity = default;
                transform.Control = default;
                transform.Heading = 0f;
                transform.Speed = 0f;
                transform.Thrust = 0f;
                transform.TargetThrust = 0f;
                transform.GroundContact = false;
                transform.GroundContactEventLogged = false;
                transform.GroundContactLogged = false;
                transform.IsDynamic = true;
                return;
            }

            SnapshotDynamicPose(transform, simulationOrder);

            // Plane_ReadChannels (0x4337E0) publishes pitch/roll/yaw targets and a latched thrust
            // target.  Plane_Physics (0x433B50) retains those targets with config +8/+12/+16:
            // the shipped SU-27 values are all 0.9.  Do not substitute the former 0.12 guess.
            Vector3f targetControl = new(channels[0], channels[1], channels[2]);
            if (transform.PlanePhysics is { } planePhysics)
            {
                Vector3f retention = planePhysics.ControlRetention;
                transform.Control = new Vector3f(
                    (retention.X * transform.Control.X) + ((1f - retention.X) * targetControl.X),
                    (retention.Y * transform.Control.Y) + ((1f - retention.Y) * targetControl.Y),
                    (retention.Z * transform.Control.Z) + ((1f - retention.Z) * targetControl.Z)) * 0.995f;
            }
            else
            {
                // Unit-level runtimes may deliberately omit the retail registry. Keep their
                // pre-existing generic motion path isolated from the retail-game implementation.
                transform.Control += (targetControl - transform.Control) * 0.12f;
            }
            if (channels[3] != -1f)
            {
                transform.TargetThrust = channels[3];
            }
            if (target is DeclaredTask livePlane)
            {
                transform.CollisionEnabled = Bool(livePlane, "Collision detection");
            }
            // Plane_Physics selects config +32 after thrust reaches 0.8 and config +36 before it.
            // SU-27 supplies 0.054 / 0.006.  The old fixed 0.001 made the Level 3 takeoff spend
            // most of its authored flight window spooling on the runway.
            float thrustStep = transform.PlanePhysics is { } physics
                ? transform.Thrust >= 0.8f ? physics.HighThrustStep : physics.LowThrustStep
                : 0.001f;
            transform.Thrust = MoveTowards(transform.Thrust, transform.TargetThrust, thrustStep);
            transform.Heading += transform.Control.Z * 0.004f;
            transform.Orientation = Matrix3f.FromEngineEulerAngles(
                transform.Control.X * 0.10f,
                transform.Control.Y * 0.18f,
                transform.Heading) * transform.AuthoredOrientation;
            // Plane_Physics adds thrust on body-local +Y. The shared body integrator converts local
            // acceleration to world space by dotting each orientation row with that local vector,
            // which yields Column2. Mission 3's gamma=3pi/2 takeoff therefore travels +X down the
            // runway; Row2 is the inverse direction and visibly makes the SU-27 fly tail-first.
            Vector3f planeForward = transform.Orientation.Column2;
            float desiredSpeed = Math.Max(0f, transform.TargetThrust) * 1_500f;
            transform.Speed += (desiredSpeed - transform.Speed) * 0.025f;
            Vector3d previousPosition = transform.Position;
            transform.Position += new Vector3d(planeForward.X, planeForward.Y, planeForward.Z)
                * transform.Speed;
            transform.Velocity = transform.Position - previousPosition;
            transform.IsDynamic = true;
            LogAirframePath(targetId, "Plane", target, transform);
            return;
        }
    }

    private bool TryPose(int id, out TransformState? state)
    {
        if (_targetPoseResolver?.Invoke(id) is { } live)
        {
            if (!_transforms.TryGetValue(id, out state))
            {
                state = new TransformState(live.Position, live.Orientation);
                _transforms[id] = state;
            }
            else
            {
                state.Position = live.Position;
                state.Orientation = live.Orientation;
            }
            return true;
        }

        if (!_transforms.TryGetValue(id, out state))
        {
            return false;
        }

        if (_root.Tree.FromId(id) is { } task
            && TryEvaluateAttachment(task, out Vector3d attachedPosition, out Matrix3f attachedOrientation))
        {
            state.Position = attachedPosition;
            state.Orientation = attachedOrientation;
            return true;
        }

        // A consist child has no independent AnimTask.  Retail asks its railroad assembly for the
        // current frame whenever EditCamera resolves a Link, not only later when the model is drawn.
        // Leaving this update in the renderer made linked cameras follow the car's spawn pose forever.
        UpdateDerivedPose(state);
        return true;
    }

    private static void UpdateDerivedPose(TransformState state)
    {
        if (state.TrainRoot is null || ReferenceEquals(state.TrainRoot, state))
        {
            return;
        }

        EvaluateDynamicPose(state, previous: false, out Vector3d position, out Matrix3f orientation);
        state.Position = position;
        state.Orientation = orientation;
    }

    private static void SnapshotDynamicPose(TransformState state, int simulationOrder)
    {
        state.PreviousPosition = state.Position;
        state.PreviousOrientation = state.Orientation;
        state.PreviousTrackPosition = state.TrackPosition;
        state.DynamicRevision++;
        state.LastSimulationOrder = simulationOrder;
    }

    private static int DynamicRevision(TransformState state) =>
        state.TrainRoot?.DynamicRevision ?? state.DynamicRevision;

    private static void EvaluateDynamicPose(
        TransformState state,
        bool previous,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (state.TrainRoot is { Railroad: { } railroad } root)
        {
            float trackPosition = previous ? root.PreviousTrackPosition : root.TrackPosition;
            railroad.Evaluate(
                trackPosition + state.TrainOffset,
                out position,
                out orientation);
            return;
        }

        position = previous ? state.PreviousPosition : state.Position;
        orientation = previous ? state.PreviousOrientation : state.Orientation;
    }

    private static Matrix3f ToCameraOrientation(Matrix3f orientation)
    {
        Matrix3f axes = orientation.Transposed();
        return Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
    }

    private static Matrix3f LookAt(Vector3d from, Vector3d to)
    {
        Vector3d d = to - from;
        double length = d.Length;
        if (length <= double.Epsilon)
        {
            return Matrix3f.Identity;
        }
        Vector3f forward = new((float)(d.X / length), (float)(d.Y / length), (float)(d.Z / length));
        Vector3f right = new(forward.Y, -forward.X, 0f);
        float rl = MathF.Sqrt((right.X * right.X) + (right.Y * right.Y));
        right = rl > 0f ? right * (1f / rl) : Vector3f.UnitX;
        // sub_508620 writes right, -cross(right, forward), forward as COLUMNS. The final camera
        // boundary transposes this retail storage into Camera3D's row-axis representation.
        Vector3f up = -Vector3f.Cross(right, forward);
        return new Matrix3f(
            right.X, up.X, forward.X,
            right.Y, up.Y, forward.Y,
            right.Z, up.Z, forward.Z);
    }

    private static double Hermite(double time, double t0, double t1, double t2, double t3, double v0, double v1, double v2, double v3)
    {
        double s1 = t2 == t0 ? 0.0 : 2.0 * (t1 - t0) / (t2 - t0) * ((v2 - v0) * 0.5);
        double s2 = t3 == t1 ? 0.0 : 2.0 * (t2 - t1) / (t3 - t1) * ((v3 - v1) * 0.5);
        double u = t2 == t1 ? 0.0 : (time - t1) / (t2 - t1);
        double a = (2.0 * v1) - (2.0 * v2) + s2 + s1;
        double b = (3.0 * v2) - (3.0 * v1) - (2.0 * s1) - s2;
        return v1 + (u * u * u * a) + (u * u * b) + (u * s1);
    }

    private static float Noise(ref uint value)
    {
        value = unchecked((value * 1664525u) + 1013904223u);
        return ((value >> 8) / 16777215f) - 0.5f;
    }

    private List<EditCameraShot> ReadShots(CutSceneTask scene)
    {
        List<EditCameraShot> shots = [];
        // IGI1 0x4F55D0 interpolates EditCamera's authored XYZ fields directly.
        // Small coordinates can belong to a separate cinematic set (Mission 4's Anya HQ);
        // they are not evidence of a vehicle-local offset. Only explicit Link IDs transform
        // the evaluated camera through 0x508940. See the MVP retail audit for both listings.
        foreach (GameTask task in Walk(scene))
        {
            if (task is not DeclaredTask camera || !string.Equals(task.Type.Name, "EditCamera", StringComparison.Ordinal))
            {
                continue;
            }
            int link = Int(camera, "Link task ID", -1);
            int target = Int(camera, "Target task ID", -1);
            (Vector3d linkOrigin, Matrix3f linkOrientation) = InitialPose(scene.Tree.FromId(link));
            Vector3d position = Vector(camera, "Position");
            shots.Add(new EditCameraShot(
                position,
                Real(camera, "Alpha"), Real(camera, "Beta"), Real(camera, "Gamma"),
                Math.Clamp(Real(camera, "FOV", 1f), 0.001f, 3f), Real(camera, "Duration"),
                link, Bool(camera, "Update link continously"), target, Bool(camera, "Update target continously"),
                Bool(camera, "Smooth to next"), Real(camera, "Time of day", -1f),
                Text(camera, "FILTER"), Real(camera, "Noise"), Vector32(camera, "Filter color RGB"),
                Real(camera, "Camera shake"), linkOrigin, linkOrientation));
        }
        return shots;
    }

    private static bool IsBandFilter(string filter) =>
        filter.Contains("BAND", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private (Vector3d Position, Matrix3f Orientation) InitialPose(GameTask? task)
    {
        if (task is not null && _taskTransforms.TryGetValue(task, out TransformState? transform))
        {
            return (transform.Position, transform.Orientation);
        }
        if (task is HumanSoldierTask soldier)
        {
            return (soldier.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma));
        }
        if (task is DeclaredTask declared)
        {
            Vector3f angles = Vector32(declared, "Orientation");
            return (Vector(declared, "Position"), Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z));
        }
        return (default, Matrix3f.Identity);
    }

    private static string Text(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;
    private static bool Bool(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is bool flag && flag;
    private static float Single(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is IConvertible convertible
            ? convertible.ToSingle(CultureInfo.InvariantCulture)
            : fallback;
    private static int Int(DeclaredTask task, string name, int fallback = 0) => task.TryGetValue(name, out object? value) && value is int number ? number : fallback;
    private static float Real(DeclaredTask task, string name, float fallback = 0f) => task.TryGetValue(name, out object? value) && value is float number ? number : fallback;
    private static float RealNamed(DeclaredTask task, string compactName, float fallback = 0f)
    {
        string wanted = compactName.Replace(" ", string.Empty, StringComparison.Ordinal);
        for (int i = 0; i < task.Type.Parameters.Count; i++)
        {
            string actual = task.Type.Parameters[i].Name.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (actual.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is float value)
            {
                return value;
            }
        }

        return fallback;
    }
    private static Vector3d Vector(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : default;
    private static Vector3f Vector32(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) && value is Vector3f vector ? vector : default;
    private static string FirstTextNamed(DeclaredTask task, string fragment)
    {
        for (int i = 0; i < task.Type.Parameters.Count; i++)
        {
            if (task.Type.Parameters[i].Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                && task.GetValue(i) is string text)
            {
                return text;
            }
        }

        return string.Empty;
    }
    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    private static Vector3f Lerp(Vector3f a, Vector3f b, float t) => a + ((b - a) * t);
    /// <summary>
    /// Ports the retail Heli live update (0x431B70), its force helper (0x431E30), Rotor::Tick
    /// (0x42D440), and the force accumulator at 0x4EC6A0.
    /// Forces and application points are body-local; the accumulator converts linear acceleration to
    /// world space with the pre-integration attitude. Retail damps angular momentum by 0.92 after the
    /// quaternion step and deliberately does not damp linear velocity.
    /// </summary>
    /// <summary>Measures a parked helicopter's rest height above the ground under its authored pose.</summary>
    /// <remarks>
    /// The authored pose of a landed helicopter sits its skids on the surface, so the distance from
    /// the pose down to the terrain is the model's own rest height. Returns <see cref="double.NaN"/>
    /// when the pose is not within the near-ground query's reach, meaning the helicopter is airborne
    /// and no rest height applies.
    /// </remarks>
    private double MeasureGroundRestOffset(Vector3d authoredPosition, GameTask? ignoredOwner = null)
    {
        // Near-body only. A 2 km column would treat an airborne spawn as parked and
        // clamp it to the dirt for the whole flight (356 lift-off regression).
        if (!TryFindSupportHeight(
                authoredPosition,
                ignoredOwner,
                allowDeepRecovery: false,
                out double surfaceHeight))
        {
            return double.NaN;
        }

        // Never record a negative rest: the first successful query can happen after the body has
        // already crossed below the surface. A negative offset would clamp it even deeper and the
        // plane/heli would keep sinking. Belly-on-surface (0) is the safe floor.
        return Math.Max(0.0, authoredPosition.Z - surfaceHeight);
    }

    private static double DefaultGroundRestOffset(HelicopterPhysicsDefinition physics) =>
        Math.Max(0.0, physics.Dimensions.Z * 0.5f * WorldScene.UnitsPerMetre);

    private static bool IsPlausibleGroundRestOffset(
        double measured,
        HelicopterPhysicsDefinition physics) =>
        !double.IsNaN(measured)
        && measured <= Math.Max(
            4.0 * WorldScene.UnitsPerMetre,
            physics.Dimensions.Z * 2.0f * WorldScene.UnitsPerMetre);

    private static string FormatMetres(double units) =>
        double.IsNaN(units)
            ? "NaN"
            : (units / WorldScene.UnitsPerMetre).ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Finds the highest supporting surface height under a point, preferring an elevated placed object (a
    /// pad, crate or rooftop) over lower terrain.
    /// </summary>
    /// <param name="position">The body position, in world units.</param>
    /// <param name="surfaceHeight">Receives the highest supporting surface height found.</param>
    /// <returns>
    /// <see langword="true"/> when at least one surface answers; <see langword="false"/> when neither the
    /// terrain nor the collision index reports a surface under the point.
    /// </returns>
    /// <remarks>
    /// Retail's terrain half (<c>0x4CBE40</c>) and placed-model sweep (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>)
    /// are two answers for the same question. The higher of the two is the surface the body actually rests
    /// on: a heli parked on a pad must rest on the pad's top, not the terrain below it. Both queries run and
    /// the higher answer wins, so an elevated collision surface takes precedence over terrain without losing
    /// the terrain answer when no object is present.
    /// </remarks>
    private bool TryFindSupportHeight(Vector3d position, out double surfaceHeight) =>
        TryFindSupportHeight(position, ignoredOwner: null, allowDeepRecovery: true, out surfaceHeight);

    private bool TryFindSupportHeight(
        Vector3d position,
        GameTask? ignoredOwner,
        bool allowDeepRecovery,
        out double surfaceHeight) =>
        TryFindSupportHeight(position, ignoredOwner, allowDeepRecovery, out surfaceHeight, out _);

    private bool TryFindSupportHeight(
        Vector3d position,
        GameTask? ignoredOwner,
        bool allowDeepRecovery,
        out double surfaceHeight,
        out string supportSource)
    {
        double best = double.NegativeInfinity;
        bool found = false;
        bool objectFound = false;
        supportSource = "none";

        if (_terrain is { IsUsable: true } terrain
            && TerrainHeightQuery.TryGetGround(
                terrain,
                position.X,
                position.Y,
                position.Z,
                TerrainHeightQuery.AboveRecoveryUnits,
                out TerrainGround ground))
        {
            best = ground.Height;
            found = true;
            supportSource = "terrain";
        }

        if (_collisionIndex is not null)
        {
            Vector3d top = new(position.X, position.Y, position.Z + 4096.0);
            Vector3d bottom = new(position.X, position.Y, position.Z - 262144.0);
            if (TryCastObjectsIgnoringOwner(top, bottom, ignoredOwner, out WorldRayHit hit)
                && hit.Position.Z <= position.Z + TerrainHeightQuery.AboveRecoveryUnits
                && hit.Position.Z > best)
            {
                best = hit.Position.Z;
                found = true;
                objectFound = true;
                supportSource = "object";
            }
        }

        // A fast sink can leave the body more than AboveRecoveryUnits below the mesh. The
        // near-ground query only sees ~20 m around its probe Z, so a single sample 2 km above
        // the body never hits the surface they just punched through. Walk the column down in
        // 4 m steps (same window as TerrainHeightQuery.SearchDepthUnits) from well above the
        // body to just under it, and take the first (highest) surface.
        if (allowDeepRecovery && !found && _terrain is { IsUsable: true } deepTerrain)
        {
            const double Step = 4.0 * WorldScene.UnitsPerMetre;
            const double High = 2_000.0 * WorldScene.UnitsPerMetre;
            for (double probeZ = position.Z + High; probeZ >= position.Z - Step; probeZ -= Step)
            {
                if (!TerrainHeightQuery.TryGetGround(
                        deepTerrain, position.X, position.Y, probeZ, out TerrainGround deep)
                    || deep.Height <= best)
                {
                    continue;
                }

                best = deep.Height;
                found = true;
                supportSource = "terrain-deep";
                break;
            }
        }

        // The body can be below an elevated pad/roof while the terrain query still answers below it.
        // In that case !found is false, but the one-metre near ray above the body has already missed
        // the object. Recover the object independently, then retain whichever valid surface is higher.
        if (allowDeepRecovery && !objectFound && _collisionIndex is not null)
        {
            Vector3d highTop = new(
                position.X, position.Y, position.Z + (2_000.0 * WorldScene.UnitsPerMetre));
            Vector3d deepBottom = new(position.X, position.Y, position.Z - 262144.0);
            if (TryCastObjectsIgnoringOwner(highTop, deepBottom, ignoredOwner, out WorldRayHit deepHit)
                && deepHit.Position.Z > best)
            {
                best = deepHit.Position.Z;
                found = true;
                supportSource = "object-deep";
            }
        }

        surfaceHeight = best;
        return found;
    }

    private bool TryCastObjectsIgnoringOwner(
        Vector3d origin,
        Vector3d end,
        GameTask? ignoredOwner,
        out WorldRayHit hit)
    {
        hit = default;
        if (_collisionIndex is null)
        {
            return false;
        }

        WorldTraceRequest request = new(
            origin,
            end,
            WorldTraceMask.StaticObjects | WorldTraceMask.DynamicObjects,
            IgnoredOwner: ignoredOwner);
        if (!_collisionIndex.TryTrace(request, out WorldTraceHit traced))
        {
            return false;
        }

        hit = traced.RayHit;
        return true;
    }

    /// <summary>Keeps a helicopter from sinking through the terrain during contact recovery.</summary>
    /// <remarks>
    /// Retail's Heli collision flag prevents a descending body from penetrating a supporting surface;
    /// it does not replace the scripted collective or cyclic controls with a pad lock. Airborne
    /// helicopters are untouched while the query finds no nearby support. The landing-height search
    /// is widened a little above the body (see <see cref="TerrainHeightQuery.AboveRecoveryUnits"/>) so
    /// a body that dips under a ridge or a rising slope still finds the surface it just crossed and
    /// rides it back up, instead of falling forever once the downward-only search loses it.
    /// </remarks>
    private void RestCollisionHelisOnSupport()
    {
        foreach ((GameTask task, TransformState state) in _taskTransforms)
        {
            if (state.CollisionEnabled && string.Equals(task.Type?.Name, "Heli", StringComparison.Ordinal))
            {
                RestHeliOnSupport(task, state);
            }
        }
    }

    private void RestHeliOnSupport(GameTask task, TransformState state)
    {
        double measuredRest = MeasureGroundRestOffset(state.OriginPosition, task);
        if (state.HelicopterPhysics is { } physics)
        {
            bool plausible = IsPlausibleGroundRestOffset(measuredRest, physics);
            double bodyFloor = DefaultGroundRestOffset(physics);
            state.GroundRestOffset = plausible
                ? Math.Max(measuredRest, bodyFloor)
                : bodyFloor;
            if (!plausible)
            {
                Note(
                    $"heli-rest-fallback id={task.Id} measured={measuredRest:R} "
                    + $"offset={state.GroundRestOffset:R}");
            }

            _logger.Warning(
                "Airframe",
                $"[AIRFRAME-REST] id={task.Id} type=Heli "
                + $"measured={FormatMetres(measuredRest)} applied={FormatMetres(state.GroundRestOffset)} "
                + $"mode={(plausible && measuredRest >= bodyFloor ? "authored" : "physics-floor")}");
        }
        else
        {
            state.GroundRestOffset = measuredRest;
        }

        if (!TryGetLandingHeight(state.OriginPosition, state.GroundRestOffset, task, out double rest)
            || double.IsNaN(rest))
        {
            return;
        }

        state.Position = new Vector3d(state.Position.X, state.Position.Y, rest);
        Note($"heli-park-rest id={task.Id} offset={state.GroundRestOffset:R}");
    }

    private void KeepHelicopterAboveGround(GameTask? ignoredOwner, TransformState transform)
    {
        Vector3d position = transform.Position;

        // The rest offset is a body/pivot property, not the current distance to whatever surface a
        // ray happens to find. If the tick-zero authored pose was not near support, use the physics
        // body's stable half-height and never calibrate from an in-flight pose.
        if (double.IsNaN(transform.GroundRestOffset))
        {
            transform.GroundRestOffset = transform.HelicopterPhysics is { } physics
                ? DefaultGroundRestOffset(physics)
                : 0.0;
        }

        if (!TryGetLandingHeight(
                position,
                transform.GroundRestOffset,
                ignoredOwner,
                out double rest,
                out string supportSource)
            || double.IsNaN(rest))
        {
            transform.GroundContact = false;
            transform.GroundContactLogged = false;
            transform.GroundContactEventLogged = false;
            return;
        }

        if (position.Z > rest + (0.5 * WorldScene.UnitsPerMetre))
        {
            transform.GroundContact = false;
            transform.GroundContactLogged = false;
            transform.GroundContactEventLogged = false;
            return;
        }

        bool belowSupport = position.Z < rest;
        bool continuingContact = transform.GroundContact
            && position.Z <= rest + GroundContactToleranceUnits;
        if (!belowSupport && !continuingContact)
        {
            return;
        }

        bool firstContact = transform.CollisionEnabled && !transform.GroundContact;
        Vector3d incomingVelocity = transform.Velocity;
        float incomingSpeed = transform.Speed;
        transform.Position = new Vector3d(position.X, position.Y, rest);
        if (transform.CollisionEnabled)
        {
            transform.GroundContact = true;

            // A mission may explicitly mark a one-shot airframe. Its first terrain contact is terminal;
            // ordinary collision-enabled helicopters retain their authored controls after contact.
            if (transform.TerminalLandingOnTerrain
                && supportSource.StartsWith("terrain", StringComparison.Ordinal))
            {
                transform.TerminalLandingLatched = true;
                // Mission 5 replaces its incoming Heli with a parked Heli at the QVM-authored
                // touchdown pose once AnimTask 3333 crosses its terminal gate. Preserve the flown
                // approach, then use that authored replacement position for the terminal contact.
                transform.TerminalLandingPosition = transform.TerminalLandingTarget ?? transform.Position;
                transform.Position = transform.TerminalLandingPosition;
                transform.Thrust = 0f;
                transform.TargetThrust = 0f;
                transform.Velocity = default;
                transform.Speed = 0f;
                transform.AngularMomentum = default;
            }
            else if (belowSupport && transform.Velocity.Z < 0d)
            {
                transform.Velocity = new Vector3d(transform.Velocity.X, transform.Velocity.Y, 0d);
            }
        }

        Note($"ground-clamp id={ignoredOwner?.Id ?? -1} z={position.Z:R} rest={rest:R}");

        if (firstContact && !transform.GroundContactEventLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            double offset = double.IsNaN(transform.GroundRestOffset)
                ? 0.0
                : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
            _logger.Warning(
                "Airframe",
                $"[AIRFRAME-CONTACT] id={ignoredOwner?.Id ?? -1} type={type} tick={SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"support={(rest / WorldScene.UnitsPerMetre) - offset:F2} "
                + $"rest={transform.Position.Z / WorldScene.UnitsPerMetre:F2} offset={offset:F2} "
                + $"incomingV=({incomingVelocity.X / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Y / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Z / WorldScene.UnitsPerMetre:F3}) "
                + $"incomingSpeed={incomingSpeed:F3} target={transform.TargetThrust:F3} "
                + $"terminal={(transform.TargetThrust <= 0f && transform.Thrust <= 0f)} "
                + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3}");
            transform.GroundContactEventLogged = true;
        }

        if (transform.TerminalLandingLatched && !transform.TerminalLandingEventLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            _logger.Warning(
                "Airframe",
                $"[AIRFRAME-LANDING-LATCH] id={ignoredOwner?.Id ?? -1} type={type} tick={SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"source={supportSource} stoppedSpeed=0.000");
            Note(
                $"heli-terminal-latch id={ignoredOwner?.Id ?? -1} source={supportSource} "
                + $"position={transform.TerminalLandingPosition}");
            transform.TerminalLandingEventLogged = true;
        }

        if (transform.GroundContact
            && transform.TargetThrust <= 0f
            && transform.Thrust <= 0f
            && !transform.GroundContactLogged)
        {
            string type = ignoredOwner?.Type.Name ?? "Airframe";
            double offset = double.IsNaN(transform.GroundRestOffset)
                ? 0.0
                : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
            _logger.Warning(
                "Airframe",
                $"[AIRFRAME-LANDING] id={ignoredOwner?.Id ?? -1} type={type} tick={SimulationTick} "
                + $"x={transform.Position.X / WorldScene.UnitsPerMetre:F2} "
                + $"y={transform.Position.Y / WorldScene.UnitsPerMetre:F2} "
                + $"z={transform.Position.Z / WorldScene.UnitsPerMetre:F2} "
                + $"support={(rest / WorldScene.UnitsPerMetre) - offset:F2} "
                + $"rest={transform.Position.Z / WorldScene.UnitsPerMetre:F2} offset={offset:F2} "
                + $"incomingV=({incomingVelocity.X / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Y / WorldScene.UnitsPerMetre:F3},"
                + $"{incomingVelocity.Z / WorldScene.UnitsPerMetre:F3}) "
                + $"incomingSpeed={incomingSpeed:F3} stoppedV=(0.000,0.000,0.000) stoppedSpeed=0.000 "
                + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3}");
            transform.GroundContactLogged = true;
        }
        else if (!transform.GroundContact)
        {
            transform.GroundContactLogged = false;
        }
    }

    private void Note(string message)
    {
        if (_diagnostics.Count >= 256)
        {
            return;
        }

        _diagnostics.Add($"tick={SimulationTick} {message}");
    }

    /// <summary>
    /// [AIRFRAME-PATH] Append-only sample of the live Heli/Plane tick. Set
    /// <c>OPENIGI_AIRFRAME_PATH_LOG</c> to a file path. One grep of the tag removes the helper.
    /// </summary>
    private void LogAirframePath(int id, string type, GameTask? target, TransformState transform)
    {
        // [AIRFRAME-PATH]
        if (transform.PhysicsTicks > 0 && transform.PhysicsTicks % 15 != 0)
        {
            return;
        }

        string? path = AirframePathLog
            ?? Environment.GetEnvironmentVariable("OPENIGI_AIRFRAME_PATH_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        double x = transform.Position.X / WorldScene.UnitsPerMetre;
        double y = transform.Position.Y / WorldScene.UnitsPerMetre;
        double z = transform.Position.Z / WorldScene.UnitsPerMetre;
        double ox = transform.OriginPosition.X / WorldScene.UnitsPerMetre;
        double oy = transform.OriginPosition.Y / WorldScene.UnitsPerMetre;
        double oz = transform.OriginPosition.Z / WorldScene.UnitsPerMetre;
        double dHoriz = Math.Sqrt(((x - ox) * (x - ox)) + ((y - oy) * (y - oy)));
        double dVert = z - oz;
        bool hasSupport = TryFindSupportHeight(
            transform.Position,
            target,
            allowDeepRecovery: true,
            out double supportHeight,
            out string supportSource);
        double support = hasSupport ? supportHeight / WorldScene.UnitsPerMetre : double.NaN;
        double offset = double.IsNaN(transform.GroundRestOffset)
            ? double.NaN
            : transform.GroundRestOffset / WorldScene.UnitsPerMetre;
        double rest = hasSupport && !double.IsNaN(offset) ? support + offset : double.NaN;
        double supportGap = hasSupport ? z - support : double.NaN;
        double restGap = !double.IsNaN(rest) ? z - rest : double.NaN;
        double vx = transform.Velocity.X / WorldScene.UnitsPerMetre;
        double vy = transform.Velocity.Y / WorldScene.UnitsPerMetre;
        double vz = transform.Velocity.Z / WorldScene.UnitsPerMetre;
        try
        {
            File.AppendAllText(
                path,
                $"[AIRFRAME-PATH] t={SimulationTick} pt={transform.PhysicsTicks} id={id} type={type} "
                + $"x={x:F2} y={y:F2} z={z:F2} dHoriz={dHoriz:F2} dVert={dVert:F2} "
                + $"vx={vx:F3} vy={vy:F3} vz={vz:F3} "
                + $"thrust={transform.Thrust:F3} target={transform.TargetThrust:F3} col={transform.CollisionEnabled} "
                + $"offset={offset:F3} support={support:F3} source={supportSource} rest={rest:F3} "
                + $"supportGap={supportGap:F3} restGap={restGap:F3} "
                + $"latched={transform.TerminalLandingLatched} "
                + $"ctl=({transform.Control.X:F3},{transform.Control.Y:F3},{transform.Control.Z:F3})"
                + Environment.NewLine);
        }
        catch (IOException)
        {
            // Path logging must never take down the tick.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Finds the height a helicopter should rest at under a body position.</summary>
    /// <param name="position">The body's current position, in world units.</param>
    /// <param name="offset">The model's measured rest height above a supporting surface.</param>
    /// <param name="rest">Receives the height the body should rest at when one is found.</param>
    /// <returns>
    /// <see langword="true"/> with <paramref name="rest"/> when a supporting surface is under the
    /// point; <see langword="false"/> when nothing holds the body there.
    /// </returns>
    /// <remarks>
    /// The terrain query mirrors retail's terrain half (<c>0x4CBE40</c>) but not the placed-model sweep
    /// (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>), so a heli landing on a pad, crate or rooftop finds nothing
    /// and would sink straight through. When the terrain misses, cast the collision index down from
    /// well above the body and rest on the solid object the ray hits instead. A NaN rest offset from
    /// the terrain measurement does not disable the object fallback: a heli resting on a pad measures
    /// its rest height from the pad's surface below the body.
    /// </remarks>
    private bool TryGetLandingHeight(Vector3d position, double offset, out double rest) =>
        TryGetLandingHeight(position, offset, ignoredOwner: null, out rest);

    private bool TryGetLandingHeight(
        Vector3d position,
        double offset,
        GameTask? ignoredOwner,
        out double rest) =>
        TryGetLandingHeight(position, offset, ignoredOwner, out rest, out _);

    private bool TryGetLandingHeight(
        Vector3d position,
        double offset,
        GameTask? ignoredOwner,
        out double rest,
        out string supportSource)
    {
        // A NaN rest offset from the terrain measurement does not disable the object fallback: a heli resting
        // on a pad measures its rest height from the pad's surface below the body.
        double restOffset = double.IsNaN(offset) ? 0.0 : offset;

        // The terrain and collision queries share one support-selection rule, so a body on an elevated pad
        // rests on the pad rather than the lower terrain underneath it.
        if (!TryFindSupportHeight(
                position,
                ignoredOwner,
                allowDeepRecovery: true,
                out double surfaceHeight,
                out supportSource))
        {
            rest = 0.0;
            supportSource = "none";
            return false;
        }

        rest = surfaceHeight + restOffset;
        return true;
    }

    private static void ApplyRetailHelicopterPhysics(
        TransformState transform,
        HelicopterPhysicsDefinition physics)
    {
        const float Gravity = 44.600887f;
        const float AngularDamping = 0.92000002f;
        Matrix3f attitude = transform.Orientation;

        // The airborne stabilizer right-multiplies a small local-X correction before forces are
        // accumulated. The first mover call initializes the body and does not execute this branch.
        if (transform.PhysicsTicks > 0)
        {
            Vector3f horizontalForward = new(attitude.M11, attitude.M21, 0f);
            float horizontalLength = horizontalForward.Length;
            if (horizontalLength != 0f)
            {
                float bankError = ((horizontalForward.X / horizontalLength) * attitude.M13)
                    + ((horizontalForward.Y / horizontalLength) * attitude.M23);
                float correction = -bankError * 0.7f * physics.Torque.Z * 0.0002908882086657217f;
                attitude *= Matrix3f.RotationX(correction);
            }
        }

        float mass = physics.Mass;
        Vector3f force = default;
        Vector3f torque = default;
        void AddForce(Vector3f value, Vector3f point)
        {
            force += value;
            torque += Vector3f.Cross(point, value);
        }

        // Rotor::Tick runs after Heli::Tick and appends its force records to the body accumulator.
        // Consume the records produced at the end of the preceding scheduler tick before adding the
        // body/control forces for this tick. Applying lift immediately used the newly levelled attitude
        // one frame too early and made the Mission 1 flyby gain too much forward and vertical speed.
        force += transform.QueuedRotorForce;
        torque += transform.QueuedRotorTorque;
        transform.QueuedRotorForce = default;
        transform.QueuedRotorTorque = default;

        if (transform.Control != default)
        {
            AddForce(
                new Vector3f(0f, transform.Control.X * physics.Torque.X * mass * -0.8f, 0f),
                new Vector3f(0f, 0f, 819.20001f));
            AddForce(
                new Vector3f(transform.Control.Y * physics.Torque.Y * mass, 0f, 0f),
                new Vector3f(0f, 0f, 409.600006f));
            AddForce(
                new Vector3f(
                    transform.Control.Z * physics.Torque.Z * transform.Thrust * mass * 0.079999998f,
                    0f,
                    0f),
                new Vector3f(0f, 12288f, 0f));
        }

        // Heli passes -worldVelocity through the current attitude into three authored local drag
        // samples. Each contributes direction * dot(normalizedAir, direction) * speed^2 * coefficient.
        // The Bell record is the captured (20 X, 3 Y, 50 Z) triplet; its modest quadratic resistance
        // is already measurable by tick 30 and prevents the long flyby from running hundreds of metres
        // ahead of its linked camera.
        Vector3f airWorld = new(
            (float)-transform.Velocity.X,
            (float)-transform.Velocity.Y,
            (float)-transform.Velocity.Z);
        Vector3f localAir = attitude.Transform(airWorld);
        float localSpeedSquared = localAir.LengthSquared;
        if (localSpeedSquared > 0f)
        {
            Vector3f airDirection = localAir / MathF.Sqrt(localSpeedSquared);
            float dynamicPressure = localSpeedSquared * 0.00032699999f;
            foreach (AerodynamicSurfaceDefinition surface in physics.Aerodynamics)
            {
                float magnitude = Vector3f.Dot(airDirection, surface.Direction)
                    * surface.Coefficient
                    * dynamicPressure;
                AddForce(surface.Direction * magnitude, surface.Point);
            }
        }

        Vector3f localAcceleration = force / mass;
        Vector3f worldAcceleration = new(
            Vector3f.Dot(attitude.Row1, localAcceleration),
            Vector3f.Dot(attitude.Row2, localAcceleration),
            Vector3f.Dot(attitude.Row3, localAcceleration) - Gravity);
        // The rigid accumulator stores world velocity as three Real32 values at +264. Position is
        // Real64, but each step adds the already-rounded velocity. Keeping velocity as an unrounded
        // Vector3d slowly pushes linked cameras metres away over a long cutscene even when every force
        // and orientation matches retail.
        transform.Velocity = new Vector3d(
            (float)(transform.Velocity.X + worldAcceleration.X),
            (float)(transform.Velocity.Y + worldAcceleration.Y),
            (float)(transform.Velocity.Z + worldAcceleration.Z));

        transform.AngularMomentum += torque;
        Vector3f dimensions = physics.Dimensions * (float)WorldScene.UnitsPerMetre;
        float inverseIx = InverseBoxInertia(mass, dimensions.Y, dimensions.Z);
        float inverseIy = InverseBoxInertia(mass, dimensions.X, dimensions.Z);
        float inverseIz = InverseBoxInertia(mass, dimensions.X, dimensions.Y);
        Vector3f localAngularRate = transform.AngularMomentum
            * new Vector3f(inverseIx, inverseIy, inverseIz);
        Vector3f worldAngularRate = new(
            Vector3f.Dot(attitude.Row1, localAngularRate),
            Vector3f.Dot(attitude.Row2, localAngularRate),
            Vector3f.Dot(attitude.Row3, localAngularRate));
        if (worldAngularRate != default)
        {
            transform.Orientation = IntegrateOrientation(attitude, worldAngularRate);
        }
        else
        {
            transform.Orientation = attitude;
        }
        transform.AngularMomentum *= AngularDamping;

        // Child rotor tasks now observe the post-integration attitude and queue local force records for
        // the next Heli::Tick, matching the retail task traversal order.
        QueueRetailRotorForces(transform, transform.Orientation, physics);

        static float InverseBoxInertia(float bodyMass, float sideA, float sideB)
        {
            float inertia = bodyMass * ((sideA * sideA) + (sideB * sideB)) * (1f / 12f);
            return inertia == 0f ? 0f : (1f / inertia) * 1.01f;
        }
    }

    private static void QueueRetailRotorForces(
        TransformState transform,
        Matrix3f attitude,
        HelicopterPhysicsDefinition physics)
    {
        const float Gravity = 44.600887f;
        float lift = physics.Mass * transform.Thrust * Gravity;
        if (lift != 0f)
        {
            // Rotor::Tick (0x42D440) takes the angle between the horizontal projection of column two
            // and column three, then stores cos(angle) * lift in local Y and lift in local Z.
            Vector3f horizontalRotor = new(attitude.M12, attitude.M22, 0f);
            Vector3f bodyUp = attitude.Column3;
            float rotorTiltCosine = 0f;
            if (horizontalRotor.LengthSquared != 0f && bodyUp.LengthSquared != 0f)
            {
                rotorTiltCosine = Vector3f.Dot(horizontalRotor, bodyUp)
                    / (horizontalRotor.Length * bodyUp.Length);
            }
            transform.QueuedRotorForce += new Vector3f(0f, rotorTiltCosine * lift, lift);
        }

        // Main- and tail-rotor axial records normally cancel on the Bell/Mil hierarchy. Preserve the
        // authored net coefficient for custom or incomplete assemblies, including its moment arm.
        if (transform.RotorAxialCoefficient != 0f && transform.Thrust != 0f)
        {
            Vector3f axial = new(
                physics.Mass * transform.Thrust * transform.RotorAxialCoefficient, 0f, 0f);
            transform.QueuedRotorForce += axial;
            transform.QueuedRotorTorque += Vector3f.Cross(new Vector3f(0f, 8192f, 0f), axial);
        }
    }

    private static Matrix3f IntegrateOrientation(Matrix3f matrix, Vector3f angularRate)
    {
        MatrixToQuaternion(matrix, out float x, out float y, out float z, out float w);
        float halfX = angularRate.X * 0.5f;
        float halfY = angularRate.Y * 0.5f;
        float halfZ = angularRate.Z * 0.5f;
        float nextX = x + (halfX * w) + (halfY * z) - (halfZ * y);
        float nextY = y + (halfY * w) + (halfZ * x) - (halfX * z);
        float nextZ = z + (halfZ * w) + (halfX * y) - (halfY * x);
        float nextW = w - (halfX * x) - (halfY * y) - (halfZ * z);
        float inverseLength = 1f / MathF.Sqrt(
            (nextX * nextX) + (nextY * nextY) + (nextZ * nextZ) + (nextW * nextW));
        x = nextX * inverseLength;
        y = nextY * inverseLength;
        z = nextZ * inverseLength;
        w = nextW * inverseLength;
        return new Matrix3f(
            1f - (2f * y * y) - (2f * z * z),
            (2f * x * y) - (2f * w * z),
            (2f * y * w) + (2f * x * z),
            (2f * w * z) + (2f * x * y),
            1f - (2f * x * x) - (2f * z * z),
            (2f * y * z) - (2f * x * w),
            (2f * x * z) - (2f * y * w),
            (2f * y * z) + (2f * x * w),
            1f - (2f * x * x) - (2f * y * y));
    }

    private static void MatrixToQuaternion(
        Matrix3f matrix,
        out float x,
        out float y,
        out float z,
        out float w)
    {
        float trace = matrix.M11 + matrix.M22 + matrix.M33;
        if (trace > 0f)
        {
            float root = MathF.Sqrt(trace + 1f);
            w = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M32 - matrix.M23) * scale;
            y = (matrix.M13 - matrix.M31) * scale;
            z = (matrix.M21 - matrix.M12) * scale;
            return;
        }

        if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
        {
            float root = MathF.Sqrt(matrix.M11 - matrix.M22 - matrix.M33 + 1f);
            x = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            y = (matrix.M12 + matrix.M21) * scale;
            z = (matrix.M13 + matrix.M31) * scale;
            w = (matrix.M32 - matrix.M23) * scale;
        }
        else if (matrix.M22 >= matrix.M33)
        {
            float root = MathF.Sqrt(matrix.M22 - matrix.M11 - matrix.M33 + 1f);
            y = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M12 + matrix.M21) * scale;
            z = (matrix.M23 + matrix.M32) * scale;
            w = (matrix.M13 - matrix.M31) * scale;
        }
        else
        {
            float root = MathF.Sqrt(matrix.M33 - matrix.M11 - matrix.M22 + 1f);
            z = root * 0.5f;
            float scale = root == 0f ? 0f : 0.5f / root;
            x = (matrix.M13 + matrix.M31) * scale;
            y = (matrix.M23 + matrix.M32) * scale;
            w = (matrix.M21 - matrix.M12) * scale;
        }
    }

    private static float MoveTowards(float value, float target, float step)
    {
        if (step <= 0f || value == target)
        {
            return target;
        }

        return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }

    private sealed class SceneState(CutSceneTask task, List<EditCameraShot> shots)
    {
        public CutSceneTask Task { get; } = task;
        public List<EditCameraShot> Shots { get; } = shots;
        public int LastShotIndex { get; set; } = -1;
        public Vector3d? LinkedPosition { get; set; }
        public Matrix3f? LinkedOrientation { get; set; }
        public Matrix3f? TargetOrientation { get; set; }
        public double TotalTime(float scale) => Shots.Sum(shot => Math.Max(0f, shot.Duration * scale));
        public List<TimedShot> Timed(float scale)
        {
            List<TimedShot> result = [];
            double time = 0;
            foreach (EditCameraShot shot in Shots)
            {
                result.Add(new TimedShot(shot, time));
                time += Math.Max(0f, shot.Duration * scale);
            }
            return result;
        }
    }

    private sealed record TimerState(DeclaredTask Task, string Run, string Reset, bool InitialRun)
    {
        public bool IsRun { get; set; } = InitialRun;
        public int Tick { get; set; }
    }

    private sealed record AnimationState(
        DeclaredTask Task,
        int TargetId,
        int Order,
        string Run,
        bool InitialRun,
        AnimDataProgram Program)
    {
        /// <summary>Running-tick counter exported as <c>AnimTask+&lt;id&gt;.nTick</c> (retail AnimTask state var).</summary>
        public int Tick { get; set; }

        /// <summary>Gets or sets whether the task drove its target on the current tick.</summary>
        public bool RanThisTick { get; set; }

        /// <summary>Gets or sets the run byte (<c>+131316</c>), exported as <c>isRun</c>; it starts at Initial run.</summary>
        public bool IsRun { get; set; } = InitialRun;
    }
    private sealed record SoundState(
        DeclaredTask Task,
        string Condition,
        string Sound,
        Vector3d Position,
        bool Simple,
        bool OneShot,
        bool RelativeToMicrophone)
    {
        public bool IsRun { get; set; }
        public bool HasPlayed { get; set; }
    }

    private sealed class CarAiState(
        DeclaredTask task,
        DeclaredTask car,
        TransformState transform,
        GraphFile graph,
        AiPatrolRoute? route)
    {
        public DeclaredTask Task { get; } = task;
        public DeclaredTask Car { get; } = car;
        public TransformState Transform { get; } = transform;
        public GraphFile Graph { get; } = graph;
        public AiPatrolRoute? Route { get; } = route;
        public bool Stopped { get; set; }

        // Retail CarAI's own fields, named by offset from 0x425DE0's object.
        public GraphNode? TargetNode { get; set; }            // +104
        public float CommandSpeedKmh { get; set; }            // +108
        public float Halt { get; set; }                       // +112
        public float RouteSpeedKmh { get; set; }              // +128
        public float DesiredSpeedKmh { get; set; }            // +100
        public float Throttle { get; set; }                   // +88, channel 1
        public float Steering { get; set; }                   // +92, channel 0
        public float Brake { get; set; }                      // +96, channel 3
        public int WaitTicks { get; set; }                    // +124
        public int Packets { get; set; }                      // +80
        public bool ChaseHeld { get; set; }                   // +116, the held target (0x426000)
        public int ChaseRetention { get; set; }               // +120, counted down from 600
    }

    /// <summary>One vehicle's death zones and how fast it moved on the last tick.</summary>
    private sealed class DeathZoneState(IReadOnlyList<VehicleDeathZone> zones)
    {
        public IReadOnlyList<VehicleDeathZone> Zones { get; } = zones;
        public Vector3d? LastPosition { get; set; }
        public double SpeedUnitsPerTick { get; set; }
    }

    /// <summary>What one armed vehicle sees, and the guns that aim from it.</summary>
    private sealed class VehicleArmament(WeaponType? legacyWeapon, List<VehicleGunState> guns)
    {
        public AiVehicleCombat Sight { get; } = new();
        public WeaponType? LegacyWeapon { get; } = legacyWeapon;
        public List<VehicleGunState> Guns { get; } = guns;

        /// <summary>Gets or sets whether the guns have been seated at least once.</summary>
        public bool Seated { get; set; }
    }

    /// <summary>One model gun's turret, cadence and ammunition.</summary>
    private sealed class VehicleGunState(VehicleGun gun)
    {
        public VehicleGun Gun { get; } = gun;
        public VehicleGunTurret Turret { get; } = new(gun.Mount);
        public int Cooldown { get; set; }
        public int Ammunition { get; set; } = gun.Mount.Ammunition;
        public int RoundsThisPull { get; set; }

        /// <summary>Gets or sets the hold of Gun_Fire, as its last run this tick left it.</summary>
        public bool Hold { get; set; }

        /// <summary>Gets or sets how many rounds the gun fired this tick.</summary>
        public int RoundsFiredThisTick { get; set; }

        /// <summary>Gets or sets a serial that advances once per round fired, for the gun flame's roll.</summary>
        public int ShotSequence { get; set; }

        /// <summary>Gets or sets the mount's position as the last seat left it (<c>+32</c>), which the gun aims from.</summary>
        public Vector3d MountPosition { get; set; }

        /// <summary>Gets or sets the mount's orientation as the last seat left it (<c>+112</c>).</summary>
        public Matrix3f MountOrientation { get; set; } = Matrix3f.Identity;

        /// <summary>Gets or sets the weapon object's position as the last seat left it (<c>+376</c>).</summary>
        public Vector3d WeaponPosition { get; set; }

        /// <summary>Gets or sets the weapon object's orientation as the last seat left it (<c>+400</c>).</summary>
        public Matrix3f WeaponOrientation { get; set; } = Matrix3f.Identity;
    }

    private sealed record TimedShot(EditCameraShot Shot, double Start);
    private sealed class AttachmentState(
        GameTask parentTask,
        TransformState parent,
        int parentRevisionAtCapture,
        Vector3d parentOriginPosition,
        Matrix3f parentOriginOrientation,
        Vector3d position,
        Matrix3f orientation)
    {
        public GameTask ParentTask { get; } = parentTask;
        public TransformState Parent { get; } = parent;
        public int ParentRevisionAtCapture { get; } = parentRevisionAtCapture;
        public Vector3d ParentOriginPosition { get; } = parentOriginPosition;
        public Matrix3f ParentOriginOrientation { get; } = parentOriginOrientation;
        public Vector3d Position { get; } = position;
        public Matrix3f Orientation { get; } = orientation;
        public Vector3d RelativePosition { get; set; } =
            parentOriginOrientation.Transform(position - parentOriginPosition);
    }

    private sealed class TransformState(Vector3d position, Matrix3f orientation)
    {
        public Vector3d Position { get; set; } = position;
        public Vector3d PreviousPosition { get; set; } = position;
        public Vector3d OriginPosition { get; } = position;
        public Matrix3f Orientation { get; set; } = orientation;
        public Matrix3f PreviousOrientation { get; set; } = orientation;
        public Matrix3f AuthoredOrientation { get; } = orientation;
        public Vector3f Euler { get; set; }
        public float Speed { get; set; }
        public bool IsDynamic { get; set; }
        public RailroadPath? Railroad { get; set; }
        public float TrackPosition { get; set; }
        public float PreviousTrackPosition { get; set; }
        public int DynamicRevision { get; set; }
        public int LastSimulationOrder { get; set; } = int.MinValue;
        public float Thrust { get; set; }
        public float TargetThrust { get; set; }
        public float OriginalThrust { get; set; }
        public float ThrustStep { get; set; }
        public float MaxSpeed { get; set; }
        public TransformState? TrainRoot { get; set; }
        public float TrainOffset { get; set; }
        public Vector3f Control { get; set; }
        public float Steering { get; set; }
        public float TargetSteering { get; set; }
        public bool ParkingBrake { get; set; }
        public CarPhysicsDefinition? CarPhysics { get; set; }

        /// <summary>The car's retail rigid body, once its controller's set-up packet has created it.</summary>
        public RetailCarBody? RetailBody { get; set; }

        /// <summary>The helicopter's retail rigid body, once its AnimTask has first spoken.</summary>
        public RetailHeliBody? RetailHeli { get; set; }

        /// <summary>Gets or sets the plane's retail body, once its AnimTask has spoken.</summary>
        public RetailPlaneBody? RetailPlane { get; set; }


        public CarMotionState CarMotion { get; } = new();
        public PlanePhysicsDefinition? PlanePhysics { get; set; }
        public GameTask? VehicleLatchedObstacle { get; set; }
        public bool VehicleGateReleased { get; set; }
        public float Heading { get; set; }
        public Vector3d Velocity { get; set; }
        public HelicopterPhysicsDefinition? HelicopterPhysics { get; set; }
        public double GroundRestOffset { get; set; } = double.NaN;
        public bool GroundContact { get; set; }
        public bool GroundContactEventLogged { get; set; }
        public bool GroundContactLogged { get; set; }
        public bool CollisionEnabled { get; set; }
        public bool TerminalLandingOnTerrain { get; set; }
        public bool TerminalLandingLatched { get; set; }
        public bool TerminalLandingEventLogged { get; set; }
        public Vector3d? TerminalLandingTarget { get; set; }
        public Vector3d TerminalLandingPosition { get; set; }
        public float RotorPhase { get; set; }
        public int RotorPhaseTick { get; set; }
        public Vector3f AngularMomentum { get; set; }
        public int PhysicsTicks { get; set; }
        public float RotorAxialCoefficient { get; set; }
        public Vector3f QueuedRotorForce { get; set; }
        public Vector3f QueuedRotorTorque { get; set; }
    }

}

/// <summary>The camera and screen treatment produced by the active authored cutscene shot.</summary>
public sealed record CutsceneFrame(
    Camera3D Camera,
    float FieldOfView,
    float ViewportHeightFactor,
    string Filter,
    float FilterNoise,
    Vector3f FilterColor,
    float TimeOfDay,
    CutSceneTask Scene,
    int ShotIndex)
{
    /// <summary>Gets whether an authored binocular holder owns the retail optics overlay.</summary>
    public bool BinocularOverlayVisible { get; init; }
}

/// <summary>A live target pose supplied to a cutscene in engine units.</summary>
public readonly record struct CutsceneTargetPose(Vector3d Position, Matrix3f Orientation);

/// <summary>The exact repeated model transforms used to render one retail Rotor primitive.</summary>
public readonly record struct RotorDrawState(
    Vector3d Position,
    Matrix3f BaseOrientation,
    int SampleCount,
    float FirstAngle,
    float SampleStep,
    float Tilt)
{
    /// <summary>Returns one blade sample as base · phase-X · aerodynamic-tilt-Z.</summary>
    public Matrix3f GetOrientation(int sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sample, SampleCount);

        return BaseOrientation
            * Matrix3f.RotationX(FirstAngle + (sample * SampleStep))
            * Matrix3f.RotationZ(Tilt);
    }
}

/// <summary>One retail ConditionalSound edge owned by a cutscene timeline.</summary>
/// <param name="Task">The ConditionalSound task that owns a non-simple voice.</param>
/// <param name="Name">The authored SoundDef name.</param>
/// <param name="Position">The world emitter, or null for a microphone-relative sound.</param>
/// <param name="Playing">True on a rising edge; false when a condition-owned voice must stop.</param>
/// <param name="Simple">Whether the rising edge is fire-and-forget.</param>
public readonly record struct CutsceneSoundEvent(
    GameTask Task,
    string Name,
    Vector3d? Position,
    bool Playing,
    bool Simple);

internal sealed record EditCameraShot(
    Vector3d Position,
    float Alpha,
    float Beta,
    float Gamma,
    float FieldOfView,
    float Duration,
    int LinkTaskId,
    bool UpdateLink,
    int TargetTaskId,
    bool UpdateTarget,
    bool SmoothToNext,
    float TimeOfDay,
    string Filter,
    float Noise,
    Vector3f FilterColor,
    float Shake,
    Vector3d LinkOrigin,
    Matrix3f LinkOriginOrientation);
