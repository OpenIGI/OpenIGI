using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// What an <c>AnimTask</c> sends its target at every step, <c>UpdateAnimDataContext_s</c>: the channels, and the tick they are for
/// (nought to set the target back to its start).
/// </summary>
/// <param name="Program">The programme, whose channels the target reads and may write back.</param>
/// <param name="Time">The tick, <c>nTime</c>.</param>
/// <param name="FlowTick">The level's tick, which the target moves in.</param>
public readonly record struct Igi2AnimUpdate(AnimDataProgram Program, int Time, int FlowTick);

/// <summary>Something an <c>AnimTask</c> drives, through its <c>AnimQTask_eUpdateAnimDataEvent</c> handler.</summary>
public interface IIgi2AnimTarget
{
    /// <summary>Takes a step of the programme.</summary>
    /// <param name="update">The step.</param>
    void UpdateAnimData(Igi2AnimUpdate update);
}

/// <summary>
/// A scripted animation, I.G.I. 2's <c>AnimTask</c> (animtask.c, <c>AnimQTask</c>): an <c>AnimData</c> programme of channel writes and
/// waits, stepped once a tick while it runs, each step sent to its target.
/// </summary>
/// <remarks>
/// <para>
/// <c>AnimQTask_UpdateInternalDataHandler</c>, as the level loads: the channels cleared, the tick nought, a step taken, so the target is
/// set back to its start. <c>AnimQTask_RunHandler</c>: its run expression, chopped to a byte, where it has one, says whether it runs;
/// running, it is taken to the tick after the one it is at (<c>AnimQTask_SetIdealTime</c>).
/// </para>
/// <para>
/// <c>AnimQTask_SetIdealTime</c>: forwards, a step and then a tick counted until it gets there; to nought or backwards, the programme
/// rewound and replayed a step more than the tick, the tick left at nought. <c>AnimQTask_RunCommand</c> steps the programme
/// (<see cref="AnimDataProgram.Step"/>) and sends its target the tick as it stands, finished or not (<c>AnimQTask_UpdateTarget</c>).
/// The scripts read and write <c>isRun</c> and <c>nTick</c>.
/// </para>
/// </remarks>
public sealed partial class Igi2AnimTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AnimTask";

    // Level_atLevelQTaskID's size: an id past it names nothing.
    private const int LevelIds = 0x1000;

    private AnimDataProgram? _program;
    private LevelExpression? _run;
    private byte _isRun = 1;
    private int _tick = -1;
    private int _flowTick;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the editor's turn of it.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the level id of the task it drives.</summary>
    [ScriptParam(ScriptParamType.Int32, "TargetID")]
    public int TargetId { get; set; } = -1;

    /// <summary>Gets or sets the expression that says whether it runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run")]
    public string RunExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it runs from the start.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Initial run")]
    public bool InitialRun
    {
        get => _isRun != 0;
        set => _isRun = value ? (byte)1 : (byte)0;
    }

    /// <summary>Gets or sets its programme.</summary>
    [ScriptParam(ScriptParamType.AnimData, "AnimData")]
    public AnimDataEntry[] AnimData { get; set; } = [];

    /// <summary>Gets a value indicating whether it runs, <c>isRun</c>.</summary>
    public bool IsRun => _isRun != 0;

    /// <summary>Gets the tick it is at, <c>nTick</c>; minus one before the level loads.</summary>
    public int Tick => _tick;

    /// <summary>Gets its programme, once the level has loaded.</summary>
    public AnimDataProgram? Program => _program;

    /// <summary>Gets the task it drives, once the level has loaded and when the id names one that can be.</summary>
    public IIgi2AnimTarget? Target { get; private set; }

    /// <summary>Sets it up as the level loads, <c>AnimQTask_UpdateInternalDataHandler</c>: the target found, and set back to its start.</summary>
    public void Begin()
    {
        Target = TargetId is >= 0 and < LevelIds ? Tree.FromId(TargetId) as IIgi2AnimTarget : null;
        _program = new AnimDataProgram(AnimData);
        _program.Rewind();
        _tick = 0;
        RunCommand();
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_program is null)
        {
            return;
        }

        _flowTick = level.LevelFlow?.Tick ?? 0;
        _run ??= Igi2LevelExpressions.Create(RunExpression, Name, level.Natives);
        if (!_run.IsEmpty)
        {
            _isRun = (byte)LevelExpression.Truncate(level.Evaluate(_run));
        }

        if (_isRun != 0)
        {
            SetIdealTime(_tick + 1);
        }
    }

    /// <summary>Takes it to a tick, <c>AnimQTask_SetIdealTime</c>.</summary>
    /// <param name="tick">The tick.</param>
    public void SetIdealTime(int tick)
    {
        if (_program is null)
        {
            return;
        }

        if (tick != 0 && tick >= _tick)
        {
            while (tick > _tick)
            {
                RunCommand();
                _tick++;
            }

            return;
        }

        _program.Rewind();
        _tick = 0;
        for (int i = 0; i <= tick; i++)
        {
            RunCommand();
        }
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", () => _isRun, value => _isRun = (byte)value, sizeof(byte));
        variables.BindInt(this, "nTick", () => _tick, value => _tick = value);
    }

    // AnimQTask_RunCommand, then AnimQTask_UpdateTarget.
    private void RunCommand()
    {
        _program!.Step();
        Target?.UpdateAnimData(new Igi2AnimUpdate(_program, _tick, _flowTick));
    }
}
