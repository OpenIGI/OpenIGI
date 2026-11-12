using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Net;
using OpenIGI.Scripting.Runtime;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.Ai;

/// <summary>All security cameras and camera-control aggregators in a loaded level.</summary>
public sealed class SecurityCameraRegistry : World.ILevelReset, Saves.ISavedStateSource
{
    internal sealed class ControlState(DeclaredTask task, string expression, Func<bool> isExploded) : Saves.IExchangedState
    {
        // Not retail: what a save keeps of a camera's control is whether it has raised its alarm.
        public void Exchange(Saves.StateExchange exchange) => IsDetection = exchange.Bool(IsDetection);

        public DeclaredTask Task { get; } = task;
        public string Expression { get; } = expression;
        public Func<bool> IsExploded { get; } = isExploded;
        public bool IsDetection { get; set; }
    }

    private readonly List<SecurityCamera> _cameras = [];
    private readonly Dictionary<GameTask, SecurityCamera> _byTask = [];
    private readonly Dictionary<SecurityCamera, int> _order = [];
    private ReplicatedCamera[]? _mirror;
    private readonly List<ControlState> _controls = [];
    private Func<SecurityCameraScan>? _scan;
    private Action<SecurityCamera>? _ticked;
    private readonly List<SecurityCameraSubject> _subjects = [];
    private readonly LevelExpressionRuntime _expressions;
    private readonly WorldTraceCast? _sight;

    private SecurityCameraRegistry(LevelExpressionRuntime expressions, WorldTraceCast? sight)
    {
        _expressions = expressions;
        _sight = sight;
    }

    /// <summary>Gets every camera in task-update order.</summary>
    public IReadOnlyList<SecurityCamera> Cameras => _cameras;

    /// <summary>Gets the number of <c>SCameraControl</c> aggregators.</summary>
    public int ControlCount => _controls.Count;

    /// <summary>
    /// Gets or sets whether the lenses take their state from a co-op server's (<see cref="Mirror"/>) instead of scanning:
    /// a client's. Their controls still read them as ever.
    /// </summary>
    public bool Mirrored { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every camera and camera control is held off, whatever its expressions say: a
    /// versus match's arena, where a camera would only sound an alarm nobody plays against. The models stay, dark and still.
    /// </summary>
    public bool SwitchedOff { get; set; }

    /// <summary>Raised on a client when the server's word is that an intact lens here was destroyed, before it is marked so.</summary>
    public Action<SecurityCamera>? MirroredDestruction { get; set; }

    /// <summary>Gets every camera as a co-op server sends them, in the level's own order.</summary>
    /// <returns>Their states, or <see langword="null"/> when the level has none.</returns>
    public ReplicatedCamera[]? Replicate()
    {
        if (_cameras.Count == 0)
        {
            return null;
        }

        ReplicatedCamera[] states = new ReplicatedCamera[Math.Min(_cameras.Count, ReplicatedCamera.MaxPerSnapshot)];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = _cameras[i].Replicate();
        }

        return states;
    }

    /// <summary>Takes the server's cameras, as the newest snapshot has them, for each lens's next update to be posed by.</summary>
    /// <param name="states">Their states, in the level's own order.</param>
    public void Mirror(ReplicatedCamera[]? states) => _mirror = states;

    // Each control's detection as the match began.
    private bool[] _controlsAtStart = [];

    /// <inheritdoc/>
    public void MarkLevelStart()
    {
        foreach (SecurityCamera camera in _cameras)
        {
            camera.MarkLevelStart();
        }

        _controlsAtStart = [.. _controls.Select(static c => c.IsDetection)];
    }

    /// <inheritdoc/>
    /// <remarks>Every camera whole, posed and scanning as the match began; a client's mirror waits for the server's next word.</remarks>
    public void ResetToLevelStart(World.ObjectCollisionIndex? solids)
    {
        foreach (SecurityCamera camera in _cameras)
        {
            camera.ResetToLevelStart();
        }

        for (int i = 0; i < _controls.Count && i < _controlsAtStart.Length; i++)
        {
            _controls[i].IsDetection = _controlsAtStart[i];
        }

        _mirror = null;
    }

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<GameTask, Saves.ISavedState>> SavedStates(World.ObjectCollisionIndex? solids) =>
        _cameras.Select(static c => new KeyValuePair<GameTask, Saves.ISavedState>(c.Task, c))
            .Concat(_controls.Select(static c => new KeyValuePair<GameTask, Saves.ISavedState>(c.Task, c)));

