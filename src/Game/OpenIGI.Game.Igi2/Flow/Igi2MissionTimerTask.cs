using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A mission timer's status, <c>eStatus</c>.</summary>
public enum Igi2MissionTimerStatus
{
    /// <summary>Waiting for its start expression.</summary>
    Waiting = 0,

    /// <summary>Counting down, and shown.</summary>
    Running = 1,

    /// <summary>Run out; no longer shown.</summary>
    Finished = 2,
}

/// <summary>
/// A countdown on screen, I.G.I. 2's <c>MissionTimer</c> (missiontimer.c): it starts when its start expression holds, counts its
/// time down a tick at a time except while its pause expression holds, and shows minutes and seconds until it runs out.
/// </summary>
/// <remarks>
/// <para>
/// <c>MissionTimer_UpdateInternalDataHandler</c>: the ticks left are its seconds times thirty, chopped. <c>MissionTimer_RunHandler</c>:
/// waiting, a start expression that holds sets it running and puts its display on the screen; running, unless a pause expression
/// holds, it loses a tick, and with none left it is finished and its display goes.
/// </para>
/// <para>
/// <c>MissionTimerDrawTask_DrawHandler</c>: <c>MM : SS</c> in <c>font3</c>, its minutes the ticks over 1800 and its seconds the rest over
/// thirty, centred at half the screen's width and nine tenths of its height.
/// </para>
/// </remarks>
public sealed partial class Igi2MissionTimerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MissionTimer";

    /// <summary>The font its display is drawn in.</summary>
    public const string Font = "LOCAL:common/fonts/font3.fnt";

    /// <summary>Where its display is centred across the screen, as a part of its width.</summary>
    public const float DisplayX = 0.5f;

    /// <summary>Where its display is centred down the screen, as a part of its height.</summary>
    public const float DisplayY = 0.9f;

    private LevelExpression? _start;
    private LevelExpression? _pause;

    /// <summary>Gets or sets the expression that starts it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Start Timer Expression")]
    public string StartExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that holds it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Pause Timer Expression")]
    public string PauseExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its time, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time In Seconds")]
    public float TimeInSeconds { get; set; }

    /// <summary>Gets its status.</summary>
    public Igi2MissionTimerStatus Status { get; private set; }

    /// <summary>Gets the ticks it has left, <c>nTicksLeft</c>.</summary>
    public int TicksLeft { get; private set; }

    /// <summary>Gets a value indicating whether its display is on the screen.</summary>
    public bool IsShown => Status == Igi2MissionTimerStatus.Running;

    /// <summary>Gets the text its display shows.</summary>
    public string DisplayText => string.Create(
        CultureInfo.InvariantCulture,
        $"{TicksLeft / 1800:00} : {TicksLeft % 1800 / 30:00}");

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        switch (Status)
        {
            case Igi2MissionTimerStatus.Waiting:
                _start ??= Igi2LevelExpressions.Create(StartExpression, Name, level.Natives);
                if (!_start.IsEmpty && level.Evaluate(_start) != 0.0)
                {
                    Status = Igi2MissionTimerStatus.Running;
                }

                break;

            case Igi2MissionTimerStatus.Running:
                _pause ??= Igi2LevelExpressions.Create(PauseExpression, Name, level.Natives);
                if (_pause.IsEmpty || level.Evaluate(_pause) == 0.0)
                {
                    if (TicksLeft != 0)
                    {
                        TicksLeft--;
                    }
                    else
                    {
                        Status = Igi2MissionTimerStatus.Finished;
                    }
                }

                break;
        }
    }

    /// <summary>Sets its ticks from its seconds, <c>MissionTimer_UpdateInternalDataHandler</c>.</summary>
    public void Prepare() => TicksLeft = Fpu.ToInt32((Fpu)TimeInSeconds * 30.0);

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();
}
