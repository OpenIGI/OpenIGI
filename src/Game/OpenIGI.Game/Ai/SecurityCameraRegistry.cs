using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Net;

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
/// <param name="Point">The point the cone and the sight ray test, in engine units, when no <paramref name="MarkSource"/> gives it.</param>
/// <param name="CamerasSide">
/// Whether it is on team 1, the side every lens scans for (<c>0x4415C0</c> passes team 1): soldiers and
/// cameras are, the player is not.
/// </param>
/// <param name="Alive">Whether it is alive — for a camera, not yet exploded.</param>
/// <param name="Task">Its task, which the sight ray does not collide with, or <see langword="null"/>.</param>
/// <param name="MarkSource">
/// What gives the point when a lens asks for it, as the original's slot reads its human through the task it refers to: a lens tests
/// the point only of what it registers -- its own side's dead, the other side's living -- so a guard on the cameras' side is
/// never placed at all.
/// </param>
public readonly record struct SecurityCameraSubject(
    object Key,
    Vector3d Point,
    bool CamerasSide,
    bool Alive,
    GameTask? Task = null,
    ISecurityCameraMark? MarkSource = null)
{
    /// <summary>Gets the point the cone and the sight ray test, in engine units.</summary>
    public Vector3d Mark => MarkSource?.CameraMark() ?? Point;
}

/// <summary>Where a lens tests a subject, worked out when the lens asks.</summary>
public interface ISecurityCameraMark
{
    /// <summary>The point the cone and the sight ray test, in engine units, as the subject stands now.</summary>
    /// <returns>The point.</returns>
    Vector3d CameraMark();
}

