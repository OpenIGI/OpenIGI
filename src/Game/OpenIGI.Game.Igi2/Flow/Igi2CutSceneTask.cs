using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A cutscene, I.G.I. 2's <c>CutScene</c>: its clock and its end, exporting <c>isRun</c>, <c>isFinished</c> and <c>nTick</c>.</summary>
/// <remarks>
/// <para>
/// <c>CutScene_RunHandler</c> (<c>igi2 0x43A470</c>): its children run, then its <c>Run</c> script says whether it runs (it does
/// when there is none), which every running cutscene adds to <c>CutScene_isRunning</c>; its <c>Reset</c> script, when it holds,
/// winds it back to its start. While it runs its cameras are placed for the tick (<c>CutScene_SetCamera</c>, <c>0x439F20</c>)
/// and its clock advances: by the <c>Time delta</c> script's seconds when there is one, never below nought, else by a tick.
/// Its <c>Start expression</c> runs the tick it starts and its <c>Stop expression</c> the tick it stops.
/// </para>
/// <para>
/// It ends when its time passes the sum of its cameras' durations, each scaled by its time scale and summed in the FPU's
/// chopped single precision; it then holds its clock at the end. One with no cameras never ends.
/// </para>
/// <para>
/// Its camera for the tick (<c>CutScene_InterpolateCamera</c>, <c>0x438F10</c>) is its four cameras about the time, the one on
/// and its neighbours (a camera that is not smoothed to the next stands in for the one after it): every value interpolated
/// between them, its frame from the angles; a camera looking at a target turned to it; letterboxed by its fades; and shaken by
/// the shake's random stream. It is what the level shows while the cutscene runs. Its filters are its presentation's, and a
/// camera's link (<c>EditCamera_TransformRelativeToLinkQTask</c>, <c>0x5815E0</c>), which no mission 1 camera uses, is not ported.
/// </para>
/// <para>
/// It is made as though it ran last (<c>CutScene_CreateHandler</c>, <c>0x43A0E0</c>): one running from its first tick does not
/// run its start expression, and one that is not runs its stop expression once.
/// </para>
/// </remarks>
public sealed partial class Igi2CutSceneTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "CutScene";

    /// <summary>The most cameras a cutscene takes, <c>atCutSceneCamera[100]</c>.</summary>
    public const int MaxCameras = 100;

    // The highest level id a camera looks a target up by, exclusive.
    private const int LevelIds = 0x1000;

    // A half turn and a whole one, as CutScene_AdjustAngles compares and steps by: the double 3.141592653589794 and the float
    // 6.2831855.
    private const double HalfTurn = 3.141592653589794;
    private const float FullTurn = 6.2831855f;

    // A shake draw's sixteen bits as a share, 2^-16.
    private const double ShakeUnit = 1.52587890625e-05;

    private LevelExpression? _run;
    private LevelExpression? _reset;
    private LevelExpression? _forward;
    private LevelExpression? _start;
    private LevelExpression? _stop;
    private bool _started;
    private byte _isRun;
    private byte _isFinished;
    private bool _wasRun = true;
    private int _lastActive = -1;
    private Vector3d _lastPosition;
    private float[] _lastOrientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private Vector3d _previousPosition;
    private bool _isBandFilter;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation in the editor.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that says whether it runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run")]
    public string RunScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that winds it back to its start.</summary>
    [ScriptParam(ScriptParamType.VarString, "Reset")]
    public string ResetScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose seconds its clock advances by each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Time delta (seconds)")]
    public string TimeDeltaScript { get; set; } = string.Empty;

    /// <summary>Gets or sets where its clock starts, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Start time (seconds)")]
    public float StartTime { get; set; }

    /// <summary>Gets or sets what its cameras' durations are scaled by, <c>vTimeScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time scale")]
    public float TimeScale { get; set; } = 1f;

    /// <summary>Gets or sets how much of the screen's height its letterbox leaves.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor")]
    public float ViewportHeightFactor { get; set; } = 1f;

    /// <summary>Gets or sets how long the letterbox takes to close, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor fade in time")]
    public float ViewportFadeInTime { get; set; }

    /// <summary>Gets or sets how long the letterbox takes to open, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewport height factor fade out time")]
    public float ViewportFadeOutTime { get; set; }

    /// <summary>Gets or sets the time of day it shows.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time of day")]
    public float TimeOfDay { get; set; }

    /// <summary>Gets or sets the script run the tick it starts.</summary>
    [ScriptParam(ScriptParamType.VarString, "Start expression")]
    public string StartScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script run the tick it stops.</summary>
    [ScriptParam(ScriptParamType.VarString, "Stop expression")]
    public string StopScript { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it ran this tick, <c>isRun</c>.</summary>
    public bool IsRun => _isRun != 0;

    /// <summary>Gets a value indicating whether it has played to its end, <c>isFinished</c>.</summary>
    public bool IsFinished => _isFinished != 0;

    /// <summary>Gets its clock, <c>nTick</c>.</summary>
    public int Tick { get; private set; }

    /// <summary>Gets its cameras in the order they are shown, with when each starts and the time they add up to.</summary>
    /// <returns>The cameras' start times, and the total.</returns>
    public (IReadOnlyList<(Igi2EditCameraTask Camera, float Start)> Cameras, float Total) Cameras()
    {
        List<(Igi2EditCameraTask, float)> cameras = [];
        Fpu total = (Fpu)0.0;
        if (FirstCamera(this) is { } first)
        {
            Register(cameras, first, ref total);
        }

        return (cameras, total.F);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!_started)
        {
            // CutScene_UpdateInternalDataHandler (igi2 0x43A6C0): fades of less than nought are none, and the clock starts at
            // its start time in ticks.
            ViewportFadeInTime = ViewportFadeInTime < 0f ? 0f : ViewportFadeInTime;
            ViewportFadeOutTime = ViewportFadeOutTime < 0f ? 0f : ViewportFadeOutTime;
            Tick = Fpu.ToInt32((Fpu)(double)Igi2LevelRun.Frequency * StartTime);
            _run = Igi2LevelExpressions.Create(RunScript, Name, level.Natives);
            _reset = Igi2LevelExpressions.Create(ResetScript, Name, level.Natives);
            _forward = Igi2LevelExpressions.Create(TimeDeltaScript, Name, level.Natives);
            _start = Igi2LevelExpressions.Create(StartScript, Name, level.Natives);
            _stop = Igi2LevelExpressions.Create(StopScript, Name, level.Natives);
            _started = true;
        }

        level.RunChildren(this);
        _isRun = _run!.IsEmpty ? (byte)1 : (byte)LevelExpression.Truncate(level.Evaluate(_run));
        if (IsRun)
        {
            level.MarkCutsceneRunning();
        }

        if (!_reset!.IsEmpty && LevelExpression.Flag(level.Evaluate(_reset)))
        {
            Tick = 0;
            _isFinished = 0;
        }

        if (IsRun)
        {
            PlaceCameras(level);
            if (!_forward!.IsEmpty)
            {
                double seconds = level.Evaluate(_forward);
                Tick = Math.Max(0, Tick + (int)LevelExpression.Truncate(seconds * Igi2LevelRun.Frequency));
            }
            else
            {
                Tick++;
            }
        }

        if (!_wasRun && IsRun && !_start!.IsEmpty)
        {
            level.Evaluate(_start);
        }
        else if (_wasRun && !IsRun && !_stop!.IsEmpty)
        {
            level.Evaluate(_stop);
        }

        // Stopped, its filters are taken down (CutScene_RunHandler, igi2 0x43A5D9).
        if (!IsRun)
        {
            _isBandFilter = false;
        }

        _wasRun = IsRun;
    }

    /// <inheritdoc/>
    /// <remarks><c>CutScene_RegisterScriptSymbolsHandler</c> (<c>igi2 0x43A200</c>): two bytes and an integer, all writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", () => _isRun, value => _isRun = (byte)value, sizeof(byte));
        variables.BindInt(this, "isFinished", () => _isFinished, value => _isFinished = (byte)value, sizeof(byte));
        variables.BindInt(this, "nTick", () => Tick, value => Tick = value);
    }

    private static Igi2EditCameraTask? FirstCamera(GameTask task) => task.Children.OfType<Igi2EditCameraTask>().FirstOrDefault();

    // CutScene_RegisterCutSceneCamera (igi2 0x438D50): a camera, then the cameras under it, then its next sibling camera.
    private void Register(List<(Igi2EditCameraTask, float)> cameras, Igi2EditCameraTask first, ref Fpu total)
    {
        for (GameTask? task = first; task is not null; task = task.NextSibling)
        {
            if (task is not Igi2EditCameraTask camera || cameras.Count >= MaxCameras)
            {
                continue;
            }

            cameras.Add((camera, total.F));
            total = ((Fpu)TimeScale * camera.Duration) + total;
            if (FirstCamera(camera) is { } inner)
            {
                Register(cameras, inner, ref total);
            }
        }
    }

    // CutScene_SetCamera (igi2 0x439F20): the camera on is the last to have started; past the cameras' total time it has
    // ended, its clock held at the end, the total in ticks, and its camera counted as changed.
    private void PlaceCameras(Igi2LevelRun level)
    {
        (IReadOnlyList<(Igi2EditCameraTask Camera, float Start)> cameras, float total) = Cameras();
        if (cameras.Count == 0)
        {
            return;
        }

        Fpu frequency = (Fpu)(double)(float)Igi2LevelRun.Frequency;
        float time = ((Fpu)(double)Tick / frequency).F;
        AdjustAngles(cameras);
        int active = -1;
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Start > time)
            {
                break;
            }

            active = i;
        }

        if (time > total)
        {
            _isFinished = 1;
            Tick = Fpu.ToInt32(frequency * total);
            _lastActive = -1;
        }

        bool changed = false;
        if (_lastActive != active)
        {
            _lastActive = active;
            changed = true;
        }

        (Igi2EditCameraTask Camera, float Start) Near(int offset) => cameras[Math.Clamp(active + offset, 0, cameras.Count - 1)];
        var c0 = Near(-1);
        var c1 = Near(0);
        var c2 = Near(1);
        var c3 = Near(2);
        if (!c0.Camera.SmoothToNext)
        {
            c0 = c1;
        }

        if (!c1.Camera.SmoothToNext)
        {
            c2 = c1;
        }

        if (!c2.Camera.SmoothToNext)
        {
            c3 = c2;
        }

        level.SetCutsceneView(Interpolate([c0, c1, c2, c3], changed, time, total, level.CutsceneShake));
    }

    // CutScene_AdjustAngles (igi2 0x439D50): each camera's angles brought within a half turn of the one before's, for good.
    private static void AdjustAngles(IReadOnlyList<(Igi2EditCameraTask Camera, float Start)> cameras)
    {
        Igi2EditCameraTask first = cameras[0].Camera;
        float alpha = first.Alpha, beta = first.Beta, gamma = first.Gamma;
        for (int i = 1; i < cameras.Count; i++)
        {
            Igi2EditCameraTask camera = cameras[i].Camera;
            camera.Alpha = Near(camera.Alpha, alpha);
            alpha = camera.Alpha;
            camera.Beta = Near(camera.Beta, beta);
            beta = camera.Beta;
            camera.Gamma = Near(camera.Gamma, gamma);
            gamma = camera.Gamma;
        }

        static float Near(float angle, float to)
        {
            while (((Fpu)angle - to).Value > HalfTurn)
            {
                angle = ((Fpu)angle - FullTurn).F;
            }

            while (((Fpu)angle - to).Value < -HalfTurn)
            {
                angle = ((Fpu)angle + FullTurn).F;
            }

            return angle;
        }
    }

    // CutScene_InterpolateCamera (igi2 0x438F10), all but its filters' and the link's.
    private Igi2CutsceneView Interpolate(
        (Igi2EditCameraTask Camera, float Start)[] keys, bool changed, double time, double total, Igi2CutsceneShake? shakes)
    {
        Igi2EditCameraTask from = keys[1].Camera, to = keys[2].Camera;
        Fpu Scalar(Func<Igi2EditCameraTask, double> value) => Igi2CutsceneCameras.InterpolateScalar(
            value(keys[0].Camera), value(keys[1].Camera), value(keys[2].Camera), value(keys[3].Camera),
            keys[0].Start, keys[1].Start, keys[2].Start, keys[3].Start, time);

        // A camera with no time of day of its own takes the cutscene's.
        double Light(Igi2EditCameraTask camera) => camera.TimeOfDay < 0f ? TimeOfDay : camera.TimeOfDay;
        Vector3d position = new(Scalar(static c => c.Position.X).Value, Scalar(static c => c.Position.Y).Value, Scalar(static c => c.Position.Z).Value);
        float alpha = Scalar(static c => c.Alpha).F;
        float beta = Scalar(static c => c.Beta).F;
        float gamma = Scalar(static c => c.Gamma).F;
        float width = Scalar(static c => c.FieldOfView).F;
        float light = Scalar(Light).F;
        float shake = Scalar(static c => c.CameraShake).F;
        float noise = Scalar(static c => c.Noise).F;

        // The band filter's colour runs from this camera's to the next's; one with no noise reaches the next's half a second
        // early.
        Vector3f colour = from.FilterColour;
        float span = ((Fpu)keys[2].Start - keys[1].Start).F;
        if (span != 0f)
        {
            Fpu share;
            if (_isBandFilter && noise == 0f)
            {
                share = !(span > 0.5f) ? 1f : ((Fpu)time - keys[1].Start) / ((Fpu)span - 0.5f);
                share = share.Value < 0.0 ? 0f : (1.0 < share.Value ? 1f : share);
            }
            else
            {
                share = ((Fpu)time - keys[1].Start) / span;
            }

            Vector3f a = from.FilterColour, b = to.FilterColour;
            colour = new Vector3f(
                ((((Fpu)b.X - a.X) * share) + a.X).F, ((((Fpu)b.Y - a.Y) * share) + a.Y).F, ((((Fpu)b.Z - a.Z) * share) + a.Z).F);
        }

        float[] frame = EngineEuler.Rotation(alpha, beta, gamma);

        // Linked, the camera is carried with its link when it changes or updates, and its position held from then otherwise.
        if (from.LinkTaskId >= 0)
        {
            if (changed || from.UpdateLinkContinuously)
            {
                if ((uint)from.LinkTaskId < LevelIds && Tree.FromId(from.LinkTaskId) is IIgi2CameraTarget link)
                {
                    Igi2CutsceneCameras.TransformRelativeToLink(ref position, frame, from.LinkedPosition, from.LinkedOrientation, link);
                }

                _lastPosition = position;
            }
            else
            {
                position = _lastPosition;
            }
        }

        if (from.TargetTaskId >= 0)
        {
            if (changed || from.UpdateTargetContinuously)
            {
                if ((uint)from.TargetTaskId < LevelIds && Tree.FromId(from.TargetTaskId) is IIgi2CameraTarget target)
                {
                    frame = Igi2CutsceneCameras.FollowTarget(position, target.CameraTargetPosition);
                }

                _lastOrientation = [.. frame];
            }
            else
            {
                frame = [.. _lastOrientation];
            }
        }

        // The letterbox opens over the fade-in and closes over the fade-out.
        float opening = 1f;
        if (ViewportFadeInTime != 0f && ViewportFadeInTime > time)
        {
            opening = ((Fpu)time / ViewportFadeInTime).F;
        }

        Fpu closing = 1f;
        if (ViewportFadeOutTime != 0f)
        {
            Fpu left = (Fpu)total - time;
            if (left.Value < ViewportFadeOutTime)
            {
                closing = left / ViewportFadeOutTime;
            }
        }

        float open = opening < closing.Value ? opening : closing.F;
        open = open < 0f ? 0f : (1f < open ? 1f : open);

        if (shakes is not null)
        {
            position = new Vector3d(Shaken(position.X), Shaken(position.Y), Shaken(position.Z));
            float turnGamma = Turn(shakes.Next()), turnBeta = Turn(shakes.Next()), turnAlpha = Turn(shakes.Next());
            frame = Igi2CutsceneCameras.Shake(frame, EngineEuler.Rotation(turnAlpha, turnBeta, turnGamma));
        }

        float height = (((Fpu)open * ViewportHeightFactor) + ((Fpu)1f - open)).F;
        Vector3f speed = default;
        if (Tick != 0)
        {
            speed = new Vector3f(
                ((Fpu)position.X - _previousPosition.X).F, ((Fpu)position.Y - _previousPosition.Y).F, ((Fpu)position.Z - _previousPosition.Z).F);
        }

        _previousPosition = position;
        if (changed)
        {
            _isBandFilter = from.FilterId is Igi2CutsceneCameras.BandFilter or Igi2CutsceneCameras.BandUpdateFilter;
        }

        return new Igi2CutsceneView(position, frame, width, light, speed, height, from.FilterId, colour, noise, changed);

        double Shaken(double at) => (((((Fpu)0.5f - ((Fpu)(double)shakes!.Next() * ShakeUnit)) * shake) * Igi2CutsceneCameras.ShakeStep) + at).Value;
        float Turn(int draw) => ((((Fpu)0.5f - ((Fpu)(double)draw * ShakeUnit)) * shake) * Igi2CutsceneCameras.ShakeTurn).F;
    }
}
