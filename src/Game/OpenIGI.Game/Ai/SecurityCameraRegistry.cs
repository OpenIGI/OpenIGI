using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Ai;

/// <summary>The audible state selected by retail <c>SCameraLens</c>.</summary>
public enum SecurityCameraBeep
{
    /// <summary>No detection loop.</summary>
    None,

    /// <summary>The camera sees an intruder but its 90-tick confirmation has not elapsed.</summary>
    Suspicious,

    /// <summary>The camera has confirmed the intruder and exports <c>isDetection</c>.</summary>
    Detected,

    /// <summary>
    /// The camera has found one of its own side fallen — a guard's body or a destroyed camera — and exports
    /// <c>isDetection</c> at once (<c>camera_beep_al</c>).
    /// </summary>
    BodyDetected,
}

/// <summary>
/// One object the camera's detection scan (<c>0x489750</c>) is offered this tick, as it describes itself:
/// a human through <c>0x45FE30</c> or another camera through <c>0x442A30</c>.
/// </summary>
/// <param name="Key">Who it is; a camera restarts its confirmation whenever this changes.</param>
/// <param name="Mark">The point the cone and the sight ray test, in engine units.</param>
/// <param name="CamerasSide">
/// Whether it is on team 1, the side every lens scans for (<c>0x4415C0</c> passes team 1): soldiers and
/// cameras are, the player is not.
/// </param>
/// <param name="Alive">Whether it is alive — for a camera, not yet exploded.</param>
/// <param name="Task">Its task, which the sight ray does not collide with, or <see langword="null"/>.</param>
public readonly record struct SecurityCameraSubject(
    object Key,
    Vector3d Mark,
    bool CamerasSide,
    bool Alive,
    GameTask? Task = null);

/// <summary>The lens lamp selected by retail <c>SCameraLens</c>'s render callback.</summary>
public enum SecurityCameraLamp
{
    /// <summary>The camera is unpowered or destroyed.</summary>
    Off,

    /// <summary>The powered camera has no target.</summary>
    Green,

    /// <summary>The camera is confirming or has confirmed a target.</summary>
    Red,
}

/// <summary>One retail <c>SCamera</c> and its articulated <c>SCameraLens</c> child.</summary>
/// <remarks>
/// This is a direct state-level transcription of <c>0x441770</c>/<c>0x441BF0</c>. Angles carried by
/// the level are degrees except for the holder/head base angles; the simulation runs at the retail
/// 30 Hz and therefore applies the original per-tick steps without frame-time scaling.
/// </remarks>
public sealed class SecurityCamera
{
    private const float DegreesToRadians = MathF.PI / 180f;
    private const float PitchStep = 2.5f * DegreesToRadians;
    private const float DisabledPitch = -75f * DegreesToRadians;
    private const float TrackingDeadZone = 5f * DegreesToRadians;
    private const float TrackingStep = 0.75f * DegreesToRadians;
    private const int DetectionDelayTicks = 90;
    private const int ResumeScanDelayTicks = 60;
    internal const float StandingTargetHeight = 2828.8f;
    internal const float CrouchingTargetHeight = 1433.6f;

    private readonly float _holderGamma;
    private readonly float _cameraGamma;
    private readonly float _cameraAlpha;
    private readonly float _rightLimit;
    private readonly float _leftLimit;
    private readonly float _scanStep;
    private readonly int _endDelayTicks;
    private readonly AiSnapshotCone _viewCone;

    private Vector3f _viewVertex;
    private Vector3f _lampVertex;
    private float _targetGamma;
    private int _lampPulseCountdown;
    private int _scanDirection;
    private int _endDelay;
    private int _resumeScanDelay;
    private object? _targetKey;
    private bool _lampPulseLarge;
    private float _previousAlpha;
    private float _previousGamma;

