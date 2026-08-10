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
    private readonly Dictionary<GameTask, AttachmentState> _attachments = [];
    private readonly Dictionary<GameTask, float> _trainTailOffsets = [];
    private readonly Func<string, ScriptArguments, QvmValue?>? _hostNative;
    private readonly Action<CutsceneSoundEvent>? _soundChanged;
    private readonly Func<string, float>? _modelLength;
    private readonly Func<string, float>? _modelPivot;
    private readonly VehiclePhysicsRegistry? _vehiclePhysics;
    private Func<int, CutsceneTargetPose?>? _targetPoseResolver;
    private bool _breakRequested;

    /// <summary>Builds the live cutscene state for a loaded level.</summary>
    public CutsceneRuntime(
        GameTask levelRoot,
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? native = null,
        Action<CutsceneSoundEvent>? soundChanged = null,
        Func<string, float>? modelLength = null,
        Func<string, float>? modelPivot = null,
        VehiclePhysicsRegistry? vehiclePhysics = null)
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
        _expressions = new LevelExpressionRuntime(variables, InvokeNative);
        List<CutSceneTask> cutscenes = [];

        foreach (GameTask task in Walk(levelRoot))
        {
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
                    _animations.Add(new AnimationState(
                        declared,
                        target,
                        Text(declared, "Run"),
                        Bool(declared, "Initial run"),
                        new AnimDataProgram(entries)));
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
            ApplyAnimation(animation.TargetId, animation.Program);
            animation.Program.Step();
            ApplyAnimation(animation.TargetId, animation.Program);
        }
        UpdateFrame();
    }

    /// <summary>Gets whether an authored cutscene currently owns the camera.</summary>
    public bool IsPlaying => ActiveScene is not null;

    /// <summary>Gets the current authored camera and presentation settings.</summary>
    public CutsceneFrame? Frame { get; private set; }

    /// <summary>Supplies live poses for targets owned by another simulation, such as animated humans.</summary>
    public void SetTargetPoseResolver(Func<int, CutsceneTargetPose?>? resolver) => _targetPoseResolver = resolver;

    /// <summary>Gets the currently running scene, if any.</summary>
    public CutSceneTask? ActiveScene => _scenes.LastOrDefault(
        static state => state.Task.IsRun && ConditionalContainerGate.IsTaskActive(state.Task))?.Task;

    /// <summary>Queues the retail break-cutscene key for the next logic tick.</summary>
    public void RequestBreak() => _breakRequested = true;

    /// <summary>Advances all cutscene-owned tasks by one fixed logic tick.</summary>
    public void Tick()
    {
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
            animation.Program.SeekTo(previousTick + 1);
            ApplyAnimation(animation.TargetId, animation.Program);
            if (previousTick < 0)
            {
                // Containers can defer an AnimTask's first run until after construction. Its first
                // active scheduler slot still has the same two tick-zero dispatches as a task that
                // was active during runtime initialization.
                animation.Program.Step();
                ApplyAnimation(animation.TargetId, animation.Program);
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
                // CutScene_RunHandler (IGI.exe 0x43A470) runs child AnimTasks before the scene,
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
            ApplyAnimation(animation.TargetId, animation.Program);
            animation.Program.Step();
            ApplyAnimation(animation.TargetId, animation.Program);
        }

        UpdateFrame();
    }

    /// <summary>Serves cutscene-specific level-expression natives to sibling mission runtimes.</summary>
    public QvmValue? TryInvokeNative(string name, ScriptArguments arguments) => InvokeNative(name, arguments);

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
            return state.IsDynamic || state.TrainRoot is not null;
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
            if (!state.IsDynamic && state.TrainRoot is null)
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
        float accumulatedPhase = state.RotorPhase * rotor.PhasePerCollectiveTick;
        float firstAngle = accumulatedPhase * 21.362831f;
        float sampleStep = rotor.PhaseStep * state.Thrust;

        float maximumTilt = rotor.MaximumTilt;
        float tilt = -Math.Clamp(1f - state.Thrust, -0.1f, 1f) * maximumTilt;
        float originalCollective = state.OriginalThrust;
        if (maximumTilt != 0f && originalCollective != 0f)
        {
            Vector3f vertical = state.Orientation.Column1;
            float axialVelocity =
                (vertical.X * (float)state.Velocity.X)
                + (vertical.Y * (float)state.Velocity.Y)
                + (vertical.Z * (float)state.Velocity.Z);
            tilt -= axialVelocity * 0.026367188f * maximumTilt / (originalCollective * 0.4f);
            tilt = Math.Clamp(tilt, -maximumTilt, maximumTilt);
        }

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
    /// Retail's Human ground pass tests the struck collision task's moving-platform flag and calls
    /// <c>Human_SetParent</c> (IGI.exe <c>0x463310</c>) with that exact task. The task-tree ancestry of
    /// the Human is unrelated: Mission 1 actor 1666 is a sibling of train car 1888, not its child.
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

    private static void EvaluateAttachmentParentPose(
        GameTask task,
        AttachmentState attachment,
        out Vector3d position,
        out Matrix3f orientation)
    {
        // The retail scheduler ticks a Human before the vehicle task it is attached to, but draws
        // vehicle cargo and resolves EditCamera links after that vehicle tick. Our grouped scheduler
        // advances all AnimTasks first, so attached Humans must explicitly sample the saved pre-tick
        // parent pose. Mission 1 Human 2021 proves the ordering: its retail skin position is exactly
        // one freight-car step behind the linked camera and car geometry from the same outer frame.
        bool usePreviousPose = task is HumanSoldierTask
            && DynamicRevision(attachment.Parent) > attachment.ParentRevisionAtCapture;
        EvaluateDynamicPose(attachment.Parent, usePreviousPose, out position, out orientation);
    }

    private void RefreshContainers(bool executeTransitions)
    {
        foreach (ConditionalContainerTask container in _containers)
        {
            if (!container.Refresh(out bool started) || !executeTransitions)
            {
                continue;
            }

            _expressions.Execute(started ? container.RunAtStart : container.RunAtStop);
        }
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

        CutSceneTask scene = state.Task;
        double time = scene.Ticks / (double)Frequency;
        List<TimedShot> timed = state.Timed(scene.TimeScale);
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

        Frame = new CutsceneFrame(
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
            if (task.Id != GameTask.NoId)
            {
                Matrix3f orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma);
                TransformState humanState = new(soldier.Position, orientation);
                Remember(task, humanState);
                CaptureAttachment(task, humanState);
            }
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

    private static bool IsVehicle(GameTask task) =>
        task is DeclaredTask && task.Type.Name is "Train" or "Heli" or "Plane" or "Car";

    private void Remember(GameTask task, TransformState state)
    {
        _taskTransforms[task] = state;
        if (task.Id != GameTask.NoId)
        {
            _transforms[task.Id] = state;
        }
    }

    private void ApplyAnimation(int targetId, AnimDataProgram program)
    {
        if (targetId < 0 || !_transforms.TryGetValue(targetId, out TransformState? transform))
        {
            return;
        }

        GameTask? target = _root.Tree.FromId(targetId);
        string type = target?.Type.Name ?? string.Empty;
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

            SnapshotDynamicPose(transform);

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
        float thrust;
        if (string.Equals(type, "Car", StringComparison.Ordinal))
        {
            if (program.Tick > 0)
            {
                SnapshotDynamicPose(transform);
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
            transform.Steering += (transform.TargetSteering - transform.Steering) * 0.18f;
            transform.Thrust += (transform.TargetThrust - transform.Thrust) * 0.12f;
            float targetSpeed = transform.ParkingBrake ? 0f : transform.Thrust * 1_200f;
            transform.Speed += (targetSpeed - transform.Speed) * (transform.ParkingBrake ? 0.3f : 0.04f);
            yaw += transform.Steering * transform.Speed / (4_096f * 4.5f);
            transform.Orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, yaw);
            Vector3f carForward = transform.Orientation.Column1;
            transform.Position += new Vector3d(carForward.X, carForward.Y, carForward.Z) * transform.Speed;
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
                    [
                        new(20f, Vector3f.UnitX, Vector3f.Zero),
                        new(3f, Vector3f.UnitY, Vector3f.Zero),
                        new(50f, Vector3f.UnitZ, Vector3f.Zero),
                    ]);
            if (program.Tick == 0)
            {
                // Heli::ReadChannels (0x431B70) is bidirectional: the tick-zero reset restores the
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
                transform.AngularMomentum = default;
                transform.QueuedRotorForce = default;
                transform.QueuedRotorTorque = default;
                transform.PhysicsTicks = 0;
                transform.TargetThrust = transform.Thrust;
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

            SnapshotDynamicPose(transform);

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

            ApplyRetailHelicopterPhysics(transform, physics);
            transform.Position += transform.Velocity;
            transform.PhysicsTicks++;
            transform.IsDynamic = true;
            return;
        }
        else if (string.Equals(type, "Plane", StringComparison.Ordinal))
        {
            if (program.Tick > 0)
            {
                SnapshotDynamicPose(transform);
            }

            // Plane::ReadChannels (0x4337E0) has the same normalized pitch/roll/yaw + latched-thrust
            // channel map as Heli.  These are control targets, not Euler deltas.
            transform.Control += (new Vector3f(channels[0], channels[1], channels[2]) - transform.Control)
                * 0.12f;
            if (channels[3] != -1f)
            {
                transform.TargetThrust = channels[3];
            }
            transform.Heading += transform.Control.Z * 0.004f;
            transform.Orientation = Matrix3f.FromEngineEulerAngles(
                transform.Control.X * 0.10f,
                transform.Control.Y * 0.18f,
                transform.Heading) * transform.AuthoredOrientation;
            Vector3f planeForward = transform.Orientation.Column2;
            float desiredSpeed = Math.Max(0f, transform.TargetThrust) * 1_500f;
            transform.Speed += (desiredSpeed - transform.Speed) * 0.025f;
            transform.Position += new Vector3d(planeForward.X, planeForward.Y, planeForward.Z)
                * transform.Speed;
            transform.IsDynamic = true;
            return;
        }
        else
        {
            thrust = channels[0];
        }

        if (thrust != -1f)
        {
            transform.Speed += thrust / Frequency;
        }
        transform.Speed *= 0.995f;
        Vector3f forward = transform.Orientation.Row2;
        transform.Position += new Vector3d(forward.X, forward.Y, forward.Z) * transform.Speed;
        if (transform.Railroad is { } railroad)
        {
            transform.TrackPosition += transform.Speed;
            railroad.Evaluate(transform.TrackPosition, out Vector3d railPosition, out Matrix3f railOrientation);
            transform.Position = railPosition;
            transform.Orientation = railOrientation;
        }
        transform.IsDynamic = true;
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

    private static void SnapshotDynamicPose(TransformState state)
    {
        state.PreviousPosition = state.Position;
        state.PreviousOrientation = state.Orientation;
        state.PreviousTrackPosition = state.TrackPosition;
        state.DynamicRevision++;
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
        foreach (GameTask task in Walk(scene))
        {
            if (task is not DeclaredTask camera || !string.Equals(task.Type.Name, "EditCamera", StringComparison.Ordinal))
            {
                continue;
            }
            int link = Int(camera, "Link task ID", -1);
            int target = Int(camera, "Target task ID", -1);
            (Vector3d linkOrigin, Matrix3f linkOrientation) = InitialPose(scene.Tree.FromId(link));
            shots.Add(new EditCameraShot(
                Vector(camera, "Position"),
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
    /// Ports Heli::Tick (0x431E30), Rotor::Tick (0x42D440), and the force accumulator at 0x4EC6A0.
    /// Forces and application points are body-local; the accumulator converts linear acceleration to
    /// world space with the pre-integration attitude. Retail damps angular momentum by 0.92 after the
    /// quaternion step and deliberately does not damp linear velocity.
    /// </summary>
    private static void ApplyRetailHelicopterPhysics(
        TransformState transform,
        HelicopterPhysicsDefinition physics)
    {
        const float gravity = 44.600887f;
        const float angularDamping = 0.92000002f;
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
            Vector3f.Dot(attitude.Row3, localAcceleration) - gravity);
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
        transform.AngularMomentum *= angularDamping;

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
        const float gravity = 44.600887f;
        float lift = physics.Mass * transform.Thrust * gravity;
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

    private sealed record AnimationState(DeclaredTask Task, int TargetId, string Run, bool InitialRun, AnimDataProgram Program);
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
        public float Heading { get; set; }
        public Vector3d Velocity { get; set; }
        public HelicopterPhysicsDefinition? HelicopterPhysics { get; set; }
        public float RotorPhase { get; set; }
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
