using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

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
}

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
    private const float StandingTargetHeight = 2828.8f;
    private const float CrouchingTargetHeight = 1433.6f;

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
    private bool _hadTarget;
    private bool _lampPulseLarge;

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
        HeadPositionUnits = ReadPosition(task);
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

    internal void Tick(
        bool powered,
        Vector3d targetFeet,
        bool targetCrouching,
        bool targetAlive,
        WorldTraceCast? sight)
    {
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

        Vector3d target = new(
            targetFeet.X,
            targetFeet.Y,
            targetFeet.Z + (targetCrouching ? CrouchingTargetHeight : StandingTargetHeight));
        bool visible = targetAlive
            && new AiEyeSnapshot(
                sightOrigin,
                sightOrientation,
                _viewCone,
                default,
                secondaryEnabled: false).IsVisible(target)
            && !IsOccluded(sight, sightOrigin, target);

        if (visible)
        {
            if (!_hadTarget)
            {
                DetectionTime = DetectionDelayTicks;
                _hadTarget = true;
            }

            if (DetectionTime > 0)
            {
                DetectionTime--;
            }
            if (DetectionTime == 0)
            {
                IsDetection = true;
            }

            Beep = IsDetection ? SecurityCameraBeep.Detected : SecurityCameraBeep.Suspicious;
            Track(target, sightOrigin, sightOrientation);
        }
        else
        {
            DetectionTime = 0;
            if (_hadTarget)
            {
                _hadTarget = false;
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

    private bool IsOccluded(WorldTraceCast? sight, Vector3d origin, Vector3d target)
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
                IgnoredOwner: Task),
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
    private sealed class Control(DeclaredTask task, string expression)
    {
        public DeclaredTask Task { get; } = task;
        public string Expression { get; } = expression;
        public bool IsDetection { get; set; }
    }

    private readonly List<SecurityCamera> _cameras = [];
    private readonly Dictionary<GameTask, SecurityCamera> _byTask = [];
    private readonly List<Control> _controls = [];
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

    /// <summary>Collects cameras, exports their retail script variables and prepares their controllers.</summary>
    public static SecurityCameraRegistry Build(
        GameTask root,
        ScriptVarRegistry variables,
        WorldTraceCast? sight = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(variables);

        SecurityCameraRegistry registry = new(new LevelExpressionRuntime(variables), sight);
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

    /// <summary>Advances all lenses, then the <c>SCameraControl</c> expressions that consume them.</summary>
    public void Tick(Vector3d targetFeet, bool targetCrouching, bool targetAlive)
    {
        foreach (SecurityCamera camera in _cameras)
        {
            bool powered = _expressions.EvaluateCondition(camera.OnExpression, emptyValue: true);
            camera.Tick(powered, targetFeet, targetCrouching, targetAlive, _sight);
        }

        foreach (Control control in _controls)
        {
            control.IsDetection = _expressions.EvaluateCondition(control.Expression, emptyValue: false);
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
    public bool TryGetHeadOrientation(WorldPlacement placement, out Matrix3f orientation)
    {
        if (_byTask.TryGetValue(placement.Owner, out SecurityCamera? camera)
            && string.Equals(placement.Instance.Name, camera.HeadModelName, StringComparison.OrdinalIgnoreCase))
        {
            orientation = camera.Orientation;
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
            Control control = new(controlTask, ReadString(controlTask, "Detection Expression"));
            _controls.Add(control);
            variables.BindBool(task, "isDetection", () => control.IsDetection, value => control.IsDetection = value);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, variables);
        }
    }

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is string value ? value.TrimEnd('\r', '\n') : string.Empty;
}
