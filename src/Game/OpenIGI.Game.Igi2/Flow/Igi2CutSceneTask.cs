using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

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
/// <para>Only the clock is here; what the cameras show is the cutscene's presentation.</para>
/// </remarks>
public sealed partial class Igi2CutSceneTask : GameTask, IIgi2RunTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "CutScene";

    /// <summary>The most cameras a cutscene takes, <c>atCutSceneCamera[100]</c>.</summary>
    public const int MaxCameras = 100;

    private Igi2LevelExpression? _run;
    private Igi2LevelExpression? _reset;
    private Igi2LevelExpression? _forward;
    private Igi2LevelExpression? _start;
    private Igi2LevelExpression? _stop;
    private bool _started;
    private byte _isRun;
    private byte _isFinished;
    private bool _wasRun;

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
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!_started)
        {
            // CutScene_UpdateInternalDataHandler (igi2 0x43A6C0): the clock starts at its start time in ticks.
            Tick = Fpu.ToInt32((Fpu)(double)Igi2LevelRun.Frequency * StartTime);
            _run = new Igi2LevelExpression(RunScript, Name, level.Natives);
            _reset = new Igi2LevelExpression(ResetScript, Name, level.Natives);
            _forward = new Igi2LevelExpression(TimeDeltaScript, Name, level.Natives);
            _start = new Igi2LevelExpression(StartScript, Name, level.Natives);
            _stop = new Igi2LevelExpression(StopScript, Name, level.Natives);
            _started = true;
        }

        level.RunChildren(this);
        _isRun = _run!.IsEmpty ? (byte)1 : (byte)Igi2LevelExpression.Truncate(level.Evaluate(_run));
        if (IsRun)
        {
            level.MarkCutsceneRunning();
        }

        if (!_reset!.IsEmpty && Igi2LevelExpression.Flag(level.Evaluate(_reset)))
        {
            Tick = 0;
            _isFinished = 0;
        }

        if (IsRun)
        {
            PlaceCameras();
            if (!_forward!.IsEmpty)
            {
                double seconds = level.Evaluate(_forward);
                Tick = Math.Max(0, Tick + (int)Igi2LevelExpression.Truncate(seconds * Igi2LevelRun.Frequency));
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

    // The clock half of CutScene_SetCamera (igi2 0x439F20): past the cameras' total time it has ended, and its clock is
    // held at the end, the total in ticks.
    private void PlaceCameras()
    {
        (IReadOnlyList<(Igi2EditCameraTask Camera, float Start)> cameras, float total) = Cameras();
        if (cameras.Count == 0)
        {
            return;
        }

        Fpu frequency = (Fpu)(double)(float)Igi2LevelRun.Frequency;
        float time = ((Fpu)(double)Tick / frequency).F;
        if (time > total)
        {
            _isFinished = 1;
            Tick = Fpu.ToInt32(frequency * total);
        }
    }
}
