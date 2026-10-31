using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Net;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;
using static OpenIGI.Game.World.DeclaredTaskValues;

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

    /// <summary>The category the airframe path samples are logged under, at Trace.</summary>
    public const string AirframePathLogCategory = "Airframe.Path";

    private readonly CutsceneContext _context;
    private readonly ScriptVarRegistry _variables;
    private readonly List<ConditionalContainerTask> _containers = [];
    private readonly List<CutsceneSceneState> _scenes = [];
    private readonly List<CutsceneTimerState> _timers = [];
    private readonly List<CutsceneAnimationState> _animations = [];
    private readonly List<CutsceneSoundState> _sounds = [];
    private readonly List<CarAiState> _carAis = [];
    private readonly List<DeclaredTask> _binocularOverlayHolders = [];
    private readonly Func<string, ScriptArguments, QvmValue?>? _hostNative;
    private readonly Action<CutsceneSoundEvent>? _soundChanged;
    private readonly LevelUpdateWalker _walker;
    private readonly Dictionary<GameTask, int> _lastVehicleTick = [];
    private readonly CutsceneAttachments _attachments;
    private readonly CutsceneGroundSupport _ground;
    private readonly CutsceneVehicleBodies _bodies;
    private readonly CutsceneTransformCapture _capture;
    private readonly CutsceneAnimationPackets _packets;
    private readonly CutsceneVehicleArmament _armament;
    private readonly CutsceneVehicleParts _parts;
    private readonly CutsceneCarAi _carAi;
    private readonly CutsceneVehicleDraw _draw;
    private readonly CutsceneCameraFrames _camera;
    private bool _breakRequested;

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
        get => _context.CollisionIndex;
        set => _context.CollisionIndex = value;
    }

    /// <summary>Gets or sets the destruction state whose vehicle origins follow live CarAI transforms.</summary>
    public DestructibleObjectRegistry? Destructibles
    {
        get => _context.Destructibles;
        set => _context.Destructibles = value;
    }

    /// <summary>Supplies the living player eye used by firing vehicles.</summary>
    public Func<Vector3d?>? VehicleCombatTarget
    {
        get => _armament.VehicleCombatTarget;
        set => _armament.VehicleCombatTarget = value;
    }

    /// <summary>
    /// Gets or sets where a vehicle's eye tests the same target: the point the human reports to a detection sweep
    /// (message <c>0x489620</c>), which the soldiers' sweep uses too. Falls back to <see cref="VehicleCombatTarget"/>.
    /// </summary>
    public Func<Vector3d?>? VehicleSightTarget
    {
        get => _armament.VehicleSightTarget;
        set => _armament.VehicleSightTarget = value;
    }

    /// <summary>Tests the firing vehicle's muzzle-to-player sight segment.</summary>
    public Func<GameTask, Vector3d, Vector3d, bool>? VehicleCombatLineOfSight
    {
        get => _armament.VehicleCombatLineOfSight;
        set => _armament.VehicleCombatLineOfSight = value;
    }

    /// <summary>Receives a vehicle missile only after sight, retention and gun cadence allow it.</summary>
    public Action<VehicleCombatShot>? VehicleCombatShotFired
    {
        get => _armament.VehicleCombatShotFired;
        set => _armament.VehicleCombatShotFired = value;
    }

    /// <inheritdoc cref="CutsceneVehicleArmament.ConfigureVehicleCombat"/>
    public void ConfigureVehicleCombat(GameTask vehicle, WeaponType weapon) => _armament.ConfigureVehicleCombat(vehicle, weapon);

    /// <inheritdoc cref="CutsceneVehicleArmament.ConfigureVehicleGuns"/>
    public void ConfigureVehicleGuns(GameTask vehicle, IReadOnlyList<VehicleGun> guns) => _armament.ConfigureVehicleGuns(vehicle, guns);

    /// <inheritdoc cref="CutsceneVehicleBodies.VehicleSoundSources"/>
    public IEnumerable<VehicleSoundSource> VehicleSoundSources() => _bodies.VehicleSoundSources();

    /// <inheritdoc cref="CutsceneVehicleArmament.VehicleGunSounds"/>
    public IEnumerable<VehicleGunSound> VehicleGunSounds() => _armament.VehicleGunSounds();

    /// <inheritdoc cref="CutsceneVehicleArmament.ConfigureDeathZones"/>
    public void ConfigureDeathZones(GameTask vehicle, IReadOnlyList<VehicleDeathZone> zones) => _armament.ConfigureDeathZones(vehicle, zones);

    /// <inheritdoc cref="CutsceneVehicleArmament.TryRunOver"/>
    public bool TryRunOver(Vector3d humanOrigin, out GameTask? vehicle) => _armament.TryRunOver(humanOrigin, out vehicle);

    /// <inheritdoc cref="CutsceneVehicleArmament.GunTurret"/>
    public VehicleGunTurret? GunTurret(GameTask vehicle, int index) => _armament.GunTurret(vehicle, index);

    /// <summary>Settles every collision-enabled helicopter onto whatever is under it.</summary>
    /// <remarks>
    /// Separate from <see cref="CollisionIndex"/> because it is a one-shot placement pass, not a
    /// consequence of the index being available: the index is what every vehicle's ground query
    /// reads, and only a helicopter authored with collision wants to be re-seated on it.
    /// </remarks>
    public void RestHelicoptersOnSupport() => _ground.RestCollisionHelisOnSupport();

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
        IOpenIgiLogger? logger = null,
        LevelUpdateWalker? walker = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        if (walker is not null && !ReferenceEquals(walker.Root, levelRoot))
        {
            throw new ArgumentException("The walk must be over the same level.", nameof(walker));
        }

        _walker = walker ?? new LevelUpdateWalker(levelRoot);
        _variables = variables;
        _hostNative = native;
        _soundChanged = soundChanged;
        _context = new CutsceneContext(
            levelRoot,
            new LevelExpressionRuntime(variables, InvokeNative),
            vehiclePhysics,
            terrain,
            logger ?? NullLogger.Instance);
        _attachments = new CutsceneAttachments(_context);
        _ground = new CutsceneGroundSupport(_context);
        _bodies = new CutsceneVehicleBodies(_context, _ground, vehicleObstacleHit, vehicleObstacleReleased);
        _capture = new CutsceneTransformCapture(_context, _attachments, modelLength, modelPivot);
        _armament = new CutsceneVehicleArmament(_context, _carAis, _animations);
        _parts = new CutsceneVehicleParts(_context, _armament);
        _packets = new CutsceneAnimationPackets(_context, _bodies, _ground, _parts);
        _carAi = new CutsceneCarAi(_context, _walker, _bodies, _armament, _carAis);
        _draw = new CutsceneVehicleDraw(_context, _attachments, _bodies, _armament, _parts);
        _camera = new CutsceneCameraFrames(_context, _attachments);
        List<CutSceneTask> cutscenes = [];

        // Every detection record takes the next value of the global round-robin counter as it initialises: the camera
        // lenses at construction, then HumanAIs and vehicles in tree order at post-load (AiVisionPhases). Runtime
        // re-initialisations carry on from where the load left the counter.
        (IReadOnlyDictionary<GameTask, int> visionPhases, int visionCounter) = AiVisionPhases.Assign(levelRoot);
        _armament.StartVisionCounter(visionCounter);
        int taskOrder = 0;
        foreach (GameTask task in levelRoot.Descendants())
        {
            int order = taskOrder++;
            _context.TaskOrder[task] = order;
            if (task.Type.Name is "Car" or "Heli")
            {
                _armament.RegisterVision(task, visionPhases);
            }
            if (task is ConditionalContainerTask { SettlesOutsideTheTick: true } container)
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

            _capture.Capture(task);

            if (task is DeclaredTask declared)
            {
                if (string.Equals(task.Type.Name, "LevelTimer", StringComparison.Ordinal))
                {
                    CutsceneTimerState timer = new(
                        declared,
                        TextOrEmpty(declared, "On"),
                        TextOrEmpty(declared, "Reset"),
                        BoolStrict(declared, "Initial run"));
                    _timers.Add(timer);
                    variables.BindBool(declared, "isRun", () => timer.IsRun, value => timer.IsRun = value);
                    variables.BindInt(declared, "nTick", () => timer.Tick, value => timer.Tick = value);
                }
                else if (string.Equals(task.Type.Name, "AnimTask", StringComparison.Ordinal)
                    && declared.TryGetValue("AnimData", out object? data)
                    && data is IReadOnlyList<AnimDataEntry> entries)
                {
                    int target = IntStrict(declared, "TargetID", IntStrict(declared, "Target ID", -1));
                    CutsceneAnimationState animation = new(
                        declared,
                        target,
                        order,
                        TextOrEmpty(declared, "Run"),
                        BoolStrict(declared, "Initial run"),
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
                    _sounds.Add(new CutsceneSoundState(
                        declared,
                        FirstTextNamed(declared, "condition"),
                        FirstTextNamed(declared, "sound"),
                        Vector(declared, "Position"),
                        BoolStrict(declared, "Simple"),
                        BoolStrict(declared, "OneShot"),
                        BoolStrict(declared, "Relative to microphone")));
                }
                else if (string.Equals(task.Type.Name, "BinocularOverlayHolder", StringComparison.Ordinal))
                {
                    _binocularOverlayHolders.Add(declared);
                }
            }
        }

        foreach (CutsceneTimerState timer in _timers)
        {
            _walker.Bind(timer.Task, _ => TickTimer(timer));
        }

        foreach (CutsceneAnimationState animation in _animations)
        {
            _walker.Bind(animation.Task, _ => TickAnimation(animation));
        }

        foreach (CutsceneSoundState sound in _sounds)
        {
            _walker.Bind(sound.Task, _ => TickSound(sound));
            _walker.RegisterDeactivation(sound.Task, _ => SetSoundRun(sound, run: false));
        }

        foreach (GameTask task in levelRoot.Descendants())
        {
            if (string.Equals(task.Type.Name, "Car", StringComparison.Ordinal))
            {
                _walker.Bind(task, TickCar);
            }
            else if (string.Equals(task.Type.Name, "Heli", StringComparison.Ordinal))
            {
                _walker.Bind(task, TickHeli);
            }
        }

        _walker.ContainerScript = (container, script) => _context.Expressions.Execute(script, container);
        _walker.HoldContainer = RetainsLevelFourOutro;

        // Capture the whole authored hierarchy before parking a vehicle. Moving a
        // parent first makes its still-authored child positions acquire the parking
        // displacement as a permanent local offset (Level 3's pilot floats above it).
        _ground.RestCollisionHelisOnSupport();

        // EditCamera caches every Link task's initial world transform during retail
        // UpdateInternalData.  Build those keys only after all task transforms exist: Train tasks do
        // not author a world Position at all, and their initial frame must first be evaluated from the
        // railroad.  Reading the keys inline above silently cached (0,0,0)/identity for linked cars.
        foreach (CutSceneTask cutscene in cutscenes)
        {
            CutsceneSceneState scene = new(cutscene, _camera.ReadShots(cutscene));
            _scenes.Add(scene);
            _walker.Bind(cutscene, _ => TickScene(scene));
            _walker.RegisterDeactivation(cutscene, _ => DeactivateScene(scene));
        }

        // Sibling humans acquire a moving parent from their live collision contact.
        // Proximity at construction can capture an inactive vehicle's parked pose,
        // baking its height difference into the actor before the scene registers.

        // The gate is prepared by the mission loader before this runtime is created. Refreshing here
        // replaces its former steady-game snapshot with the authored initial registration state.
        RefreshContainers(executeTransitions: true);
        foreach (CutsceneSceneState state in _scenes)
        {
            if (!ConditionalContainerGate.IsTaskActive(state.Task))
            {
                continue;
            }

            bool run = string.IsNullOrWhiteSpace(state.Task.Run)
                ? state.Task.InitialRun
                : _context.Expressions.EvaluateCondition(
                    state.Task.Run, emptyValue: state.Task.InitialRun, state.Task);
            state.Task.IsRun = run;
            if (run)
            {
                ExecuteSceneExpression(state.Task, state.Task.StartExpression);
            }
        }

        // An AnimTask's PostLoad (0x4F3940) seeks its programme to zero (0x4F3EF0(anim, 0)), which sends one
        // packet with counter 0: the receiver's set-up, which also lets vehicle handlers install hold
        // sentinels into the aliased channel block. Its first RUNNING walk sends counter 0 again (see
        // TickAnimation), so the first physics packet comes on the walk after. Sending both here made every
        // vehicle an AnimTask drives start a walk early: mission 8's intro truck 1501 took 750 steps where
        // the original takes 749, the last one past the scene's nTick < 750 cut-off.
        foreach (CutsceneAnimationState animation in _animations)
        {
            if (!ConditionalContainerGate.IsTaskActive(animation.Task))
            {
                continue;
            }
            animation.Program.SeekTo(0);
            _packets.Apply(animation.TargetId, animation.Order, animation.Program);
        }
        UpdateFrame();
    }

    /// <summary>Gets the number of <see cref="Tick"/> calls applied since construction.</summary>
    public int SimulationTick => _context.SimulationTick;

    /// <summary>
    /// Ride / rest / attach notes for tests and playtest logs (task id + tick). Capped.
    /// </summary>
    public IReadOnlyList<string> Diagnostics => _context.Diagnostics;

    /// <summary>Gets whether an authored cutscene currently owns the camera.</summary>
    public bool IsPlaying => ActiveScene is not null;

    /// <summary>Gets the current authored camera and presentation settings.</summary>
    public CutsceneFrame? Frame { get; private set; }

    /// <summary>Supplies live poses for targets owned by another simulation, such as animated humans.</summary>
    public void SetTargetPoseResolver(Func<int, CutsceneTargetPose?>? resolver) => _camera.TargetPoseResolver = resolver;

    /// <inheritdoc cref="CutsceneVehicleBodies.ConfigureRetailCars"/>
    public void ConfigureRetailCars(
        IRetailCarWorld world,
        Func<string, OpenIGI.Formats.World.MefModel?> models,
        Func<string, MagicObjectDefinition?>? magic = null) =>
        _bodies.ConfigureRetailCars(world, models, magic);

    /// <inheritdoc cref="CutsceneCarAi.ConfigureCarAi"/>
    public void ConfigureCarAi(IReadOnlyDictionary<int, GraphFile> graphs) => _carAi.ConfigureCarAi(graphs);

    /// <inheritdoc cref="CutsceneCarAi.CarAiTargetNode"/>
    public int? CarAiTargetNode(GameTask car) => _carAi.CarAiTargetNode(car);

    /// <inheritdoc cref="CutsceneVehicleBodies.VehicleOccupants"/>
    public IReadOnlyList<VehicleOccupant> VehicleOccupants() => _bodies.VehicleOccupants();

    /// <summary>Marks an explicitly configured helicopter for terminal terrain landing.</summary>
    /// <param name="task">The helicopter task whose first terrain contact is terminal.</param>
    /// <remarks>
    /// Mission setup supplies this state for authored one-shot landings. The generic cutscene runtime
    /// does not infer terminal behavior from a task id, so unrelated helicopters continue their own
    /// animation after touching terrain.
    /// </remarks>
    public void EnableTerminalTerrainLanding(GameTask task)
    {
        CutsceneTransformState state = TerminalLandingState(task);
        state.TerminalLandingOnTerrain = true;
    }

    /// <summary>Marks a helicopter for terminal landing at a separately authored final pose.</summary>
    /// <param name="task">The helicopter task whose first terrain contact is terminal.</param>
    /// <param name="authoredTouchdown">The retail task whose authored position is the landing target.</param>
    public void EnableTerminalTerrainLanding(GameTask task, GameTask authoredTouchdown)
    {
        CutsceneTransformState state = TerminalLandingState(task);
        ArgumentNullException.ThrowIfNull(authoredTouchdown);
        if (!_context.TaskTransforms.TryGetValue(authoredTouchdown, out CutsceneTransformState? touchdown))
        {
            throw new ArgumentException(
                "Terminal terrain landing requires a captured touchdown task.",
                nameof(authoredTouchdown));
        }

        state.TerminalLandingOnTerrain = true;
        state.TerminalLandingTarget = touchdown.OriginPosition;
    }

    private CutsceneTransformState TerminalLandingState(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state)
            || !string.Equals(task.Type?.Name, "Heli", StringComparison.Ordinal))
        {
            throw new ArgumentException("Terminal terrain landing requires a captured Heli task.", nameof(task));
        }

        return state;
    }

    // A co-op server's scenes, run and finished, two bits a scene in declaration order: a client's scenes run when the
    // server's do rather than when their own expressions say.
    private bool[]? _mirroredRuns;
    private bool[]? _mirroredFinished;

    /// <summary>Gets or sets whether the scenes run when a co-op server's do, rather than when their own expressions hold.</summary>
    public bool Mirrored { get; set; }

    /// <summary>Writes which scenes are running and which have finished, for a co-op server's clients.</summary>
    /// <param name="writer">Where to.</param>
    public void WriteRuns(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt((uint)_scenes.Count);
        foreach (CutsceneSceneState scene in _scenes)
        {
            writer.WriteBool(scene.Task.IsRun);
            writer.WriteBool(scene.Task.IsFinished);
        }
    }

    /// <summary>Takes a co-op server's scenes, as <see cref="WriteRuns"/> wrote them.</summary>
    /// <param name="reader">The scenes.</param>
    public void ReadRuns(ref BitReader reader)
    {
        int count = (int)reader.ReadVarUInt();
        bool[] runs = new bool[_scenes.Count];
        bool[] finished = new bool[_scenes.Count];
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            bool run = reader.ReadBool();
            bool done = reader.ReadBool();
            if (i < runs.Length)
            {
                runs[i] = run;
                finished[i] = done;
            }
        }

        _mirroredRuns = runs;
        _mirroredFinished = finished;
    }

    /// <summary>Gets the currently running scene, if any.</summary>
    public CutSceneTask? ActiveScene => _scenes.LastOrDefault(
        static state => state.Task.IsRun && ConditionalContainerGate.IsTaskActive(state.Task))?.Task;

    /// <summary>Queues the retail break-cutscene key for the next logic tick.</summary>
    /// <remarks>A co-op client's scenes are its server's to break: it asks nothing of its own.</remarks>
    public void RequestBreak() => _breakRequested = !Mirrored;

    /// <summary>Advances all cutscene-owned tasks by one fixed logic tick.</summary>
    public void Tick()
    {
        _context.SimulationTick++;
        foreach (CutsceneAnimationState animation in _animations)
        {
            animation.RanThisTick = false;
        }

        // One walk of the level tree in authored order: each task runs its own update where the tree puts it,
        // not grouped by type.
        _walker.Tick();
        UpdateFrame();
    }

    /// <summary>
    /// Gets the level's update walk. Other level systems register the tasks they own into it, so that one walk
    /// in authored order ticks them all.
    /// </summary>
    public LevelUpdateWalker Walker => _walker;

    /// <summary>A LevelTimer's update (<c>0x51BE10</c>): children first, then On, Reset and the tick count.</summary>
    private void TickTimer(CutsceneTimerState timer)
    {
        _walker.RunChildren(timer.Task);
        if (_context.Expressions.EvaluateCondition(timer.Reset, emptyValue: false, timer.Task))
        {
            timer.Tick = 0;
        }

        timer.IsRun = string.IsNullOrWhiteSpace(timer.Run)
            ? timer.InitialRun
            : _context.Expressions.EvaluateCondition(timer.Run, emptyValue: timer.InitialRun, timer.Task);
        if (timer.IsRun)
        {
            timer.Tick++;
        }
    }

    /// <summary>An AnimTask's update (<c>0x4F3B40</c>): the run expression, then the next record step's packet.</summary>
    private void TickAnimation(CutsceneAnimationState animation)
    {
        // 0x4F3B40: a Run expression rewrites the run byte every tick; without one the byte keeps whatever it holds
        // -- its Initial run, or what a script wrote to isRun. The task steps while the byte is set.
        if (!string.IsNullOrWhiteSpace(animation.Run))
        {
            animation.IsRun = _context.Expressions.EvaluateCondition(animation.Run, emptyValue: animation.IsRun, animation.Task);
        }

        bool run = animation.IsRun;
        animation.RanThisTick = run;
        if (!run)
        {
            return;
        }

        animation.Tick++;
        if (animation.Program.Tick < 0)
        {
            // A task that was out of the world at load has its PostLoad seek on its first run.
            animation.Program.SeekTo(0);
            _packets.Apply(animation.TargetId, animation.Order, animation.Program);
        }

        // 0x4F3EF0(anim, cur + 1): one step, whose packet carries the counter before it moves on -- so the first
        // running walk sends counter 0 (a set-up) and physics starts on the next.
        animation.Program.Step();
        _packets.Apply(animation.TargetId, animation.Order, animation.Program);
        animation.Program.CountTick();
    }

    /// <summary>A CutScene's update (<c>0x4F51D0</c>): its children (its AnimTasks) first, then its own run.</summary>
    private void TickScene(CutsceneSceneState state)
    {
        CutSceneTask scene = state.Task;
        _walker.RunChildren(scene);
        bool wasRun = scene.IsRun;
        if (_context.Expressions.EvaluateCondition(scene.Reset, emptyValue: false, scene))
        {
            scene.Ticks = 0;
            scene.IsFinished = false;
        }

        int mirrored = Mirrored ? _scenes.IndexOf(state) : -1;
        if (mirrored >= 0)
        {
            // A co-op client's scene runs when the server's does, and has finished when the server's has.
            scene.IsRun = _mirroredRuns is { } runs && mirrored < runs.Length && runs[mirrored];
            if (_mirroredFinished is { } finished && mirrored < finished.Length && finished[mirrored])
            {
                scene.IsFinished = true;
            }
        }
        else
        {
            scene.IsRun = string.IsNullOrWhiteSpace(scene.Run)
                ? scene.InitialRun && !scene.IsFinished
                : _context.Expressions.EvaluateCondition(scene.Run, emptyValue: false, scene);
        }

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

            // The camera this tick is set from the tick the scene is ON, not the one it moves to: retail's first
            // running tick submits time nought (0x4F51D0 -> 0x4F55D0, probed on mission 12), the same level tick
            // the gondola makes its first move in. Built from the incremented count, the port's camera ran a tick
            // ahead of everything else in the level.
            state.FrameTicks = scene.Ticks;
            int delta = string.IsNullOrWhiteSpace(scene.TimeDelta)
                ? 1
                : (int)(_context.Expressions.EvaluateSingle(scene.TimeDelta, owner: scene) * Frequency);
            scene.Ticks = Math.Max(0, scene.Ticks + delta);
        }

        if (wasRun && !scene.IsRun)
        {
            ExecuteSceneExpression(scene, scene.StopExpression);
        }
    }

    /// <summary>A scene taken out of the world stops.</summary>
    private void DeactivateScene(CutsceneSceneState state)
    {
        if (state.Task.IsRun)
        {
            state.Task.IsRun = false;
            ExecuteSceneExpression(state.Task, state.Task.StopExpression);
        }
    }

    /// <summary>A ConditionalSound's update (<c>0x4250D0</c>): its condition's edges, then its children.</summary>
    private void TickSound(CutsceneSoundState sound)
    {
        bool run = _context.Expressions.EvaluateCondition(sound.Condition, emptyValue: false, sound.Task);
        SetSoundRun(sound, run);
        _walker.RunChildren(sound.Task);
    }

    private void SetSoundRun(CutsceneSoundState sound, bool run)
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
        if (_lastVehicleTick.TryGetValue(car, out int last) && last == _context.SimulationTick)
        {
            return;
        }

        _lastVehicleTick[car] = _context.SimulationTick;
        _armament.TickVehicleVision(car);
        _walker.RunChildren(car);
        _armament.TickVehicleCombat(car);
        _armament.TickDeathZones(car);
    }

    /// <summary>A Heli's update (<c>0x4316D0</c>): its children -- its AnimTask flies it -- then its gun.</summary>
    private void TickHeli(GameTask heli)
    {
        _parts.TickDoor(heli);
        _armament.TickVehicleVision(heli);
        _walker.RunChildren(heli);
        _armament.TickVehicleCombat(heli);
        _armament.TickDeathZones(heli);
    }

    /// <summary>Serves cutscene-specific level-expression natives to sibling mission runtimes.</summary>
    public QvmValue? TryInvokeNative(string name, ScriptArguments arguments) => InvokeNative(name, arguments);

    /// <summary>Evaluates one scene's camera frame at an arbitrary scene time in seconds.</summary>
    /// <remarks>
    /// Uses exactly the production interpolation path (same knot selection, Hermite basis, link/
    /// target remap and presentation fields) so tests can compare the emitted camera against the
    /// authored EditCamera data without driving the whole level simulation. Callers must accumulate
    /// shot start times exactly like <see cref="CutsceneSceneState.Timed"/> (double += float duration) so
    /// knot times match bit-for-bit and the current-shot scan selects the asserted shot.
    /// </remarks>
    internal CutsceneFrame? EvaluateSceneFrame(CutSceneTask scene, double time)
    {
        CutsceneSceneState? state = _scenes.FirstOrDefault(item => ReferenceEquals(item.Task, scene));
        return state is null ? null : _camera.BuildFrame(state, time);
    }

    /// <summary>Clears one-tick cutscene input after every level expression has had a chance to read it.</summary>
    public void FinishExpressionTick() => _breakRequested = false;

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetDrawTransform"/>
    public bool TryGetDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation) =>
        _draw.TryGetDrawTransform(task, out position, out orientation);

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetDynamicVelocity"/>
    public bool TryGetDynamicVelocity(GameTask task, out Vector3d velocity) => _draw.TryGetDynamicVelocity(task, out velocity);

    /// <inheritdoc cref="CutsceneVehicleBodies.BindVehicleWheels"/>
    public void BindVehicleWheels(WorldScene scene, Func<string, float>? modelRadius = null) =>
        _bodies.BindVehicleWheels(scene, modelRadius);

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetPlacementDrawTransform(GameTask, Vector3d, Matrix3f, out Vector3d, out Matrix3f)"/>
    public bool TryGetPlacementDrawTransform(
        GameTask task,
        Vector3d placementPosition,
        Matrix3f placementOrientation,
        out Vector3d position,
        out Matrix3f orientation) =>
        _draw.TryGetPlacementDrawTransform(task, placementPosition, placementOrientation, out position, out orientation);

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetPlacementDrawTransform(WorldPlacement, out Vector3d, out Matrix3f)"/>
    public bool TryGetPlacementDrawTransform(WorldPlacement placement, out Vector3d position, out Matrix3f orientation) =>
        _draw.TryGetPlacementDrawTransform(placement, out position, out orientation);

    /// <inheritdoc cref="CutsceneVehicleParts.Configure"/>
    public void ConfigureVehicleParts(WorldScene scene, Func<string, MagicObjectDefinition?> magic, Func<MagicObjectDefinition, string> taskTypeName) =>
        _parts.Configure(scene, magic, taskTypeName);

    /// <inheritdoc cref="CutsceneVehicleParts.Made"/>
    public void VehiclePartMade(WorldPlacement placement) => _parts.Made(placement);

    /// <summary>Gets or sets the game's stream, which the vehicles' guns and the helicopters' rocket pods draw from.</summary>
    public IgiRandom? VehicleRandom
    {
        get => _parts.Random;
        set
        {
            _parts.Random = value;
            _armament.Random = value;
        }
    }

    /// <inheritdoc cref="CutsceneVehicleArmament.BindVehicleGunPlacements"/>
    public void BindVehicleGunPlacements(WorldScene scene) => _armament.BindVehicleGunPlacements(scene);

    /// <inheritdoc cref="CutsceneVehicleArmament.VehicleGunVisuals"/>
    public IEnumerable<VehicleGunVisual> VehicleGunVisuals() => _armament.VehicleGunVisuals();

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetWheelDraw"/>
    public bool TryGetWheelDraw(WorldPlacement placement, out Matrix3f rotation) => _draw.TryGetWheelDraw(placement, out rotation);

    /// <inheritdoc cref="CutsceneVehicleDraw.TryGetRotorDrawState"/>
    public bool TryGetRotorDrawState(WorldPlacement placement, out RotorDrawState draw) => _draw.TryGetRotorDrawState(placement, out draw);

    /// <inheritdoc cref="CutsceneAttachments.TryGetAttachedDrawTransform"/>
    public bool TryGetAttachedDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation) =>
        _attachments.TryGetAttachedDrawTransform(task, out position, out orientation);

    /// <inheritdoc cref="CutsceneAttachments.TryAttachActorToTask"/>
    public bool TryAttachActorToTask(GameTask task, GameTask parent, Vector3d position, Matrix3f orientation) =>
        _attachments.TryAttachActorToTask(task, parent, position, orientation);

    /// <inheritdoc cref="CutsceneAttachments.SetPlatformPose"/>
    public void SetPlatformPose(GameTask platform, Vector3d position, Matrix3f orientation) =>
        _attachments.SetPlatformPose(platform, position, orientation);

    /// <inheritdoc cref="CutsceneAttachments.TryGetAttachmentParent"/>
    public bool TryGetAttachmentParent(GameTask task, out GameTask? parent) => _attachments.TryGetAttachmentParent(task, out parent);

    /// <inheritdoc cref="CutsceneAttachments.TryDetachInactiveAttachment"/>
    public bool TryDetachInactiveAttachment(GameTask task) => _attachments.TryDetachInactiveAttachment(task);

    /// <inheritdoc cref="CutsceneAttachments.AdvanceAttachedActor"/>
    public bool AdvanceAttachedActor(GameTask task, Vector3f rootMotion) => _attachments.AdvanceAttachedActor(task, rootMotion);

    /// <inheritdoc cref="CutsceneVehicleBodies.RetailVehicleModels"/>
    public IReadOnlyList<RetailRayModel> RetailVehicleModels() => _bodies.RetailVehicleModels();

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

            _context.Expressions.Execute(started ? container.RunAtStart : container.RunAtStop, container);
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
            || !_context.Expressions.EvaluateCondition("!HumanPlayer_0.isDead", emptyValue: true))
        {
            return false;
        }

        foreach (CutsceneSceneState scene in _scenes)
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

        _context.Expressions.Execute(expression, scene);
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
        CutsceneSceneState? state = _scenes.LastOrDefault(
            static item => item.Task.IsRun && ConditionalContainerGate.IsTaskActive(item.Task));
        if (state is null || state.Shots.Count == 0)
        {
            Frame = null;
            return;
        }

        CutsceneFrame? frame = _camera.BuildFrame(state, (state.FrameTicks ?? state.Task.Ticks) / (double)Frequency);
        Frame = frame is null
            ? null
            : frame with { BinocularOverlayVisible = IsBinocularOverlayVisible() };
    }

    private bool IsBinocularOverlayVisible()
    {
        foreach (DeclaredTask holder in _binocularOverlayHolders)
        {
            if (ConditionalContainerGate.IsTaskActive(holder)
                && _context.Expressions.EvaluateCondition(
                    TextOrEmpty(holder, "Visible expression"), emptyValue: false, holder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the AnimTasks' states, in their order, for the suites that read them.</summary>
    internal IReadOnlyList<CutsceneAnimationState> AnimationsForTesting => _animations;

    /// <summary>Gets each driven object's transform state, by task id, for the suites that read them.</summary>
    internal IReadOnlyDictionary<int, CutsceneTransformState> TransformsForTesting => _context.Transforms;

    /// <summary>Gets each driven object's transform state, by task, for the suites that read them.</summary>
    internal IReadOnlyDictionary<GameTask, CutsceneTransformState> TaskTransformsForTesting => _context.TaskTransforms;

    /// <summary>Gets what each attached task rides, by task, for the suites that read them.</summary>
    internal IReadOnlyDictionary<GameTask, CutsceneAttachmentState> AttachmentsForTesting => _attachments.All;

    /// <summary>Gets the CarAI tasks' states, for the suites that read them.</summary>
    internal IReadOnlyList<CarAiState> CarAisForTesting => _carAis;

    /// <summary>Makes every AnimTask run from the next tick on: an empty run expression, initial run set and running.</summary>
    internal void RunEveryAnimationForTesting()
    {
        foreach (CutsceneAnimationState animation in _animations)
        {
            animation.Run = string.Empty;
            animation.InitialRun = true;
            animation.IsRun = true;
        }
    }
}
