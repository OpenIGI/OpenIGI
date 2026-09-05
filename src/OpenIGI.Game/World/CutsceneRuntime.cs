using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
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
    private readonly Dictionary<int, TransformState> _transforms = [];
    private readonly Dictionary<GameTask, TransformState> _taskTransforms = [];
    private readonly Dictionary<GameTask, int> _taskOrder = [];
    private readonly Dictionary<GameTask, AttachmentState> _attachments = [];
    private readonly Dictionary<GameTask, float> _trainTailOffsets = [];
    private readonly Func<string, ScriptArguments, QvmValue?>? _hostNative;
    private readonly Action<CutsceneSoundEvent>? _soundChanged;
    private readonly Func<string, float>? _modelLength;
    private readonly Func<string, float>? _modelPivot;
    private readonly VehiclePhysicsRegistry? _vehiclePhysics;
    private readonly TerrainData? _terrain;
    private readonly IIgiLogger _logger;
    private ObjectCollisionIndex? _collisionIndex;
    private readonly Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? _vehicleObstacleHit;
    private readonly Func<GameTask, bool>? _vehicleObstacleReleased;
    private Func<int, CutsceneTargetPose?>? _targetPoseResolver;
    private bool _breakRequested;
    private readonly List<string> _diagnostics = [];

    /// <summary>Gets or sets the solid-object index used to find a supporting surface for vehicles.</summary>
    /// <remarks>
    /// Wired by the host when a level has placed pads, crates or rooftops under a vehicle the
    /// terrain half cannot see. The terrain half mirrors retail's <c>0x4CBE40</c>; the object sweep
    /// mirrors retail's placed-model sweep (<c>0x4CDB20</c> -&gt; <c>0x4C7770</c>).
    /// </remarks>
    public ObjectCollisionIndex? CollisionIndex
    {
        get => _collisionIndex;
        set
        {
            _collisionIndex = value;
            if (value is not null)
            {
                RestCollisionHelisOnSupport();
            }
        }
    }

    /// <summary>Builds the live cutscene state for a loaded level.</summary>
    public CutsceneRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? native = null,
        Action<CutsceneSoundEvent>? soundChanged = null,
        Func<string, float>? modelLength = null,
        Func<string, float>? modelPivot = null,
        VehiclePhysicsRegistry? vehiclePhysics = null,
        TerrainData? terrain = null,
        IIgiLogger? logger = null,
        Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? vehicleObstacleHit = null,
        Func<GameTask, bool>? vehicleObstacleReleased = null)
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
        _terrain = terrain;
        _logger = logger ?? NullLogger.Instance;
        _vehicleObstacleHit = vehicleObstacleHit;
        _vehicleObstacleReleased = vehicleObstacleReleased;
        _expressions = new LevelExpressionRuntime(variables, InvokeNative);
        List<CutSceneTask> cutscenes = [];

        int taskOrder = 0;
        foreach (GameTask task in Walk(levelRoot))
        {
            int order = taskOrder++;
            _taskOrder[task] = order;
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
            }
        }

        // EditCamera caches every Link task's initial world transform during retail
        // UpdateInternalData.  Build those keys only after all task transforms exist: Train tasks do
        // not author a world Position at all, and their initial frame must first be evaluated from the
        // railroad.  Reading the keys inline above silently cached (0,0,0)/identity for linked cars.
        foreach (CutSceneTask cutscene in cutscenes)
        {
            _scenes.Add(new SceneState(cutscene, ReadShots(cutscene)));
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
                : _expressions.EvaluateCondition(state.Task.Run, emptyValue: state.Task.InitialRun);
            state.Task.IsRun = run;
            if (run)
            {
                _expressions.Execute(state.Task.StartExpression);
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

    /// <summary>Marks an explicitly configured helicopter for terminal terrain landing.</summary>
    /// <param name="task">The helicopter task whose first terrain contact is terminal.</param>
    /// <remarks>
    /// Mission setup supplies this state for authored one-shot landings. The generic cutscene runtime
    /// does not infer terminal behavior from a task id, so unrelated helicopters continue their own
    /// animation after touching terrain.
    /// </remarks>
    public void EnableTerminalTerrainLanding(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!_taskTransforms.TryGetValue(task, out TransformState? state)
            || !string.Equals(task.Type?.Name, "Heli", StringComparison.Ordinal))
        {
            throw new ArgumentException("Terminal terrain landing requires a captured Heli task.", nameof(task));
        }

        state.TerminalLandingOnTerrain = true;
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
        RefreshContainers(executeTransitions: true);

        foreach (TimerState timer in _timers)
        {
            if (_expressions.EvaluateCondition(timer.Reset, emptyValue: false))
            {
                timer.Tick = 0;
            }

            timer.IsRun = string.IsNullOrWhiteSpace(timer.Run)
                ? timer.InitialRun
                : _expressions.EvaluateCondition(timer.Run, emptyValue: timer.InitialRun);
            if (timer.IsRun)
            {
                timer.Tick++;
            }
        }

        foreach (AnimationState animation in _animations)
        {
            bool run = string.IsNullOrWhiteSpace(animation.Run)
                ? animation.InitialRun
                : _expressions.EvaluateCondition(animation.Run, emptyValue: animation.InitialRun);
            if (!run || !ConditionalContainerGate.IsTaskActive(animation.Task))
            {
                continue;
            }

            int previousTick = animation.Program.Tick;
            animation.Tick++;
            animation.Program.SeekTo(previousTick + 1);
            ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
            if (previousTick < 0)
            {
                // Containers can defer an AnimTask's first run until after construction. Its first
                // active scheduler slot still has the same two tick-zero dispatches as a task that
                // was active during runtime initialization.
                animation.Program.Step();
                ApplyAnimation(animation.TargetId, animation.Order, animation.Program);
            }
        }

        foreach (SceneState state in _scenes)
        {
            CutSceneTask scene = state.Task;
            bool wasRun = scene.IsRun;

            if (!ConditionalContainerGate.IsTaskActive(scene))
            {
                scene.IsRun = false;
                if (wasRun)
                {
                    _expressions.Execute(scene.StopExpression);
                }
                continue;
            }

            if (_expressions.EvaluateCondition(scene.Reset, emptyValue: false))
            {
                scene.Ticks = 0;
                scene.IsFinished = false;
            }

            scene.IsRun = string.IsNullOrWhiteSpace(scene.Run)
                ? scene.InitialRun && !scene.IsFinished
                : _expressions.EvaluateCondition(scene.Run, emptyValue: false);

            if (!wasRun && scene.IsRun)
            {
                _expressions.Execute(scene.StartExpression);
            }

            if (scene.IsRun)
            {
                // CutScene_RunHandler (IGI.exe 0x4F63C0; 0x43A470 is mid-instruction in AlarmLight) runs
                // child AnimTasks before the scene,
                // evaluates Run, and then calls CutScene_SetCamera with the *current* nTick. The
                // camera marks isFinished only when that old tick is beyond the authored duration;
                // Run stays latched for this frame and nTick is incremented afterwards. The Run
                // expression observes isFinished on the following frame. Keeping those two boundary
                // frames is essential: Mission 1's 2.2-second landing scene publishes train ticks
                // 0..67, exactly as the retail probe records.
                double total = state.TotalTime(scene.TimeScale);
                if ((scene.Ticks / (double)Frequency) > total)
                {
                    scene.Ticks = Math.Max(0, (int)(Frequency * total));
                    scene.IsFinished = true;
                }

                int delta = string.IsNullOrWhiteSpace(scene.TimeDelta)
                    ? 1
                    : (int)(_expressions.EvaluateSingle(scene.TimeDelta) * Frequency);
                // Expressions returning a float are evaluated through the script union. Most retail
                // scenes leave TimeDelta empty; the minimum retains forward progress for malformed data.
                scene.Ticks = Math.Max(0, scene.Ticks + delta);
            }

            if (wasRun && !scene.IsRun)
            {
                _expressions.Execute(scene.StopExpression);
            }
        }

        foreach (SoundState sound in _sounds)
        {
            bool run = ConditionalContainerGate.IsTaskActive(sound.Task)
                && _expressions.EvaluateCondition(sound.Condition, emptyValue: false);
            if (run != sound.IsRun)
            {
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
                    // A non-simple ConditionalSound owns its voice for the condition's lifetime. A
                    // simple sound is fire-and-forget and deliberately receives no falling-edge stop.
                    _soundChanged?.Invoke(new CutsceneSoundEvent(
                        sound.Task,
                        sound.Sound,
                        sound.RelativeToMicrophone ? null : sound.Position,
                        Playing: false,
                        Simple: false));
                }
            }
        }

        RefreshContainers(executeTransitions: true);

        // Retail walks the authored task tree. A CutScene can therefore become active before a later
        // sibling ConditionalContainer is visited; registering that container immediately runs a
        // newly exposed AnimTask in the same frame. Our grouped passes discover that registration in
        // the refresh above, so publish the deferred task's two tick-zero packets here instead of
        // waiting a frame. Mission 1 scene 1201/container 4003 is the source-of-truth case.
        foreach (AnimationState animation in _animations)
        {
            if (animation.Program.Tick >= 0 || !ConditionalContainerGate.IsTaskActive(animation.Task))
            {
                continue;
            }

            bool run = string.IsNullOrWhiteSpace(animation.Run)
                ? animation.InitialRun
                : _expressions.EvaluateCondition(animation.Run, emptyValue: animation.InitialRun);
            if (!run)
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

        if (!TryGetPlacementDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            out position,
            out orientation))
        {
            return false;
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
    /// the Human to that exact task. <c>0x463310</c> is a mid-instruction false start (AGENTS.md);
    /// r2's real prologues are Human ground <c>0x462390</c> and <c>Human_AttachedMovement</c>
    /// <c>0x462CF0</c>. The task-tree ancestry of the Human is unrelated: Mission 1 actor 1666 is a
    /// sibling of train car 1888, not its child.
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

            if (!container.Refresh(out bool started) || !executeTransitions)
            {
                continue;
            }

            _expressions.Execute(started ? container.RunAtStart : container.RunAtStop);
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
            "MenuManager_SetEnabled" or "Game_DisableMusic" or "Game_EnableMusic" or "Game_CutsceneDelete"
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

        Frame = BuildFrame(state, state.Task.Ticks / (double)Frequency);
    }

    /// <summary>Interpolates one scene's authored camera at an arbitrary scene time.</summary>
    private CutsceneFrame? BuildFrame(SceneState state, double time)
    {
        CutSceneTask scene = state.Task;
        List<TimedShot> timed = state.Timed(scene.TimeScale);
        // An EditCamera with no duration is a spline/filter endpoint, not a frame-owning shot. Retail
        // keeps the preceding camera live on the two completion-boundary ticks; selecting a terminal
        // zero-duration entry here exposes its marker pose as a visible flash (Mission 4's outro puts
        // that pose inside Bell 356). Keep zero-length entries in `timed` so the preceding Hermite and
        // filter interpolation can still end at their authored values, but never make one `active`.
        bool OwnsFrame(TimedShot item) => Math.Max(0f, item.Shot.Duration * scene.TimeScale) > 0f;
        int current = timed.FindIndex(OwnsFrame);
        if (current < 0)
        {
            current = 0;
        }
        for (int i = current + 1; i < timed.Count; i++)
        {
            if (timed[i].Start > time)
            {
                break;
            }
            if (OwnsFrame(timed[i]))
            {
                current = i;
            }
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
            CarRoute = CarRouteProfile.For(declared),
        };
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
            if (state.CollisionEnabled)
            {
                RestHeliOnSupport(task, state);
            }
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
            if (program.Tick > 0)
            {
                SnapshotDynamicPose(transform, simulationOrder);
            }

            // Car::ReadChannels (0x42EA10) copies ch0 to the steering target and latches ch1 as
            // throttle only when it is not -1.  ch3 is the parking-brake pulse.  The former generic
            // mover had those first two channels reversed and advanced yaw even while stationary,
            // which sends the mission-one outro truck sideways out of its garage.
            transform.TargetSteering = channels[0];
            if (channels[1] != -1f)
            {
                transform.TargetThrust = channels[1];
            }
            transform.ParkingBrake = channels[3] != -1f;

            // The two response coefficients are physics-object data in retail (+24 steering, +32
            // throttle), rather than level parameters.  Keep the same first-order state shape here;
            // the probe records the exact per-model response and pose so these defaults can be
            // replaced without changing the timeline contract.
            // Retail smooths steer/throttle with data-driven factors from the model's CAR.QVM
            // physics blob (cutscene_truck/TRUCK: retention 0.95, response 0.05 per channel;
            // ANIMTASK-RE.md §4b) — x += (target - x) * response.
            transform.Steering += (transform.TargetSteering - transform.Steering) * 0.05f;
            transform.Thrust += (transform.TargetThrust - transform.Thrust) * 0.05f;
            float targetSpeed = transform.ParkingBrake ? 0f : transform.Thrust * 1_200f;
            transform.Speed += (targetSpeed - transform.Speed) * (transform.ParkingBrake ? 0.3f : 0.04f);

            // Car::Tick (IGI.exe 0x42ECA0) integrates the authored steering around the vehicle's
            // local-Y forward axis. The only shipped route needing the post-gate sign is the Level 1
            // "Truck which Jones will steal" (model 622_01_1); Mission 2 reuses that model for a
            // different inbound route. Keep the authored identity and its probe dimensions together
            // instead of applying an ID- or model-wide exception to every Car.
            CarRouteProfile? route = transform.CarRoute;
            if (!transform.VehicleGateReleased
                && route is not null
                && _vehicleObstacleReleased is not null
                && _root.Tree.FromId(route.GateTaskId) is { } routeGate
                && ConditionalContainerGate.IsTaskActive(routeGate)
                && ActiveScene?.Ticks >= route.GateOpenTick
                && _vehicleObstacleReleased(routeGate))
            {
                // The Level 1 gate is scripted to open on the cutscene clock. Once the truck reaches
                // that doorway, use the gate's live state rather than waiting for a blocking collision:
                // a fully open door is deliberately absent from the collision index.
                transform.VehicleGateReleased = true;
            }
            float previousHeading = transform.Heading;
            Matrix3f previousOrientation = transform.Orientation;
            float headingDelta = transform.Steering * transform.Speed / (4_096f * 4.5f);
            transform.Heading += transform.VehicleGateReleased && route is not null
                ? headingDelta
                : -headingDelta;
            transform.Orientation = Matrix3f.FromEngineEulerAngles(
                transform.Euler.X,
                transform.Euler.Y,
                transform.Heading);
            Vector3f carForward = transform.Orientation.Column2;
            Vector3d carDirection = new(carForward.X, carForward.Y, carForward.Z);
            Vector3d proposedPosition = transform.Position + (carDirection * transform.Speed);

            bool blocked = false;
            GameTask? obstacle = null;
            if (route is not null && target is not null)
            {
                for (int heightIndex = 0; heightIndex < route.ProbeHeightsMetres.Count && !blocked; heightIndex++)
                {
                    double heightMetres = route.ProbeHeightsMetres[heightIndex];
                    Vector3d offset = new(
                        0.0,
                        0.0,
                        heightMetres * WorldScene.UnitsPerMetre);
                    Vector3d probeStart = transform.Position
                        + (carDirection * (route.FrontBumperMetres * WorldScene.UnitsPerMetre))
                        + offset;
                    Vector3d probeEnd = proposedPosition
                        + (carDirection * ((route.FrontBumperMetres * WorldScene.UnitsPerMetre) + route.ProbeMarginUnits))
                        + offset;

                    if (_vehicleObstacleHit?.Invoke(target, probeStart, probeEnd) is { } contact)
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
                transform.Speed = 0f;
                transform.Heading = previousHeading;
                transform.Orientation = previousOrientation;
            }
            else
            {
                transform.Position = proposedPosition;
            }
            transform.IsDynamic = true;
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
            if (program.Tick == 0)
            {
                // The retail Plane move callback is registered by 0x4330F0 and begins at 0x433690.
                // The dispatcher resets the body on tick zero before any physics runs, matching the
                // callback's observed zero-tick state (the callback itself is a run/state handler).
                // The plane flies a scripted path through the level's terrain, and like the heli it has
                // no per-tick collision that would stop it sinking into a valley or a ridge. Measure the
                // model's rest height once (authored pose minus the ground under it), the same way the
                // heli's parked airframe does, so the clamp below keeps the body above the terrain
                // instead of letting a fly-by path clip through the ground.
                transform.Position = transform.OriginPosition;
                transform.Orientation = transform.AuthoredOrientation;
                transform.Velocity = default;
                transform.Control = default;
                transform.Heading = 0f;
                transform.Speed = 0f;
                transform.GroundContact = false;
                transform.GroundContactEventLogged = false;
                transform.GroundContactLogged = false;
                transform.GroundRestOffset = MeasureGroundRestOffset(transform.OriginPosition);
                transform.IsDynamic = true;
                return;
            }

            SnapshotDynamicPose(transform, simulationOrder);

            // The Plane AnimTask publishes normalized pitch/roll/yaw controls and a latched thrust
            // target. These are control targets, not Euler deltas; the callback that owns the Plane
            // run/state transition is the r2/Ghidra-confirmed 0x433690 prologue.
            transform.Control += (new Vector3f(channels[0], channels[1], channels[2]) - transform.Control)
                * 0.12f;
            if (channels[3] != -1f)
            {
                transform.TargetThrust = channels[3];
            }
            if (target is DeclaredTask livePlane)
            {
                transform.CollisionEnabled = Bool(livePlane, "Collision detection");
            }
            transform.Heading += transform.Control.Z * 0.004f;
            transform.Orientation = Matrix3f.FromEngineEulerAngles(
                transform.Control.X * 0.10f,
                transform.Control.Y * 0.18f,
                transform.Heading) * transform.AuthoredOrientation;
            // IGI vehicles are Y-forward: the second row of Real32x9 is the local +Y axis in world
            // space (same as the generic AnimTask path below). Column2 is the transposed axis and
            // sent Mission 3/4 planes backwards along the authored heading.
            Vector3f planeForward = transform.Orientation.Row2;
            float desiredSpeed = Math.Max(0f, transform.TargetThrust) * 1_500f;
            transform.Speed += (desiredSpeed - transform.Speed) * 0.025f;
            transform.Position += new Vector3d(planeForward.X, planeForward.Y, planeForward.Z)
                * transform.Speed;
            KeepHelicopterAboveGround(target, transform);
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
        // Mission 4 CutScene 1000 mixes world ObjectPos (the pad, ~53 km from origin) with
        // cabin-scale ObjectPos (~18 m). Those small records are local offsets — Link is -1 —
        // but Hermite still interpolates them as world points, so the intro camera leaves the
        // castle and flies through empty sky to (17.7, -0.4, 2.8). Promote any cabin-scale
        // shot onto the last world-space camera (or the CutScene anchor) so the path stays
        // in the level. World-scale shots (Mission 1's linked heli cameras included) are
        // left untouched.
        Vector3d lastWorld = scene.Position;
        foreach (GameTask task in Walk(scene))
        {
            if (task is not DeclaredTask camera || !string.Equals(task.Type.Name, "EditCamera", StringComparison.Ordinal))
            {
                continue;
            }
            int link = Int(camera, "Link task ID", -1);
            int target = Int(camera, "Target task ID", -1);
            (Vector3d linkOrigin, Matrix3f linkOrientation) = InitialPose(scene.Tree.FromId(link));
            Vector3d position = ResolveEditCameraPosition(Vector(camera, "Position"), scene.Position, ref lastWorld);
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

    /// <summary>
    /// ObjectPos smaller than a kilometre on a cutscene that itself sits tens of kilometres
    /// from the origin is a local offset, not a world point.
    /// </summary>
    private static Vector3d ResolveEditCameraPosition(
        Vector3d authored, Vector3d sceneOrigin, ref Vector3d lastWorld)
    {
        const double LocalRadius = 1_000.0 * WorldScene.UnitsPerMetre;
        double authoredSquared = authored.LengthSquared;
        if (authoredSquared < LocalRadius * LocalRadius
            && sceneOrigin.LengthSquared > LocalRadius * LocalRadius)
        {
            return lastWorld + authored;
        }

        if (authoredSquared >= LocalRadius * LocalRadius)
        {
            lastWorld = authored;
        }

        return authored;
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
                transform.TerminalLandingPosition = transform.Position;
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
    private sealed record TimedShot(EditCameraShot Shot, double Start);
    private sealed class AttachmentState(
        TransformState parent,
        int parentRevisionAtCapture,
        Vector3d parentOriginPosition,
        Matrix3f parentOriginOrientation,
        Vector3d position,
        Matrix3f orientation)
    {
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
        public CarRouteProfile? CarRoute { get; set; }
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
        public Vector3d TerminalLandingPosition { get; set; }
        public float RotorPhase { get; set; }
        public int RotorPhaseTick { get; set; }
        public Vector3f AngularMomentum { get; set; }
        public int PhysicsTicks { get; set; }
        public float RotorAxialCoefficient { get; set; }
        public Vector3f QueuedRotorForce { get; set; }
        public Vector3f QueuedRotorTorque { get; set; }
    }

    /// <summary>The only retail car route that changes steering side after crossing an opening gate.</summary>
    private sealed record CarRouteProfile(
        IReadOnlyList<double> ProbeHeightsMetres,
        double FrontBumperMetres,
        double ProbeMarginUnits,
        int GateTaskId,
        int GateOpenTick)
    {
        // LEVEL1/OBJECTS.QVM declares task 1099 as label "Truck which Jones will steal", model
        // 622_01_1. LEVEL2 deliberately reuses model 622_01_1 for label "Car which Jones drives into
        // base", so both the authored label and model are the route identity. The bumper/heights are
        // the established dimensions of this one retail truck, not a policy for every Car.
        private const string LevelOneStolenTruckLabel = "Truck which Jones will steal";
        private const string TruckModel = "622_01_1";

        // Door_401 is the nearer leaf of the garage exit. LEVEL1/OBJECTS.QVM places it at
        // (24979312, -56202112, 174413120) in the loaded placement and opens it after
        // CutScene_1204's eleven-second mark. That authored clock gate means an already-open door
        // follows the same transition as the normal closed-then-open route.
        private static readonly CarRouteProfile LevelOneStolenTruck = new(
            [0.75, 1.95],
            3.9,
            128.0,
            GateTaskId: 401,
            GateOpenTick: 11 * Frequency);

        public static CarRouteProfile? For(DeclaredTask task) =>
            string.Equals(task.Type.Name, "Car", StringComparison.Ordinal)
            && string.Equals(task.Label, LevelOneStolenTruckLabel, StringComparison.Ordinal)
            && string.Equals(Text(task, WorldScene.ModelParameter), TruckModel, StringComparison.Ordinal)
                ? LevelOneStolenTruck
                : null;
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
    int ShotIndex);

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