    internal SecurityCamera(DeclaredTask task)
    {
        Task = task;
        OnExpression = ReadString(task, "On Expression");
        HeadModelName = ReadString(task, WorldScene.SecurityCameraModelParameters[1]);
        if (HeadModelName.Length == 0)
        {
            HeadModelName = WorldScene.SecurityCameraModels[1];
        }

        _holderGamma = ReadFloat(task, "Holder Gamma");
        _cameraAlpha = ReadFloat(task, "Camera Alpha");
        _cameraGamma = ReadFloat(task, "Camera Gamma");
        _rightLimit = ReadInt(task, "Rotate Gamma Right (d)", 45) * DegreesToRadians;
        _leftLimit = ReadInt(task, "Rotate Gamma Left (d)", 45) * DegreesToRadians;
        _scanStep = ReadInt(task, "Rotate Gamma Speed (d/s)", 45) * DegreesToRadians / 30f;
        _endDelayTicks = Math.Max(0, (int)(ReadFloat(task, "Gamma Delay (s)", 1f) * 30f));

        double alpha = (ReadInt(task, "Viewcone Alpha (d)", 90) / 2.0) * Math.PI / 180.0;
        double gamma = (ReadInt(task, "Viewcone Gamma (d)", 90) / 2.0) * Math.PI / 180.0;
        double length = ReadFloat(task, "Viewcone length (m)", 10f) * WorldScene.UnitsPerMetre;
        _viewCone = new AiSnapshotCone(alpha, gamma, length);

        CurrentAlpha = _cameraAlpha;
        _previousAlpha = CurrentAlpha;
        _previousGamma = CurrentGamma;
        HeadPositionUnits = ReadPosition(task);
        HolderPositionUnits = HeadPositionUnits;
        RefreshTransform();
    }

    /// <summary>Gets the task owning this camera.</summary>
    public DeclaredTask Task { get; }

    /// <summary>Gets the level expression that powers the camera.</summary>
    public string OnExpression { get; }

    /// <summary>Gets the live/destroyed head's intact model name.</summary>
    public string HeadModelName { get; }

    /// <summary>Gets the current head origin, in engine units.</summary>
    public Vector3d HeadPositionUnits { get; private set; }

    /// <summary>Gets the current eye point (head magic vertex 1), in engine units.</summary>
    public Vector3d ViewPositionUnits { get; private set; }

    /// <summary>Gets the current lamp point (head magic vertex 0), in engine units.</summary>
    public Vector3d LampPositionUnits { get; private set; }

    /// <summary>Gets the current lens orientation.</summary>
    public Matrix3f Orientation { get; private set; }

    /// <summary>Gets a render-only lens orientation between the previous and current ticks.</summary>
    public Matrix3f PresentedOrientation(float tickFraction, bool smooth)
    {
        float alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f;
        float pitch = _previousAlpha + ((CurrentAlpha - _previousAlpha) * alpha);
        float yaw = LerpAngle(_previousGamma, CurrentGamma, alpha);
        return Matrix3f.FromEngineEulerAngles(
            pitch,
            0f,
            _holderGamma + _cameraGamma + yaw);
    }

    /// <summary>Gets the presentation-interpolated lamp point in engine units.</summary>
    public Vector3d PresentedLampPositionUnits(float tickFraction, bool smooth)
    {
        Matrix3f orientation = PresentedOrientation(tickFraction, smooth);
        Vector3f offset = orientation.Transposed().Transform(_lampVertex);
        return new Vector3d(
            HeadPositionUnits.X + offset.X,
            HeadPositionUnits.Y + offset.Y,
            HeadPositionUnits.Z + offset.Z);
    }

    /// <summary>Gets the lens pitch in radians.</summary>
    public float CurrentAlpha { get; private set; }

    /// <summary>Gets the scanning/tracking yaw added to the authored camera gamma.</summary>
    public float CurrentGamma { get; private set; }

    /// <summary>Gets whether the authored on-expression currently powers the camera.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets or sets the exported <c>isDetection</c> byte.</summary>
    public bool IsDetection { get; set; }