/// <summary>What a lens's scan is offered besides the cameras themselves, as it stands when the lens updates.</summary>
/// <param name="TargetFeet">The player's feet, in engine units.</param>
/// <param name="TargetCrouching">Whether the player is crouching, which lowers the mark.</param>
/// <param name="TargetAlive">Whether the player is alive.</param>
/// <param name="PlayerOnCamerasSide">Whether the player is on team 1, the cameras' side.</param>
/// <param name="Soldiers">Every live soldier in the global human list's order (<c>0x45FE30</c>).</param>
/// <param name="Partners">
/// A co-op session's other players, offered after the player as the humans they are: the detector (<c>0x489750</c>)
/// registers every human, not only the first player, so a camera sees a partner as it sees the player.
/// </param>
public readonly record struct SecurityCameraScan(
    Vector3d TargetFeet,
    bool TargetCrouching,
    bool TargetAlive,
    bool PlayerOnCamerasSide,
    IReadOnlyList<SecurityCameraSubject> Soldiers,
    IReadOnlyList<SecurityCameraSubject>? Partners = null)
{
    /// <summary>Gets a player's subject as a lens tests it: its mark is above the feet, lower while crouching.</summary>
    /// <param name="key">Who it is.</param>
    /// <param name="feet">Its feet, in engine units.</param>
    /// <param name="crouching">Whether it is crouching.</param>
    /// <param name="camerasSide">Whether it is on team 1, the cameras' side.</param>
    /// <param name="alive">Whether it is alive.</param>
    /// <returns>The subject.</returns>
    public static SecurityCameraSubject PlayerSubject(object key, Vector3d feet, bool crouching, bool camerasSide, bool alive) =>
        new(
            key,
            new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + (crouching ? SecurityCamera.CrouchingTargetHeight : SecurityCamera.StandingTargetHeight)),
            camerasSide,
            alive);
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
public sealed class SecurityCamera : Saves.IExchangedState
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

    /// <summary>Initializes a new instance of the <see cref="SecurityCamera"/> class from what its level declares of it.</summary>
    /// <param name="owner">The camera's task.</param>
    /// <param name="task">What the level declares of the camera.</param>
    public SecurityCamera(DeclaredTask owner, ISecurityCameraDeclaration task)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(task);
        Task = owner;
        OnExpression = task.OnExpression;
        HeadModelName = task.CameraModel;
        if (HeadModelName.Length == 0)
        {
            HeadModelName = WorldScene.SecurityCameraModels[1];
        }

        _holderGamma = task.HolderGamma;
        _cameraAlpha = task.CameraAlpha;
        _cameraGamma = task.CameraGamma;
        _rightLimit = task.RotateGammaRight * DegreesToRadians;
        _leftLimit = task.RotateGammaLeft * DegreesToRadians;
        _scanStep = task.RotateGammaSpeed * DegreesToRadians / 30f;
        _endDelayTicks = Math.Max(0, (int)(task.GammaDelay * 30f));

        double alpha = (task.ViewconeAlpha / 2.0) * Math.PI / 180.0;
        double gamma = (task.ViewconeGamma / 2.0) * Math.PI / 180.0;
        double length = task.ViewconeLength * WorldScene.UnitsPerMetre;
        _viewCone = new AiSnapshotCone(alpha, gamma, length);
        ViewconeAngle = task.ViewconeGamma * DegreesToRadians;
        ViewconeLength = ((X87)task.ViewconeLength * 4096f).Value;

        CurrentAlpha = _cameraAlpha;
        _previousAlpha = CurrentAlpha;
        _previousGamma = CurrentGamma;
        HeadPositionUnits = task.HolderPosition;
        HolderPositionUnits = HeadPositionUnits;
        RefreshTransform();
    }

    /// <summary>Gets the task owning this camera.</summary>
    public DeclaredTask Task { get; }

    /// <summary>Gets the whole angle its view spans across, radians, as the map computer fans it (<c>0x442DD0</c>).</summary>
    public float ViewconeAngle { get; }

    /// <summary>Gets how far it sees, world units.</summary>
    public double ViewconeLength { get; }

    /// <summary>Gets the level expression that powers the camera.</summary>
    public string OnExpression { get; }

    /// <summary>Gets whether the holder is powered: <c>SCamera</c>'s <c>+360</c>.</summary>
    /// <remarks>
    /// The holder's update (<c>0x442610</c>) runs its lens first and only then writes this from its On Expression
    /// -- nought once exploded (<c>+368</c>), unchanged when it has no expression -- so the lens (<c>0x441770</c>)
    /// always reads the value the holder left last tick. The constructor (<c>0x4423F0</c>) starts it at nought:
    /// a camera's first tick is unpowered.
    /// </remarks>
    public bool IsPowered { get; set; }

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

    /// <summary>Ties the camera to the placement it is drawn as.</summary>
    public void Bind(WorldPlacement placement)
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

    // The camera as the match began, which a round puts back: its scan, its lamp, whether it was shot out or saw anyone.
    private Start _start;

    /// <summary>Marks the state the level starts in, to be put back by <see cref="ResetToLevelStart"/>.</summary>
    public void MarkLevelStart() => _start = new Start(
        _targetGamma, _lampPulseCountdown, _scanDirection, _endDelay, _resumeScanDelay, _targetKey, _lampPulseLarge,
        IsPowered, HeadPositionUnits, CurrentAlpha, CurrentGamma, IsActive, IsDetection, IsLastDetection, DetectionTime,
        IsExploded, Beep, IsMoving, HolderPositionUnits, IsBodyTarget);

    /// <summary>Puts back the state the level started in.</summary>
    public void ResetToLevelStart()
    {
        Start s = _start;
        _targetGamma = s.TargetGamma;
        _lampPulseCountdown = s.LampPulseCountdown;
        _scanDirection = s.ScanDirection;
        _endDelay = s.EndDelay;
        _resumeScanDelay = s.ResumeScanDelay;
        _targetKey = s.TargetKey;
        _lampPulseLarge = s.LampPulseLarge;
        IsPowered = s.IsPowered;
        HeadPositionUnits = s.HeadPositionUnits;
        CurrentAlpha = s.CurrentAlpha;
        CurrentGamma = s.CurrentGamma;
        _previousAlpha = s.CurrentAlpha;
        _previousGamma = s.CurrentGamma;
        IsActive = s.IsActive;
        IsDetection = s.IsDetection;
        IsLastDetection = s.IsLastDetection;
        DetectionTime = s.DetectionTime;
        IsExploded = s.IsExploded;
        Beep = s.Beep;
        IsMoving = s.IsMoving;
        HolderPositionUnits = s.HolderPositionUnits;
        IsBodyTarget = s.IsBodyTarget;
        RefreshTransform();
    }

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    // Whom it was looking at is not kept: a camera read back that still sees someone takes them up as it would a new sight.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        Start levelStart = _start;
        MarkLevelStart();
        Start s = _start;
        _start = new Start(
            exchange.Real(s.TargetGamma),
            exchange.Int(s.LampPulseCountdown),
            exchange.Int(s.ScanDirection),
            exchange.Int(s.EndDelay),
            exchange.Int(s.ResumeScanDelay),
            null,
            exchange.Bool(s.LampPulseLarge),
            exchange.Bool(s.IsPowered),
            exchange.Position(s.HeadPositionUnits),
            exchange.Real(s.CurrentAlpha),
            exchange.Real(s.CurrentGamma),
            exchange.Bool(s.IsActive),
            exchange.Bool(s.IsDetection),
            exchange.Bool(s.IsLastDetection),
            exchange.Int(s.DetectionTime),
            exchange.Bool(s.IsExploded),
            (SecurityCameraBeep)exchange.Int((int)s.Beep),
            exchange.Bool(s.IsMoving),
            exchange.Position(s.HolderPositionUnits),
            exchange.Bool(s.IsBodyTarget));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

    private readonly record struct Start(
        float TargetGamma,
        int LampPulseCountdown,
        int ScanDirection,
        int EndDelay,
        int ResumeScanDelay,
        object? TargetKey,
        bool LampPulseLarge,
        bool IsPowered,
        Vector3d HeadPositionUnits,
        float CurrentAlpha,
        float CurrentGamma,
        bool IsActive,
        bool IsDetection,
        bool IsLastDetection,
        int DetectionTime,
        bool IsExploded,
        SecurityCameraBeep Beep,
        bool IsMoving,
        Vector3d HolderPositionUnits,
        bool IsBodyTarget);

    /// <summary>Breaks the camera: its lens is out for the rest of the level.</summary>
    public void Destroy()
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

    /// <summary>Gets the camera as a co-op server sends it to its clients.</summary>
    /// <returns>Its state.</returns>
    public ReplicatedCamera Replicate()
    {
        ReplicatedCameraCondition flags = ReplicatedCameraCondition.None;
        if (IsPowered) { flags |= ReplicatedCameraCondition.Powered; }
        if (IsActive) { flags |= ReplicatedCameraCondition.Active; }
        if (IsExploded) { flags |= ReplicatedCameraCondition.Exploded; }
        if (IsDetection) { flags |= ReplicatedCameraCondition.Detection; }
        if (IsLastDetection) { flags |= ReplicatedCameraCondition.LastDetection; }
        if (Beep != SecurityCameraBeep.None) { flags |= ReplicatedCameraCondition.Seeing; }
        if (IsBodyTarget) { flags |= ReplicatedCameraCondition.Body; }
        if (IsMoving) { flags |= ReplicatedCameraCondition.Moving; }
        return new ReplicatedCamera(
            Quantize.AngleToBits(CurrentAlpha),
            Quantize.AngleToBits(CurrentGamma),
            (byte)Math.Clamp(DetectionTime, 0, byte.MaxValue),
            flags);
    }

    // A co-op client's lens, posed by the server's as the newest snapshot has it instead of scanning: the client's copy of
    // the level holds only its own player, and a camera that answered to it alone would be red for a joiner no alarm heard
    // and green for a host the server's had seen. The lamp keeps its own pulse.
    /// <summary>Takes the camera as a co-op server sent it, on a machine that mirrors the level.</summary>
    public void TickMirrored(in ReplicatedCamera state)
    {
        _previousAlpha = CurrentAlpha;
        _previousGamma = CurrentGamma;
        PulseLamp();
        ReplicatedCameraCondition flags = state.Flags;
        if ((flags & ReplicatedCameraCondition.Exploded) != 0 && !IsExploded)
        {
            Destroy();
        }

        IsPowered = (flags & ReplicatedCameraCondition.Powered) != 0;
        IsActive = (flags & ReplicatedCameraCondition.Active) != 0 && !IsExploded;
        IsDetection = (flags & ReplicatedCameraCondition.Detection) != 0;
        IsLastDetection = (flags & ReplicatedCameraCondition.LastDetection) != 0;
        IsBodyTarget = (flags & ReplicatedCameraCondition.Body) != 0;
        IsMoving = (flags & ReplicatedCameraCondition.Moving) != 0;
        DetectionTime = state.DetectionTime;
        Beep = (flags & ReplicatedCameraCondition.Seeing) == 0 ? SecurityCameraBeep.None
            : !IsDetection ? SecurityCameraBeep.Suspicious
            : IsBodyTarget ? SecurityCameraBeep.BodyDetected
            : SecurityCameraBeep.Detected;
        CurrentAlpha = Quantize.AngleFromBits(state.Alpha);
        CurrentGamma = Quantize.AngleFromBits(state.Gamma);
        RefreshTransform();
    }

    /// <summary>Advances the camera one tick: its power, its sweep and what its lens sees.</summary>
    public void Tick(
        bool powered,
        IReadOnlyList<SecurityCameraSubject> subjects,
        WorldTraceCast? sight)
    {
        _previousAlpha = CurrentAlpha;
        _previousGamma = CurrentGamma;
        PulseLamp();

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

    private void PulseLamp()
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
}