    /// <summary>Collects cameras, exports their retail script variables and prepares their controllers.</summary>
    public static SecurityCameraRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        WorldTraceCast? sight = null,
        World.LevelExpressionRuntime? expressions = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        SecurityCameraRegistry registry = new(expressions ?? new LevelExpressionRuntime(variables), sight);
        registry.Collect(root, variables);
        return registry;
    }

    /// <summary>Binds every lens to the exact resolved head model transform and magic vertex.</summary>
    public void BindScene(IEnumerable<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        foreach (WorldPlacement placement in placements)
        {
            if (_byTask.TryGetValue(placement.Owner, out SecurityCamera? camera)
                && string.Equals(placement.Instance.Name, camera.HeadModelName, StringComparison.OrdinalIgnoreCase))
            {
                camera.Bind(placement);
            }
        }
    }

    /// <summary>The key the player goes by among a camera's scan subjects.</summary>
    public static readonly object PlayerKey = new();

    /// <summary>Puts every camera and every <c>SCameraControl</c> into the level walk, each updating where the walk reaches it.</summary>
    /// <param name="walker">The level walk.</param>
    /// <param name="scan">What a lens scans at the moment it updates: the player and every live soldier.</param>
    /// <param name="ticked">Called after each camera's update, to carry its head's new transform.</param>
    /// <remarks>
    /// <c>SCamera</c>'s update (<c>0x442610</c>) runs its children -- the lens, which sweeps and scans
    /// (<c>0x441770</c>) -- and then evaluates its power. <c>SCameraControl</c>'s (<c>0x440D70</c>) runs its terminal's
    /// and then its Detection Expression, so a control reads this tick's isDetection from every camera ahead of it in
    /// the tree and last tick's from those after.
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker, Func<SecurityCameraScan> scan, Action<SecurityCamera>? ticked = null)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(scan);
        foreach (SecurityCamera camera in _cameras)
        {
            if (Level.LevelTaskClass.TryAs(camera.Task, walker, out SecurityCameraTask? task))
            {
                task.Cameras = this;
                task.Camera = camera;
            }
        }

        // SCameraControl's update (0x440D70) is Terminal's, then its isDetection, which its task runs in that order.
        foreach (ControlState control in _controls)
        {
            if (Level.LevelTaskClass.TryAs(control.Task, walker, out SecurityCameraControlTask? task))
            {
                task.Cameras = this;
                task.Control = control;
            }
        }

        _scan = scan;
        _ticked = ticked;
    }

    /// <summary>A camera's own update, where the level's walk reaches its task (<c>0x442610</c>): the lens, its children, then its power.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="task">The camera's task.</param>
    /// <param name="level">The level's run, which runs the camera's children.</param>
    /// <returns>Whether the registry keeps and stepped it.</returns>
    internal bool RunFromWalk(SecurityCamera camera, SecurityCameraTask task, Level.LevelRun level)
    {
        if (_scan is null)
        {
            return false;
        }

        TickLens(camera, BuildSubjects(_scan()));
        level.RunChildren(task);
        UpdatePower(camera);
        _ticked?.Invoke(camera);
        return true;
    }

    /// <summary>A control's detection, once its terminal's update has run (<c>0x440D70</c>).</summary>
    /// <param name="control">The control.</param>
    internal void UpdateDetectionFromWalk(ControlState control)
    {
        if (_scan is not null)
        {
            UpdateDetection(control);
        }
    }

    /// <summary>Advances all lenses against the player alone.</summary>
    public void Tick(Vector3d targetFeet, bool targetCrouching, bool targetAlive) =>
        Tick(targetFeet, targetCrouching, targetAlive, playerOnCamerasSide: false, []);

    /// <summary>Advances all lenses against what a scan offers, then the <c>SCameraControl</c> expressions that consume them.</summary>
    /// <param name="scan">The player, its partners and the soldiers.</param>
    public void Tick(SecurityCameraScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan.Soldiers);
        List<SecurityCameraSubject> subjects = BuildSubjects(scan);
        foreach (SecurityCamera camera in _cameras)
        {
            TickLens(camera, subjects);
            UpdatePower(camera);
        }

        foreach (ControlState control in _controls)
        {
            UpdateDetection(control);
        }
    }

    /// <summary>Advances all lenses, then the <c>SCameraControl</c> expressions that consume them.</summary>
    /// <param name="targetFeet">The player's feet, in engine units.</param>
    /// <param name="targetCrouching">Whether the player is crouching, which lowers the mark.</param>
    /// <param name="targetAlive">Whether the player is alive.</param>
    /// <param name="playerOnCamerasSide">Whether the player is on team 1, the cameras' side.</param>
    /// <param name="soldiers">
    /// Every live soldier in the global human list's order, as <c>0x45FE30</c> describes it. The cameras
    /// themselves are appended here (<c>0x442A30</c>), so one lens sees another that has been destroyed.
    /// </param>
    public void Tick(
        Vector3d targetFeet,
        bool targetCrouching,
        bool targetAlive,
        bool playerOnCamerasSide,
        IReadOnlyList<SecurityCameraSubject> soldiers)
    {
        ArgumentNullException.ThrowIfNull(soldiers);
        Tick(new SecurityCameraScan(targetFeet, targetCrouching, targetAlive, playerOnCamerasSide, soldiers));
    }

    // The lens's update (0x441770), against the holder's power as last tick left it -- or, on a co-op client, the server's.
    private void TickLens(SecurityCamera camera, IReadOnlyList<SecurityCameraSubject> subjects)
    {
        if (!Mirrored)
        {
            camera.Tick(camera.IsPowered, subjects, _sight);
            return;
        }

        // Nothing from the server yet: the lens holds as it stands.
        int at = _order[camera];
        if (_mirror is not { } states || at >= states.Length)
        {
            return;
        }

        ReplicatedCamera state = states[at];
        if ((state.Flags & ReplicatedCameraCondition.Exploded) != 0 && !camera.IsExploded)
        {
            MirroredDestruction?.Invoke(camera);
        }

        camera.TickMirrored(state);
    }

    // The rest of the holder's update (0x442610): off once exploded, otherwise its On Expression when it has one. A client's
    // holder takes its power from the server's with the lens.
    private void UpdatePower(SecurityCamera camera)
    {
        if (Mirrored)
        {
            return;
        }

        if (camera.IsExploded || SwitchedOff)
        {
            camera.IsPowered = false;
        }
        else if (!string.IsNullOrWhiteSpace(camera.OnExpression))
        {
            camera.IsPowered = _expressions.EvaluateCondition(camera.OnExpression, emptyValue: false, camera.Task);
        }
    }

    // SCameraControl's own half of 0x440D70: held off once the control has exploded (+400), otherwise its Detection
    // Expression when it has one.
    private void UpdateDetection(ControlState control)
    {
        if (control.IsExploded() || SwitchedOff)
        {
            control.IsDetection = false;
        }
        else if (!string.IsNullOrWhiteSpace(control.Expression))
        {
            control.IsDetection = _expressions.EvaluateCondition(control.Expression, emptyValue: false, control.Task);
        }
    }

    // What 0x489750 is offered: the player, a co-op session's other players, the soldiers in the global human list's order,
    // then every camera (0x442A30).
    private List<SecurityCameraSubject> BuildSubjects(SecurityCameraScan scan)
    {
        _subjects.Clear();
        _subjects.Add(SecurityCameraScan.PlayerSubject(
            PlayerKey, scan.TargetFeet, scan.TargetCrouching, scan.PlayerOnCamerasSide, scan.TargetAlive));
        if (scan.Partners is { } partners)
        {
            _subjects.AddRange(partners);
        }

        _subjects.AddRange(scan.Soldiers);
        foreach (SecurityCamera camera in _cameras)
        {
            _subjects.Add(new SecurityCameraSubject(
                camera, camera.HolderPositionUnits, CamerasSide: true, Alive: !camera.IsExploded, camera.Task));
        }

        return _subjects;
    }

    /// <summary>Marks a camera's lens exploded immediately when its damage handler fires.</summary>
    public bool Destroy(GameTask task)
    {
        if (!_byTask.TryGetValue(task, out SecurityCamera? camera) || camera.IsExploded)
        {
            return false;
        }
        camera.Destroy();
        return true;
    }

    /// <summary>Resolves the camera owned by an exact level task.</summary>
    public bool TryGetCamera(GameTask task, out SecurityCamera? camera)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _byTask.TryGetValue(task, out camera);
    }

    /// <summary>Resolves the current transform for a rendered camera head.</summary>
    public bool TryGetHeadOrientation(
        WorldPlacement placement,
        float tickFraction,
        bool smooth,
        out Matrix3f orientation)
    {
        if (_byTask.TryGetValue(placement.Owner, out SecurityCamera? camera)
            && string.Equals(placement.Instance.Name, camera.HeadModelName, StringComparison.OrdinalIgnoreCase))
        {
            orientation = camera.PresentedOrientation(tickFraction, smooth);
            return true;
        }

        orientation = default;
        return false;
    }

    private void Collect(GameTask task, ScriptVarRegistry variables)
    {
        if (task is SecurityCameraTask declared)
        {
            SecurityCamera camera = new(declared, declared);
            _order[camera] = _cameras.Count;
            _cameras.Add(camera);
            _byTask[task] = camera;
            variables.BindBool(task, "isDetection", () => camera.IsDetection, value => camera.IsDetection = value);
            variables.BindBool(task, "isLastDetection", () => camera.IsLastDetection, value => camera.IsLastDetection = value);
            variables.BindInt(task, "nDetectionTime", () => camera.DetectionTime, value => camera.DetectionTime = value);
            variables.BindBool(task, "isExploded", () => camera.IsExploded, value =>
            {
                if (value)
                {
                    camera.Destroy();
                }
            });
        }
        else if (task is SecurityCameraControlTask controlTask)
        {
            string explodedSymbol = $"{controlTask.Name}.isExploded";
            ControlState control = new(
                controlTask,
                controlTask.DetectionExpression,
                () => variables.TryRead(explodedSymbol, out QvmValue blown) && blown.ToInt32() != 0);
            _controls.Add(control);
            variables.BindBool(task, "isDetection", () => control.IsDetection, value => control.IsDetection = value);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, variables);
        }
    }
}