    /// <summary>Gets or sets the exported previous-tick <c>isLastDetection</c> byte.</summary>
    public bool IsLastDetection { get; set; }

    /// <summary>Gets or sets the exported confirmation countdown.</summary>
    public int DetectionTime { get; set; }

    /// <summary>Gets whether the lens has been destroyed.</summary>
    public bool IsExploded { get; private set; }

    /// <summary>Gets the detection loop selected this tick.</summary>
    public SecurityCameraBeep Beep { get; private set; }

    /// <summary>Gets whether yaw changed this tick and the <c>camera_move</c> loop should sound.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Gets the status colour drawn by <c>alarmlight.spr</c>.</summary>
    public SecurityCameraLamp Lamp => !IsActive
        ? SecurityCameraLamp.Off
        : IsDetection || DetectionTime > 0 ? SecurityCameraLamp.Red : SecurityCameraLamp.Green;

    /// <summary>Gets which half of the retail 16-tick lamp pulse is active.</summary>
    public bool IsLargeLampPulse => _lampPulseLarge;

    internal void Bind(WorldPlacement placement)
    {
        HeadPositionUnits = placement.Position * WorldScene.UnitsPerMetre;
        if (placement.Model.TryGetMagicVertex(1, out Vector3f viewVertex))
        {
            _viewVertex = viewVertex;
        }
        if (placement.Model.TryGetMagicVertex(0, out Vector3f lampVertex))
        {
            _lampVertex = lampVertex;
        }
        RefreshTransform();
    }

    internal void Destroy()
    {
        IsExploded = true;
        IsActive = false;
        IsDetection = false;
        DetectionTime = 0;
        Beep = SecurityCameraBeep.None;
        IsMoving = false;
    }

    /// <summary>Gets the camera's own mark as another lens's scan sees it — the holder origin.</summary>
    /// <remarks><c>0x442A30</c> describes an <c>SCamera</c> by its task position (<c>+32</c>).</remarks>
    public Vector3d HolderPositionUnits { get; private set; }

    /// <summary>Gets whether the confirmed target is one of the camera's own side fallen.</summary>
    public bool IsBodyTarget { get; private set; }

    internal void Tick(
        bool powered,
        IReadOnlyList<SecurityCameraSubject> subjects,
        WorldTraceCast? sight)
    {
        _previousAlpha = CurrentAlpha;
        _previousGamma = CurrentGamma;
        if (_lampPulseCountdown > 0)
        {
            _lampPulseCountdown--;
        }
        else
        {
            _lampPulseCountdown = 7;
            _lampPulseLarge = !_lampPulseLarge;
        }

        IsLastDetection = IsDetection;
        IsDetection = false;
        IsMoving = false;
        Beep = SecurityCameraBeep.None;
        IsActive = powered && !IsExploded;

        Matrix3f sightOrientation = Orientation;
        Vector3d sightOrigin = ViewPositionUnits;

        if (!IsActive)
        {
            CurrentAlpha = MoveTowards(CurrentAlpha, DisabledPitch, PitchStep);
            RefreshTransform();
            return;
        }

        CurrentAlpha = MoveTowards(CurrentAlpha, _cameraAlpha, PitchStep);

        // 0x441BF0 picks its target from the scan's two lists. Every lens scans with flags 6 (0x4415C0):
        // its own side DEAD goes to +428 and the other side ALIVE to +416; the living of its own side and
        // the other side's dead are never registered. The first visible entry of +428 is taken, then the
        // first visible entry of +416 replaces it -- an intruder outranks a body.
        SecurityCameraSubject? target = null;
        bool bodyTarget = false;
        AiEyeSnapshot eye = new(sightOrigin, sightOrientation, _viewCone, default, secondaryEnabled: false);
        foreach (bool fallenList in (ReadOnlySpan<bool>)[true, false])
        {
            foreach (SecurityCameraSubject subject in subjects)
            {
                bool registered = fallenList
                    ? subject.CamerasSide && !subject.Alive
                    : !subject.CamerasSide && subject.Alive;
                if (registered
                    && !ReferenceEquals(subject.Task, Task)
                    && eye.IsVisible(subject.Mark)
                    && !IsOccluded(sight, sightOrigin, subject.Mark, subject.Task))
                {
                    target = subject;
                    bodyTarget = fallenList;
                    break;
                }
            }
        }

        if (target is { } seen)
        {
            // A new target restarts the confirmation: none for a body (detected this very tick), 90
            // ticks for an intruder. The same target keeps counting down across ticks.
            if (!Equals(_targetKey, seen.Key))
            {
                _targetKey = seen.Key;
                DetectionTime = bodyTarget ? 0 : DetectionDelayTicks;
            }

            if (DetectionTime > 0)
            {
                DetectionTime--;
            }
            if (DetectionTime == 0)
            {
                IsDetection = true;
            }

            IsBodyTarget = bodyTarget;
            Beep = !IsDetection ? SecurityCameraBeep.Suspicious
                : bodyTarget ? SecurityCameraBeep.BodyDetected
                : SecurityCameraBeep.Detected;
            Track(seen.Mark, sightOrigin, sightOrientation);
        }
        else
        {
            DetectionTime = 0;
            IsBodyTarget = false;
            if (_targetKey is not null)
            {
                _targetKey = null;
                _resumeScanDelay = ResumeScanDelayTicks;
            }

            if (_resumeScanDelay > 0)
            {
                _resumeScanDelay--;
            }
            if (_resumeScanDelay == 0)
            {
                Scan();
            }
        }

        RefreshTransform();
    }