/// <summary>What a level declares of a security camera: where its holder hangs, how its head turns and what it sees.</summary>
public interface ISecurityCameraDeclaration
{
    /// <summary>Gets the expression that powers the camera.</summary>
    string OnExpression { get; }

    /// <summary>Gets the head's model; empty for the type's own.</summary>
    string CameraModel { get; }

    /// <summary>Gets the holder's heading, radians.</summary>
    float HolderGamma { get; }

    /// <summary>Gets the head's pitch, radians.</summary>
    float CameraAlpha { get; }

    /// <summary>Gets the head's heading, radians.</summary>
    float CameraGamma { get; }

    /// <summary>Gets how far the head turns to its right, degrees.</summary>
    int RotateGammaRight { get; }

    /// <summary>Gets how far the head turns to its left, degrees.</summary>
    int RotateGammaLeft { get; }

    /// <summary>Gets how fast the head turns, degrees a second.</summary>
    int RotateGammaSpeed { get; }

    /// <summary>Gets how long the head rests at each end of its sweep, seconds.</summary>
    float GammaDelay { get; }

    /// <summary>Gets the view cone's whole pitch angle, degrees.</summary>
    int ViewconeAlpha { get; }

    /// <summary>Gets the view cone's whole heading angle, degrees.</summary>
    int ViewconeGamma { get; }

    /// <summary>Gets how far the camera sees, metres.</summary>
    float ViewconeLength { get; }

    /// <summary>Gets where the holder hangs, world units.</summary>
    Vector3d HolderPosition { get; }
}