    private void Track(Vector3d target, Vector3d origin, Matrix3f orientation)
    {
        Vector3d delta = target - origin;
        double localX = (orientation.M31 * delta.Z)
            + (orientation.M21 * delta.Y)
            + (orientation.M11 * delta.X);
        double localY = (orientation.M32 * delta.Z)
            + (orientation.M22 * delta.Y)
            + (orientation.M12 * delta.X);
        float error = (float)-Math.Atan2(localX, localY);
        if (MathF.Abs(error) <= TrackingDeadZone)
        {
            return;
        }

        float before = CurrentGamma;
        CurrentGamma = Math.Clamp(
            CurrentGamma + Math.Clamp(error, -TrackingStep, TrackingStep),
            -_rightLimit,
            _leftLimit);
        IsMoving = CurrentGamma != before;
    }

    private void Scan()
    {
        if (_scanDirection == 0)
        {
            _scanDirection = 1;
            _targetGamma = _leftLimit;
        }

        if (_endDelay > 0)
        {
            _endDelay--;
            return;
        }

        float before = CurrentGamma;
        CurrentGamma = MoveTowards(CurrentGamma, _targetGamma, _scanStep);
        IsMoving = CurrentGamma != before;
        if (CurrentGamma != _targetGamma)
        {
            return;
        }

        _endDelay = _endDelayTicks;
        if (_scanDirection == -1)
        {
            _scanDirection = 1;
            _targetGamma = _leftLimit;
        }
        else
        {
            _scanDirection = -1;
            _targetGamma = -_rightLimit;
        }
    }

    private bool IsOccluded(WorldTraceCast? sight, Vector3d origin, Vector3d target, GameTask? subject)
    {
        if (sight is null)
        {
            return false;
        }

        return sight(
            new WorldTraceRequest(
                origin,
                target,
                WorldTraceMask.World,
                IgnoredOwner: Task,
                IgnoredSubject: subject,
                TracePortals: true),
            out _);
    }

    private void RefreshTransform()
    {
        Orientation = Matrix3f.FromEngineEulerAngles(
            CurrentAlpha,
            0f,
            _holderGamma + _cameraGamma + CurrentGamma);
        Vector3f eyeOffset = Orientation.Transposed().Transform(_viewVertex);
        Vector3f lampOffset = Orientation.Transposed().Transform(_lampVertex);
        ViewPositionUnits = new Vector3d(
            HeadPositionUnits.X + eyeOffset.X,
            HeadPositionUnits.Y + eyeOffset.Y,
            HeadPositionUnits.Z + eyeOffset.Z);
        LampPositionUnits = new Vector3d(
            HeadPositionUnits.X + lampOffset.X,
            HeadPositionUnits.Y + lampOffset.Y,
            HeadPositionUnits.Z + lampOffset.Z);
    }

    private static float MoveTowards(float value, float target, float step)
    {
        if (value < target)
        {
            return MathF.Min(value + step, target);
        }
        if (value > target)
        {
            return MathF.Max(value - step, target);
        }
        return value;
    }

    private static float LerpAngle(float from, float to, float amount)
    {
        float difference = MathF.IEEERemainder(to - from, 2f * MathF.PI);
        return from + (difference * amount);
    }

    private static Vector3d ReadPosition(DeclaredTask task) =>
        task.TryGetValue("Holder Position", out object? raw) && raw is Vector3d position ? position : default;

    private static int ReadInt(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? raw) ? raw switch
        {
            short value => value,
            int value => value,
            byte value => value,
            _ => fallback,
        } : fallback;

    private static float ReadFloat(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? raw) ? raw switch
        {
            float value => value,
            double value => (float)value,
            _ => fallback,
        } : fallback;

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is string value ? value.TrimEnd('\r', '\n') : string.Empty;
}

/// <summary>All security cameras and camera-control aggregators in a loaded level.</summary>
public sealed class SecurityCameraRegistry
{
    // An SCameraControl (0x440C20) is registered under Terminal (0x443680), so it is a terminal first: one with no
    // model, standing at the origin, whose On Expression powers the cameras that read its isOn.
    private sealed class Control(DeclaredTask task, string onExpression, string detectionExpression)
    {
        public DeclaredTask Task { get; } = task;
        public string OnExpression { get; } = onExpression;
        public string Expression { get; } = detectionExpression;
        public bool IsDetection { get; set; }

        // Terminal's constructor (0x443840) sets +576 to one: a control is on before it has ever ticked.
        public bool IsOn { get; set; } = true;
        public bool IsHacked { get; set; }
        public bool IsHackedThisTick { get; set; }
        public bool IsInUse { get; set; }
        public bool IsLastInUse { get; set; }
    }

    private readonly List<SecurityCamera> _cameras = [];
    private readonly Dictionary<GameTask, SecurityCamera> _byTask = [];
    private readonly List<Control> _controls = [];

    // Every camera and control in the order the level walks them, which is the order they tick in.
    private readonly List<object> _order = [];
    private readonly List<SecurityCameraSubject> _subjects = [];
    private readonly LevelExpressionRuntime _expressions;
    private readonly ScriptVarRegistry _variables;
    private readonly WorldTraceCast? _sight;

    private SecurityCameraRegistry(LevelExpressionRuntime expressions, ScriptVarRegistry variables, WorldTraceCast? sight)
    {
        _expressions = expressions;
        _variables = variables;
        _sight = sight;
    }

    /// <summary>Gets every camera in task-update order.</summary>
    public IReadOnlyList<SecurityCamera> Cameras => _cameras;

    /// <summary>Gets the number of <c>SCameraControl</c> aggregators.</summary>
    public int ControlCount => _controls.Count;

    /// <summary>Collects cameras, exports their retail script variables and prepares their controllers.</summary>
    public static SecurityCameraRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        WorldTraceCast? sight = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        SecurityCameraRegistry registry = new(new LevelExpressionRuntime(variables), variables, sight);
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

    /// <summary>Advances all lenses against the player alone.</summary>
    public void Tick(Vector3d targetFeet, bool targetCrouching, bool targetAlive) =>
        Tick(targetFeet, targetCrouching, targetAlive, playerOnCamerasSide: false, []);

    /// <summary>Advances every lens and every <c>SCameraControl</c>, in the order the level walks them.</summary>
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

        _subjects.Clear();
        _subjects.Add(new SecurityCameraSubject(
            PlayerKey,
            new Vector3d(
                targetFeet.X,
                targetFeet.Y,
                targetFeet.Z + (targetCrouching
                    ? SecurityCamera.CrouchingTargetHeight
                    : SecurityCamera.StandingTargetHeight)),
            playerOnCamerasSide,
            targetAlive));
        _subjects.AddRange(soldiers);
        foreach (SecurityCamera camera in _cameras)
        {
            _subjects.Add(new SecurityCameraSubject(
                camera, camera.HolderPositionUnits, CamerasSide: true, Alive: !camera.IsExploded, camera.Task));
        }

        // In the level's walk order: missions 6 and 8 declare each SCameraControl before the cameras whose On
        // Expression is its isOn, so a camera reads this tick's power, and a control reads its cameras' last tick.
        foreach (object entry in _order)
        {
            if (entry is SecurityCamera camera)
            {
                bool powered = _expressions.EvaluateCondition(camera.OnExpression, emptyValue: true, camera.Task);
                camera.Tick(powered, _subjects, _sight);
            }
            else if (entry is Control control)
            {
                TickControl(control);
            }
        }
    }

    // 0x440D70: the terminal's tick (0x4438E0), then the detection. An exploded control (the ExplodeObject base's
    // +400) is off and detects nothing; otherwise each expression it declares is evaluated into its byte, and one
    // it leaves empty leaves the byte alone. Its in-use and hacked-this-tick bytes only ever fall: a control has no
    // model, so nothing uses or hacks it.
    private void TickControl(Control control)
    {
        // The ExplodeObject base's byte, as the destructible registry exports it (absent where nothing does).
        bool exploded = _variables.TryRead(control.Task.Name + ".isExploded", out QvmValue value) && value.ToInt32() != 0;

        if (exploded)
        {
            control.IsOn = false;
        }
        else if (control.OnExpression.Length > 0)
        {
            control.IsOn = _expressions.EvaluateCondition(control.OnExpression, emptyValue: true, control.Task);
        }

        control.IsLastInUse = control.IsInUse;
        control.IsHackedThisTick = false;

        if (exploded)
        {
            control.IsDetection = false;
        }
        else if (control.Expression.Length > 0)
        {
            control.IsDetection = _expressions.EvaluateCondition(control.Expression, emptyValue: false, control.Task);
        }
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
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal))
        {
            SecurityCamera camera = new(declared);
            _cameras.Add(camera);
            _order.Add(camera);
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
        else if (task is DeclaredTask controlTask
            && string.Equals(task.Type.Name, "SCameraControl", StringComparison.Ordinal))
        {
            Control control = new(
                controlTask, ReadString(controlTask, "On Expression"), ReadString(controlTask, "Detection Expression"));
            _controls.Add(control);
            _order.Add(control);
            variables.BindBool(task, "isDetection", () => control.IsDetection, value => control.IsDetection = value);

            // Terminal's exports (0x443A60). The cameras of missions 6 and 8 are powered by the control's isOn; without
            // it every one of them read an unknown variable and started the mission switched off.
            variables.BindBool(task, "isOn", () => control.IsOn, value => control.IsOn = value);
            variables.BindBool(task, "isHacked", () => control.IsHacked, value => control.IsHacked = value);
            variables.BindBool(task, "isHackedThisTick", () => control.IsHackedThisTick, value => control.IsHackedThisTick = value);
            variables.BindBool(task, "isInUse", () => control.IsInUse, value => control.IsInUse = value);
            variables.BindBool(task, "isLastInUse", () => control.IsLastInUse, value => control.IsLastInUse = value);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, variables);
        }
    }

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is string value ? value.TrimEnd('\r', '\n') : string.Empty;
}
